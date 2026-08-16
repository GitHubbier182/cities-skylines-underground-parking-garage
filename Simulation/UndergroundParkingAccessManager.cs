using UnityEngine;

namespace UndergroundParkingGarage
{
    internal struct UndergroundParkingRoadConnection
    {
        public int FacilityId;
        public ushort SegmentId;
        public byte LaneIndex;
        public byte SegmentOffset;
        public Vector3 LanePosition;
        public Vector3 RoadEntrancePosition;
        public Vector3 LaneDirection;

        public bool IsValid
        {
            get { return SegmentId != 0; }
        }

        public UndergroundParkingRoadConnection(
            int facilityId,
            ushort segmentId,
            byte laneIndex,
            byte segmentOffset,
            Vector3 lanePosition,
            Vector3 roadEntrancePosition,
            Vector3 laneDirection)
        {
            FacilityId = facilityId;
            SegmentId = segmentId;
            LaneIndex = laneIndex;
            SegmentOffset = segmentOffset;
            LanePosition = lanePosition;
            RoadEntrancePosition = roadEntrancePosition;
            LaneDirection = laneDirection;
        }
    }

    internal static class UndergroundParkingAccessManager
    {
        public static bool TryGetArrivalConnectionBeforeEntrance(
            UndergroundParkingFacility facility,
            byte laneIndex,
            UndergroundParkingRoadConnection entranceConnection,
            float distanceBeforeEntrance,
            Vector3 preferredDirection,
            out UndergroundParkingRoadConnection connection)
        {
            connection = default(UndergroundParkingRoadConnection);
            UndergroundParkingRoadConnection resolvedEntrance;
            if (!entranceConnection.IsValid
                || distanceBeforeEntrance <= 0f
                || !TryGetArrivalConnection(
                    facility,
                    laneIndex,
                    entranceConnection.SegmentOffset,
                    preferredDirection,
                    out resolvedEntrance))
                return false;

            uint laneId;
            if (!TryGetLaneId(resolvedEntrance, out laneId))
                return false;

            NetManager netManager = NetManager.instance;
            if (netManager == null || laneId >= netManager.m_lanes.m_size)
                return false;

            ref NetLane lane = ref netManager.m_lanes.m_buffer[laneId];
            Vector3 rawPosition;
            Vector3 rawDirection;
            lane.CalculatePositionAndDirection(
                resolvedEntrance.SegmentOffset / 255f,
                out rawPosition,
                out rawDirection);
            rawDirection.y = 0f;
            if (rawDirection.sqrMagnitude <= 0.001f
                || lane.m_length <= 0.01f)
                return false;
            rawDirection.Normalize();

            int travelOffsetSign = Vector3.Dot(
                rawDirection,
                resolvedEntrance.LaneDirection) >= 0f
                ? 1
                : -1;
            int offsetDistance = Mathf.Max(
                1,
                Mathf.RoundToInt(distanceBeforeEntrance / lane.m_length * 255f));
            int estimatedOffset = resolvedEntrance.SegmentOffset
                                  - travelOffsetSign * offsetDistance;

            float bestError = float.MaxValue;
            UndergroundParkingRoadConnection best =
                default(UndergroundParkingRoadConnection);
            for (int delta = -3; delta <= 3; delta++)
            {
                int candidateOffset = Mathf.Clamp(
                    estimatedOffset + delta,
                    1,
                    254);
                UndergroundParkingRoadConnection candidate;
                if (!TryGetArrivalConnection(
                        facility,
                        laneIndex,
                        (byte)candidateOffset,
                        resolvedEntrance.LaneDirection,
                        out candidate))
                    continue;

                Vector3 entranceDelta =
                    candidate.LanePosition - resolvedEntrance.LanePosition;
                entranceDelta.y = 0f;
                if (Vector3.Dot(
                        entranceDelta,
                        resolvedEntrance.LaneDirection) >= -0.01f)
                    continue;

                float error = Mathf.Abs(
                    entranceDelta.magnitude - distanceBeforeEntrance);
                if (error >= bestError)
                    continue;
                bestError = error;
                best = candidate;
            }

            if (!best.IsValid || bestError > 1f)
                return false;
            connection = best;
            return true;
        }

        public static bool TryGetDepartureConnectionAfterEntrance(
            UndergroundParkingFacility facility,
            byte laneIndex,
            UndergroundParkingRoadConnection entranceConnection,
            float distanceAfterEntrance,
            Vector3 preferredDirection,
            out UndergroundParkingRoadConnection connection)
        {
            connection = default(UndergroundParkingRoadConnection);
            UndergroundParkingRoadConnection resolvedEntrance;
            if (!entranceConnection.IsValid
                || distanceAfterEntrance <= 0f
                || !TryGetArrivalConnection(
                    facility,
                    laneIndex,
                    entranceConnection.SegmentOffset,
                    preferredDirection,
                    out resolvedEntrance))
                return false;

            uint laneId;
            if (!TryGetLaneId(resolvedEntrance, out laneId))
                return false;

            NetManager netManager = NetManager.instance;
            if (netManager == null || laneId >= netManager.m_lanes.m_size)
                return false;

            ref NetLane lane = ref netManager.m_lanes.m_buffer[laneId];
            Vector3 rawPosition;
            Vector3 rawDirection;
            lane.CalculatePositionAndDirection(
                resolvedEntrance.SegmentOffset / 255f,
                out rawPosition,
                out rawDirection);
            rawDirection.y = 0f;
            if (rawDirection.sqrMagnitude <= 0.001f
                || lane.m_length <= 0.01f)
                return false;
            rawDirection.Normalize();

            int travelOffsetSign = Vector3.Dot(
                rawDirection,
                resolvedEntrance.LaneDirection) >= 0f
                ? 1
                : -1;
            int offsetDistance = Mathf.Max(
                1,
                Mathf.RoundToInt(distanceAfterEntrance / lane.m_length * 255f));
            int estimatedOffset = resolvedEntrance.SegmentOffset
                                  + travelOffsetSign * offsetDistance;

            float bestError = float.MaxValue;
            UndergroundParkingRoadConnection best =
                default(UndergroundParkingRoadConnection);
            for (int delta = -3; delta <= 3; delta++)
            {
                int candidateOffset = Mathf.Clamp(
                    estimatedOffset + delta,
                    1,
                    254);
                UndergroundParkingRoadConnection candidate;
                if (!TryGetArrivalConnection(
                        facility,
                        laneIndex,
                        (byte)candidateOffset,
                        resolvedEntrance.LaneDirection,
                        out candidate))
                    continue;

                Vector3 entranceDelta =
                    candidate.LanePosition - resolvedEntrance.LanePosition;
                entranceDelta.y = 0f;
                if (Vector3.Dot(
                        entranceDelta,
                        resolvedEntrance.LaneDirection) <= 0.01f)
                    continue;

                float error = Mathf.Abs(
                    entranceDelta.magnitude - distanceAfterEntrance);
                if (error >= bestError)
                    continue;
                bestError = error;
                best = candidate;
            }

            if (!best.IsValid || bestError > 1f)
                return false;
            connection = best;
            return true;
        }

        public static bool TryGetLaneId(
            UndergroundParkingRoadConnection connection,
            out uint laneId)
        {
            laneId = 0u;
            NetManager netManager = NetManager.instance;
            if (!connection.IsValid
                || netManager == null
                || connection.SegmentId >= netManager.m_segments.m_size)
                return false;

            ref NetSegment segment =
                ref netManager.m_segments.m_buffer[connection.SegmentId];
            laneId = segment.m_lanes;
            int laneIndex = 0;
            while (laneId != 0u && laneIndex < connection.LaneIndex)
            {
                if (laneId >= netManager.m_lanes.m_size)
                {
                    laneId = 0u;
                    return false;
                }
                laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
                laneIndex++;
            }
            return laneId != 0u && laneId < netManager.m_lanes.m_size;
        }

        public static bool TryGetLiveLanePose(
            UndergroundParkingRoadConnection connection,
            out uint laneId,
            out Vector3 lanePosition,
            out Vector3 laneDirection)
        {
            laneId = 0u;
            lanePosition = Vector3.zero;
            laneDirection = Vector3.zero;

            NetManager netManager = NetManager.instance;
            if (netManager == null
                || connection.SegmentId == 0
                || connection.SegmentId >= netManager.m_segments.m_size)
                return false;

            ref NetSegment segment =
                ref netManager.m_segments.m_buffer[connection.SegmentId];
            NetInfo info = segment.Info;
            if ((segment.m_flags & NetSegment.Flags.Created) == 0
                || (segment.m_flags & NetSegment.Flags.Deleted) != 0
                || info == null
                || info.m_lanes == null
                || connection.LaneIndex >= info.m_lanes.Length
                || !TryGetLaneId(connection, out laneId))
                return false;

            NetInfo.Lane laneInfo = info.m_lanes[connection.LaneIndex];
            if (laneInfo == null
                || (laneInfo.m_laneType
                    & (NetInfo.LaneType.Vehicle
                       | NetInfo.LaneType.TransportVehicle)) == 0
                || (laneInfo.m_laneType & NetInfo.LaneType.Parking) != 0
                || (laneInfo.m_vehicleType & VehicleInfo.VehicleType.Car) == 0)
                return false;

            netManager.m_lanes.m_buffer[laneId].CalculatePositionAndDirection(
                connection.SegmentOffset / 255f,
                out lanePosition,
                out laneDirection);
            laneDirection.y = 0f;
            if (!IsFinite(lanePosition)
                || !IsFinite(laneDirection)
                || laneDirection.sqrMagnitude <= 0.001f)
                return false;

            laneDirection.Normalize();
            if ((laneInfo.m_finalDirection & NetInfo.Direction.Backward) != 0
                && (laneInfo.m_finalDirection & NetInfo.Direction.Forward) == 0)
            {
                laneDirection = -laneDirection;
            }
            else if ((laneInfo.m_finalDirection & NetInfo.Direction.Backward) != 0
                     && (laneInfo.m_finalDirection & NetInfo.Direction.Forward) != 0)
            {
                Vector3 preferredDirection = connection.LaneDirection;
                preferredDirection.y = 0f;
                if (IsFinite(preferredDirection)
                    && preferredDirection.sqrMagnitude > 0.001f
                    && Vector3.Dot(laneDirection, preferredDirection) < 0f)
                {
                    laneDirection = -laneDirection;
                }
            }

            return true;
        }

        public static bool TryGetLiveLanePose(
            UndergroundParkingRoadConnection connection,
            out Vector3 lanePosition,
            out Vector3 laneDirection)
        {
            uint laneId;
            return TryGetLiveLanePose(
                connection,
                out laneId,
                out lanePosition,
                out laneDirection);
        }

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                   && IsFinite(value.y)
                   && IsFinite(value.z);
        }

        public static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x)
                   && IsFinite(value.y)
                   && IsFinite(value.z)
                   && IsFinite(value.w)
                   && value.x * value.x
                      + value.y * value.y
                      + value.z * value.z
                      + value.w * value.w > 0.001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryGetRoadConnection(
            UndergroundParkingFacility facility,
            out UndergroundParkingRoadConnection connection,
            out string message)
        {
            connection = default(UndergroundParkingRoadConnection);
            message = string.Empty;

            if (facility.SurfaceSegmentId == 0)
            {
                message = "No road segment saved for the parking entrance.";
                return false;
            }

            NetManager netManager = NetManager.instance;
            if (netManager == null
                || facility.SurfaceSegmentId >= netManager.m_segments.m_size)
            {
                message = "Road manager is unavailable.";
                return false;
            }

            NetSegment segment = netManager.m_segments.m_buffer[facility.SurfaceSegmentId];
            NetInfo info = segment.Info;
            if ((segment.m_flags & NetSegment.Flags.Created) == 0
                || (segment.m_flags & NetSegment.Flags.Deleted) != 0
                || info == null
                || info.m_lanes == null)
            {
                message = "Saved road segment is no longer available.";
                return false;
            }

            byte segmentOffset = (byte)Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp01(facility.SurfaceSegmentPosition) * 255f),
                1,
                254);
            float laneOffset = segmentOffset / 255f;

            int bestLaneIndex;
            Vector3 bestLanePosition;
            Vector3 bestLaneDirection;
            if (!TryFindSameSideVehicleLane(
                    facility,
                    netManager,
                    ref segment,
                    info,
                    laneOffset,
                    out bestLaneIndex,
                    out bestLanePosition,
                    out bestLaneDirection))
            {
                message = "No same-side drivable car lane found at the parking entrance.";
                return false;
            }

            connection = new UndergroundParkingRoadConnection(
                facility.Id,
                facility.SurfaceSegmentId,
                (byte)Mathf.Clamp(bestLaneIndex, 0, 255),
                segmentOffset,
                bestLanePosition,
                facility.VehicleNodePosition,
                bestLaneDirection);
            return true;
        }

        public static bool TryGetArrivalConnection(
            UndergroundParkingFacility facility,
            byte laneIndex,
            byte segmentOffset,
            Vector3 preferredDirection,
            out UndergroundParkingRoadConnection connection)
        {
            connection = default(UndergroundParkingRoadConnection);
            NetManager netManager = NetManager.instance;
            if (!facility.IsValid
                || facility.SurfaceSegmentId == 0
                || segmentOffset == 0
                || netManager == null
                || facility.SurfaceSegmentId >= netManager.m_segments.m_size)
                return false;

            ref NetSegment segment =
                ref netManager.m_segments.m_buffer[facility.SurfaceSegmentId];
            NetInfo info = segment.Info;
            if ((segment.m_flags & NetSegment.Flags.Created) == 0
                || (segment.m_flags & NetSegment.Flags.Deleted) != 0
                || info == null
                || info.m_lanes == null
                || laneIndex >= info.m_lanes.Length)
                return false;

            NetInfo.Lane laneInfo = info.m_lanes[laneIndex];
            if (laneInfo == null
                || (laneInfo.m_laneType
                    & (NetInfo.LaneType.Vehicle
                       | NetInfo.LaneType.TransportVehicle)) == 0
                || (laneInfo.m_laneType & NetInfo.LaneType.Parking) != 0
                || (laneInfo.m_vehicleType & VehicleInfo.VehicleType.Car) == 0)
                return false;

            uint laneId = segment.m_lanes;
            for (int index = 0; laneId != 0u && index < laneIndex; index++)
            {
                if (laneId >= netManager.m_lanes.m_size)
                    return false;
                laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
            }
            if (laneId == 0u || laneId >= netManager.m_lanes.m_size)
                return false;

            Vector3 lanePosition;
            Vector3 laneDirection;
            netManager.m_lanes.m_buffer[laneId].CalculatePositionAndDirection(
                segmentOffset / 255f,
                out lanePosition,
                out laneDirection);
            laneDirection.y = 0f;
            if (laneDirection.sqrMagnitude <= 0.001f)
                return false;
            laneDirection.Normalize();
            if ((laneInfo.m_finalDirection & NetInfo.Direction.Backward) != 0
                && (laneInfo.m_finalDirection & NetInfo.Direction.Forward) == 0)
            {
                laneDirection = -laneDirection;
            }
            else if ((laneInfo.m_finalDirection & NetInfo.Direction.Backward) != 0
                     && (laneInfo.m_finalDirection & NetInfo.Direction.Forward) != 0)
            {
                preferredDirection.y = 0f;
                if (preferredDirection.sqrMagnitude > 0.001f
                    && Vector3.Dot(laneDirection, preferredDirection) < 0f)
                {
                    laneDirection = -laneDirection;
                }
            }

            connection = new UndergroundParkingRoadConnection(
                facility.Id,
                facility.SurfaceSegmentId,
                laneIndex,
                segmentOffset,
                lanePosition,
                facility.VehicleNodePosition,
                laneDirection);
            return true;
        }

        private static bool TryFindSameSideVehicleLane(
            UndergroundParkingFacility facility,
            NetManager netManager,
            ref NetSegment segment,
            NetInfo info,
            float laneOffset,
            out int bestLaneIndex,
            out Vector3 bestLanePosition,
            out Vector3 bestLaneDirection)
        {
            bestLaneIndex = -1;
            bestLanePosition = Vector3.zero;
            bestLaneDirection = Vector3.zero;

            uint laneId = segment.m_lanes;
            float bestScore = float.MaxValue;
            for (int laneIndex = 0; laneIndex < info.m_lanes.Length && laneId != 0u; laneIndex++)
            {
                if (laneId >= netManager.m_lanes.m_size)
                    break;

                NetInfo.Lane laneInfo = info.m_lanes[laneIndex];
                uint nextLaneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
                if (laneInfo == null
                    || (laneInfo.m_laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle)) == 0
                    || (laneInfo.m_laneType & NetInfo.LaneType.Parking) != 0
                    || (laneInfo.m_vehicleType & VehicleInfo.VehicleType.Car) == 0)
                {
                    laneId = nextLaneId;
                    continue;
                }

                Vector3 lanePosition;
                Vector3 laneDirection;
                netManager.m_lanes.m_buffer[laneId].CalculatePositionAndDirection(
                    laneOffset,
                    out lanePosition,
                    out laneDirection);

                Vector3 sideDelta = lanePosition - facility.SurfaceRoadPosition;
                sideDelta.y = 0f;
                if (Vector3.Dot(sideDelta, facility.Side) < -0.1f)
                {
                    laneId = nextLaneId;
                    continue;
                }

                laneDirection.y = 0f;
                if (laneDirection.sqrMagnitude > 0.001f)
                {
                    laneDirection.Normalize();
                    if ((laneInfo.m_finalDirection & NetInfo.Direction.Backward) != 0
                        && (laneInfo.m_finalDirection & NetInfo.Direction.Forward) == 0)
                    {
                        laneDirection = -laneDirection;
                    }
                }

                float score = UndergroundParkingGeometry.FlatSqrDistance(lanePosition, facility.VehicleNodePosition);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestLaneIndex = laneIndex;
                    bestLanePosition = lanePosition;
                    bestLaneDirection = laneDirection;
                }

                laneId = nextLaneId;
            }

            return bestLaneIndex >= 0;
        }
    }
}
