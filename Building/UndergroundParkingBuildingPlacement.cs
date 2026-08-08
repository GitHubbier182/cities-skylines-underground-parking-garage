using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingBuildingPlacement
    {
        public static bool IsActive
        {
            get
            {
                ToolController controller = ToolsModifierControl.toolController;
                if (controller == null || !(controller.CurrentTool is BuildingTool))
                    return false;

                BuildingTool tool = controller.GetComponent<BuildingTool>();
                return tool != null && UndergroundParkingBuildingPrefab.IsGaragePrefab(tool.m_prefab);
            }
        }

        public static bool IsActiveVariant(UndergroundParkingStandaloneVariant variant)
        {
            ToolController controller = ToolsModifierControl.toolController;
            if (controller == null || !(controller.CurrentTool is BuildingTool))
                return false;

            BuildingTool tool = controller.GetComponent<BuildingTool>();
            return tool != null
                   && UndergroundParkingBuildingPrefab.IsGaragePrefab(tool.m_prefab)
                   && UndergroundParkingBuildingPrefab.GetVariant(tool.m_prefab) == variant;
        }

        public static void Activate()
        {
            Activate(UndergroundParkingStandaloneVariant.Compact);
        }

        public static void Activate(UndergroundParkingStandaloneVariant variant)
        {
            if (!UndergroundParkingFeatures.PlacementEnabled)
                return;

            BuildingInfo prefab = UndergroundParkingBuildingPrefab.EnsurePrefab(variant);
            if (prefab == null)
                return;

            if (!UndergroundParkingBuildingPrefab.IsRegistered)
            {
                UndergroundParkingLog.Error("Cannot activate parking entrance placement: runtime prefab is not registered with PrefabCollection.");
                return;
            }

            ToolController controller = ToolsModifierControl.toolController;
            if (controller == null)
                return;

            ClearExternalPlacementState();
            EnterNormalPlacementInfoMode();

            BuildingTool buildingTool = controller.GetComponent<BuildingTool>();
            if (buildingTool == null)
            {
                UndergroundParkingLog.Error("Cannot activate parking entrance placement: BuildingTool is unavailable.");
                return;
            }

            buildingTool.m_prefab = prefab;
            buildingTool.m_relocate = 0;
            controller.CurrentTool = buildingTool;
            UndergroundParkingPanel.UpdateButtonState();
            UndergroundParkingLog.Advanced(
                "Vanilla BuildingTool activated for underground parking garage entrance: variant="
                + variant);
        }

        private static void EnterNormalPlacementInfoMode()
        {
            InfoManager manager = InfoManager.instance;
            if (manager == null || manager.CurrentMode == InfoManager.InfoMode.None)
                return;

            try
            {
                manager.SetCurrentMode(
                    InfoManager.InfoMode.None,
                    InfoManager.SubInfoMode.Default);
                UndergroundParkingLog.Advanced(
                    "Standalone kiosk placement selected normal world view.");
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning(
                    "Could not enter normal view for standalone kiosk placement: "
                    + e.Message);
            }
        }

        public static void Deactivate()
        {
            ToolController controller = ToolsModifierControl.toolController;
            if (controller == null)
                return;

            BuildingTool buildingTool = controller.GetComponent<BuildingTool>();
            if (buildingTool != null && UndergroundParkingBuildingPrefab.IsGaragePrefab(buildingTool.m_prefab))
            {
                buildingTool.m_prefab = null;
                buildingTool.m_relocate = 0;
            }

            if (controller.CurrentTool is BuildingTool)
                ToolsModifierControl.SetTool<DefaultTool>();

            UndergroundParkingPanel.UpdateButtonState();
        }

        public static void ClearExternalPlacementState()
        {
            ToolController controller = ToolsModifierControl.toolController;
            if (controller == null)
                return;

            try
            {
                UndergroundParkingPlacementTool.Deactivate();

                BuildingTool buildingTool = controller.GetComponent<BuildingTool>();
                if (buildingTool != null)
                {
                    buildingTool.m_prefab = null;
                    buildingTool.m_relocate = 0;
                }

                NetTool netTool = controller.GetComponent<NetTool>();
                if (netTool != null)
                    netTool.m_prefab = null;

                PropTool propTool = controller.GetComponent<PropTool>();
                if (propTool != null)
                    propTool.m_prefab = null;

                TreeTool treeTool = controller.GetComponent<TreeTool>();
                if (treeTool != null)
                    treeTool.m_prefab = null;

                TransportTool transportTool = controller.GetComponent<TransportTool>();
                if (transportTool != null)
                {
                    transportTool.m_prefab = null;
                    transportTool.m_building = 0;
                }

                controller.m_editPrefabInfo = null;
                ToolsModifierControl.SetTool<DefaultTool>();
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning("Failed to clear previous placement tool state: " + e.Message);
            }
        }
    }
}
