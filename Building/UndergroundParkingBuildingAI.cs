using ColossalFramework.Math;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal sealed class UndergroundParkingBuildingAI : ParkAI
    {
        private static readonly List<UndergroundParkingFacility> ZoningFacilities =
            new List<UndergroundParkingFacility>();
        private static readonly HashSet<ushort> ProtectedFacilityBuildings =
            new HashSet<ushort>();
        private static readonly HashSet<ushort> RetrospectiveBulldozeBuildings =
            new HashSet<ushort>();
        private static readonly MethodInfo ZoneBlockOverlapQuad = typeof(ZoneBlock).GetMethod(
            "OverlapQuad",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(Quad2) },
            null);

        public override void CreateBuilding(ushort buildingID, ref Building data)
        {
            base.CreateBuilding(buildingID, ref data);
            UndergroundParkingBuildingPrefab.RefreshBuildingSelection(buildingID);
            RegisterFacility(buildingID, ref data, "created");
        }

        public override void BuildingLoaded(ushort buildingID, ref Building data, uint version)
        {
            base.BuildingLoaded(buildingID, ref data, version);
            UndergroundParkingBuildingPrefab.RefreshBuildingSelection(buildingID);
            RegisterFacility(buildingID, ref data, "loaded");
        }

        public override void EndRelocating(ushort buildingID, ref Building data)
        {
            base.EndRelocating(buildingID, ref data);
            UndergroundParkingBuildingPrefab.RefreshBuildingSelection(buildingID);
            RegisterFacility(buildingID, ref data, "relocated");
        }

        public override void ReleaseBuilding(ushort buildingID, ref Building data)
        {
            UndergroundParkingRegistry.RemoveForBuilding(buildingID, "Bulldozed underground parking entrance.");
            base.ReleaseBuilding(buildingID, ref data);
        }

        public override ToolBase.ToolErrors CheckBulldozing(ushort buildingID, ref Building data)
        {
            ToolBase.ToolErrors errors = base.CheckBulldozing(buildingID, ref data);
            UndergroundParkingFacility facility;
            if (UndergroundParkingRegistry.TryGetForBuilding(buildingID, out facility)
                && (UndergroundParkingOccupancyManager.CountAssignedParkedCars(facility) > 0
                    || UndergroundParkingRegistry.HasFacilityActivity(facility.Id)))
            {
                errors |= ToolBase.ToolErrors.ObjectCollision;
            }

            return errors;
        }

        public override ToolBase.ToolErrors CheckBuildPosition(
            ushort relocateID,
            ref Vector3 position,
            ref float angle,
            float waterHeight,
            float elevation,
            ref Segment3 connectionSegment,
            out int productionRate,
            out int constructionCost)
        {
            ToolBase.ToolErrors errors = base.CheckBuildPosition(
                relocateID,
                ref position,
                ref angle,
                waterHeight,
                elevation,
                ref connectionSegment,
                out productionRate,
                out constructionCost);

            if ((errors & ToolBase.ToolErrors.VisibleErrors) == ToolBase.ToolErrors.None)
            {
                UndergroundParkingFacility facility;
                string message;
                if (!UndergroundParkingGeometry.TryCreateFacilityFromTerrainPosition(
                        position,
                        UndergroundParkingBuildingPrefab.GetVariant(m_info),
                        out facility,
                        out message))
                    errors |= ToolBase.ToolErrors.CannotBePlacedOnThisRoad;
                else if (UndergroundParkingRegistry.OverlapsGarageReservation(facility, relocateID))
                    errors |= ToolBase.ToolErrors.ObjectCollision;
            }

            return errors;
        }

        public override void GetPlacementInfoMode(
            out InfoManager.InfoMode mode,
            out InfoManager.SubInfoMode subMode,
            float elevation)
        {
            // The standalone kiosk is a normal visible roadside building.
            // Its P-tab tile must therefore place it in the ordinary world
            // view; Transport x-ray is reserved for the building-attached
            // placement tool whose underground footprint must be inspected.
            mode = InfoManager.InfoMode.None;
            subMode = InfoManager.SubInfoMode.Default;
        }

        public override bool RequireRoadAccess()
        {
            return false;
        }

        public override string GetLocalizedTooltip()
        {
            return UndergroundParkingStandaloneCatalog.Get(
                UndergroundParkingBuildingPrefab.GetVariant(m_info)).Title;
        }

        public override string GetLocalizedStats(ushort buildingID, ref Building data)
        {
            UndergroundParkingFacility facility = UndergroundParkingFacility.None;
            int parked = 0;
            int incoming = 0;
            if (UndergroundParkingFeatures.ParkingOccupancyEnabled
                && UndergroundParkingRegistry.TryGetForBuilding(buildingID, out facility))
            {
                UndergroundParkingOccupancyManager.GetFacilitySpaceCounts(
                    facility,
                    out parked,
                    out incoming);
            }

            return "Parked: "
                   + parked
                   + "\nIncoming: "
                   + incoming
                   + "\nCapacity: "
                   + (facility.IsValid
                       ? UndergroundParkingGeometry.GetParkingSpaceCapacity(facility)
                       : UndergroundParkingGeometry.GetParkingSpaceCapacity(
                           CreateFallbackFacility()))
                   + "\nFloors: "
                   + (facility.IsValid
                       ? facility.FloorCount
                       : UndergroundParkingStandaloneCatalog.Get(
                           UndergroundParkingBuildingPrefab.GetVariant(m_info))
                           .DefaultFloorCount)
                   + (UndergroundParkingFeatures.ParkingOccupancyEnabled
                       ? "\nAdditional floor: ₡25,000"
                       : "\nParking use is disabled by the feature flag.");
        }

        public override int GetResourceRate(ushort buildingID, ref Building data, EconomyManager.Resource resource)
        {
            int baseRate = base.GetResourceRate(buildingID, ref data, resource);
            if (resource != EconomyManager.Resource.Maintenance || baseRate == 0)
                return baseRate;

            UndergroundParkingFacility facility;
            if (!UndergroundParkingRegistry.TryGetForBuilding(buildingID, out facility))
                return baseRate;

            // One floor is 600/month (400 fixed + 200 floor). Preserve all
            // vanilla budget/policy scaling already present in baseRate.
            float multiplier = (400f + 200f * facility.FloorCount) / 600f;
            return baseRate < 0
                ? -Mathf.CeilToInt(-baseRate * multiplier)
                : Mathf.CeilToInt(baseRate * multiplier);
        }

        private static void RegisterFacility(ushort buildingID, ref Building data, string reason)
        {
            if (buildingID == 0 || (data.m_flags & Building.Flags.Created) == 0)
                return;

            UndergroundParkingFacility draft;
            string message;
            if (!UndergroundParkingGeometry.TryCreateFacilityFromTerrainPosition(
                    data.m_position,
                    UndergroundParkingBuildingPrefab.GetVariant(data.Info),
                    out draft,
                    out message))
            {
                UndergroundParkingLog.Warning("Unable to register underground parking building "
                                               + buildingID
                                               + " after "
                                               + reason
                                               + ": "
                                               + message);
                return;
            }

            bool replaced;
            string status;
            UndergroundParkingRegistry.AddOrReplaceFromBuilding(buildingID, draft, out replaced, out status);
            ClearZoningUnderEntrance(buildingID, ref data);
            UndergroundParkingBuildingPrefab.RefreshBuildingSelection(buildingID);
        }

        private UndergroundParkingFacility CreateFallbackFacility()
        {
            UndergroundParkingStandaloneSpec spec =
                UndergroundParkingStandaloneCatalog.Get(
                    UndergroundParkingBuildingPrefab.GetVariant(m_info));
            return new UndergroundParkingFacility(
                1,
                1,
                0f,
                Vector3.zero,
                Vector3.zero,
                Vector3.forward,
                Vector3.right,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                0,
                0,
                0,
                0,
                false,
                0,
                spec.DefaultFloorCount,
                0,
                Vector3.right,
                Vector3.forward,
                spec.GarageWidth,
                spec.GarageLength);
        }

        private static void ClearZoningUnderEntrance(ushort buildingId, ref Building data)
        {
            ZoneManager manager = ZoneManager.instance;
            if (manager == null || manager.m_blocks.m_buffer == null)
                return;

            BuildingInfo info = data.Info;
            if (info == null)
                return;

            // Use the placed building's exact vanilla transform. Reconstructed
            // road/facility axes can differ by 90 degrees from BuildingTool's
            // finalized roadside rotation and delete the neighbouring cells.
            Vector3 footprintCenter = data.m_position;
            ZoneBlock[] blocks = manager.m_blocks.m_buffer;
            int changedCells = 0;
            int changedBlocks = 0;
            for (ushort blockId = 1; blockId < blocks.Length; blockId++)
            {
                ZoneBlock block = blocks[blockId];
                if (block.m_flags == 0 || block.m_valid == 0)
                    continue;

                Vector3 blockDelta = block.m_position - footprintCenter;
                blockDelta.y = 0f;
                if (blockDelta.sqrMagnitude > 80f * 80f)
                    continue;

                ulong footprintMask = GetEntranceZoningExclusionMask(
                    ref block,
                    ref data,
                    info);
                if (footprintMask == 0UL)
                    continue;

                int deleted = DeleteMaskedFootprintCells(
                    ref block,
                    footprintMask,
                    true);
                if (deleted == 0)
                    continue;

                blocks[blockId] = block;
                manager.UpdateBlock(blockId);
                changedCells += deleted;
                changedBlocks++;
            }

            UndergroundParkingLog.Advanced("Deleted zoning cells beneath exact UPG kiosk footprint: cells="
                                        + changedCells
                                        + " blocks="
                                        + changedBlocks
                                        + " building="
                                        + buildingId
                                        + " angle="
                                        + data.m_angle.ToString("0.000"));
        }

        internal static void ApplyRetrospectiveZoningRepair()
        {
            BuildingManager buildingManager = BuildingManager.instance;
            if (buildingManager == null
                || buildingManager.m_buildings.m_buffer == null)
            {
                return;
            }

            int facilityCount = UndergroundParkingRegistry.CopyTo(ZoningFacilities);
            ProtectedFacilityBuildings.Clear();
            RetrospectiveBulldozeBuildings.Clear();
            for (int i = 0; i < facilityCount; i++)
            {
                UndergroundParkingFacility facility = ZoningFacilities[i];
                if (facility.EntranceBuildingId != 0)
                    ProtectedFacilityBuildings.Add(facility.EntranceBuildingId);
                if (facility.TargetBuildingId != 0)
                    ProtectedFacilityBuildings.Add(facility.TargetBuildingId);
            }

            int repairedEntrances = 0;
            int buildingLimit = Mathf.Min(
                (int)buildingManager.m_buildings.m_size,
                ushort.MaxValue + 1);
            for (int i = 0; i < facilityCount; i++)
            {
                UndergroundParkingFacility facility = ZoningFacilities[i];
                ushort entranceId = facility.EntranceBuildingId;
                if (facility.TargetBuildingId != 0
                    || entranceId == 0
                    || entranceId >= buildingManager.m_buildings.m_size)
                {
                    continue;
                }

                ref Building entrance =
                    ref buildingManager.m_buildings.m_buffer[entranceId];
                BuildingInfo entranceInfo = entrance.Info;
                if ((entrance.m_flags & Building.Flags.Created) == 0
                    || (entrance.m_flags & Building.Flags.Deleted) != 0
                    || !UndergroundParkingBuildingPrefab.IsGaragePrefab(entranceInfo))
                {
                    continue;
                }

                Quad2 footprint = CreateEntranceFootprint(
                    ref entrance,
                    entranceInfo);
                for (int candidateIndex = 1;
                     candidateIndex < buildingLimit;
                     candidateIndex++)
                {
                    ushort candidateId = (ushort)candidateIndex;
                    if (ProtectedFacilityBuildings.Contains(candidateId))
                        continue;

                    ref Building candidate =
                        ref buildingManager.m_buildings.m_buffer[candidateId];
                    if ((candidate.m_flags & Building.Flags.Created) == 0
                        || (candidate.m_flags & Building.Flags.Deleted) != 0)
                    {
                        continue;
                    }

                    BuildingInfo candidateInfo = candidate.Info;
                    if (candidateInfo == null
                        || UndergroundParkingBuildingPrefab.IsGaragePrefab(candidateInfo))
                    {
                        continue;
                    }

                    ItemClass.CollisionType collisionType =
                        candidateInfo.m_buildingAI == null
                            ? ItemClass.CollisionType.Terrain
                            : candidateInfo.m_buildingAI.GetCollisionType();
                    if (!candidate.OverlapQuad(
                            candidateId,
                            footprint,
                            entrance.m_position.y - 1024f,
                            entrance.m_position.y + 1024f,
                            collisionType))
                    {
                        continue;
                    }

                    ushort releaseId = candidate.m_parentBuilding != 0
                        ? candidate.m_parentBuilding
                        : candidateId;
                    if (!ProtectedFacilityBuildings.Contains(releaseId))
                        RetrospectiveBulldozeBuildings.Add(releaseId);
                }

            }

            int bulldozed = 0;
            foreach (ushort buildingId in RetrospectiveBulldozeBuildings)
            {
                if (buildingId == 0
                    || buildingId >= buildingManager.m_buildings.m_size
                    || ProtectedFacilityBuildings.Contains(buildingId))
                {
                    continue;
                }

                ref Building building =
                    ref buildingManager.m_buildings.m_buffer[buildingId];
                if ((building.m_flags & Building.Flags.Created) == 0
                    || (building.m_flags & Building.Flags.Deleted) != 0
                    || UndergroundParkingBuildingPrefab.IsGaragePrefab(building.Info))
                {
                    continue;
                }

                buildingManager.ReleaseBuilding(buildingId);
                bulldozed++;
            }

            for (int i = 0; i < facilityCount; i++)
            {
                UndergroundParkingFacility facility = ZoningFacilities[i];
                ushort entranceId = facility.EntranceBuildingId;
                if (facility.TargetBuildingId != 0
                    || entranceId == 0
                    || entranceId >= buildingManager.m_buildings.m_size)
                {
                    continue;
                }

                ref Building entrance =
                    ref buildingManager.m_buildings.m_buffer[entranceId];
                if ((entrance.m_flags & Building.Flags.Created) == 0
                    || (entrance.m_flags & Building.Flags.Deleted) != 0
                    || !UndergroundParkingBuildingPrefab.IsGaragePrefab(entrance.Info))
                {
                    continue;
                }

                ClearZoningUnderEntrance(entranceId, ref entrance);
                repairedEntrances++;
            }

            UndergroundParkingLog.Warning(
                "Applied retrospective standalone entrance zoning repair: entrances="
                + repairedEntrances
                + " overlappingBuildingsBulldozed="
                + bulldozed);
        }

        internal static void PreventZoneBlockOverRegisteredEntrances(
            ushort blockId,
            ref ZoneBlock block)
        {
            if (blockId == 0 || block.m_flags == 0 || block.m_valid == 0)
                return;

            BuildingManager buildingManager = BuildingManager.instance;
            if (buildingManager == null)
                return;

            int facilityCount = UndergroundParkingRegistry.CopyTo(ZoningFacilities);
            int deleted = 0;
            for (int i = 0; i < facilityCount; i++)
            {
                ushort buildingId = ZoningFacilities[i].EntranceBuildingId;
                if (buildingId == 0 || buildingId >= buildingManager.m_buildings.m_size)
                    continue;

                Building building = buildingManager.m_buildings.m_buffer[buildingId];
                if ((building.m_flags & Building.Flags.Created) == 0
                    || (building.m_flags & Building.Flags.Deleted) != 0
                    || !UndergroundParkingBuildingPrefab.IsGaragePrefab(building.Info))
                {
                    continue;
                }

                deleted += DeleteFootprintCellsFromBlock(ref block, ref building);
            }

            if (deleted > 0)
            {
                UndergroundParkingLog.Advanced("Prevented new zoning over UPG entrance: block="
                                            + blockId
                                            + " cells="
                                            + deleted);
            }
        }

        private static int DeleteFootprintCellsFromBlock(ref ZoneBlock block, ref Building building)
        {
            BuildingInfo info = building.Info;
            if (info == null)
                return 0;

            Vector3 blockDelta = block.m_position - building.m_position;
            blockDelta.y = 0f;
            if (blockDelta.sqrMagnitude > 80f * 80f)
                return 0;

            ulong footprintMask = GetEntranceZoningExclusionMask(
                ref block,
                ref building,
                info);
            if (footprintMask == 0UL)
                return 0;

            return DeleteMaskedFootprintCells(ref block, footprintMask, false);
        }

        private static int DeleteMaskedFootprintCells(
            ref ZoneBlock block,
            ulong footprintMask,
            bool reportOutOfSync)
        {
            int deleted = 0;
            // Vanilla ZoneBlock stores up to eight rows along the road. Each
            // row owns four depth cells, with the bit index row * 8 + depth;
            // SetZone receives those same coordinates as (depth, row).
            for (int row = 0; row < 8; row++)
            {
                for (int depth = 0; depth < 4; depth++)
                {
                    int cellIndex = row * 8 + depth;
                    ulong cellMask = 1UL << cellIndex;
                    if ((block.m_valid & cellMask) == 0UL
                        || (footprintMask & cellMask) == 0UL)
                        continue;

                    try
                    {
                        block.SetZone(depth, row, ItemClass.Zone.Unzoned);
                    }
                    catch (InvalidOperationException e)
                    {
                        if (reportOutOfSync)
                        {
                            UndergroundParkingLog.Warning(
                                "Skipped out-of-sync zoning cell under UPG entrance: "
                                + e.Message);
                        }
                    }

                    // Unzoning alone is not authoritative: the road's zone
                    // block can retain/recalculate the cell as buildable.
                    // Delete the exact footprint cell from the block completely.
                    block.m_valid &= ~cellMask;
                    block.m_shared &= ~cellMask;
                    block.m_occupied1 &= ~cellMask;
                    block.m_occupied2 &= ~cellMask;
                    deleted++;
                }
            }

            return deleted;
        }

        private static ulong GetEntranceZoningExclusionMask(
            ref ZoneBlock block,
            ref Building building,
            BuildingInfo info)
        {
            Quad2 footprint = CreateEntranceFootprint(ref building, info);
            if (ZoneBlockOverlapQuad == null)
                return 0UL;

            object result = ZoneBlockOverlapQuad.Invoke(block, new object[] { footprint });
            return result is ulong ? (ulong)result : 0UL;
        }

        private static Quad2 CreateEntranceFootprint(
            ref Building building,
            BuildingInfo info)
        {
            Vector2 widthDirection = new Vector2(
                Mathf.Cos(building.m_angle),
                Mathf.Sin(building.m_angle));
            Vector2 lengthDirection = new Vector2(
                widthDirection.y,
                -widthDirection.x);
            // Runtime prefab variants are authoritative for their footprint.
            // Building.Width/Length can retain stale encoded dimensions, so
            // use the selected prefab's exact cell matrix and vanilla
            // ZoneBlock.OverlapQuad cell geometry.
            float halfWidth = Mathf.Max(
                0f,
                info.m_cellWidth * 4f);
            float halfLength = Mathf.Max(
                0f,
                info.m_cellLength * 4f);
            Vector2 width = widthDirection * halfWidth;
            Vector2 length = lengthDirection * halfLength;
            Vector2 center = new Vector2(building.m_position.x, building.m_position.z);
            Quad2 footprint = new Quad2
            {
                a = center - width - length,
                b = center + width - length,
                c = center + width + length,
                d = center - width + length
            };
            return footprint;
        }
    }
}
