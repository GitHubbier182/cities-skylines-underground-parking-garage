using UnityEngine;

namespace UndergroundParkingGarage
{
    public struct UndergroundParkingFacility
    {
        public static readonly UndergroundParkingFacility None = new UndergroundParkingFacility();

        public int Id;
        public ushort SurfaceSegmentId;
        public float SurfaceSegmentPosition;
        public Vector3 SurfaceRoadPosition;
        public Vector3 EntrancePosition;
        public Vector3 Direction;
        public Vector3 Side;
        public Vector3 GarageCenter;
        public Vector3 VehicleNodePosition;
        public Vector3 ConnectorStartPosition;
        public ushort EntranceBuildingId;
        public ushort EntrancePropId;
        public ushort EntranceBackPropId;
        public ushort ConnectorSegmentId;
        public ushort ConnectorStartNodeId;
        public ushort ConnectorEndNodeId;
        public bool ConnectorCreated;
        public int FloorCount;
        public ushort TargetBuildingId;
        public Vector3 GarageForward;
        public Vector3 GarageRight;
        public float GarageWidth;
        public float GarageLength;
        public bool EntranceVisualsEnabled;
        public int GarageDetailVariant;

        public bool IsValid
        {
            get { return Id > 0 && SurfaceSegmentId != 0; }
        }

        public UndergroundParkingFacility(
            int id,
            ushort surfaceSegmentId,
            float surfaceSegmentPosition,
            Vector3 surfaceRoadPosition,
            Vector3 entrancePosition,
            Vector3 direction,
            Vector3 side,
            Vector3 garageCenter,
            Vector3 vehicleNodePosition,
            Vector3 connectorStartPosition,
            ushort entrancePropId,
            ushort connectorSegmentId,
            ushort connectorStartNodeId,
            ushort connectorEndNodeId,
            bool connectorCreated,
            ushort entranceBuildingId = 0,
            int floorCount = 1,
            ushort targetBuildingId = 0,
            Vector3 garageForward = default(Vector3),
            Vector3 garageRight = default(Vector3),
            float garageWidth = 0f,
            float garageLength = 0f,
            ushort entranceBackPropId = 0,
            bool entranceVisualsEnabled = true,
            int garageDetailVariant = -1)
        {
            Id = id;
            SurfaceSegmentId = surfaceSegmentId;
            SurfaceSegmentPosition = Mathf.Clamp01(surfaceSegmentPosition);
            SurfaceRoadPosition = surfaceRoadPosition;
            EntrancePosition = entrancePosition;
            Direction = NormalizeFlat(direction, Vector3.forward);
            Side = NormalizeFlat(side, Vector3.right);
            GarageCenter = garageCenter;
            VehicleNodePosition = vehicleNodePosition;
            ConnectorStartPosition = connectorStartPosition;
            EntranceBuildingId = entranceBuildingId;
            EntrancePropId = entrancePropId;
            EntranceBackPropId = entranceBackPropId;
            ConnectorSegmentId = connectorSegmentId;
            ConnectorStartNodeId = connectorStartNodeId;
            ConnectorEndNodeId = connectorEndNodeId;
            ConnectorCreated = connectorCreated;
            FloorCount = UndergroundParkingGeometry.ClampFloorCount(floorCount);
            TargetBuildingId = targetBuildingId;
            GarageForward = NormalizeFlat(garageForward, Side);
            GarageRight = NormalizeFlat(garageRight, new Vector3(GarageForward.z, 0f, -GarageForward.x));
            GarageWidth = garageWidth > 0f ? garageWidth : UndergroundParkingGeometry.GarageWidth;
            GarageLength = garageLength > 0f ? garageLength : UndergroundParkingGeometry.GarageLength;
            EntranceVisualsEnabled = entranceVisualsEnabled;
            GarageDetailVariant = garageDetailVariant >= 0
                ? Mathf.Clamp(garageDetailVariant, 0, 7)
                : -1;
        }

        public UndergroundParkingFacility WithConnector(
            ushort connectorSegmentId,
            ushort connectorStartNodeId,
            ushort connectorEndNodeId,
            bool connectorCreated)
        {
            return new UndergroundParkingFacility(
                Id,
                SurfaceSegmentId,
                SurfaceSegmentPosition,
                SurfaceRoadPosition,
                EntrancePosition,
                Direction,
                Side,
                GarageCenter,
                VehicleNodePosition,
                ConnectorStartPosition,
                EntrancePropId,
                connectorSegmentId,
                connectorStartNodeId,
                connectorEndNodeId,
                connectorCreated,
                EntranceBuildingId,
                FloorCount, TargetBuildingId, GarageForward, GarageRight, GarageWidth, GarageLength,
                EntranceBackPropId, EntranceVisualsEnabled, GarageDetailVariant);
        }

        public UndergroundParkingFacility WithEntranceProps(ushort entrancePropId, ushort entranceBackPropId)
        {
            return new UndergroundParkingFacility(
                Id,
                SurfaceSegmentId,
                SurfaceSegmentPosition,
                SurfaceRoadPosition,
                EntrancePosition,
                Direction,
                Side,
                GarageCenter,
                VehicleNodePosition,
                ConnectorStartPosition,
                entrancePropId,
                ConnectorSegmentId,
                ConnectorStartNodeId,
                ConnectorEndNodeId,
                ConnectorCreated,
                EntranceBuildingId,
                FloorCount, TargetBuildingId, GarageForward, GarageRight, GarageWidth, GarageLength,
                entranceBackPropId, EntranceVisualsEnabled, GarageDetailVariant);
        }

        public UndergroundParkingFacility WithEntranceBuilding(ushort entranceBuildingId)
        {
            return new UndergroundParkingFacility(
                Id,
                SurfaceSegmentId,
                SurfaceSegmentPosition,
                SurfaceRoadPosition,
                EntrancePosition,
                Direction,
                Side,
                GarageCenter,
                VehicleNodePosition,
                ConnectorStartPosition,
                EntrancePropId,
                ConnectorSegmentId,
                ConnectorStartNodeId,
                ConnectorEndNodeId,
                ConnectorCreated,
                entranceBuildingId,
                FloorCount, TargetBuildingId, GarageForward, GarageRight, GarageWidth, GarageLength,
                EntranceBackPropId, EntranceVisualsEnabled, GarageDetailVariant);
        }

        public UndergroundParkingFacility WithFloorCount(int floorCount)
        {
            return new UndergroundParkingFacility(
                Id, SurfaceSegmentId, SurfaceSegmentPosition, SurfaceRoadPosition,
                EntrancePosition, Direction, Side,
                UndergroundParkingGeometry.CalculateGarageCenter(
                    EntrancePosition,
                    Side,
                    floorCount,
                    GarageLength),
                VehicleNodePosition, ConnectorStartPosition, EntrancePropId,
                ConnectorSegmentId, ConnectorStartNodeId, ConnectorEndNodeId,
                ConnectorCreated, EntranceBuildingId, floorCount,
                TargetBuildingId, GarageForward, GarageRight, GarageWidth, GarageLength,
                EntranceBackPropId, EntranceVisualsEnabled, GarageDetailVariant);
        }

        public UndergroundParkingFacility WithEntranceVisuals(bool enabled)
        {
            return new UndergroundParkingFacility(
                Id, SurfaceSegmentId, SurfaceSegmentPosition, SurfaceRoadPosition,
                EntrancePosition, Direction, Side, GarageCenter, VehicleNodePosition,
                ConnectorStartPosition, EntrancePropId, ConnectorSegmentId,
                ConnectorStartNodeId, ConnectorEndNodeId, ConnectorCreated,
                EntranceBuildingId, FloorCount, TargetBuildingId, GarageForward,
                GarageRight, GarageWidth, GarageLength, EntranceBackPropId, enabled,
                GarageDetailVariant);
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
    }
}
