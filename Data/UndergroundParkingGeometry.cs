using ColossalFramework.Math;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingGeometry
    {
        public const float BuildingCellSize = 8f;
        public const float EntranceLotWidth = BuildingCellSize * 2f;
        public const float EntranceLotLength = BuildingCellSize * 3f;
        public const float GarageWidth = 50f;
        public const float GarageLength = 25f;
        public const int DefaultFloorCount = 1;
        public const int MaxFloorCount = 5;
        public const float GarageFloorHeight = 8.5f;
        public const float SquareMetersPerParkingSpace = 32f;
        public const int ParkingSpacesPerFloor = 39;
        public const float GarageTopDepth = 5f;
        public const float GarageSetback = GarageLength * 0.5f;
        public const float MaximumBuildingEntranceDistance = 50f;
        public const float BuildingAttachedFootprintScale = 0.9f;
        public const float ParkingSlotWidth = 3.5f;
        public const float ParkingSlotLength = 6f;
        public const float ParkingSlotEdgePadding = 1.6f;

        private const int BezierSampleCount = 36;
        private const int NetSegmentGridResolution = 270;
        private const float NetSegmentGridCellSize = 64f;
        private const float NetSegmentGridHalfResolution = 135f;
        private const int MaxSegmentGridChainIterations = 32768;
        private const float DefaultRoadHalfWidth = 8f;
        private const float EntranceRoadGap = 0.7f;
        private const float PavementPitInset = 2.05f;
        private const float EntranceSurfaceLift = 0.08f;
        private const float RoadSnapSearchRadius = 48f;
        private const float MaxRoadSnapDistance = 42f;

        private static readonly System.Collections.Generic.List<ushort> CandidateSegments =
            new System.Collections.Generic.List<ushort>(64);

        public static bool TryCreateFacility(ushort segmentId, Vector3 hitPosition, out UndergroundParkingFacility facility, out string message)
        {
            return TryCreateFacilityFromEntranceCenter(
                segmentId,
                hitPosition,
                UndergroundParkingStandaloneCatalog.Get(
                    UndergroundParkingStandaloneVariant.Compact),
                out facility,
                out message);
        }

        private static bool TryCreateFacilityFromEntranceCenter(
            ushort segmentId,
            Vector3 entranceCenter,
            UndergroundParkingStandaloneSpec spec,
            out UndergroundParkingFacility facility,
            out string message)
        {
            facility = UndergroundParkingFacility.None;
            message = string.Empty;

            NetManager netManager = NetManager.instance;
            if (netManager == null
                || segmentId == 0
                || segmentId >= netManager.m_segments.m_size)
            {
                message = "Select an above-ground road segment.";
                return false;
            }

            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == 0
                || segment.Info == null
                || !(segment.Info.m_netAI is RoadBaseAI))
            {
                message = "Underground parking entrances must face a road.";
                return false;
            }

            if (segment.m_startNode == 0
                || segment.m_endNode == 0
                || segment.m_startNode >= netManager.m_nodes.m_size
                || segment.m_endNode >= netManager.m_nodes.m_size)
            {
                message = "Unable to resolve the selected road.";
                return false;
            }

            if (IsUndergroundNode(netManager, segment.m_startNode) || IsUndergroundNode(netManager, segment.m_endNode))
            {
                message = "Place the pedestrian entrance against an above-ground road.";
                return false;
            }

            Bezier3 bezier = GetSegmentBezier(netManager, ref segment);
            float segmentPosition = FindClosestPositionOnBezier(bezier, entranceCenter);
            Vector3 roadPosition = bezier.Position(segmentPosition);

            Vector3 direction = GetBezierDirection(bezier, segmentPosition);
            if (direction.sqrMagnitude <= 0.001f)
            {
                message = "Unable to resolve road direction.";
                return false;
            }

            direction.Normalize();
            Vector3 right = new Vector3(-direction.z, 0f, direction.x);
            Vector3 toEntrance = entranceCenter - roadPosition;
            toEntrance.y = 0f;
            Vector3 side = Vector3.Dot(toEntrance, right) >= 0f ? right : -right;

            roadPosition.y = ResolveSurfaceHeight(roadPosition) + EntranceSurfaceLift;

            Vector3 entrancePosition = entranceCenter;
            entrancePosition.y = ResolveSurfaceHeight(entrancePosition) + EntranceSurfaceLift;

            Vector3 garageCenter = CalculateGarageCenter(
                entrancePosition,
                side,
                spec.DefaultFloorCount,
                spec.GarageLength);

            Vector3 vehicleNodePosition = CalculateVehicleConnectionNodePosition(entrancePosition, side);

            Vector3 connectorStartPosition = roadPosition;

            facility = new UndergroundParkingFacility(
                0,
                segmentId,
                segmentPosition,
                roadPosition,
                entrancePosition,
                direction,
                side,
                garageCenter,
                vehicleNodePosition,
                connectorStartPosition,
                0,
                0,
                0,
                0,
                false,
                0,
                spec.DefaultFloorCount,
                0,
                side,
                new Vector3(side.z, 0f, -side.x),
                spec.GarageWidth,
                spec.GarageLength);
            return true;
        }

        public static bool TryCreateFacilityFromTerrainPosition(Vector3 terrainPosition, out UndergroundParkingFacility facility, out string message)
        {
            return TryCreateFacilityFromTerrainPosition(
                terrainPosition,
                UndergroundParkingStandaloneVariant.Compact,
                out facility,
                out message);
        }

        public static bool TryCreateFacilityFromTerrainPosition(
            Vector3 terrainPosition,
            UndergroundParkingStandaloneVariant variant,
            out UndergroundParkingFacility facility,
            out string message)
        {
            facility = UndergroundParkingFacility.None;
            message = string.Empty;

            ushort segmentId;
            Vector3 roadPosition;
            if (!TryFindNearestSurfaceRoad(terrainPosition, out segmentId, out roadPosition))
            {
                UndergroundParkingStandaloneSpec missingRoadSpec =
                    UndergroundParkingStandaloneCatalog.Get(variant);
                message = "Move the "
                          + missingRoadSpec.CellWidth
                          + "x"
                          + missingRoadSpec.CellLength
                          + " entrance footprint next to an above-ground road.";
                return false;
            }

            return TryCreateFacilityFromEntranceCenter(
                segmentId,
                terrainPosition,
                UndergroundParkingStandaloneCatalog.Get(variant),
                out facility,
                out message);
        }

        public static bool TryCreateFacilityForBuilding(
            ushort buildingId,
            Vector3 terrainPosition,
            out UndergroundParkingFacility facility,
            out string message)
        {
            facility = UndergroundParkingFacility.None;
            Building building;
            if (!TryGetUsableBuilding(buildingId, out building))
            {
                message = "Select a placed building first.";
                return false;
            }

            UndergroundParkingFacility roadDraft;
            if (!TryCreateFacilityFromTerrainPosition(terrainPosition, out roadDraft, out message))
                return false;

            Vector3 pavementPosition;
            Vector3 pavementDirection;
            Vector3 pavementSide;
            if (!TryGetPavementPitPlacement(
                    roadDraft,
                    out pavementPosition,
                    out pavementDirection,
                    out pavementSide)
                || !TryCreateFacilityFromEntranceCenter(
                    roadDraft.SurfaceSegmentId,
                    pavementPosition,
                    UndergroundParkingStandaloneCatalog.Get(
                        UndergroundParkingStandaloneVariant.Compact),
                    out roadDraft,
                    out message))
            {
                message = "Choose a clear pavement point beside an above-ground road.";
                return false;
            }

            Vector3 forward;
            Vector3 right;
            GetBuildingAxes(building.m_angle, out forward, out right);
            float hostWidth = GetBuildingWidth(building);
            float hostLength = GetBuildingLength(building);
            float width = hostWidth * BuildingAttachedFootprintScale;
            float length = hostLength * BuildingAttachedFootprintScale;
            Vector3 roadToBuilding = building.m_position - roadDraft.SurfaceRoadPosition;
            roadToBuilding.y = 0f;
            if (Vector3.Dot(roadToBuilding, roadDraft.Side) <= 0f)
            {
                message = "Choose an entrance on the same side of the road as the selected building.";
                return false;
            }
            if (DistanceFromFootprint(building.m_position, right, forward, hostWidth, hostLength, roadDraft.EntrancePosition)
                > MaximumBuildingEntranceDistance)
            {
                message = "Choose a road entrance no more than 50 m from the selected building.";
                return false;
            }

            Vector3 garageCenter = building.m_position;
            garageCenter.y = ResolveSurfaceHeight(building.m_position) - GetGarageCenterDepth(DefaultFloorCount);
            Vector3 toGarage = garageCenter - roadDraft.EntrancePosition;
            toGarage.y = 0f;
            Vector3 tunnelDirection = NormalizeFlat(toGarage, roadDraft.Side);

            facility = new UndergroundParkingFacility(
                0, roadDraft.SurfaceSegmentId, roadDraft.SurfaceSegmentPosition,
                roadDraft.SurfaceRoadPosition, roadDraft.EntrancePosition,
                roadDraft.Direction, tunnelDirection, garageCenter,
                roadDraft.VehicleNodePosition, roadDraft.ConnectorStartPosition,
                0, 0, 0, 0, false, 0, DefaultFloorCount,
                buildingId, forward, right, width, length, 0,
                !UndergroundParkingGarageSettings.SuppressAttachedEntranceVisuals);
            message = string.Empty;
            return true;
        }

        public static bool TryGetUsableBuilding(ushort buildingId, out Building building)
        {
            building = default(Building);
            BuildingManager manager = BuildingManager.instance;
            if (manager == null || buildingId == 0 || buildingId >= manager.m_buildings.m_size)
                return false;

            building = manager.m_buildings.m_buffer[buildingId];
            Building.Flags terminalHostFlags = Building.Flags.Abandoned
                                               | Building.Flags.BurnedDown
                                               | Building.Flags.Collapsed
                                               | Building.Flags.Demolishing;
            return (building.m_flags & Building.Flags.Created) != 0
                   && (building.m_flags & (Building.Flags.Deleted | Building.Flags.Hidden)) == 0
                   && (building.m_flags & terminalHostFlags) == 0
                   && building.Info != null;
        }

        public static float GetBuildingWidth(Building building)
        {
            return Mathf.Max(BuildingCellSize,
                Mathf.Max(building.Info.m_size.x, building.Info.m_cellWidth * BuildingCellSize));
        }

        public static float GetBuildingLength(Building building)
        {
            return Mathf.Max(BuildingCellSize,
                Mathf.Max(building.Info.m_size.z, building.Info.m_cellLength * BuildingCellSize));
        }

        public static void GetBuildingAxes(float angle, out Vector3 forward, out Vector3 right)
        {
            float sin = Mathf.Sin(angle);
            float cos = Mathf.Cos(angle);
            // Match the vanilla Building footprint axes used by zoning and
            // collision code: width=(cos,sin), length=(sin,-cos) in XZ.
            right = new Vector3(cos, 0f, sin);
            forward = new Vector3(sin, 0f, -cos);
        }

        private static float DistanceFromFootprint(Vector3 center, Vector3 right, Vector3 forward,
            float width, float length, Vector3 point)
        {
            Vector3 delta = point - center;
            delta.y = 0f;
            float dx = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, right)) - width * 0.5f);
            float dz = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, forward)) - length * 0.5f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool TryGetCurrentPlacement(
            UndergroundParkingFacility facility,
            out Vector3 roadPosition,
            out Vector3 entrancePosition,
            out Vector3 direction,
            out Vector3 side)
        {
            roadPosition = facility.SurfaceRoadPosition;
            entrancePosition = facility.EntrancePosition;
            direction = facility.Direction;
            side = facility.Side;

            // Placement drafts intentionally have Id == 0 until the registry
            // commits them.  A resolved surface segment is sufficient here.
            if (facility.SurfaceSegmentId == 0)
                return false;

            NetManager netManager = NetManager.instance;
            if (netManager == null
                || facility.SurfaceSegmentId >= netManager.m_segments.m_size)
                return false;

            NetSegment segment = netManager.m_segments.m_buffer[facility.SurfaceSegmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == 0
                || segment.Info == null
                || segment.m_startNode == 0
                || segment.m_endNode == 0)
            {
                return false;
            }

            Bezier3 bezier = GetSegmentBezier(netManager, ref segment);
            roadPosition = bezier.Position(Mathf.Clamp01(facility.SurfaceSegmentPosition));
            roadPosition.y = ResolveSurfaceHeight(roadPosition) + EntranceSurfaceLift;

            direction = GetBezierDirection(bezier, facility.SurfaceSegmentPosition);
            if (direction.sqrMagnitude <= 0.001f)
                direction = facility.Direction;

            direction.y = 0f;
            direction.Normalize();

            entrancePosition = facility.EntrancePosition;
            entrancePosition.y = ResolveSurfaceHeight(entrancePosition) + EntranceSurfaceLift;

            side = entrancePosition - roadPosition;
            side.y = 0f;
            if (side.sqrMagnitude <= 0.001f)
                side = facility.Side;

            side = NormalizeFlat(side, new Vector3(-direction.z, 0f, direction.x));
            return true;
        }

        public static bool TryGetPavementPitPlacement(
            UndergroundParkingFacility facility,
            out Vector3 pitPosition,
            out Vector3 direction,
            out Vector3 side)
        {
            Vector3 roadPosition;
            Vector3 entrancePosition;
            pitPosition = Vector3.zero;
            if (!TryGetCurrentPlacement(facility, out roadPosition, out entrancePosition, out direction, out side))
                return false;

            NetManager netManager = NetManager.instance;
            if (netManager == null
                || facility.SurfaceSegmentId == 0
                || facility.SurfaceSegmentId >= netManager.m_segments.m_size)
            {
                return false;
            }

            NetSegment segment = netManager.m_segments.m_buffer[facility.SurfaceSegmentId];
            float roadHalfWidth = EstimateRoadHalfWidth(segment.Info);
            float offset = Mathf.Clamp(roadHalfWidth + PavementPitInset, 5f, 35f);
            pitPosition = roadPosition + side * offset;
            pitPosition.y = ResolveSurfaceHeight(pitPosition) + EntranceSurfaceLift;
            return true;
        }

        public static Vector3 CalculateGarageCenter(Vector3 entrancePosition, Vector3 side)
        {
            return CalculateGarageCenter(entrancePosition, side, DefaultFloorCount);
        }

        public static Vector3 ResolveCurrentVisualGarageCenter(
            UndergroundParkingFacility facility)
        {
            if (facility.TargetBuildingId != 0)
                return facility.GarageCenter;

            Vector3 roadPosition;
            Vector3 entrancePosition;
            Vector3 direction;
            Vector3 side;
            return TryGetCurrentPlacement(
                facility,
                out roadPosition,
                out entrancePosition,
                out direction,
                out side)
                ? CalculateGarageCenter(
                    entrancePosition,
                    side,
                    facility.FloorCount,
                    facility.GarageLength)
                : facility.GarageCenter;
        }

        public static Vector3 CalculateGarageCenter(Vector3 entrancePosition, Vector3 side, int floorCount)
        {
            return CalculateGarageCenter(
                entrancePosition,
                side,
                floorCount,
                GarageLength);
        }

        public static Vector3 CalculateGarageCenter(
            Vector3 entrancePosition,
            Vector3 side,
            int floorCount,
            float garageLength)
        {
            side.y = 0f;
            if (side.sqrMagnitude <= 0.001f)
                side = Vector3.right;

            side.Normalize();
            Vector3 garageCenter = entrancePosition
                                   + side * (Mathf.Max(8f, garageLength) * 0.5f);
            garageCenter.y = ResolveSurfaceHeight(garageCenter) - GetGarageCenterDepth(floorCount);
            return garageCenter;
        }

        public static int ClampFloorCount(int floorCount)
        {
            return Mathf.Clamp(floorCount, DefaultFloorCount, MaxFloorCount);
        }

        public static float GetGarageHeight(int floorCount)
        {
            return GarageFloorHeight * ClampFloorCount(floorCount);
        }

        public static float GetGarageCenterDepth(int floorCount)
        {
            return GarageTopDepth + GetGarageHeight(floorCount) * 0.5f;
        }

        public static int GetParkingSpaceCapacity(int floorCount)
        {
            return ParkingSpacesPerFloor * ClampFloorCount(floorCount);
        }

        public static int GetParkingSpaceCapacity(UndergroundParkingFacility facility)
        {
            return GetParkingSpaceCapacity(facility, facility.FloorCount);
        }

        public static int GetParkingSpaceCapacity(UndergroundParkingFacility facility, int floorCount)
        {
            float usableWidth = Mathf.Max(ParkingSlotWidth,
                facility.GarageWidth - ParkingSlotEdgePadding * 2f);
            float usableLength = Mathf.Max(ParkingSlotLength,
                facility.GarageLength - ParkingSlotEdgePadding * 2f);
            int columns = Mathf.Max(1, Mathf.FloorToInt(usableWidth / ParkingSlotWidth));
            int rows = Mathf.Max(1, Mathf.FloorToInt(usableLength / ParkingSlotLength));
            return columns * rows * ClampFloorCount(floorCount);
        }

        public static int GetMinimumFloorCount(UndergroundParkingFacility facility)
        {
            if (facility.TargetBuildingId != 0)
                return DefaultFloorCount;

            return UndergroundParkingStandaloneCatalog.Get(
                UndergroundParkingStandaloneCatalog.FromFacility(facility))
                .MinimumFloorCount;
        }

        public static int GetMaximumFloorCount(UndergroundParkingFacility facility)
        {
            return facility.TargetBuildingId != 0
                ? Mathf.Min(MaxFloorCount, DefaultFloorCount + 1)
                : MaxFloorCount;
        }

        public static Vector3 CalculateVehicleConnectionNodePosition(Vector3 entrancePosition, Vector3 side)
        {
            Vector3 nodePosition = entrancePosition;
            nodePosition.y = ResolveSurfaceHeight(nodePosition) + EntranceSurfaceLift;
            return nodePosition;
        }

        public static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (dx * dx) + (dz * dz);
        }

        public static bool IsInsideGarageFootprint(UndergroundParkingFacility facility, Vector3 position, float padding)
        {
            Vector3 garageForward = NormalizeFlat(facility.GarageForward, facility.Side);
            Vector3 garageRight = NormalizeFlat(facility.GarageRight, new Vector3(garageForward.z, 0f, -garageForward.x));
            Vector3 delta = position - facility.GarageCenter;
            delta.y = 0f;

            return Mathf.Abs(Vector3.Dot(delta, garageRight)) <= (facility.GarageWidth * 0.5f) + padding
                   && Mathf.Abs(Vector3.Dot(delta, garageForward)) <= (facility.GarageLength * 0.5f) + padding;
        }

        public static bool GarageFootprintOverlapsRect(
            UndergroundParkingFacility facility,
            Vector3 center,
            Vector3 rectRight,
            Vector3 rectForward,
            float rectHalfWidth,
            float rectHalfLength,
            float padding)
        {
            Vector3 garageForward = NormalizeFlat(facility.GarageForward, facility.Side);
            Vector3 garageRight = NormalizeFlat(facility.GarageRight, new Vector3(garageForward.z, 0f, -garageForward.x));
            rectRight = NormalizeFlat(rectRight, Vector3.right);
            rectForward = NormalizeFlat(rectForward, Vector3.forward);

            return RectsOverlapXZ(
                facility.GarageCenter,
                garageRight,
                garageForward,
                (facility.GarageWidth * 0.5f) + padding,
                (facility.GarageLength * 0.5f) + padding,
                center,
                rectRight,
                rectForward,
                rectHalfWidth,
                rectHalfLength);
        }

        public static bool GarageFootprintIntersectsSegment(
            UndergroundParkingFacility facility,
            Vector3 start,
            Vector3 end,
            float padding)
        {
            Vector3 delta = end - start;
            delta.y = 0f;
            float length = delta.magnitude;
            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(length / 4f), 1, 64);

            for (int i = 0; i <= sampleCount; i++)
            {
                Vector3 point = Vector3.Lerp(start, end, i / (float)sampleCount);
                if (IsInsideGarageFootprint(facility, point, padding))
                    return true;
            }

            return false;
        }

        private static bool IsUndergroundNode(NetManager netManager, ushort nodeId)
        {
            if (nodeId == 0 || nodeId >= netManager.m_nodes.m_size)
                return false;

            return (netManager.m_nodes.m_buffer[nodeId].m_flags & NetNode.Flags.Underground) != 0;
        }

        private static Vector3 NormalizeFlat(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude <= 0.001f)
                value = fallback;

            value.y = 0f;
            if (value.sqrMagnitude <= 0.001f)
                value = Vector3.forward;

            value.Normalize();
            return value;
        }

        private static bool RectsOverlapXZ(
            Vector3 centerA,
            Vector3 rightA,
            Vector3 forwardA,
            float halfWidthA,
            float halfLengthA,
            Vector3 centerB,
            Vector3 rightB,
            Vector3 forwardB,
            float halfWidthB,
            float halfLengthB)
        {
            Vector3 delta = centerB - centerA;
            delta.y = 0f;

            return OverlapsOnAxis(delta, rightA, rightA, forwardA, halfWidthA, halfLengthA, rightB, forwardB, halfWidthB, halfLengthB)
                   && OverlapsOnAxis(delta, forwardA, rightA, forwardA, halfWidthA, halfLengthA, rightB, forwardB, halfWidthB, halfLengthB)
                   && OverlapsOnAxis(delta, rightB, rightA, forwardA, halfWidthA, halfLengthA, rightB, forwardB, halfWidthB, halfLengthB)
                   && OverlapsOnAxis(delta, forwardB, rightA, forwardA, halfWidthA, halfLengthA, rightB, forwardB, halfWidthB, halfLengthB);
        }

        private static bool OverlapsOnAxis(
            Vector3 delta,
            Vector3 axis,
            Vector3 rightA,
            Vector3 forwardA,
            float halfWidthA,
            float halfLengthA,
            Vector3 rightB,
            Vector3 forwardB,
            float halfWidthB,
            float halfLengthB)
        {
            axis = NormalizeFlat(axis, Vector3.forward);
            float distance = Mathf.Abs(Vector3.Dot(delta, axis));
            float radiusA = (halfWidthA * Mathf.Abs(Vector3.Dot(rightA, axis)))
                            + (halfLengthA * Mathf.Abs(Vector3.Dot(forwardA, axis)));
            float radiusB = (halfWidthB * Mathf.Abs(Vector3.Dot(rightB, axis)))
                            + (halfLengthB * Mathf.Abs(Vector3.Dot(forwardB, axis)));
            return distance <= radiusA + radiusB;
        }

        private static bool TryFindNearestSurfaceRoad(Vector3 position, out ushort segmentId, out Vector3 roadPosition)
        {
            segmentId = 0;
            roadPosition = Vector3.zero;

            NetManager netManager = NetManager.instance;
            if (netManager == null
                || netManager.m_segments == null
                || netManager.m_segments.m_buffer == null)
            {
                return false;
            }

            CandidateSegments.Clear();
            AddSegmentGridCandidates(netManager, position, RoadSnapSearchRadius, CandidateSegments);
            if (CandidateSegments.Count == 0)
                return false;

            float bestDistance = MaxRoadSnapDistance * MaxRoadSnapDistance;
            for (int i = 0; i < CandidateSegments.Count; i++)
            {
                ushort candidateId = CandidateSegments[i];
                if (!TryGetNearestRoadPoint(netManager, candidateId, position, out roadPosition, out float distance))
                    continue;

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                segmentId = candidateId;
            }

            if (segmentId == 0)
                return false;

            TryGetNearestRoadPoint(netManager, segmentId, position, out roadPosition, out _);
            return true;
        }

        private static bool TryGetNearestRoadPoint(
            NetManager netManager,
            ushort segmentId,
            Vector3 position,
            out Vector3 roadPosition,
            out float distanceSqr)
        {
            roadPosition = Vector3.zero;
            distanceSqr = float.MaxValue;

            if (segmentId == 0 || segmentId >= netManager.m_segments.m_size)
                return false;

            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == 0
                || segment.Info == null
                || !(segment.Info.m_netAI is RoadBaseAI)
                || segment.m_startNode == 0
                || segment.m_endNode == 0
                || IsUndergroundNode(netManager, segment.m_startNode)
                || IsUndergroundNode(netManager, segment.m_endNode))
            {
                return false;
            }

            Bezier3 bezier = GetSegmentBezier(netManager, ref segment);
            float segmentPosition = FindClosestPositionOnBezier(bezier, position);
            roadPosition = bezier.Position(segmentPosition);
            distanceSqr = FlatSqrDistance(roadPosition, position);
            return true;
        }

        private static void AddSegmentGridCandidates(
            NetManager netManager,
            Vector3 position,
            float radius,
            System.Collections.Generic.List<ushort> candidates)
        {
            if (netManager.m_segmentGrid == null || candidates == null)
                return;

            int minGridX = Mathf.Max((int)((position.x - radius) / NetSegmentGridCellSize + NetSegmentGridHalfResolution), 0);
            int minGridZ = Mathf.Max((int)((position.z - radius) / NetSegmentGridCellSize + NetSegmentGridHalfResolution), 0);
            int maxGridX = Mathf.Min((int)((position.x + radius) / NetSegmentGridCellSize + NetSegmentGridHalfResolution), NetSegmentGridResolution - 1);
            int maxGridZ = Mathf.Min((int)((position.z + radius) / NetSegmentGridCellSize + NetSegmentGridHalfResolution), NetSegmentGridResolution - 1);
            NetSegment[] segments = netManager.m_segments.m_buffer;

            for (int z = minGridZ; z <= maxGridZ; z++)
            {
                int rowOffset = z * NetSegmentGridResolution;
                for (int x = minGridX; x <= maxGridX; x++)
                {
                    ushort candidateId = netManager.m_segmentGrid[rowOffset + x];
                    int guard = 0;
                    while (candidateId != 0)
                    {
                        AddCandidateSegment(candidates, candidateId);
                        if (candidateId >= segments.Length)
                            break;

                        candidateId = segments[candidateId].m_nextGridSegment;
                        guard++;
                        if (guard > MaxSegmentGridChainIterations)
                            break;
                    }
                }
            }
        }

        private static void AddCandidateSegment(System.Collections.Generic.List<ushort> candidates, ushort segmentId)
        {
            if (segmentId == 0 || candidates == null)
                return;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == segmentId)
                    return;
            }

            candidates.Add(segmentId);
        }

        public static float ResolveSurfaceHeight(Vector3 position)
        {
            TerrainManager terrainManager = TerrainManager.instance;
            return terrainManager == null ? position.y : terrainManager.SampleRawHeightSmooth(position);
        }

        private static Bezier3 GetSegmentBezier(NetManager netManager, ref NetSegment segment)
        {
            Vector3 start = netManager.m_nodes.m_buffer[segment.m_startNode].m_position;
            Vector3 end = netManager.m_nodes.m_buffer[segment.m_endNode].m_position;
            Vector3 middleA;
            Vector3 middleB;
            NetSegment.CalculateMiddlePoints(start, segment.m_startDirection, end, segment.m_endDirection, false, false, out middleA, out middleB);
            return new Bezier3
            {
                a = start,
                b = middleA,
                c = middleB,
                d = end
            };
        }

        private static float FindClosestPositionOnBezier(Bezier3 bezier, Vector3 point)
        {
            float bestT = 0f;
            float bestDistance = float.MaxValue;

            for (int i = 0; i <= BezierSampleCount; i++)
            {
                float t = i / (float)BezierSampleCount;
                Vector3 candidate = bezier.Position(t);
                float distance = FlatSqrDistance(candidate, point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestT = t;
                }
            }

            return Mathf.Clamp01(bestT);
        }

        private static Vector3 GetBezierDirection(Bezier3 bezier, float position)
        {
            float before = Mathf.Clamp01(position - 0.0125f);
            float after = Mathf.Clamp01(position + 0.0125f);
            Vector3 direction = bezier.Position(after) - bezier.Position(before);
            direction.y = 0f;
            return direction;
        }

        private static float EstimateRoadHalfWidth(NetInfo info)
        {
            if (info == null || info.m_lanes == null)
                return DefaultRoadHalfWidth;

            float max = DefaultRoadHalfWidth;
            for (int i = 0; i < info.m_lanes.Length; i++)
            {
                NetInfo.Lane lane = info.m_lanes[i];
                if ((lane.m_laneType & NetInfo.LaneType.Vehicle) == 0)
                    continue;

                max = Mathf.Max(max, Mathf.Abs(lane.m_position) + 2.2f);
            }

            return Mathf.Clamp(max, 4f, 18f);
        }

        private static float GetEntranceCenterOffset(NetInfo info)
        {
            return Mathf.Clamp(
                EstimateRoadHalfWidth(info) + EntranceRoadGap + (EntranceLotLength * 0.5f),
                13f,
                31f);
        }
    }
}
