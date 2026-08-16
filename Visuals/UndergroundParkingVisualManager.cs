using System;
using System.Collections.Generic;
using ColossalFramework.Math;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingVisualManager
    {
        private const float EntrancePadWidth = UndergroundParkingGeometry.EntranceLotWidth;
        private const float EntrancePadLength = UndergroundParkingGeometry.EntranceLotLength;
        private const float EntranceKioskWidth = 5.4f;
        private const float EntranceKioskLength = 4.4f;
        private const float EntranceKioskHeight = 3.6f;
        private const float SurfaceLift = 0.08f;
        private const float BuildingAttachedSignSurfaceLift = 0.02f;
        private const float BuildingAttachedSignFaceSeparation = 0.08f;
        private const float BuildingAttachedPortalSurfaceLift = 0.06f;
        private const float BuildingAttachedKerbSurfaceLift =
            BuildingAttachedPortalSurfaceLift + 0.04f;
        private const float BuildingAttachedParkingFieldSurfaceLift =
            BuildingAttachedPortalSurfaceLift + 0.05f;
        private const float BuildingAttachedParkingGlyphSurfaceLift =
            BuildingAttachedPortalSurfaceLift + 0.06f;
        private const float BuildingAttachedMaximumTerrainTransition = 2.5f;
        private const float BuildingAttachedRaisedRoadTerrainSeparation = 4f;
        private const float BuildingAttachedKerbWidth = 0.24f;
        private const float BuildingAttachedKerbTargetLength = 0.70f;
        private const float BuildingAttachedSignSideOffset = 4.15f;
        private const float BuildingAttachedLampBaseLowering = 0.06f;
        private const float BuildingAttachedSurfaceWidth = 6.1f;
        private const float TerrainNormalSampleDistance = 2f;
        private const float ForecourtLightTargetHeight = 0.2f;
        private const float TunnelWidth = 4.5f;
        private const float TunnelHeight = 3.8f;
        private const float TunnelTurningPadWidth = 5f;
        private const float TunnelTurningPadLength = 5f;
        private const float TunnelTurningPadDepth = 4f;

        private enum SurfaceTunnelOpeningFace
        {
            Far,
            Left,
            Right
        }

        private enum GarageTunnelOpeningFace
        {
            Near,
            Left,
            Right
        }
        private const float TunnelGarageWallOverlap = 0.85f;
        private const float GarageChamferRatio = 0.01625f;
        private const int GarageCornerArcSegments = 4;
        private const float TunnelCornerClearance = 0.75f;
        private const float VisualSlotWidth = UndergroundParkingLaneLayout.BayWidth;
        private const float VisualSlotLength = UndergroundParkingLaneLayout.BayDepth;
        private const float VisualSlotEdgePadding = 1.6f;
        private const float ParkedVehicleBayClearance = 0.18f;
        private const float ParkedVehicleSurfaceClearance = 0.01f;
        private const float InternalJourneyMaximumCameraHeight = 500f;
        private const float InternalJourneySpeed = 13.4112f;
        private const float InternalDepartureSpeed = 13.4112f;
        private const float VisibilityRefreshInterval = 0.1f;
        private const float SurfaceMaintenanceInterval = 2f;
        private const int MaximumInternalJourneys = 64;
        private static readonly Color GarageBlockColor = new Color(0.272f, 0.272f, 0.272f, 0.88f);
        private static readonly Color GarageSlabColor = new Color(0.34f, 0.35f, 0.36f, 0.92f);
        private static readonly Color GarageStructureColor = new Color(0.22f, 0.23f, 0.24f, 0.96f);
        private static readonly Color GarageAisleColor = new Color(0.18f, 0.20f, 0.22f, 0.9f);
        private static readonly Color GarageMarkingColor = new Color(0.72f, 0.72f, 0.64f, 0.9f);
        private static readonly Color GarageRampColor = new Color(0.29f, 0.31f, 0.32f, 0.98f);
        private static readonly Color GarageCirculationColor = new Color(0.54f, 0.56f, 0.57f, 0.98f);
        private static readonly Color GarageDuctColor = new Color(0.42f, 0.46f, 0.48f, 0.96f);
        private static readonly Color AttachedPortalRampColor = new Color(0.17f, 0.18f, 0.18f, 1f);
        // Retained only by the disconnected legacy overlay helper; live routed
        // vehicle colour now comes from PassengerCarAI.GetColor.
        private static readonly Color ParkingBlue = new Color32(0, 102, 178, 255);

        private static readonly List<GameObject> Visuals = new List<GameObject>();
        private static readonly List<RenderItem> RenderItems = new List<RenderItem>();
        private static readonly List<RenderItem> ParkedCarRenderItems = new List<RenderItem>();
        private static readonly List<Mesh> GeneratedTunnelMeshes = new List<Mesh>();
        private static readonly List<Mesh> GeneratedSurfaceMeshes = new List<Mesh>();
        private static readonly List<Mesh> GeneratedLaneLayoutMeshes = new List<Mesh>();
        private static readonly List<UndergroundParkingFacility> FacilityBuffer =
            new List<UndergroundParkingFacility>();
        private static readonly List<UndergroundParkingCarVisual> ParkedCarBuffer =
            new List<UndergroundParkingCarVisual>();
        private static readonly List<InternalParkingJourney> InternalJourneys =
            new List<InternalParkingJourney>();
        private static readonly List<InternalDepartureJourney> InternalDepartureJourneys =
            new List<InternalDepartureJourney>();
        private static readonly object InternalJourneyActivitySync = new object();
        private static readonly Dictionary<int, int> InternalJourneyActivityByFacility =
            new Dictionary<int, int>();
        private static readonly List<Vector3> InternalJourneyWaypointBuffer =
            new List<Vector3>();
        private static readonly HashSet<int> CameraVisibleFacilities = new HashSet<int>();
        private static readonly HashSet<int> PreviouslyVisibleFacilityIds = new HashSet<int>();
        private static readonly HashSet<int> CameraXrayVisibleFacilities = new HashSet<int>();
        private static readonly HashSet<int> PreviouslyXrayVisibleFacilityIds = new HashSet<int>();
        private static readonly object CameraVisibilitySync = new object();
        private static readonly ParkingRenderableManager ParkingRenderableManagerInstance = new ParkingRenderableManager();
        private static GameObject _root;
        private static Material _entrancePadMaterial;
        private static Material _entranceKioskMaterial;
        private static Material _entranceSignMaterial;
        private static Material _buildingAttachedPortalMaterial;
        private static Material _buildingAttachedTarmacMaterial;
        private static Material _buildingAttachedSideKerbMaterial;
        private static Material _buildingAttachedParkingMarkMaterial;
        private static Material _buildingAttachedLampLensMaterial;
        private static Material _garageStructureMaterial;
        private static Material _parkedCarMaterial;
        private static Mesh _padMesh;
        private static Mesh _kioskMesh;
        private static Mesh _signMesh;
        private static Mesh _attachedLampPoleMesh;
        private static Mesh _attachedLampHeadMesh;
        private static Mesh _attachedLampLensMesh;
        private static Mesh _garageStructureMesh;
        private static readonly Dictionary<long, Mesh> GarageStructureMeshes = new Dictionary<long, Mesh>();
        private static readonly Dictionary<long, Mesh> GarageTopAccessMeshes = new Dictionary<long, Mesh>();
        private static readonly Dictionary<VehicleInfo, Mesh> NeutralVehicleMeshes =
            new Dictionary<VehicleInfo, Mesh>();
        private static Mesh _parkedCarMesh;
        private static Mesh _parkedMotorcycleMesh;
        private static bool _renderManagerRegistered;
        private static bool _xrayLogged;
        private static volatile bool _rebuildRequested;
        private static volatile bool _parkedCarRefreshRequested;
        private static float _lastAppliedWetness = -1f;
        private static int _worldRendererRepairLogCount;
        private static long _nextInternalJourneySequence;
        private static float _nextVisibilityRefreshTime;
        private static float _nextSurfaceMaintenanceTime;

        public static void Initialize()
        {
            if (_root == null)
            {
                _root = new GameObject("UndergroundParkingGarageVisuals");
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }

            if (_root.GetComponent<UndergroundParkingVisualVisibilityKeeper>() == null)
                _root.AddComponent<UndergroundParkingVisualVisibilityKeeper>();

            EnsureRenderManagerRegistered();
            EnsureRenderResources();
        }

        public static void RebuildAll()
        {
            Initialize();
            Clear();

            int count = UndergroundParkingRegistry.CopyTo(FacilityBuffer);
            for (int i = 0; i < count; i++)
                AddVisual(FacilityBuffer[i]);

            UndergroundParkingLog.Advanced("UPG surface visuals rebuilt: facilities="
                                        + count
                                        + " shadowlessNightLights="
                                        + count);

            RefreshParkedCars();

            _xrayLogged = false;
        }

        public static void RequestRebuild()
        {
            _rebuildRequested = true;
        }

        public static void RequestParkedCarRefresh()
        {
            _parkedCarRefreshRequested = true;
        }

        public static bool HasPendingMainThreadUpdates
        {
            get
            {
                return _rebuildRequested
                       || _parkedCarRefreshRequested
                       || InternalJourneys.Count > 0
                       || InternalDepartureJourneys.Count > 0;
            }
        }

        public static void ProcessMainThreadUpdates()
        {
            if (_rebuildRequested)
            {
                _rebuildRequested = false;
                RebuildAll();
            }

            if (_parkedCarRefreshRequested)
            {
                _parkedCarRefreshRequested = false;
                RefreshParkedCars();
            }

            float now = Time.realtimeSinceStartup;
            if (now >= _nextVisibilityRefreshTime)
            {
                _nextVisibilityRefreshTime = now + VisibilityRefreshInterval;
                UpdateVisibility();
                UpdateCameraVisibleFacilities();
            }

            // Weather response and defensive renderer repair belong only to
            // exposed entrance assets. Keep them off the underground journey
            // path and run them slowly even while pose integration is active.
            if (Visuals.Count > 0 && now >= _nextSurfaceMaintenanceTime)
            {
                _nextSurfaceMaintenanceTime = now + SurfaceMaintenanceInterval;
                UpdateWeatherResponse();
                EnsureWorldVisualRenderersActive();
            }

            // Underground motion is transform-only and remains frame-driven so
            // visible cars move continuously without stepping. Its cached
            // neutral material performs no weather, light, shade or colour
            // update here.
            UpdateInternalParkingJourneys();
            UpdateInternalDepartureJourneys();
        }

        public static bool TryStartInternalParkingJourney(
            ushort parkedId,
            int facilityId,
            VehicleInfo info,
            Vector3 start)
        {
            if (parkedId == 0
                || facilityId <= 0
                || info == null
                || !UndergroundParkingOccupancyManager.SupportsAutomatedTunnel(facilityId)
                || InternalJourneys.Count >= MaximumInternalJourneys)
            {
                return false;
            }

            UndergroundParkingInternalJourneyPlan plan;
            if (!UndergroundParkingOccupancyManager.TryBuildInternalParkingJourney(
                    parkedId,
                    start,
                    InternalJourneyWaypointBuffer,
                    out plan)
                || plan.Facility.Id != facilityId
                || plan.Info != info
                || !ShouldAnimateInternalParkingJourney(plan.Facility))
            {
                InternalJourneyWaypointBuffer.Clear();
                return false;
            }

            Mesh mesh = GetNeutralParkingProxyMesh(info);
            if (mesh == null)
                return false;

            List<Vector3> waypoints = new List<Vector3>(InternalJourneyWaypointBuffer);
            InternalJourneyWaypointBuffer.Clear();
            InternalParkingJourney journey = new InternalParkingJourney(
                parkedId,
                facilityId,
                mesh,
                info,
                waypoints,
                plan.FinalRotation,
                plan.Facility.GarageCenter,
                ++_nextInternalJourneySequence,
                true);
            if (journey.TotalDistance <= 0.1f
                || !journey.CreateVisual("UPG internal parking car"))
                return false;

            InternalJourneys.Add(journey);
            lock (InternalJourneyActivitySync)
            {
                int activeCount;
                InternalJourneyActivityByFacility.TryGetValue(facilityId, out activeCount);
                InternalJourneyActivityByFacility[facilityId] = activeCount + 1;
            }
            UndergroundParkingLog.Advanced(
                "UPG x-ray internal parking journey started: parked="
                + parkedId
                + " facility="
                + facilityId
                + " waypoints="
                + waypoints.Count
                + " concurrent="
                + InternalJourneys.Count);
            return true;
        }

        public static bool HasInternalParkingJourneyForFacility(int facilityId)
        {
            if (facilityId <= 0)
                return false;
            lock (InternalJourneyActivitySync)
                return InternalJourneyActivityByFacility.ContainsKey(facilityId);
        }

        public static bool TryStartInternalDepartureJourney(
            VehicleInfo info,
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection connection,
            int slotIndex,
            ushort vehicleId,
            Color surfaceColor)
        {
            if (info == null
                || vehicleId == 0
                || !facility.IsValid
                || !connection.IsValid
                || slotIndex < 0
                || !UndergroundParkingOccupancyManager.SupportsAutomatedTunnel(facility)
                || InternalDepartureJourneys.Count >= MaximumInternalJourneys)
            {
                return false;
            }

            Vector3 lane;
            Vector3 approach;
            Vector3 portalControl;
            Vector3 portal;
            Vector3 underground;
            UndergroundParkingPortalAnimationManager.GetArrivalTraversalPath(
                facility,
                connection,
                connection.LanePosition,
                out lane,
                out approach,
                out portalControl,
                out portal,
                out underground);
            Quaternion finalRotation;
            if (!UndergroundParkingOccupancyManager.TryBuildInternalDepartureJourney(
                    facility,
                    slotIndex,
                    portal,
                    InternalJourneyWaypointBuffer,
                    out finalRotation))
            {
                InternalJourneyWaypointBuffer.Clear();
                return false;
            }

            Mesh mesh = GetNeutralParkingProxyMesh(info);
            if (mesh == null)
            {
                InternalJourneyWaypointBuffer.Clear();
                return false;
            }

            List<Vector3> waypoints = new List<Vector3>(InternalJourneyWaypointBuffer);
            InternalJourneyWaypointBuffer.Clear();
            InternalParkingJourney movement = new InternalParkingJourney(
                0,
                facility.Id,
                mesh,
                info,
                waypoints,
                finalRotation,
                facility.GarageCenter,
                ++_nextInternalJourneySequence,
                false);
            if (movement.TotalDistance <= 0.1f
                || !movement.CreateVisual("UPG internal departure car"))
                return false;

            InternalDepartureJourneys.Add(new InternalDepartureJourney(
                vehicleId,
                info,
                facility,
                connection,
                surfaceColor,
                movement));
            IncrementInternalJourneyActivity(facility.Id);
            UndergroundParkingLog.Advanced(
                "UPG x-ray internal departure journey started: vehicle="
                + vehicleId
                + " facility="
                + facility.Id
                + " slot="
                + slotIndex
                + " waypoints="
                + waypoints.Count
                + " opposingTrafficYield=False");
            return true;
        }

        private static void EnsureWorldVisualRenderersActive()
        {
            int repairedContainers = 0;
            int repairedRenderers = 0;
            if (_root != null && !_root.activeSelf)
            {
                _root.SetActive(true);
                repairedContainers++;
            }

            for (int i = 0; i < Visuals.Count; i++)
            {
                GameObject visual = Visuals[i];
                if (visual == null)
                    continue;

                if (!visual.activeSelf)
                {
                    visual.SetActive(true);
                    repairedContainers++;
                }

                MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>();
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    MeshRenderer renderer = renderers[rendererIndex];
                    if (renderer == null || renderer.enabled)
                        continue;

                    renderer.enabled = true;
                    repairedRenderers++;
                }
            }

            if ((repairedContainers > 0 || repairedRenderers > 0)
                && _worldRendererRepairLogCount++ < 4)
            {
                UndergroundParkingLog.Warning(
                    "Restored disabled UPG normal-world entrance renderer state: containers="
                    + repairedContainers
                    + " renderers="
                    + repairedRenderers);
            }
        }

        private static void UpdateWeatherResponse()
        {
            float wetness = GetCurrentSurfaceWetness();
            if (_lastAppliedWetness >= 0f
                && Mathf.Abs(wetness - _lastAppliedWetness) < 0.01f)
                return;

            _lastAppliedWetness = wetness;
            UndergroundParkingBuildingPrefab.UpdateWeatherResponse(wetness);
            ApplyWetSurface(_buildingAttachedTarmacMaterial, wetness, Color.white, 0.74f, 0.08f, 0.78f);
            ApplyWetSurface(_buildingAttachedSideKerbMaterial, wetness, Color.white, 0.88f, 0.12f, 0.64f);
            ApplyWetSurface(
                _entranceKioskMaterial,
                wetness,
                new Color(0.14f, 0.17f, 0.19f, 1f),
                0.80f,
                0.10f,
                0.70f);
        }

        private static float GetCurrentSurfaceWetness()
        {
            WeatherManager weather = WeatherManager.instance;
            return weather == null
                ? 0f
                : Mathf.Clamp01(Mathf.Max(
                    weather.m_groundWetness,
                    weather.m_currentRain * 0.75f));
        }

        private static void ApplyWetSurface(
            Material material,
            float wetness,
            Color dryColor,
            float wetColorMultiplier,
            float drySmoothness,
            float wetSmoothness)
        {
            if (material == null)
                return;

            Color wetColor = new Color(
                dryColor.r * wetColorMultiplier,
                dryColor.g * wetColorMultiplier,
                dryColor.b * wetColorMultiplier,
                dryColor.a);
            Color tint = Color.Lerp(dryColor, wetColor, wetness);
            material.color = tint;
            material.SetColor("_Color", tint);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", Mathf.Lerp(drySmoothness, wetSmoothness, wetness));
            if (material.HasProperty("_GlossMapScale"))
                material.SetFloat("_GlossMapScale", Mathf.Lerp(drySmoothness, wetSmoothness, wetness));
        }

        public static bool IsFacilityVisibleOnCamera(int facilityId)
        {
            if (facilityId <= 0)
                return false;

            lock (CameraVisibilitySync)
                return CameraVisibleFacilities.Contains(facilityId);
        }

        private static bool IsFacilityXrayVisible(int facilityId)
        {
            if (facilityId <= 0)
                return false;

            lock (CameraVisibilitySync)
                return CameraXrayVisibleFacilities.Contains(facilityId);
        }

        private static void UpdateCameraVisibleFacilities()
        {
            Camera camera = null;
            CameraController cameraController = ToolsModifierControl.cameraController;
            if (cameraController != null)
                camera = cameraController.m_camera;
            if (camera == null)
                camera = Camera.main;
            lock (CameraVisibilitySync)
            {
                foreach (int oldId in PreviouslyVisibleFacilityIds)
                    CameraVisibleFacilities.Remove(oldId);
                PreviouslyVisibleFacilityIds.Clear();
                foreach (int oldId in PreviouslyXrayVisibleFacilityIds)
                    CameraXrayVisibleFacilities.Remove(oldId);
                PreviouslyXrayVisibleFacilityIds.Clear();

                if (camera == null)
                    return;

                int count = UndergroundParkingRegistry.CopyTo(FacilityBuffer);
                bool cacheXrayVisibility = ShouldShowXrayVisuals();
                for (int i = 0; i < count; i++)
                {
                    UndergroundParkingFacility facility = FacilityBuffer[i];
                    if (!facility.IsValid
                        || facility.Id <= 0)
                        continue;

                    // The visible entrance is the player-facing trigger. The
                    // underground vehicle node can sit far toward the host
                    // building and leave the viewport even while an attached P
                    // panel and its road handoff are centred on screen.
                    Vector3 midpoint = Vector3.Lerp(
                        facility.SurfaceRoadPosition,
                        facility.EntrancePosition,
                        0.5f);
                    Vector3 garageCentre =
                        UndergroundParkingGeometry.ResolveCurrentVisualGarageCenter(
                            facility);

                    // Cache one conservative envelope per facility at the
                    // existing 10 Hz visibility cadence. Static x-ray drawing
                    // can then reject an off-screen garage with a hash lookup,
                    // rather than projecting every floor, tunnel and parked
                    // body during every render pass.
                    if (cacheXrayVisibility)
                    {
                        Vector3 xrayEnvelopeCentre = Vector3.Lerp(
                            facility.EntrancePosition,
                            garageCentre,
                            0.5f);
                        float xrayEnvelopeRadius =
                            Vector3.Distance(facility.EntrancePosition, garageCentre) * 0.5f
                            + Mathf.Max(facility.GarageWidth, facility.GarageLength);
                        if (IsXrayPointInsideCameraEnvelope(
                                camera,
                                xrayEnvelopeCentre,
                                xrayEnvelopeRadius))
                        {
                            CameraXrayVisibleFacilities.Add(facility.Id);
                            PreviouslyXrayVisibleFacilityIds.Add(facility.Id);
                        }
                    }

                    if (Mathf.Abs(camera.transform.position.y - garageCentre.y)
                        > InternalJourneyMaximumCameraHeight)
                    {
                        continue;
                    }
                    if (!IsPointInsideCameraEnvelope(camera, facility.SurfaceRoadPosition)
                        && !IsPointInsideCameraEnvelope(camera, facility.EntrancePosition)
                        && !IsPointInsideCameraEnvelope(camera, midpoint)
                        && !IsPointInsideCameraEnvelope(camera, facility.VehicleNodePosition)
                        && !IsPointInsideCameraEnvelope(camera, garageCentre))
                        continue;

                    CameraVisibleFacilities.Add(facility.Id);
                    PreviouslyVisibleFacilityIds.Add(facility.Id);
                }
            }
        }

        private static bool IsPointInsideCameraEnvelope(Camera camera, Vector3 point)
        {
            Vector3 viewport = camera.WorldToViewportPoint(point);
            return viewport.z > 0f
                   && viewport.x >= -0.08f
                   && viewport.x <= 1.08f
                   && viewport.y >= -0.08f
                   && viewport.y <= 1.08f;
        }

        private static bool ShouldAnimateInternalParkingJourney(
            UndergroundParkingFacility facility)
        {
            if (!facility.IsValid
                || !ShouldShowXrayVisuals())
            {
                return false;
            }

            return IsFacilityVisibleOnCamera(facility.Id);
        }

        private static void UpdateInternalParkingJourneys()
        {
            if (InternalJourneys.Count == 0)
                return;

            SimulationManager simulationManager = SimulationManager.instance;
            float simulationTimeSpeed = simulationManager == null
                ? 1f
                : Mathf.Max(0f, simulationManager.m_simulationTimeSpeed);
            float delta = Mathf.Max(0f, Time.unscaledDeltaTime * simulationTimeSpeed);
            for (int i = InternalJourneys.Count - 1; i >= 0; i--)
            {
                InternalParkingJourney journey = InternalJourneys[i];
                UndergroundParkingFacility facility = UndergroundParkingFacility.None;
                UndergroundParkingRoadConnection connection =
                    default(UndergroundParkingRoadConnection);
                bool stillOwned =
                    UndergroundParkingOccupancyManager.IsUsableParkedVehicle(journey.ParkedId)
                    && UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                        journey.FacilityId,
                        out facility,
                        out connection);
                if (!stillOwned || !ShouldAnimateInternalParkingJourney(facility))
                {
                    CompleteInternalParkingJourney(i, false);
                    continue;
                }

                journey.UpdateVisual(true);

                float proposedDistance = Mathf.Min(
                    journey.TotalDistance,
                    journey.Distance + InternalJourneySpeed * delta);
                journey.Distance = proposedDistance;
                journey.UpdatePose(delta);
                journey.UpdateVisual(true);
                if (journey.Distance >= journey.TotalDistance - 0.01f)
                    CompleteInternalParkingJourney(i, true);
            }
        }

        private static void CompleteInternalParkingJourney(int index, bool reachedBay)
        {
            if (index < 0 || index >= InternalJourneys.Count)
                return;

            InternalParkingJourney journey = InternalJourneys[index];
            journey.DestroyVisual();
            UndergroundParkingOccupancyHarmony.CompleteRoutedArrivalAnimation(journey.ParkedId);
            InternalJourneys.RemoveAt(index);
            DecrementInternalJourneyActivity(journey.FacilityId);
            UndergroundParkingLog.Advanced(
                "UPG x-ray internal parking journey completed: parked="
                + journey.ParkedId
                + " facility="
                + journey.FacilityId
                + " reachedBay="
                + reachedBay
                + " concurrentRemaining="
                + InternalJourneys.Count);
        }

        private static void UpdateInternalDepartureJourneys()
        {
            if (InternalDepartureJourneys.Count == 0)
                return;

            SimulationManager simulationManager = SimulationManager.instance;
            float simulationTimeSpeed = simulationManager == null
                ? 1f
                : Mathf.Max(0f, simulationManager.m_simulationTimeSpeed);
            float delta = Mathf.Max(0f, Time.unscaledDeltaTime * simulationTimeSpeed);
            for (int i = InternalDepartureJourneys.Count - 1; i >= 0; i--)
            {
                InternalDepartureJourney departure = InternalDepartureJourneys[i];
                InternalParkingJourney journey = departure.Movement;
                if (!UndergroundParkingOccupancyHarmony.IsManagedDepartureReleasePending(
                        departure.VehicleId))
                {
                    RemoveInternalDepartureJourney(i, false);
                    continue;
                }

                journey.UpdateVisual(true);

                float proposedDistance = ShouldAnimateInternalParkingJourney(departure.Facility)
                    ? Mathf.Min(
                        journey.TotalDistance,
                        journey.Distance + InternalDepartureSpeed * delta)
                    : journey.TotalDistance;
                journey.Distance = proposedDistance;
                journey.UpdatePose(delta);
                journey.UpdateVisual(true);
                if (journey.Distance < journey.TotalDistance - 0.01f)
                    continue;

                if (UndergroundParkingPortalAnimationManager.QueueDepartureFromSurfacePortal(
                        departure.Info,
                        departure.Facility,
                        departure.Connection,
                        departure.VehicleId,
                        departure.SurfaceColor))
                {
                    RemoveInternalDepartureJourney(i, false);
                }
            }
        }

        private static void IncrementInternalJourneyActivity(int facilityId)
        {
            lock (InternalJourneyActivitySync)
            {
                int activeCount;
                InternalJourneyActivityByFacility.TryGetValue(facilityId, out activeCount);
                InternalJourneyActivityByFacility[facilityId] = activeCount + 1;
            }
        }

        private static void DecrementInternalJourneyActivity(int facilityId)
        {
            lock (InternalJourneyActivitySync)
            {
                int activeCount;
                if (!InternalJourneyActivityByFacility.TryGetValue(facilityId, out activeCount))
                    return;
                if (activeCount <= 1)
                    InternalJourneyActivityByFacility.Remove(facilityId);
                else
                    InternalJourneyActivityByFacility[facilityId] = activeCount - 1;
            }
        }

        private static void RemoveInternalDepartureJourney(int index, bool restartTicket)
        {
            if (index < 0 || index >= InternalDepartureJourneys.Count)
                return;
            InternalDepartureJourney departure = InternalDepartureJourneys[index];
            departure.Movement.DestroyVisual();
            if (restartTicket)
                UndergroundParkingOccupancyHarmony.RestartManagedDepartureAnimation(
                    departure.VehicleId);
            InternalDepartureJourneys.RemoveAt(index);
            DecrementInternalJourneyActivity(departure.Facility.Id);
        }

        private static void CompleteAllInternalParkingJourneys()
        {
            for (int i = InternalDepartureJourneys.Count - 1; i >= 0; i--)
                RemoveInternalDepartureJourney(i, true);
            for (int i = InternalJourneys.Count - 1; i >= 0; i--)
                CompleteInternalParkingJourney(i, false);
        }

        public static void Clear()
        {
            CompleteAllInternalParkingJourneys();
            for (int i = 0; i < Visuals.Count; i++)
            {
                if (Visuals[i] != null)
                    UnityEngine.Object.Destroy(Visuals[i]);
            }

            Visuals.Clear();
            RenderItems.Clear();
            ParkedCarRenderItems.Clear();
            for (int i = 0; i < GeneratedTunnelMeshes.Count; i++)
            {
                if (GeneratedTunnelMeshes[i] != null)
                    UnityEngine.Object.Destroy(GeneratedTunnelMeshes[i]);
            }
            GeneratedTunnelMeshes.Clear();
            for (int i = 0; i < GeneratedSurfaceMeshes.Count; i++)
            {
                if (GeneratedSurfaceMeshes[i] != null)
                    UnityEngine.Object.Destroy(GeneratedSurfaceMeshes[i]);
            }
            GeneratedSurfaceMeshes.Clear();
            for (int i = 0; i < GeneratedLaneLayoutMeshes.Count; i++)
            {
                if (GeneratedLaneLayoutMeshes[i] != null)
                    UnityEngine.Object.Destroy(GeneratedLaneLayoutMeshes[i]);
            }
            GeneratedLaneLayoutMeshes.Clear();
            foreach (KeyValuePair<VehicleInfo, Mesh> entry in NeutralVehicleMeshes)
            {
                if (entry.Value != null)
                    UnityEngine.Object.Destroy(entry.Value);
            }
            NeutralVehicleMeshes.Clear();
            _xrayLogged = false;
            _lastAppliedWetness = -1f;
        }

        public static void Shutdown()
        {
            _rebuildRequested = false;
            _lastAppliedWetness = -1f;
            Clear();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            lock (CameraVisibilitySync)
            {
                CameraVisibleFacilities.Clear();
                PreviouslyVisibleFacilityIds.Clear();
                CameraXrayVisibleFacilities.Clear();
                PreviouslyXrayVisibleFacilityIds.Clear();
            }
        }

        public static void UpdateVisibility()
        {
            if (!ShouldShowXrayVisuals())
                _xrayLogged = false;
        }

        private static void AddVisual(UndergroundParkingFacility facility)
        {
            Vector3 roadPosition;
            Vector3 entrancePosition;
            Vector3 direction;
            Vector3 side;
            bool hasCurrentPlacement = UndergroundParkingGeometry.TryGetCurrentPlacement(
                facility,
                out roadPosition,
                out entrancePosition,
                out direction,
                out side);
            if (!hasCurrentPlacement && !facility.IsValid)
                return;

            if (!hasCurrentPlacement)
            {
                roadPosition = facility.SurfaceRoadPosition;
                entrancePosition = facility.EntrancePosition;
                direction = NormalizeFlat(facility.Direction, Vector3.forward);
                side = NormalizeFlat(facility.Side, Vector3.right);
            }

            Vector3 entranceSurfaceNormal = ResolveSurfaceNormal(entrancePosition);
            Quaternion rotation = CreateSurfaceRotation(side, entranceSurfaceNormal);
            if (facility.EntranceBuildingId == 0 && facility.TargetBuildingId == 0)
            {
                if (!hasCurrentPlacement)
                    return;

                GameObject container = new GameObject("Underground Parking Garage " + facility.Id);
                container.transform.parent = _root.transform;
                container.transform.position = entrancePosition + entranceSurfaceNormal * SurfaceLift;
                container.transform.rotation = rotation;

                BuildingInfo entrancePrefab = UndergroundParkingBuildingPrefab.Prefab;
                if (entrancePrefab != null
                    && entrancePrefab.m_mesh != null
                    && entrancePrefab.m_material != null)
                {
                    // Legacy registry-only entrances use the exact same generated
                    // portal mesh as current vanilla-placed entrance buildings.
                    AddMeshChild(container, "surface-portal", entrancePrefab.m_mesh,
                        entrancePrefab.m_material, Vector3.zero, Vector3.one);
                }
                else
                {
                    // Retain a safe visual fallback for unusually incomplete prefab
                    // startup states; placement/routing remains authoritative.
                    AddMeshChild(container, "entrance-pad", GetPadMesh(), GetEntrancePadMaterial(), Vector3.zero, Vector3.one);
                    AddMeshChild(container, "entrance-kiosk", GetKioskMesh(), GetEntranceKioskMaterial(),
                        new Vector3(0f, EntranceKioskHeight * 0.5f, -EntrancePadLength * 0.28f), Vector3.one);
                    AddMeshChild(container, "parking-sign", GetSignMesh(), GetEntranceSignMaterial(),
                        new Vector3(0f, EntranceKioskHeight + 0.25f, -EntrancePadLength * 0.48f), Vector3.one);
                }

                Visuals.Add(container);
            }
            else
            {
                // Placed entrances carry their parking mark in the building prefab mesh.
                // Do not spawn a separate terrain or road-side marker for them.
            }

            if (facility.TargetBuildingId == 0)
            {
                AddStandaloneParkingMarkOverlay(
                    facility,
                    entrancePosition,
                    rotation,
                    entranceSurfaceNormal);
                AddEntranceParkingLight(facility, entrancePosition, rotation);
            }
            else
            {
                if (facility.EntranceVisualsEnabled)
                {
                    AddBuildingAttachedPortal(facility);
                    AddBuildingAttachedWorldSign(facility);
                }
                else
                {
                    UndergroundParkingLog.Advanced("Skipped player-disabled building-attached entrance visuals: facility="
                                               + facility.Id);
                }
            }

            Vector3 garageCenter =
                UndergroundParkingGeometry.ResolveCurrentVisualGarageCenter(facility);
            float garageHeight = UndergroundParkingGeometry.GetGarageHeight(facility.FloorCount);
            float garageRadius = Mathf.Max(
                Mathf.Max(facility.GarageWidth, facility.GarageLength),
                garageHeight);
            float topY = garageCenter.y + garageHeight * 0.5f;
            Quaternion garageRotation = Quaternion.LookRotation(facility.GarageForward, Vector3.up);
            // X-ray rendering intentionally bypasses terrain depth. Draw the
            // deepest storey first so each successively nearer storey's slab,
            // complete 39-bay grid, aisle, walls, and columns remain visible
            // instead of the bottom floor painting over every floor above it.
            for (int floor = facility.FloorCount - 1; floor >= 0; floor--)
            {
                Vector3 floorCenter = garageCenter;
                floorCenter.y = topY - UndergroundParkingGeometry.GarageFloorHeight * (floor + 0.5f);
                Matrix4x4 garageMatrix = Matrix4x4.TRS(
                    floorCenter,
                    garageRotation,
                    new Vector3(
                        facility.GarageWidth,
                        UndergroundParkingGeometry.GarageFloorHeight,
                        facility.GarageLength));
                RenderItems.Add(new RenderItem(
                    facility.Id,
                    GetGarageStructureMesh(facility),
                    garageMatrix,
                    GetGarageStructureMaterial(),
                    floorCenter,
                    garageRadius));

                Mesh laneLayoutMesh = CreateLaneLayoutMesh(facility);
                if (laneLayoutMesh != null)
                {
                    GeneratedLaneLayoutMeshes.Add(laneLayoutMesh);
                    RenderItems.Add(new RenderItem(
                        facility.Id,
                        laneLayoutMesh,
                        Matrix4x4.TRS(floorCenter, garageRotation, Vector3.one),
                        GetGarageStructureMaterial(),
                        floorCenter,
                        garageRadius));
                }
            }
            Vector3 topFloorCenter = garageCenter;
            topFloorCenter.y = topY - UndergroundParkingGeometry.GarageFloorHeight * 0.5f;
            RenderItems.Add(new RenderItem(
                facility.Id,
                GetGarageTopAccessMesh(facility),
                Matrix4x4.TRS(
                    topFloorCenter,
                    garageRotation,
                    new Vector3(
                        facility.GarageWidth,
                        UndergroundParkingGeometry.GarageFloorHeight,
                        facility.GarageLength)),
                GetGarageStructureMaterial(),
                topFloorCenter,
                garageRadius));
            if (facility.TargetBuildingId != 0
                && UndergroundParkingOccupancyManager.SupportsAutomatedTunnel(facility))
                AddTunnelRenderItem(facility, entrancePosition, garageCenter, garageRadius);
            UndergroundParkingLog.Advanced("UPG x-ray floor grids built: facility="
                                        + facility.Id
                                        + " floors="
                                        + facility.FloorCount
                                        + " grids="
                                        + facility.FloorCount
                                        + " baysPerGrid="
                                        + UndergroundParkingGeometry.GetParkingSpaceCapacity(facility, 1)
                                        + " detailedGarage=True"
                                        + " visualVariant="
                                        + GetGarageVisualVariant(facility)
                                        + " vehicleRamp=sloped-floor-to-floor"
                                        + " spiralStairs=two-sides-plus-surface-extension"
                                        + " ventilation=scaled-ceiling-ducts"
                                        + " footprintChanged=False capacityChanged=False");
        }

        private static void AddStandaloneParkingMarkOverlay(
            UndergroundParkingFacility facility,
            Vector3 entrancePosition,
            Quaternion rotation,
            Vector3 surfaceNormal)
        {
            if (facility.EntranceBuildingId != 0)
            {
                BuildingManager buildingManager = BuildingManager.instance;
                if (buildingManager != null
                    && facility.EntranceBuildingId < buildingManager.m_buildings.m_size)
                {
                    ref Building building = ref buildingManager.m_buildings.m_buffer[
                        facility.EntranceBuildingId];
                    if ((building.m_flags & Building.Flags.Created) != 0)
                    {
                        entrancePosition = building.m_position;
                        rotation = Quaternion.AngleAxis(
                            -building.m_angle * Mathf.Rad2Deg,
                            Vector3.up);
                        surfaceNormal = Vector3.up;
                    }
                }
            }
            else
            {
                entrancePosition += surfaceNormal * SurfaceLift;
            }

            GameObject container = new GameObject(
                "UPG exact parking colour overlay " + facility.Id);
            container.transform.parent = _root.transform;
            container.transform.position = entrancePosition;
            container.transform.rotation = rotation;
            UndergroundParkingStandaloneVariant variant =
                UndergroundParkingStandaloneCatalog.FromFacility(facility);
            AddMeshChild(
                container,
                "exact-blue-white-p",
                UndergroundParkingBuildingPrefab.GetParkingMarkOverlayMesh(variant),
                UndergroundParkingBuildingPrefab.ParkingMarkOverlayMaterial,
                Vector3.zero,
                Vector3.one);
            Visuals.Add(container);
            UndergroundParkingLog.Advanced("UPG colour-invariant parking overlay built: facility="
                                        + facility.Id
                                        + " shader="
                                        + UndergroundParkingBuildingPrefab.ParkingMarkOverlayMaterial.shader.name
                                        + " vertexPalette=True displayTargetBlue=0,102,178"
                                        + " displayTargetWhite=254,254,254"
                                        + " vertexEncoding=active-color-space");
        }

        private static void AddBuildingAttachedWorldSign(
            UndergroundParkingFacility facility)
        {
            PropInfo sign = UndergroundParkingEntranceAnchorService.GetRequiredParkingSignInfo();
            Mesh mesh = GetPrefabMesh(sign);
            Material material = GetPrefabMaterial(sign);
            if (mesh == null || material == null)
            {
                UndergroundParkingLog.Warning("Cannot build normal-world parking sign visual: prefab mesh/material unavailable.");
                return;
            }

            Vector3 center;
            Vector3 roadTangent;
            Vector3 inward;
            Vector3 roadNormal;
            float pavementWidth;
            if (!TryResolveBuildingAttachedRoadFrame(
                    facility,
                    out center,
                    out roadTangent,
                    out inward,
                    out roadNormal,
                    out _,
                    out pavementWidth))
            {
                return;
            }

            Vector3 position = center
                               + roadTangent * BuildingAttachedSignSideOffset
                               + inward * (pavementWidth * 0.5f - 0.12f)
                               + roadNormal * (
                                   BuildingAttachedSignSurfaceLift
                                   + ResolveDetailedRoadProfileOffset(
                                       center,
                                       roadTangent,
                                       inward,
                                       roadNormal,
                                       BuildingAttachedSignSideOffset,
                                       pavementWidth * 0.5f - 0.12f,
                                       pavementWidth * 0.5f));

            GameObject container = new GameObject("UPG normal-world parking sign " + facility.Id);
            container.transform.parent = _root.transform;
            container.transform.position = position;
            container.transform.rotation = Quaternion.LookRotation(
                roadTangent,
                roadNormal);

            AddMeshChild(container, "parking-sign-front", mesh, material, Vector3.zero, Vector3.one);
            GameObject back = AddMeshChild(
                container,
                "parking-sign-back",
                mesh,
                material,
                new Vector3(0f, 0f, -BuildingAttachedSignFaceSeparation),
                Vector3.one);
            back.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up);
            Visuals.Add(container);
            UndergroundParkingLog.Advanced("Built Utility-Roads-style normal-world parking sign visual: facility="
                                        + facility.Id
                                        + " position="
                                        + FormatVector(position)
                                        + " prefab="
                                        + sign.name);
        }

        private static void AddBuildingAttachedPortal(
            UndergroundParkingFacility facility)
        {
            Vector3 position;
            Vector3 roadTangent;
            Vector3 inward;
            Vector3 roadNormal;
            float roadHalfWidth;
            float pavementWidth;
            if (!TryResolveBuildingAttachedRoadFrame(
                    facility,
                    out position,
                    out roadTangent,
                    out inward,
                    out roadNormal,
                    out roadHalfWidth,
                    out pavementWidth))
            {
                return;
            }

            GameObject container = new GameObject("UPG building-attached surface " + facility.Id);
            container.transform.parent = _root.transform;
            container.transform.position = position;
            // The live segment frame owns yaw, pitch and roll. A bounded
            // triangle profile then follows only the nearby terrain's relative
            // cross-section shape; raised-road terrain is rejected so it cannot
            // pull the network-aligned surface underground.
            container.transform.rotation = Quaternion.LookRotation(inward, roadNormal);

            Mesh surfaceMesh = CreateBuildingAttachedSurfaceMesh(pavementWidth);
            ProfileMeshToDetailedRoadSurface(
                surfaceMesh,
                position,
                roadTangent,
                inward,
                roadNormal,
                pavementWidth * 0.5f);
            GeneratedSurfaceMeshes.Add(surfaceMesh);
            AddMeshChild(
                container,
                "flat-road-aligned-entrance",
                surfaceMesh,
                GetBuildingAttachedTarmacMaterial(),
                Vector3.zero,
                Vector3.one);

            Mesh sideKerbMesh = CreateBuildingAttachedSideKerbMesh(pavementWidth);
            ProfileMeshToDetailedRoadSurface(
                sideKerbMesh,
                position,
                roadTangent,
                inward,
                roadNormal,
                pavementWidth * 0.5f);
            GeneratedSurfaceMeshes.Add(sideKerbMesh);
            AddMeshChild(
                container,
                "flat-viewer-left-right-kerb-stones",
                sideKerbMesh,
                GetBuildingAttachedSideKerbMaterial(),
                Vector3.zero,
                Vector3.one);

            Mesh parkingMark = CreateBuildingAttachedParkingMarkMesh(pavementWidth);
            ProfileMeshToDetailedRoadSurface(
                parkingMark,
                position,
                roadTangent,
                inward,
                roadNormal,
                pavementWidth * 0.5f);
            GeneratedSurfaceMeshes.Add(parkingMark);
            AddMeshChild(
                container,
                "kiosk-blue-white-p",
                parkingMark,
                GetBuildingAttachedParkingMarkMaterial(),
                Vector3.zero,
                Vector3.one);
            AddBuildingAttachedParkingLight(
                facility,
                container,
                position,
                roadTangent,
                inward,
                pavementWidth);
            Visuals.Add(container);
            UndergroundParkingLog.Advanced("Built building-attached flat entrance surface: facility="
                                        + facility.Id
                                        + " position="
                                        + FormatVector(position)
                                        + " roadHalfWidth="
                                        + roadHalfWidth.ToString("0.00")
                                        + " pavementWidth="
                                        + pavementWidth.ToString("0.00")
                                        + " roadFrame=segment-bezier-planar"
                                        + " orientation=per-entry-live-entrance-side"
                                        + " exactYawPitchRoll=True"
                                        + " parkingMarkScale=0.40"
                                        + " profile=16x12-triangle-grid"
                                        + " profileAnchor=exact-road-seam-then-direct-detailed-ground"
                                        + " raisedRoadTerrainSeparation=4.00"
                                        + " surfaceClearance=0.06"
                                        + " sideKerbs=viewer-left-right-only"
                                        + " kerbSurfaceClearance=0.10"
                                        + " kerbStoneWidth=0.24 kerbStoneTargetLength=0.70"
                                        + " lightingShader="
                                        + GetBuildingAttachedTarmacMaterial().shader.name
                                        + " parkingMarkShader="
                                        + GetBuildingAttachedParkingMarkMaterial().shader.name
                                        + " parkingMarkVertexPalette=True"
                                        + " parkingMarkDisplayTarget=0,102,178"
                                        + " parkingMarkVertexEncoding=active-color-space"
                                        + " pFloodlight=True");
        }

        private static void AddBuildingAttachedParkingLight(
            UndergroundParkingFacility facility,
            GameObject container,
            Vector3 center,
            Vector3 roadTangent,
            Vector3 inward,
            float pavementWidth)
        {
            const float lampX = -2.72f;
            const float poleHeight = 3.2f;
            float lampZ = pavementWidth * 0.5f - 0.32f;
            float panelCenterZ = pavementWidth * 0.5f - 0.12f - 4.8f * 0.4f * 0.5f;
            Vector3 lampRoadNormal = Vector3.Cross(inward, roadTangent);
            if (lampRoadNormal.y < 0f)
                lampRoadNormal = -lampRoadNormal;
            lampRoadNormal = NormalizeVector(lampRoadNormal, Vector3.up);
            float baseLocalY = BuildingAttachedPortalSurfaceLift
                               - BuildingAttachedLampBaseLowering
                               + ResolveDetailedRoadProfileOffset(
                                   center,
                                   roadTangent,
                                   inward,
                                   lampRoadNormal,
                                   lampX,
                                   lampZ,
                                   pavementWidth * 0.5f);

            AddMeshChild(
                container,
                "p-floodlight-pole",
                GetAttachedLampPoleMesh(),
                GetEntranceKioskMaterial(),
                new Vector3(lampX, baseLocalY + poleHeight * 0.5f, lampZ),
                Vector3.one);

            Vector3 fixture = new Vector3(lampX, baseLocalY + poleHeight, lampZ);
            Vector3 target = new Vector3(0f, 0.07f, panelCenterZ);
            GameObject head = new GameObject("p-floodlight-head");
            head.transform.parent = container.transform;
            head.transform.localPosition = fixture;
            head.transform.localRotation = Quaternion.LookRotation(target - fixture, Vector3.up);
            AddMeshChild(
                head,
                "housing",
                GetAttachedLampHeadMesh(),
                GetEntranceKioskMaterial(),
                Vector3.zero,
                Vector3.one);
            AddMeshChild(
                head,
                "lens",
                GetAttachedLampLensMesh(),
                GetBuildingAttachedLampLensMaterial(),
                new Vector3(0f, 0f, 0.205f),
                Vector3.one);
            // Use the kiosk P floodlight verbatim. The attached fixture keeps
            // its road-aligned physical position, but the illumination is no
            // longer a separate warm cookie/pool approximation.
            Light light = head.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(0.94f, 0.97f, 1f);
            light.range = 12f;
            light.spotAngle = 82f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false;

            UndergroundParkingEntranceLightController controller =
                head.AddComponent<UndergroundParkingEntranceLightController>();
            controller.Initialize(light, facility.Id);
            UndergroundParkingLog.Advanced("Built building-attached P floodlight: facility="
                                       + facility.Id
                                       + " source=kiosk-P-light"
                                       + " side=negative-road-tangent"
                                       + " height="
                                       + poleHeight.ToString("0.00")
                                       + " baseLowering=0.06"
                                       + " color=0.94,0.97,1.00"
                                       + " range=12.00 angle=82.00"
                                       + " maxIntensity=3.80 cookie=None"
                                       + " syntheticPool=False"
                                       + " nightController=kiosk-shared");
        }

        private static void ResolveRoadCrossSection(
            ushort segmentId,
            out float halfWidth,
            out float pavementWidth)
        {
            halfWidth = 8f;
            pavementWidth = 3f;
            NetManager netManager = NetManager.instance;
            if (netManager == null || segmentId == 0 || segmentId >= netManager.m_segments.m_size)
                return;

            NetInfo info = netManager.m_segments.m_buffer[segmentId].Info;
            if (info == null)
                return;

            halfWidth = Mathf.Max(2f, info.m_halfWidth);
            pavementWidth = Mathf.Clamp(info.m_pavementWidth, 1f, halfWidth);
        }

        private static bool TryResolveBuildingAttachedRoadFrame(
            UndergroundParkingFacility facility,
            out Vector3 surfaceCenter,
            out Vector3 roadTangent,
            out Vector3 inward,
            out Vector3 roadNormal,
            out float roadHalfWidth,
            out float pavementWidth)
        {
            surfaceCenter = Vector3.zero;
            roadTangent = Vector3.forward;
            inward = Vector3.right;
            roadNormal = Vector3.up;
            ResolveRoadCrossSection(
                facility.SurfaceSegmentId,
                out roadHalfWidth,
                out pavementWidth);

            NetManager netManager = NetManager.instance;
            ushort segmentId = facility.SurfaceSegmentId;
            if (netManager == null
                || segmentId == 0
                || segmentId >= netManager.m_segments.m_size)
            {
                return false;
            }

            ref NetSegment segment = ref netManager.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == 0
                || segment.Info == null
                || segment.m_startNode == 0
                || segment.m_endNode == 0)
            {
                return false;
            }

            Vector3 start = netManager.m_nodes.m_buffer[segment.m_startNode].m_position;
            Vector3 end = netManager.m_nodes.m_buffer[segment.m_endNode].m_position;
            Vector3 middleA;
            Vector3 middleB;
            NetSegment.CalculateMiddlePoints(
                start,
                segment.m_startDirection,
                end,
                segment.m_endDirection,
                false,
                false,
                out middleA,
                out middleB);
            Bezier3 bezier = new Bezier3
            {
                a = start,
                b = middleA,
                c = middleB,
                d = end
            };

            float t = Mathf.Clamp01(facility.SurfaceSegmentPosition);
            float before = Mathf.Clamp01(t - 0.0125f);
            float after = Mathf.Clamp01(t + 0.0125f);
            Vector3 roadCenter = bezier.Position(t);
            roadTangent = bezier.Position(after) - bezier.Position(before);
            if (roadTangent.sqrMagnitude <= 0.001f)
                roadTangent = NormalizeFlat(facility.Direction, Vector3.forward);
            else
                roadTangent.Normalize();

            Vector3 flatTangent = NormalizeFlat(roadTangent, facility.Direction);
            inward = new Vector3(-flatTangent.z, 0f, flatTangent.x);

            // Resolve every entrance from its own live geometry. A segment's
            // start/end ordering says nothing about which pavement edge this
            // particular entrance occupies, and persisted Side values can come
            // from older placement/recovery builds. The vector from this exact
            // segment position to this exact entrance remains authoritative.
            Vector3 entranceSide = facility.EntrancePosition - roadCenter;
            entranceSide.y = 0f;
            if (entranceSide.sqrMagnitude <= 0.001f)
                entranceSide = facility.Side;
            entranceSide = NormalizeFlat(entranceSide, inward);
            if (Vector3.Dot(inward, entranceSide) < 0f)
                inward = -inward;

            // Canonicalise the tangent to viewer-right for this resolved side.
            // This makes local +X, the roadside sign offset and the opposite
            // lamp side agree even when another segment stores its nodes in the
            // reverse order. Longitudinal pitch is preserved by flipping the
            // complete 3D tangent rather than flattening it.
            Vector3 viewerRight = new Vector3(inward.z, 0f, -inward.x);
            if (Vector3.Dot(flatTangent, viewerRight) < 0f)
                roadTangent = -roadTangent;

            roadNormal = Vector3.Cross(inward, roadTangent);
            if (roadNormal.y < 0f)
                roadNormal = -roadNormal;
            roadNormal = NormalizeVector(roadNormal, Vector3.up);
            surfaceCenter = roadCenter
                            + inward * (roadHalfWidth - pavementWidth * 0.5f);
            return true;
        }

        private static Mesh GetPrefabMesh(PropInfo prefab)
        {
            if (prefab == null)
                return null;
            return prefab.m_mesh != null ? prefab.m_mesh : prefab.m_lodMesh;
        }

        private static Material GetPrefabMaterial(PropInfo prefab)
        {
            if (prefab == null)
                return null;
            return prefab.m_material != null ? prefab.m_material : prefab.m_lodMaterial;
        }

        internal static bool TryGetExistingTunnelTraversal(
            UndergroundParkingFacility facility,
            out Vector3 surfaceEntry,
            out Vector3 garageEntry)
        {
            surfaceEntry = Vector3.zero;
            garageEntry = Vector3.zero;
            if (!facility.IsValid
                || facility.TargetBuildingId == 0
                || !UndergroundParkingOccupancyManager.SupportsAutomatedTunnel(facility))
                return false;

            Vector3 surfaceCenter;
            Vector3 roadTangent;
            Vector3 inward;
            Vector3 roadNormal;
            float roadHalfWidth;
            float pavementWidth;
            if (!TryResolveBuildingAttachedRoadFrame(
                    facility,
                    out surfaceCenter,
                    out roadTangent,
                    out inward,
                    out roadNormal,
                    out roadHalfWidth,
                    out pavementWidth))
            {
                return false;
            }

            roadTangent.Normalize();
            float surfaceHalfWidth = BuildingAttachedSurfaceWidth * 0.5f;
            float surfaceHalfDepth = pavementWidth * 0.5f;
            Vector3 surfaceLeftCarriageway = surfaceCenter
                                             - roadTangent * surfaceHalfWidth
                                             - inward * surfaceHalfDepth
                                             + roadNormal * ResolveDetailedRoadProfileOffset(
                                                 surfaceCenter, roadTangent, inward, roadNormal,
                                                 -surfaceHalfWidth, -surfaceHalfDepth, surfaceHalfDepth)
                                             - roadNormal * BuildingAttachedPortalSurfaceLift;
            Vector3 surfaceRightCarriageway = surfaceCenter
                                              + roadTangent * surfaceHalfWidth
                                              - inward * surfaceHalfDepth
                                              + roadNormal * ResolveDetailedRoadProfileOffset(
                                                  surfaceCenter, roadTangent, inward, roadNormal,
                                                  surfaceHalfWidth, -surfaceHalfDepth, surfaceHalfDepth)
                                              - roadNormal * BuildingAttachedPortalSurfaceLift;
            Vector3 surfaceLeftOuter = surfaceCenter
                                       - roadTangent * surfaceHalfWidth
                                       + inward * surfaceHalfDepth
                                       + roadNormal * ResolveDetailedRoadProfileOffset(
                                           surfaceCenter, roadTangent, inward, roadNormal,
                                           -surfaceHalfWidth, surfaceHalfDepth, surfaceHalfDepth)
                                       - roadNormal * BuildingAttachedPortalSurfaceLift;
            Vector3 surfaceRightOuter = surfaceCenter
                                        + roadTangent * surfaceHalfWidth
                                        + inward * surfaceHalfDepth
                                        + roadNormal * ResolveDetailedRoadProfileOffset(
                                            surfaceCenter, roadTangent, inward, roadNormal,
                                            surfaceHalfWidth, surfaceHalfDepth, surfaceHalfDepth)
                                        - roadNormal * BuildingAttachedPortalSurfaceLift;

            Vector3 garageCenter =
                UndergroundParkingGeometry.ResolveCurrentVisualGarageCenter(facility);
            float garageFloorY =
                UndergroundParkingOccupancyManager.GetGarageLevelY(facility, 0);
            Vector3 rampTop;
            Vector3 garageMouthAxis;
            Vector3 garageWallNormal;
            if (!UndergroundParkingOccupancyManager.TryGetInternalRampTopGeometry(
                    facility,
                    garageFloorY,
                    out rampTop,
                    out garageMouthAxis,
                    out garageWallNormal))
            {
                return false;
            }

            Vector3 carriagewayCenter =
                (surfaceLeftCarriageway + surfaceRightCarriageway) * 0.5f;
            Vector3 outerCenter =
                (surfaceLeftOuter + surfaceRightOuter) * 0.5f;
            bool outerEdgeIsNearGarage = FlatSqrDistance(outerCenter, rampTop)
                                         <= FlatSqrDistance(carriagewayCenter, rampTop);
            Vector3 surfaceEntranceBackCenter = outerEdgeIsNearGarage
                ? outerCenter
                : carriagewayCenter;
            Vector3 turningPadDirection = ResolveTunnelTurningPadDirection(
                inward,
                surfaceEntranceBackCenter,
                rampTop);
            Vector3 turningPadAxis = NormalizeFlat(
                roadTangent,
                Vector3.Cross(Vector3.up, turningPadDirection));
            Vector3 surfaceOpeningCenter;
            ResolveSurfaceTunnelOpening(
                surfaceEntranceBackCenter,
                turningPadDirection,
                turningPadAxis,
                rampTop,
                out surfaceOpeningCenter,
                out _,
                out _);
            // The chamber begins at the garage-side edge behind the complete
            // P entrance. Its left, right or far wall owns the one tunnel
            // opening according to the actual garage-mouth direction.
            surfaceEntry = surfaceOpeningCenter
                           + Vector3.down * TunnelTurningPadDepth
                           + Vector3.up * 0.18f;
            bool externalGarageApproach = RequiresExternalGaragePad(
                surfaceEntry,
                rampTop,
                garageWallNormal);
            Vector3 garagePadOutward = NormalizeFlat(
                garageWallNormal,
                surfaceEntry - rampTop);
            Vector3 towardTunnel = surfaceEntry - rampTop;
            towardTunnel.y = 0f;
            if (towardTunnel.sqrMagnitude > 0.001f
                && Vector3.Dot(garagePadOutward, towardTunnel) < 0f)
            {
                garagePadOutward = -garagePadOutward;
            }
            garageEntry = rampTop;
            if (externalGarageApproach)
            {
                Vector3 garagePadNearCenter = rampTop
                                               + garagePadOutward
                                               * TunnelTurningPadLength;
                Vector3 garagePadDirection = -garagePadOutward;
                ResolveGarageTunnelOpening(
                    garagePadNearCenter,
                    garagePadDirection,
                    NormalizeFlat(garageMouthAxis, facility.GarageForward),
                    surfaceEntry,
                    out garageEntry,
                    out _,
                    out _);
            }
            garageEntry += Vector3.up * 0.18f;
            return true;
        }

        private static bool RequiresExternalGaragePad(
            Vector3 surfaceEntry,
            Vector3 garageMouthCenter,
            Vector3 garageWallNormal)
        {
            Vector3 towardSurface = surfaceEntry - garageMouthCenter;
            towardSurface.y = 0f;
            return Vector3.Dot(towardSurface, garageWallNormal) > 0f;
        }

        private static Vector3 ResolveTunnelTurningPadDirection(
            Vector3 inward,
            Vector3 surfaceFloorCenter,
            Vector3 garageMouthCenter)
        {
            Vector3 direction = NormalizeFlat(
                inward,
                garageMouthCenter - surfaceFloorCenter);
            Vector3 towardGarage = garageMouthCenter - surfaceFloorCenter;
            towardGarage.y = 0f;
            if (towardGarage.sqrMagnitude > 0.001f
                && Vector3.Dot(direction, towardGarage) < 0f)
            {
                direction = -direction;
            }
            return direction;
        }

        private static void ResolveSurfaceTunnelOpening(
            Vector3 turningPadNearCenter,
            Vector3 turningPadDirection,
            Vector3 turningPadAxis,
            Vector3 garageMouthCenter,
            out Vector3 openingCenter,
            out Vector3 openingAxis,
            out SurfaceTunnelOpeningFace openingFace)
        {
            float halfLength = TunnelTurningPadLength * 0.5f;
            float halfWidth = TunnelTurningPadWidth * 0.5f;
            Vector3 chamberCenter = turningPadNearCenter
                                    + turningPadDirection * halfLength;
            Vector3 farCenter = turningPadNearCenter
                                + turningPadDirection * TunnelTurningPadLength;
            Vector3 leftCenter = chamberCenter - turningPadAxis * halfWidth;
            Vector3 rightCenter = chamberCenter + turningPadAxis * halfWidth;

            openingCenter = farCenter;
            openingAxis = turningPadAxis;
            openingFace = SurfaceTunnelOpeningFace.Far;
            float bestDistance = FlatSqrDistance(farCenter, garageMouthCenter);
            float leftDistance = FlatSqrDistance(leftCenter, garageMouthCenter);
            if (leftDistance < bestDistance)
            {
                bestDistance = leftDistance;
                openingCenter = leftCenter;
                openingAxis = turningPadDirection;
                openingFace = SurfaceTunnelOpeningFace.Left;
            }
            float rightDistance = FlatSqrDistance(rightCenter, garageMouthCenter);
            if (rightDistance < bestDistance)
            {
                openingCenter = rightCenter;
                openingAxis = turningPadDirection;
                openingFace = SurfaceTunnelOpeningFace.Right;
            }
        }

        private static void ResolveGarageTunnelOpening(
            Vector3 padNearCenter,
            Vector3 padDirection,
            Vector3 padAxis,
            Vector3 surfaceTunnelCenter,
            out Vector3 openingCenter,
            out Vector3 openingAxis,
            out GarageTunnelOpeningFace openingFace)
        {
            float halfLength = TunnelTurningPadLength * 0.5f;
            float halfWidth = TunnelTurningPadWidth * 0.5f;
            Vector3 chamberCenter = padNearCenter + padDirection * halfLength;
            Vector3 leftCenter = chamberCenter - padAxis * halfWidth;
            Vector3 rightCenter = chamberCenter + padAxis * halfWidth;

            // The far face is permanently open to the selected garage aisle.
            // The descending tunnel must therefore use whichever remaining
            // face it actually reaches instead of always piercing the exterior
            // near wall.
            openingCenter = padNearCenter;
            openingAxis = padAxis;
            openingFace = GarageTunnelOpeningFace.Near;
            float bestDistance = FlatSqrDistance(
                padNearCenter,
                surfaceTunnelCenter);
            float leftDistance = FlatSqrDistance(leftCenter, surfaceTunnelCenter);
            if (leftDistance < bestDistance)
            {
                bestDistance = leftDistance;
                openingCenter = leftCenter;
                openingAxis = padDirection;
                openingFace = GarageTunnelOpeningFace.Left;
            }
            float rightDistance = FlatSqrDistance(rightCenter, surfaceTunnelCenter);
            if (rightDistance < bestDistance)
            {
                openingCenter = rightCenter;
                openingAxis = padDirection;
                openingFace = GarageTunnelOpeningFace.Right;
            }
        }

        private static void AddTunnelRenderItem(
            UndergroundParkingFacility facility,
            Vector3 entrancePosition,
            Vector3 garageCenter,
            float garageRadius)
        {
            Vector3 surfaceCenter;
            Vector3 roadTangent;
            Vector3 inward;
            Vector3 roadNormal;
            float roadHalfWidth;
            float pavementWidth;
            if (!TryResolveBuildingAttachedRoadFrame(
                    facility,
                    out surfaceCenter,
                    out roadTangent,
                    out inward,
                    out roadNormal,
                    out roadHalfWidth,
                    out pavementWidth))
                return;

            roadTangent.Normalize();
            float surfaceHalfWidth = BuildingAttachedSurfaceWidth * 0.5f;
            float surfaceHalfDepth = pavementWidth * 0.5f;
            Vector3 surfaceLeftCarriageway = surfaceCenter
                                             - roadTangent * surfaceHalfWidth
                                             - inward * surfaceHalfDepth
                                             + roadNormal * ResolveDetailedRoadProfileOffset(
                                                 surfaceCenter, roadTangent, inward, roadNormal,
                                                 -surfaceHalfWidth, -surfaceHalfDepth, surfaceHalfDepth)
                                             - roadNormal * BuildingAttachedPortalSurfaceLift;
            Vector3 surfaceRightCarriageway = surfaceCenter
                                              + roadTangent * surfaceHalfWidth
                                              - inward * surfaceHalfDepth
                                              + roadNormal * ResolveDetailedRoadProfileOffset(
                                                  surfaceCenter, roadTangent, inward, roadNormal,
                                                  surfaceHalfWidth, -surfaceHalfDepth, surfaceHalfDepth)
                                              - roadNormal * BuildingAttachedPortalSurfaceLift;
            Vector3 surfaceLeftOuter = surfaceCenter
                                       - roadTangent * surfaceHalfWidth
                                       + inward * surfaceHalfDepth
                                       + roadNormal * ResolveDetailedRoadProfileOffset(
                                           surfaceCenter, roadTangent, inward, roadNormal,
                                           -surfaceHalfWidth, surfaceHalfDepth, surfaceHalfDepth)
                                       - roadNormal * BuildingAttachedPortalSurfaceLift;
            Vector3 surfaceRightOuter = surfaceCenter
                                        + roadTangent * surfaceHalfWidth
                                        + inward * surfaceHalfDepth
                                        + roadNormal * ResolveDetailedRoadProfileOffset(
                                            surfaceCenter, roadTangent, inward, roadNormal,
                                            surfaceHalfWidth, surfaceHalfDepth, surfaceHalfDepth)
                                        - roadNormal * BuildingAttachedPortalSurfaceLift;

            Vector3 garageMouthCenter;
            Vector3 garageMouthAxis;
            Vector3 garageWallNormal;
            float garageFloorY =
                UndergroundParkingOccupancyManager.GetGarageLevelY(facility, 0);
            if (!UndergroundParkingOccupancyManager.TryGetInternalRampTopGeometry(
                    facility,
                    garageFloorY,
                    out garageMouthCenter,
                    out garageMouthAxis,
                    out garageWallNormal)
                && !TryResolveGarageWallMouth(
                    facility,
                    surfaceCenter,
                    garageCenter,
                    out garageMouthCenter,
                    out garageMouthAxis,
                    out garageWallNormal))
            {
                return;
            }

            Vector3 garagePadOutward = NormalizeFlat(
                garageWallNormal,
                surfaceCenter - garageMouthCenter);
            Vector3 towardTunnel = surfaceCenter - garageMouthCenter;
            towardTunnel.y = 0f;
            if (towardTunnel.sqrMagnitude > 0.001f
                && Vector3.Dot(garagePadOutward, towardTunnel) < 0f)
            {
                garagePadOutward = -garagePadOutward;
            }
            Vector3 garageCornerA;
            Vector3 garageCornerB;

            Vector3 carriagewayCenter = (surfaceLeftCarriageway + surfaceRightCarriageway) * 0.5f;
            Vector3 outerCenter = (surfaceLeftOuter + surfaceRightOuter) * 0.5f;
            bool outerEdgeIsNearGarage = FlatSqrDistance(outerCenter, garageMouthCenter)
                                         <= FlatSqrDistance(carriagewayCenter, garageMouthCenter);
            Vector3 surfaceNearLeft = outerEdgeIsNearGarage
                ? surfaceLeftOuter
                : surfaceLeftCarriageway;
            Vector3 surfaceNearRight = outerEdgeIsNearGarage
                ? surfaceRightOuter
                : surfaceRightCarriageway;
            Vector3 surfaceFarLeft = outerEdgeIsNearGarage
                ? surfaceLeftCarriageway
                : surfaceLeftOuter;
            Vector3 surfaceFarRight = outerEdgeIsNearGarage
                ? surfaceRightCarriageway
                : surfaceRightOuter;

            Vector3 surfaceEntranceBackCenter =
                (surfaceNearLeft + surfaceNearRight) * 0.5f;
            Vector3 turningPadDirection = ResolveTunnelTurningPadDirection(
                inward,
                surfaceEntranceBackCenter,
                garageMouthCenter);
            Vector3 turningPadAxis = NormalizeFlat(
                roadTangent,
                Vector3.Cross(Vector3.up, turningPadDirection));
            float turningPadHalfWidth = TunnelTurningPadWidth * 0.5f;
            // Keep the complete 5x5x4 chamber behind and below the entrance:
            // its roof is flush with the surface and its floor is one clear
            // chamber depth underground.
            float turningPadY = surfaceEntranceBackCenter.y
                                - TunnelTurningPadDepth;
            Vector3 turningPadNearCenter = surfaceEntranceBackCenter;
            turningPadNearCenter.y = turningPadY;
            Vector3 turningPadFarCenter = turningPadNearCenter
                                          + turningPadDirection * TunnelTurningPadLength;
            Vector3 turningPadNearLeft = turningPadNearCenter
                                         - turningPadAxis * turningPadHalfWidth;
            Vector3 turningPadNearRight = turningPadNearCenter
                                          + turningPadAxis * turningPadHalfWidth;
            Vector3 turningPadFarLeft = turningPadFarCenter
                                        - turningPadAxis * turningPadHalfWidth;
            Vector3 turningPadFarRight = turningPadFarCenter
                                         + turningPadAxis * turningPadHalfWidth;
            Vector3 surfaceTunnelStartCenter;
            Vector3 surfaceTunnelStartAxis;
            SurfaceTunnelOpeningFace surfaceTunnelOpeningFace;
            ResolveSurfaceTunnelOpening(
                turningPadNearCenter,
                turningPadDirection,
                turningPadAxis,
                garageMouthCenter,
                out surfaceTunnelStartCenter,
                out surfaceTunnelStartAxis,
                out surfaceTunnelOpeningFace);
            surfaceTunnelStartCenter.y = turningPadY;
            Vector3 tunnelStartLeft = surfaceTunnelStartCenter
                                      - surfaceTunnelStartAxis
                                      * (TunnelWidth * 0.5f);
            Vector3 tunnelStartRight = surfaceTunnelStartCenter
                                       + surfaceTunnelStartAxis
                                       * (TunnelWidth * 0.5f);
            bool externalGarageApproach = RequiresExternalGaragePad(
                surfaceTunnelStartCenter,
                garageMouthCenter,
                garageWallNormal);
            garagePadOutward = NormalizeFlat(
                garageWallNormal,
                surfaceTunnelStartCenter - garageMouthCenter);
            towardTunnel = surfaceTunnelStartCenter - garageMouthCenter;
            towardTunnel.y = 0f;
            if (towardTunnel.sqrMagnitude > 0.001f
                && Vector3.Dot(garagePadOutward, towardTunnel) < 0f)
            {
                garagePadOutward = -garagePadOutward;
            }
            Vector3 garagePadAxis = NormalizeFlat(
                garageMouthAxis,
                facility.GarageForward);
            Vector3 garagePadDirection = -garagePadOutward;
            Vector3 garagePadNearCenter = externalGarageApproach
                ? garageMouthCenter + garagePadOutward * TunnelTurningPadLength
                : garageMouthCenter;
            garagePadNearCenter.y = garageFloorY;
            Vector3 garagePadFarCenter = garagePadNearCenter
                                         + garagePadDirection
                                         * TunnelTurningPadLength;
            Vector3 garageTunnelOpeningCenter = garageMouthCenter;
            Vector3 garageTunnelOpeningAxis = garageMouthAxis;
            GarageTunnelOpeningFace garageTunnelOpeningFace =
                GarageTunnelOpeningFace.Near;
            if (externalGarageApproach)
            {
                ResolveGarageTunnelOpening(
                    garagePadNearCenter,
                    garagePadDirection,
                    garagePadAxis,
                    surfaceTunnelStartCenter,
                    out garageTunnelOpeningCenter,
                    out garageTunnelOpeningAxis,
                    out garageTunnelOpeningFace);
            }
            garageTunnelOpeningCenter.y = garageFloorY;
            garageCornerA = garageTunnelOpeningCenter
                            - garageTunnelOpeningAxis * (TunnelWidth * 0.5f);
            garageCornerB = garageTunnelOpeningCenter
                            + garageTunnelOpeningAxis * (TunnelWidth * 0.5f);
            garageCornerA.y = garageFloorY + TunnelHeight * 0.5f;
            garageCornerB.y = garageFloorY + TunnelHeight * 0.5f;
            Vector3 garagePadNearLeft = garagePadNearCenter
                                        - garagePadAxis * turningPadHalfWidth;
            Vector3 garagePadNearRight = garagePadNearCenter
                                         + garagePadAxis * turningPadHalfWidth;
            Vector3 garagePadFarLeft = garagePadFarCenter
                                       - garagePadAxis * turningPadHalfWidth;
            Vector3 garagePadFarRight = garagePadFarCenter
                                        + garagePadAxis * turningPadHalfWidth;

            Mesh mesh = CreateUpwardSurfaceMouthTunnelMesh(
                surfaceNearLeft,
                surfaceNearRight,
                surfaceFarLeft,
                surfaceFarRight,
                turningPadNearLeft,
                turningPadNearRight,
                turningPadFarLeft,
                turningPadFarRight,
                tunnelStartLeft,
                tunnelStartRight,
                surfaceTunnelOpeningFace,
                garageCornerA,
                garageCornerB,
                garageTunnelOpeningFace,
                garagePadNearLeft,
                garagePadNearRight,
                garagePadFarLeft,
                garagePadFarRight,
                externalGarageApproach,
                TunnelHeight);
            if (mesh == null)
                return;

            Vector3 center = (surfaceLeftCarriageway
                              + surfaceRightCarriageway
                              + surfaceLeftOuter
                              + surfaceRightOuter
                              + garageCornerA
                              + garageCornerB) / 6f;
            float length = Mathf.Max(
                (garageCornerA - surfaceNearLeft).magnitude,
                (garageCornerB - surfaceNearRight).magnitude);
            GeneratedTunnelMeshes.Add(mesh);
            RenderItems.Add(new RenderItem(
                facility.Id, mesh, Matrix4x4.identity, GetGarageStructureMaterial(), center,
                Mathf.Max(garageRadius, length * 0.5f)));
            float surfaceFloorY = turningPadY;
            float tunnelRun = Mathf.Max(
                0.1f,
                Mathf.Sqrt(FlatSqrDistance(
                    surfaceTunnelStartCenter,
                    (garageCornerA + garageCornerB) * 0.5f)));
            float tunnelGrade = Mathf.Abs(surfaceFloorY - garageFloorY) / tunnelRun;
            UndergroundParkingLog.Advanced("UPG upward full-slab-mouth tunnel built: facility="
                                        + facility.Id
                                        + " surfaceLeftCarriageway=" + FormatVector(surfaceLeftCarriageway)
                                        + " surfaceRightCarriageway=" + FormatVector(surfaceRightCarriageway)
                                        + " surfaceLeftOuter=" + FormatVector(surfaceLeftOuter)
                                        + " surfaceRightOuter=" + FormatVector(surfaceRightOuter)
                                        + " garageCornerA=" + FormatVector(garageCornerA)
                                        + " garageCornerB=" + FormatVector(garageCornerB)
                                        + " surfaceMouthWidth="
                                        + BuildingAttachedSurfaceWidth.ToString("0.00")
                                        + " surfaceMouthDepth="
                                        + pavementWidth.ToString("0.00")
                                        + " garageMouthWidth="
                                        + TunnelWidth.ToString("0.00")
                                        + " garageMouthLevel=level-0-driving-floor"
                                        + " tunnelRoadwayGrade="
                                        + tunnelGrade.ToString("0.000")
                                        + " singleRamp=True"
                                        + " surfaceMouthPlane=shared-profiled-slab"
                                        + " surfaceMouthCoverage=complete-entry-footprint"
                                        + " projection=upward-not-sideways"
                                        + " planShape=direct-slab-to-garage-taper"
                                        + " turningPad=5x5x4"
                                        + " garagePad="
                                        + (externalGarageApproach ? "5x5x4" : "none-internal")
                                        + " tunnelEntry=" + FormatVector(surfaceTunnelStartCenter)
                                        + " surfaceOpeningFace=" + surfaceTunnelOpeningFace
                                        + " tunnelExit=" + FormatVector(garageTunnelOpeningCenter)
                                        + " garageOpeningFace=" + garageTunnelOpeningFace
                                        + " cornerPairing=minimum-distance"
                                        + " intermediateGeometry=False"
                                        + " centrelineInference=False");
        }

        private static bool TryResolveGarageWallMouth(
            UndergroundParkingFacility facility,
            Vector3 roadMouthCenter,
            Vector3 garageCenter,
            out Vector3 mouthCenter,
            out Vector3 mouthAxis,
            out Vector3 wallNormal)
        {
            mouthCenter = Vector3.zero;
            mouthAxis = Vector3.forward;
            wallNormal = Vector3.right;
            Vector3 right = NormalizeFlat(facility.GarageRight, Vector3.right);
            Vector3 forward = NormalizeFlat(facility.GarageForward, Vector3.forward);
            float halfWidth = Mathf.Max(0.5f, facility.GarageWidth * 0.5f);
            float halfLength = Mathf.Max(0.5f, facility.GarageLength * 0.5f);
            float mouthHalf = TunnelWidth * 0.5f;
            float bestDistance = float.MaxValue;

            TryGarageWallCandidate(
                roadMouthCenter, garageCenter + right * halfWidth,
                forward, right, halfLength, facility.GarageLength, mouthHalf,
                ref bestDistance, ref mouthCenter, ref mouthAxis, ref wallNormal);
            TryGarageWallCandidate(
                roadMouthCenter, garageCenter - right * halfWidth,
                forward, -right, halfLength, facility.GarageLength, mouthHalf,
                ref bestDistance, ref mouthCenter, ref mouthAxis, ref wallNormal);
            TryGarageWallCandidate(
                roadMouthCenter, garageCenter + forward * halfLength,
                right, forward, halfWidth, facility.GarageWidth, mouthHalf,
                ref bestDistance, ref mouthCenter, ref mouthAxis, ref wallNormal);
            TryGarageWallCandidate(
                roadMouthCenter, garageCenter - forward * halfLength,
                right, -forward, halfWidth, facility.GarageWidth, mouthHalf,
                ref bestDistance, ref mouthCenter, ref mouthAxis, ref wallNormal);

            if (bestDistance == float.MaxValue)
                return false;

            mouthCenter -= wallNormal * TunnelGarageWallOverlap;
            return true;
        }

        private static void TryGarageWallCandidate(
            Vector3 roadMouthCenter,
            Vector3 wallCenter,
            Vector3 wallAxis,
            Vector3 wallNormal,
            float wallHalfLength,
            float fullWallLength,
            float mouthHalf,
            ref float bestDistance,
            ref Vector3 bestCenter,
            ref Vector3 bestAxis,
            ref Vector3 bestNormal)
        {
            float usableHalf = wallHalfLength
                               - fullWallLength * GarageChamferRatio
                               - TunnelCornerClearance;
            if (usableHalf < mouthHalf)
                return;

            wallAxis = NormalizeFlat(wallAxis, Vector3.forward);
            wallNormal = NormalizeFlat(wallNormal, Vector3.right);
            float along = Vector3.Dot(roadMouthCenter - wallCenter, wallAxis);
            along = Mathf.Clamp(along, -usableHalf + mouthHalf, usableHalf - mouthHalf);
            Vector3 candidate = wallCenter + wallAxis * along;
            float distance = FlatSqrDistance(roadMouthCenter, candidate);
            if (distance >= bestDistance)
                return;

            bestDistance = distance;
            bestCenter = candidate;
            bestAxis = wallAxis;
            bestNormal = wallNormal;
        }

        private static Mesh CreateUpwardSurfaceMouthTunnelMesh(
            Vector3 surfaceNearLeft,
            Vector3 surfaceNearRight,
            Vector3 surfaceFarLeft,
            Vector3 surfaceFarRight,
            Vector3 turningPadNearLeft,
            Vector3 turningPadNearRight,
            Vector3 turningPadFarLeft,
            Vector3 turningPadFarRight,
            Vector3 tunnelStartLeft,
            Vector3 tunnelStartRight,
            SurfaceTunnelOpeningFace surfaceOpeningFace,
            Vector3 garageCornerA,
            Vector3 garageCornerB,
            GarageTunnelOpeningFace garageOpeningFace,
            Vector3 garagePadNearLeft,
            Vector3 garagePadNearRight,
            Vector3 garagePadFarLeft,
            Vector3 garagePadFarRight,
            bool includeGaragePad,
            float height)
        {
            Vector3 garageLinkA = garageCornerA;
            Vector3 garageLinkB = garageCornerB;
            float directPairing = FlatSqrDistance(tunnelStartLeft, garageLinkA)
                                  + FlatSqrDistance(tunnelStartRight, garageLinkB);
            float crossedPairing = FlatSqrDistance(tunnelStartLeft, garageLinkB)
                                   + FlatSqrDistance(tunnelStartRight, garageLinkA);
            if (crossedPairing < directPairing)
            {
                Vector3 swap = garageLinkA;
                garageLinkA = garageLinkB;
                garageLinkB = swap;
            }
            if ((garageLinkA - tunnelStartLeft).sqrMagnitude < 0.25f
                || (garageLinkB - tunnelStartRight).sqrMagnitude < 0.25f)
                return null;

            float halfHeight = height * 0.5f;
            Vector3 up = Vector3.up * halfHeight;
            Vector3 garageABottom = garageCornerA - up;
            Vector3 garageBBottom = garageCornerB - up;
            Vector3 garageATop = garageCornerA + up;
            Vector3 garageBTop = garageCornerB + up;
            Vector3 garageLinkABottom = garageLinkA - up;
            Vector3 garageLinkBBottom = garageLinkB - up;
            Vector3 garageLinkATop = garageLinkA + up;
            Vector3 garageLinkBTop = garageLinkB + up;
            Vector3 chamberUp = Vector3.up * TunnelTurningPadDepth;
            Vector3 turningPadNearLeftTop = turningPadNearLeft + chamberUp;
            Vector3 turningPadNearRightTop = turningPadNearRight + chamberUp;
            Vector3 turningPadFarLeftTop = turningPadFarLeft + chamberUp;
            Vector3 turningPadFarRightTop = turningPadFarRight + chamberUp;
            Vector3 tunnelStartLeftTop = tunnelStartLeft + Vector3.up * height;
            Vector3 tunnelStartRightTop = tunnelStartRight + Vector3.up * height;
            Vector3 garagePadNearLeftTop = garagePadNearLeft + chamberUp;
            Vector3 garagePadNearRightTop = garagePadNearRight + chamberUp;
            Vector3 garagePadFarLeftTop = garagePadFarLeft + chamberUp;
            Vector3 garagePadFarRightTop = garagePadFarRight + chamberUp;
            MeshDraft draft = new MeshDraft();
            // The surface end is a horizontal opening matching the complete
            // entry slab. Its edge nearer the garage becomes the roof, while
            // the farther edge becomes the floor. One horizontal 5x5x4 turning
            // chamber follows. Its far wall frames the narrower tunnel mouth
            // before the ruled descent reaches the garage; all sections remain
            // one connected mesh.
            AddQuad(draft, surfaceNearLeft, surfaceNearRight,
                turningPadNearRightTop, turningPadNearLeftTop,
                GarageStructureColor);
            AddQuad(draft, turningPadNearLeftTop, turningPadNearRightTop,
                turningPadFarRightTop, turningPadFarLeftTop,
                GarageStructureColor);
            AddQuad(draft, tunnelStartLeftTop, tunnelStartRightTop,
                garageLinkBTop, garageLinkATop,
                GarageStructureColor);
            AddQuad(draft, surfaceFarRight, surfaceFarLeft,
                turningPadNearLeft, turningPadNearRight,
                GarageStructureColor);
            AddQuad(draft, turningPadNearRight, turningPadNearLeft,
                turningPadFarLeft, turningPadFarRight,
                GarageStructureColor);
            AddQuad(draft, tunnelStartRight, tunnelStartLeft,
                garageLinkABottom, garageLinkBBottom,
                GarageStructureColor);
            AddQuad(draft, surfaceFarLeft, surfaceNearLeft,
                turningPadNearLeftTop, turningPadNearLeft,
                GarageStructureColor);
            AddQuad(draft, tunnelStartLeft, tunnelStartLeftTop,
                garageLinkATop, garageLinkABottom,
                GarageStructureColor);
            AddQuad(draft, surfaceNearRight, surfaceFarRight,
                turningPadNearRight, turningPadNearRightTop,
                GarageStructureColor);
            AddQuad(draft, tunnelStartRightTop, tunnelStartRight,
                garageLinkBBottom, garageLinkBTop,
                GarageStructureColor);
            // The road-facing near side remains the chamber entrance. Choose
            // the left, right or far face nearest the garage mouth for the one
            // framed tunnel opening; the other two faces remain complete.
            if (surfaceOpeningFace == SurfaceTunnelOpeningFace.Left)
            {
                AddQuad(draft, turningPadNearLeft, turningPadNearLeftTop,
                    tunnelStartLeftTop, tunnelStartLeft,
                    GarageStructureColor);
                AddQuad(draft, tunnelStartRight, tunnelStartRightTop,
                    turningPadFarLeftTop, turningPadFarLeft,
                    GarageStructureColor);
                AddQuad(draft, tunnelStartLeftTop, turningPadNearLeftTop,
                    turningPadFarLeftTop, tunnelStartRightTop,
                    GarageStructureColor);
            }
            else
            {
                AddQuad(draft, turningPadNearLeft, turningPadNearLeftTop,
                    turningPadFarLeftTop, turningPadFarLeft,
                    GarageStructureColor);
            }

            if (surfaceOpeningFace == SurfaceTunnelOpeningFace.Right)
            {
                AddQuad(draft, turningPadNearRightTop, turningPadNearRight,
                    tunnelStartLeft, tunnelStartLeftTop,
                    GarageStructureColor);
                AddQuad(draft, tunnelStartRightTop, tunnelStartRight,
                    turningPadFarRight, turningPadFarRightTop,
                    GarageStructureColor);
                AddQuad(draft, turningPadNearRightTop, tunnelStartLeftTop,
                    tunnelStartRightTop, turningPadFarRightTop,
                    GarageStructureColor);
            }
            else
            {
                AddQuad(draft, turningPadNearRightTop, turningPadNearRight,
                    turningPadFarRight, turningPadFarRightTop,
                    GarageStructureColor);
            }

            if (surfaceOpeningFace == SurfaceTunnelOpeningFace.Far)
            {
                AddQuad(draft, turningPadFarLeft, turningPadFarLeftTop,
                    tunnelStartLeftTop, tunnelStartLeft,
                    GarageStructureColor);
                AddQuad(draft, tunnelStartRight, tunnelStartRightTop,
                    turningPadFarRightTop, turningPadFarRight,
                    GarageStructureColor);
                AddQuad(draft, tunnelStartLeftTop, turningPadFarLeftTop,
                    turningPadFarRightTop, tunnelStartRightTop,
                    GarageStructureColor);
            }
            else
            {
                AddQuad(draft, turningPadFarLeft, turningPadFarLeftTop,
                    turningPadFarRightTop, turningPadFarRight,
                    GarageStructureColor);
            }
            if (includeGaragePad)
            {
                // An approach from outside the selected wall needs a level-0
                // landing before it crosses into the garage. An approach from
                // the building side is already internal and ends directly at
                // the ramp mouth, so drawing this chamber would duplicate the
                // landing and corrupt the visible topology.
                AddQuad(draft, garagePadNearLeftTop, garagePadNearRightTop,
                    garagePadFarRightTop, garagePadFarLeftTop,
                    GarageStructureColor);
                AddQuad(draft, garagePadNearRight, garagePadNearLeft,
                    garagePadFarLeft, garagePadFarRight,
                    GarageStructureColor);

                if (garageOpeningFace == GarageTunnelOpeningFace.Near)
                {
                    AddQuad(draft, garagePadNearLeft, garagePadNearLeftTop,
                        garageATop, garageABottom,
                        GarageStructureColor);
                    AddQuad(draft, garageBBottom, garageBTop,
                        garagePadNearRightTop, garagePadNearRight,
                        GarageStructureColor);
                    AddQuad(draft, garageATop, garagePadNearLeftTop,
                        garagePadNearRightTop, garageBTop,
                        GarageStructureColor);
                }
                else
                {
                    AddQuad(draft, garagePadNearLeft, garagePadNearLeftTop,
                        garagePadNearRightTop, garagePadNearRight,
                        GarageStructureColor);
                }

                if (garageOpeningFace == GarageTunnelOpeningFace.Left)
                {
                    AddQuad(draft, garagePadNearLeft, garagePadNearLeftTop,
                        garageATop, garageABottom,
                        GarageStructureColor);
                    AddQuad(draft, garageBBottom, garageBTop,
                        garagePadFarLeftTop, garagePadFarLeft,
                        GarageStructureColor);
                    AddQuad(draft, garageATop, garagePadNearLeftTop,
                        garagePadFarLeftTop, garageBTop,
                        GarageStructureColor);
                }
                else
                {
                    AddQuad(draft, garagePadNearLeft, garagePadNearLeftTop,
                        garagePadFarLeftTop, garagePadFarLeft,
                        GarageStructureColor);
                }

                if (garageOpeningFace == GarageTunnelOpeningFace.Right)
                {
                    AddQuad(draft, garagePadNearRightTop, garagePadNearRight,
                        garageABottom, garageATop,
                        GarageStructureColor);
                    AddQuad(draft, garageBTop, garageBBottom,
                        garagePadFarRight, garagePadFarRightTop,
                        GarageStructureColor);
                    AddQuad(draft, garagePadNearRightTop, garageATop,
                        garageBTop, garagePadFarRightTop,
                        GarageStructureColor);
                }
                else
                {
                    AddQuad(draft, garagePadNearRightTop, garagePadNearRight,
                        garagePadFarRight, garagePadFarRightTop,
                        GarageStructureColor);
                }
            }
            return BuildMesh("Underground Parking Garage Upward Full Slab Mouth Tunnel", draft);
        }

        private static float FlatSqrDistance(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return x * x + z * z;
        }

        private static void AddEntranceParkingLight(
            UndergroundParkingFacility facility,
            Vector3 entrancePosition,
            Quaternion rotation)
        {
            if (facility.EntranceBuildingId != 0)
            {
                BuildingManager buildingManager = BuildingManager.instance;
                if (buildingManager != null)
                {
                    ref Building building = ref buildingManager.m_buildings.m_buffer[facility.EntranceBuildingId];
                    if ((building.m_flags & Building.Flags.Created) != 0)
                    {
                        entrancePosition = building.m_position;
                        rotation = Quaternion.AngleAxis(
                            -building.m_angle * Mathf.Rad2Deg,
                            Vector3.up);
                    }
                }
            }

            GameObject lightObject = new GameObject("UPG P light " + facility.Id);
            lightObject.transform.parent = _root.transform;
            UndergroundParkingStandaloneVariant variant =
                UndergroundParkingStandaloneCatalog.FromFacility(facility);
            float parkingMarkOffset =
                UndergroundParkingBuildingPrefab.GetForecourtParkingMarkZOffset(variant);
            float pavingLift =
                UndergroundParkingBuildingPrefab.GetSurfacePavingLift(variant);
            Vector3 fixture = entrancePosition + rotation * new Vector3(
                0f,
                3.6f,
                -5.18f + parkingMarkOffset);
            Vector3 target = entrancePosition + rotation * new Vector3(
                0f,
                ForecourtLightTargetHeight + pavingLift,
                -2.75f + parkingMarkOffset);
            lightObject.transform.position = fixture;
            lightObject.transform.rotation = Quaternion.LookRotation(target - fixture, rotation * Vector3.up);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(0.94f, 0.97f, 1f);
            light.range = 12f;
            light.spotAngle = 82f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false;

            UndergroundParkingEntranceLightController controller =
                lightObject.AddComponent<UndergroundParkingEntranceLightController>();
            controller.Initialize(light, facility.Id);
            Visuals.Add(lightObject);
        }

        private static void EnsureRenderResources()
        {
            GetGarageStructureMesh();
            GetGarageStructureMaterial();
            GetParkedCarMesh();
            GetParkedMotorcycleMesh();
            GetParkedCarMaterial();
        }

        private static void RefreshParkedCars()
        {
            ParkedCarRenderItems.Clear();
            int count = UndergroundParkingOccupancyManager.CopyParkedCarVisuals(ParkedCarBuffer);
            Material material = GetParkedCarMaterial();
            for (int i = 0; i < count; i++)
            {
                UndergroundParkingCarVisual car = ParkedCarBuffer[i];
                Mesh mesh = GetNeutralParkingProxyMesh(car.Info);
                if (mesh == null)
                    continue;
                float scale = GetNeutralVehicleRenderScale(car.Info);
                Vector3 position = car.Position
                                   + Vector3.up
                                   * GetNeutralVehicleVerticalOffset();
                ParkedCarRenderItems.Add(new RenderItem(
                    car.FacilityId,
                    mesh,
                    Matrix4x4.TRS(position, car.Rotation, Vector3.one * scale),
                    material,
                    position,
                    3f));
            }
        }

        internal static float GetNeutralVehicleRenderScale(VehicleInfo info)
        {
            if (info == null)
                return 1f;
            Vector3 authoredSize = GetNeutralVehicleAuthoredSize(info);
            float maximumWidth = VisualSlotWidth - ParkedVehicleBayClearance * 2f;
            float maximumLength = VisualSlotLength - ParkedVehicleBayClearance * 2f;
            float scale = Mathf.Min(
                authoredSize.x > 0.05f ? maximumWidth / authoredSize.x : 1f,
                authoredSize.z > 0.05f ? maximumLength / authoredSize.z : 1f);
            // Passenger vehicles should remain clearly visible. Raw imported
            // mesh bounds can contain remote helper geometry and previously
            // drove this value almost to zero; the prefab's authored physical
            // size is the stable world-space contract.
            return Mathf.Clamp(scale, 0.6f, 1f);
        }

        private static float GetNeutralVehicleVerticalOffset()
        {
            // Vehicle prefab meshes already use their authored road-contact
            // pivot. Adding half the authored height lifted both the moving
            // and parked proxy above the garage slab.
            return ParkedVehicleSurfaceClearance;
        }

        private static Vector3 GetNeutralVehicleAuthoredSize(VehicleInfo info)
        {
            return info != null && info.m_generatedInfo != null
                ? info.m_generatedInfo.m_size
                : new Vector3(2f, 1.5f, 4f);
        }

        private static Mesh GetNeutralParkingProxyMesh(VehicleInfo info)
        {
            if (info == null || info.m_mesh == null)
                return null;
            Mesh mesh;
            if (NeutralVehicleMeshes.TryGetValue(info, out mesh) && mesh != null)
                return mesh;

            mesh = UnityEngine.Object.Instantiate(info.m_mesh);
            mesh.name = "UPG neutral " + (info.name ?? "vehicle");
            if (mesh.vertexCount > 0)
            {
                Color[] colors = new Color[mesh.vertexCount];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = Color.white;
                mesh.colors = colors;
            }
            NeutralVehicleMeshes[info] = mesh;
            return mesh;
        }

        private static bool IsMotorcycleVehicleInfo(VehicleInfo info)
        {
            string name = info == null ? string.Empty : info.name ?? string.Empty;
            string lower = name.ToLowerInvariant();
            return lower.Contains("motorcycle")
                   || lower.Contains("motorbike")
                   || lower.Contains("scooter")
                   || lower.Contains("moped")
                   || lower.Contains("personal electric transport");
        }

        internal static Material GetNeutralVehicleMaterial()
        {
            return GetParkedCarMaterial();
        }

        private static GameObject AddMeshChild(
            GameObject parent,
            string name,
            Mesh mesh,
            Material material,
            Vector3 localPosition,
            Vector3 localScale)
        {
            GameObject child = new GameObject(name);
            child.transform.parent = parent.transform;
            child.transform.localPosition = localPosition;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = localScale;

            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            // Retain the exact generated/prefab material and mesh bindings. The
            // per-renderer instance accessors can detach these runtime visuals
            // into silent copies during info-view transitions and rebuilds.
            renderer.sharedMaterial = material;
            renderer.enabled = true;
            return child;
        }

        private static Mesh GetPadMesh()
        {
            if (_padMesh == null)
                _padMesh = CreateFlatQuad("Underground Parking Garage Entrance Pad", EntrancePadWidth, EntrancePadLength);
            return _padMesh;
        }

        private static Mesh GetKioskMesh()
        {
            if (_kioskMesh == null)
                _kioskMesh = CreateBoxMesh("Underground Parking Garage Entrance Kiosk", EntranceKioskWidth, EntranceKioskHeight, EntranceKioskLength);
            return _kioskMesh;
        }

        private static Mesh GetSignMesh()
        {
            if (_signMesh == null)
                _signMesh = CreateVerticalQuad("Underground Parking Garage Sign", 3.1f, 3.1f);
            return _signMesh;
        }

        private static Mesh GetAttachedLampPoleMesh()
        {
            if (_attachedLampPoleMesh == null)
                _attachedLampPoleMesh = CreateBoxMesh(
                    "Underground Parking Garage Attached Lamp Pole",
                    0.14f,
                    3.2f,
                    0.14f);
            return _attachedLampPoleMesh;
        }

        private static Mesh GetAttachedLampHeadMesh()
        {
            if (_attachedLampHeadMesh == null)
                _attachedLampHeadMesh = CreateBoxMesh(
                    "Underground Parking Garage Attached Floodlight Housing",
                    0.48f,
                    0.28f,
                    0.4f);
            return _attachedLampHeadMesh;
        }

        private static Mesh GetAttachedLampLensMesh()
        {
            if (_attachedLampLensMesh == null)
                _attachedLampLensMesh = CreateBoxMesh(
                    "Underground Parking Garage Attached Floodlight Lens",
                    0.38f,
                    0.2f,
                    0.025f);
            return _attachedLampLensMesh;
        }

        private static Mesh CreateBuildingAttachedSurfaceMesh(
            float pavementWidth)
        {
            MeshDraft draft = new MeshDraft();
            const float halfWidth = BuildingAttachedSurfaceWidth * 0.5f;
            float halfDepth = pavementWidth * 0.5f;
            const int columns = 16;
            const int rows = 12;
            for (int z = 0; z <= rows; z++)
            {
                float v = z / (float)rows;
                float localZ = Mathf.Lerp(-halfDepth, halfDepth, v);
                for (int x = 0; x <= columns; x++)
                {
                    float u = x / (float)columns;
                    float localX = Mathf.Lerp(-halfWidth, halfWidth, u);
                    draft.Vertices.Add(new Vector3(
                        localX,
                        BuildingAttachedPortalSurfaceLift,
                        localZ));
                    draft.Uvs.Add(new Vector2(u, v));
                    draft.Colors.Add(Color.white);
                }
            }

            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int a = z * (columns + 1) + x;
                    int b = a + 1;
                    int d = (z + 1) * (columns + 1) + x;
                    int c = d + 1;
                    draft.Triangles.Add(a);
                    draft.Triangles.Add(c);
                    draft.Triangles.Add(b);
                    draft.Triangles.Add(a);
                    draft.Triangles.Add(d);
                    draft.Triangles.Add(c);
                }
            }
            return BuildMesh("Underground Parking Garage Road-Edge Surface", draft);
        }

        private static void ProfileMeshToDetailedRoadSurface(
            Mesh mesh,
            Vector3 center,
            Vector3 roadTangent,
            Vector3 inward,
            Vector3 roadNormal,
            float profileHalfDepth)
        {
            if (mesh == null)
                return;

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                vertex.y += ResolveDetailedRoadProfileOffset(
                    center,
                    roadTangent,
                    inward,
                    roadNormal,
                    vertex.x,
                    vertex.z,
                    profileHalfDepth);
                vertices[i] = vertex;
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static float ResolveDetailedRoadProfileOffset(
            Vector3 center,
            Vector3 roadTangent,
            Vector3 inward,
            Vector3 roadNormal,
            float localX,
            float localZ,
            float profileHalfDepth)
        {
            TerrainManager terrain = TerrainManager.instance;
            if (terrain == null)
                return 0f;

            float terrainCenterY = terrain.SampleDetailHeight(center);
            // A genuinely elevated or deeply sunken network owns its own deck;
            // do not stretch a ground apron several metres up to it. Ordinary
            // roadside cuts and embankments remain eligible and are contoured.
            if (Mathf.Abs(terrainCenterY - center.y)
                > BuildingAttachedRaisedRoadTerrainSeparation)
                return 0f;

            Vector3 planarSample = center
                                   + roadTangent * localX
                                   + inward * localZ;
            float terrainY = terrain.SampleDetailHeight(planarSample);

            float halfDepth = Mathf.Max(0.1f, profileHalfDepth);
            float across = Mathf.InverseLerp(
                -halfDepth,
                halfDepth,
                localZ);

            // The carriageway seam is the sole road-owned row. Every subsequent
            // row is a true detailed-terrain drape with its existing physical
            // surface clearance. Interpolating between the road plane and an
            // uphill terrain sample places every intermediate row below that
            // same terrain and leaves only a narrow ribbon visible. Subtracting
            // a hidden road-terrain datum is equally invalid because road and
            // building terrain can have unrelated discontinuities. The first
            // triangle strip provides the explicit join between the exact live
            // road seam and the independently sampled ground.
            if (across <= 0.0001f)
                return 0f;

            float verticalOffset = Mathf.Clamp(
                terrainY - planarSample.y,
                -BuildingAttachedMaximumTerrainTransition,
                BuildingAttachedMaximumTerrainTransition);
            return verticalOffset / Mathf.Max(0.25f, roadNormal.y);
        }

        private static Mesh CreateBuildingAttachedParkingMarkMesh(float pavementWidth)
        {
            MeshDraft draft = new MeshDraft();
            Vector2 blue = new Vector2(0.25f, 0.5f);
            Vector2 white = new Vector2(0.75f, 0.5f);
            const float markScale = 0.4f;
            float panelDepth = 4.8f * markScale;
            float panelCenterZ = pavementWidth * 0.5f - 0.12f - panelDepth * 0.5f;

            // Keep the compact kiosk-sized blue field but construct a purpose-made
            // centred P instead of scaling the kiosk's block-built glyph. Local +Z
            // is up for a viewer in the carriageway looking toward the outside edge.
            AddPaletteHorizontalQuad(
                draft, -2.3f * markScale, 2.3f * markScale,
                panelCenterZ - panelDepth * 0.5f,
                panelCenterZ + panelDepth * 0.5f,
                BuildingAttachedParkingFieldSurfaceLift, blue);
            UndergroundParkingMarkGeometry.AddCenteredParkingSignP(
                draft.Vertices,
                draft.Uvs,
                draft.Colors,
                draft.Triangles,
                0f,
                panelCenterZ,
                1f,
                1f,
                1f,
                BuildingAttachedParkingGlyphSurfaceLift,
                white);
            return BuildMesh("Underground Parking Garage Upright Kiosk P", draft);
        }

        private static Mesh CreateBuildingAttachedSideKerbMesh(float pavementWidth)
        {
            MeshDraft draft = new MeshDraft();
            const float tarmacHalfWidth = 3.05f;
            float halfDepth = pavementWidth * 0.5f;
            int stoneCount = Mathf.Max(
                1,
                Mathf.CeilToInt(pavementWidth / BuildingAttachedKerbTargetLength));
            float stoneLength = pavementWidth / stoneCount;
            const float jointHalfGap = 0.0025f;

            for (int side = 0; side < 2; side++)
            {
                float minX = side == 0
                    ? -tarmacHalfWidth
                    : tarmacHalfWidth - BuildingAttachedKerbWidth;
                float maxX = side == 0
                    ? -tarmacHalfWidth + BuildingAttachedKerbWidth
                    : tarmacHalfWidth;
                for (int stone = 0; stone < stoneCount; stone++)
                {
                    float minZ = -halfDepth + stone * stoneLength + jointHalfGap;
                    float maxZ = -halfDepth + (stone + 1) * stoneLength - jointHalfGap;
                    AddTexturedHorizontalQuad(
                        draft,
                        minX,
                        maxX,
                        minZ,
                        maxZ,
                        BuildingAttachedKerbSurfaceLift,
                        Vector2.zero,
                        Vector2.one);
                }
            }

            return BuildMesh(
                "Underground Parking Garage Viewer Left Right Flat Kerb Stones",
                draft);
        }

        private static Mesh GetGarageStructureMesh()
        {
            if (_garageStructureMesh == null)
                _garageStructureMesh = CreateMultiStoreyGarageStructureMesh(
                    UndergroundParkingGeometry.GarageWidth,
                    UndergroundParkingGeometry.GarageLength);
            return _garageStructureMesh;
        }

        private static Mesh GetGarageStructureMesh(UndergroundParkingFacility facility)
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt(
                Mathf.Max(UndergroundParkingGeometry.ParkingSlotWidth,
                    facility.GarageWidth - UndergroundParkingGeometry.ParkingSlotEdgePadding * 2f)
                / UndergroundParkingGeometry.ParkingSlotWidth));
            int rows = Mathf.Max(1, Mathf.FloorToInt(
                Mathf.Max(UndergroundParkingGeometry.ParkingSlotLength,
                    facility.GarageLength - UndergroundParkingGeometry.ParkingSlotEdgePadding * 2f)
                / UndergroundParkingGeometry.ParkingSlotLength));
            int variant = GetGarageVisualVariant(facility);
            long key = GetGarageVisualMeshKey(
                facility.GarageWidth,
                facility.GarageLength,
                columns,
                rows,
                variant);
            Mesh mesh;
            if (!GarageStructureMeshes.TryGetValue(key, out mesh) || mesh == null)
            {
                mesh = CreateMultiStoreyGarageStructureMesh(
                    facility.GarageWidth,
                    facility.GarageLength,
                    variant);
                GarageStructureMeshes[key] = mesh;
            }
            return mesh;
        }

        private static Mesh GetGarageTopAccessMesh(UndergroundParkingFacility facility)
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt(
                Mathf.Max(UndergroundParkingGeometry.ParkingSlotWidth,
                    facility.GarageWidth - UndergroundParkingGeometry.ParkingSlotEdgePadding * 2f)
                / UndergroundParkingGeometry.ParkingSlotWidth));
            int rows = Mathf.Max(1, Mathf.FloorToInt(
                Mathf.Max(UndergroundParkingGeometry.ParkingSlotLength,
                    facility.GarageLength - UndergroundParkingGeometry.ParkingSlotEdgePadding * 2f)
                / UndergroundParkingGeometry.ParkingSlotLength));
            int variant = GetGarageVisualVariant(facility);
            long key = GetGarageVisualMeshKey(
                facility.GarageWidth,
                facility.GarageLength,
                columns,
                rows,
                variant);
            Mesh mesh;
            if (!GarageTopAccessMeshes.TryGetValue(key, out mesh) || mesh == null)
            {
                mesh = CreateGarageTopAccessMesh(
                    facility.GarageWidth,
                    facility.GarageLength,
                    variant);
                GarageTopAccessMeshes[key] = mesh;
            }
            return mesh;
        }

        private static int GetGarageVisualVariant(UndergroundParkingFacility facility)
        {
            return Mathf.Clamp(facility.GarageDetailVariant, 0, 7);
        }

        private static long GetGarageVisualMeshKey(
            float garageWidth,
            float garageLength,
            int columns,
            int rows,
            int variant)
        {
            long widthDecimetres = (uint)Mathf.Clamp(Mathf.RoundToInt(garageWidth * 10f), 0, 65535);
            long lengthDecimetres = (uint)Mathf.Clamp(Mathf.RoundToInt(garageLength * 10f), 0, 65535);
            return (widthDecimetres << 48)
                   | (lengthDecimetres << 32)
                   | ((long)(columns & 255) << 24)
                   | ((long)(rows & 255) << 16)
                   | (uint)(variant & 255);
        }

        private static Mesh GetParkedCarMesh()
        {
            if (_parkedCarMesh != null)
                return _parkedCarMesh;

            VehicleInfo selected = FindDeterministicPassengerCarPrefab();
            if (selected != null && selected.m_mesh != null)
            {
                // Clone the vanilla silhouette so its paint/wheel vertex colours do
                // not leak into the single-colour x-ray overlay material.
                _parkedCarMesh = UnityEngine.Object.Instantiate(selected.m_mesh);
                _parkedCarMesh.name = "Underground Parking Garage " + selected.name + " Xray Mesh";
                Color[] neutralColors = new Color[_parkedCarMesh.vertexCount];
                for (int i = 0; i < neutralColors.Length; i++)
                    neutralColors[i] = Color.white;
                _parkedCarMesh.colors = neutralColors;
                UndergroundParkingLog.Advanced("UPG parked x-ray car model selected: " + selected.name);
                return _parkedCarMesh;
            }

            // Safe fallback only for unusually incomplete prefab environments.
            MeshDraft draft = new MeshDraft();
            Color grey = new Color(0.58f, 0.6f, 0.62f, 0.72f);
            AddBox(draft, -0.82f, 0.82f, -0.35f, 0.16f, -1.95f, 1.95f, grey);
            AddBox(draft, -0.65f, 0.65f, 0.16f, 0.5f, -0.72f, 0.84f, grey);
            _parkedCarMesh = BuildMesh("Underground Parking Garage Fallback Car", draft);
            return _parkedCarMesh;
        }

        private static VehicleInfo FindDeterministicPassengerCarPrefab()
        {
            VehicleInfo best = null;
            int bestScore = int.MaxValue;
            int count = PrefabCollection<VehicleInfo>.LoadedCount();
            for (int i = 0; i < count; i++)
            {
                VehicleInfo candidate = PrefabCollection<VehicleInfo>.GetLoaded((uint)i);
                if (candidate == null
                    || candidate.m_mesh == null
                    || candidate.m_material == null
                    || !(candidate.m_vehicleAI is PassengerCarAI))
                {
                    continue;
                }

                string name = candidate.name ?? string.Empty;
                string lower = name.ToLowerInvariant();
                if (lower.Contains("scooter")
                    || lower.Contains("motorcycle")
                    || lower.Contains("camper")
                    || lower.Contains("trailer"))
                {
                    continue;
                }

                int score = lower.Contains("sedan") ? 0
                    : lower.Contains("hatchback") || lower.Contains("family") ? 1
                    : lower.Contains("compact") ? 2
                    : lower.Contains("sports") ? 3
                    : 10;
                if (score > bestScore
                    || (score == bestScore
                        && best != null
                        && string.CompareOrdinal(name, best.name) >= 0))
                {
                    continue;
                }

                best = candidate;
                bestScore = score;
            }

            return best;
        }

        private static Mesh GetParkedMotorcycleMesh()
        {
            if (_parkedMotorcycleMesh != null)
                return _parkedMotorcycleMesh;

            VehicleInfo selected = FindDeterministicMotorcyclePrefab();
            if (selected != null && selected.m_mesh != null)
            {
                _parkedMotorcycleMesh = UnityEngine.Object.Instantiate(selected.m_mesh);
                _parkedMotorcycleMesh.name = "Underground Parking Garage " + selected.name + " Xray Mesh";
                Color[] neutralColors = new Color[_parkedMotorcycleMesh.vertexCount];
                for (int i = 0; i < neutralColors.Length; i++)
                    neutralColors[i] = Color.white;
                _parkedMotorcycleMesh.colors = neutralColors;
                UndergroundParkingLog.Advanced("UPG parked x-ray motorcycle model selected: " + selected.name);
                return _parkedMotorcycleMesh;
            }

            MeshDraft draft = new MeshDraft();
            Color grey = new Color(0.46f, 0.48f, 0.5f, 0.78f);
            AddBox(draft, -0.22f, 0.22f, -0.18f, 0.24f, -1.05f, 1.05f, grey);
            AddBox(draft, -0.34f, 0.34f, 0.18f, 0.62f, -0.15f, 0.48f, grey);
            _parkedMotorcycleMesh = BuildMesh("Underground Parking Garage Fallback Motorcycle", draft);
            return _parkedMotorcycleMesh;
        }

        private static VehicleInfo FindDeterministicMotorcyclePrefab()
        {
            VehicleInfo best = null;
            int count = PrefabCollection<VehicleInfo>.LoadedCount();
            for (int i = 0; i < count; i++)
            {
                VehicleInfo candidate = PrefabCollection<VehicleInfo>.GetLoaded((uint)i);
                if (candidate == null || candidate.m_mesh == null || !(candidate.m_vehicleAI is PassengerCarAI))
                    continue;

                string name = candidate.name ?? string.Empty;
                string lower = name.ToLowerInvariant();
                if (!lower.Contains("motorcycle")
                    && !lower.Contains("motorbike")
                    && !lower.Contains("scooter")
                    && !lower.Contains("moped"))
                    continue;

                if (best == null || string.CompareOrdinal(name, best.name) < 0)
                    best = candidate;
            }

            return best;
        }

        private static Mesh CreateFlatQuad(string name, float width, float length)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            float halfW = width * 0.5f;
            float halfL = length * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-halfW, 0f, -halfL),
                new Vector3(halfW, 0f, -halfL),
                new Vector3(halfW, 0f, halfL),
                new Vector3(-halfW, 0f, halfL)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateVerticalQuad(string name, float width, float height)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            float halfW = width * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-halfW, 0f, 0f),
                new Vector3(halfW, 0f, 0f),
                new Vector3(halfW, height, 0f),
                new Vector3(-halfW, height, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBoxMesh(string name, float width, float height, float length)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            float halfW = width * 0.5f;
            float halfL = length * 0.5f;
            float halfH = height * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-halfW, -halfH, -halfL),
                new Vector3(halfW, -halfH, -halfL),
                new Vector3(halfW, -halfH, halfL),
                new Vector3(-halfW, -halfH, halfL),
                new Vector3(-halfW, halfH, -halfL),
                new Vector3(halfW, halfH, -halfL),
                new Vector3(halfW, halfH, halfL),
                new Vector3(-halfW, halfH, halfL)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateMultiStoreyGarageStructureMesh(
            float garageWidth,
            float garageLength,
            int visualVariant = 0)
        {
            MeshDraft draft = new MeshDraft();
            const float floorTop = -0.39f;

            // Floor and ceiling slabs keep the garage readable as one underground
            // volume without returning to the previous featureless solid box.
            AddBeveledPrism(draft, 0.5f, 0.5f, GarageChamferRatio, -0.5f, floorTop, GarageSlabColor);
            AddBeveledPrism(draft, 0.5f, 0.5f, GarageChamferRatio, 0.44f, 0.5f, GarageSlabColor);

            // Perimeter wall bands and a restrained structural column grid.
            AddBeveledRing(
                draft,
                0.5f,
                0.5f,
                GarageChamferRatio,
                0.46f,
                0.45f,
                0.045f,
                floorTop,
                0.44f,
                GarageStructureColor);
            float[] columnXs = { -0.32f, 0f, 0.32f };
            float[] columnZs = { -0.34f, 0.34f };
            for (int x = 0; x < columnXs.Length; x++)
            {
                for (int z = 0; z < columnZs.Length; z++)
                {
                    AddBox(
                        draft,
                        columnXs[x] - 0.014f,
                        columnXs[x] + 0.014f,
                        floorTop,
                        0.44f,
                        columnZs[z] - 0.028f,
                        columnZs[z] + 0.028f,
                        GarageStructureColor);
                }
            }

            // Bay and aisle markings are facility-specific because their
            // orientation follows the live entrance. They are emitted by
            // CreateLaneLayoutMesh instead of this shared structure mesh.
            float usableWidth = garageWidth - VisualSlotEdgePadding * 2f;
            float usableLength = garageLength - VisualSlotEdgePadding * 2f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(usableWidth / VisualSlotWidth));
            int rows = 0;
            float firstCenterX = -usableWidth * 0.5f + VisualSlotWidth * 0.5f;
            float firstCenterZ = -usableLength * 0.5f + VisualSlotLength * 0.5f;
            float minGridX = firstCenterX - VisualSlotWidth * 0.5f;
            float maxGridX = firstCenterX + (columns - 1) * VisualSlotWidth + VisualSlotWidth * 0.5f;
            for (int row = 0; row < rows; row++)
            {
                float centerZ = firstCenterZ + row * VisualSlotLength;
                float minZ = centerZ - VisualSlotLength * 0.5f;
                float maxZ = centerZ + VisualSlotLength * 0.5f;
                for (int divider = 0; divider <= columns; divider++)
                {
                    float x = minGridX + divider * VisualSlotWidth;
                    AddBox(
                        draft,
                        (x - 0.08f) / garageWidth,
                        (x + 0.08f) / garageWidth,
                        floorTop + 0.013f,
                        floorTop + 0.024f,
                        minZ / garageLength,
                        maxZ / garageLength,
                        GarageMarkingColor);
                }

                AddBox(
                    draft,
                    minGridX / garageWidth,
                    maxGridX / garageWidth,
                    floorTop + 0.013f,
                    floorTop + 0.024f,
                    minZ / garageLength,
                    (minZ + 0.16f) / garageLength,
                    GarageMarkingColor);
                AddBox(
                    draft,
                    minGridX / garageWidth,
                    maxGridX / garageWidth,
                    floorTop + 0.013f,
                    floorTop + 0.024f,
                    (maxZ - 0.16f) / garageLength,
                    maxZ / garageLength,
                    GarageMarkingColor);
            }

            AddGarageSpiralStairs(
                draft,
                garageWidth,
                garageLength,
                visualVariant,
                floorTop + 0.03f,
                0.42f);
            AddGarageVentilationDucts(
                draft,
                garageWidth,
                garageLength,
                visualVariant);

            return BuildMesh("Underground Parking Garage Detailed Structure", draft);
        }

        private static Mesh CreateLaneLayoutMesh(UndergroundParkingFacility facility)
        {
            int requiredBays = UndergroundParkingGeometry.GetParkingSpaceCapacity(facility, 1);
            UndergroundParkingLaneLayout layout;
            if (!UndergroundParkingLaneLayout.TryCreate(
                    facility,
                    requiredBays,
                    out layout))
            {
                UndergroundParkingLog.Warning(
                    "UPG aisle layout retained legacy geometry because complete capacity could not be represented: facility="
                    + facility.Id
                    + " requiredPerFloor="
                    + requiredBays);
                return null;
            }
            if (layout.UsesCompactAttachedLayout)
            {
                UndergroundParkingLog.Advanced(
                    "UPG compact attached aisle layout built: facility="
                    + facility.Id
                    + " requiredPerFloor="
                    + requiredBays
                    + " physicalBays="
                    + layout.PaintedBays.Count
                    + " bayPitch="
                    + layout.BayPitch.ToString("0.00")
                    + " entranceCrossAisle="
                    + (layout.CrossAisleSpan > 0.01f)
                    + " directSingleAisleIngress="
                    + (layout.CrossAisleSpan <= 0.01f)
                    + " automatedTunnel="
                    + layout.SupportsAutomatedTunnel);
            }

            MeshDraft draft = new MeshDraft();
            const float lineHalfWidth = 0.08f;
            float markingY = -0.39f * UndergroundParkingGeometry.GarageFloorHeight + 0.16f;
            float aisleMinY = markingY;
            float aisleMaxY = markingY + 0.012f;
            float minY = markingY + 0.025f;
            float maxY = markingY + 0.04f;
            float halfGarageWidth = Mathf.Max(1f, facility.GarageWidth * 0.5f - VisualSlotEdgePadding);
            float halfGarageLength = Mathf.Max(1f, facility.GarageLength * 0.5f - VisualSlotEdgePadding);

            if (layout.CrossAisleSpan > 0.01f && layout.AislesAlongForward)
            {
                AddBox(
                    draft,
                    -halfGarageWidth,
                    halfGarageWidth,
                    aisleMinY,
                    aisleMaxY,
                    layout.CrossAisleCoordinate - layout.CrossAisleSpan * 0.5f,
                    layout.CrossAisleCoordinate + layout.CrossAisleSpan * 0.5f,
                    GarageAisleColor);
            }
            else if (layout.CrossAisleSpan > 0.01f)
            {
                AddBox(
                    draft,
                    layout.CrossAisleCoordinate - layout.CrossAisleSpan * 0.5f,
                    layout.CrossAisleCoordinate + layout.CrossAisleSpan * 0.5f,
                    aisleMinY,
                    aisleMaxY,
                    -halfGarageLength,
                    halfGarageLength,
                    GarageAisleColor);
            }

            HashSet<int> renderedAisles = new HashSet<int>();
            for (int i = 0; i < layout.PaintedBays.Count; i++)
            {
                UndergroundParkingBay bay = layout.PaintedBays[i];
                float aisleCoordinate = layout.AislesAlongForward
                    ? bay.LocalLanePosition.x
                    : bay.LocalLanePosition.z;
                int aisleKey = Mathf.RoundToInt(aisleCoordinate * 100f);
                if (renderedAisles.Add(aisleKey))
                {
                    if (layout.AislesAlongForward)
                    {
                        AddBox(
                            draft,
                            aisleCoordinate - UndergroundParkingLaneLayout.AisleWidth * 0.5f,
                            aisleCoordinate + UndergroundParkingLaneLayout.AisleWidth * 0.5f,
                            aisleMinY,
                            aisleMaxY,
                            -halfGarageLength,
                            halfGarageLength,
                            GarageAisleColor);
                    }
                    else
                    {
                        AddBox(
                            draft,
                            -halfGarageWidth,
                            halfGarageWidth,
                            aisleMinY,
                            aisleMaxY,
                            aisleCoordinate - UndergroundParkingLaneLayout.AisleWidth * 0.5f,
                            aisleCoordinate + UndergroundParkingLaneLayout.AisleWidth * 0.5f,
                            GarageAisleColor);
                    }
                }

                float halfWidth = layout.BayPitch * 0.5f;
                float halfDepth = UndergroundParkingLaneLayout.BayDepth * 0.5f;
                float minX = bay.AisleAlongForward
                    ? bay.LocalPosition.x - halfDepth
                    : bay.LocalPosition.x - halfWidth;
                float maxX = bay.AisleAlongForward
                    ? bay.LocalPosition.x + halfDepth
                    : bay.LocalPosition.x + halfWidth;
                float minZ = bay.AisleAlongForward
                    ? bay.LocalPosition.z - halfWidth
                    : bay.LocalPosition.z - halfDepth;
                float maxZ = bay.AisleAlongForward
                    ? bay.LocalPosition.z + halfWidth
                    : bay.LocalPosition.z + halfDepth;
                AddBox(draft, minX, minX + lineHalfWidth * 2f, minY, maxY, minZ, maxZ, GarageMarkingColor);
                AddBox(draft, maxX - lineHalfWidth * 2f, maxX, minY, maxY, minZ, maxZ, GarageMarkingColor);
                AddBox(draft, minX, maxX, minY, maxY, minZ, minZ + lineHalfWidth * 2f, GarageMarkingColor);
                AddBox(draft, minX, maxX, minY, maxY, maxZ - lineHalfWidth * 2f, maxZ, GarageMarkingColor);
            }

            return BuildMesh("Underground Parking Garage Lane And Bay Layout", draft);
        }

        private static Mesh CreateGarageTopAccessMesh(
            float garageWidth,
            float garageLength,
            int visualVariant)
        {
            MeshDraft draft = new MeshDraft();
            float surfaceExtension = UndergroundParkingGeometry.GarageTopDepth
                                     / UndergroundParkingGeometry.GarageFloorHeight;
            AddGarageSpiralStairs(
                draft,
                garageWidth,
                garageLength,
                visualVariant,
                0.44f,
                0.44f + surfaceExtension);
            return BuildMesh("Underground Parking Garage Surface Access Stairs", draft);
        }

        private static Mesh CreateEntryAlignedGarageRampMesh(
            UndergroundParkingFacility facility,
            Vector3 garageMouthCenter,
            Vector3 garageWallNormal,
            out bool switchback,
            out float maximumGrade)
        {
            switchback = false;
            maximumGrade = 0f;
            float garageWidth = Mathf.Max(8f, facility.GarageWidth);
            float garageLength = Mathf.Max(8f, facility.GarageLength);
            Quaternion inverseRotation = Quaternion.Inverse(
                Quaternion.LookRotation(facility.GarageForward, Vector3.up));
            Vector3 localMouth = inverseRotation * (garageMouthCenter - facility.GarageCenter);
            Vector3 localOutward = inverseRotation * garageWallNormal;
            bool alongX = Mathf.Abs(localOutward.x) >= Mathf.Abs(localOutward.z);
            float alongDimension = alongX ? garageWidth : garageLength;
            float crossDimension = alongX ? garageLength : garageWidth;
            float outwardSign = alongX
                ? Mathf.Sign(localOutward.x)
                : Mathf.Sign(localOutward.z);
            if (Mathf.Abs(outwardSign) < 0.5f)
                outwardSign = 1f;

            const float floorTop = -0.39f;
            const float roofDeck = 0.44f;
            const float rampWidth = 3.2f;
            const float edgeClearance = 1.35f;
            const float targetMaximumGrade = 0.18f;
            float verticalDrop = (roofDeck - floorTop)
                                 * UndergroundParkingGeometry.GarageFloorHeight;
            float availableRun = Mathf.Max(
                3f,
                alongDimension - edgeClearance * 2f);
            float requiredRun = verticalDrop / targetMaximumGrade;
            float highAlong = outwardSign * (alongDimension * 0.5f - edgeClearance);
            float inwardSign = -outwardSign;
            float crossOffset = alongX ? localMouth.z : localMouth.x;
            float crossLimit = Mathf.Max(0f, crossDimension * 0.5f - rampWidth * 0.5f - 0.8f);
            crossOffset = Mathf.Clamp(crossOffset, -crossLimit, crossLimit);
            MeshDraft draft = new MeshDraft();

            if (availableRun >= requiredRun)
            {
                float lowAlong = highAlong + inwardSign * requiredRun;
                AddSlopedGarageDeck(
                    draft,
                    garageWidth,
                    garageLength,
                    alongX,
                    crossOffset,
                    highAlong,
                    lowAlong,
                    rampWidth,
                    roofDeck,
                    floorTop,
                    GarageRampColor);
                maximumGrade = verticalDrop / requiredRun;
            }
            else
            {
                switchback = true;
                float adjacentSpacing = rampWidth + 0.65f;
                float positiveRoom = crossLimit - crossOffset;
                float negativeRoom = crossOffset + crossLimit;
                float secondCrossOffset = positiveRoom >= adjacentSpacing
                    ? crossOffset + adjacentSpacing
                    : crossOffset - adjacentSpacing;
                secondCrossOffset = Mathf.Clamp(secondCrossOffset, -crossLimit, crossLimit);
                float farAlong = highAlong + inwardSign * availableRun;
                float middleY = (roofDeck + floorTop) * 0.5f;
                AddSlopedGarageDeck(
                    draft,
                    garageWidth,
                    garageLength,
                    alongX,
                    crossOffset,
                    highAlong,
                    farAlong,
                    rampWidth,
                    roofDeck,
                    middleY,
                    GarageRampColor);
                AddSlopedGarageDeck(
                    draft,
                    garageWidth,
                    garageLength,
                    alongX,
                    secondCrossOffset,
                    farAlong,
                    highAlong,
                    rampWidth,
                    middleY,
                    floorTop,
                    GarageRampColor);
                AddGarageRampLanding(
                    draft,
                    garageWidth,
                    garageLength,
                    alongX,
                    farAlong,
                    crossOffset,
                    secondCrossOffset,
                    rampWidth,
                    middleY);
                maximumGrade = (verticalDrop * 0.5f) / availableRun;
            }

            return BuildMesh("Underground Parking Garage Entry Aligned Vehicle Ramp", draft);
        }

        private static void AddGarageRampLanding(
            MeshDraft draft,
            float garageWidth,
            float garageLength,
            bool alongX,
            float along,
            float firstCross,
            float secondCross,
            float rampWidth,
            float y)
        {
            const float landingDepth = 2.2f;
            float minAlong = along - landingDepth * 0.5f;
            float maxAlong = along + landingDepth * 0.5f;
            float minCross = Mathf.Min(firstCross, secondCross) - rampWidth * 0.5f;
            float maxCross = Mathf.Max(firstCross, secondCross) + rampWidth * 0.5f;
            if (alongX)
            {
                AddBox(
                    draft,
                    minAlong / garageWidth,
                    maxAlong / garageWidth,
                    y - 0.015f,
                    y + 0.015f,
                    minCross / garageLength,
                    maxCross / garageLength,
                    GarageRampColor);
            }
            else
            {
                AddBox(
                    draft,
                    minCross / garageWidth,
                    maxCross / garageWidth,
                    y - 0.015f,
                    y + 0.015f,
                    minAlong / garageLength,
                    maxAlong / garageLength,
                    GarageRampColor);
            }
        }

        private static void AddSlopedGarageDeck(
            MeshDraft draft,
            float garageWidth,
            float garageLength,
            bool alongX,
            float crossOffset,
            float startAlong,
            float endAlong,
            float deckWidth,
            float startY,
            float endY,
            Color color)
        {
            const float thickness = 0.025f;
            const float guardHeight = 0.095f;
            float halfWidth = deckWidth * 0.5f;
            Vector3 startLeft = GarageDetailPoint(
                alongX ? startAlong : crossOffset - halfWidth,
                startY,
                alongX ? crossOffset - halfWidth : startAlong,
                garageWidth,
                garageLength);
            Vector3 startRight = GarageDetailPoint(
                alongX ? startAlong : crossOffset + halfWidth,
                startY,
                alongX ? crossOffset + halfWidth : startAlong,
                garageWidth,
                garageLength);
            Vector3 endLeft = GarageDetailPoint(
                alongX ? endAlong : crossOffset - halfWidth,
                endY,
                alongX ? crossOffset - halfWidth : endAlong,
                garageWidth,
                garageLength);
            Vector3 endRight = GarageDetailPoint(
                alongX ? endAlong : crossOffset + halfWidth,
                endY,
                alongX ? crossOffset + halfWidth : endAlong,
                garageWidth,
                garageLength);
            Vector3 down = Vector3.down * thickness;
            AddQuad(draft, startLeft, startRight, endRight, endLeft, color);
            AddQuad(draft, startRight + down, startLeft + down, endLeft + down, endRight + down, color);
            AddQuad(draft, startLeft + down, startLeft, endLeft, endLeft + down, color);
            AddQuad(draft, startRight, startRight + down, endRight + down, endRight, color);
            AddQuad(draft, startLeft + Vector3.up * guardHeight,
                startLeft, endLeft, endLeft + Vector3.up * guardHeight,
                GarageCirculationColor);
            AddQuad(draft, startRight, startRight + Vector3.up * guardHeight,
                endRight + Vector3.up * guardHeight, endRight,
                GarageCirculationColor);

            for (int post = 0; post <= 5; post++)
            {
                float t = post / 5f;
                float along = Mathf.Lerp(startAlong, endAlong, t);
                float y = Mathf.Lerp(startY, endY, t);
                AddGarageBoxMetres(
                    draft,
                    alongX ? along - 0.045f : crossOffset - halfWidth - 0.045f,
                    alongX ? along + 0.045f : crossOffset - halfWidth + 0.045f,
                    y,
                    y + guardHeight,
                    alongX ? crossOffset - halfWidth - 0.045f : along - 0.045f,
                    alongX ? crossOffset - halfWidth + 0.045f : along + 0.045f,
                    garageWidth,
                    garageLength,
                    GarageCirculationColor);
                AddGarageBoxMetres(
                    draft,
                    alongX ? along - 0.045f : crossOffset + halfWidth - 0.045f,
                    alongX ? along + 0.045f : crossOffset + halfWidth + 0.045f,
                    y,
                    y + guardHeight,
                    alongX ? crossOffset + halfWidth - 0.045f : along - 0.045f,
                    alongX ? crossOffset + halfWidth + 0.045f : along + 0.045f,
                    garageWidth,
                    garageLength,
                    GarageCirculationColor);
            }
        }

        private static void AddGarageSpiralStairs(
            MeshDraft draft,
            float garageWidth,
            float garageLength,
            int visualVariant,
            float startY,
            float endY)
        {
            float radius = Mathf.Clamp(
                Mathf.Min(garageWidth, garageLength) * 0.052f,
                0.65f,
                1.25f);
            float chamferClearance = Mathf.Max(0.55f,
                Mathf.Max(garageWidth, garageLength) * GarageChamferRatio);
            float inset = radius + chamferClearance;
            float signX = (visualVariant & 1) == 0 ? -1f : 1f;
            float signZ = (visualVariant & 4) == 0 ? -1f : 1f;
            Vector2 first = new Vector2(
                signX * Mathf.Max(0f, garageWidth * 0.5f - inset),
                signZ * Mathf.Max(0f, garageLength * 0.5f - inset));
            Vector2 second = -first;
            float baseAngle = (visualVariant & 3) * Mathf.PI * 0.5f;
            AddGarageSpiralStair(
                draft, first.x, first.y, radius, startY, endY,
                baseAngle, garageWidth, garageLength);
            AddGarageSpiralStair(
                draft, second.x, second.y, radius, startY, endY,
                baseAngle + Mathf.PI, garageWidth, garageLength);
        }

        private static void AddGarageSpiralStair(
            MeshDraft draft,
            float centerX,
            float centerZ,
            float radius,
            float startY,
            float endY,
            float startAngle,
            float garageWidth,
            float garageLength)
        {
            float heightMetres = Mathf.Abs(endY - startY)
                                 * UndergroundParkingGeometry.GarageFloorHeight;
            int steps = Mathf.Clamp(Mathf.CeilToInt(heightMetres / 0.28f), 8, 40);
            float turns = Mathf.Max(0.8f, heightMetres / 3.2f);
            AddVerticalGarageCylinder(
                draft, centerX, centerZ, 0.10f, startY, endY,
                8, garageWidth, garageLength, GarageCirculationColor);
            for (int step = 0; step < steps; step++)
            {
                float t = steps <= 1 ? 0f : step / (float)(steps - 1);
                float angle = startAngle + t * turns * Mathf.PI * 2f;
                float treadCenterX = centerX + Mathf.Cos(angle) * radius * 0.56f;
                float treadCenterZ = centerZ + Mathf.Sin(angle) * radius * 0.56f;
                float y = Mathf.Lerp(startY, endY, t);
                AddOrientedGarageBox(
                    draft,
                    treadCenterX,
                    treadCenterZ,
                    angle,
                    radius * 0.58f,
                    Mathf.Max(0.22f, radius * 0.24f),
                    y,
                    y + 0.016f,
                    garageWidth,
                    garageLength,
                    GarageCirculationColor);
            }

            for (int post = 0; post < 8; post++)
            {
                float angle = startAngle + post * Mathf.PI * 0.25f;
                float x = centerX + Mathf.Cos(angle) * radius;
                float z = centerZ + Mathf.Sin(angle) * radius;
                AddVerticalGarageCylinder(
                    draft, x, z, 0.035f, startY, endY,
                    6, garageWidth, garageLength, GarageCirculationColor);
            }
        }

        private static void AddGarageVentilationDucts(
            MeshDraft draft,
            float garageWidth,
            float garageLength,
            int visualVariant)
        {
            bool alongX = garageWidth >= garageLength;
            float longDimension = alongX ? garageWidth : garageLength;
            float shortDimension = alongX ? garageLength : garageWidth;
            float offsetSign = (visualVariant & 1) == 0 ? 1f : -1f;
            float mainOffset = offsetSign * shortDimension * 0.22f;
            float mainHalfLength = Mathf.Max(1.5f, longDimension * 0.38f);
            const float ductHalfWidth = 0.42f;
            const float ductMinY = 0.315f;
            const float ductMaxY = 0.395f;
            if (alongX)
            {
                AddGarageBoxMetres(
                    draft, -mainHalfLength, mainHalfLength, ductMinY, ductMaxY,
                    mainOffset - ductHalfWidth, mainOffset + ductHalfWidth,
                    garageWidth, garageLength, GarageDuctColor);
            }
            else
            {
                AddGarageBoxMetres(
                    draft, mainOffset - ductHalfWidth, mainOffset + ductHalfWidth,
                    ductMinY, ductMaxY, -mainHalfLength, mainHalfLength,
                    garageWidth, garageLength, GarageDuctColor);
            }

            int branches = Mathf.Clamp(Mathf.FloorToInt(longDimension / 11f), 2, 5);
            float branchTarget = -offsetSign * shortDimension * 0.28f;
            for (int branch = 0; branch < branches; branch++)
            {
                float along = Mathf.Lerp(
                    -mainHalfLength * 0.75f,
                    mainHalfLength * 0.75f,
                    branches == 1 ? 0.5f : branch / (float)(branches - 1));
                if (alongX)
                {
                    AddGarageBoxMetres(
                        draft, along - 0.30f, along + 0.30f, ductMinY, ductMaxY,
                        Mathf.Min(mainOffset, branchTarget), Mathf.Max(mainOffset, branchTarget),
                        garageWidth, garageLength, GarageDuctColor);
                    AddGarageBoxMetres(
                        draft, along - 0.36f, along + 0.36f, 0.19f, ductMaxY,
                        branchTarget - 0.36f, branchTarget + 0.36f,
                        garageWidth, garageLength, GarageDuctColor);
                }
                else
                {
                    AddGarageBoxMetres(
                        draft, Mathf.Min(mainOffset, branchTarget), Mathf.Max(mainOffset, branchTarget),
                        ductMinY, ductMaxY, along - 0.30f, along + 0.30f,
                        garageWidth, garageLength, GarageDuctColor);
                    AddGarageBoxMetres(
                        draft, branchTarget - 0.36f, branchTarget + 0.36f,
                        0.19f, ductMaxY, along - 0.36f, along + 0.36f,
                        garageWidth, garageLength, GarageDuctColor);
                }
            }
        }

        private static Vector3 GarageDetailPoint(
            float xMetres,
            float y,
            float zMetres,
            float garageWidth,
            float garageLength)
        {
            return new Vector3(
                xMetres / Mathf.Max(0.1f, garageWidth),
                y,
                zMetres / Mathf.Max(0.1f, garageLength));
        }

        private static void AddGarageBoxMetres(
            MeshDraft draft,
            float minX,
            float maxX,
            float minY,
            float maxY,
            float minZ,
            float maxZ,
            float garageWidth,
            float garageLength,
            Color color)
        {
            AddBox(
                draft,
                minX / garageWidth,
                maxX / garageWidth,
                minY,
                maxY,
                minZ / garageLength,
                maxZ / garageLength,
                color);
        }

        private static void AddOrientedGarageBox(
            MeshDraft draft,
            float centerX,
            float centerZ,
            float angle,
            float halfWidth,
            float halfLength,
            float minY,
            float maxY,
            float garageWidth,
            float garageLength,
            Color color)
        {
            Vector2 right = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 forward = new Vector2(-right.y, right.x);
            Vector2 center = new Vector2(centerX, centerZ);
            Vector2 a = center - right * halfWidth - forward * halfLength;
            Vector2 b = center + right * halfWidth - forward * halfLength;
            Vector2 c = center + right * halfWidth + forward * halfLength;
            Vector2 d = center - right * halfWidth + forward * halfLength;
            Vector3 ab = GarageDetailPoint(a.x, minY, a.y, garageWidth, garageLength);
            Vector3 bb = GarageDetailPoint(b.x, minY, b.y, garageWidth, garageLength);
            Vector3 cb = GarageDetailPoint(c.x, minY, c.y, garageWidth, garageLength);
            Vector3 db = GarageDetailPoint(d.x, minY, d.y, garageWidth, garageLength);
            Vector3 at = GarageDetailPoint(a.x, maxY, a.y, garageWidth, garageLength);
            Vector3 bt = GarageDetailPoint(b.x, maxY, b.y, garageWidth, garageLength);
            Vector3 ct = GarageDetailPoint(c.x, maxY, c.y, garageWidth, garageLength);
            Vector3 dt = GarageDetailPoint(d.x, maxY, d.y, garageWidth, garageLength);
            AddQuad(draft, ab, bb, bt, at, color);
            AddQuad(draft, bb, cb, ct, bt, color);
            AddQuad(draft, cb, db, dt, ct, color);
            AddQuad(draft, db, ab, at, dt, color);
            AddQuad(draft, at, bt, ct, dt, color);
            AddQuad(draft, db, cb, bb, ab, color);
        }

        private static void AddVerticalGarageCylinder(
            MeshDraft draft,
            float centerX,
            float centerZ,
            float radius,
            float minY,
            float maxY,
            int segments,
            float garageWidth,
            float garageLength,
            Color color)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                float angleA = segment * Mathf.PI * 2f / segments;
                float angleB = (segment + 1) * Mathf.PI * 2f / segments;
                Vector3 aBottom = GarageDetailPoint(
                    centerX + Mathf.Cos(angleA) * radius,
                    minY,
                    centerZ + Mathf.Sin(angleA) * radius,
                    garageWidth,
                    garageLength);
                Vector3 bBottom = GarageDetailPoint(
                    centerX + Mathf.Cos(angleB) * radius,
                    minY,
                    centerZ + Mathf.Sin(angleB) * radius,
                    garageWidth,
                    garageLength);
                Vector3 aTop = GarageDetailPoint(
                    centerX + Mathf.Cos(angleA) * radius,
                    maxY,
                    centerZ + Mathf.Sin(angleA) * radius,
                    garageWidth,
                    garageLength);
                Vector3 bTop = GarageDetailPoint(
                    centerX + Mathf.Cos(angleB) * radius,
                    maxY,
                    centerZ + Mathf.Sin(angleB) * radius,
                    garageWidth,
                    garageLength);
                AddQuad(draft, aBottom, bBottom, bTop, aTop, color);
            }
        }

        private static Mesh BuildMesh(string name, MeshDraft draft)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.vertices = draft.Vertices.ToArray();
            mesh.uv = draft.Uvs.ToArray();
            mesh.triangles = draft.Triangles.ToArray();
            mesh.colors = draft.Colors.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddBox(
            MeshDraft draft,
            float minX,
            float maxX,
            float minY,
            float maxY,
            float minZ,
            float maxZ,
            Color color)
        {
            Vector3 p000 = new Vector3(minX, minY, minZ);
            Vector3 p100 = new Vector3(maxX, minY, minZ);
            Vector3 p110 = new Vector3(maxX, maxY, minZ);
            Vector3 p010 = new Vector3(minX, maxY, minZ);
            Vector3 p001 = new Vector3(minX, minY, maxZ);
            Vector3 p101 = new Vector3(maxX, minY, maxZ);
            Vector3 p111 = new Vector3(maxX, maxY, maxZ);
            Vector3 p011 = new Vector3(minX, maxY, maxZ);

            AddQuad(draft, p000, p100, p110, p010, color);
            AddQuad(draft, p101, p001, p011, p111, color);
            AddQuad(draft, p001, p000, p010, p011, color);
            AddQuad(draft, p100, p101, p111, p110, color);
            AddQuad(draft, p010, p110, p111, p011, color);
            AddQuad(draft, p001, p101, p100, p000, color);
        }

        private static void AddHorizontalQuad(
            MeshDraft draft,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float y,
            Color color)
        {
            AddQuad(
                draft,
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ),
                color);
        }

        private static void AddPaletteHorizontalQuad(
            MeshDraft draft,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float y,
            Vector2 uv)
        {
            AddPaletteQuad(
                draft,
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ),
                uv);
        }

        private static void AddTexturedHorizontalQuad(
            MeshDraft draft,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float y,
            Vector2 minUv,
            Vector2 maxUv)
        {
            AddTexturedQuad(
                draft,
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ),
                new Vector2(minUv.x, minUv.y),
                new Vector2(maxUv.x, minUv.y),
                new Vector2(maxUv.x, maxUv.y),
                new Vector2(minUv.x, maxUv.y));
        }

        private static void AddMappedKioskMarkRect(
            MeshDraft draft,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float panelCenterZ,
            float scaleX,
            float scaleZ,
            Vector2 uv)
        {
            float mappedMinX = -maxX * scaleX;
            float mappedMaxX = -minX * scaleX;
            float mappedMinZ = MapKioskMarkZ(maxZ, panelCenterZ, scaleZ);
            float mappedMaxZ = MapKioskMarkZ(minZ, panelCenterZ, scaleZ);
            AddPaletteHorizontalQuad(
                draft, mappedMinX, mappedMaxX, mappedMinZ, mappedMaxZ, 0.07f, uv);
        }

        private static void AddMappedKioskMarkQuad(
            MeshDraft draft,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            float panelCenterZ,
            float scaleX,
            float scaleZ,
            Vector2 uv)
        {
            AddPaletteQuad(
                draft,
                MapKioskMarkPoint(a, panelCenterZ, scaleX, scaleZ),
                MapKioskMarkPoint(b, panelCenterZ, scaleX, scaleZ),
                MapKioskMarkPoint(c, panelCenterZ, scaleX, scaleZ),
                MapKioskMarkPoint(d, panelCenterZ, scaleX, scaleZ),
                uv);
        }

        private static Vector3 MapKioskMarkPoint(
            Vector2 point,
            float panelCenterZ,
            float scaleX,
            float scaleZ)
        {
            return new Vector3(
                -point.x * scaleX,
                0.07f,
                MapKioskMarkZ(point.y, panelCenterZ, scaleZ));
        }

        private static float MapKioskMarkZ(float kioskZ, float panelCenterZ, float scaleZ)
        {
            const float KioskPanelCenterZ = -0.85f;
            return panelCenterZ - (kioskZ - KioskPanelCenterZ) * scaleZ;
        }

        private static void AddBeveledPrism(
            MeshDraft draft,
            float halfWidth,
            float halfLength,
            float bevel,
            float minY,
            float maxY,
            Color color)
        {
            Vector3[] bottom = CreateRoundedLoop(halfWidth, halfLength, bevel, minY);
            Vector3[] top = CreateRoundedLoop(halfWidth, halfLength, bevel, maxY);
            for (int i = 1; i < bottom.Length - 1; i++)
            {
                AddTriangle(draft, bottom[0], bottom[i + 1], bottom[i], color);
                AddTriangle(draft, top[0], top[i], top[i + 1], color);
            }

            for (int i = 0; i < bottom.Length; i++)
            {
                int next = (i + 1) % bottom.Length;
                AddQuad(draft, bottom[i], bottom[next], top[next], top[i], color);
            }
        }

        private static void AddBeveledRing(
            MeshDraft draft,
            float outerHalfWidth,
            float outerHalfLength,
            float outerBevel,
            float innerHalfWidth,
            float innerHalfLength,
            float innerBevel,
            float minY,
            float maxY,
            Color color)
        {
            Vector3[] outerBottom = CreateRoundedLoop(outerHalfWidth, outerHalfLength, outerBevel, minY);
            Vector3[] outerTop = CreateRoundedLoop(outerHalfWidth, outerHalfLength, outerBevel, maxY);
            Vector3[] innerBottom = CreateRoundedLoop(innerHalfWidth, innerHalfLength, innerBevel, minY);
            Vector3[] innerTop = CreateRoundedLoop(innerHalfWidth, innerHalfLength, innerBevel, maxY);
            for (int i = 0; i < outerBottom.Length; i++)
            {
                int next = (i + 1) % outerBottom.Length;
                AddQuad(draft, outerBottom[i], outerBottom[next], outerTop[next], outerTop[i], color);
                AddQuad(draft, innerBottom[next], innerBottom[i], innerTop[i], innerTop[next], color);
                AddQuad(draft, outerTop[i], outerTop[next], innerTop[next], innerTop[i], color);
                AddQuad(draft, outerBottom[next], outerBottom[i], innerBottom[i], innerBottom[next], color);
            }
        }

        private static Vector3[] CreateRoundedLoop(
            float halfWidth,
            float halfLength,
            float radius,
            float y)
        {
            radius = Mathf.Clamp(
                radius,
                0.001f,
                Mathf.Min(halfWidth, halfLength) - 0.001f);
            Vector3[] loop = new Vector3[GarageCornerArcSegments * 4];
            int index = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                float centerX = corner == 0 || corner == 1
                    ? halfWidth - radius
                    : -halfWidth + radius;
                float centerZ = corner == 0 || corner == 3
                    ? -halfLength + radius
                    : halfLength - radius;
                float startAngle = -90f + corner * 90f;
                for (int segment = 0; segment < GarageCornerArcSegments; segment++)
                {
                    float angle = (startAngle
                                   + segment * 90f / (GarageCornerArcSegments - 1))
                                  * Mathf.Deg2Rad;
                    loop[index++] = new Vector3(
                        centerX + Mathf.Cos(angle) * radius,
                        y,
                        centerZ + Mathf.Sin(angle) * radius);
                }
            }
            return loop;
        }

        private static void AddTriangle(MeshDraft draft, Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            int start = draft.Vertices.Count;
            draft.Vertices.Add(a);
            draft.Vertices.Add(b);
            draft.Vertices.Add(c);
            draft.Uvs.Add(Vector2.zero);
            draft.Uvs.Add(Vector2.right);
            draft.Uvs.Add(Vector2.up);
            draft.Colors.Add(color);
            draft.Colors.Add(color);
            draft.Colors.Add(color);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 1);
            draft.Triangles.Add(start + 2);
        }

        private static void AddQuad(MeshDraft draft, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int start = draft.Vertices.Count;
            draft.Vertices.Add(a);
            draft.Vertices.Add(b);
            draft.Vertices.Add(c);
            draft.Vertices.Add(d);
            draft.Uvs.Add(new Vector2(0f, 0f));
            draft.Uvs.Add(new Vector2(1f, 0f));
            draft.Uvs.Add(new Vector2(1f, 1f));
            draft.Uvs.Add(new Vector2(0f, 1f));
            draft.Colors.Add(color);
            draft.Colors.Add(color);
            draft.Colors.Add(color);
            draft.Colors.Add(color);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 2);
            draft.Triangles.Add(start + 1);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 3);
            draft.Triangles.Add(start + 2);
        }

        private static void AddPaletteQuad(
            MeshDraft draft,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector2 uv)
        {
            int start = draft.Vertices.Count;
            draft.Vertices.Add(a);
            draft.Vertices.Add(b);
            draft.Vertices.Add(c);
            draft.Vertices.Add(d);
            draft.Uvs.Add(uv);
            draft.Uvs.Add(uv);
            draft.Uvs.Add(uv);
            draft.Uvs.Add(uv);
            Color color = uv.x < 0.5f
                ? UndergroundParkingMarkGeometry.ParkingBlueVertex
                : UndergroundParkingMarkGeometry.ParkingWhiteVertex;
            draft.Colors.Add(color);
            draft.Colors.Add(color);
            draft.Colors.Add(color);
            draft.Colors.Add(color);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 2);
            draft.Triangles.Add(start + 1);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 3);
            draft.Triangles.Add(start + 2);
        }

        private static void AddTexturedQuad(
            MeshDraft draft,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector2 uvA,
            Vector2 uvB,
            Vector2 uvC,
            Vector2 uvD)
        {
            int start = draft.Vertices.Count;
            draft.Vertices.Add(a);
            draft.Vertices.Add(b);
            draft.Vertices.Add(c);
            draft.Vertices.Add(d);
            draft.Uvs.Add(uvA);
            draft.Uvs.Add(uvB);
            draft.Uvs.Add(uvC);
            draft.Uvs.Add(uvD);
            draft.Colors.Add(Color.white);
            draft.Colors.Add(Color.white);
            draft.Colors.Add(Color.white);
            draft.Colors.Add(Color.white);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 2);
            draft.Triangles.Add(start + 1);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 3);
            draft.Triangles.Add(start + 2);
        }

        private static Material GetEntrancePadMaterial()
        {
            if (_entrancePadMaterial == null)
                _entrancePadMaterial = CreateTransparentMaterial(new Color(0.2f, 0.24f, 0.28f, 0.68f), null, 3000, true);
            return _entrancePadMaterial;
        }

        private static Material GetEntranceKioskMaterial()
        {
            if (_entranceKioskMaterial == null)
            {
                _entranceKioskMaterial = CreateOpaqueMaterial(new Color(0.14f, 0.17f, 0.19f, 1f), 510);
                ApplyWetSurface(
                    _entranceKioskMaterial,
                    GetCurrentSurfaceWetness(),
                    new Color(0.14f, 0.17f, 0.19f, 1f),
                    0.80f,
                    0.10f,
                    0.70f);
            }
            return _entranceKioskMaterial;
        }

        private static Material GetEntranceSignMaterial()
        {
            if (_entranceSignMaterial == null)
                _entranceSignMaterial = CreateTransparentMaterial(Color.white, CreateParkingSignTexture(), 5000, false);
            return _entranceSignMaterial;
        }

        private static Material GetBuildingAttachedPortalMaterial()
        {
            if (_buildingAttachedPortalMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored")
                                ?? Shader.Find("Unlit/Color")
                                ?? Shader.Find("Diffuse");
                _buildingAttachedPortalMaterial = new Material(shader);
                _buildingAttachedPortalMaterial.hideFlags = HideFlags.HideAndDontSave;
                _buildingAttachedPortalMaterial.color = Color.white;
                _buildingAttachedPortalMaterial.SetColor("_Color", Color.white);
                _buildingAttachedPortalMaterial.SetInt("_SrcBlend",
                    (int)UnityEngine.Rendering.BlendMode.One);
                _buildingAttachedPortalMaterial.SetInt("_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.Zero);
                _buildingAttachedPortalMaterial.SetInt("_Cull",
                    (int)UnityEngine.Rendering.CullMode.Back);
                _buildingAttachedPortalMaterial.SetInt("_ZWrite", 1);
                _buildingAttachedPortalMaterial.SetInt("_ZTest",
                    (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                _buildingAttachedPortalMaterial.renderQueue = 2005;
            }

            return _buildingAttachedPortalMaterial;
        }

        private static Material GetBuildingAttachedTarmacMaterial()
        {
            if (_buildingAttachedTarmacMaterial == null)
            {
                _buildingAttachedTarmacMaterial = CreateLocallyLitSurfaceMaterial(
                    CreateFineTarmacTexture());
                _buildingAttachedTarmacMaterial.renderQueue = 2005;
                ApplyWetSurface(
                    _buildingAttachedTarmacMaterial,
                    GetCurrentSurfaceWetness(),
                    Color.white,
                    0.74f,
                    0.08f,
                    0.78f);
            }

            return _buildingAttachedTarmacMaterial;
        }

        private static Material GetBuildingAttachedSideKerbMaterial()
        {
            if (_buildingAttachedSideKerbMaterial == null)
            {
                _buildingAttachedSideKerbMaterial = CreateLocallyLitSurfaceMaterial(
                    CreateFlatKerbStoneTexture());
                _buildingAttachedSideKerbMaterial.renderQueue = 2006;
                ApplyWetSurface(
                    _buildingAttachedSideKerbMaterial,
                    GetCurrentSurfaceWetness(),
                    Color.white,
                    0.88f,
                    0.12f,
                    0.64f);
            }

            return _buildingAttachedSideKerbMaterial;
        }

        private static Material GetBuildingAttachedParkingMarkMaterial()
        {
            if (_buildingAttachedParkingMarkMaterial == null)
            {
                _buildingAttachedParkingMarkMaterial = CreateColorStableParkingMarkMaterial(
                    2006);
                _buildingAttachedParkingMarkMaterial.renderQueue = 2006;
            }

            return _buildingAttachedParkingMarkMaterial;
        }

        private static Material GetBuildingAttachedLampLensMaterial()
        {
            if (_buildingAttachedLampLensMaterial == null)
            {
                _buildingAttachedLampLensMaterial = CreateLocallyLitSurfaceMaterial(null);
                Color lensColor = new Color(0.72f, 0.62f, 0.42f, 1f);
                _buildingAttachedLampLensMaterial.color = lensColor;
                _buildingAttachedLampLensMaterial.SetColor("_Color", lensColor);
                _buildingAttachedLampLensMaterial.renderQueue = 2007;
            }

            return _buildingAttachedLampLensMaterial;
        }

        private static Material CreateLocallyLitSurfaceMaterial(Texture mainTexture)
        {
            // Do not use Custom/Buildings/Building/Default here. It interprets
            // absent packed building maps and reproduced the large cream/tiled
            // section even with the attached projected Light hard-disabled.
            Shader shader = Shader.Find("Standard")
                            ?? Shader.Find("Diffuse")
                            ?? Shader.Find("Legacy Shaders/Diffuse")
                            ?? Shader.Find("Unlit/Texture");
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.mainTexture = mainTexture;
            material.color = Color.white;
            material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.08f);
            if (material.HasProperty("_GlossMapScale"))
                material.SetFloat("_GlossMapScale", 0.08f);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            return material;
        }

        private static Material CreateColorStableParkingMarkMaterial(int renderQueue)
        {
            // The game's atmospheric texture path turned RGB 0,102,178 into
            // pale cyan even with Unlit/Texture. These meshes carry exact
            // blue/white vertex colours, and Internal-Colored avoids that path.
            Shader shader = Shader.Find("Hidden/Internal-Colored")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("UI/Default")
                            ?? Shader.Find("Sprites/Default");
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = Color.white;
            material.SetColor("_Color", Color.white);
            material.SetInt("_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            material.SetInt("_ZWrite", 1);
            material.SetInt("_ZTest",
                (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            material.renderQueue = Mathf.Max(2000, renderQueue);
            return material;
        }

        private static Texture2D CreateFineTarmacTexture()
        {
            const int size = 256;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hash = unchecked(x * 374761393 + y * 668265263);
                    hash = unchecked((hash ^ (hash >> 13)) * 1274126177);
                    hash ^= hash >> 16;
                    int grain = (hash & 15) - 7;
                    int aggregate = (hash & 255) < 5 ? 11 : 0;
                    byte r = (byte)Mathf.Clamp(45 + grain + aggregate, 0, 255);
                    byte g = (byte)Mathf.Clamp(48 + grain + aggregate, 0, 255);
                    byte b = (byte)Mathf.Clamp(49 + grain + aggregate, 0, 255);
                    pixels[y * size + x] = new Color32(r, g, b, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            return texture;
        }

        private static Texture2D CreateFlatKerbStoneTexture()
        {
            const int size = 128;
            const int border = 1;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hash = unchecked(x * 1103515245 + y * 12345);
                    hash ^= hash >> 16;
                    int grain = (hash & 15) - 7;
                    bool edge = x < border
                                || x >= size - border
                                || y < border
                                || y >= size - border;
                    int baseTone = edge ? 62 : 94;
                    byte r = (byte)Mathf.Clamp(baseTone + grain, 0, 255);
                    byte g = (byte)Mathf.Clamp(baseTone + grain + (edge ? 0 : 2), 0, 255);
                    byte b = (byte)Mathf.Clamp(baseTone + grain + (edge ? 1 : 3), 0, 255);
                    pixels[y * size + x] = new Color32(r, g, b, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            return texture;
        }

        private static Material GetGarageStructureMaterial()
        {
            if (_garageStructureMaterial == null)
                _garageStructureMaterial = CreateXrayBlockMaterial(5200);
            return _garageStructureMaterial;
        }

        private static Material GetParkedCarMaterial()
        {
            if (_parkedCarMaterial == null)
            {
                GetParkedCarMesh();
                Color grey = new Color(0.46f, 0.48f, 0.5f, 0.78f);
                // Vehicle diffuse textures use the game's vehicle shader alpha
                // layout, while generic transparent shaders retain a hard-coded
                // terrain depth test. Use the same explicit x-ray overlay shader
                // path as the garage so underground car silhouettes stay visible.
                _parkedCarMaterial = CreateXrayOverlayMaterial(grey, 5300);
            }

            return _parkedCarMaterial;
        }

        private static Material CreateXrayBlockMaterial(int renderQueue)
        {
            return CreateXrayOverlayMaterial(GarageBlockColor, renderQueue);
        }

        private static Material CreateXrayOverlayMaterial(Color color, int renderQueue)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Diffuse");
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = color;
            material.SetColor("_Color", color);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = renderQueue;
            return material;
        }

        private static Material CreateOpaqueMaterial(Color color, int renderQueue)
        {
            Shader shader = Shader.Find("Standard")
                            ?? Shader.Find("Diffuse")
                            ?? Shader.Find("Unlit/Color");
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = color;
            material.SetColor("_Color", color);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 1);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.10f);
            material.renderQueue = Mathf.Max(2000, renderQueue);
            return material;
        }

        private static Material CreateTransparentMaterial(Color color, Texture texture, int renderQueue, bool depthTest)
        {
            Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Transparent/Diffuse") ?? Shader.Find("Diffuse");
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = color;
            if (texture != null)
                material.mainTexture = texture;

            material.SetColor("_Color", color);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)(depthTest
                ? UnityEngine.Rendering.CompareFunction.LessEqual
                : UnityEngine.Rendering.CompareFunction.Always));
            material.renderQueue = renderQueue;
            return material;
        }

        private static Texture2D CreateParkingSignTexture()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 blue = new Color32(52, 112, 210, 245);
            Color32 white = new Color32(245, 248, 255, 255);
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            FillRect(pixels, size, size, 18, 18, 92, 92, blue);
            FillRect(pixels, size, size, 32, 34, 18, 62, white);
            FillRect(pixels, size, size, 50, 34, 30, 14, white);
            FillRect(pixels, size, size, 76, 42, 12, 20, white);
            FillRect(pixels, size, size, 50, 60, 28, 14, white);

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            return texture;
        }

        private static void FillRect(Color32[] pixels, int width, int height, int x, int y, int w, int h, Color32 color)
        {
            for (int yy = Mathf.Max(0, y); yy < Mathf.Min(height, y + h); yy++)
            {
                int row = yy * width;
                for (int xx = Mathf.Max(0, x); xx < Mathf.Min(width, x + w); xx++)
                    pixels[row + xx] = color;
            }
        }

        private static float ResolveSurfaceHeight(Vector3 position)
        {
            TerrainManager terrainManager = TerrainManager.instance;
            return terrainManager == null ? position.y : terrainManager.SampleRawHeightSmooth(position);
        }

        private static Vector3 ResolveSurfaceNormal(Vector3 position)
        {
            Vector3 right = Vector3.right * TerrainNormalSampleDistance;
            Vector3 forward = Vector3.forward * TerrainNormalSampleDistance;
            float leftHeight = ResolveSurfaceHeight(position - right);
            float rightHeight = ResolveSurfaceHeight(position + right);
            float backHeight = ResolveSurfaceHeight(position - forward);
            float frontHeight = ResolveSurfaceHeight(position + forward);
            Vector3 across = new Vector3(TerrainNormalSampleDistance * 2f, rightHeight - leftHeight, 0f);
            Vector3 along = new Vector3(0f, frontHeight - backHeight, TerrainNormalSampleDistance * 2f);
            Vector3 normal = Vector3.Cross(along, across);
            if (normal.y < 0f)
                normal = -normal;

            return NormalizeVector(normal, Vector3.up);
        }

        private static Quaternion CreateSurfaceRotation(Vector3 forward, Vector3 surfaceNormal)
        {
            surfaceNormal = NormalizeVector(surfaceNormal, Vector3.up);
            forward = forward - surfaceNormal * Vector3.Dot(forward, surfaceNormal);
            forward = NormalizeVector(forward, Vector3.ProjectOnPlane(Vector3.forward, surfaceNormal));
            return Quaternion.LookRotation(forward, surfaceNormal);
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

        private static Vector3 NormalizeVector(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude <= 0.001f)
                value = fallback;

            if (value.sqrMagnitude <= 0.001f)
                value = Vector3.up;

            value.Normalize();
            return value;
        }

        private static string FormatVector(Vector3 value)
        {
            return "("
                   + value.x.ToString("0.0")
                   + ", "
                   + value.y.ToString("0.0")
                   + ", "
                   + value.z.ToString("0.0")
                   + ")";
        }

        private sealed class MeshDraft
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Triangles = new List<int>();
        }

        private static void EnsureRenderManagerRegistered()
        {
            if (_renderManagerRegistered)
                return;

            try
            {
                RenderManager.RegisterRenderableManager(ParkingRenderableManagerInstance);
                _renderManagerRegistered = true;
                UndergroundParkingLog.Advanced("X-ray render manager registered.");
            }
            catch (Exception e)
            {
                UndergroundParkingLog.Warning("X-ray render manager failed: " + e.Message);
            }
        }

        private static DrawCallData RenderXrayVisuals(RenderManager.CameraInfo cameraInfo, string passName, bool forceVisible)
        {
            DrawCallData drawCallData = default(DrawCallData);
            if (cameraInfo == null || (RenderItems.Count == 0 && UndergroundParkingRegistry.Count == 0))
                return drawCallData;

            // Overlay callback selection does not itself prove UPG visibility;
            // other information views can invoke the underground pass too.
            // Transport mode is the mandatory top-level processing gate.
            if (!ShouldShowXrayVisuals())
                return drawCallData;

            int rendered = 0;
            Material activeStaticMaterial = null;
            for (int i = 0; i < RenderItems.Count; i++)
            {
                RenderItem item = RenderItems[i];
                if (item.Mesh == null || item.Material == null)
                    continue;

                if (!IsFacilityXrayVisible(item.FacilityId))
                    continue;
                if (!forceVisible && !cameraInfo.CheckRenderDistance(item.Center, 4096f))
                    continue;

                if (activeStaticMaterial != item.Material)
                {
                    if (!item.Material.SetPass(0))
                        continue;
                    activeStaticMaterial = item.Material;
                }

                Graphics.DrawMeshNow(item.Mesh, item.Matrix);
                rendered++;
            }

            bool parkedCarPassReady = false;
            Material parkedCarMaterial = GetParkedCarMaterial();
            for (int i = 0; i < ParkedCarRenderItems.Count; i++)
            {
                RenderItem item = ParkedCarRenderItems[i];
                if (item.Mesh == null || item.Material == null)
                    continue;

                if (!IsFacilityXrayVisible(item.FacilityId))
                    continue;
                if (!forceVisible && !cameraInfo.CheckRenderDistance(item.Center, 4096f))
                    continue;

                // All parked bodies deliberately share one cached neutral
                // material. Bind it once for the visible batch instead of once
                // per car; repeated SetPass calls dominated the growing
                // occupancy draw set without changing any rendered state.
                if (!parkedCarPassReady)
                {
                    if (parkedCarMaterial == null
                        || !parkedCarMaterial.SetPass(0))
                        break;
                    parkedCarPassReady = true;
                }

                Graphics.DrawMeshNow(item.Mesh, item.Matrix);
                rendered++;
            }

            // Moving and placed underground cars must be composited in this
            // same final x-ray pass. A camera-owned MeshRenderer is drawn
            // before the garage overlay and therefore makes the identical
            // grey material appear much darker.
            for (int i = 0; i < InternalJourneys.Count; i++)
                rendered += DrawInternalJourney(
                    InternalJourneys[i],
                    parkedCarMaterial,
                    ref parkedCarPassReady);
            for (int i = 0; i < InternalDepartureJourneys.Count; i++)
                rendered += DrawInternalJourney(
                    InternalDepartureJourneys[i].Movement,
                    parkedCarMaterial,
                    ref parkedCarPassReady);
            Camera renderCamera = null;
            CameraController cameraController = ToolsModifierControl.cameraController;
            if (cameraController != null)
                renderCamera = cameraController.m_camera;
            if (renderCamera == null)
                renderCamera = Camera.main;
            rendered += UndergroundParkingPortalAnimationManager
                .DrawXrayDepartures(renderCamera);

            drawCallData.m_overlayCalls = rendered;
            if (rendered > 0 && !_xrayLogged)
            {
                _xrayLogged = true;
                UndergroundParkingLog.Advanced("X-ray visuals rendered: count="
                                            + rendered
                                            + " pass="
                                            + passName
                                            + " infoMode="
                                            + GetCurrentInfoModeName());
            }

            return drawCallData;
        }

        private static int DrawInternalJourney(
            InternalParkingJourney journey,
            Material material,
            ref bool materialPassReady)
        {
            if (journey == null || journey.Mesh == null || material == null)
                return 0;

            Vector3 position = journey.Position + Vector3.up * journey.VerticalOffset;
            if (!IsFacilityVisibleOnCamera(journey.FacilityId))
                return 0;
            if (!materialPassReady)
            {
                if (!material.SetPass(0))
                    return 0;
                materialPassReady = true;
            }

            Graphics.DrawMeshNow(
                journey.Mesh,
                Matrix4x4.TRS(
                    position,
                    journey.Rotation,
                    Vector3.one * journey.RenderScale));
            return 1;
        }

        private static bool IsXrayPointInsideCameraEnvelope(
            Camera camera,
            Vector3 position,
            float radius)
        {
            if (camera == null)
                return false;

            Vector3 centre = camera.WorldToViewportPoint(position);
            if (centre.z + radius <= 0f)
                return false;

            Vector3 horizontalEdge = camera.WorldToViewportPoint(
                position + camera.transform.right * radius);
            Vector3 verticalEdge = camera.WorldToViewportPoint(
                position + camera.transform.up * radius);
            float horizontalMargin = Mathf.Abs(horizontalEdge.x - centre.x) + 0.08f;
            float verticalMargin = Mathf.Abs(verticalEdge.y - centre.y) + 0.08f;
            return centre.x >= -horizontalMargin
                   && centre.x <= 1f + horizontalMargin
                   && centre.y >= -verticalMargin
                   && centre.y <= 1f + verticalMargin;
        }

        private static int DrawAllocatedVehicleBodyTints(
            RenderManager.CameraInfo cameraInfo)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            UndergroundParkingEntryRouteManager.RoutedVehicleHighlight[] highlights =
                UndergroundParkingEntryRouteManager.GetPublishedHighlights();
            if (cameraInfo == null
                || vehicleManager == null
                || highlights == null
                || highlights.Length == 0)
                return 0;

            int rendered = 0;
            for (int i = 0; i < highlights.Length; i++)
            {
                UndergroundParkingEntryRouteManager.RoutedVehicleHighlight highlight =
                    highlights[i];
                ushort vehicleId = highlight.VehicleId;
                if (vehicleId == 0
                    || vehicleId >= vehicleManager.m_vehicles.m_size)
                    continue;

                ref Vehicle vehicle =
                    ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                VehicleInfo info = vehicle.Info;
                Vector3 position = vehicle.GetLastFramePosition();
                if ((vehicle.m_flags & Vehicle.Flags.Created) == 0
                    || info == null
                    || !(info.m_vehicleAI is PassengerCarAI)
                    || !string.Equals(
                        info.name,
                        highlight.PrefabName,
                        StringComparison.Ordinal)
                    || vehicle.m_citizenUnits != highlight.CitizenUnits
                    || !cameraInfo.Intersect(
                        position,
                        info.m_generatedInfo == null
                            ? 8f
                            : Mathf.Max(8f, info.m_generatedInfo.m_size.magnitude)))
                    continue;

                if (RenderAllocatedVehicleBodyTint(
                        cameraInfo,
                        vehicleId,
                        ref vehicle,
                        info))
                {
                    rendered++;
                }
            }
            return rendered;
        }

        private static bool RenderAllocatedVehicleBodyTint(
            RenderManager.CameraInfo cameraInfo,
            ushort vehicleId,
            ref Vehicle vehicle,
            VehicleInfo info)
        {
            SimulationManager simulationManager = SimulationManager.instance;
            if (cameraInfo == null || simulationManager == null || info == null)
                return false;

            // Reproduce vanilla Vehicle.RenderInstance's current render-frame
            // interpolation, then use its public body renderer with only the
            // colour argument changed. This preserves the exact model,
            // submeshes, tyres, lights, steering and sway instead of drawing a
            // selection box or modifying the simulated vehicle/prefab colour.
            uint targetFrame = vehicle.GetTargetFrame(info, vehicleId);
            Vehicle.Frame older = vehicle.GetFrameData(targetFrame - 32u);
            Vehicle.Frame newer = vehicle.GetFrameData(targetFrame - 16u);
            float interpolation = ((targetFrame & 15u)
                                   + simulationManager.m_referenceTimer) * 0.0625f;

            bool underground = newer.m_underground && older.m_underground;
            bool insideBuilding = newer.m_insideBuilding && older.m_insideBuilding;
            bool transition = newer.m_transition || older.m_transition;
            if (insideBuilding && !transition)
                return false;

            Bezier3 positionCurve = new Bezier3
            {
                a = older.m_position,
                b = older.m_position + older.m_velocity * 0.333f,
                c = newer.m_position - newer.m_velocity * 0.333f,
                d = newer.m_position
            };
            Vector3 position = positionCurve.Position(interpolation);

            Bezier3 swayCurve = new Bezier3
            {
                a = older.m_swayPosition,
                b = older.m_swayPosition + older.m_swayVelocity * 0.333f,
                c = newer.m_swayPosition - newer.m_swayVelocity * 0.333f,
                d = newer.m_swayPosition
            };
            Vector3 sway = swayCurve.Position(interpolation);
            if (info.m_generatedInfo != null)
            {
                sway.x *= info.m_leanMultiplier
                          / Mathf.Max(1f, info.m_generatedInfo.m_wheelGauge);
                sway.z *= info.m_nodMultiplier
                          / Mathf.Max(1f, info.m_generatedInfo.m_wheelBase);
            }

            Quaternion rotation = Quaternion.Lerp(
                older.m_rotation,
                newer.m_rotation,
                interpolation);
            Vector4 lightState = interpolation >= 0.5f
                ? newer.m_lightIntensity
                : older.m_lightIntensity;
            Vector4 tyrePosition = new Vector4(
                older.m_steerAngle
                + (newer.m_steerAngle - older.m_steerAngle) * interpolation,
                older.m_travelDistance
                + (newer.m_travelDistance - older.m_travelDistance) * interpolation,
                0f,
                0f);
            Vector3 velocity = Vector3.Lerp(
                older.m_velocity,
                newer.m_velocity,
                interpolation) * 3.75f;
            float acceleration = newer.m_velocity.magnitude
                                 - older.m_velocity.magnitude;
            Color bodyColor = ParkingBlue;
            // Vehicle blink state belongs to the independently interpolated
            // light vector above. Feeding it into the body-colour alpha makes
            // the exact routed-car tint switch off on alternate blink phases.
            bodyColor.a = 1f;
            InstanceID instanceId = default(InstanceID);
            instanceId.Vehicle = vehicleId;
            int variationMask = ~(1 << (vehicle.m_gateIndex & 31));
            RenderManager.CameraInfo fullDetailCameraInfo =
                CreateFullDetailVehicleCameraInfo(cameraInfo, position, info);

            Vehicle.RenderInstance(
                fullDetailCameraInfo,
                info,
                position,
                rotation,
                sway,
                lightState,
                tyrePosition,
                velocity,
                acceleration,
                bodyColor,
                vehicle.m_flags,
                vehicle.m_flags2,
                variationMask,
                instanceId,
                underground || transition,
                !underground || transition);
            return true;
        }

        private static RenderManager.CameraInfo CreateFullDetailVehicleCameraInfo(
            RenderManager.CameraInfo source,
            Vector3 vehiclePosition,
            VehicleInfo info)
        {
            // Vehicle.RenderInstance writes distant vehicles into the prefab's
            // shared LOD batch. This overlay runs after the normal vehicle LOD
            // flush, so an appended blue instance can miss that frame and the
            // full/LOD threshold flickers while zooming. Preserve the real
            // camera/frustum, but supply a private distance origin that keeps
            // this already-frustum-validated routed car on the immediate full
            // body path at every playable camera height.
            float lodDistance = info == null
                ? 1f
                : Mathf.Max(1f, info.m_lodRenderDistance);
            return new RenderManager.CameraInfo
            {
                m_camera = source.m_camera,
                m_layerMask = source.m_layerMask,
                m_rotation = source.m_rotation,
                m_shadowRotation = source.m_shadowRotation,
                m_position = vehiclePosition - (source.m_forward * (lodDistance * 0.45f)),
                m_right = source.m_right,
                m_up = source.m_up,
                m_forward = source.m_forward,
                m_shadowOffset = source.m_shadowOffset,
                m_near = source.m_near,
                m_far = source.m_far,
                m_height = source.m_height,
                m_bounds = source.m_bounds,
                m_nearBounds = source.m_nearBounds,
                m_planeA = source.m_planeA,
                m_planeB = source.m_planeB,
                m_planeC = source.m_planeC,
                m_planeD = source.m_planeD,
                m_planeE = source.m_planeE,
                m_planeF = source.m_planeF,
                m_directionA = source.m_directionA,
                m_directionB = source.m_directionB,
                m_directionC = source.m_directionC,
                m_directionD = source.m_directionD
            };
        }

        private static bool ShouldHighlightAllocatedVehicles()
        {
            try
            {
                InfoManager manager = InfoManager.instance;
                return manager != null
                       && manager.CurrentMode == InfoManager.InfoMode.Transport;
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldShowXrayVisuals()
        {
            try
            {
                InfoManager manager = InfoManager.instance;
                // UPG is a Public Transport facility. Water, electricity,
                // traffic and every other information view must not run or
                // draw its underground garage pass merely because some info
                // mode is active.
                return manager != null
                       && manager.CurrentMode == InfoManager.InfoMode.Transport;
            }
            catch
            {
                return false;
            }
        }

        private static string GetCurrentInfoModeName()
        {
            try
            {
                InfoManager manager = InfoManager.instance;
                return manager == null ? "none" : manager.CurrentMode.ToString();
            }
            catch
            {
                return "unknown";
            }
        }

        private struct RenderItem
        {
            public readonly int FacilityId;
            public readonly Mesh Mesh;
            public readonly Matrix4x4 Matrix;
            public readonly Material Material;
            public readonly Vector3 Center;
            public readonly float Radius;

            public RenderItem(int facilityId, Mesh mesh, Matrix4x4 matrix, Material material, Vector3 center, float radius)
            {
                FacilityId = facilityId;
                Mesh = mesh;
                Matrix = matrix;
                Material = material;
                Center = center;
                Radius = radius;
            }
        }

        private sealed class InternalParkingJourney
        {
            public readonly ushort ParkedId;
            public readonly int FacilityId;
            public readonly Mesh Mesh;
            public readonly List<Vector3> Waypoints;
            public readonly Quaternion FinalRotation;
            public readonly Vector3 FacilityCentre;
            public readonly long Sequence;
            public readonly bool PreferShortestArrivalYaw;
            public readonly float TotalDistance;
            public readonly float RenderScale;
            public readonly float VerticalOffset;
            public GameObject Root;
            public MeshRenderer Renderer;
            public float Distance;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool RenderedOnce;

            public InternalParkingJourney(
                ushort parkedId,
                int facilityId,
                Mesh mesh,
                VehicleInfo info,
                List<Vector3> waypoints,
                Quaternion finalRotation,
                Vector3 facilityCentre,
                long sequence,
                bool preferShortestArrivalYaw)
            {
                ParkedId = parkedId;
                FacilityId = facilityId;
                Mesh = mesh;
                RenderScale = GetNeutralVehicleRenderScale(info);
                VerticalOffset = GetNeutralVehicleVerticalOffset();
                Waypoints = waypoints;
                FinalRotation = finalRotation;
                FacilityCentre = facilityCentre;
                Sequence = sequence;
                PreferShortestArrivalYaw = preferShortestArrivalYaw;
                Distance = 0f;
                RenderedOnce = false;
                TotalDistance = 0f;
                for (int i = 1; i < waypoints.Count; i++)
                    TotalDistance += (waypoints[i] - waypoints[i - 1]).magnitude;
                Position = waypoints.Count == 0 ? facilityCentre : waypoints[0];
                Vector3 firstDirection = waypoints.Count < 2
                    ? finalRotation * Vector3.forward
                    : waypoints[1] - waypoints[0];
                Rotation = firstDirection.sqrMagnitude <= 0.001f
                    ? finalRotation
                    : Quaternion.LookRotation(firstDirection.normalized, Vector3.up);
            }

            public bool CreateVisual(string name)
            {
                if (_root == null || Mesh == null)
                    return false;

                Material material = GetParkedCarMaterial();
                if (material == null)
                    return false;

                Root = new GameObject(name);
                Root.transform.parent = _root.transform;
                MeshFilter filter = Root.AddComponent<MeshFilter>();
                filter.sharedMesh = Mesh;
                Renderer = Root.AddComponent<MeshRenderer>();
                int materialCount = Mathf.Max(1, Mesh.subMeshCount);
                Material[] materials = new Material[materialCount];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;
                Renderer.sharedMaterials = materials;
                Renderer.receiveShadows = false;
                Renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                Renderer.enabled = false;
                UpdateVisual(true);
                return true;
            }

            public void UpdateVisual(bool visible)
            {
                if (Root == null || Renderer == null)
                    return;
                Root.transform.position = Position + Vector3.up * VerticalOffset;
                Root.transform.rotation = Rotation;
                Root.transform.localScale = Vector3.one * RenderScale;
                bool enable = visible && ShouldShowXrayVisuals();
                // The final x-ray render pass owns the visible draw so moving
                // cars composite identically to parked cars. This renderer is
                // retained only as the lightweight pose object.
                Renderer.enabled = false;
                if (!enable || RenderedOnce)
                    return;

                RenderedOnce = true;
                UndergroundParkingLog.Advanced(
                    "UPG internal vehicle renderer enabled: facility="
                    + FacilityId
                    + " parked="
                    + ParkedId
                    + " position="
                    + FormatVector(Position)
                    + " scale="
                    + RenderScale.ToString("0.000")
                    + " subMeshes="
                    + Mesh.subMeshCount);
            }

            public void DestroyVisual()
            {
                if (Root != null)
                    UnityEngine.Object.Destroy(Root);
                Root = null;
                Renderer = null;
            }

            public void UpdatePose(float delta)
            {
                float remaining = Distance;
                for (int i = 1; i < Waypoints.Count; i++)
                {
                    Vector3 from = Waypoints[i - 1];
                    Vector3 to = Waypoints[i];
                    float segmentLength = (to - from).magnitude;
                    if (segmentLength <= 0.001f)
                        continue;
                    if (remaining > segmentLength)
                    {
                        remaining -= segmentLength;
                        continue;
                    }

                    float segmentProgress = Mathf.Clamp01(remaining / segmentLength);
                    Position = Vector3.Lerp(from, to, segmentProgress);
                    Vector3 direction = to - from;
                    Quaternion desired = direction.sqrMagnitude <= 0.001f
                        ? Rotation
                        : Quaternion.LookRotation(direction.normalized, Vector3.up);
                    if (Distance >= TotalDistance - 0.01f)
                        desired = FinalRotation;
                    float rotationBlend = Mathf.Clamp01(delta * 6f);
                    Rotation = PreferShortestArrivalYaw
                        ? InterpolateShortestPlanarYaw(
                            Rotation,
                            desired,
                            rotationBlend)
                        : Quaternion.Slerp(
                            Rotation,
                            desired,
                            rotationBlend);
                    return;
                }

                if (Waypoints.Count > 0)
                    Position = Waypoints[Waypoints.Count - 1];
                Rotation = FinalRotation;
            }

            private static Quaternion InterpolateShortestPlanarYaw(
                Quaternion current,
                Quaternion desired,
                float blend)
            {
                Vector3 currentForward = current * Vector3.forward;
                Vector3 desiredForward = desired * Vector3.forward;
                Vector3 currentHorizontal = currentForward;
                Vector3 desiredHorizontal = desiredForward;
                currentHorizontal.y = 0f;
                desiredHorizontal.y = 0f;
                if (currentHorizontal.sqrMagnitude <= 0.0001f
                    || desiredHorizontal.sqrMagnitude <= 0.0001f)
                {
                    return Quaternion.Slerp(current, desired, blend);
                }

                float currentYaw = Mathf.Atan2(
                    currentHorizontal.x,
                    currentHorizontal.z) * Mathf.Rad2Deg;
                float desiredYaw = Mathf.Atan2(
                    desiredHorizontal.x,
                    desiredHorizontal.z) * Mathf.Rad2Deg;
                float yawDelta = Mathf.DeltaAngle(currentYaw, desiredYaw);
                // Unity's planar positive yaw is a right turn. At the only
                // ambiguous case, an exact half-turn, prefer that natural
                // rightward circulation instead of allowing quaternion sign
                // selection to send an entering car left. Every non-tie still
                // follows the dynamically smaller signed yaw.
                if (Mathf.Abs(Mathf.Abs(yawDelta) - 180f) <= 0.001f)
                    yawDelta = 180f;
                float yaw = currentYaw + yawDelta * blend;

                float currentPitch = Mathf.Atan2(
                    -currentForward.y,
                    currentHorizontal.magnitude) * Mathf.Rad2Deg;
                float desiredPitch = Mathf.Atan2(
                    -desiredForward.y,
                    desiredHorizontal.magnitude) * Mathf.Rad2Deg;
                float pitch = Mathf.LerpAngle(
                    currentPitch,
                    desiredPitch,
                    blend);
                return Quaternion.AngleAxis(yaw, Vector3.up)
                       * Quaternion.AngleAxis(pitch, Vector3.right);
            }

            public Vector3 GetPositionAtDistance(float distance)
            {
                float remaining = Mathf.Clamp(distance, 0f, TotalDistance);
                for (int i = 1; i < Waypoints.Count; i++)
                {
                    Vector3 from = Waypoints[i - 1];
                    Vector3 to = Waypoints[i];
                    float segmentLength = (to - from).magnitude;
                    if (segmentLength <= 0.001f)
                        continue;
                    if (remaining > segmentLength)
                    {
                        remaining -= segmentLength;
                        continue;
                    }
                    return Vector3.Lerp(from, to, Mathf.Clamp01(remaining / segmentLength));
                }
                return Waypoints.Count == 0
                    ? FacilityCentre
                    : Waypoints[Waypoints.Count - 1];
            }
        }

        private sealed class InternalDepartureJourney
        {
            public readonly ushort VehicleId;
            public readonly VehicleInfo Info;
            public readonly UndergroundParkingFacility Facility;
            public readonly UndergroundParkingRoadConnection Connection;
            public readonly Color SurfaceColor;
            public readonly InternalParkingJourney Movement;

            public InternalDepartureJourney(
                ushort vehicleId,
                VehicleInfo info,
                UndergroundParkingFacility facility,
                UndergroundParkingRoadConnection connection,
                Color surfaceColor,
                InternalParkingJourney movement)
            {
                VehicleId = vehicleId;
                Info = info;
                Facility = facility;
                Connection = connection;
                SurfaceColor = surfaceColor;
                Movement = movement;
            }
        }

        private sealed class ParkingRenderableManager : IRenderableManager
        {
            private DrawCallData _drawCallData;

            public string GetName()
            {
                return "Underground Parking Garage Xray";
            }

            public DrawCallData GetDrawCallData()
            {
                return _drawCallData;
            }

            public void CheckReferences()
            {
            }

            public void InitRenderData()
            {
            }

            public void BeginRendering(RenderManager.CameraInfo cameraInfo)
            {
                _drawCallData = default(DrawCallData);
            }

            public void EndRendering(RenderManager.CameraInfo cameraInfo)
            {
            }

            public void BeginOverlay(RenderManager.CameraInfo cameraInfo)
            {
                _drawCallData = default(DrawCallData);
            }

            public void EndOverlay(RenderManager.CameraInfo cameraInfo)
            {
                _drawCallData = RenderXrayVisuals(cameraInfo, "EndOverlay", false);
            }

            public void UndergroundOverlay(RenderManager.CameraInfo cameraInfo)
            {
                _drawCallData = RenderXrayVisuals(cameraInfo, "UndergroundOverlay", true);
            }

            public bool CalculateGroupData(
                int groupX,
                int groupZ,
                int layer,
                ref int vertexCount,
                ref int triangleCount,
                ref int objectCount,
                ref RenderGroup.VertexArrays vertexArrays)
            {
                return false;
            }

            public void PopulateGroupData(
                int groupX,
                int groupZ,
                int layer,
                ref int vertexIndex,
                ref int triangleIndex,
                Vector3 groupPosition,
                RenderGroup.MeshData data,
                ref Vector3 min,
                ref Vector3 max,
                ref float maxRenderDistance,
                ref float maxInstanceDistance,
                ref bool requireSurfaceMaps)
            {
            }
        }
    }

    internal class UndergroundParkingVisualVisibilityKeeper : MonoBehaviour
    {
        private float _nextUpdate;

        private void LateUpdate()
        {
            bool hasPendingUpdates = UndergroundParkingVisualManager.HasPendingMainThreadUpdates;
            if (!hasPendingUpdates && Time.realtimeSinceStartup < _nextUpdate)
                return;

            _nextUpdate = Time.realtimeSinceStartup + 0.2f;
            UndergroundParkingVisualManager.ProcessMainThreadUpdates();
        }
    }

    internal sealed class UndergroundParkingEntranceLightController : MonoBehaviour
    {
        private const float RefreshInterval = 0.25f;
        private const float DefaultMaximumIntensity = 3.8f;
        private const int StateLogLimitPerBranch = 4;
        private static int _standardStateLogCount;
        private static int _glowStateLogCount;
        private static bool? _lastLoggedStandardEnabled;
        private static bool? _lastLoggedGlowEnabled;
        private Light _light;
        private int _facilityId;
        private float _maximumIntensity;
        private float _nextRefresh;
        private Renderer _lensRenderer;
        private Renderer _poolRenderer;

        public void Initialize(
            Light light,
            int facilityId,
            float maximumIntensity = DefaultMaximumIntensity,
            Renderer lensRenderer = null,
            Renderer poolRenderer = null)
        {
            _light = light;
            _facilityId = facilityId;
            _maximumIntensity = Mathf.Max(0f, maximumIntensity);
            _lensRenderer = lensRenderer;
            _poolRenderer = poolRenderer;
            RefreshLight();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh)
                return;

            _nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshLight();
        }

        private void RefreshLight()
        {
            if (_light == null)
                return;

            DayNightProperties properties = DayNightProperties.instance;
            SimulationManager simulation = SimulationManager.instance;
            float hour = simulation == null ? 12f : simulation.m_currentDayTimeHour;
            bool clockNight = hour >= 19f || hour < 6.5f;
            float atmosphereNight = properties == null ? 0f : properties.NightTime;
            Light sun = properties == null ? null : properties.sunLightSource;
            bool sceneNight = sun != null
                              && (sun.intensity < 0.35f || sun.color.maxColorComponent < 0.42f);
            float requestedNight = Mathf.Max(atmosphereNight, clockNight || sceneNight ? 1f : 0f);
            float strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.72f, requestedNight));
            bool enabled = strength > 0.02f;
            _light.intensity = _maximumIntensity * strength;
            _light.enabled = enabled;
            if (_lensRenderer != null)
                _lensRenderer.enabled = enabled;
            if (_poolRenderer != null)
                _poolRenderer.enabled = enabled;

            bool glowBranch = _lensRenderer != null || _poolRenderer != null;
            bool? lastLoggedEnabled = glowBranch
                ? _lastLoggedGlowEnabled
                : _lastLoggedStandardEnabled;
            int branchLogCount = glowBranch
                ? _glowStateLogCount
                : _standardStateLogCount;
            if (_facilityId > 0
                && branchLogCount < StateLogLimitPerBranch
                && (!lastLoggedEnabled.HasValue || lastLoggedEnabled.Value != enabled))
            {
                if (glowBranch)
                {
                    _lastLoggedGlowEnabled = enabled;
                    _glowStateLogCount++;
                }
                else
                {
                    _lastLoggedStandardEnabled = enabled;
                    _standardStateLogCount++;
                }
                UndergroundParkingLog.Advanced("UPG parking spotlight state: enabled="
                                            + enabled
                                            + " branch="
                                            + (glowBranch ? "attached-glow" : "standalone-light")
                                            + " hour="
                                            + hour.ToString("0.0")
                                            + " atmosphereNight="
                                            + atmosphereNight.ToString("0.00")
                                            + " sceneNight="
                                            + sceneNight
                                            + " intensity="
                                            + (_maximumIntensity * strength).ToString("0.00")
                                            + " glowRendererTarget=False"
                                            + " lensGlowRendererTarget="
                                            + (_lensRenderer != null)
                                            + " poolGlowRendererTarget="
                                            + (_poolRenderer != null)
                                            + " projectedLightEnabled="
                                            + _light.enabled);
            }
        }
    }
}
