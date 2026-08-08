using ColossalFramework.UI;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal class UndergroundParkingPublicTransportTab : MonoBehaviour
    {
        private const string ComponentName = "UndergroundParkingGaragePublicTransportTab";
        private const string TabName = "UndergroundParkingGarageTab";
        private const string PageName = "UndergroundParkingGaragePage";
        private const float RetrySeconds = 0.75f;
        private const int LogAttemptLimit = 8;

        private float _nextAttemptTime;
        private int _attempts;
        private bool _installed;
        private UITabstrip _tabstrip;
        private UIButton _parkingTab;
        private UIComponent _parkingPage;

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
            if (_installed)
            {
                if (_parkingTab == null
                    || _tabstrip == null
                    || !IsPublicTransportTabstrip(_tabstrip))
                {
                    _installed = false;
                    _parkingTab = null;
                    _parkingPage = null;
                    _tabstrip = null;
                }

                return;
            }

            if (Time.realtimeSinceStartup < _nextAttemptTime)
                return;

            _nextAttemptTime = Time.realtimeSinceStartup + RetrySeconds;
            _attempts++;
            TryInstall();
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
                _tabstrip = tabstrip;
                _parkingTab = existing;
                _parkingPage = FindChild(tabstrip.tabPages, PageName);
                ConfigureParkingTab(tabstrip, existing);
                _installed = true;
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
            tab.eventClick += OnParkingTabClicked;
            ConfigureParkingTab(tabstrip, tab);

            UIComponent page = null;
            if (tabPages.components.Count > oldPageCount)
                page = tabPages.components[oldPageCount];
            if (page == null)
                page = tabPages.AddTabPage("Parking");

            if (page == null)
            {
                UndergroundParkingLog.Warning("Public Transport tab page creation failed for Underground Parking.");
                return;
            }

            page.name = PageName;
            page.isVisible = false;
            UndergroundParkingPanel panel = page.GetComponentInChildren<UndergroundParkingPanel>();
            if (panel == null)
                panel = page.AddUIComponent<UndergroundParkingPanel>();

            panel.relativePosition = new Vector3(12f, 12f);
            _tabstrip = tabstrip;
            _parkingTab = tab;
            _parkingPage = page;
            _installed = true;
            UndergroundParkingLog.Advanced("Added Underground Parking tab to Public Transport toolbar: tabstrip="
                                        + GetPath(tabstrip)
                                        + " page="
                                        + GetPath(page));
        }

        private void OnParkingTabClicked(UIComponent component, UIMouseEventParameter eventParam)
        {
            UndergroundParkingBuildingPlacement.ClearExternalPlacementState();

            if (_parkingPage != null)
            {
                _parkingPage.isVisible = true;
                _parkingPage.BringToFront();
            }

            UndergroundParkingPanel.RefreshInstance();
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
            }
            else
            {
                tab.normalBgSprite = "ButtonMenu";
                tab.hoveredBgSprite = "ButtonMenuHovered";
                tab.pressedBgSprite = "ButtonMenuPressed";
                tab.focusedBgSprite = "ButtonMenuPressed";
                tab.disabledBgSprite = "ButtonMenuDisabled";
            }

            tab.text = "P";
            tab.textScale = 1.05f;
            tab.tooltip = "Underground Parking Garage";
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
}
