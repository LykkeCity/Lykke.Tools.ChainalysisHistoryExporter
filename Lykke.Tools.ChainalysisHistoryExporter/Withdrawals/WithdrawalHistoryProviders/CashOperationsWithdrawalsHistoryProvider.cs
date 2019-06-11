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
    public class CashOperationsWithdrawalsHistoryProvider : IWithdrawalsHistoryProvider
    {
        #region Entities 

        // ReSharper disable IdentifierTypo
        // ReSharper disable UnusedMember.Local
        // ReSharper disable UnusedAutoPropertyAccessor.Local

        private class CashOperationEntity : TableEntity
        {
            public DateTime DateTime { get; set; }
            public bool IsHidden { get; set; }
            public string AssetId { get; set; }
            public string ClientId { get; set; }
            public double Amount { get; set; }
            public string BlockChainHash { get; set; }
            public string Multisig { get; set; }
            public string TransactionId { get; set; }
            public string AddressFrom { get; set; }
            public string AddressTo { get; set; }
            public bool? IsSettled { get; set; }
            public string StateField { get; set; }
            public bool IsRefund { get; set; }
            public string TypeField { get; set; }
            public double FeeSize { get; set; }
            public string FeeTypeText { get; set; }
        }

        // ReSharper restore IdentifierTypo
        // ReSharper restore UnusedMember.Local
        // ReSharper restore UnusedAutoPropertyAccessor.Local

        #endregion

        private readonly BlockchainsProvider _blockchainsProvider;
        private readonly CloudTable _table;

        public CashOperationsWithdrawalsHistoryProvider(
            BlockchainsProvider blockchainsProvider,
            IOptions<AzureStorageSettings> azureStorageSettings)
        {
            _blockchainsProvider = blockchainsProvider;

            var azureAccount = CloudStorageAccount.Parse(azureStorageSettings.Value.CashOperationsConnString);
            var azureClient = azureAccount.CreateCloudTableClient();

            _table = azureClient.GetTableReference("OperationsCash");
        }

        public async Task<PaginatedList<Transaction>> GetHistoryAsync(string continuation)
        {
            var continuationToken = continuation != null
                ? JsonConvert.DeserializeObject<TableContinuationToken>(continuation)
                : null;
            var query = new TableQuery<CashOperationEntity>
            {
                TakeCount = 1000
            };
            var response = await _table.ExecuteQuerySegmentedAsync(query, continuationToken);

            var transactions = response.Results
                .Where(operation => 
                    operation.Amount < 0 && 
                    !string.IsNullOrWhiteSpace(operation.AddressTo) && 
                    !string.IsNullOrWhiteSpace(operation.BlockChainHash) &&
                    operation.AddressTo != operation.AddressFrom &&
                    !string.IsNullOrWhiteSpace(operation.AssetId) &&
                    !string.IsNullOrWhiteSpace(operation.ClientId))
                .Select(operation =>
                {
                    var blockchain = _blockchainsProvider.GetByAssetIdOrDefault(operation.AssetId);

                    if (blockchain == null)
                    {
                        return null;
                    }

                    return new Transaction
                    (
                        blockchain.CryptoCurrency,
                        operation.BlockChainHash,
                        new Guid(operation.ClientId),
                        operation.AddressTo,
                        TransactionType.Withdrawal
                    );
                })
                .Where(x => x != null)
                .ToArray();

            return PaginatedList.From(response.ContinuationToken, transactions);
        }
    }
}
