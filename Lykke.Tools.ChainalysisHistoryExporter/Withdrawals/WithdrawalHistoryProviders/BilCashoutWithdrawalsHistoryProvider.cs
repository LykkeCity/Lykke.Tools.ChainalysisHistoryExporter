﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Lykke.Tools.ChainalysisHistoryExporter.Common;
using Lykke.Tools.ChainalysisHistoryExporter.Configuration;
using Lykke.Tools.ChainalysisHistoryExporter.Reporting;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Lykke.Tools.ChainalysisHistoryExporter.Withdrawals.WithdrawalHistoryProviders
{
    internal class BilCashoutWithdrawalsHistoryProvider : IWithdrawalsHistoryProvider
    {
        #region Entities

        // ReSharper disable UnusedMember.Local
        // ReSharper disable UnusedAutoPropertyAccessor.Local
        // ReSharper disable ClassNeverInstantiated.Local

        private static class CashoutResult
        {
            public const string Unknown = "Unknown";
            public const string Success = "Success";
            public const string Failure = "Failure";
        }

        private class CashoutEntity : TableEntity
        {
            public string State { get; set; }
            public string Result { get; set; }
            public Guid ClientId { get; set; }
            public string BlockchainType { get; set; }
            public string ToAddress { get; set; }
            public string TransactionHash { get; set; }
        }

        // ReSharper restore UnusedMember.Local
        // ReSharper restore UnusedAutoPropertyAccessor.Local
        // ReSharper restore ClassNeverInstantiated.Local

        #endregion

        private readonly BlockchainsProvider _blockchainsProvider;
        private readonly CloudTable _table;
        

        public BilCashoutWithdrawalsHistoryProvider(
            BlockchainsProvider blockchainsProvider,
            IOptions<AzureStorageSettings> azureStorageSettings)
        {
            _blockchainsProvider = blockchainsProvider;

            var azureAccount = CloudStorageAccount.Parse(azureStorageSettings.Value.CashoutProcessorConnString);
            var azureClient = azureAccount.CreateCloudTableClient();

            _table = azureClient.GetTableReference("Cashout");
        }

        public async Task<PaginatedList<Transaction>> GetHistoryAsync(string continuation)
        {
            var continuationToken = continuation != null
                ? JsonConvert.DeserializeObject<TableContinuationToken>(continuation)
                : null;
            var query = new TableQuery<CashoutEntity>
            {
                TakeCount = 1000
            };
            var response = await _table.ExecuteQuerySegmentedAsync(query, continuationToken);

            var transactions = response.Results
                .Where(cashout => cashout.Result == CashoutResult.Success && cashout.TransactionHash != null)
                .Select(cashout =>
                {
                    var blockchain = _blockchainsProvider.GetByBilIdOrDefault(cashout.BlockchainType);

                    if (blockchain == null)
                    {
                        return null;
                    }

                    return new Transaction
                    {
                        CryptoCurrency = blockchain.CryptoCurrency,
                        Hash = cashout.TransactionHash,
                        UserId = cashout.ClientId,
                        OutputAddress = cashout.ToAddress,
                        Type = TransactionType.Withdrawal
                    };
                })
                .Where(x => x != null)
                .ToArray();

            return PaginatedList.From(response.ContinuationToken, transactions);
        }
    }
}
