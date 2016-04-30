﻿using System;
using System.IO;
using HiddenBitcoin.DataClasses;
using HiddenBitcoin.Helpers;
using NBitcoin;

namespace HiddenBitcoin
{
    public class Safe
    {
        private Mnemonic _mnemonic;
        private NBitcoin.Network _network;

        private Safe(string password, string walletFilePath, DataClasses.Network network, string mnemonicString = null)
        {
            SetNetwork(network);

            if (mnemonicString == null)
                _mnemonic = SetSeed(password);
            else
            {
                SetSeed(password, mnemonicString);
            }

            WalletFilePath = walletFilePath;
        }

        public ExtKey SeedExtKey { get; private set; }
        public string Seed => SeedExtKey.GetWif(_network).ToWif();
        public string SeedPublicKey => SeedExtKey.Neuter().GetWif(_network).ToWif();
        public string Mnemonic => _mnemonic.ToString();
        public string WalletFilePath { get; private set; }

        public DataClasses.Network Network
        {
            get
            {
                if (_network == NBitcoin.Network.Main)
                    return DataClasses.Network.MainNet;
                if (_network == NBitcoin.Network.TestNet)
                    return DataClasses.Network.TestNet;
                throw new InvalidOperationException("WrongNetwork");
            }
        }

        private static void Save(string mnemonic, string password, string walletFilePath, DataClasses.Network network)
        {
            if (File.Exists(walletFilePath))
                throw new Exception("WalletFileAlreadyExists");

            var directoryPath = Path.GetDirectoryName(Path.GetFullPath(walletFilePath));
            if (directoryPath != null) Directory.CreateDirectory(directoryPath);

            var encryptedMnemonic = StringCipher.Encrypt(mnemonic, password);
            var networkString = network.ToString();

            WalletFileSerializer.Serialize(walletFilePath, encryptedMnemonic, networkString);
        }

        public static Safe Load(string password, string walletFilePath)
        {
            if (!File.Exists(walletFilePath))
                throw new Exception("WalletFileDoesNotExists");

            var walletFileRawContent = WalletFileSerializer.Deserialize(walletFilePath);

            var encryptedMnemonic = walletFileRawContent.Seed;
            var mnemonic = StringCipher.Decrypt(encryptedMnemonic, password);

            DataClasses.Network network;
            var networkString = walletFileRawContent.Network;
            if (networkString == DataClasses.Network.MainNet.ToString())
                network = DataClasses.Network.MainNet;
            else if (networkString == DataClasses.Network.TestNet.ToString())
                network = DataClasses.Network.TestNet;
            else throw new Exception("NotRecognizedNetworkInWalletFile");

            return new Safe(password, walletFilePath, network, mnemonic);
        }

        /// <summary>
        ///     Creates a mnemonic, encrypts it with the password, and stores it in the path.
        /// </summary>
        /// <param name="password"></param>
        /// <param name="walletFilePath"></param>
        /// <param name="network"></param>
        /// <returns></returns>
        public static Safe Create(string password, string walletFilePath,
            DataClasses.Network network = DataClasses.Network.MainNet)
        {
            var safe = new Safe(password, walletFilePath, network);
            Save(safe.Mnemonic, password, walletFilePath, network);
            return safe;
        }

        public static Safe Recover(string mnemonic, string password, string walletFilePath, DataClasses.Network network)
        {
            var safe = new Safe(password, walletFilePath, network, mnemonic);
            Save(mnemonic, password, walletFilePath, network);
            return safe;
        }

        private Mnemonic SetSeed(string password, string mnemonicString = null)
        {
            var mnemonic =
                mnemonicString == null
                    ? new Mnemonic(Wordlist.English, WordCount.Twelve)
                    : new Mnemonic(mnemonicString);

            _mnemonic = mnemonic;

            SeedExtKey = mnemonic.DeriveExtKey(password);

            return mnemonic;
        }

        private void SetNetwork(DataClasses.Network network)
        {
            if (network == DataClasses.Network.MainNet)
                _network = NBitcoin.Network.Main;
            else if (network == DataClasses.Network.TestNet)
                _network = NBitcoin.Network.TestNet;
            else throw new Exception("WrongNetwork");
        }
    }
}