using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UndergroundParkingGarage
{
    public static class UndergroundParkingRegistry
    {
        internal const string GarageOverlapStatus = "Underground garage footprint overlaps an existing underground parking facility.";

        private const int SerializationVersion = 11;
        private const int MaximumSerializedFacilities = ushort.MaxValue - 1;
        private const float SameFacilityPositionTolerance = 0.04f;
        private static readonly List<UndergroundParkingFacility> Facilities = new List<UndergroundParkingFacility>();
        private static readonly HashSet<int> ClosedFacilityIds = new HashSet<int>();
        private static readonly HashSet<int> RelocationPausedFacilityIds = new HashSet<int>();
        private static readonly List<PendingEntranceRelocation> PendingEntranceRelocations =
            new List<PendingEntranceRelocation>();
        private const int PendingRelocationChecksPerUpdate = 4;
        private static int _nextId = 1;
        private static int _revision;
        private static string _lastStatus = "Ready.";
        private static bool _legacyParkingMigrated;

        private sealed class PendingEntranceRelocation
        {
            public ushort BuildingId;
            public int FacilityId;
            public UndergroundParkingFacility Draft;
        }

        internal struct ImportedParkedAssignment
        {
            public readonly ushort ParkedId;
            public readonly int FacilityId;
            public readonly int SlotIndex;
            public readonly string PrefabName;

            public ImportedParkedAssignment(
                ushort parkedId,
                int facilityId,
                int slotIndex,
                string prefabName)
            {
                ParkedId = parkedId;
                FacilityId = facilityId;
                SlotIndex = slotIndex;
                PrefabName = prefabName ?? string.Empty;
            }
        }

        internal static bool NeedsLegacyParkingMigration
        {
            get { return !_legacyParkingMigrated; }
        }

        internal static void MarkLegacyParkingMigrated()
        {
            _legacyParkingMigrated = true;
        }

        public static int Count
        {
            get { return Facilities.Count; }
        }

        public static int Revision
        {
            get { return _revision; }
        }

        public static string LastStatus
        {
            get { return _lastStatus; }
        }

        public static UndergroundParkingFacility AddOrReplace(
            UndergroundParkingFacility draft,
            out bool replaced,
            out string status)
        {
            return AddOrReplaceInternal(draft, false, out replaced, out status);
        }

        public static UndergroundParkingFacility AddOrReplaceFromBuilding(
            ushort buildingId,
            UndergroundParkingFacility draft,
            out bool replaced,
            out string status)
        {
            return AddOrReplaceInternal(draft.WithEntranceBuilding(buildingId), false, out replaced, out status);
        }

        private static UndergroundParkingFacility AddOrReplaceInternal(
            UndergroundParkingFacility draft,
            bool createAnchor,
            out bool replaced,
            out string status)
        {
            replaced = false;
            if (draft.TargetBuildingId != 0)
            {
                UndergroundParkingFacility existingTarget;
                if (TryGetForTargetBuilding(draft.TargetBuildingId, out existingTarget))
                {
                    status = "This building already has an underground car park. Delete the existing car park first.";
                    _lastStatus = status;
                    UndergroundParkingLog.Warning("Rejected duplicate building-attached parking placement: targetBuilding="
                                                   + draft.TargetBuildingId
                                                   + " existingFacility="
                                                   + existingTarget.Id);
                    return UndergroundParkingFacility.None;
                }
            }

            int floorCount = draft.FloorCount;
            if (draft.EntranceBuildingId != 0)
            {
                for (int i = 0; i < Facilities.Count; i++)
                {
                    if (Facilities[i].EntranceBuildingId == draft.EntranceBuildingId)
                    {
                        floorCount = Facilities[i].FloorCount;
                        break;
                    }
                }
            }
            Vector3 garageCenter = draft.TargetBuildingId == 0
                ? UndergroundParkingGeometry.CalculateGarageCenter(
                    draft.EntrancePosition,
                    draft.Side,
                    floorCount,
                    draft.GarageLength)
                : draft.GarageCenter;
            UndergroundParkingFacility candidate = new UndergroundParkingFacility(
                0,
                draft.SurfaceSegmentId,
                draft.SurfaceSegmentPosition,
                draft.SurfaceRoadPosition,
                draft.EntrancePosition,
                draft.Direction,
                draft.Side,
                garageCenter,
                draft.VehicleNodePosition,
                draft.ConnectorStartPosition,
                0,
                0,
                0,
                0,
                false,
                draft.EntranceBuildingId,
                floorCount, draft.TargetBuildingId, draft.GarageForward, draft.GarageRight,
                draft.GarageWidth, draft.GarageLength, 0, draft.EntranceVisualsEnabled);

            if (OverlapsGarageReservation(candidate, candidate.EntranceBuildingId))
            {
                status = GarageOverlapStatus;
                _lastStatus = status;
                UndergroundParkingLog.Warning("Rejected underground parking garage placement: "
                                               + status
                                               + " surfaceSegment="
                                               + candidate.SurfaceSegmentId
                                               + " pos="
                                               + candidate.SurfaceSegmentPosition.ToString("0.000")
                                               + " entranceBuilding="
                                               + candidate.EntranceBuildingId);
                return UndergroundParkingFacility.None;
            }

            int preservedGarageDetailVariant = -1;
            for (int i = Facilities.Count - 1; i >= 0; i--)
            {
                if (!IsSameRegisteredBuilding(Facilities[i], candidate)
                    && !IsSameTargetBuilding(Facilities[i], candidate)
                    && !IsSamePlacement(Facilities[i], candidate))
                    continue;

                preservedGarageDetailVariant = Facilities[i].GarageDetailVariant;
                break;
            }
            int facilityId;
            if (!TryAllocateFacilityId(out facilityId))
            {
                status = "The city has reached the supported underground parking facility limit.";
                _lastStatus = status;
                UndergroundParkingLog.Warning(
                    "Rejected underground parking placement because no safe facility ID remains: count="
                    + Facilities.Count);
                return UndergroundParkingFacility.None;
            }
            int garageDetailVariant = preservedGarageDetailVariant >= 0
                ? preservedGarageDetailVariant
                : CreateGarageDetailVariant(facilityId, candidate);
            UndergroundParkingFacility facility = new UndergroundParkingFacility(
                facilityId,
                candidate.SurfaceSegmentId,
                candidate.SurfaceSegmentPosition,
                candidate.SurfaceRoadPosition,
                candidate.EntrancePosition,
                candidate.Direction,
                candidate.Side,
                candidate.GarageCenter,
                candidate.VehicleNodePosition,
                candidate.ConnectorStartPosition,
                candidate.EntrancePropId,
                candidate.ConnectorSegmentId,
                candidate.ConnectorStartNodeId,
                candidate.ConnectorEndNodeId,
                candidate.ConnectorCreated,
                candidate.EntranceBuildingId,
                candidate.FloorCount, candidate.TargetBuildingId, candidate.GarageForward,
                candidate.GarageRight, candidate.GarageWidth, candidate.GarageLength, 0,
                candidate.EntranceVisualsEnabled,
                garageDetailVariant);

            if (createAnchor && !UndergroundParkingEntranceAnchorService.TryEnsureAnchor(ref facility))
            {
                status = "Unable to place the required parking sign. Nothing was created.";
                _lastStatus = status;
                UndergroundParkingLog.Warning("Rejected underground parking garage placement because the surface sign could not be created: targetBuilding="
                                               + facility.TargetBuildingId
                                               + " surfaceSegment="
                                               + facility.SurfaceSegmentId);
                return UndergroundParkingFacility.None;
            }

            for (int i = Facilities.Count - 1; i >= 0; i--)
            {
                if (!IsSameRegisteredBuilding(Facilities[i], facility)
                    && !IsSameTargetBuilding(Facilities[i], facility)
                    && !IsSamePlacement(Facilities[i], facility))
                    continue;

                ClosedFacilityIds.Remove(Facilities[i].Id);
                ReleaseOwnedObjects(Facilities[i]);
                Facilities.RemoveAt(i);
                replaced = true;
            }

            Facilities.Add(facility);
            UndergroundParkingHostManager.ClearStatus(facility.TargetBuildingId);
            _revision++;
            UndergroundParkingOccupancyHarmony.RefreshForFacilityCount();
            UndergroundParkingBuildingPrefab.RefreshBuildingSelection(facility.EntranceBuildingId);
            UndergroundParkingVisualManager.RequestRebuild();

            UndergroundParkingRoadConnection roadConnection;
            string roadConnectionMessage;
            bool hasRoadConnection = UndergroundParkingAccessManager.TryGetRoadConnection(
                facility,
                out roadConnection,
                out roadConnectionMessage);

            status = "Placed underground parking garage entrance. Capacity: "
                     + UndergroundParkingGeometry.GetParkingSpaceCapacity(facility)
                     + " spaces. "
                     + (hasRoadConnection
                         ? "Road-lane handoff resolved; no generated road connector created."
                         : "No road-lane handoff resolved: " + roadConnectionMessage);
            _lastStatus = status;
            UndergroundParkingLog.Advanced("Underground parking garage placed: id="
                                        + facility.Id
                                        + " surfaceSegment="
                                        + facility.SurfaceSegmentId
                                        + " pos="
                                        + facility.SurfaceSegmentPosition.ToString("0.000")
                                        + " connectorSegment="
                                        + facility.ConnectorSegmentId
                                        + " connectorNode="
                                        + facility.ConnectorStartNodeId
                                        + " entranceBuilding="
                                        + facility.EntranceBuildingId
                                        + " garageDetailVariant="
                                        + facility.GarageDetailVariant
                                        + " garageDetailAssignment="
                                        + (preservedGarageDetailVariant >= 0 ? "preserved" : "new-once")
                                        + " roadLaneConnected="
                                        + hasRoadConnection
                                        + " laneSegment="
                                        + (hasRoadConnection ? roadConnection.SegmentId.ToString() : "0")
                                        + " laneIndex="
                                        + (hasRoadConnection ? roadConnection.LaneIndex.ToString() : "0")
                                        + " laneOffset="
                                        + (hasRoadConnection ? roadConnection.SegmentOffset.ToString() : "0")
                                        + " roadEntrance="
                                        + (hasRoadConnection ? FormatVector(roadConnection.RoadEntrancePosition) : "none")
                                        + " lanePosition="
                                        + (hasRoadConnection ? FormatVector(roadConnection.LanePosition) : "none")
                                        + " replaced="
                                        + replaced
                                        + " count="
                                        + Facilities.Count
                                        + " status="
                                        + status);
            return facility;
        }

        public static void RemoveGeneratedConnectors()
        {
            if (Facilities.Count == 0)
                return;

            bool changed = false;
            int released = 0;
            for (int i = 0; i < Facilities.Count; i++)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (!facility.ConnectorCreated)
                    continue;

                UndergroundParkingConnectorCleanup.ReleaseConnector(facility);
                Facilities[i] = facility.WithConnector(0, 0, 0, false);
                changed = true;
                released++;
            }

            if (!changed)
                return;

            _revision++;
            _lastStatus = "Removed generated underground connector(s): " + released + ".";
            UndergroundParkingLog.Advanced("Removed generated underground parking connector(s): count="
                                        + released);
        }

        public static void RemoveBuildingAttachedPropAnchors()
        {
            bool changed = false;
            int released = 0;
            for (int i = 0; i < Facilities.Count; i++)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (facility.TargetBuildingId == 0
                    || (facility.EntrancePropId == 0 && facility.EntranceBackPropId == 0))
                    continue;

                UndergroundParkingEntranceAnchorService.ReleaseAnchor(facility);
                Facilities[i] = facility.WithEntranceProps(0, 0);
                changed = true;
                released++;
            }

            if (!changed)
                return;

            _revision++;
            UndergroundParkingVisualManager.RequestRebuild();
            UndergroundParkingLog.Advanced("Removed obsolete building-attached PropManager sign anchors: count="
                                        + released);
        }

        public static void RemoveFacilitiesWithMissingAnchors()
        {
            if (Facilities.Count == 0)
                return;

            int removed = 0;
            for (int i = Facilities.Count - 1; i >= 0; i--)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (facility.TargetBuildingId != 0)
                    continue;
                if (facility.EntrancePropId == 0)
                    continue;

                if (UndergroundParkingEntranceAnchorService.IsAnchorLive(facility))
                    continue;

                ClosedFacilityIds.Remove(facility.Id);
                ReleaseOwnedObjects(facility);
                Facilities.RemoveAt(i);
                removed++;
            }

            if (removed <= 0)
                return;

            _revision++;
            UndergroundParkingOccupancyHarmony.RefreshForFacilityCount();
            _lastStatus = "Bulldozed underground parking entrance marker(s): " + removed + ".";
            UndergroundParkingVisualManager.RequestRebuild();
            UndergroundParkingPanel.RefreshInstance();
            UndergroundParkingLog.Advanced("Bulldoze cleanup removed underground parking garage marker(s): count="
                                        + removed
                                        + " remaining="
                                        + Facilities.Count);
        }

        public static void RemoveFacilitiesWithMissingBuildings()
        {
            if (Facilities.Count == 0)
                return;

            int removed = 0;
            for (int i = Facilities.Count - 1; i >= 0; i--)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (facility.TargetBuildingId != 0)
                {
                    Building target;
                    if (UndergroundParkingGeometry.TryGetUsableBuilding(facility.TargetBuildingId, out target))
                        continue;

                    Building.Flags hostFlags = Building.Flags.None;
                    BuildingManager buildingManager = BuildingManager.instance;
                    if (buildingManager != null
                        && facility.TargetBuildingId < buildingManager.m_buildings.m_size)
                    {
                        hostFlags = buildingManager.m_buildings.m_buffer[
                            facility.TargetBuildingId].m_flags;
                    }
                    UndergroundParkingLog.Warning(
                        "Nuking building-attached underground garage after host lifecycle ended: facility="
                        + facility.Id
                        + " targetBuilding="
                        + facility.TargetBuildingId
                        + " hostFlags="
                        + hostFlags);
                    UndergroundParkingHostManager.ClearStatus(facility.TargetBuildingId);
                }
                else
                {
                    if (facility.EntranceBuildingId == 0)
                        continue;
                    if (UndergroundParkingBuildingPrefab.IsGarageBuilding(facility.EntranceBuildingId))
                        continue;
                }

                ClosedFacilityIds.Remove(facility.Id);
                ReleaseOwnedObjects(facility);
                Facilities.RemoveAt(i);
                removed++;
            }

            if (removed <= 0)
                return;

            _revision++;
            UndergroundParkingOccupancyHarmony.RefreshForFacilityCount();
            _lastStatus = "Removed missing underground parking entrance building(s): " + removed + ".";
            UndergroundParkingVisualManager.RequestRebuild();
            UndergroundParkingPanel.RefreshInstance();
            UndergroundParkingLog.Advanced("Building cleanup removed underground parking garage entrance(s): count="
                                        + removed
                                        + " remaining="
                                        + Facilities.Count);
        }

        public static bool RemoveForBuilding(ushort buildingId, string status)
        {
            if (buildingId == 0 || Facilities.Count == 0)
                return false;

            int removed = 0;
            for (int i = Facilities.Count - 1; i >= 0; i--)
            {
                if (Facilities[i].EntranceBuildingId != buildingId)
                    continue;

                ClosedFacilityIds.Remove(Facilities[i].Id);
                ReleaseOwnedObjects(Facilities[i]);
                Facilities.RemoveAt(i);
                removed++;
            }

            if (removed <= 0)
                return false;

            _revision++;
            UndergroundParkingOccupancyHarmony.RefreshForFacilityCount();
            _lastStatus = status;
            UndergroundParkingVisualManager.RequestRebuild();
            UndergroundParkingPanel.RefreshInstance();
            UndergroundParkingLog.Advanced("Removed underground parking garage facility for building="
                                        + buildingId
                                        + " count="
                                        + removed
                                        + " remaining="
                                        + Facilities.Count);
            return true;
        }

        public static int NukeAllFacilities(out int kiosksBulldozed)
        {
            kiosksBulldozed = 0;
            int facilitiesRemoved = Facilities.Count;
            HashSet<ushort> kioskBuildings = new HashSet<ushort>();

            for (int i = Facilities.Count - 1; i >= 0; i--)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (facility.TargetBuildingId == 0 && facility.EntranceBuildingId != 0)
                    kioskBuildings.Add(facility.EntranceBuildingId);

                UndergroundParkingHostManager.ClearStatus(facility.TargetBuildingId);
                ReleaseOwnedObjects(facility);
            }

            Facilities.Clear();
            ClosedFacilityIds.Clear();
            RelocationPausedFacilityIds.Clear();
            PendingEntranceRelocations.Clear();
            _nextId = 1;
            _legacyParkingMigrated = true;

            // Also collect orphaned standalone kiosks whose registry record was
            // already missing. The reset promises a true pre-UPG city surface,
            // not merely an empty serialized facility list.
            BuildingManager buildingManager = BuildingManager.instance;
            if (buildingManager != null)
            {
                int buildingCount = (int)buildingManager.m_buildings.m_size;
                for (int i = 1; i < buildingCount && i <= ushort.MaxValue; i++)
                {
                    ref Building building = ref buildingManager.m_buildings.m_buffer[i];
                    if ((building.m_flags & Building.Flags.Created) == 0
                        || (building.m_flags & Building.Flags.Deleted) != 0
                        || !UndergroundParkingBuildingPrefab.IsGarageBuilding((ushort)i))
                    {
                        continue;
                    }

                    kioskBuildings.Add((ushort)i);
                }

                foreach (ushort buildingId in kioskBuildings)
                {
                    if (buildingId == 0 || buildingId >= buildingManager.m_buildings.m_size)
                        continue;

                    ref Building building = ref buildingManager.m_buildings.m_buffer[buildingId];
                    if ((building.m_flags & Building.Flags.Created) == 0
                        || (building.m_flags & Building.Flags.Deleted) != 0
                        || !UndergroundParkingBuildingPrefab.IsGarageBuilding(buildingId))
                    {
                        continue;
                    }

                    buildingManager.ReleaseBuilding(buildingId);
                    kiosksBulldozed++;
                }
            }

            _revision++;
            UndergroundParkingOccupancyHarmony.RefreshForFacilityCount();
            _lastStatus = "Temporary reset removed "
                          + facilitiesRemoved
                          + " underground parking facilit"
                          + (facilitiesRemoved == 1 ? "y" : "ies")
                          + " and bulldozed "
                          + kiosksBulldozed
                          + " standalone kiosk"
                          + (kiosksBulldozed == 1 ? "." : "s.");
            UndergroundParkingVisualManager.RequestRebuild();
            UndergroundParkingPanel.RefreshInstance();
            UndergroundParkingLog.Warning(
                "TEMPORARY CITY RESET removed all underground parking: facilities="
                + facilitiesRemoved
                + " kiosks="
                + kiosksBulldozed);
            return facilitiesRemoved;
        }

        public static bool TryGetForBuilding(ushort buildingId, out UndergroundParkingFacility facility)
        {
            facility = UndergroundParkingFacility.None;
            if (buildingId == 0)
                return false;

            for (int i = 0; i < Facilities.Count; i++)
            {
                if (Facilities[i].EntranceBuildingId != buildingId)
                    continue;

                facility = Facilities[i];
                return facility.IsValid;
            }

            return false;
        }

        public static bool TryGetForTargetBuilding(ushort buildingId, out UndergroundParkingFacility facility)
        {
            facility = UndergroundParkingFacility.None;
            if (buildingId == 0)
                return false;

            for (int i = 0; i < Facilities.Count; i++)
            {
                if (Facilities[i].TargetBuildingId != buildingId)
                    continue;

                facility = Facilities[i];
                return facility.IsValid;
            }

            return false;
        }

        public static bool IsFacilityOpen(UndergroundParkingFacility facility)
        {
            return facility.IsValid
                   && !ClosedFacilityIds.Contains(facility.Id)
                   && !RelocationPausedFacilityIds.Contains(facility.Id);
        }

        internal static bool IsEntranceRelocationPending(int facilityId)
        {
            if (facilityId <= 0)
                return false;

            for (int i = 0; i < PendingEntranceRelocations.Count; i++)
            {
                if (PendingEntranceRelocations[i].FacilityId == facilityId)
                    return true;
            }

            return false;
        }

        public static bool TrySetTargetFacilityOpen(ushort buildingId, bool open, out string status)
        {
            UndergroundParkingFacility facility;
            if (!TryGetForTargetBuilding(buildingId, out facility))
            {
                status = "This building has no underground car park.";
                return false;
            }

            if (IsEntranceRelocationPending(facility.Id))
            {
                status = "The entrance is relocating. Its previous open or closed state will be restored automatically.";
                _lastStatus = status;
                return false;
            }

            int cancelledOffers = 0;
            int restartedSearches = 0;
            if (open)
                ClosedFacilityIds.Remove(facility.Id);
            else
            {
                ClosedFacilityIds.Add(facility.Id);
                // A pre-trip candidate is only an offer: TM:PE still owns the
                // active road vehicle, its occupants and the parking search.
                // Withdraw every such offer synchronously when the player
                // closes the facility so those searches can resume normally.
                // Prepared/adopted transactions have already crossed TM:PE's
                // authoritative ParkVehicle boundary and must finish under
                // the existing carriageway occupant-safety contract.
                cancelledOffers = TmpeParkingCompatibilityManager
                    .CancelUncommittedCandidatesForFacility(
                        facility.Id,
                        out restartedSearches);
            }

            _revision++;
            status = open
                ? "Car park turned on and accepting arrivals."
                : "Car park turned off. "
                  + cancelledOffers
                  + (cancelledOffers == 1
                      ? " uncommitted offer returned to TM:PE; "
                      : " uncommitted offers returned to TM:PE; ")
                  + restartedSearches
                  + (restartedSearches == 1
                      ? " parking search restarted immediately; "
                      : " parking searches restarted immediately; ")
                  + "committed arrivals may finish and existing vehicles may leave.";
            _lastStatus = status;
            UndergroundParkingLog.Advanced("Building-attached car park state changed: facility="
                                        + facility.Id
                                        + " targetBuilding="
                                        + buildingId
                                        + " open="
                                        + open
                                        + " cancelledUncommittedOffers="
                                        + cancelledOffers
                                        + " restartedTmpeSearches="
                                        + restartedSearches);
            return true;
        }

        public static bool TrySetTargetEntranceVisuals(ushort buildingId, bool enabled, out string status)
        {
            for (int i = 0; i < Facilities.Count; i++)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (facility.TargetBuildingId != buildingId)
                    continue;

                Facilities[i] = facility.WithEntranceVisuals(enabled);
                _revision++;
                status = enabled
                    ? "UPG entrance sign and ramp shown."
                    : "UPG entrance sign and ramp hidden; add your own decoration.";
                _lastStatus = status;
                UndergroundParkingVisualManager.RequestRebuild();
                UndergroundParkingLog.Advanced("Building-attached entrance visuals changed: facility="
                                            + facility.Id
                                            + " targetBuilding="
                                            + buildingId
                                            + " enabled="
                                            + enabled);
                return true;
            }

            status = "This building has no underground car park.";
            return false;
        }

        public static bool TryRelocateTargetEntrance(
            ushort buildingId,
            int expectedFacilityId,
            UndergroundParkingFacility draft,
            out string status)
        {
            int facilityIndex = -1;
            UndergroundParkingFacility existing = UndergroundParkingFacility.None;
            for (int i = 0; i < Facilities.Count; i++)
            {
                if (Facilities[i].TargetBuildingId != buildingId
                    || Facilities[i].Id != expectedFacilityId)
                    continue;

                facilityIndex = i;
                existing = Facilities[i];
                break;
            }

            if (facilityIndex < 0
                || !existing.IsValid
                || draft.TargetBuildingId != buildingId
                || draft.SurfaceSegmentId == 0)
            {
                RemovePendingEntranceRelocation(expectedFacilityId);
                status = "The original underground car park is no longer available. Its entrance was not moved.";
                _lastStatus = status;
                return false;
            }

            UndergroundParkingFacility relocated = new UndergroundParkingFacility(
                existing.Id,
                draft.SurfaceSegmentId,
                draft.SurfaceSegmentPosition,
                draft.SurfaceRoadPosition,
                draft.EntrancePosition,
                draft.Direction,
                draft.Side,
                existing.GarageCenter,
                draft.VehicleNodePosition,
                draft.ConnectorStartPosition,
                existing.EntrancePropId,
                0,
                0,
                0,
                false,
                0,
                existing.FloorCount,
                existing.TargetBuildingId,
                existing.GarageForward,
                existing.GarageRight,
                existing.GarageWidth,
                existing.GarageLength,
                existing.EntranceBackPropId,
                existing.EntranceVisualsEnabled,
                existing.GarageDetailVariant);

            UndergroundParkingRoadConnection connection;
            string connectionMessage;
            if (!UndergroundParkingAccessManager.TryGetRoadConnection(
                    relocated,
                    out connection,
                    out connectionMessage))
            {
                RemovePendingEntranceRelocation(expectedFacilityId);
                status = "The new entrance has no valid road-lane handoff. The original entrance was retained.";
                _lastStatus = status;
                return false;
            }

            RelocationPausedFacilityIds.Add(existing.Id);
            bool resumeOpen = !ClosedFacilityIds.Contains(existing.Id);
            int restartedSearches;
            int cancelledOffers = TmpeParkingCompatibilityManager
                .CancelUncommittedCandidatesForFacility(
                    existing.Id,
                    out restartedSearches);
            int failedArrivalRepaths;
            int repathedArrivals = TmpeParkingCompatibilityManager
                .RepathAdoptedArrivalsForFacility(
                    existing.Id,
                    connection,
                    out failedArrivalRepaths);

            RemovePendingEntranceRelocation(expectedFacilityId);
            Facilities[facilityIndex] = relocated;
            UndergroundParkingPortalAnimationManager
                .RequestDepartureRepathForFacility(existing.Id);
            int refreshedDepartures = UndergroundParkingOccupancyHarmony
                .RefreshManagedDeparturesForRelocatedFacility(existing.Id);
            _revision++;
            UndergroundParkingVisualManager.RequestRebuild();
            UndergroundParkingPanel.RefreshInstance();
            status = resumeOpen
                ? "Underground car park entrance moved and the car park reopened to new arrivals. Garage and parked vehicles are unchanged."
                : "Underground car park entrance moved. The car park remains closed as before; garage and parked vehicles are unchanged.";
            _lastStatus = status;
            UndergroundParkingLog.Advanced(
                "Building-attached entrance relocated transactionally: facility="
                + existing.Id
                + " targetBuilding="
                + buildingId
                + " oldSegment="
                + existing.SurfaceSegmentId
                + " oldPosition="
                + existing.SurfaceSegmentPosition.ToString("0.000")
                + " newSegment="
                + relocated.SurfaceSegmentId
                + " newPosition="
                + relocated.SurfaceSegmentPosition.ToString("0.000")
                + " cancelledUncommittedOffers="
                + cancelledOffers
                + " restartedTmpeSearches="
                + restartedSearches
                + " repathedAdoptedArrivals="
                + repathedArrivals
                + " failedArrivalRepaths="
                + failedArrivalRepaths
                + " refreshedDepartures="
                + refreshedDepartures
                + " immediateCommit=True"
                + " garageUnchanged=True occupancyUnchanged=True");
            return true;
        }

        public static void ProcessPendingEntranceRelocations()
        {
            int checkedCount = 0;
            for (int i = PendingEntranceRelocations.Count - 1;
                 i >= 0 && checkedCount < PendingRelocationChecksPerUpdate;
                 i--, checkedCount++)
            {
                PendingEntranceRelocation pending = PendingEntranceRelocations[i];
                if (HasFacilityActivity(pending.FacilityId))
                    continue;

                string status;
                bool moved = TryRelocateTargetEntrance(
                    pending.BuildingId,
                    pending.FacilityId,
                    pending.Draft,
                    out status);
                UndergroundParkingHostManager.ReportStatus(pending.BuildingId, status);
                if (!moved)
                {
                    UndergroundParkingLog.Warning(
                        "Deferred building-attached entrance relocation cancelled: facility="
                        + pending.FacilityId
                        + " reason="
                        + status
                        + " originalEntranceRetained=True");
                }
            }
        }

        internal static bool HasFacilityActivity(int facilityId)
        {
            return UndergroundParkingEntryRouteManager.HasActivityForFacility(facilityId)
                   || UndergroundParkingPortalAnimationManager.HasActivityForFacility(facilityId)
                   || UndergroundParkingVisualManager.HasInternalParkingJourneyForFacility(facilityId)
                   || UndergroundParkingOccupancyHarmony.HasLifecycleActivityForFacility(facilityId)
                   || UndergroundParkingOccupancyManager.HasTransientActivityForFacility(facilityId);
        }

        private static void QueuePendingEntranceRelocation(
            ushort buildingId,
            int facilityId,
            UndergroundParkingFacility draft)
        {
            for (int i = 0; i < PendingEntranceRelocations.Count; i++)
            {
                if (PendingEntranceRelocations[i].FacilityId != facilityId)
                    continue;

                PendingEntranceRelocations[i].BuildingId = buildingId;
                PendingEntranceRelocations[i].Draft = draft;
                return;
            }

            PendingEntranceRelocations.Add(new PendingEntranceRelocation
            {
                BuildingId = buildingId,
                FacilityId = facilityId,
                Draft = draft
            });
        }

        private static void RemovePendingEntranceRelocation(int facilityId)
        {
            for (int i = PendingEntranceRelocations.Count - 1; i >= 0; i--)
            {
                if (PendingEntranceRelocations[i].FacilityId == facilityId)
                    PendingEntranceRelocations.RemoveAt(i);
            }
            RelocationPausedFacilityIds.Remove(facilityId);
        }

        public static int SetAllBuildingAttachedEntranceVisuals(bool enabled)
        {
            int changed = 0;
            for (int i = 0; i < Facilities.Count; i++)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (facility.TargetBuildingId == 0 || facility.EntranceVisualsEnabled == enabled)
                    continue;

                Facilities[i] = facility.WithEntranceVisuals(enabled);
                changed++;
            }

            if (changed > 0)
            {
                _revision++;
                UndergroundParkingVisualManager.RequestRebuild();
            }

            _lastStatus = enabled
                ? "UPG entrance signs and ramps shown for all building-attached garages."
                : "UPG entrance signs and ramps hidden for all building-attached garages.";
            UndergroundParkingLog.Advanced("All building-attached entrance visuals changed: enabled="
                                        + enabled
                                        + " changed="
                                        + changed);
            return changed;
        }

        public static bool TryRemoveForTargetBuilding(ushort buildingId, out string status)
        {
            UndergroundParkingFacility facility;
            if (!TryGetForTargetBuilding(buildingId, out facility))
            {
                status = "This building has no underground car park.";
                return false;
            }

            int occupied = UndergroundParkingOccupancyManager.CountAssignedParkedCars(facility);
            if (occupied > 0)
            {
                status = "Cannot delete while " + occupied
                         + (occupied == 1 ? " vehicle is" : " vehicles are")
                         + " still parked. Turn off the car park and let it drain first.";
                return false;
            }

            if (HasFacilityActivity(facility.Id))
            {
                status = "Cannot delete while garage traffic is still using this entrance.";
                return false;
            }

            for (int i = Facilities.Count - 1; i >= 0; i--)
            {
                if (Facilities[i].TargetBuildingId != buildingId)
                    continue;

                ClosedFacilityIds.Remove(Facilities[i].Id);
                ReleaseOwnedObjects(Facilities[i]);
                Facilities.RemoveAt(i);
            }

            _revision++;
            UndergroundParkingOccupancyHarmony.RefreshForFacilityCount();
            status = "Underground car park deleted. No refund issued.";
            _lastStatus = status;
            UndergroundParkingVisualManager.RequestRebuild();
            UndergroundParkingPanel.RefreshInstance();
            UndergroundParkingLog.Advanced("Deleted building-attached underground car park: facility="
                                        + facility.Id
                                        + " targetBuilding="
                                        + buildingId);
            return true;
        }

        public static bool TrySetFloorCount(ushort buildingId, int requestedFloorCount, out string status)
        {
            status = string.Empty;
            for (int i = 0; i < Facilities.Count; i++)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (facility.EntranceBuildingId != buildingId
                    && facility.TargetBuildingId != buildingId)
                    continue;

                int floorCount = UndergroundParkingGeometry.ClampFloorCount(requestedFloorCount);
                int minimumFloorCount =
                    UndergroundParkingGeometry.GetMinimumFloorCount(facility);
                int maximumFloorCount =
                    UndergroundParkingGeometry.GetMaximumFloorCount(facility);
                if (floorCount != requestedFloorCount
                    || requestedFloorCount < minimumFloorCount
                    || requestedFloorCount > maximumFloorCount)
                {
                    status = requestedFloorCount < minimumFloorCount
                        ? "This underground garage must retain at least "
                          + minimumFloorCount
                          + (minimumFloorCount == 1 ? " floor." : " floors.")
                        : "The underground garage supports at most "
                          + maximumFloorCount
                          + " floors.";
                    return false;
                }

                int occupied = UndergroundParkingOccupancyManager.CountAssignedParkedCars(facility);
                int capacity = UndergroundParkingGeometry.GetParkingSpaceCapacity(facility, floorCount);
                int parkedOnRemovedFloors;
                if (floorCount < facility.FloorCount
                    && UndergroundParkingOccupancyManager.HasAssignedCarsOnRemovedFloors(
                        facility,
                        floorCount,
                        out parkedOnRemovedFloors))
                {
                    status = "Cannot remove the floor while "
                             + parkedOnRemovedFloors
                             + (parkedOnRemovedFloors == 1 ? " vehicle is" : " vehicles are")
                             + " still parked on it.";
                    return false;
                }

                int transientClaimsOnRemovedFloors;
                if (floorCount < facility.FloorCount
                    && UndergroundParkingOccupancyManager.HasTransientClaimsOnRemovedFloors(
                        facility,
                        floorCount,
                        out transientClaimsOnRemovedFloors))
                {
                    status = "Cannot remove the floor while "
                             + transientClaimsOnRemovedFloors
                             + (transientClaimsOnRemovedFloors == 1
                                 ? " arriving vehicle still owns a space on it."
                                 : " arriving vehicles still own spaces on it.");
                    return false;
                }

                if (occupied > capacity)
                {
                    status = "Cannot remove a floor while " + occupied + " vehicles occupy " + capacity + " remaining spaces.";
                    return false;
                }

                if (floorCount == facility.FloorCount)
                {
                    status = "Floor count is already " + floorCount + ".";
                    return true;
                }

                UndergroundParkingFacility updated = facility.WithFloorCount(floorCount);
                Facilities[i] = updated;
                _revision++;
                UndergroundParkingOccupancyManager.RefreshFacilitySlotPositions(updated);
                _lastStatus = "Updated underground garage to " + floorCount + " floor(s), capacity " + capacity + ".";
                status = _lastStatus;
                UndergroundParkingVisualManager.RequestRebuild();
                UndergroundParkingBuildingPrefab.RefreshBuildingSelection(buildingId);
                UndergroundParkingLog.Advanced("UPG floor count changed: facility=" + facility.Id
                                            + " building=" + buildingId
                                            + " floors=" + floorCount
                                            + " capacity=" + capacity
                                            + " occupied=" + occupied);
                return true;
            }

            status = "The selected entrance has no registered underground garage.";
            return false;
        }

        public static int CopyTo(List<UndergroundParkingFacility> buffer)
        {
            if (buffer == null)
                return 0;

            buffer.Clear();
            buffer.AddRange(Facilities);
            return buffer.Count;
        }

        public static bool OverlapsGarageReservation(
            UndergroundParkingFacility candidate,
            ushort ignoreBuildingId)
        {
            if (Facilities.Count == 0 || candidate.SurfaceSegmentId == 0)
                return false;

            Vector3 candidateForward = candidate.GarageForward;
            candidateForward.y = 0f;
            if (candidateForward.sqrMagnitude <= 0.001f)
                candidateForward = Vector3.forward;

            candidateForward.Normalize();
            Vector3 candidateRight = new Vector3(candidateForward.z, 0f, -candidateForward.x);

            for (int i = 0; i < Facilities.Count; i++)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (!facility.IsValid
                    || (ignoreBuildingId != 0
                        && (facility.EntranceBuildingId == ignoreBuildingId
                            || facility.TargetBuildingId == ignoreBuildingId))
                    || IsSameRegisteredBuilding(facility, candidate))
                {
                    continue;
                }

                if (UndergroundParkingGeometry.GarageFootprintOverlapsRect(
                        facility,
                        candidate.GarageCenter,
                        candidateRight,
                        candidateForward,
                        candidate.GarageWidth * 0.5f,
                        candidate.GarageLength * 0.5f,
                        0.25f))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IntersectsGarageReservationPath(Vector3 start, Vector3 middle, Vector3 end)
        {
            if (Facilities.Count == 0)
                return false;

            for (int i = 0; i < Facilities.Count; i++)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (!facility.IsValid)
                    continue;

                if (UndergroundParkingGeometry.GarageFootprintIntersectsSegment(facility, start, middle, 1.25f)
                    || UndergroundParkingGeometry.GarageFootprintIntersectsSegment(facility, middle, end, 1.25f))
                {
                    return true;
                }
            }

            return false;
        }

        public static void RefreshEntranceBuildingSelection()
        {
            if (Facilities.Count == 0)
                return;

            for (int i = 0; i < Facilities.Count; i++)
                UndergroundParkingBuildingPrefab.RefreshBuildingSelection(Facilities[i].EntranceBuildingId);
        }

        public static void RefreshRoadConnections()
        {
            if (Facilities.Count == 0)
                return;

            int connected = 0;
            int missing = 0;
            for (int i = 0; i < Facilities.Count; i++)
            {
                UndergroundParkingRoadConnection connection;
                string message;
                if (UndergroundParkingAccessManager.TryGetRoadConnection(Facilities[i], out connection, out message))
                {
                    connected++;
                    continue;
                }

                missing++;
                UndergroundParkingLog.Warning("Underground parking road-lane handoff missing: facility="
                                               + Facilities[i].Id
                                               + " segment="
                                               + Facilities[i].SurfaceSegmentId
                                               + " pos="
                                               + Facilities[i].SurfaceSegmentPosition.ToString("0.000")
                                               + " reason="
                                               + message);
            }

            UndergroundParkingLog.Advanced("Underground parking road-lane handoffs refreshed: connected="
                                        + connected
                                        + " missing="
                                        + missing);
        }

        public static void ClearTransient()
        {
            RelocationPausedFacilityIds.Clear();
            PendingEntranceRelocations.Clear();
            UndergroundParkingVisualManager.Clear();
        }

        public static byte[] Serialize()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(SerializationVersion);
                    writer.Write(_legacyParkingMigrated);
                    writer.Write(Facilities.Count);
                    for (int i = 0; i < Facilities.Count; i++)
                    {
                        UndergroundParkingFacility facility = Facilities[i];
                        writer.Write(facility.Id);
                        writer.Write(facility.SurfaceSegmentId);
                        writer.Write(facility.SurfaceSegmentPosition);
                        WriteVector(writer, facility.SurfaceRoadPosition);
                        WriteVector(writer, facility.EntrancePosition);
                        WriteVector(writer, facility.Direction);
                        WriteVector(writer, facility.Side);
                        WriteVector(writer, facility.GarageCenter);
                        WriteVector(writer, facility.VehicleNodePosition);
                        WriteVector(writer, facility.ConnectorStartPosition);
                        writer.Write(facility.EntrancePropId);
                        writer.Write(facility.EntranceBackPropId);
                        writer.Write(facility.ConnectorSegmentId);
                        writer.Write(facility.ConnectorStartNodeId);
                        writer.Write(facility.ConnectorEndNodeId);
                        writer.Write(facility.ConnectorCreated);
                        writer.Write(facility.EntranceBuildingId);
                        writer.Write(facility.FloorCount);
                        writer.Write(facility.TargetBuildingId);
                        WriteVector(writer, facility.GarageForward);
                        WriteVector(writer, facility.GarageRight);
                        writer.Write(facility.GarageWidth);
                        writer.Write(facility.GarageLength);
                        writer.Write(!ClosedFacilityIds.Contains(facility.Id));
                        writer.Write(facility.EntranceVisualsEnabled);
                        writer.Write(facility.GarageDetailVariant);
                    }
                }

                return stream.ToArray();
            }
        }

        public static int Restore(byte[] data)
        {
            Facilities.Clear();
            ClosedFacilityIds.Clear();
            RelocationPausedFacilityIds.Clear();
            PendingEntranceRelocations.Clear();
            _nextId = 1;
            _legacyParkingMigrated = false;
            _revision++;

            if (data == null || data.Length == 0)
            {
                _legacyParkingMigrated = false;
                _lastStatus = "Ready.";
                return 0;
            }

            List<UndergroundParkingFacility> restoredFacilities =
                new List<UndergroundParkingFacility>();
            HashSet<int> restoredIds = new HashSet<int>();
            HashSet<int> restoredClosedFacilityIds = new HashSet<int>();
            bool restoredLegacyParkingMigrated = false;
            int restoredNextId = 1;
            using (MemoryStream stream = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int version = reader.ReadInt32();
                if (version < 1 || version > SerializationVersion)
                {
                    UndergroundParkingLog.Warning("Ignoring unsupported underground parking data version: " + version);
                    return 0;
                }

                restoredLegacyParkingMigrated = version >= 4 && reader.ReadBoolean();
                int count = reader.ReadInt32();
                if (count < 0 || count > MaximumSerializedFacilities)
                {
                    throw new InvalidDataException(
                        "UPG facility count is outside the supported range: " + count);
                }
                int maxId = 0;
                int legacyGarageDetailUpgrades = 0;
                for (int i = 0; i < count; i++)
                {
                    int id = reader.ReadInt32();
                    ushort surfaceSegmentId = reader.ReadUInt16();
                    float surfaceSegmentPosition = reader.ReadSingle();
                    Vector3 surfaceRoadPosition = ReadVector(reader);
                    Vector3 entrancePosition = ReadVector(reader);
                    Vector3 direction = ReadVector(reader);
                    Vector3 side = ReadVector(reader);
                    Vector3 garageCenter = ReadVector(reader);
                    Vector3 vehicleNodePosition = ReadVector(reader);
                    Vector3 connectorStartPosition = ReadVector(reader);
                    ushort entrancePropId = version >= 2 ? reader.ReadUInt16() : (ushort)0;
                    ushort entranceBackPropId = version >= 8 ? reader.ReadUInt16() : (ushort)0;
                    ushort connectorSegmentId = reader.ReadUInt16();
                    ushort connectorStartNodeId = reader.ReadUInt16();
                    ushort connectorEndNodeId = reader.ReadUInt16();
                    bool connectorCreated = reader.ReadBoolean();
                    ushort entranceBuildingId = version >= 3 ? reader.ReadUInt16() : (ushort)0;
                    int floorCount = version >= 5 ? reader.ReadInt32() : UndergroundParkingGeometry.DefaultFloorCount;
                    ushort targetBuildingId = version >= 6 ? reader.ReadUInt16() : (ushort)0;
                    Vector3 garageForward = version >= 6 ? ReadVector(reader) : side;
                    Vector3 garageRight = version >= 6 ? ReadVector(reader) : new Vector3(garageForward.z, 0f, -garageForward.x);
                    float garageWidth = version >= 6 ? reader.ReadSingle() : UndergroundParkingGeometry.GarageWidth;
                    float garageLength = version >= 6 ? reader.ReadSingle() : UndergroundParkingGeometry.GarageLength;
                    bool isOpen = version >= 7 ? reader.ReadBoolean() : true;
                    // Version 9 introduced the setting with inverted/default
                    // semantics and could hide every existing attached entrance.
                    // Consume its saved field, but migrate all pre-fix facilities
                    // back to visible. Version 10 persists only explicit choices
                    // made after the corrected opt-in suppression UI shipped.
                    bool savedEntranceVisualsEnabled = version >= 9
                        ? reader.ReadBoolean()
                        : true;
                    bool entranceVisualsEnabled = version >= 10
                        ? savedEntranceVisualsEnabled
                        : true;
                    int garageDetailVariant = version >= 11
                        ? Mathf.Clamp(reader.ReadInt32(), 0, 7)
                        : CreateGarageDetailVariant(
                            id,
                            surfaceSegmentId,
                            surfaceSegmentPosition,
                            targetBuildingId,
                            garageWidth,
                            garageLength);
                    if (id <= 0
                        || id > MaximumSerializedFacilities
                        || surfaceSegmentId == 0
                        || !IsFinite(surfaceSegmentPosition)
                        || !IsFinite(surfaceRoadPosition)
                        || !IsFinite(entrancePosition)
                        || !IsFinite(direction)
                        || !IsFinite(side)
                        || !IsFinite(garageCenter)
                        || !IsFinite(vehicleNodePosition)
                        || !IsFinite(connectorStartPosition)
                        || !IsFinite(garageForward)
                        || !IsFinite(garageRight)
                        || !IsFinite(garageWidth)
                        || !IsFinite(garageLength)
                        || garageWidth <= 0f
                        || garageLength <= 0f
                        || !restoredIds.Add(id))
                        continue;

                    UndergroundParkingFacility restored = new UndergroundParkingFacility(
                        id,
                        surfaceSegmentId,
                        surfaceSegmentPosition,
                        surfaceRoadPosition,
                        entrancePosition,
                        direction,
                        side,
                        garageCenter,
                        vehicleNodePosition,
                        connectorStartPosition,
                        entrancePropId,
                        connectorSegmentId,
                        connectorStartNodeId,
                        connectorEndNodeId,
                        connectorCreated,
                        entranceBuildingId,
                        floorCount, targetBuildingId, garageForward, garageRight, garageWidth, garageLength,
                        entranceBackPropId, entranceVisualsEnabled,
                        garageDetailVariant);
                    restoredFacilities.Add(restored);
                    if (version < 11)
                        legacyGarageDetailUpgrades++;
                    if (!isOpen)
                        restoredClosedFacilityIds.Add(id);
                    maxId = Math.Max(maxId, id);
                }

                restoredNextId = maxId >= MaximumSerializedFacilities
                    ? 1
                    : maxId + 1;
                if (legacyGarageDetailUpgrades > 0)
                {
                    UndergroundParkingLog.Advanced(
                        "Assigned one-time persisted detail layouts to legacy underground garages: count="
                        + legacyGarageDetailUpgrades
                        + " rerollOnReload=False");
                }
            }

            Facilities.AddRange(restoredFacilities);
            ClosedFacilityIds.UnionWith(restoredClosedFacilityIds);
            _legacyParkingMigrated = restoredLegacyParkingMigrated;
            _nextId = restoredNextId;
            _lastStatus = Facilities.Count == 0
                ? "Ready."
                : "Restored " + Facilities.Count + " underground parking garage marker(s).";
            return Facilities.Count;
        }

        public static void RefreshSavedGeometry()
        {
            if (Facilities.Count == 0)
                return;

            for (int i = 0; i < Facilities.Count; i++)
                Facilities[i] = RefreshFacilityGeometry(Facilities[i]);

            _revision++;
            UndergroundParkingLog.Advanced("Refreshed underground parking garage geometry: count="
                                        + Facilities.Count
                                        + " capacity="
                                        + GetTotalCapacity());
        }

        internal static int ImportBuildingAttachmentsFromRebuildSnapshot(
            byte[] data,
            List<ImportedParkedAssignment> importedAssignments)
        {
            const int rebuildMagic = 0x55504752;
            const int minimumVersion = 4;
            const int maximumVersion = 10;
            const int maximumRecords = 32767;
            if (data == null || data.Length == 0)
                return 0;

            if (importedAssignments == null)
                throw new ArgumentNullException("importedAssignments");

            int imported = 0;
            int originalFacilityCount = Facilities.Count;
            int originalAssignmentCount = importedAssignments.Count;
            int originalNextId = _nextId;
            HashSet<int> originalClosedFacilityIds =
                new HashSet<int>(ClosedFacilityIds);
            try
            {
                using (MemoryStream stream = new MemoryStream(data, false))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != rebuildMagic)
                        throw new InvalidDataException("Data is not a UPG rebuild snapshot.");
                    int version = reader.ReadInt32();
                    if (version < minimumVersion || version > maximumVersion)
                        throw new InvalidDataException("Unsupported UPG rebuild snapshot version: " + version);
                    int count = reader.ReadInt32();
                    if (count < 0 || count > maximumRecords)
                        throw new InvalidDataException("UPG rebuild facility count is outside the supported range.");

                    for (int index = 0; index < count; index++)
                    {
                    reader.ReadInt32(); // Rebuild-local facility ID; remapped below.
                    byte kind = reader.ReadByte();
                    ushort surfaceSegmentId = reader.ReadUInt16();
                    float surfaceSegmentPosition = reader.ReadSingle();
                    Vector3 surfaceRoadPosition = ReadVector(reader);
                    Vector3 entrancePosition = ReadVector(reader);
                    Vector3 direction = ReadVector(reader);
                    Vector3 side = ReadVector(reader);
                    Vector3 garageCenter = ReadVector(reader);
                    Vector3 vehiclePortalPosition = ReadVector(reader);
                    Vector3 connectorStartPosition = ReadVector(reader);
                    ushort entranceBuildingId = reader.ReadUInt16();
                    ushort targetBuildingId = reader.ReadUInt16();
                    Vector3 garageForward = ReadVector(reader);
                    Vector3 garageRight = ReadVector(reader);
                    float garageWidth = reader.ReadSingle();
                    float garageLength = reader.ReadSingle();
                    int floorCount = reader.ReadInt32();
                    reader.ReadInt32(); // Rebuild spaces-per-floor; active geometry owns capacity.
                    byte operatingState = reader.ReadByte();

                    UndergroundParkingFacility existing;
                    int activeFacilityId = 0;
                    bool attachedRecord = kind == 2
                                          && targetBuildingId != 0
                                          && entranceBuildingId == 0
                                          && surfaceSegmentId != 0
                                          && IsFinite(surfaceSegmentPosition)
                                          && IsFinite(surfaceRoadPosition)
                                          && IsFinite(entrancePosition)
                                          && IsFinite(direction)
                                          && IsFinite(side)
                                          && IsFinite(garageCenter)
                                          && IsFinite(vehiclePortalPosition)
                                          && IsFinite(connectorStartPosition)
                                          && IsFinite(garageForward)
                                          && IsFinite(garageRight)
                                          && IsFinite(garageWidth)
                                          && IsFinite(garageLength)
                                          && garageWidth > 0f
                                          && garageLength > 0f;
                    string hostRejection;
                    if (attachedRecord
                        && !TryResolveImportedAttachedHost(
                            ref targetBuildingId,
                            entrancePosition,
                            garageCenter,
                            garageForward,
                            garageRight,
                            garageWidth,
                            garageLength,
                            floorCount,
                            out hostRejection))
                    {
                        attachedRecord = false;
                        UndergroundParkingLog.Warning(
                            "Rejected stale building-attached garage from rebuild snapshot: targetBuilding="
                            + targetBuildingId
                            + " reason="
                            + hostRejection);
                    }
                    if (attachedRecord && TryGetForTargetBuilding(targetBuildingId, out existing))
                    {
                        activeFacilityId = existing.Id;
                    }
                    else if (attachedRecord)
                    {
                        if (!TryAllocateFacilityId(out activeFacilityId))
                        {
                            UndergroundParkingLog.Warning(
                                "Rejected building-attached garage import because no safe facility ID remains: count="
                                + Facilities.Count);
                            activeFacilityId = 0;
                        }
                    }

                    if (attachedRecord && activeFacilityId != 0
                        && !TryGetForTargetBuilding(targetBuildingId, out existing))
                    {
                        UndergroundParkingFacility facility = new UndergroundParkingFacility(
                            activeFacilityId,
                            surfaceSegmentId,
                            surfaceSegmentPosition,
                            surfaceRoadPosition,
                            entrancePosition,
                            direction,
                            side,
                            garageCenter,
                            vehiclePortalPosition,
                            connectorStartPosition,
                            0,
                            0,
                            0,
                            0,
                            false,
                            0,
                            floorCount,
                            targetBuildingId,
                            garageForward,
                            garageRight,
                            garageWidth,
                            garageLength,
                            0,
                            true,
                            CreateGarageDetailVariant(
                                activeFacilityId,
                                surfaceSegmentId,
                                surfaceSegmentPosition,
                                targetBuildingId,
                                garageWidth,
                                garageLength));
                        Facilities.Add(facility);
                        if (operatingState != 0)
                            ClosedFacilityIds.Add(activeFacilityId);
                        imported++;
                        UndergroundParkingLog.Warning(
                            "Imported missing building-attached garage from rebuild snapshot: facility="
                            + activeFacilityId
                            + " targetBuilding="
                            + targetBuildingId);
                    }

                    int storedCount = reader.ReadInt32();
                    if (storedCount < 0 || storedCount > maximumRecords)
                        throw new InvalidDataException("UPG rebuild stored-vehicle count is outside the supported range.");
                    for (int storedIndex = 0; storedIndex < storedCount; storedIndex++)
                    {
                        ushort parkedId = reader.ReadUInt16();
                        string prefabName = reader.ReadString();
                        reader.ReadByte(); // Stored vehicle kind.
                        byte recordKind = reader.ReadByte();
                        reader.ReadUInt32(); // Owner citizen ID.
                        int slot = reader.ReadInt32();
                        reader.ReadUInt32(); // Stored frame.
                        reader.ReadUInt32(); // Retrievable-after frame.
                        if (activeFacilityId != 0
                            && parkedId != 0
                            && recordKind == 2
                            && slot >= 0
                            && prefabName.Length <= 256)
                        {
                            importedAssignments.Add(new ImportedParkedAssignment(
                                parkedId,
                                activeFacilityId,
                                slot,
                                prefabName));
                        }
                    }
                }
            }
            }
            catch
            {
                if (Facilities.Count > originalFacilityCount)
                {
                    Facilities.RemoveRange(
                        originalFacilityCount,
                        Facilities.Count - originalFacilityCount);
                }
                if (importedAssignments.Count > originalAssignmentCount)
                {
                    importedAssignments.RemoveRange(
                        originalAssignmentCount,
                        importedAssignments.Count - originalAssignmentCount);
                }
                ClosedFacilityIds.Clear();
                ClosedFacilityIds.UnionWith(originalClosedFacilityIds);
                _nextId = originalNextId;
                throw;
            }

            if (imported > 0)
                _revision++;
            UndergroundParkingLog.Advanced(
                "UPG rebuild-snapshot attached import complete: imported="
                + imported
                + " parkedAssignments="
                + importedAssignments.Count
                + " totalFacilities="
                + Facilities.Count);
            return imported;
        }

        private static bool TryAllocateFacilityId(out int facilityId)
        {
            facilityId = 0;
            if (Facilities.Count >= MaximumSerializedFacilities)
                return false;

            int candidate = _nextId;
            if (candidate <= 0 || candidate > MaximumSerializedFacilities)
                candidate = 1;

            if (!ContainsFacilityId(candidate))
            {
                facilityId = candidate;
                _nextId = candidate == MaximumSerializedFacilities
                    ? 1
                    : candidate + 1;
                return true;
            }

            HashSet<int> usedIds = new HashSet<int>();
            for (int i = 0; i < Facilities.Count; i++)
            {
                int existingId = Facilities[i].Id;
                if (existingId > 0 && existingId <= MaximumSerializedFacilities)
                    usedIds.Add(existingId);
            }

            for (candidate = 1;
                 candidate <= MaximumSerializedFacilities;
                 candidate++)
            {
                if (usedIds.Contains(candidate))
                    continue;

                facilityId = candidate;
                _nextId = candidate == MaximumSerializedFacilities
                    ? 1
                    : candidate + 1;
                return true;
            }

            return false;
        }

        private static bool ContainsFacilityId(int facilityId)
        {
            for (int i = 0; i < Facilities.Count; i++)
            {
                if (Facilities[i].Id == facilityId)
                    return true;
            }

            return false;
        }

        private static bool TryResolveImportedAttachedHost(
            ref ushort targetBuildingId,
            Vector3 entrancePosition,
            Vector3 savedGarageCenter,
            Vector3 savedGarageForward,
            Vector3 savedGarageRight,
            float savedGarageWidth,
            float savedGarageLength,
            int floorCount,
            out string rejection)
        {
            string directRejection;
            if (DoesImportedAttachedHostMatch(
                    targetBuildingId,
                    entrancePosition,
                    savedGarageCenter,
                    savedGarageForward,
                    savedGarageRight,
                    savedGarageWidth,
                    savedGarageLength,
                    floorCount,
                    out directRejection))
            {
                rejection = string.Empty;
                return true;
            }

            BuildingManager manager = BuildingManager.instance;
            if (manager == null)
            {
                rejection = directRejection;
                return false;
            }

            ushort resolvedBuildingId = 0;
            int matches = 0;
            for (int buildingIndex = 1;
                 buildingIndex < manager.m_buildings.m_size;
                 buildingIndex++)
            {
                ushort candidateId = (ushort)buildingIndex;
                if (candidateId == targetBuildingId)
                    continue;

                string ignored;
                if (!DoesImportedAttachedHostMatch(
                        candidateId,
                        entrancePosition,
                        savedGarageCenter,
                        savedGarageForward,
                        savedGarageRight,
                        savedGarageWidth,
                        savedGarageLength,
                        floorCount,
                        out ignored))
                {
                    continue;
                }

                resolvedBuildingId = candidateId;
                matches++;
                if (matches > 1)
                    break;
            }

            if (matches == 1)
            {
                UndergroundParkingLog.Warning(
                    "Remapped recovered building-attached garage to its unique live host: savedTarget="
                    + targetBuildingId
                    + " liveTarget="
                    + resolvedBuildingId);
                targetBuildingId = resolvedBuildingId;
                rejection = string.Empty;
                return true;
            }

            rejection = matches == 0
                ? directRejection + "; no-live-host-matches-saved-geometry"
                : "saved-host-is-ambiguous matches=" + matches;
            return false;
        }

        private static bool DoesImportedAttachedHostMatch(
            ushort targetBuildingId,
            Vector3 entrancePosition,
            Vector3 savedGarageCenter,
            Vector3 savedGarageForward,
            Vector3 savedGarageRight,
            float savedGarageWidth,
            float savedGarageLength,
            int floorCount,
            out string rejection)
        {
            Building building;
            if (!UndergroundParkingGeometry.TryGetUsableBuilding(targetBuildingId, out building)
                || UndergroundParkingBuildingPrefab.IsGaragePrefab(building.Info))
            {
                rejection = "saved-host-is-not-the-same-live-building";
                return false;
            }

            Vector3 expectedCenter = building.m_position;
            expectedCenter.y = UndergroundParkingGeometry.ResolveSurfaceHeight(building.m_position)
                               - UndergroundParkingGeometry.GetGarageCenterDepth(
                                   floorCount);
            Vector3 centerDelta = savedGarageCenter - expectedCenter;
            float horizontalCenterDistance = Mathf.Sqrt(
                centerDelta.x * centerDelta.x + centerDelta.z * centerDelta.z);
            if (horizontalCenterDistance > 1.5f || Mathf.Abs(centerDelta.y) > 2f)
            {
                rejection = "saved-host-centre-mismatch distance="
                            + horizontalCenterDistance.ToString("0.0")
                            + " heightDelta="
                            + centerDelta.y.ToString("0.0");
                return false;
            }

            Vector3 liveForward;
            Vector3 liveRight;
            UndergroundParkingGeometry.GetBuildingAxes(
                building.m_angle,
                out liveForward,
                out liveRight);
            Vector3 normalizedSavedForward = NormalizeFlat(savedGarageForward, liveForward);
            Vector3 normalizedSavedRight = NormalizeFlat(savedGarageRight, liveRight);
            if (Mathf.Abs(Vector3.Dot(normalizedSavedForward, liveForward)) < 0.995f
                || Mathf.Abs(Vector3.Dot(normalizedSavedRight, liveRight)) < 0.995f)
            {
                rejection = "saved-host-orientation-mismatch";
                return false;
            }

            float hostWidth = UndergroundParkingGeometry.GetBuildingWidth(building);
            float hostLength = UndergroundParkingGeometry.GetBuildingLength(building);
            float expectedWidth = hostWidth
                                  * UndergroundParkingGeometry.BuildingAttachedFootprintScale;
            float expectedLength = hostLength
                                   * UndergroundParkingGeometry.BuildingAttachedFootprintScale;
            if (Mathf.Abs(savedGarageWidth - expectedWidth) > 0.35f
                || Mathf.Abs(savedGarageLength - expectedLength) > 0.35f)
            {
                rejection = "saved-host-footprint-mismatch saved="
                            + savedGarageWidth.ToString("0.0")
                            + "x"
                            + savedGarageLength.ToString("0.0")
                            + " live="
                            + expectedWidth.ToString("0.0")
                            + "x"
                            + expectedLength.ToString("0.0");
                return false;
            }

            Vector3 entranceDelta = entrancePosition - building.m_position;
            entranceDelta.y = 0f;
            float halfWidth = hostWidth * 0.5f;
            float halfLength = hostLength * 0.5f;
            float lateralExcess = Mathf.Max(
                0f,
                Mathf.Abs(Vector3.Dot(entranceDelta, liveRight)) - halfWidth);
            float forwardExcess = Mathf.Max(
                0f,
                Mathf.Abs(Vector3.Dot(entranceDelta, liveForward)) - halfLength);
            float entranceDistanceFromFootprint = Mathf.Sqrt(
                lateralExcess * lateralExcess + forwardExcess * forwardExcess);
            if (entranceDistanceFromFootprint
                > UndergroundParkingGeometry.MaximumBuildingEntranceDistance)
            {
                rejection = "saved-entrance-too-far-from-host distance="
                            + entranceDistanceFromFootprint.ToString("0.0");
                return false;
            }

            rejection = string.Empty;
            return true;
        }

        private static Vector3 NormalizeFlat(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude < 0.0001f)
                return fallback;
            value.Normalize();
            return value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsSamePlacement(UndergroundParkingFacility existing, UndergroundParkingFacility candidate)
        {
            if (existing.SurfaceSegmentId != candidate.SurfaceSegmentId)
                return false;

            if (Vector3.Dot(existing.Side, candidate.Side) < 0.25f)
                return false;

            return Mathf.Abs(existing.SurfaceSegmentPosition - candidate.SurfaceSegmentPosition) <= SameFacilityPositionTolerance;
        }

        private static bool IsSameTargetBuilding(UndergroundParkingFacility existing, UndergroundParkingFacility candidate)
        {
            return candidate.TargetBuildingId != 0 && existing.TargetBuildingId == candidate.TargetBuildingId;
        }

        private static UndergroundParkingFacility RefreshFacilityGeometry(UndergroundParkingFacility facility)
        {
            Vector3 roadPosition;
            Vector3 entrancePosition;
            Vector3 direction;
            Vector3 side;
            if (!UndergroundParkingGeometry.TryGetCurrentPlacement(
                    facility,
                    out roadPosition,
                    out entrancePosition,
                    out direction,
                    out side))
            {
                return facility;
            }

            Vector3 garageCenter = facility.GarageCenter;
            Vector3 garageForward = facility.GarageForward;
            Vector3 garageRight = facility.GarageRight;
            float garageWidth = facility.GarageWidth;
            float garageLength = facility.GarageLength;
            Building targetBuilding;
            if (UndergroundParkingGeometry.TryGetUsableBuilding(facility.TargetBuildingId, out targetBuilding))
            {
                garageCenter = targetBuilding.m_position;
                garageCenter.y = UndergroundParkingGeometry.ResolveSurfaceHeight(targetBuilding.m_position)
                                 - UndergroundParkingGeometry.GetGarageCenterDepth(facility.FloorCount);
                UndergroundParkingGeometry.GetBuildingAxes(
                    targetBuilding.m_angle,
                    out garageForward,
                    out garageRight);
                garageWidth = UndergroundParkingGeometry.GetBuildingWidth(targetBuilding)
                              * UndergroundParkingGeometry.BuildingAttachedFootprintScale;
                garageLength = UndergroundParkingGeometry.GetBuildingLength(targetBuilding)
                               * UndergroundParkingGeometry.BuildingAttachedFootprintScale;
            }
            Vector3 vehicleNodePosition = UndergroundParkingGeometry.CalculateVehicleConnectionNodePosition(entrancePosition, side);
            return new UndergroundParkingFacility(
                facility.Id,
                facility.SurfaceSegmentId,
                facility.SurfaceSegmentPosition,
                roadPosition,
                entrancePosition,
                direction,
                side,
                garageCenter,
                vehicleNodePosition,
                roadPosition,
                facility.EntrancePropId,
                facility.ConnectorSegmentId,
                facility.ConnectorStartNodeId,
                facility.ConnectorEndNodeId,
                facility.ConnectorCreated,
                facility.EntranceBuildingId,
                facility.FloorCount, facility.TargetBuildingId, garageForward,
                garageRight, garageWidth, garageLength, facility.EntranceBackPropId,
                facility.EntranceVisualsEnabled, facility.GarageDetailVariant);
        }

        private static int CreateGarageDetailVariant(
            int facilityId,
            UndergroundParkingFacility facility)
        {
            uint frame = SimulationManager.instance == null
                ? 0u
                : SimulationManager.instance.m_currentFrameIndex;
            int variant = CreateGarageDetailVariant(
                facilityId,
                facility.SurfaceSegmentId,
                facility.SurfaceSegmentPosition,
                facility.TargetBuildingId,
                facility.GarageWidth,
                facility.GarageLength);
            return (variant ^ (int)(frame & 7u)) & 7;
        }

        private static int CreateGarageDetailVariant(
            int facilityId,
            ushort surfaceSegmentId,
            float surfaceSegmentPosition,
            ushort targetBuildingId,
            float garageWidth,
            float garageLength)
        {
            int seed = unchecked(facilityId * 73856093);
            seed ^= unchecked(surfaceSegmentId * 19349663);
            seed ^= unchecked(targetBuildingId * 83492791);
            seed ^= Mathf.RoundToInt(surfaceSegmentPosition * 10000f) * 31;
            seed ^= Mathf.RoundToInt(garageWidth * 10f) * 17;
            seed ^= Mathf.RoundToInt(garageLength * 10f) * 13;
            seed ^= seed >> 16;
            return seed & 7;
        }

        private static int GetTotalCapacity()
        {
            int capacity = 0;
            for (int i = 0; i < Facilities.Count; i++)
                capacity += UndergroundParkingGeometry.GetParkingSpaceCapacity(Facilities[i]);
            return capacity;
        }

        private static bool IsSameRegisteredBuilding(UndergroundParkingFacility existing, UndergroundParkingFacility candidate)
        {
            return existing.EntranceBuildingId != 0
                   && existing.EntranceBuildingId == candidate.EntranceBuildingId;
        }

        private static void ReleaseOwnedObjects(UndergroundParkingFacility facility)
        {
            RemovePendingEntranceRelocation(facility.Id);
            UndergroundParkingOccupancyManager.ReleaseFacility(facility);
            UndergroundParkingEntranceAnchorService.ReleaseAnchor(facility);
            UndergroundParkingConnectorCleanup.ReleaseConnector(facility);
        }

        internal static bool TrySetConnector(
            int facilityId,
            ushort segmentId,
            ushort startNodeId,
            ushort endNodeId,
            bool created)
        {
            for (int i = 0; i < Facilities.Count; i++)
            {
                if (Facilities[i].Id != facilityId)
                    continue;

                Facilities[i] = Facilities[i].WithConnector(
                    segmentId,
                    startNodeId,
                    endNodeId,
                    created);
                _revision++;
                return true;
            }

            return false;
        }

        private static void WriteVector(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static Vector3 ReadVector(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static string FormatVector(Vector3 value)
        {
            return "("
                   + value.x.ToString("0.0")
                   + ", "
                   + value.y.ToString("0.0")
                   + ", "
                   + value.z.ToString("0.0")
                   + ")";
        }
    }
}
