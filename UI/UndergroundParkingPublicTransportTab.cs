using ColossalFramework.UI;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace UndergroundParkingGarage
{
    internal class UndergroundParkingPublicTransportTab : MonoBehaviour
    {
        private const string ComponentName = "UndergroundParkingGaragePublicTransportTab";
        private const string TabName = "UndergroundParkingGarageTab";
        private const string PageName = "UndergroundParkingGaragePage";
        private const string TabIconName = "UndergroundParkingGarageTabIcon";
        private const string TabIconSpriteName = "UndergroundParkingGarageBlueWhiteP";
        private const float RetrySeconds = 0.75f;
        private const int LogAttemptLimit = 8;

        private static UITextureAtlas _tabIconAtlas;

        private float _nextAttemptTime;
        private int _attempts;
        private bool _installed;
        private UITabstrip _tabstrip;
        private UIButton _parkingTab;
        private UIComponent _parkingPage;
        private UndergroundParkingPanel _parkingPanel;
        private int _tabIndex = -1;
        private UIComponent _publicTransportPanel;
        private UITabstrip _rootTabstrip;
        private UIComponent _publicTransportPage;
        private int _publicTransportPageIndex = -1;
        private bool _wasOpen;

        public static void EnsureOnRoot(GameObject root)
        {
            if (root == null)
                return;

            UndergroundParkingPublicTransportTab existing = root.GetComponent<UndergroundParkingPublicTransportTab>();
            if (existing == null)
            {
                existing = root.AddComponent<UndergroundParkingPublicTransportTab>();
                existing.name = ComponentName;
            }
        }

        private void LateUpdate()
        {
            long startedAt = UndergroundParkingTabPerformanceDiagnostics.BeginCallbackSample();
            try
            {
                if (_installed)
                {
                    if (_parkingTab == null
                        || _parkingPage == null
                        || _parkingPanel == null
                        || _tabstrip == null
                        || !IsPublicTransportTabstrip(_tabstrip))
                    {
                        ClearInstalledReferences();
                    }
                    else
                    {
                        SyncPageVisibility();
                        UpdateEffectiveOpenState();
                    }

                    return;
                }

                if (Time.realtimeSinceStartup < _nextAttemptTime)
                    return;

                _nextAttemptTime = Time.realtimeSinceStartup + RetrySeconds;
                _attempts++;
                TryInstall();
            }
            finally
            {
                UndergroundParkingTabPerformanceDiagnostics.EndTabUpdate(startedAt);
                UndergroundParkingTabPerformanceDiagnostics.ObserveRenderedFrame(
                    HasSelectedPageOwnership());
            }
        }

        private void OnDestroy()
        {
            RemoveInstalledUi();
            UndergroundParkingTabPerformanceDiagnostics.Reset();
        }

        private void TryInstall()
        {
            UITabstrip tabstrip = FindPublicTransportTabstrip();
            if (tabstrip == null)
            {
                if (_attempts <= LogAttemptLimit)
                    UndergroundParkingLog.Advanced("Public Transport tabstrip not ready; waiting. attempt=" + _attempts);
                return;
            }

            UIButton existing = FindChild(tabstrip, TabName) as UIButton;
            if (existing != null)
            {
                UIComponent existingPage = FindChild(tabstrip.tabPages, PageName);
                if (existingPage == null)
                {
                    UnityEngine.Object.Destroy(existing.gameObject);
                    UndergroundParkingLog.Warning(
                        "Removed incomplete Underground Parking tab without its page; installation will retry.");
                    return;
                }
                UndergroundParkingPanel existingPanel =
                    existingPage.GetComponentInChildren<UndergroundParkingPanel>();
                if (existingPanel == null)
                    existingPanel = existingPage.AddUIComponent<UndergroundParkingPanel>();
                ConfigureParkingTab(tabstrip, existing);
                AdoptInstalledUi(tabstrip, existing, existingPage, existingPanel);
                return;
            }

            UITabContainer tabPages = tabstrip.tabPages;
            if (tabPages == null)
            {
                UndergroundParkingLog.Warning("Public Transport tabstrip has no tab page container; cannot add Underground Parking tab.");
                return;
            }

            int oldPageCount = tabPages.components.Count;
            UIButton tab = tabstrip.AddTab(string.Empty, true);
            if (tab == null)
            {
                UndergroundParkingLog.Warning("Public Transport tabstrip refused Underground Parking tab.");
                return;
            }

            tab.name = TabName;
            tab.tooltip = "Underground Parking Garage";
            ConfigureParkingTab(tabstrip, tab);

            UIComponent page = null;
            if (tabPages.components.Count > oldPageCount)
                page = tabPages.components[oldPageCount];
            if (page == null)
                page = tabPages.AddTabPage("Parking");

            if (page == null)
            {
                UnityEngine.Object.Destroy(tab.gameObject);
                UndergroundParkingLog.Warning("Public Transport tab page creation failed for Underground Parking.");
                return;
            }

            page.name = PageName;
            page.isVisible = false;
            UndergroundParkingPanel panel = page.GetComponentInChildren<UndergroundParkingPanel>();
            if (panel == null)
                panel = page.AddUIComponent<UndergroundParkingPanel>();

            panel.relativePosition = new Vector3(12f, 12f);
            AdoptInstalledUi(tabstrip, tab, page, panel);
            UndergroundParkingLog.Advanced("Added Underground Parking tab to Public Transport toolbar: tabstrip="
                                        + GetPath(tabstrip)
                                        + " page="
                                        + GetPath(page));
        }

        private void OnParkingTabClicked(UIComponent component, UIMouseEventParameter eventParam)
        {
            UndergroundParkingTabPerformanceDiagnostics.RecordTabClick(
                _tabIndex,
                _tabstrip == null ? -1 : _tabstrip.selectedIndex);
            UndergroundParkingBuildingPlacement.ClearExternalPlacementState();

            if (_tabstrip != null && _tabIndex >= 0)
                _tabstrip.selectedIndex = _tabIndex;

            if (_parkingPage != null)
            {
                _parkingPage.isVisible = true;
                _parkingPage.BringToFront();
            }

            UndergroundParkingPanel.RefreshInstance();
        }

        private void AdoptInstalledUi(
            UITabstrip tabstrip,
            UIButton tab,
            UIComponent page,
            UndergroundParkingPanel panel)
        {
            _tabstrip = tabstrip;
            _parkingTab = tab;
            _parkingPage = page;
            _parkingPanel = panel;
            _tabIndex = FindTabIndex(tabstrip, tab);
            _publicTransportPanel = UIView.Find<UIPanel>("PublicTransportPanel");
            _rootTabstrip = FindOwningTabstrip(
                _publicTransportPanel,
                out _publicTransportPage);
            _publicTransportPageIndex = FindPageIndex(
                _rootTabstrip == null ? null : _rootTabstrip.tabPages,
                _publicTransportPage);
            tab.eventClick -= OnParkingTabClicked;
            tab.eventClick += OnParkingTabClicked;
            tabstrip.eventSelectedIndexChanged -= OnSelectedIndexChanged;
            tabstrip.eventSelectedIndexChanged += OnSelectedIndexChanged;
            if (_rootTabstrip != null)
            {
                _rootTabstrip.eventSelectedIndexChanged -= OnRootSelectedIndexChanged;
                _rootTabstrip.eventSelectedIndexChanged += OnRootSelectedIndexChanged;
            }
            page.eventVisibilityChanged -= OnParkingPageVisibilityChanged;
            page.eventVisibilityChanged += OnParkingPageVisibilityChanged;
            _installed = true;
            SyncPageVisibility();
            _wasOpen = HasSelectedPageOwnership();
            UndergroundParkingPanel.RefreshInstance();
        }

        private void OnSelectedIndexChanged(UIComponent component, int selectedIndex)
        {
            SyncPageVisibility();
            UpdateEffectiveOpenState();
        }

        private void OnRootSelectedIndexChanged(UIComponent component, int selectedIndex)
        {
            UpdateEffectiveOpenState();
        }

        private void OnParkingPageVisibilityChanged(UIComponent component, bool visible)
        {
            UpdateEffectiveOpenState();
        }

        private void SyncPageVisibility()
        {
            if (_tabstrip == null || _parkingTab == null || _parkingPage == null)
                return;
            if (_tabIndex < 0)
                _tabIndex = FindTabIndex(_tabstrip, _parkingTab);

            bool selected = IsSelected(_parkingTab, _tabstrip, _tabIndex);
            bool visibilityChanged = _parkingPage.isVisible != selected;
            if (visibilityChanged)
                _parkingPage.isVisible = selected;
            if (selected && visibilityChanged)
                _parkingPage.BringToFront();
        }

        private bool HasSelectedPageOwnership()
        {
            if (!_installed
                || _tabstrip == null
                || _parkingPage == null
                || _parkingPanel == null
                || _tabIndex < 0
                || !IsSelected(_parkingTab, _tabstrip, _tabIndex)
                || !IsEffectivelyVisible(_parkingPage)
                || !IsEffectivelyVisible(_publicTransportPanel))
            {
                return false;
            }

            return _rootTabstrip != null
                   && _publicTransportPageIndex >= 0
                   && _rootTabstrip.selectedIndex == _publicTransportPageIndex;
        }

        private void UpdateEffectiveOpenState()
        {
            bool open = HasSelectedPageOwnership();
            if (_wasOpen == open)
                return;
            _wasOpen = open;
            if (open)
                UndergroundParkingPanel.RefreshInstance();
            else
                UndergroundParkingBuildingPlacement.ClearExternalPlacementState();
        }

        private void ClearInstalledReferences()
        {
            if (_tabstrip != null)
                _tabstrip.eventSelectedIndexChanged -= OnSelectedIndexChanged;
            if (_rootTabstrip != null)
                _rootTabstrip.eventSelectedIndexChanged -= OnRootSelectedIndexChanged;
            if (_parkingTab != null)
                _parkingTab.eventClick -= OnParkingTabClicked;
            if (_parkingPage != null)
                _parkingPage.eventVisibilityChanged -= OnParkingPageVisibilityChanged;

            _installed = false;
            _tabstrip = null;
            _parkingTab = null;
            _parkingPage = null;
            _parkingPanel = null;
            _tabIndex = -1;
            _publicTransportPanel = null;
            _rootTabstrip = null;
            _publicTransportPage = null;
            _publicTransportPageIndex = -1;
            _wasOpen = false;
        }

        private void RemoveInstalledUi()
        {
            if (_tabstrip != null)
                _tabstrip.eventSelectedIndexChanged -= OnSelectedIndexChanged;
            if (_rootTabstrip != null)
                _rootTabstrip.eventSelectedIndexChanged -= OnRootSelectedIndexChanged;

            if (_parkingTab != null)
            {
                _parkingTab.eventClick -= OnParkingTabClicked;
                UnityEngine.Object.Destroy(_parkingTab.gameObject);
            }
            if (_parkingPage != null)
            {
                _parkingPage.eventVisibilityChanged -= OnParkingPageVisibilityChanged;
                UnityEngine.Object.Destroy(_parkingPage.gameObject);
            }
            ClearInstalledReferences();
        }

        private static UITabstrip FindPublicTransportTabstrip()
        {
            UIPanel publicTransportPanel = UIView.Find<UIPanel>("PublicTransportPanel");
            return FindBestTabstrip(publicTransportPanel);
        }

        private static UITabstrip FindBestTabstrip(Component root)
        {
            if (root == null)
                return null;

            UITabstrip[] tabstrips = root.GetComponentsInChildren<UITabstrip>(true);
            UITabstrip best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < tabstrips.Length; i++)
            {
                UITabstrip candidate = tabstrips[i];
                if (!IsPublicTransportTabstrip(candidate))
                    continue;

                int score = ScoreTabstrip(candidate);
                if (score <= bestScore)
                    continue;

                best = candidate;
                bestScore = score;
            }

            return bestScore >= 100 ? best : null;
        }

        private static bool IsPublicTransportTabstrip(UITabstrip tabstrip)
        {
            if (tabstrip == null)
                return false;

            Transform current = tabstrip.transform;
            while (current != null)
            {
                UIComponent component = current.GetComponent<UIComponent>();
                if (component != null
                    && component.name == "PublicTransportPanel")
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static int ScoreTabstrip(UITabstrip tabstrip)
        {
            if (tabstrip == null)
                return int.MinValue;

            string path = GetPath(tabstrip).ToLowerInvariant();
            int score = 0;
            if (path.IndexOf("publictransportpanel") >= 0)
                score += 100;
            if (path.IndexOf("publictransport") >= 0)
                score += 40;
            if (path.IndexOf("m_tabstrip") >= 0 || path.IndexOf("tabstrip") >= 0)
                score += 10;
            if (path.IndexOf("elevation") >= 0)
                score -= 80;
            if (tabstrip.tabPages != null)
                score += 20;
            score += Mathf.Min(20, tabstrip.tabCount * 2);
            return score;
        }

        private static void ConfigureParkingTab(UITabstrip tabstrip, UIButton tab)
        {
            if (tabstrip == null || tab == null)
                return;

            UIButton template = FindTemplateButton(tabstrip, tab);
            if (template != null)
            {
                tab.atlas = template.atlas;
                tab.normalBgSprite = template.normalBgSprite;
                tab.hoveredBgSprite = template.hoveredBgSprite;
                tab.pressedBgSprite = template.pressedBgSprite;
                tab.focusedBgSprite = template.focusedBgSprite;
                tab.disabledBgSprite = template.disabledBgSprite;
                tab.width = template.width;
                tab.height = template.height;
            }
            else
            {
                tab.normalBgSprite = "ButtonMenu";
                tab.hoveredBgSprite = "ButtonMenuHovered";
                tab.pressedBgSprite = "ButtonMenuPressed";
                tab.focusedBgSprite = "ButtonMenuPressed";
                tab.disabledBgSprite = "ButtonMenuDisabled";
            }

            tab.text = string.Empty;
            tab.normalFgSprite = string.Empty;
            tab.hoveredFgSprite = string.Empty;
            tab.pressedFgSprite = string.Empty;
            tab.focusedFgSprite = string.Empty;
            tab.disabledFgSprite = string.Empty;
            tab.tooltip = "Underground Parking Garage";
            tab.isVisible = true;
            tab.isEnabled = true;

            UISprite icon = tab.Find<UISprite>(TabIconName);
            if (icon == null)
            {
                icon = tab.AddUIComponent<UISprite>();
                icon.name = TabIconName;
            }

            UITextureAtlas iconAtlas = GetOrCreateTabIconAtlas();
            icon.atlas = iconAtlas;
            icon.spriteName = TabIconSpriteName;
            float iconSize = Mathf.Min(
                28f,
                Mathf.Max(20f, Mathf.Min(tab.width - 12f, tab.height - 10f)));
            icon.width = iconSize;
            icon.height = iconSize;
            icon.relativePosition = new Vector3(
                Mathf.Max(0f, (tab.width - icon.width) * 0.5f),
                Mathf.Max(0f, (tab.height - icon.height) * 0.5f));
            icon.isInteractive = false;
            icon.isVisible = iconAtlas != null;
            if (iconAtlas != null)
                icon.BringToFront();
            else
            {
                tab.text = "P";
                tab.textScale = 1.05f;
            }
        }

        private static UITextureAtlas GetOrCreateTabIconAtlas()
        {
            if (_tabIconAtlas != null)
                return _tabIconAtlas;

            UIView view = UIView.GetAView();
            if (view == null
                || view.defaultAtlas == null
                || view.defaultAtlas.material == null)
            {
                return null;
            }

            Texture2D texture = CreateBlueWhiteParkingIconTexture();
            Material material = new Material(view.defaultAtlas.material);
            material.mainTexture = texture;
            UITextureAtlas atlas = ScriptableObject.CreateInstance<UITextureAtlas>();
            atlas.name = "UndergroundParkingGarageTabIconAtlas";
            atlas.material = material;
            atlas.AddSprite(new UITextureAtlas.SpriteInfo
            {
                name = TabIconSpriteName,
                texture = texture,
                region = new Rect(0f, 0f, 1f, 1f),
                border = new RectOffset()
            });
            _tabIconAtlas = atlas;
            return _tabIconAtlas;
        }

        private static Texture2D CreateBlueWhiteParkingIconTexture()
        {
            const int size = 128;
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 blue = new Color32(0, 102, 178, 255);
            Color32 white = new Color32(254, 254, 254, 255);
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            // Preserve a true square sign regardless of the native tab's
            // dimensions. The narrow white perimeter gives the exact-blue
            // field a finished road-sign edge, and the deliberately taller,
            // narrower glyph remains a recognisable P after mip filtering.
            FillRoundedTabIconRect(pixels, size, 4, 4, 120, 120, 14, white);
            FillRoundedTabIconRect(pixels, size, 10, 10, 108, 108, 10, blue);
            FillTabIconRect(pixels, size, 42, 27, 15, 76, white);
            FillRoundedTabIconRect(pixels, size, 50, 67, 41, 36, 14, white);
            FillRoundedTabIconRect(pixels, size, 58, 75, 22, 20, 7, blue);

            Texture2D texture = new Texture2D(
                size,
                size,
                TextureFormat.ARGB32,
                true);
            texture.name = "Underground Parking Garage blue-white P tab icon";
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static void FillTabIconRect(
            Color32[] pixels,
            int size,
            int x,
            int y,
            int width,
            int height,
            Color32 color)
        {
            int minX = Mathf.Clamp(x, 0, size);
            int maxX = Mathf.Clamp(x + width, 0, size);
            int minY = Mathf.Clamp(y, 0, size);
            int maxY = Mathf.Clamp(y + height, 0, size);
            for (int py = minY; py < maxY; py++)
            {
                int row = py * size;
                for (int px = minX; px < maxX; px++)
                    pixels[row + px] = color;
            }
        }

        private static void FillRoundedTabIconRect(
            Color32[] pixels,
            int size,
            int x,
            int y,
            int width,
            int height,
            int radius,
            Color32 color)
        {
            int minX = Mathf.Clamp(x, 0, size);
            int maxX = Mathf.Clamp(x + width, 0, size);
            int minY = Mathf.Clamp(y, 0, size);
            int maxY = Mathf.Clamp(y + height, 0, size);
            float clampedRadius = Mathf.Max(0f, Mathf.Min(
                radius,
                Mathf.Min(width, height) * 0.5f));
            float left = x + clampedRadius;
            float right = x + width - clampedRadius;
            float bottom = y + clampedRadius;
            float top = y + height - clampedRadius;
            float radiusSquared = clampedRadius * clampedRadius;
            for (int py = minY; py < maxY; py++)
            {
                int row = py * size;
                for (int px = minX; px < maxX; px++)
                {
                    float nearestX = Mathf.Clamp(px + 0.5f, left, right);
                    float nearestY = Mathf.Clamp(py + 0.5f, bottom, top);
                    float dx = px + 0.5f - nearestX;
                    float dy = py + 0.5f - nearestY;
                    if (dx * dx + dy * dy <= radiusSquared)
                        pixels[row + px] = color;
                }
            }
        }

        private static UIButton FindTemplateButton(UITabstrip tabstrip, UIButton parkingTab)
        {
            if (tabstrip == null)
                return null;

            UIButton[] buttons = tabstrip.GetComponentsInChildren<UIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                UIButton button = buttons[i];
                if (button == null || button == parkingTab || button.name == TabName)
                    continue;

                if (button.transform.parent == tabstrip.transform
                    && button.width >= 24f
                    && button.height >= 24f)
                    return button;
            }

            return null;
        }

        private static UIComponent FindChild(UIComponent parent, string childName)
        {
            if (parent == null)
                return null;

            UIComponent[] children = parent.GetComponentsInChildren<UIComponent>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].name == childName)
                    return children[i];
            }

            return null;
        }

        private static int FindTabIndex(UITabstrip tabstrip, UIButton tab)
        {
            if (tabstrip == null || tab == null)
                return -1;
            int tabIndex = 0;
            for (int i = 0; i < tabstrip.components.Count; i++)
            {
                UIButton candidate = tabstrip.components[i] as UIButton;
                if (candidate == null || candidate.transform.parent != tabstrip.transform)
                    continue;
                if (candidate == tab)
                    return tabIndex;
                tabIndex++;
            }
            return -1;
        }

        private static bool IsSelected(
            UIButton tab,
            UITabstrip tabstrip,
            int tabIndex)
        {
            if (tab == null || tabstrip == null)
                return false;
            return tabIndex >= 0
                ? tabstrip.selectedIndex == tabIndex
                : tab.state == UIButton.ButtonState.Focused;
        }

        private static UITabstrip FindOwningTabstrip(
            UIComponent component,
            out UIComponent owningPage)
        {
            owningPage = null;
            UIView view = UIView.GetAView();
            if (component == null || view == null)
                return null;

            UITabstrip[] tabstrips = view.GetComponentsInChildren<UITabstrip>(true);
            UIComponent candidatePage = component;
            while (candidatePage != null)
            {
                UITabContainer pages = candidatePage.parent as UITabContainer;
                if (pages != null)
                {
                    for (int i = 0; i < tabstrips.Length; i++)
                    {
                        if (tabstrips[i] != null && tabstrips[i].tabPages == pages)
                        {
                            owningPage = candidatePage;
                            return tabstrips[i];
                        }
                    }
                }
                candidatePage = candidatePage.parent;
            }
            return null;
        }

        private static int FindPageIndex(UITabContainer pages, UIComponent page)
        {
            if (pages == null || page == null)
                return -1;
            for (int i = 0; i < pages.components.Count; i++)
            {
                if (pages.components[i] == page)
                    return i;
            }
            return -1;
        }

        private static bool IsEffectivelyVisible(UIComponent component)
        {
            while (component != null)
            {
                if (!component.isVisible)
                    return false;
                component = component.parent;
            }
            return true;
        }

        private static string GetPath(UIComponent component)
        {
            if (component == null)
                return string.Empty;

            string path = component.name;
            Transform current = component.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }

    internal static class UndergroundParkingTabPerformanceDiagnostics
    {
        private const float ClosedBaselineSeconds = 10f;
        private const float OpenWindowSeconds = 10f;
        private const float PostCloseWindowSeconds = 5f;

        private static bool _armed;
        private static bool _tabOpen;
        private static bool _awaitingPostClose;
        private static float _elapsedSeconds;
        private static int _frames;
        private static double _frameTotalMs;
        private static double _worstFrameMs;
        private static TimingAccumulator _tabUpdate;
        private static TimingAccumulator _panelRepaint;

        internal static long BeginCallbackSample()
        {
            return UndergroundParkingGarageSettings.AdvancedDiagnostics
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        internal static void EndTabUpdate(long startedAt)
        {
            Record(ref _tabUpdate, startedAt);
        }

        internal static void EndPanelRepaint(long startedAt)
        {
            Record(ref _panelRepaint, startedAt);
        }

        internal static void RecordTabClick(int tabIndex, int selectedIndex)
        {
            if (UndergroundParkingGarageSettings.AdvancedDiagnostics)
            {
                UndergroundParkingLog.Advanced(
                    "UPG-tab performance event: click tabIndex=" + tabIndex
                    + " selectedIndex=" + selectedIndex + ".");
            }
        }

        internal static void ObserveRenderedFrame(bool tabOpen)
        {
            if (!UndergroundParkingGarageSettings.AdvancedDiagnostics)
            {
                if (_armed)
                    Reset();
                return;
            }

            if (!_armed)
            {
                _armed = true;
                _tabOpen = tabOpen;
                ResetWindow();
                UndergroundParkingLog.Advanced(
                    "UPG-tab performance diagnostics armed: initialOpen=" + tabOpen + ".");
            }

            if (_tabOpen != tabOpen)
            {
                LogSnapshot(_tabOpen ? "open-final" : "pre-open-baseline", _tabOpen);
                _awaitingPostClose = _tabOpen;
                _tabOpen = tabOpen;
                UndergroundParkingLog.Advanced(
                    "UPG-tab performance transition: open=" + tabOpen + ".");
                ResetWindow();
            }

            float frameSeconds = Time.unscaledDeltaTime;
            if (frameSeconds > 0f
                && !float.IsNaN(frameSeconds)
                && !float.IsInfinity(frameSeconds))
            {
                double frameMs = frameSeconds * 1000d;
                _elapsedSeconds += frameSeconds;
                _frames++;
                _frameTotalMs += frameMs;
                if (frameMs > _worstFrameMs)
                    _worstFrameMs = frameMs;
            }

            if (_tabOpen && _elapsedSeconds >= OpenWindowSeconds)
            {
                LogSnapshot("open-window", true);
                ResetWindow();
            }
            else if (!_tabOpen
                     && _awaitingPostClose
                     && _elapsedSeconds >= PostCloseWindowSeconds)
            {
                LogSnapshot("post-close-window", false);
                _awaitingPostClose = false;
                ResetWindow();
            }
            else if (!_tabOpen && _elapsedSeconds >= ClosedBaselineSeconds)
            {
                LogSnapshot("closed-baseline", false);
                ResetWindow();
            }
        }

        internal static void Reset()
        {
            _armed = false;
            _tabOpen = false;
            _awaitingPostClose = false;
            ResetWindow();
        }

        private static void Record(ref TimingAccumulator timing, long startedAt)
        {
            if (startedAt == 0L || !UndergroundParkingGarageSettings.AdvancedDiagnostics)
                return;
            long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            if (elapsedTicks >= 0L)
                timing.Record(elapsedTicks * 1000d / Stopwatch.Frequency);
        }

        private static void LogSnapshot(string label, bool tabOpen)
        {
            double averageFrameMs = _frames > 0 ? _frameTotalMs / _frames : 0d;
            UndergroundParkingLog.Advanced(
                "UPG-tab performance sample: label=" + label
                + " open=" + tabOpen
                + " seconds=" + _elapsedSeconds.ToString("0.00")
                + " frames=" + _frames
                + " avgFrameMs=" + averageFrameMs.ToString("0.00")
                + " approxFps=" + (averageFrameMs > 0.001d ? 1000d / averageFrameMs : 0d).ToString("0.0")
                + " worstFrameMs=" + _worstFrameMs.ToString("0.00")
                + " tabUpdate=" + _tabUpdate.Format()
                + " panelRepaint=" + _panelRepaint.Format()
                + ".");
        }

        private static void ResetWindow()
        {
            _elapsedSeconds = 0f;
            _frames = 0;
            _frameTotalMs = 0d;
            _worstFrameMs = 0d;
            _tabUpdate = default(TimingAccumulator);
            _panelRepaint = default(TimingAccumulator);
        }

        private struct TimingAccumulator
        {
            internal int Count;
            internal double TotalMs;
            internal double WorstMs;

            internal void Record(double elapsedMs)
            {
                Count++;
                TotalMs += elapsedMs;
                if (elapsedMs > WorstMs)
                    WorstMs = elapsedMs;
            }

            internal string Format()
            {
                return "count:" + Count
                       + ",avgMs:" + (Count > 0 ? TotalMs / Count : 0d).ToString("0.000")
                       + ",worstMs:" + WorstMs.ToString("0.000");
            }
        }
    }
}
