using ColossalFramework.Math;
using ColossalFramework.UI;
using UnityEngine;

namespace UndergroundParkingGarage
{
    public class UndergroundParkingPlacementTool : ToolBase
    {
        private enum PlacementStage { SelectBuilding, SelectRoadEntrance }
        private static bool _active;
        private static bool _changedInfoMode;
        private PlacementStage _stage;
        private ushort _selectedBuildingId;
        private int _relocatingFacilityId;
        private UndergroundParkingFacility _hoverFacility;
        private bool _hoverValid;
        private string _hoverMessage = string.Empty;
        private Vector3 _hoverTerrainPosition;
        private bool _hasHoverTerrainPosition;
        private Vector3 _hoverBuildingPosition;
        private float _hoverBuildingMarkerRadius;
        private bool _hasHoverBuildingPosition;
        private Vector3 _selectedBuildingMarkerPosition;
        private float _selectedBuildingMarkerRadius;
        private bool _hasSelectedBuildingMarker;
        private GUIStyle _tooltipStyle;

        public static bool Active
        {
            get { return _active; }
        }

        public static UndergroundParkingPlacementTool EnsureOnToolController()
        {
            ToolController controller = ToolsModifierControl.toolController;
            if (controller == null)
            {
                UndergroundParkingLog.Error("Cannot create placement tool: ToolController is unavailable.");
                return null;
            }

            UndergroundParkingPlacementTool tool = controller.GetComponent<UndergroundParkingPlacementTool>();
            if (tool == null)
            {
                tool = controller.gameObject.AddComponent<UndergroundParkingPlacementTool>();
                UndergroundParkingLog.Advanced("Placement tool attached to ToolController.");
            }

            return tool;
        }

        public static void Activate()
        {
            if (!UndergroundParkingFeatures.PlacementEnabled)
                return;

            if (!UndergroundParkingGarageSettings.SuppressAttachedEntranceVisuals
                && !UndergroundParkingEntranceAnchorService.IsRequiredParkingSignAvailable())
            {
                ShowMissingDependencyModal();
                return;
            }

            UndergroundParkingPlacementTool tool = EnsureOnToolController();
            if (tool == null || ToolsModifierControl.toolController == null)
                return;

            ClearExternalPlacementState();
            EnterPlacementInfoMode();
            _active = true;
            tool._stage = PlacementStage.SelectBuilding;
            tool._selectedBuildingId = 0;
            tool._relocatingFacilityId = 0;
            tool._hasHoverTerrainPosition = false;
            tool._hasHoverBuildingPosition = false;
            tool._hasSelectedBuildingMarker = false;
            ToolsModifierControl.toolController.CurrentTool = tool;
            UndergroundParkingPanel.UpdateButtonState();
            UndergroundParkingLog.Advanced("Placement tool activated.");
        }

        public static bool ActivateEntranceRelocation(ushort buildingId, out string status)
        {
            status = string.Empty;
            UndergroundParkingFacility facility;
            Building building;
            if (!UndergroundParkingFeatures.PlacementEnabled
                || !UndergroundParkingRegistry.TryGetForTargetBuilding(buildingId, out facility)
                || !UndergroundParkingGeometry.TryGetUsableBuilding(buildingId, out building))
            {
                status = "This building has no movable underground car park entrance.";
                return false;
            }

            if (facility.EntranceVisualsEnabled
                && !UndergroundParkingEntranceAnchorService.IsRequiredParkingSignAvailable())
            {
                ShowMissingDependencyModal();
                status = "The required parking-sign asset is unavailable. The original entrance was retained.";
                return false;
            }

            if (UndergroundParkingRegistry.IsEntranceRelocationPending(facility.Id))
            {
                status = "This entrance is already waiting for committed traffic to finish before it moves.";
                return false;
            }

            UndergroundParkingPlacementTool tool = EnsureOnToolController();
            if (tool == null || ToolsModifierControl.toolController == null)
            {
                status = "The placement tool is unavailable. The original entrance was retained.";
                return false;
            }

            ClearExternalPlacementState();
            EnterPlacementInfoMode();
            _active = true;
            tool._stage = PlacementStage.SelectRoadEntrance;
            tool._selectedBuildingId = buildingId;
            tool._relocatingFacilityId = facility.Id;
            tool._hoverFacility = UndergroundParkingFacility.None;
            tool._hoverValid = false;
            tool._hasHoverTerrainPosition = false;
            tool._hasHoverBuildingPosition = false;
            tool._selectedBuildingMarkerPosition = building.m_position;
            tool._selectedBuildingMarkerPosition.y =
                UndergroundParkingGeometry.ResolveSurfaceHeight(building.m_position) + 0.18f;
            tool._selectedBuildingMarkerRadius = Mathf.Max(
                7f,
                Mathf.Max(
                    UndergroundParkingGeometry.GetBuildingWidth(building),
                    UndergroundParkingGeometry.GetBuildingLength(building)) * 0.55f);
            tool._hasSelectedBuildingMarker = true;
            ToolsModifierControl.toolController.CurrentTool = tool;
            UndergroundParkingPanel.UpdateButtonState();
            status = "Choose a new pavement entrance. Right-click or Escape keeps the original entrance.";
            UndergroundParkingLog.Advanced(
                "Building-attached entrance relocation entered second placement stage: facility="
                + facility.Id
                + " targetBuilding="
                + buildingId
                + " originalEntranceRetained=True");
            return true;
        }

        public static void ReassertPlacementMode()
        {
            UndergroundParkingPlacementTool tool = EnsureOnToolController();
            if (tool == null || ToolsModifierControl.toolController == null)
                return;

            if (!_active)
            {
                Activate();
                return;
            }

            tool._stage = PlacementStage.SelectBuilding;
            tool._selectedBuildingId = 0;
            tool._relocatingFacilityId = 0;
            tool._hoverFacility = UndergroundParkingFacility.None;
            tool._hoverValid = false;
            tool._hasHoverTerrainPosition = false;
            tool._hasHoverBuildingPosition = false;
            tool._hasSelectedBuildingMarker = false;
            UndergroundParkingPlacementPreview.Clear();
            ToolsModifierControl.toolController.CurrentTool = tool;
            InfoManager manager = InfoManager.instance;
            if (manager != null)
            {
                try
                {
                    manager.SetCurrentMode(InfoManager.InfoMode.Transport, InfoManager.SubInfoMode.Default);
                }
                catch (System.Exception e)
                {
                    UndergroundParkingLog.Warning("Could not re-enter Transport x-ray view: " + e.Message);
                }
            }
            UndergroundParkingPanel.UpdateButtonState();
            UndergroundParkingLog.Advanced("Placement tool reset to building selection with Transport x-ray view reasserted.");
        }

        private static void ShowMissingDependencyModal()
        {
            const string title = "Parking Sign Required";
            string message = "Building-attached underground parking requires Steam Workshop item "
                             + UndergroundParkingEntranceAnchorService.RequiredWorkshopItemId
                             + " (parking sign by SvenBerlin). Subscribe to it, then restart Cities: Skylines.";
            try
            {
                if (UIView.library != null)
                {
                    ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                    if (panel != null)
                    {
                        panel.SetMessage(title, message, false);
                        return;
                    }
                }
                ConfirmPanel.ShowModal(title, message, null);
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning("Could not show missing parking-sign dependency message: " + e.Message);
            }
        }

        internal static void ClearExternalPlacementState()
        {
            ToolController controller = ToolsModifierControl.toolController;
            if (controller == null)
                return;

            try
            {
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

                if (!(controller.CurrentTool is UndergroundParkingPlacementTool))
                    ToolsModifierControl.SetTool<DefaultTool>();
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning("Failed to clear previous placement tool state: " + e.Message);
            }
        }

        public static void Deactivate()
        {
            if (!_active)
                return;

            MaintainPlacementInfoMode();

            _active = false;
            RestorePreviousInfoMode();
            UndergroundParkingPlacementTool tool = ToolsModifierControl.toolController == null
                ? null
                : ToolsModifierControl.toolController.GetComponent<UndergroundParkingPlacementTool>();
            if (tool != null)
            {
                tool._stage = PlacementStage.SelectBuilding;
                tool._selectedBuildingId = 0;
                tool._relocatingFacilityId = 0;
                tool._hasHoverTerrainPosition = false;
                tool._hasHoverBuildingPosition = false;
                tool._hasSelectedBuildingMarker = false;
            }
            UndergroundParkingPlacementPreview.Clear();
            if (ToolsModifierControl.toolController != null
                && ToolsModifierControl.toolController.CurrentTool is UndergroundParkingPlacementTool)
            {
                ToolsModifierControl.SetTool<DefaultTool>();
            }

            UndergroundParkingPanel.UpdateButtonState();
            UndergroundParkingLog.Advanced("Placement tool deactivated.");
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            if (!_active)
                return;

            MaintainPlacementInfoMode();

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                if (_relocatingFacilityId > 0)
                {
                    ushort buildingId = _selectedBuildingId;
                    UndergroundParkingHostManager.ReportStatus(
                        buildingId,
                        "Entrance move cancelled. The original entrance was retained.");
                    UndergroundParkingLog.Advanced(
                        "Building-attached entrance relocation cancelled: facility="
                        + _relocatingFacilityId
                        + " targetBuilding="
                        + buildingId
                        + " originalEntranceRetained=True");
                    Deactivate();
                    return;
                }

                if (_stage == PlacementStage.SelectRoadEntrance)
                {
                    _stage = PlacementStage.SelectBuilding;
                    _selectedBuildingId = 0;
                    _hoverFacility = UndergroundParkingFacility.None;
                    _hasHoverTerrainPosition = false;
                    _hasHoverBuildingPosition = false;
                    _hasSelectedBuildingMarker = false;
                    UndergroundParkingPlacementPreview.Clear();
                    UndergroundParkingLog.Advanced("Placement sequence reversed: entrance selection -> building selection; x-ray retained.");
                }
                else
                {
                    UndergroundParkingLog.Advanced("Placement sequence exited from building selection; restoring normal view.");
                    Deactivate();
                }
                return;
            }

            if (IsMouseOverUi())
            {
                _hoverValid = false;
                _hoverFacility = UndergroundParkingFacility.None;
                _hasHoverTerrainPosition = false;
                _hasHoverBuildingPosition = false;
                _hoverMessage = "UI active.";
                UndergroundParkingPlacementPreview.UpdatePreview(_hoverFacility, false, _hoverMessage);
                return;
            }

            UpdateHover();
            UndergroundParkingPlacementPreview.UpdatePreview(_hoverFacility, _hoverValid, _hoverMessage);

            bool leftClick = Input.GetMouseButtonDown(0);
            if (_relocatingFacilityId > 0 && leftClick && !_hoverValid)
            {
                ushort buildingId = _selectedBuildingId;
                UndergroundParkingHostManager.ReportStatus(
                    buildingId,
                    "Invalid new entrance. The original entrance was retained.");
                UndergroundParkingLog.Warning(
                    "Building-attached entrance relocation rejected by preview: facility="
                    + _relocatingFacilityId
                    + " targetBuilding="
                    + buildingId
                    + " reason="
                    + _hoverMessage
                    + " originalEntranceRetained=True");
                Deactivate();
                return;
            }

            if (!_hoverValid || !leftClick)
                return;

            if (_stage == PlacementStage.SelectBuilding)
            {
                _stage = PlacementStage.SelectRoadEntrance;
                _selectedBuildingMarkerPosition = _hoverBuildingPosition;
                _selectedBuildingMarkerRadius = _hoverBuildingMarkerRadius;
                _hasSelectedBuildingMarker = _hasHoverBuildingPosition;
                _hoverValid = false;
                _hoverFacility = UndergroundParkingFacility.None;
                _hasHoverBuildingPosition = false;
                UndergroundParkingLog.Advanced("Underground parking target building selected: building=" + _selectedBuildingId);
                return;
            }

            UndergroundParkingFacility placementDraft = _hoverFacility;
            int relocatingFacilityId = _relocatingFacilityId;
            ushort relocatingBuildingId = _selectedBuildingId;
            SimulationManager simulationManager = SimulationManager.instance;
            if (simulationManager == null)
            {
                _hoverMessage = "Simulation is not ready to place the parking entrance.";
                return;
            }

            simulationManager.AddAction(delegate
            {
                string status;
                if (relocatingFacilityId > 0)
                {
                    bool moved = UndergroundParkingRegistry.TryRelocateTargetEntrance(
                        relocatingBuildingId,
                        relocatingFacilityId,
                        placementDraft,
                        out status);
                    UndergroundParkingHostManager.ReportStatus(relocatingBuildingId, status);
                    if (!moved)
                    {
                        UndergroundParkingLog.Warning(
                            "Simulation-thread entrance relocation rejected: facility="
                            + relocatingFacilityId
                            + " reason="
                            + status
                            + " originalEntranceRetained=True");
                    }
                    return;
                }

                bool replaced;
                UndergroundParkingFacility placed = UndergroundParkingRegistry.AddOrReplace(
                    placementDraft, out replaced, out status);
                if (!placed.IsValid)
                    UndergroundParkingLog.Warning("Simulation-thread parking placement rejected: "
                                                  + (string.IsNullOrEmpty(status)
                                                      ? "Unable to place this entrance."
                                                      : status));
            });

            if (relocatingFacilityId > 0)
            {
                UndergroundParkingLog.Advanced(
                    "Building-attached entrance relocation queued: facility="
                    + relocatingFacilityId
                    + " targetBuilding="
                    + relocatingBuildingId
                    + " originalEntranceRetainedUntilCommit=True");
                Deactivate();
                return;
            }

            _stage = PlacementStage.SelectBuilding;
            _selectedBuildingId = 0;
            _hoverFacility = UndergroundParkingFacility.None;
            _hoverValid = false;
            _hasHoverTerrainPosition = false;
            _hasHoverBuildingPosition = false;
            _hasSelectedBuildingMarker = false;
            UndergroundParkingPlacementPreview.Clear();
            UndergroundParkingLog.Advanced(
                "Placement sequence queued simulation-thread entrance commit and returned to building selection; x-ray retained for another facility.");
        }

        protected override void OnToolGUI(Event e)
        {
            base.OnToolGUI(e);
            if (!_active || e.type != EventType.Repaint)
                return;

            string text = _hoverValid
                ? (_stage == PlacementStage.SelectBuilding
                    ? "Click this building to place parking beneath its footprint."
                    : "Click to place the pavement P sign and underground tunnel entrance.")
                : _hoverMessage;
            if (_tooltipStyle == null)
            {
                _tooltipStyle = new GUIStyle(GUI.skin.box);
                _tooltipStyle.wordWrap = true;
                _tooltipStyle.alignment = TextAnchor.MiddleLeft;
                _tooltipStyle.padding = new RectOffset(10, 10, 6, 6);
            }

            Vector2 mouse = Event.current.mousePosition;
            float x = Mathf.Clamp(
                mouse.x + 18f,
                8f,
                Mathf.Max(8f, Screen.width - 188f));
            float width = Mathf.Clamp(Screen.width - x - 8f, 180f, 460f);
            float height = Mathf.Clamp(
                _tooltipStyle.CalcHeight(new GUIContent(text), width),
                36f,
                96f);
            GUI.color = _hoverValid ? new Color(0.45f, 0.72f, 1f, 0.98f) : new Color(1f, 0.4f, 0.35f, 0.98f);
            GUI.Box(
                new Rect(
                    x,
                    Mathf.Clamp(mouse.y + 18f, 8f, Mathf.Max(8f, Screen.height - height - 8f)),
                    width,
                    height),
                text,
                _tooltipStyle);
            GUI.color = Color.white;
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            base.RenderOverlay(cameraInfo);

            if (!_active)
                return;

            UndergroundParkingPlacementPreview.RenderOverlay(cameraInfo);
            if (_stage == PlacementStage.SelectBuilding && _hasHoverBuildingPosition)
            {
                Color buildingOuter = new Color(0.08f, 0.56f, 1f, 0.96f);
                Color buildingInner = new Color(1f, 1f, 1f, 0.98f);
                RenderManager.instance.OverlayEffect.DrawCircle(
                    cameraInfo, buildingOuter, _hoverBuildingPosition,
                    _hoverBuildingMarkerRadius,
                    _hoverBuildingPosition.y - 1f,
                    _hoverBuildingPosition.y + 48f,
                    true, true);
                RenderManager.instance.OverlayEffect.DrawCircle(
                    cameraInfo, buildingInner, _hoverBuildingPosition,
                    2.4f,
                    _hoverBuildingPosition.y - 1f,
                    _hoverBuildingPosition.y + 48f,
                    true, true);
            }
            if (_stage == PlacementStage.SelectRoadEntrance && _hasSelectedBuildingMarker)
            {
                Color selectedOuter = new Color(0.08f, 0.56f, 1f, 0.96f);
                Color selectedInner = new Color(1f, 1f, 1f, 0.98f);
                RenderManager.instance.OverlayEffect.DrawCircle(
                    cameraInfo, selectedOuter, _selectedBuildingMarkerPosition,
                    _selectedBuildingMarkerRadius,
                    _selectedBuildingMarkerPosition.y - 1f,
                    _selectedBuildingMarkerPosition.y + 48f,
                    true, true);
                RenderManager.instance.OverlayEffect.DrawCircle(
                    cameraInfo, selectedInner, _selectedBuildingMarkerPosition,
                    2.4f,
                    _selectedBuildingMarkerPosition.y - 1f,
                    _selectedBuildingMarkerPosition.y + 48f,
                    true, true);
            }
            if (_stage == PlacementStage.SelectRoadEntrance && _hasHoverTerrainPosition)
            {
                Vector3 markerPosition = _hoverValid && _hoverFacility.SurfaceSegmentId != 0
                    ? _hoverFacility.EntrancePosition
                    : _hoverTerrainPosition;
                Color outer = _hoverValid
                    ? new Color(0.12f, 0.95f, 1f, 0.95f)
                    : new Color(1f, 0.16f, 0.08f, 0.95f);
                Color inner = _hoverValid
                    ? new Color(1f, 1f, 1f, 0.98f)
                    : new Color(1f, 0.72f, 0.12f, 0.98f);
                RenderManager.instance.OverlayEffect.DrawCircle(
                    cameraInfo, outer, markerPosition, 7f,
                    markerPosition.y - 1f, markerPosition.y + 24f, true, true);
                RenderManager.instance.OverlayEffect.DrawCircle(
                    cameraInfo, inner, markerPosition, 2.4f,
                    markerPosition.y - 1f, markerPosition.y + 24f, true, true);
            }
        }

        private void UpdateHover()
        {
            _hoverValid = false;
            _hoverFacility = UndergroundParkingFacility.None;
            _hoverMessage = _stage == PlacementStage.SelectBuilding
                ? "Select the building that will receive an underground parking lot."
                : "Select a pavement point on the building's side of an above-ground road, within 50 m.";
            _hasHoverTerrainPosition = false;
            _hasHoverBuildingPosition = false;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            if (_stage == PlacementStage.SelectBuilding)
            {
                ToolBase.RaycastOutput output;
                if (!TryRaycastBuilding(camera, out output) || output.m_building == 0)
                    return;

                Building building;
                if (!UndergroundParkingGeometry.TryGetUsableBuilding(output.m_building, out building))
                    return;

                _selectedBuildingId = output.m_building;
                _hoverBuildingPosition = building.m_position;
                _hoverBuildingPosition.y = UndergroundParkingGeometry.ResolveSurfaceHeight(building.m_position) + 0.18f;
                float buildingWidth = UndergroundParkingGeometry.GetBuildingWidth(building);
                float buildingLength = UndergroundParkingGeometry.GetBuildingLength(building);
                _hoverBuildingMarkerRadius = Mathf.Max(
                    7f,
                    Mathf.Max(buildingWidth, buildingLength) * 0.55f);
                _hasHoverBuildingPosition = true;
                UndergroundParkingFacility existingFacility;
                if (UndergroundParkingRegistry.TryGetForTargetBuilding(_selectedBuildingId, out existingFacility))
                {
                    _hoverMessage = "This building already has an underground car park. Delete it before placing another.";
                    return;
                }
                _hoverValid = true;
                _hoverMessage = string.Empty;
                return;
            }

            Vector3 terrainPosition;
            if (!TryRaycastTerrain(camera, out terrainPosition))
                return;
            _hoverTerrainPosition = terrainPosition;
            _hasHoverTerrainPosition = true;

            string message;
            UndergroundParkingFacility facility;
            if (!UndergroundParkingGeometry.TryCreateFacilityForBuilding(_selectedBuildingId, terrainPosition, out facility, out message))
            {
                _hoverMessage = message;
                return;
            }

            ushort ignoredExistingBuildingId = _relocatingFacilityId > 0
                ? _selectedBuildingId
                : (ushort)0;
            if (UndergroundParkingRegistry.OverlapsGarageReservation(
                    facility,
                    ignoredExistingBuildingId))
            {
                _hoverMessage = UndergroundParkingRegistry.GarageOverlapStatus;
                return;
            }

            _hoverFacility = facility;
            _hoverValid = true;
            _hoverMessage = string.Empty;
        }

        private static void EnterPlacementInfoMode()
        {
            InfoManager manager = InfoManager.instance;
            if (manager == null)
                return;

            try
            {
                manager.SetCurrentMode(InfoManager.InfoMode.Transport, InfoManager.SubInfoMode.Default);
                _changedInfoMode = true;
            }
            catch (System.Exception e)
            {
                _changedInfoMode = false;
                UndergroundParkingLog.Warning("Could not enter Transport x-ray view for building-attached placement: " + e.Message);
            }
        }

        private static void MaintainPlacementInfoMode()
        {
            InfoManager manager = InfoManager.instance;
            if (manager == null
                || manager.CurrentMode == InfoManager.InfoMode.Transport)
            {
                return;
            }

            try
            {
                manager.SetCurrentMode(
                    InfoManager.InfoMode.Transport,
                    InfoManager.SubInfoMode.Default);
                _changedInfoMode = true;
                UndergroundParkingLog.Advanced(
                    "Building-attached placement reasserted Transport x-ray after view drift.");
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning(
                    "Could not retain Transport x-ray for building-attached placement: "
                    + e.Message);
            }
        }

        private static void RestorePreviousInfoMode()
        {
            if (!_changedInfoMode)
                return;

            _changedInfoMode = false;
            InfoManager manager = InfoManager.instance;
            if (manager == null)
                return;

            try
            {
                manager.SetCurrentMode(InfoManager.InfoMode.None, InfoManager.SubInfoMode.Default);
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning("Could not restore the previous info view after building-attached placement: " + e.Message);
            }
        }

        private static bool TryRaycastTerrain(Camera camera, out Vector3 hitPosition)
        {
            hitPosition = Vector3.zero;
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            TerrainManager terrainManager = TerrainManager.instance;
            if (terrainManager == null)
                return false;

            Segment3 segment = new Segment3(ray.origin, ray.origin + (ray.direction * camera.farClipPlane));
            return terrainManager.RayCast(segment, out hitPosition);
        }

        private static bool TryRaycastBuilding(Camera camera, out ToolBase.RaycastOutput output)
        {
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            ToolBase.RaycastInput input = new ToolBase.RaycastInput(ray, camera.farClipPlane);
            input.m_buildingService = new ToolBase.RaycastService(ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Layer.Default);
            input.m_ignoreBuildingFlags = Building.Flags.None;
            return ToolBase.RayCast(input, out output);
        }

        private static bool IsMouseOverUi()
        {
            UIComponent hovered = UIInput.hoveredComponent;
            if (hovered != null && hovered.isVisible)
                return true;

            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return ContainsMouse(UndergroundParkingPanel.Instance, mouse);
        }

        private static bool ContainsMouse(UIComponent component, Vector2 mouse)
        {
            if (component == null || !component.isVisible)
                return false;

            Vector3 position = component.absolutePosition;
            Rect rect = new Rect(position.x, position.y, component.width, component.height);
            return rect.Contains(mouse);
        }
    }
}
