﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lykke.Tools.ChainalysisHistoryExporter.Common;
using Lykke.Tools.ChainalysisHistoryExporter.Configuration;
using Lykke.Tools.ChainalysisHistoryExporter.Reporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using QBitNinja.Client.Models;
using Transaction = Lykke.Tools.ChainalysisHistoryExporter.Reporting.Transaction;

namespace Lykke.Tools.ChainalysisHistoryExporter.Deposits.DepositHistoryProviders.Bitcoin
{
    internal class BtcDepositsHistoryProvider : IDepositsHistoryProvider
    {
        private readonly ILogger<BtcDepositsHistoryProvider> _logger;
        private readonly CustomQBitNinjaClient _client;
        private readonly Blockchain _bitcoin;

        public BtcDepositsHistoryProvider(
            ILogger<BtcDepositsHistoryProvider> logger,
            BlockchainsProvider blockchainsProvider,
            IOptions<BtcSettings> settings)
        {
            _logger = logger;
            _bitcoin = blockchainsProvider.GetBitcoin();
            _client = new CustomQBitNinjaClient(new Uri(settings.Value.NinjaUrl), Network.GetNetwork(settings.Value.Network));
        }

        public bool CanProvideHistoryFor(DepositWallet depositWallet)
        {
            return depositWallet.CryptoCurrency == _bitcoin.CryptoCurrency;
        }

        public async Task<PaginatedList<Transaction>> GetHistoryAsync(DepositWallet depositWallet, string continuation)
        {
            if (!CanProvideHistoryFor(depositWallet))
            {
                return PaginatedList.From(Array.Empty<Transaction>());
            }

            var btcAddress = GetAddressOrDefault(depositWallet.Address);
            if (btcAddress == null)
            {
                _logger.LogWarning($"Address {depositWallet.Address} is not valid Bitcoin address, skipping");

                return PaginatedList.From(Array.Empty<Transaction>());
            }

            var response = await _client.GetBalance(btcAddress, false, continuation);
            var depositOperations = response.Operations.Where(IsDeposit);
            var depositTransactions = Map(depositOperations, depositWallet.Address, depositWallet.UserId);

            return PaginatedList.From(response.Continuation, depositTransactions.ToArray());
        }

        private IEnumerable<Transaction> Map(IEnumerable<BalanceOperation> source, 
            string outputAddress,
            Guid userId)
        {
            return source.Select(balanceOperation => new Transaction
            (
                _bitcoin.CryptoCurrency,
                balanceOperation.TransactionId.ToString(),
                userId,
                outputAddress,
                TransactionType.Deposit
            ));
        }

        private static bool IsDeposit(BalanceOperation source)
        {
            return !source.SpentCoins.Any();
        }

        private BitcoinAddress GetAddressOrDefault(string address)
        {

            if (IsUncoloredBtcAddress(address))
            {
                return BitcoinAddress.Create(address, _client.Network);
            }

            if (IsColoredBtcAddress(address))
            {
                return new BitcoinColoredAddress(address, _client.Network).Address;
            }

            return null;
        }

        private bool IsUncoloredBtcAddress(string address)
        {
            try
            {
                BitcoinAddress.Create(address, _client.Network);

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private bool IsColoredBtcAddress(string address)
        {
            try
            {
                // ReSharper disable once ObjectCreationAsStatement
                new BitcoinColoredAddress(address, _client.Network);

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
