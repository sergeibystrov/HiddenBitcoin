﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HiddenBitcoin.DataClasses.KeyManagement;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using NBitcoin.SPV;

namespace HiddenBitcoin.DataClasses.Monitoring
{
    public class SpvSafeMonitor
    {
        private const string TrackerFilePath = "Tracker.dat";
        private const string ChainFilePath = "Chain.dat";
        private const string AddressManagerFilePath = "AddressManager.dat";

        public readonly Safe Safe;
        private NodeConnectionParameters _connectionParameters;
        private bool _disposed;
        internal NodesGroup Group;

        public SpvSafeMonitor(Safe safe)
        {
            Safe = safe;
            
            PeriodicProgressPercentAdjust();
        }

        public int SyncProgressPercent { get; private set; }
        public int ConnectionProgressPercent { get; private set; }
        public bool Connected => ConnectionProgressPercent == 100;
        public bool Synced => SyncProgressPercent == 100;

        // ReSharper disable once InconsistentNaming
        private NBitcoin.Network _Network => Convert.ToNBitcoinNetwork(Safe.Network);
        public Network Network => Safe.Network;

        public BalanceInfo GetBalance(string address)
        {
            if(!Connected)
                throw new Exception("NotConnectedToNodes");
            if(!Synced)
                throw new Exception("NotSynced");
            


            //var confirmedBalance = balanceSummary.Confirmed.Amount.ToDecimal(MoneyUnit.BTC);
            //var unconfirmedBalance = balanceSummary.UnConfirmed.Amount.ToDecimal(MoneyUnit.BTC);

            return new BalanceInfo(address, unconfirmedBalance, confirmedBalance);
        }

        public async void StartConnecting()
        {
            await Task.Factory.StartNew(() =>
            {
                var parameters = new NodeConnectionParameters();
                //So we find nodes faster
                parameters.TemplateBehaviors.Add(new AddressManagerBehavior(GetAddressManager()));
                //So we don't have to load the chain each time we start
                parameters.TemplateBehaviors.Add(new ChainBehavior(GetChain()));
                //Tracker knows which scriptPubKey and outpoints to track, it monitors all your wallets at the same
                parameters.TemplateBehaviors.Add(new TrackerBehavior(GetTracker()));
                if (_disposed) return;
                Group = new NodesGroup(_Network, parameters, new NodeRequirement
                {
                    RequiredServices = NodeServices.Network //Needed for SPV
                })
                {MaximumNodeConnection = 3};
                Group.Connect();
                _connectionParameters = parameters;
            });

            PeriodicSave();
            PeriodicKick();
        }

        public void Disconnect()
        {
            _disposed = true;
            SaveAsync();
            Group?.Disconnect();
        }

        private AddressManager GetAddressManager()
        {
            if (_connectionParameters != null)
            {
                return _connectionParameters.TemplateBehaviors.Find<AddressManagerBehavior>().AddressManager;
            }
            try
            {
                return AddressManager.LoadPeerFile(AddressManagerFilePath);
            }
            catch
            {
                return new AddressManager();
            }
        }

        private ConcurrentChain GetChain()
        {
            if (_connectionParameters != null)
            {
                return _connectionParameters.TemplateBehaviors.Find<ChainBehavior>().Chain;
            }
            var chain = new ConcurrentChain(_Network);
            try
            {
                chain.Load(File.ReadAllBytes(ChainFilePath));
            }
            catch
            {
                // ignored
            }
            return chain;
        }

        private Tracker GetTracker()
        {
            if (_connectionParameters != null)
            {
                return _connectionParameters.TemplateBehaviors.Find<TrackerBehavior>().Tracker;
            }
            try
            {
                using (var fs = File.OpenRead(TrackerFilePath))
                {
                    return Tracker.Load(fs);
                }
            }
            catch
            {
                // ignored
            }
            return new Tracker();
        }

        private async void PeriodicSave()
        {
            while (!_disposed)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                SaveAsync();
            }
        }

        private void SaveAsync()
        {
            Task.Factory.StartNew(() =>
            {
                GetAddressManager().SavePeerFile(AddressManagerFilePath, _Network);
                using (var fs = File.Open(ChainFilePath, FileMode.Create))
                {
                    GetChain().WriteTo(fs);
                }
                using (var fs = File.Open(TrackerFilePath, FileMode.Create))
                {
                    GetTracker().Save(fs);
                }
            });
        }

        private async void PeriodicKick()
        {
            while (!_disposed)
            {
                await Task.Delay(TimeSpan.FromMinutes(7));
                Group.Purge("For privacy concerns, will renew bloom filters on fresh nodes");
            }
        }

        private async void PeriodicProgressPercentAdjust()
        {
            while (!_disposed)
            {
                await Task.Delay(500);

                if (Group == null)
                    ConnectionProgressPercent = 0;
                else if (Group.ConnectedNodes == null)
                    ConnectionProgressPercent = 0;
                else
                {
                    var nodeCount = Group.ConnectedNodes.Count;
                    var maxNode = Group.MaximumNodeConnection;
                    ConnectionProgressPercent = (int) Math.Round((double) (100*nodeCount)/maxNode);
                }

                if (Group == null)
                    SyncProgressPercent = 0;
                else if (Group.ConnectedNodes == null)
                    SyncProgressPercent = 0;
                else if (Group.ConnectedNodes.Count == 0)
                    SyncProgressPercent = 0;
                else if (Group.ConnectedNodes.First().PeerVersion == null)
                    SyncProgressPercent = 0;
                else
                {
                    var localHeight = GetChain().Height;
                    var startHeight = Group.ConnectedNodes.First().PeerVersion.StartHeight;
                        // Can't find how to get the blockchain height, but it'll do it for this case
                    SyncProgressPercent = (int) Math.Round((double) (100*localHeight)/startHeight);
                }

                if (ConnectionProgressPercent > 100) ConnectionProgressPercent = 100;
                if (SyncProgressPercent > 100) SyncProgressPercent = 100;
            }
        }
    }
}