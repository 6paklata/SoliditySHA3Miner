﻿using System;
using System.Linq;
using System.Text;
using System.Timers;
using System.Threading.Tasks;
using CudaSolver;
using Nethereum.Hex.HexTypes;

namespace SoliditySHA3Miner.Miner
{
    public class CUDA : IMiner
    {
        #region Static

        public static int GetDeviceCount(out string errorMessage)
        {
            errorMessage = string.Empty;
            return Solver.getDeviceCount(ref errorMessage);
        }

        public static string GetDeviceName(int deviceID, out string errorMessage)
        {
            errorMessage = string.Empty;
            return Solver.getDeviceName(deviceID, ref errorMessage);
        }

        public static string GetDevices(out string errorMessage)
        {
            errorMessage = string.Empty;
            var devicesString = new StringBuilder();
            var cudaCount = Solver.getDeviceCount(ref errorMessage);

            if (!string.IsNullOrEmpty(errorMessage)) return string.Empty;

            for (int i = 0; i < cudaCount; i++)
            {
                var deviceName = Solver.getDeviceName(i, ref errorMessage);
                if (!string.IsNullOrEmpty(errorMessage)) return string.Empty;

                devicesString.AppendLine(string.Format("{0}: {1}", i, deviceName));
            }
            return devicesString.ToString();
        }

        #endregion

        private Timer m_hashPrintTimer;
        private Timer m_updateMinerTimer;
        private int m_pauseOnFailedScan;
        private int m_failedScanCount;

        public Solver Solver { get; }

        #region IMiner

        public NetworkInterface.INetworkInterface NetworkInterface { get; }
        public Device[] Devices { get; }
        public bool HasAssignedDevices => (bool)Solver?.isAssigned();
        public bool IsAnyInitialised => (bool)Solver?.isAnyInitialised();
        public bool IsMining => (bool)Solver?.isMining();

        private NetworkInterface.MiningParameters lastMiningParameters;

        public void StartMining(int networkUpdateInterval, int hashratePrintInterval)
        {
            try
            {
                if (NetworkInterface.IsPool) Program.Print("[INFO] Waiting for pool to respond...");
                else Program.Print("[INFO] Waiting for network to respond...");
                UpdateMiner(Solver).Wait();

                m_updateMinerTimer = new Timer(networkUpdateInterval);
                m_updateMinerTimer.Elapsed += m_updateMinerTimer_Elapsed;
                m_updateMinerTimer.Start();

                m_hashPrintTimer = new Timer(hashratePrintInterval);
                m_hashPrintTimer.Elapsed += m_hashPrintTimer_Elapsed;
                m_hashPrintTimer.Start();

                Solver.startFinding();
            }
            catch (Exception ex)
            {
                Program.Print(string.Format("[ERROR] {0}", ex.Message));
                StopMining();
            }
        }

        public void StopMining()
        {
            try
            {
                m_updateMinerTimer.Stop();
                Solver.stopFinding();
            }
            catch (Exception ex)
            {
                Program.Print(string.Format("[ERROR] {0}", ex.Message));
            }
        }

        public ulong GetTotalHashrate()
        {
            return Solver.getTotalHashRate();
        }

        public ulong GetHashrateByDevice(string platformName, int deviceID)
        {
            return Solver.getHashRateByDeviceID(deviceID);
        }

        public long GetDifficulty()
        {
            try { return (long)lastMiningParameters.MiningDifficulty.Value; }
            catch (OverflowException) { return long.MaxValue; }
        }

        public void Dispose()
        {
            try
            {
                Solver.Dispose();
                m_updateMinerTimer.Dispose();
            }
            catch (Exception ex)
            {
                Program.Print(string.Format("[ERROR] {0}", ex.Message));
            }
        }

        #endregion

        public CUDA(NetworkInterface.INetworkInterface networkInterface, Device[] cudaDevices, HexBigInteger maxDifficulty, uint customDifficulty, bool isSubmitStale, int pauseOnFailedScans)
        {
            try
            {
                Devices = cudaDevices;
                NetworkInterface = networkInterface;
                m_pauseOnFailedScan = pauseOnFailedScans;
                m_failedScanCount = 0;

                Solver = new Solver(maxDifficulty.HexValue)
                {
                    OnMessageHandler = m_cudaSolver_OnMessage,
                    OnSolutionHandler = m_cudaSolver_OnSolution
                };
                if (customDifficulty > 0u) Solver.setCustomDifficulty(customDifficulty);
                Solver.setSubmitStale(isSubmitStale);

                if (cudaDevices.All(d => d.DeviceID == -1))
                {
                    Program.Print("[INFO] CUDA device not set.");
                    return;
                }

                for (int i = 0; i < Devices.Length; i++)
                    if (Devices[i].DeviceID > -1)
                        Solver.assignDevice(Devices[i].DeviceID, Devices[i].Intensity);
            }
            catch (Exception ex)
            {
                Program.Print(string.Format("[ERROR] {0}", ex.Message));
            }
        }

        private async Task UpdateMiner(Solver solver)
        {
            await Task.Factory.StartNew(() =>
            {
                lock (NetworkInterface)
                {
                    try
                    {
                        var miningParameters = NetworkInterface.GetMiningParameters();
                        if (miningParameters == null) return;

                        lastMiningParameters = miningParameters;

                        solver.updatePrefix(lastMiningParameters.ChallengeNumberByte32String + lastMiningParameters.EthAddress.Replace("0x", string.Empty));
                        solver.updateTarget(lastMiningParameters.MiningTargetByte32String);
                        solver.updateDifficulty(lastMiningParameters.MiningDifficulty.HexValue);

                        if (m_failedScanCount > m_pauseOnFailedScan && !Solver.isMining())
                        {
                            m_failedScanCount = 0;
                            Solver.startFinding();
                        }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            Program.Print(string.Format("[ERROR] {0}", ex.Message));

                            m_failedScanCount += 1;
                            if (m_failedScanCount > m_pauseOnFailedScan && Solver.isMining()) Solver.stopFinding();
                        }
                        catch (Exception) { }
                    }
                }
            });
        }

        private void m_hashPrintTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var hashString = new StringBuilder();
            hashString.Append("CUDA [INFO] Hashrates:");

            foreach (var device in Devices)
                if (device.DeviceID > -1)
                    hashString.AppendFormat(" {0} MH/s", Solver.getHashRateByDeviceID(device.DeviceID) / 1000000.0f);

            Program.Print(hashString.ToString());
            Program.Print(string.Format("CUDA [INFO] Total Hashrate: {0} MH/s", Solver.getTotalHashRate() / 1000000.0f));
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
        }

        private void m_updateMinerTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var updateTask = UpdateMiner(Solver);
        }

        private void m_cudaSolver_OnSolution(string digest, string address, string challenge, string difficulty, string target, string solution, bool isCustomDifficulty)
        {
            var updateTask = UpdateMiner(Solver);
            NetworkInterface.SubmitSolution(digest, address, challenge, difficulty, target, solution, isCustomDifficulty);
        }

        private void m_cudaSolver_OnMessage(int deviceID, string type, string message)
        {
            var sFormat = new StringBuilder();
            if (deviceID > -1) sFormat.Append("CUDA ID: {0} ");
            
            switch (type.ToUpperInvariant())
            {
                case "INFO":
                    sFormat.Append(deviceID > -1 ? "[INFO] {1}" : "[INFO] {0}");
                    break;
                case "WARN":
                    sFormat.Append(deviceID > -1 ? "[WARN] {1}" : "[WARN] {0}");
                    break;
                case "ERROR":
                    sFormat.Append(deviceID > -1 ? "[ERROR] {1}" : "[ERROR] {0}");
                    break;
                case "DEBUG":
                default:
#if DEBUG
                    sFormat.Append(deviceID > -1 ? "[DEBUG] {1}" : "[DEBUG] {0}");
                    break;
#else
                    return;
#endif
            }
            Program.Print(deviceID > -1
                ? string.Format(sFormat.ToString(), deviceID, message)
                : string.Format(sFormat.ToString(), message));
        }
    }
}