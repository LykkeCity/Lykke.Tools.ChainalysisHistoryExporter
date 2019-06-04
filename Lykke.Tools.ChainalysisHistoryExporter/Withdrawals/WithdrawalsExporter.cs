﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lykke.Tools.ChainalysisHistoryExporter.Common;
using Lykke.Tools.ChainalysisHistoryExporter.Reporting;
using Microsoft.Extensions.Logging;
using Polly;

namespace Lykke.Tools.ChainalysisHistoryExporter.Withdrawals
{
    internal class WithdrawalsExporter
    {
        private readonly ILogger<WithdrawalsExporter> _logger;
        private readonly Report _report;
        private readonly IEnumerable<IWithdrawalsHistoryProvider> _withdrawalsHistoryProviders;
        private int _exportedWithdrawalsCount;

        public WithdrawalsExporter(
            ILogger<WithdrawalsExporter> logger,
            Report report,
            IEnumerable<IWithdrawalsHistoryProvider> withdrawalsHistoryProviders)
        {
            _logger = logger;
            _report = report;
            _withdrawalsHistoryProviders = withdrawalsHistoryProviders;
        }

        public async Task ExportAsync()
        {
            _logger.LogInformation("Exporting withdrawals...");

            var tasks = new List<Task>();

            foreach (var historyProvider in _withdrawalsHistoryProviders)
            {
                tasks.Add(ExportProviderWithdrawals(historyProvider));
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation($"Withdrawals exporting done. {_exportedWithdrawalsCount} withdrawals exported");
        }

        private async Task ExportProviderWithdrawals(IWithdrawalsHistoryProvider historyProvider)
        {
            PaginatedList<Transaction> transactions = null;

            do
            {
                transactions = await Policy
                    .Handle<Exception>(ex =>
                    {
                        _logger.LogWarning(ex,
                            $"Failed to get withdrawals history using {historyProvider.GetType().Name}. Operation will be retried.");
                        return true;
                    })
                    .WaitAndRetryForeverAsync(i => TimeSpan.FromSeconds(Math.Min(i, 5)))
                    .ExecuteAsync(async () => await historyProvider.GetHistoryAsync(transactions?.Continuation));

                foreach (var tx in transactions.Items)
                {
                    _report.AddTransaction(tx);

                    var exportedWithdrawalsCount = Interlocked.Increment(ref _exportedWithdrawalsCount);

                    if (exportedWithdrawalsCount % 1000 == 0)
                    {
                        _logger.LogInformation($"{exportedWithdrawalsCount} withdrawals exported so far");
                    }
                }
            } while (transactions.Continuation != null);
        }
    }
}
