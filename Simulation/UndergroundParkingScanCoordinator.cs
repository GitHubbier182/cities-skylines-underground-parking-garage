using System;
using ScratchyBald.CitiesSkylines.Shared;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingScanCoordinator
    {
        private const string OwnerId = "UndergroundParkingGarage";
        private const string WarmupRequestId = "parked-vehicle-cache-warmup";

        private static bool _available;
        private static bool _registered;
        private static bool _failureLogged;
        private static string _warmupTicket;

        public static void Initialize()
        {
            Shutdown();
            _failureLogged = false;
            try
            {
                ScratchysScanManager.Initialize(
                    OwnerId,
                    delegate
                    {
                        return UndergroundParkingGarageSettings.AdvancedDiagnostics;
                    });
                _registered = true;
                _available = true;
                UndergroundParkingLog.Info(
                    "Scratchy's Scan Manager registered; parked-vehicle cache warm-up will use cooperative simulation-thread startup steps.");
            }
            catch (Exception exception)
            {
                LogFallback("initialization failed", exception);
            }
        }

        public static bool TryQueueParkedVehicleWarmup(Func<bool> step)
        {
            if (!_available || step == null)
                return false;

            CancelWarmup();
            try
            {
                _warmupTicket = ScratchysScanManager.QueueSimulationThreadScan(
                    OwnerId,
                    WarmupRequestId,
                    ScratchysScanManager.StartupPriority,
                    step,
                    delegate
                    {
                        _warmupTicket = null;
                        UndergroundParkingOccupancyManager.CompleteManagedWarmupScan();
                    },
                    delegate(Exception exception)
                    {
                        _warmupTicket = null;
                        UndergroundParkingOccupancyManager.UseLocalWarmupFallback();
                        LogFallback("parked-vehicle warm-up failed", exception);
                    });
                return !string.IsNullOrEmpty(_warmupTicket);
            }
            catch (Exception exception)
            {
                _available = false;
                UndergroundParkingOccupancyManager.UseLocalWarmupFallback();
                LogFallback("parked-vehicle warm-up submission failed", exception);
                return false;
            }
        }

        public static void PumpSimulationThread()
        {
            if (!_available)
                return;

            try
            {
                ScratchysScanManager.PumpSimulationThread();
            }
            catch (Exception exception)
            {
                _available = false;
                _warmupTicket = null;
                UndergroundParkingOccupancyManager.UseLocalWarmupFallback();
                LogFallback("simulation-thread pump failed", exception);
            }
        }

        public static void CancelWarmup()
        {
            if (string.IsNullOrEmpty(_warmupTicket))
                return;

            try
            {
                ScratchysScanManager.Cancel(_warmupTicket);
            }
            catch (Exception exception)
            {
                LogFallback("parked-vehicle warm-up cancellation failed", exception);
            }
            _warmupTicket = null;
        }

        public static void Shutdown()
        {
            if (_registered)
            {
                try
                {
                    ScratchysScanManager.CancelOwner(OwnerId);
                }
                catch (Exception exception)
                {
                    LogFallback("level-unload cancellation failed", exception);
                }
            }

            _warmupTicket = null;
            _available = false;
            _registered = false;
        }

        private static void LogFallback(string operation, Exception exception)
        {
            if (_failureLogged)
                return;

            _failureLogged = true;
            UndergroundParkingLog.Warning(
                "Scratchy's Scan Manager "
                + operation
                + "; UPG will preserve its existing locally paced warm-up. exception="
                + exception);
        }
    }
}
