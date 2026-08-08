namespace UndergroundParkingGarage
{
    internal enum UndergroundParkingStandaloneVariant
    {
        Compact = 0,
        Grand = 1,
        Square = 2
    }

    internal struct UndergroundParkingStandaloneSpec
    {
        public readonly UndergroundParkingStandaloneVariant Variant;
        public readonly string PrefabName;
        public readonly string Title;
        public readonly string Description;
        public readonly int CellWidth;
        public readonly int CellLength;
        public readonly float GarageWidth;
        public readonly float GarageLength;
        public readonly int DefaultFloorCount;
        public readonly int MinimumFloorCount;
        public readonly int ConstructionCost;

        public UndergroundParkingStandaloneSpec(
            UndergroundParkingStandaloneVariant variant,
            string prefabName,
            string title,
            string description,
            int cellWidth,
            int cellLength,
            float garageWidth,
            float garageLength,
            int defaultFloorCount,
            int minimumFloorCount,
            int constructionCost)
        {
            Variant = variant;
            PrefabName = prefabName;
            Title = title;
            Description = description;
            CellWidth = cellWidth;
            CellLength = cellLength;
            GarageWidth = garageWidth;
            GarageLength = garageLength;
            DefaultFloorCount = defaultFloorCount;
            MinimumFloorCount = minimumFloorCount;
            ConstructionCost = constructionCost;
        }
    }

    internal static class UndergroundParkingStandaloneCatalog
    {
        public const int VariantCount = 3;

        public static UndergroundParkingStandaloneSpec Get(
            UndergroundParkingStandaloneVariant variant)
        {
            switch (variant)
            {
                case UndergroundParkingStandaloneVariant.Grand:
                    return new UndergroundParkingStandaloneSpec(
                        variant,
                        "Underground Parking Grand Pavilion 4x4",
                        "Grand Underground Parking Pavilion",
                        "A 4x4 roadside pavilion serving a two-level 8x10-cell underground car park.",
                        4,
                        4,
                        8f * UndergroundParkingGeometry.BuildingCellSize,
                        10f * UndergroundParkingGeometry.BuildingCellSize,
                        2,
                        2,
                        50000);

                case UndergroundParkingStandaloneVariant.Square:
                    return new UndergroundParkingStandaloneSpec(
                        variant,
                        "Underground Parking Civic Pavilion 3x3",
                        "Civic Underground Parking Pavilion",
                        "A 3x3 roadside pavilion serving an 8x8-cell underground car park.",
                        3,
                        3,
                        8f * UndergroundParkingGeometry.BuildingCellSize,
                        8f * UndergroundParkingGeometry.BuildingCellSize,
                        1,
                        1,
                        35000);

                default:
                    return new UndergroundParkingStandaloneSpec(
                        UndergroundParkingStandaloneVariant.Compact,
                        UndergroundParkingBuildingPrefab.PrefabName,
                        UndergroundParkingLocalization.BuildingTitle,
                        UndergroundParkingLocalization.BuildingDescription,
                        2,
                        3,
                        UndergroundParkingGeometry.GarageWidth,
                        UndergroundParkingGeometry.GarageLength,
                        UndergroundParkingGeometry.DefaultFloorCount,
                        UndergroundParkingGeometry.DefaultFloorCount,
                        25000);
            }
        }

        public static UndergroundParkingStandaloneVariant FromFacility(
            UndergroundParkingFacility facility)
        {
            if (Approximately(facility.GarageWidth, 64f)
                && (Approximately(facility.GarageLength, 80f)
                    || Approximately(facility.GarageLength, 160f)))
            {
                return UndergroundParkingStandaloneVariant.Grand;
            }

            if (Approximately(facility.GarageWidth, 64f)
                && Approximately(facility.GarageLength, 64f))
            {
                return UndergroundParkingStandaloneVariant.Square;
            }

            return UndergroundParkingStandaloneVariant.Compact;
        }

        private static bool Approximately(float left, float right)
        {
            return System.Math.Abs(left - right) <= 0.1f;
        }
    }
}
