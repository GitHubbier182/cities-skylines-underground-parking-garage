using ColossalFramework;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingFloorManager
    {
        private const int AddedFloorDisplayCost = 25000;
        private static string _lastStatus = string.Empty;

        public static string LastStatus { get { return _lastStatus; } }

        public static void RequestFloorChange(ushort buildingId, int delta)
        {
            if (buildingId == 0 || delta == 0 || SimulationManager.instance == null)
                return;

            _lastStatus = "Updating underground floors...";
            UndergroundParkingLog.Advanced("UPG floor change requested: building="
                                        + buildingId
                                        + " delta="
                                        + delta);
            SimulationManager.instance.AddAction(delegate { ApplyFloorChange(buildingId, delta); });
        }

        public static bool IsEntranceBuildingOpen(ushort buildingId)
        {
            BuildingManager buildings = BuildingManager.instance;
            return buildingId != 0
                   && buildings != null
                   && buildingId < buildings.m_buildings.m_size
                   && (buildings.m_buildings.m_buffer[buildingId].m_flags & Building.Flags.Created) != 0
                   && buildings.m_buildings.m_buffer[buildingId].m_productionRate != 0;
        }

        public static void RequestSetEntranceBuildingOpen(ushort buildingId, bool open)
        {
            if (buildingId == 0 || SimulationManager.instance == null)
                return;

            _lastStatus = open ? "Opening car park..." : "Closing car park...";
            SimulationManager.instance.AddAction(delegate { ApplyEntranceBuildingOpen(buildingId, open); });
        }

        private static void ApplyEntranceBuildingOpen(ushort buildingId, bool open)
        {
            UndergroundParkingFacility facility;
            BuildingManager buildings = BuildingManager.instance;
            if (!UndergroundParkingRegistry.TryGetForBuilding(buildingId, out facility)
                || buildings == null
                || buildingId >= buildings.m_buildings.m_size)
            {
                _lastStatus = "The selected entrance is no longer available.";
                return;
            }

            Building building = buildings.m_buildings.m_buffer[buildingId];
            BuildingInfo info = building.Info;
            if ((building.m_flags & Building.Flags.Created) == 0
                || (building.m_flags & Building.Flags.Deleted) != 0
                || info == null
                || info.m_buildingAI == null)
            {
                _lastStatus = "The selected entrance is no longer available.";
                return;
            }

            // Match the vanilla building on/off control. SetProductionRate owns
            // the Active flag transition and all normal building-side effects;
            // the parking arrival gate already observes that authoritative state.
            info.m_buildingAI.SetProductionRate(
                buildingId,
                ref buildings.m_buildings.m_buffer[buildingId],
                open ? (byte)100 : (byte)0);
            _lastStatus = open
                ? "Car park opened and accepting arrivals."
                : "Car park closed. Existing vehicles may still leave.";
            UndergroundParkingLog.Advanced("Standalone car park building state changed: facility="
                                        + facility.Id
                                        + " building="
                                        + buildingId
                                        + " open="
                                        + open);
        }

        private static void ApplyFloorChange(ushort buildingId, int delta)
        {
            UndergroundParkingFacility facility;
            if (!UndergroundParkingRegistry.TryGetForBuilding(buildingId, out facility)
                && !UndergroundParkingRegistry.TryGetForTargetBuilding(buildingId, out facility))
            {
                _lastStatus = "The selected entrance has no registered underground garage.";
                return;
            }

            int requested = facility.FloorCount + delta;
            int minimumFloorCount =
                UndergroundParkingGeometry.GetMinimumFloorCount(facility);
            if (requested < minimumFloorCount)
            {
                _lastStatus = "This underground garage must retain at least "
                              + minimumFloorCount
                              + (minimumFloorCount == 1 ? " floor." : " floors.");
                return;
            }

            int maximumFloorCount =
                UndergroundParkingGeometry.GetMaximumFloorCount(facility);
            if (requested > maximumFloorCount)
            {
                _lastStatus = "Maximum underground floor count is "
                              + maximumFloorCount
                              + ".";
                return;
            }

            string validation;
            if (delta < 0)
            {
                if (!UndergroundParkingRegistry.TrySetFloorCount(buildingId, requested, out validation))
                {
                    _lastStatus = validation;
                    UndergroundParkingLog.Advanced("UPG floor removal rejected: building="
                                                + buildingId
                                                + " reason="
                                                + validation);
                    return;
                }

                _lastStatus = "Floor removed. No refund issued.";
                return;
            }

            EconomyManager economy = EconomyManager.instance;
            BuildingManager buildings = BuildingManager.instance;
            if (economy == null || buildings == null || buildingId >= buildings.m_buildings.m_size)
            {
                _lastStatus = "The city economy is not available; no floor was added.";
                return;
            }

            Building building = buildings.m_buildings.m_buffer[buildingId];
            BuildingInfo info = building.Info;
            if ((building.m_flags & Building.Flags.Created) == 0 || info == null || info.m_class == null)
            {
                _lastStatus = "The selected entrance is no longer available.";
                return;
            }

            int cost = AddedFloorDisplayCost * 100;

            if (economy.PeekResource(EconomyManager.Resource.Construction, cost) < cost)
            {
                _lastStatus = "Not enough money to add a floor (₡" + AddedFloorDisplayCost + ").";
                return;
            }

            int charged = economy.FetchResource(EconomyManager.Resource.Construction, cost, info.m_class);
            if (charged < cost)
            {
                if (charged > 0)
                    economy.AddResource(EconomyManager.Resource.Construction, charged, info.m_class);
                _lastStatus = "The floor charge could not be completed; no floor was added.";
                return;
            }

            if (!UndergroundParkingRegistry.TrySetFloorCount(buildingId, requested, out validation))
            {
                economy.AddResource(EconomyManager.Resource.Construction, charged, info.m_class);
                _lastStatus = validation + " The charge was refunded.";
                UndergroundParkingLog.Advanced("UPG floor addition rejected and refunded: building="
                                            + buildingId
                                            + " reason="
                                            + validation);
                return;
            }

            _lastStatus = "Floor added. Charged ₡" + AddedFloorDisplayCost + ".";
        }
    }
}
