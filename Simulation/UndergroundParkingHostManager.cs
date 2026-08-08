using ColossalFramework;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingHostManager
    {
        private static string _lastStatus = string.Empty;
        private static ushort _statusBuildingId;

        public static string GetStatus(ushort buildingId)
        {
            return buildingId != 0 && buildingId == _statusBuildingId ? _lastStatus : string.Empty;
        }

        public static void ClearStatus(ushort buildingId)
        {
            if (buildingId == 0 || buildingId != _statusBuildingId)
                return;

            _statusBuildingId = 0;
            _lastStatus = string.Empty;
        }

        public static void ReportStatus(ushort buildingId, string status)
        {
            if (buildingId == 0)
                return;

            _statusBuildingId = buildingId;
            _lastStatus = status ?? string.Empty;
        }

        public static void RequestMoveEntrance(ushort buildingId)
        {
            if (buildingId == 0)
                return;

            string status;
            UndergroundParkingPlacementTool.ActivateEntranceRelocation(buildingId, out status);
            ReportStatus(buildingId, status);
        }

        public static void RequestSetOpen(ushort buildingId, bool open)
        {
            if (buildingId == 0 || SimulationManager.instance == null)
                return;

            _statusBuildingId = buildingId;
            _lastStatus = open ? "Turning on car park..." : "Turning off car park...";
            SimulationManager.instance.AddAction(delegate
            {
                string status;
                UndergroundParkingRegistry.TrySetTargetFacilityOpen(buildingId, open, out status);
                _lastStatus = status;
            });
        }

        public static void RequestSetEntranceVisuals(ushort buildingId, bool enabled)
        {
            if (buildingId == 0 || SimulationManager.instance == null)
                return;

            _statusBuildingId = buildingId;
            _lastStatus = enabled ? "Showing UPG entrance visuals..." : "Hiding UPG entrance visuals...";
            SimulationManager.instance.AddAction(delegate
            {
                string status;
                UndergroundParkingRegistry.TrySetTargetEntranceVisuals(buildingId, enabled, out status);
                _lastStatus = status;
            });
        }

        public static void RequestSetAllEntranceVisuals(bool enabled)
        {
            if (SimulationManager.instance == null)
                return;

            SimulationManager.instance.AddAction(delegate
            {
                UndergroundParkingRegistry.SetAllBuildingAttachedEntranceVisuals(enabled);
            });
        }

        public static void RequestDelete(ushort buildingId)
        {
            if (buildingId == 0 || SimulationManager.instance == null)
                return;

            _statusBuildingId = buildingId;
            _lastStatus = "Checking underground car park...";
            SimulationManager.instance.AddAction(delegate
            {
                string status;
                UndergroundParkingRegistry.TryRemoveForTargetBuilding(buildingId, out status);
                _lastStatus = status;
            });
        }
    }
}
