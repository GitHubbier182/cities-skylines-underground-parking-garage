using System;
using System.Reflection;
using ColossalFramework.UI;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal sealed class UndergroundParkingFloorPanel : MonoBehaviour
    {
        private const float RefreshSeconds = 0.2f;
        private const float SummaryPanelGap = 8f;
        private const float ScreenEdgePadding = 8f;
        private const string PositionSavedKey =
            "UndergroundParkingGarage.ParkingManagement.PositionSaved";
        private const string PositionXKey =
            "UndergroundParkingGarage.ParkingManagement.PositionX";
        private const string PositionYKey =
            "UndergroundParkingGarage.ParkingManagement.PositionY";
        private const string PositionFormatKey =
            "UndergroundParkingGarage.ParkingManagement.PositionFormat";
        private const int SummaryRelativePositionFormat = 2;
        private UIPanel _panel;
        private UILabel _summary;
        private UIDragHandle _dragHandle;
        private UILabel _occupancy;
        private UILabel _status;
        private UIButton _down;
        private UIButton _up;
        private UIButton _view;
        private UIButton _toggle;
        private UIButton _moveEntrance;
        private UIButton _entranceVisuals;
        private UIButton _delete;
        private ushort _buildingId;
        private ushort _xrayBuildingId;
        private Type _xrayHostPanelType;
        private UIComponent _xrayHostPanel;
        private bool _xrayHostClosedByVanilla;
        private bool _hostMode;
        private bool _userPositioned;
        private bool _isDragging;
        private bool _legacyAbsolutePositionPending;
        private Vector3 _savedPosition;
        private UIComponent _cachedSummary;
        private UIComponent _trackedHostSummary;
        private Vector3 _lastHostPosition;
        private bool _hasTrackedHostPosition;
        private float _nextRefresh;
        private int _viewLogCount;

        public static UndergroundParkingFloorPanel Instance;

        public static void EnsureOnRoot(GameObject root)
        {
            if (Instance == null && root != null)
                Instance = root.AddComponent<UndergroundParkingFloorPanel>();
        }

        public static void DestroyInstance()
        {
            if (Instance != null)
                UnityEngine.Object.Destroy(Instance);
            Instance = null;
        }

        private void Awake()
        {
            Instance = this;
            UIView view = UIView.GetAView();
            if (view == null)
                return;

            _panel = view.AddUIComponent(typeof(UIPanel)) as UIPanel;
            _panel.name = "UndergroundParkingFloorControls";
            _panel.width = 460f;
            _panel.height = 220f;
            _panel.backgroundSprite = "GenericPanel";
            _panel.color = new Color32(45, 55, 70, 245);
            _panel.canFocus = true;
            _panel.isInteractive = true;
            _panel.isVisible = false;

            _dragHandle = _panel.AddUIComponent<UIDragHandle>();
            _dragHandle.width = _panel.width;
            _dragHandle.height = 34f;
            _dragHandle.relativePosition = Vector3.zero;
            _dragHandle.target = _panel;
            _dragHandle.tooltip = "Drag to move this panel.";
            _dragHandle.eventMouseDown += OnPanelDragStarted;
            _dragHandle.eventMouseUp += OnPanelDragFinished;

            _summary = UIHelpers.AddLabel(_panel, string.Empty, 0.9f);
            _summary.width = 440f;
            _summary.height = 22f;
            _summary.relativePosition = new Vector3(10f, 8f);
            _summary.tooltip = "Drag to move this panel.";

            _occupancy = UIHelpers.AddLabel(_panel, string.Empty, 0.78f);
            _occupancy.width = 440f;
            _occupancy.height = 20f;
            _occupancy.relativePosition = new Vector3(10f, 34f);
            _occupancy.isVisible = false;

            _down = UIHelpers.AddButton(_panel, "− Floor", OnDown);
            _down.width = 92f;
            _down.height = 28f;
            _down.relativePosition = new Vector3(10f, 34f);

            _up = UIHelpers.AddButton(_panel, "+ Floor  ₡25,000", OnUp);
            _up.width = 174f;
            _up.height = 28f;
            _up.relativePosition = new Vector3(112f, 34f);

            _status = UIHelpers.AddLabel(_panel, string.Empty, 0.72f);
            _status.width = 440f;
            _status.height = 52f;
            _status.relativePosition = new Vector3(10f, 98f);

            _view = UIHelpers.AddButton(_panel, "View Car Park", OnView);
            _view.width = 150f;
            _view.height = 28f;
            _view.relativePosition = new Vector3(10f, 62f);

            _toggle = UIHelpers.AddButton(_panel, "Close Car Park", OnToggle);
            _toggle.width = 170f;
            _toggle.height = 28f;
            _toggle.relativePosition = new Vector3(170f, 62f);
            _toggle.isVisible = false;

            _moveEntrance = UIHelpers.AddButton(_panel, "Move Entrance", OnMoveEntrance);
            _moveEntrance.width = 205f;
            _moveEntrance.height = 28f;
            _moveEntrance.relativePosition = new Vector3(10f, 94f);
            _moveEntrance.isVisible = false;

            _entranceVisuals = UIHelpers.AddButton(_panel, "Hide Entrance Visuals", OnEntranceVisuals);
            _entranceVisuals.width = 225f;
            _entranceVisuals.height = 28f;
            _entranceVisuals.relativePosition = new Vector3(225f, 94f);
            _entranceVisuals.isVisible = false;

            _delete = UIHelpers.AddButton(_panel, "Delete Underground Garage", OnDelete);
            _delete.width = 440f;
            _delete.height = 28f;
            _delete.relativePosition = new Vector3(10f, 128f);
            _delete.isVisible = false;

            // The title label is created after the handle and otherwise wins
            // hit testing over most of the strip. Keep the native targeted
            // handle above every title-bar child so the whole 460x34 area has
            // one continuous drag owner.
            _dragHandle.BringToFront();

            LoadUserPosition(view);
        }

        private void Update()
        {
            if (_panel == null)
                return;

            CompleteVanillaXrayHostClosure();

            // Follow only real movement of the host. Never continuously write
            // the saved absolute target: that competes with dfGUI's native
            // drag and produces visible stutter and snap-back.
            FollowHostPanelMotion();

            float now = Time.realtimeSinceStartup;
            if (now < _nextRefresh)
                return;
            _nextRefresh = now + RefreshSeconds;

            UIView view = UIView.GetAView();
            UIComponent visibleHostSummary = view == null
                ? null
                : GetVisibleBuildingSummary(view);
            InstanceID selected = WorldInfoPanel.GetCurrentInstanceID();
            ushort buildingId = selected.Building;
            if (!IsCarParkViewActive())
                ClearXraySelectionSession();
            if (visibleHostSummary == null)
                buildingId = 0;
            if (buildingId == 0
                && _buildingId != 0
                && _xrayBuildingId == _buildingId
                && IsCarParkViewActive()
                && visibleHostSummary != null)
            {
                // Transport x-ray can clear the current WorldInfoPanel target
                // while leaving the exact visible host dialogue open. Retain
                // that validated selection, but never recreate a dialogue that
                // the player deliberately closed.
                buildingId = _buildingId;
            }
            UndergroundParkingFacility facility = UndergroundParkingFacility.None;
            bool kiosk = buildingId != 0 && UndergroundParkingRegistry.TryGetForBuilding(buildingId, out facility);
            bool host = !kiosk && buildingId != 0
                        && UndergroundParkingRegistry.TryGetForTargetBuilding(buildingId, out facility);
            if (!kiosk && !host)
            {
                ClearPanelSelection();
                return;
            }

            bool selectionChanged = _buildingId != buildingId;
            bool wasVisible = _panel != null && _panel.isVisible;
            _buildingId = buildingId;
            if (IsCarParkViewActive())
                _xrayBuildingId = buildingId;
            _hostMode = host;
            _view.text = IsCarParkViewActive() ? "Hide Car Park" : "View Car Park";
            int occupied = UndergroundParkingOccupancyManager.CountAssignedParkedCars(facility);
            _summary.text = "Parking Management";

            if (host)
            {
                _panel.height = 252f;
                bool open = UndergroundParkingRegistry.IsFacilityOpen(facility);
                bool relocating = UndergroundParkingRegistry.IsEntranceRelocationPending(facility.Id);
                int capacity = UndergroundParkingGeometry.GetParkingSpaceCapacity(facility);
                int parkedOnRemovedFloor = 0;
                bool removedFloorOccupied = facility.FloorCount > 1
                                            && UndergroundParkingOccupancyManager.HasAssignedCarsOnRemovedFloors(
                                                facility,
                                                facility.FloorCount - 1,
                                                out parkedOnRemovedFloor);
                int minimumFloorCount =
                    UndergroundParkingGeometry.GetMinimumFloorCount(facility);
                int maximumFloorCount =
                    UndergroundParkingGeometry.GetMaximumFloorCount(facility);
                _down.isVisible = true;
                _up.isVisible = true;
                _occupancy.isVisible = true;
                _toggle.isVisible = true;
                _moveEntrance.isVisible = true;
                _entranceVisuals.isVisible = true;
                _delete.isVisible = true;
                _view.isVisible = true;
                _down.relativePosition = new Vector3(10f, 58f);
                _up.relativePosition = new Vector3(112f, 58f);
                _view.relativePosition = new Vector3(10f, 90f);
                _toggle.relativePosition = new Vector3(170f, 90f);
                _moveEntrance.relativePosition = new Vector3(10f, 122f);
                _entranceVisuals.relativePosition = new Vector3(225f, 122f);
                _delete.relativePosition = new Vector3(10f, 156f);
                _status.relativePosition = new Vector3(10f, 190f);
                _toggle.text = relocating
                    ? "Relocating..."
                    : open ? "Close Car Park" : "Open Car Park";
                _entranceVisuals.text = facility.EntranceVisualsEnabled
                    ? "Hide Entrance Visuals"
                    : "Show Entrance Visuals";
                _delete.isEnabled = occupied == 0 && !relocating;
                _toggle.isEnabled = !relocating;
                _moveEntrance.isEnabled = !relocating;
                _down.isEnabled = facility.FloorCount > minimumFloorCount
                                  && !removedFloorOccupied
                                  && !relocating
                                  && occupied <= UndergroundParkingGeometry.GetParkingSpaceCapacity(
                                      facility,
                                      facility.FloorCount - 1);
                _up.isEnabled = facility.FloorCount < maximumFloorCount
                                && !relocating;
                _occupancy.text = "Floors: " + facility.FloorCount
                                  + "  •  Spaces used: " + occupied + " / " + capacity
                                  + (relocating
                                      ? "  •  Relocating entrance"
                                      : open ? "  •  Accepting arrivals" : "  •  Draining");
                _status.text = removedFloorOccupied
                    ? "Floor "
                      + facility.FloorCount
                      + " has "
                      + parkedOnRemovedFloor
                      + (parkedOnRemovedFloor == 1 ? " parked vehicle. " : " parked vehicles. ")
                      + "Empty it before removal."
                    : relocating
                    ? UndergroundParkingHostManager.GetStatus(buildingId)
                    : occupied > 0 && open
                    ? "Turn off the car park to drain it before deletion."
                    : UndergroundParkingHostManager.GetStatus(buildingId);
            }
            else
            {
                _panel.height = 220f;
                _down.relativePosition = new Vector3(10f, 34f);
                _up.relativePosition = new Vector3(112f, 34f);
                _view.relativePosition = new Vector3(10f, 62f);
                _toggle.relativePosition = new Vector3(170f, 62f);
                _down.isVisible = true;
                _up.isVisible = true;
                _occupancy.isVisible = false;
                _toggle.isVisible = true;
                _moveEntrance.isVisible = false;
                _entranceVisuals.isVisible = false;
                _delete.isVisible = false;
                _view.isVisible = true;
                bool buildingOpen = UndergroundParkingFloorManager.IsEntranceBuildingOpen(buildingId);
                _toggle.text = buildingOpen ? "Close Car Park" : "Open Car Park";
                _status.relativePosition = new Vector3(10f, 98f);
                int parkedOnRemovedFloor = 0;
                bool removedFloorOccupied = facility.FloorCount > 1
                                            && UndergroundParkingOccupancyManager.HasAssignedCarsOnRemovedFloors(
                                                facility,
                                                facility.FloorCount - 1,
                                                out parkedOnRemovedFloor);
                _status.text = removedFloorOccupied
                    ? "Floor "
                      + facility.FloorCount
                      + " has "
                      + parkedOnRemovedFloor
                      + (parkedOnRemovedFloor == 1 ? " parked vehicle. " : " parked vehicles. ")
                      + "Empty it before removal."
                    : UndergroundParkingFloorManager.LastStatus;
                int minimumFloorCount =
                    UndergroundParkingGeometry.GetMinimumFloorCount(facility);
                _down.isEnabled = facility.FloorCount > minimumFloorCount
                                  && !removedFloorOccupied
                                  && occupied <= UndergroundParkingGeometry.GetParkingSpaceCapacity(facility, facility.FloorCount - 1);
                _up.isEnabled = facility.FloorCount
                                < UndergroundParkingGeometry.GetMaximumFloorCount(facility);
            }

            if (!_isDragging && (selectionChanged || !wasVisible))
            {
                if (_userPositioned)
                    PositionAtSavedSummaryOffset(UIView.GetAView());
                else
                    PositionBesideBuildingSummary();
                TrackCurrentHostPosition();
            }
            if (selectionChanged || !wasVisible)
                _panel.BringToFront();
            _panel.isVisible = true;
        }

        private void CompleteVanillaXrayHostClosure()
        {
            if (!_xrayHostClosedByVanilla)
                return;

            _xrayHostClosedByVanilla = false;
            InstanceID selected = WorldInfoPanel.GetCurrentInstanceID();
            if (selected.Building != 0
                && selected.Building != _xrayBuildingId)
            {
                // Vanilla closed the restored host while selecting a different
                // target. Preserve that target and let the ordinary refresh
                // transfer or close the companion.
                ClearXraySelectionSession();
                return;
            }

            // Vanilla closed the restored host without selecting a different
            // building. End only UPG's x-ray companion session and restore the
            // normal selectable view; UPG never reads or replays the click.
            ClearXraySelectionSession();
            InfoManager manager = InfoManager.instance;
            if (manager != null
                && manager.CurrentMode == InfoManager.InfoMode.Transport)
                manager.SetCurrentMode(
                    InfoManager.InfoMode.None,
                    InfoManager.SubInfoMode.Default);
            ClearPanelSelection();
        }

        private void OnXrayHostVisibilityChanged(
            UIComponent component,
            bool visible)
        {
            if (visible
                || component == null
                || component != _xrayHostPanel
                || _xrayBuildingId == 0)
                return;

            component.eventVisibilityChanged -= OnXrayHostVisibilityChanged;
            _xrayHostPanel = null;
            // The companion is visually subordinate to this exact vanilla
            // host. Hide it in the same visibility transition; defer only
            // InfoManager cleanup so vanilla's close callback can finish.
            ClearPanelSelection();
            _xrayHostClosedByVanilla = true;
        }

        private void ObserveRestoredXrayHost()
        {
            if (_xrayHostPanel != null)
                _xrayHostPanel.eventVisibilityChanged -=
                    OnXrayHostVisibilityChanged;

            UIView view = UIView.GetAView();
            _xrayHostPanel = view == null
                ? null
                : GetVisibleBuildingSummary(view);
            if (_xrayHostPanel != null)
                _xrayHostPanel.eventVisibilityChanged +=
                    OnXrayHostVisibilityChanged;
        }

        private void ClearXraySelectionSession()
        {
            if (_xrayHostPanel != null)
                _xrayHostPanel.eventVisibilityChanged -=
                    OnXrayHostVisibilityChanged;
            _xrayHostPanel = null;
            _xrayBuildingId = 0;
            _xrayHostPanelType = null;
            _xrayHostClosedByVanilla = false;
        }

        private void ClearPanelSelection()
        {
            _buildingId = 0;
            _hostMode = false;
            _isDragging = false;
            _cachedSummary = null;
            _trackedHostSummary = null;
            _hasTrackedHostPosition = false;
            if (_panel != null)
                _panel.isVisible = false;
        }

        private void PositionAtSavedSummaryOffset(UIView view)
        {
            if (view == null || _panel == null)
                return;

            UIComponent summary = GetVisibleBuildingSummary(view);
            if (_legacyAbsolutePositionPending)
            {
                if (summary == null)
                {
                    _panel.absolutePosition = _savedPosition;
                    KeepUserPositionOnScreen(view);
                    return;
                }

                _savedPosition -= summary.absolutePosition;
                _legacyAbsolutePositionPending = false;
                SaveSummaryOffset();
            }

            if (summary != null)
            {
                Vector3 attachedPosition =
                    summary.absolutePosition + _savedPosition;
                Vector3 currentPosition = _panel.absolutePosition;
                if (!Mathf.Approximately(
                        currentPosition.x,
                        attachedPosition.x)
                    || !Mathf.Approximately(
                        currentPosition.y,
                        attachedPosition.y))
                    _panel.absolutePosition = attachedPosition;
            }

            KeepUserPositionOnScreen(view);
        }

        private void FollowHostPanelMotion()
        {
            if (_panel == null || !_panel.isVisible)
                return;

            UIView view = UIView.GetAView();
            UIComponent summary = view == null
                ? null
                : GetVisibleBuildingSummary(view);
            if (summary == null)
            {
                _trackedHostSummary = null;
                _hasTrackedHostPosition = false;
                return;
            }

            Vector3 hostPosition = summary.absolutePosition;
            if (!_hasTrackedHostPosition
                || _trackedHostSummary != summary)
            {
                _trackedHostSummary = summary;
                _lastHostPosition = hostPosition;
                _hasTrackedHostPosition = true;
                if (!_isDragging && _userPositioned)
                    PositionAtSavedSummaryOffset(view);
                return;
            }

            Vector3 hostDelta = hostPosition - _lastHostPosition;
            _lastHostPosition = hostPosition;
            if (_isDragging
                || (Mathf.Abs(hostDelta.x) < 0.05f
                    && Mathf.Abs(hostDelta.y) < 0.05f))
                return;

            _panel.absolutePosition += hostDelta;
            KeepUserPositionOnScreen(view);
        }

        private void TrackCurrentHostPosition()
        {
            UIView view = UIView.GetAView();
            UIComponent summary = view == null
                ? null
                : GetVisibleBuildingSummary(view);
            _trackedHostSummary = summary;
            _hasTrackedHostPosition = summary != null;
            if (_hasTrackedHostPosition)
                _lastHostPosition = summary.absolutePosition;
        }

        private void CaptureAndSaveSummaryOffset(UIView view)
        {
            UIComponent summary = GetVisibleBuildingSummary(view);
            if (summary == null || _panel == null)
                return;

            _savedPosition =
                _panel.absolutePosition - summary.absolutePosition;
            _legacyAbsolutePositionPending = false;
            SaveSummaryOffset();
        }

        private void PositionBesideBuildingSummary()
        {
            UIView view = UIView.GetAView();
            if (view == null || _panel == null)
                return;

            UIComponent summary = GetVisibleBuildingSummary(view);
            float x;
            float y;
            if (summary != null)
            {
                Vector3 summaryPosition = summary.absolutePosition;
                x = summaryPosition.x + summary.width + SummaryPanelGap;
                y = summaryPosition.y;
            }
            else
            {
                // A selected building can use a specialized status panel that
                // is not exposed as a visible WorldInfoPanel component. The
                // selected facility remains authoritative, so loss of an
                // optional UI anchor must never suppress its management panel.
                x = view.fixedWidth - _panel.width - ScreenEdgePadding;
                y = ScreenEdgePadding;
            }
            x = Mathf.Clamp(x, ScreenEdgePadding, Mathf.Max(ScreenEdgePadding, view.fixedWidth - _panel.width - ScreenEdgePadding));
            y = Mathf.Clamp(y, ScreenEdgePadding, Mathf.Max(ScreenEdgePadding, view.fixedHeight - _panel.height - ScreenEdgePadding));
            _panel.absolutePosition = new Vector3(x, y);
        }

        private void OnPanelDragStarted(
            UIComponent component,
            UIMouseEventParameter parameter)
        {
            _userPositioned = true;
            _isDragging = true;
            TrackCurrentHostPosition();
            if (_panel != null)
                _panel.BringToFront();
        }

        private void OnPanelDragFinished(
            UIComponent component,
            UIMouseEventParameter parameter)
        {
            UIView view = UIView.GetAView();
            _isDragging = false;
            if (view == null || _panel == null)
                return;

            KeepUserPositionOnScreen(view);
            CaptureAndSaveSummaryOffset(view);
            TrackCurrentHostPosition();
        }

        private void LoadUserPosition(UIView view)
        {
            if (view == null
                || _panel == null
                || PlayerPrefs.GetInt(PositionSavedKey, 0) == 0)
                return;

            float x = PlayerPrefs.GetFloat(PositionXKey, float.NaN);
            float y = PlayerPrefs.GetFloat(PositionYKey, float.NaN);
            if (float.IsNaN(x)
                || float.IsInfinity(x)
                || float.IsNaN(y)
                || float.IsInfinity(y))
            {
                ClearSavedPosition();
                return;
            }

            _savedPosition = new Vector3(x, y);
            _userPositioned = true;
            _legacyAbsolutePositionPending =
                PlayerPrefs.GetInt(PositionFormatKey, 0)
                != SummaryRelativePositionFormat;
        }

        private void SaveSummaryOffset()
        {
            PlayerPrefs.SetInt(PositionSavedKey, 1);
            PlayerPrefs.SetFloat(PositionXKey, _savedPosition.x);
            PlayerPrefs.SetFloat(PositionYKey, _savedPosition.y);
            PlayerPrefs.SetInt(
                PositionFormatKey,
                SummaryRelativePositionFormat);
            PlayerPrefs.Save();
        }

        private static void ClearSavedPosition()
        {
            PlayerPrefs.DeleteKey(PositionSavedKey);
            PlayerPrefs.DeleteKey(PositionXKey);
            PlayerPrefs.DeleteKey(PositionYKey);
            PlayerPrefs.DeleteKey(PositionFormatKey);
            PlayerPrefs.Save();
        }

        private bool KeepUserPositionOnScreen(UIView view)
        {
            if (view == null || _panel == null)
                return false;

            Vector3 position = _panel.absolutePosition;
            float x = Mathf.Clamp(
                position.x,
                ScreenEdgePadding,
                Mathf.Max(
                    ScreenEdgePadding,
                    view.fixedWidth - _panel.width - ScreenEdgePadding));
            float y = Mathf.Clamp(
                position.y,
                ScreenEdgePadding,
                Mathf.Max(
                    ScreenEdgePadding,
                    view.fixedHeight - _panel.height - ScreenEdgePadding));
            bool changed = !Mathf.Approximately(position.x, x)
                           || !Mathf.Approximately(position.y, y);
            if (changed)
                _panel.absolutePosition = new Vector3(x, y);
            return changed;
        }

        private UIComponent GetVisibleBuildingSummary(UIView view)
        {
            if (IsVisibleSummary(_cachedSummary))
                return _cachedSummary;

            _cachedSummary = FindVisibleBuildingSummary(view);
            return _cachedSummary;
        }

        private static bool IsVisibleSummary(UIComponent summary)
        {
            if (summary == null
                || !summary.enabled
                || summary.gameObject == null
                || !summary.gameObject.activeInHierarchy
                || !summary.isVisible)
                return false;

            WorldInfoPanel panel =
                summary.GetComponent<WorldInfoPanel>();
            return panel != null
                   && panel.GetType().Name.IndexOf(
                       "Building",
                       StringComparison.OrdinalIgnoreCase)
                   >= 0;
        }

        private static UIComponent FindVisibleBuildingSummary(UIView view)
        {
            WorldInfoPanel[] panels = view.GetComponentsInChildren<WorldInfoPanel>(true);
            UIComponent activeWorldInfoFallback = null;
            for (int i = 0; i < panels.Length; i++)
            {
                WorldInfoPanel panel = panels[i];
                if (panel == null
                    || !panel.enabled
                    || panel.gameObject == null
                    || !panel.gameObject.activeInHierarchy)
                    continue;

                UIComponent component = panel.GetComponent<UIComponent>();
                if (component == null || !component.isVisible)
                    continue;

                if (activeWorldInfoFallback == null)
                    activeWorldInfoFallback = component;

                string typeName = panel.GetType().Name;
                if (typeName.IndexOf("Building", StringComparison.OrdinalIgnoreCase) >= 0)
                    return component;
            }

            // City-service and other specialized building dialogues do not
            // necessarily include "Building" in their runtime type name.
            // The current selected building ID remains the facility authority;
            // this fallback supplies only the visible dialogue ownership gate.
            return activeWorldInfoFallback;
        }

        private void OnDown(UIComponent component, UIMouseEventParameter parameter)
        {
            UndergroundParkingFloorManager.RequestFloorChange(_buildingId, -1);
        }

        private void OnUp(UIComponent component, UIMouseEventParameter parameter)
        {
            UndergroundParkingFloorManager.RequestFloorChange(_buildingId, 1);
        }

        private void OnToggle(UIComponent component, UIMouseEventParameter parameter)
        {
            if (!_hostMode)
            {
                UndergroundParkingFloorManager.RequestSetEntranceBuildingOpen(
                    _buildingId,
                    !UndergroundParkingFloorManager.IsEntranceBuildingOpen(_buildingId));
                return;
            }

            UndergroundParkingFacility facility;
            if (UndergroundParkingRegistry.TryGetForTargetBuilding(_buildingId, out facility))
                UndergroundParkingHostManager.RequestSetOpen(
                    _buildingId,
                    !UndergroundParkingRegistry.IsFacilityOpen(facility));
        }

        private void OnView(UIComponent component, UIMouseEventParameter parameter)
        {
            InfoManager manager = InfoManager.instance;
            if (manager == null)
                return;

            try
            {
                bool hide = IsCarParkViewActive();
                Type hostPanelType = hide
                    ? null
                    : GetCurrentHostWorldInfoPanelType();
                manager.SetCurrentMode(
                    hide ? InfoManager.InfoMode.None : InfoManager.InfoMode.Transport,
                    InfoManager.SubInfoMode.Default);
                if (hide)
                    ClearXraySelectionSession();
                else
                {
                    _xrayBuildingId = _buildingId;
                    _xrayHostPanelType = hostPanelType;
                    _xrayHostClosedByVanilla = false;
                }
                ToolsModifierControl.SetTool<DefaultTool>();
                if (!hide)
                {
                    RestoreHostWorldInfoPanel(
                        _xrayHostPanelType,
                        _xrayBuildingId);
                    ObserveRestoredXrayHost();
                }
                _view.text = hide ? "View Car Park" : "Hide Car Park";
                if (_viewLogCount++ < 8)
                {
                    UndergroundParkingLog.Advanced(
                        hide
                            ? "UPG selected car park hidden and normal view restored: building=" + _buildingId
                            : "UPG selected car park opened in x-ray view: building=" + _buildingId);
                }
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning(
                    "Could not toggle selected car park x-ray view: " + e.Message);
            }
        }

        private void OnEntranceVisuals(UIComponent component, UIMouseEventParameter parameter)
        {
            if (!_hostMode)
                return;

            UndergroundParkingFacility facility;
            if (UndergroundParkingRegistry.TryGetForTargetBuilding(_buildingId, out facility))
                UndergroundParkingHostManager.RequestSetEntranceVisuals(
                    _buildingId,
                    !facility.EntranceVisualsEnabled);
        }

        private void OnMoveEntrance(UIComponent component, UIMouseEventParameter parameter)
        {
            if (_hostMode)
                UndergroundParkingHostManager.RequestMoveEntrance(_buildingId);
        }

        private static bool IsCarParkViewActive()
        {
            InfoManager manager = InfoManager.instance;
            return manager != null && manager.CurrentMode == InfoManager.InfoMode.Transport;
        }

        private Type GetCurrentHostWorldInfoPanelType()
        {
            UIView view = UIView.GetAView();
            UIComponent summary = view == null
                ? null
                : GetVisibleBuildingSummary(view);
            WorldInfoPanel panel = summary == null
                ? null
                : summary.GetComponent<WorldInfoPanel>();
            return panel == null ? null : panel.GetType();
        }

        private static void RestoreHostWorldInfoPanel(
            Type panelType,
            ushort buildingId)
        {
            if (panelType == null
                || buildingId == 0
                || !typeof(WorldInfoPanel).IsAssignableFrom(panelType))
                return;

            BuildingManager buildingManager = BuildingManager.instance;
            if (buildingManager == null
                || buildingId >= buildingManager.m_buildings.m_size)
                return;

            try
            {
                MethodInfo showDefinition = null;
                MethodInfo[] methods = typeof(WorldInfoPanel).GetMethods(
                    BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method.Name == "Show"
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 2)
                    {
                        showDefinition = method;
                        break;
                    }
                }
                if (showDefinition == null)
                    return;

                InstanceID instance = default(InstanceID);
                instance.Building = buildingId;
                Vector3 position = buildingManager
                    .m_buildings.m_buffer[buildingId].m_position;
                showDefinition.MakeGenericMethod(panelType).Invoke(
                    null,
                    new object[] { position, instance });
            }
            catch (Exception e)
            {
                UndergroundParkingLog.Warning(
                    "Could not restore host building dialogue in car-park view: "
                    + e.Message);
            }
        }

        private void OnDelete(UIComponent component, UIMouseEventParameter parameter)
        {
            if (_hostMode)
                UndergroundParkingHostManager.RequestDelete(_buildingId);
        }

        private void OnDestroy()
        {
            if (_dragHandle != null)
            {
                _dragHandle.eventMouseDown -= OnPanelDragStarted;
                _dragHandle.eventMouseUp -= OnPanelDragFinished;
            }
            if (_xrayHostPanel != null)
                _xrayHostPanel.eventVisibilityChanged -=
                    OnXrayHostVisibilityChanged;
            if (_panel != null)
                UnityEngine.Object.Destroy(_panel.gameObject);
            _dragHandle = null;
            _panel = null;
            _cachedSummary = null;
            _trackedHostSummary = null;
            _hasTrackedHostPosition = false;
            if (Instance == this)
                Instance = null;
        }
    }
}
