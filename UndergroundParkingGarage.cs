using ColossalFramework.UI;
using ColossalFramework.Plugins;
using ICities;
using ScratchyBald.CitiesSkylines.UI;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingFeatures
    {
        public static readonly bool PlacementEnabled = true;
        public static readonly bool ParkingOccupancyEnabled = true;
    }

    internal static class UndergroundParkingGarageSettings
    {
        private const string SuppressAttachedEntranceVisualsKey =
            "UndergroundParkingGarage.SuppressAttachedEntranceVisuals.V2";
        private const string AdvancedDiagnosticsKey =
            "UndergroundParkingGarage.AdvancedDiagnostics";

        public static bool AdvancedDiagnostics
        {
            get { return PlayerPrefs.GetInt(AdvancedDiagnosticsKey, 0) != 0; }
            set
            {
                bool changed = AdvancedDiagnostics != value;
                PlayerPrefs.SetInt(AdvancedDiagnosticsKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (changed)
                {
                    UndergroundParkingLog.Info(
                        "Advanced diagnostics " +
                        (value ? "enabled." : "disabled."));
                }
            }
        }

        public static bool SuppressAttachedEntranceVisuals
        {
            get { return PlayerPrefs.GetInt(SuppressAttachedEntranceVisualsKey, 0) != 0; }
            set
            {
                PlayerPrefs.SetInt(SuppressAttachedEntranceVisualsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }

    public class UndergroundParkingGarageMod : IUserMod
    {
        public string Name
        {
            get { return "Underground Parking Garage"; }
        }

        public string Description
        {
            get { return "Add underground parking through a roadside entrance or beneath a selected building."; }
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            UIHelperBase diagnosticsGroup = helper.AddGroup("Diagnostics");
            diagnosticsGroup.AddCheckbox(
                "Enable advanced logs",
                UndergroundParkingGarageSettings.AdvancedDiagnostics,
                value => UndergroundParkingGarageSettings.AdvancedDiagnostics = value);

            UIHelperBase visualGroup = helper.AddGroup("Building-attached garage entrances");
            visualGroup.AddCheckbox(
                "Hide UPG parking signs and entrance ramps for all attached garages",
                UndergroundParkingGarageSettings.SuppressAttachedEntranceVisuals,
                value =>
                {
                    UndergroundParkingGarageSettings.SuppressAttachedEntranceVisuals = value;
                    UndergroundParkingHostManager.RequestSetAllEntranceVisuals(!value);
                });

            UIHelperBase resetGroup = helper.AddGroup("Safe removal and city reset");
            resetGroup.AddButton(
                "NUKE all underground parking facilities",
                () => ConfirmPanel.ShowModal(
                    "Nuke Underground Parking",
                    "This permanently removes every Underground Parking Garage facility from the loaded city, releases its stored parking records back to the road, and bulldozes every standalone entrance kiosk.\n\nThis cannot be undone. Continue?",
                    (component, result) =>
                    {
                        if (result == 1)
                            UndergroundParkingCityReset.Request();
                    }));
        }
    }

    internal static class UndergroundParkingCityReset
    {
        public static void Request()
        {
            SimulationManager simulationManager = SimulationManager.instance;
            if (simulationManager == null || BuildingManager.instance == null)
            {
                ShowMessage(
                    "Nuke Underground Parking",
                    "Load a city before using the temporary reset button.");
                return;
            }

            simulationManager.AddAction(Execute);
        }

        private static void Execute()
        {
            UndergroundParkingBuildingPlacement.Deactivate();
            UndergroundParkingPlacementTool.Deactivate();
            UndergroundParkingPlacementPreview.Shutdown();

            // A car which has already passed the exact portal-admission gate
            // owns a valid pavement/parking transaction. Finish that existing
            // transaction before its facility is removed so shutdown never
            // restores the retained native body to a road stop with no route.
            UndergroundParkingOccupancyHarmony
                .CompletePortalArrivalsForCityReset();

            // Stop presentation first, but retain the live TM:PE relocation
            // owner through the first exact car/citizen release pass.
            UndergroundParkingPortalAnimationManager.Shutdown();

            int kiosksBulldozed;
            int facilitiesRemoved = UndergroundParkingRegistry.NukeAllFacilities(
                out kiosksBulldozed);
            UndergroundParkingOccupancyManager.ProcessPendingVanillaReleases(64);
            UndergroundParkingOccupancyHarmony.Release();
            UndergroundParkingOccupancyManager.Clear();

            if (UndergroundParkingFeatures.ParkingOccupancyEnabled)
            {
                UndergroundParkingOccupancyHarmony.RefreshForFacilityCount();
                UndergroundParkingOccupancyManager.RebuildAll();
            }

            UndergroundParkingPortalAnimationManager.Initialize(null);
            UndergroundParkingVisualManager.RebuildAll();
            UndergroundParkingLog.Warning(
                "TEMPORARY CITY RESET completed: facilitiesRemoved="
                + facilitiesRemoved
                + " kiosksBulldozed="
                + kiosksBulldozed
                + " remainingFacilities="
                + UndergroundParkingRegistry.Count
                + " pendingVanillaReleases="
                + UndergroundParkingOccupancyManager.PendingVanillaReleaseCount);
        }

        private static void ShowMessage(string title, string message)
        {
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
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning(
                    "Could not show temporary reset status: " + e.Message);
            }
        }
    }

    internal static class UndergroundParkingDisableGuard
    {
        private static bool _installed;
        private static bool _restoringEnabledState;

        public static void Install()
        {
            if (_installed)
                return;

            PluginManager pluginManager = PluginManager.instance;
            if (pluginManager == null)
                return;

            pluginManager.eventPluginsStateChanged += OnPluginsStateChanged;
            _installed = true;
        }

        private static void OnPluginsStateChanged()
        {
            if (_restoringEnabledState || UndergroundParkingRegistry.Count == 0)
                return;

            PluginManager pluginManager = PluginManager.instance;
            if (pluginManager == null)
                return;

            PluginManager.PluginInfo plugin =
                pluginManager.FindPluginInfo(typeof(UndergroundParkingGarageMod).Assembly);
            if (plugin == null || plugin.isEnabled)
                return;

            _restoringEnabledState = true;
            try
            {
                plugin.isEnabled = true;
                string message =
                    "Underground Parking Garage stayed enabled because the last loaded city still contains "
                    + UndergroundParkingRegistry.Count
                    + " live underground parking facilit"
                    + (UndergroundParkingRegistry.Count == 1 ? "y" : "ies")
                    + ".\n\nLoad that city, use \"NUKE all underground parking facilities\", save to a new slot, "
                    + "exit the game completely, and then disable or unsubscribe the mod.";
                UndergroundParkingLog.Warning(
                    "Blocked UPG deactivation while live facilities remain: facilities="
                    + UndergroundParkingRegistry.Count);
                ShowMessage("Remove Underground Parking Garage safely", message);
            }
            finally
            {
                _restoringEnabledState = false;
            }
        }

        private static void ShowMessage(string title, string message)
        {
            try
            {
                if (UIView.library == null)
                    return;

                ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                if (panel != null)
                    panel.SetMessage(title, message, false);
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Warning(
                    "Could not show the UPG safe-removal guard: " + e.Message);
            }
        }
    }

    public class UndergroundParkingGarageLoading : LoadingExtensionBase
    {
        private static readonly ReleaseNoticeContent ReleaseNotice = new ReleaseNoticeContent(
            "UndergroundParkingGarage.ShownReleaseNoticeId",
            "v2.3.0",
            "Underground Parking Garage 2.3.0",
            "Visible underground journeys and expanded garages",
            string.Empty,
            "UPG",
            new[]
            {
                "Watch cars follow tunnels and marked aisles to their spaces—and drive back out when retrieved.",
                "Building-attached garages can add a second underground floor for ₡25,000.",
                "Garage layouts now include dedicated circulation aisles and footprint-aware tunnel routes.",
                "Cars enter squarely through the entrance, while arrivals and departures use opposing sides.",
                "Surface buildings can be placed safely above buried garages.",
                "Safer garage removal and reset handling."
            },
            true,
            string.Empty,
            null,
            new[]
            {
                new ReleaseNoticeVersion("v2.2.0", "3 August 2026, 22:11 BST", new[]
                {
                    "Cars arrive, park and leave underground garages normally while TM:PE's More realistic parking option is enabled.",
                    "The building-attached Parking Management panel can be moved and remembers its position.",
                    "Detailed underground-parking diagnostics are now off by default and can be enabled temporarily in Options when troubleshooting."
                }, true),
                new ReleaseNoticeVersion("v2.1.1", "31 July 2026, 21:21 BST", new[]
                {
                    "Cars complete underground garage arrivals in affected existing cities using TM:PE Parking AI."
                }, true),
                new ReleaseNoticeVersion("v2.1.0", "30 July 2026, 11:54 BST", new[]
                {
                    "Adds the 3x3 Civic and 4x4 Grand standalone underground garages.",
                    "Each standalone size has its own P-tab icon, footprint and above/below-ground preview.",
                    "Improves Yet Another Toolbar, terrain, zoning and safe-removal behavior."
                }, true),
                new ReleaseNoticeVersion("v2.0.2", "29 July 2026, 02:14 BST", new[]
                {
                    "Saved standalone garages load safely with Ploppable RICO Revisited.",
                    "UPG adds only its compact P tab and stays out of unrelated asset previews.",
                    "TM:PE Parking AI cars complete underground arrivals correctly."
                }, true),
                new ReleaseNoticeVersion("v2.0.0", "22 July 2026, 15:59 BST", new[]
                {
                    "Cars visibly drive into and out of garages while preserving vehicles, occupants and routes.",
                    "Redesigns attached and standalone entrances plus detailed underground interiors.",
                    "Adds x-ray viewing, open/close and guarded floor, deletion and relocation controls.",
                    "Hardens saved-city recovery, capacity handling and complete parking journeys."
                }, true),
                new ReleaseNoticeVersion("v1.0.0", "12 July 2026, 12:18 BST", new[]
                {
                    "Initial release: build standalone or attached underground garages with real parked vehicles.",
                    "Adds x-ray interiors, guarded controls and protected underground footprints."
                }, false)
            });

        private GameObject _root;

        public override void OnCreated(ILoading loading)
        {
            base.OnCreated(loading);
            UndergroundParkingDisableGuard.Install();

            LoadingManager loadingManager = LoadingManager.instance;
            if (loadingManager == null)
            {
                UndergroundParkingLog.Error(
                    "Parking entrance prefab registration could not be queued because LoadingManager is unavailable.");
                return;
            }

            // Run only after every loading extension has returned from OnCreated.
            // Ploppable RICO Revisited patches BuildingInfo.InitializePrefab but
            // initializes the collections used by that prefix from its own
            // OnCreated callback. Direct registration here therefore depends on
            // arbitrary extension order and can make the saved kiosk prefab
            // temporarily unknown before RICO has initialized.
            loadingManager.QueueLoadingAction(RegisterRuntimePrefab());
        }

        private static System.Collections.IEnumerator RegisterRuntimePrefab()
        {
            UndergroundParkingBuildingPrefab.EnsurePrefab();
            yield break;
        }

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            if (mode != LoadMode.LoadGame && mode != LoadMode.NewGame)
                return;

            if (_root == null)
            {
                _root = new GameObject("UndergroundParkingGarageRoot");
                Object.DontDestroyOnLoad(_root);
            }

            UndergroundParkingBuildingPrefab.EnsurePrefab();
            UndergroundParkingPublicTransportTab.EnsureOnRoot(_root);
            UndergroundParkingBulldozeMonitor.EnsureOnRoot(_root);
            UndergroundParkingBuildingMonitor.EnsureOnRoot(_root);
            UndergroundParkingFloorPanel.EnsureOnRoot(_root);
            UndergroundParkingSolidVolumeHarmony.Apply();

            UndergroundParkingRegistry.RefreshSavedGeometry();
            UndergroundParkingRegistry.RemoveGeneratedConnectors();
            SimulationManager simulationManager = SimulationManager.instance;
            if (simulationManager != null)
            {
                simulationManager.AddAction(delegate
                {
                    UndergroundParkingBuildingAI.ApplyRetrospectiveZoningRepair();
                    UndergroundParkingRegistry.RemoveBuildingAttachedPropAnchors();
                });
            }
            UndergroundParkingRegistry.RefreshRoadConnections();
            UndergroundParkingRegistry.RefreshEntranceBuildingSelection();
            if (UndergroundParkingFeatures.ParkingOccupancyEnabled)
            {
                UndergroundParkingOccupancyHarmony.BeginLevel();
                UndergroundParkingOccupancyManager.RebuildAll();
            }
            else
            {
                UndergroundParkingOccupancyHarmony.Release();
                UndergroundParkingOccupancyManager.Clear();
                UndergroundParkingLog.Advanced("UPG parking occupancy integration disabled by feature flag.");
            }

            UndergroundParkingVisualManager.Initialize();
            UndergroundParkingVisualManager.RebuildAll();
            UndergroundParkingPortalAnimationManager.Initialize(_root);
            OneTimeUpdateNoticePanel.ShowIfNeeded(UIView.GetAView(), ReleaseNotice);

            UndergroundParkingLog.Info("Enabled. facilities="
                                        + UndergroundParkingRegistry.Count
                                        + " placement="
                                        + UndergroundParkingFeatures.PlacementEnabled
                                        + " parkingOccupancy="
                                        + UndergroundParkingFeatures.ParkingOccupancyEnabled
                                        + " parkingHooksActive="
                                        + UndergroundParkingOccupancyHarmony.IsApplied
                                        + " generatedConnectors=false advancedDiagnostics="
                                        + UndergroundParkingGarageSettings.AdvancedDiagnostics);
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();

            UndergroundParkingBuildingPlacement.Deactivate();
            UndergroundParkingPlacementTool.Deactivate();
            UndergroundParkingPlacementPreview.Shutdown();
            UndergroundParkingPortalAnimationManager.Shutdown();
            UndergroundParkingVisualManager.Shutdown();
            UndergroundParkingOccupancyHarmony.EndLevel();
            UndergroundParkingOccupancyManager.Clear();
            UndergroundParkingOccupancyManager.ClearPendingVanillaReleases();
            TmpeParkingCompatibilityManager.ReleaseRelocationServiceIfInactive();
            UndergroundParkingSolidVolumeHarmony.Release();
            UndergroundParkingBuildingPrefab.Release();
            OneTimeUpdateNoticePanel.DestroyInstance();
            UndergroundParkingPanel.DestroyInstance();
            UndergroundParkingFloorPanel.DestroyInstance();

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            UndergroundParkingRegistry.ClearTransient();
            UndergroundParkingLog.Info("Disabled.");
        }
    }

    public class UndergroundParkingGarageThreading : ThreadingExtensionBase
    {
        private uint _lastPortalFrame;
        private uint _lastOccupancyFrame;

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            base.OnUpdate(realTimeDelta, simulationTimeDelta);

            SimulationManager simulationManager = SimulationManager.instance;
            if (simulationManager == null)
                return;

            bool hasActiveParking =
                UndergroundParkingFeatures.ParkingOccupancyEnabled
                && UndergroundParkingRegistry.Count > 0;
            bool hasPendingVanillaReleases =
                UndergroundParkingOccupancyManager.HasPendingVanillaReleases;
            if (!hasActiveParking && !hasPendingVanillaReleases)
                return;

            uint frame = simulationManager.m_currentFrameIndex;
            if (_lastPortalFrame == 0u || frame - _lastPortalFrame >= 16u)
            {
                _lastPortalFrame = frame;
                UndergroundParkingOccupancyManager.UpdateHousekeeping();
                if (hasActiveParking)
                {
                    UndergroundParkingOccupancyHarmony.UpdateDeferredArrivalAssociations();
                    UndergroundParkingRegistry.ProcessPendingEntranceRelocations();
                }
            }

            if (!hasActiveParking)
                return;

            if (_lastOccupancyFrame != 0u && frame - _lastOccupancyFrame < 512u)
                return;

            _lastOccupancyFrame = frame;
            UndergroundParkingOccupancyManager.LogOccupancySnapshot();
        }

    }

    public class UndergroundParkingGarageSerializable : SerializableDataExtensionBase
    {
        // These legacy keys are part of the save-game format. Keep them stable so
        // cities created before the public rename continue to load their garages.
        private const string DataId = "ExperimentalUndergroundParking.Facilities.v1";
        private const string ParkingDataId = "ExperimentalUndergroundParking.ParkedAssignments.v1";
        private const string RebuildDataId = "ScratchyBald.UndergroundParkingGarage.Rebuild.v1";
        private const int RebuildDataMagic = 0x55504752;

        public override void OnLoadData()
        {
            base.OnLoadData();

            // A failed save-container read must never leave the previous city's
            // registry active in the newly loaded city.
            UndergroundParkingRegistry.Restore(null);
            UndergroundParkingOccupancyManager.StagePersistentAssignments(null);
            try
            {
                byte[] data = serializableDataManager.LoadData(DataId);
                int count = UndergroundParkingRegistry.Restore(data);
                if (data == null || data.Length == 0)
                    UndergroundParkingLog.Advanced("No saved underground parking garage facilities found.");

                UndergroundParkingOccupancyManager.StagePersistentAssignments(
                    serializableDataManager.LoadData(ParkingDataId));
                System.Collections.Generic.List<UndergroundParkingRegistry.ImportedParkedAssignment>
                    importedAssignments =
                        new System.Collections.Generic.List<UndergroundParkingRegistry.ImportedParkedAssignment>();
                string rebuildDataId;
                byte[] rebuildData = FindRebuildSnapshot(out rebuildDataId);
                int imported = UndergroundParkingRegistry.ImportBuildingAttachmentsFromRebuildSnapshot(
                    rebuildData,
                    importedAssignments);
                for (int i = 0; i < importedAssignments.Count; i++)
                {
                    UndergroundParkingRegistry.ImportedParkedAssignment assignment =
                        importedAssignments[i];
                    UndergroundParkingOccupancyManager.AppendPersistentAssignment(
                        assignment.ParkedId,
                        assignment.FacilityId,
                        assignment.SlotIndex,
                        assignment.PrefabName);
                }
                UndergroundParkingLog.Advanced("Restored underground parking garage facilities: legacy="
                                           + count
                                           + " importedAttached="
                                           + imported
                                           + " rebuildSource="
                                           + (string.IsNullOrEmpty(rebuildDataId)
                                               ? "missing"
                                               : rebuildDataId)
                                           + " total="
                                           + UndergroundParkingRegistry.Count);
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Error("Failed to restore underground parking garage facilities: " + e);
            }
        }

        private byte[] FindRebuildSnapshot(out string sourceDataId)
        {
            sourceDataId = null;
            byte[] exact = serializableDataManager.LoadData(RebuildDataId);
            if (HasRebuildMagic(exact))
            {
                sourceDataId = RebuildDataId;
                return exact;
            }

            // The consolidated implementation changed assembly/runtime ownership
            // several times during UAT. Cities: Skylines retains serializable-data
            // payloads by their stored key, so enumerate the loaded save container
            // and identify the rebuild payload by its own UPGR header instead of
            // assuming that every staged build wrote it under the final key.
            string[] dataIds = serializableDataManager.EnumerateData();
            if (dataIds == null)
                return null;

            for (int i = 0; i < dataIds.Length; i++)
            {
                string dataId = dataIds[i];
                if (string.IsNullOrEmpty(dataId)
                    || string.Equals(dataId, RebuildDataId, System.StringComparison.Ordinal))
                    continue;

                byte[] candidate;
                try
                {
                    candidate = serializableDataManager.LoadData(dataId);
                }
                catch (System.Exception e)
                {
                    UndergroundParkingLog.Warning(
                        "Could not inspect saved data entry while locating UPG rebuild state: id="
                        + dataId
                        + " reason="
                        + e.Message);
                    continue;
                }

                if (!HasRebuildMagic(candidate))
                    continue;

                sourceDataId = dataId;
                UndergroundParkingLog.Warning(
                    "Recovered UPG rebuild snapshot from staged save-data key: "
                    + dataId);
                return candidate;
            }

            UndergroundParkingLog.Warning(
                "No UPGR rebuild snapshot was present in the loaded save container; "
                + "building-attached facilities cannot be reconstructed from standalone records.");
            return null;
        }

        private static bool HasRebuildMagic(byte[] data)
        {
            return data != null
                   && data.Length >= 4
                   && data[0] == (byte)(RebuildDataMagic & 0xff)
                   && data[1] == (byte)((RebuildDataMagic >> 8) & 0xff)
                   && data[2] == (byte)((RebuildDataMagic >> 16) & 0xff)
                   && data[3] == (byte)((RebuildDataMagic >> 24) & 0xff);
        }

        public override void OnSaveData()
        {
            base.OnSaveData();

            try
            {
                byte[] data = UndergroundParkingRegistry.Serialize();
                serializableDataManager.SaveData(DataId, data);
                byte[] parkingData = UndergroundParkingOccupancyManager.SerializePersistentAssignments();
                serializableDataManager.SaveData(ParkingDataId, parkingData);
                UndergroundParkingLog.Advanced("Saved underground parking garage facilities: count="
                                            + UndergroundParkingRegistry.Count);
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Error("Failed to save underground parking garage facilities: " + e);
            }
        }
    }
}
