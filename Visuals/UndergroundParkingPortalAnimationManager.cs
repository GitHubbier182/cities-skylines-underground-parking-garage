using System.Collections.Generic;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingPortalAnimationManager
    {
        private const int MaxQueuedAnimations = 64;
        private const float PortalTravelSpeed = 13.4112f;
        private const float ArrivalEntranceClearanceDelay = 0.10f;
        private const float SurfaceDescentStartProgress = 0.32f;
        private const int PortalLengthSamples = 32;
        private const float MaximumAnimationCameraHeight = 300f;
        private static readonly Color ParkingBlue = new Color32(0, 102, 178, 255);
        private static readonly object Sync = new object();
        private static readonly Queue<PortalAnimationRequest> Pending = new Queue<PortalAnimationRequest>();
        private static readonly HashSet<int> DepartureRepathFacilities =
            new HashSet<int>();
        // One physical entrance has one movement owner. Mark it busy when a
        // request is accepted (not later when rendering starts), so arrivals
        // remain real road vehicles and departures remain staged until the
        // complete prior handoff has finished.
        private static readonly HashSet<int> BusyFacilities = new HashSet<int>();
        private static readonly Dictionary<int, float> ArrivalAdmissionCooldowns =
            new Dictionary<int, float>();
        private static readonly List<int> ArrivalAdmissionCooldownFacilityBuffer =
            new List<int>();
        private static GameObject _root;
        private static int _lifecycleGeneration;
        private static int _arrivalLogCount;
        private static int _departureLogCount;
        private static int _startedLogCount;
        private static int _completedLogCount;
        private static int _handoffRebaseLogCount;

        public static void Initialize(GameObject parent)
        {
            if (_root != null)
                return;

            _lifecycleGeneration = unchecked(_lifecycleGeneration + 1);
            _root = new GameObject("UndergroundParkingGaragePortalAnimations");
            if (parent != null)
                _root.transform.parent = parent.transform;
            Object.DontDestroyOnLoad(_root);
            PortalAnimationDriver driver =
                _root.AddComponent<PortalAnimationDriver>();
            driver.LifecycleGeneration = _lifecycleGeneration;
        }

        public static void Shutdown()
        {
            // Unity destroys the old driver at the end of the frame. A city
            // reset can initialize the next UPG lifecycle before that delayed
            // OnDestroy runs, so invalidate this driver's callbacks now. The
            // occupancy shutdown path below remains the sole rollback owner.
            _lifecycleGeneration = unchecked(_lifecycleGeneration + 1);
            lock (Sync)
            {
                foreach (PortalAnimationRequest request in Pending)
                    AbortRequest(request);
                Pending.Clear();
                BusyFacilities.Clear();
                ArrivalAdmissionCooldowns.Clear();
                ArrivalAdmissionCooldownFacilityBuffer.Clear();
                DepartureRepathFacilities.Clear();
            }

            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _arrivalLogCount = 0;
            _departureLogCount = 0;
            _startedLogCount = 0;
            _completedLogCount = 0;
            _handoffRebaseLogCount = 0;
        }

        public static bool HasActivityForFacility(int facilityId)
        {
            if (facilityId <= 0)
                return false;

            lock (Sync)
            {
                return BusyFacilities.Contains(facilityId);
            }
        }

        public static void RequestDepartureRepathForFacility(int facilityId)
        {
            if (facilityId <= 0)
                return;
            lock (Sync)
            {
                DepartureRepathFacilities.Add(facilityId);
            }
        }

        public static bool QueueArrival(
            VehicleInfo info,
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection connection,
            Vector3 surfacePosition,
            Quaternion surfaceRotation,
            Color surfaceColor,
            ushort vehicleId,
            bool tmpeControlledEntrance)
        {
            return Queue(
                info,
                facility,
                connection,
                true,
                surfacePosition,
                surfaceRotation,
                surfaceColor,
                vehicleId,
                tmpeControlledEntrance);
        }

        public static bool QueueDeparture(
            VehicleInfo info,
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection connection,
            ushort vehicleId,
            Color surfaceColor)
        {
            return Queue(info, facility, connection, false, Vector3.zero, Quaternion.identity, surfaceColor, vehicleId, false);
        }

        internal static void GetArrivalTraversalPath(
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection connection,
            Vector3 surfaceStart,
            out Vector3 start,
            out Vector3 startControl,
            out Vector3 portalControl,
            out Vector3 portal,
            out Vector3 underground)
        {
            // This is the atomic real-to-proxy handoff pose. Preserve the
            // exact native vehicle origin here; lifting it would make the
            // proxy jump on the first rendered frame. The established portal
            // endpoint retains its small surface-clearance offset.
            start = surfaceStart != Vector3.zero
                ? surfaceStart
                : connection.LanePosition;
            // One physical tunnel has one centre-line. Do not apply the old
            // longitudinal arrival/departure lane offset: it placed the car to
            // the viewer's right of the rendered entrance even though both
            // visual owners already serialize traffic through the portal.
            // Every entrance uses the same two-owner handoff: the exact-colour
            // surface vehicle finishes fully underground in the upper chamber,
            // then the neutral internal vehicle owns the remaining journey.
            // Building-attached entrances consume the existing 5x5 chamber and
            // tunnel centre-line; the kiosk resolves the equivalent chamber
            // endpoint directly behind and below its authored entrance.
            if (!UndergroundParkingVisualManager.TryGetExistingTunnelTraversal(
                    facility,
                    out portal,
                    out underground))
            {
                Vector3 laneDirection = NormalizeFlat(
                    connection.LaneDirection,
                    facility.Direction);
                Vector3 entranceDirection = GetPerpendicularEntranceDirection(
                    facility,
                    connection,
                    laneDirection);
                portal = facility.VehicleNodePosition
                         + entranceDirection * 5f
                         + Vector3.down * 4f
                         + Vector3.up * 0.18f;
                underground = portal;
            }
            GetOrderedSurfaceControls(
                facility,
                connection,
                start,
                portal,
                out startControl,
                out portalControl);
        }

        private static void GetOrderedSurfaceControls(
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection connection,
            Vector3 start,
            Vector3 portal,
            out Vector3 startControl,
            out Vector3 portalControl)
        {
            Vector3 laneDirection = NormalizeFlat(
                connection.LaneDirection,
                facility.Direction);
            Vector3 entranceDirection = GetPerpendicularEntranceDirection(
                facility,
                connection,
                laneDirection);
            BuildPerpendicularEntranceControls(
                start,
                portal,
                laneDirection,
                entranceDirection,
                out startControl,
                out portalControl);
        }

        private static Vector3 GetPerpendicularEntranceDirection(
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection connection,
            Vector3 laneDirection)
        {
            // A vehicle-length-aware stop is deliberately behind the entrance
            // centre. Its direct vector to the building therefore contains a
            // road-longitudinal component and must never be used as the tunnel
            // heading. Remove that component so every entrance turn finishes
            // exactly perpendicular to its live road, independent of vehicle
            // length or TM:PE's compatible terminal offset.
            Vector3 toEntrance =
                facility.VehicleNodePosition - connection.LanePosition;
            toEntrance.y = 0f;
            laneDirection = NormalizeFlat(laneDirection, facility.Direction);
            Vector3 perpendicular = toEntrance
                                    - laneDirection
                                    * Vector3.Dot(toEntrance, laneDirection);
            return NormalizeFlat(perpendicular, facility.Side);
        }

        private static void BuildPerpendicularEntranceControls(
            Vector3 start,
            Vector3 portal,
            Vector3 laneDirection,
            Vector3 entranceDirection,
            out Vector3 startControl,
            out Vector3 portalControl)
        {
            laneDirection = NormalizeFlat(laneDirection, portal - start);
            entranceDirection = NormalizeFlat(entranceDirection, portal - start);
            Vector3 surfaceDelta = portal - start;
            surfaceDelta.y = 0f;
            float surfaceDistance = surfaceDelta.magnitude;
            float projectedLaneDistance = Mathf.Max(
                0f,
                Vector3.Dot(surfaceDelta, laneDirection));
            // The real car's exact stopped pose is the only surface handoff
            // point. This remains a Bezier handle so building/portal geometry
            // cannot invent a second road station or shift the approved stop.
            float startLead = Mathf.Min(
                3f,
                Mathf.Max(0.35f, projectedLaneDistance * 0.45f));
            if (projectedLaneDistance > 0.05f)
                startLead = Mathf.Min(startLead, projectedLaneDistance);
            else
                startLead = 0f;
            float portalLead = Mathf.Min(
                3f,
                Mathf.Max(0.75f, surfaceDistance * 0.30f));

            startControl = start + laneDirection * startLead;
            // This final control is deliberately only on the road-normal
            // entrance axis. Moving it along the road to "order" the cubic
            // made the car approach the ramp diagonally.
            portalControl = portal - entranceDirection * portalLead;
            startControl.y = start.y;
            portalControl.y = portal.y;
        }

        internal static Vector3 EvaluateArrivalTraversalPath(
            Vector3 start,
            Vector3 startControl,
            Vector3 portalControl,
            Vector3 portal,
            Vector3 underground,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            if ((underground - portal).sqrMagnitude <= 0.001f)
            {
                float inverseSurface = 1f - t;
                Vector3 point =
                    inverseSurface * inverseSurface * inverseSurface * start
                    + 3f * inverseSurface * inverseSurface * t * startControl
                    + 3f * inverseSurface * t * t * portalControl
                    + t * t * t * portal;
                // Keep the requested short forward movement before descent
                // solely on the vertical axis. It may not alter the accepted
                // road-tangent-to-perpendicular horizontal curve.
                float descent = Mathf.InverseLerp(
                    SurfaceDescentStartProgress,
                    1f,
                    t);
                descent = descent * descent * (3f - 2f * descent);
                point.y = Mathf.Lerp(start.y, portal.y, descent);
                return point;
            }
            if (t <= 0.45f)
            {
                float approachT = t / 0.45f;
                float inverse = 1f - approachT;
                return inverse * inverse * inverse * start
                       + 3f * inverse * inverse * approachT * startControl
                       + 3f * inverse * approachT * approachT * portalControl
                       + approachT * approachT * approachT * portal;
            }

            return EvaluateRamp(
                portal,
                underground,
                (t - 0.45f) / 0.55f);
        }

        private static Vector3 EvaluateRamp(
            Vector3 portal,
            Vector3 underground,
            float progress)
        {
            // The visible tunnel descent is a ruled, straight floor from the
            // garage-side edge of its 5x5 turning pad to the garage-floor
            // mouth. Following that exact centre-line is the only path that
            // can remain inside the existing mesh.
            return Vector3.Lerp(portal, underground, Mathf.Clamp01(progress));
        }

        private static float CalculateTraversalDuration(
            Vector3 start,
            Vector3 startControl,
            Vector3 portalControl,
            Vector3 portal,
            Vector3 underground)
        {
            float length = 0f;
            Vector3 previous = start;
            for (int sample = 1; sample <= PortalLengthSamples; sample++)
            {
                Vector3 point = EvaluateArrivalTraversalPath(
                    start,
                    startControl,
                    portalControl,
                    portal,
                    underground,
                    sample / (float)PortalLengthSamples);
                length += (point - previous).magnitude;
                previous = point;
            }
            return Mathf.Max(0.05f, length / PortalTravelSpeed);
        }

        private static bool Queue(
            VehicleInfo info,
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection connection,
            bool arrival,
            Vector3 arrivalStart,
            Quaternion arrivalRotation,
            Color surfaceColor,
            ushort vehicleId,
            bool tmpeControlledEntrance)
        {
            if (_root == null
                || info == null
                || info.m_mesh == null
                || info.m_material == null
                || !connection.IsValid)
                return false;

            if (arrival && tmpeControlledEntrance)
            {
                Vector3 stoppedForward = arrivalRotation * Vector3.forward;
                stoppedForward.y = 0f;
                if (stoppedForward.sqrMagnitude > 0.001f)
                {
                    stoppedForward.Normalize();
                    connection.LaneDirection = stoppedForward;
                }
            }

            Vector3 lane;
            Vector3 arrivalApproach;
            Vector3 arrivalPortalControl;
            Vector3 portal;
            Vector3 underground;
            GetArrivalTraversalPath(
                facility,
                connection,
                arrival && arrivalStart != Vector3.zero
                    ? arrivalStart
                    : connection.LanePosition,
                out lane,
                out arrivalApproach,
                out arrivalPortalControl,
                out portal,
                out underground);
            if (arrival && tmpeControlledEntrance)
            {
                // TM:PE's planning callback never starts visible movement. The
                // native, signal-aware road car reaches the validated portal
                // first, so the compatibility proxy begins at that exact
                // captured pose and owns only the brief kerb-to-underground
                // transfer from the seven-metre road target through the
                // entrance to the established underground endpoint.
                GetOrderedSurfaceControls(
                    facility,
                    connection,
                    lane,
                    portal,
                    out arrivalApproach,
                    out arrivalPortalControl);
            }
            const float portalSide = 0f;
            if (!arrival)
            {
                // The departure endpoint is the vehicle-length-aware road pose
                // after the entrance. Build its forward curve back toward the
                // portal with the opposite tangent so reversing that curve
                // exits the opening perpendicular and finishes in the lane's
                // actual travel direction without a 180-degree correction.
                UndergroundParkingRoadConnection departurePathConnection =
                    connection;
                departurePathConnection.LaneDirection = -NormalizeFlat(
                    connection.LaneDirection,
                    facility.Direction);
                GetOrderedSurfaceControls(
                    facility,
                    departurePathConnection,
                    lane,
                    portal,
                    out arrivalApproach,
                    out arrivalPortalControl);
            }
            Vector3 loweredPortal = portal + Vector3.down * 3.2f;
            Vector3 departureDirection = NormalizeFlat(
                loweredPortal - underground,
                -facility.Side);
            Quaternion departureRotation = Quaternion.LookRotation(
                departureDirection,
                Vector3.up);
            // Arrival ownership never varies by entrance type. The exact-colour
            // proxy ends at the fully-underground chamber; the neutral proxy is
            // the sole owner of the drive from there to the allocated bay.
            bool surfaceOnlyArrival = arrival;
            Vector3 arrivalEnd = surfaceOnlyArrival ? portal : underground;
            float duration = CalculateTraversalDuration(
                lane,
                arrivalApproach,
                arrivalPortalControl,
                portal,
                arrivalEnd);

            PortalAnimationRequest request = arrival
                ? new PortalAnimationRequest(info, lane, arrivalApproach, portal, arrivalEnd, true, arrivalRotation, surfaceColor, facility.Id, vehicleId, facility.TargetBuildingId == 0, arrivalPortalControl, duration, tmpeControlledEntrance)
                : new PortalAnimationRequest(info, underground, arrivalApproach, portal, lane, false, departureRotation, surfaceColor, facility.Id, vehicleId, facility.TargetBuildingId == 0, arrivalPortalControl, duration);

            lock (Sync)
            {
                if (Pending.Count >= MaxQueuedAnimations
                    || BusyFacilities.Contains(facility.Id))
                    return false;
                BusyFacilities.Add(facility.Id);
                Pending.Enqueue(request);
            }

            if (arrival && _arrivalLogCount < 24)
            {
                _arrivalLogCount++;
                UndergroundParkingLog.Advanced("UPG visible vehicle arrival queued: facility="
                                            + facility.Id
                                            + " portalLane=arrival"
                                            + " offset="
                                            + portalSide.ToString("0.00")
                                            + " physicalPortalCentreline=True"
                                            + " model="
                                            + info.name);
            }
            else if (!arrival && _departureLogCount < 24)
            {
                _departureLogCount++;
                UndergroundParkingLog.Advanced("UPG visible vehicle departure queued: facility="
                                            + facility.Id
                                            + " vehicle="
                                            + vehicleId
                                            + " portalLane=departure"
                                            + " offset="
                                            + portalSide.ToString("0.00")
                                            + " physicalPortalCentreline=True"
                                            + " model="
                                            + info.name);
            }
            return true;
        }

        private static bool TryDequeue(out PortalAnimationRequest request)
        {
            lock (Sync)
            {
                if (Pending.Count == 0)
                {
                    request = default(PortalAnimationRequest);
                    return false;
                }

                request = Pending.Dequeue();
                return true;
            }
        }

        private static Vector3 NormalizeFlat(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude <= 0.001f)
                value = fallback;
            value.Normalize();
            return value;
        }

        private sealed class PortalAnimationDriver : MonoBehaviour
        {
            private readonly List<PortalAnimation> _active = new List<PortalAnimation>();
            public int LifecycleGeneration;

            private void Update()
            {
                HashSet<int> departureRepaths = null;
                lock (Sync)
                {
                    if (DepartureRepathFacilities.Count > 0)
                    {
                        departureRepaths = new HashSet<int>(
                            DepartureRepathFacilities);
                        DepartureRepathFacilities.Clear();
                    }
                }

                if (departureRepaths != null)
                {
                    for (int i = _active.Count - 1; i >= 0; i--)
                    {
                        PortalAnimation animation = _active[i];
                        if (animation.Arrival
                            || !departureRepaths.Contains(animation.FacilityId))
                            continue;

                        Object.Destroy(animation.Root);
                        ReleaseAnimationFacility(animation);
                        UndergroundParkingOccupancyHarmony
                            .RestartManagedDepartureAnimation(
                                animation.VehicleId);
                        _active.RemoveAt(i);
                    }
                }

                PortalAnimationRequest request;
                while (TryDequeue(out request))
                {
                    if (departureRepaths != null
                        && !request.Arrival
                        && departureRepaths.Contains(request.FacilityId))
                    {
                        ReleaseFacility(request.FacilityId, false);
                        UndergroundParkingOccupancyHarmony
                            .RestartManagedDepartureAnimation(
                                request.VehicleId);
                        continue;
                    }
                    PortalAnimation animation = CreateAnimation(request);
                    if (animation != null)
                        _active.Add(animation);
                    else
                    {
                        ReleaseFacility(request.FacilityId, request.Arrival);
                        AbortRequest(request);
                    }
                }

                // Portal ownership is part of the simulated traffic lifecycle,
                // so its 1.8-second traversal must advance on the same clock as
                // the road vehicles feeding it. Using unscaled real time made
                // the portal three-to-four times too slow at higher game speed:
                // every following car then waited with frozen simulation frames
                // until promotion and its render interpolation wrapped back to
                // the stored stop. Vanilla exposes the exact render-time scale
                // used by its moving world through m_simulationTimeSpeed.
                SimulationManager simulationManager = SimulationManager.instance;
                float simulationTimeSpeed = simulationManager == null
                    ? 1f
                    : Mathf.Max(0f, simulationManager.m_simulationTimeSpeed);
                float delta = Mathf.Max(
                    0f,
                    Time.unscaledDeltaTime * simulationTimeSpeed);
                UpdateArrivalAdmissionCooldowns(delta);
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    PortalAnimation animation = _active[i];
                    if (animation.WaitingForArrivalHandoff)
                    {
                        // The simulation action which removes the native body
                        // can wait behind several frames of queued work. Keep
                        // observing the exact vanilla render pose until that
                        // removal is acknowledged; a one-time sample taken when
                        // the request was queued becomes visibly stale for
                        // promoted queue heads, especially motorcycles.
                        if (animation.ControlledTmpeEntrance)
                        {
                            UndergroundParkingOccupancyHarmony
                                .ObserveRoutedArrivalRenderPose(
                                    animation.VehicleId);
                            UndergroundParkingOccupancyHarmony
                                .RequestRoutedArrivalNativeUnspawn(
                                    animation.VehicleId);
                        }
                        ushort parkedId;
                        int signal = UndergroundParkingOccupancyHarmony.ConsumeRoutedArrivalAnimationSignal(
                            animation.VehicleId,
                            out parkedId);
                        if (signal == 0)
                            continue;
                        if (signal < 0)
                        {
                            Object.Destroy(animation.Root);
                            ReleaseAnimationFacility(animation);
                            _active.RemoveAt(i);
                            continue;
                        }
                        animation.WaitingForArrivalHandoff = false;
                        animation.ParkedId = parkedId;
                        Vector3 exactHandoffPosition;
                        Quaternion exactHandoffRotation;
                        if (animation.ControlledTmpeEntrance
                            && UndergroundParkingOccupancyHarmony
                                .TryGetRoutedArrivalHandoffPose(
                                    animation.VehicleId,
                                    out exactHandoffPosition,
                                    out exactHandoffRotation))
                        {
                            RebaseControlledArrivalPath(
                                animation,
                                exactHandoffPosition,
                                exactHandoffRotation);
                        }
                        // The simulation thread has removed the exact real body
                        // from the world. For a controlled TM:PE arrival the
                        // rebase uses the final vanilla render-frame pose
                        // observed while the native body remained spawned,
                        // not an earlier request-time sample or a later
                        // simulation interpolation. Ordinary
                        // arrivals have retired the car; the TM:PE controlled
                        // path retains its created record and occupants until
                        // endpoint commit. The proxy is now the sole visible
                        // identity.
                        animation.Root.transform.position = animation.Start;
                        animation.Root.transform.rotation = animation.InitialRotation;
                        animation.Root.transform.localScale = Vector3.one;
                        ApplyPresentationColor(animation);
                        animation.Renderer.enabled =
                            IsPortalAnimationVisible(animation);
                        UndergroundParkingLifecycleDiagnostics.TraceVehicle(
                            animation.VehicleId,
                            "arrival-proxy-exposed-after-real-despawn",
                            animation.FacilityId,
                            "position=" + UndergroundParkingLifecycleDiagnostics.FormatPosition(animation.Start));
                        if (_startedLogCount++ < 24)
                            UndergroundParkingLog.Advanced("UPG visible vehicle animation started: facility="
                                                        + animation.FacilityId
                                                        + " arrival=True vehicle="
                                                        + animation.VehicleId
                                                        + " handoff=after-real-despawn");
                        continue;
                    }

                    animation.Renderer.enabled =
                        IsPortalAnimationVisible(animation);
                    if (animation.Renderer.enabled)
                        ApplyPresentationColor(animation);
                    if (animation.Arrival
                        && animation.ControlledTmpeEntrance
                        && animation.CompletionRequested)
                    {
                        ushort completedParkedId;
                        int completionSignal =
                            UndergroundParkingOccupancyHarmony
                                .ConsumeRoutedArrivalAnimationSignal(
                                    animation.VehicleId,
                                    out completedParkedId);
                        if (completionSignal == 0)
                            continue;
                        if (completionSignal < 0)
                        {
                            Object.Destroy(animation.Root);
                            ReleaseAnimationFacility(animation);
                            _active.RemoveAt(i);
                            continue;
                        }
                        animation.ParkedId = completedParkedId;
                        if (UndergroundParkingVisualManager.TryStartInternalParkingJourney(
                                completedParkedId,
                                animation.FacilityId,
                                animation.Info,
                                animation.End))
                        {
                            Object.Destroy(animation.Root);
                            ReleaseAnimationFacility(animation);
                            _active.RemoveAt(i);
                            continue;
                        }
                        UndergroundParkingOccupancyHarmony
                            .CompleteRoutedArrivalAnimation(
                                completedParkedId);
                    }

                    animation.Elapsed += delta;
                    float t = Mathf.Clamp01(animation.Elapsed / animation.Duration);
                    // The native road car is already stopped at this exact
                    // pose. Arrival must leave on the first animation update;
                    // smoothstep's zero initial derivative creates a visible
                    // second stop after the handoff. Linear path time gives a
                    // prompt, continuous pull into the portal. Departures keep
                    // their accepted eased presentation.
                    float smooth = animation.Arrival
                        ? t
                        : t * t * (3f - 2f * t);
                    Vector3 position = EvaluatePath(animation, smooth);
                    animation.Root.transform.position = position;

                    Vector3 ahead = EvaluatePath(animation, Mathf.Clamp01(smooth + 0.025f));
                    Vector3 direction = ahead - position;
                    if (direction.sqrMagnitude > 0.001f)
                    {
                        // A near-downward direction makes LookRotation's yaw
                        // underdetermined and allowed the car to roll around
                        // toward the opposite heading. Keep yaw owned by the
                        // horizontal path tangent and add only its actual ramp
                        // pitch around the vehicle's local right axis.
                        Vector3 horizontalDirection = direction;
                        horizontalDirection.y = 0f;
                        if (horizontalDirection.sqrMagnitude > 0.0001f)
                        {
                            Quaternion yaw = Quaternion.LookRotation(
                                horizontalDirection.normalized,
                                Vector3.up);
                            float pitch = Mathf.Atan2(
                                -direction.y,
                                horizontalDirection.magnitude) * Mathf.Rad2Deg;
                            animation.Root.transform.rotation =
                                yaw * Quaternion.AngleAxis(pitch, Vector3.right);
                        }
                    }

                    if (animation.Arrival)
                    {
                        // This short owner represents the exact surface car and
                        // remains full size until its centre is safely below the
                        // garage roof. The separate x-ray owner begins only at
                        // that endpoint and supplies the neutral-grey journey.
                        animation.Root.transform.localScale = Vector3.one;
                    }
                    else
                    {
                        // Run the accepted full arrival presentation in exact
                        // reverse: emerge from the underground endpoint, fade
                        // in below the apron, rise through the P, then remain
                        // fully visible for the complete apron-to-road curve.
                        float portalFade = Mathf.InverseLerp(0f, 0.28f, t);
                        animation.Root.transform.localScale =
                            Vector3.one * Mathf.Clamp01(portalFade);
                    }

                    if (t < 1f)
                        continue;

                    if (!animation.CompletionRequested)
                    {
                        animation.CompletionRequested = true;
                        if (animation.Arrival)
                            ScheduleArrivalAdmissionRelease(animation);
                        if (_completedLogCount++ < 24)
                            UndergroundParkingLog.Advanced("UPG visible vehicle animation completed: facility="
                                                        + animation.FacilityId
                                                        + " arrival="
                                                        + animation.Arrival
                                                        + " vehicle="
                                                        + animation.VehicleId);

                        UndergroundParkingLifecycleDiagnostics.TraceVehicle(
                            animation.VehicleId,
                            animation.Arrival
                                ? "arrival-proxy-animation-completed"
                                : "retrieval-proxy-animation-completed",
                            animation.FacilityId,
                            "position=" + UndergroundParkingLifecycleDiagnostics.FormatPosition(animation.End));

                        CompleteDeparture(animation.VehicleId);
                        if (animation.Arrival
                            && animation.ControlledTmpeEntrance)
                        {
                            UndergroundParkingOccupancyHarmony
                                .RequestRoutedArrivalCommitAtTransfer(
                                    animation.VehicleId);
                            continue;
                        }
                        if (animation.Arrival)
                        {
                            if (UndergroundParkingVisualManager.TryStartInternalParkingJourney(
                                    animation.ParkedId,
                                    animation.FacilityId,
                                    animation.Info,
                                    animation.End))
                            {
                                Object.Destroy(animation.Root);
                                ReleaseAnimationFacility(animation);
                                _active.RemoveAt(i);
                                continue;
                            }
                            UndergroundParkingOccupancyHarmony.CompleteRoutedArrivalAnimation(
                                animation.ParkedId);
                        }
                    }

                    // Retain the proxy at the exact handoff while the existing
                    // simulation-side lane-space authority waits. Retire it only
                    // after the initialized real car has spawned at that pose.
                    if (!animation.Arrival
                        && UndergroundParkingOccupancyHarmony.IsManagedDepartureReleasePending(
                            animation.VehicleId))
                    {
                        continue;
                    }

                    Object.Destroy(animation.Root);
                    ReleaseAnimationFacility(animation);
                    _active.RemoveAt(i);
                }
            }

            private static void ApplyPresentationColor(PortalAnimation animation)
            {
                if (animation == null
                    || animation.Renderer == null
                    || animation.Arrival
                    || !animation.ControlledTmpeEntrance)
                    return;

                InfoManager manager = InfoManager.instance;
                bool transportHighlight = manager != null
                                          && manager.CurrentMode
                                          == InfoManager.InfoMode.Transport;
                if (animation.PresentationColorInitialized
                    && animation.TransportHighlightApplied
                    == transportHighlight)
                    return;

                animation.ColorProperties.SetColor(
                    "_Color",
                    transportHighlight ? ParkingBlue : animation.SurfaceColor);
                animation.Renderer.SetPropertyBlock(animation.ColorProperties);
                animation.PresentationColorInitialized = true;
                animation.TransportHighlightApplied = transportHighlight;
            }

            private static bool IsPortalAnimationVisible(
                PortalAnimation animation)
            {
                if (animation == null
                    || animation.Root == null
                    || animation.Renderer == null)
                {
                    return false;
                }

                Camera camera = null;
                CameraController cameraController = ToolsModifierControl.cameraController;
                if (cameraController != null)
                    camera = cameraController.m_camera;
                if (camera == null)
                    camera = Camera.main;
                if (camera == null)
                    return false;

                Vector3 position = animation.Root.transform.position;
                if (Mathf.Abs(camera.transform.position.y - position.y)
                    > MaximumAnimationCameraHeight)
                {
                    return false;
                }

                Vector3 viewport = camera.WorldToViewportPoint(position);
                return viewport.z > 0f
                       && viewport.x >= -0.08f
                       && viewport.x <= 1.08f
                       && viewport.y >= -0.08f
                       && viewport.y <= 1.08f;
            }

            private static void RebaseControlledArrivalPath(
                PortalAnimation animation,
                Vector3 exactPosition,
                Quaternion exactRotation)
            {
                Vector3 previousStart = animation.Start;
                Vector3 laneDirection = NormalizeFlat(
                    animation.Approach - previousStart,
                    exactRotation * Vector3.forward);
                animation.Start = exactPosition;
                animation.InitialRotation = exactRotation;

                Vector3 entranceDirection = NormalizeFlat(
                    animation.Portal - animation.PortalControl,
                    animation.Portal - exactPosition);
                BuildPerpendicularEntranceControls(
                    exactPosition,
                    animation.Portal,
                    laneDirection,
                    entranceDirection,
                    out animation.Approach,
                    out animation.PortalControl);
                Vector3 surfaceDelta = animation.Portal - exactPosition;
                surfaceDelta.y = 0f;
                float projectedDistance = Vector3.Dot(
                    surfaceDelta,
                    laneDirection);

                animation.Duration = CalculateTraversalDuration(
                    animation.Start,
                    animation.Approach,
                    animation.PortalControl,
                    animation.Portal,
                    animation.End);

                if (_handoffRebaseLogCount++ < 64)
                {
                    Vector3 rebaseDelta = exactPosition - previousStart;
                    UndergroundParkingLog.Advanced(
                        "UPG controlled arrival path rebased at last rendered pose: vehicle="
                        + animation.VehicleId
                        + " facility="
                        + animation.FacilityId
                        + " delta="
                        + rebaseDelta.magnitude.ToString("0.000")
                        + " projectedToPortal="
                        + projectedDistance.ToString("0.000")
                        + " controls=perpendicular-entry");
                }
            }

            private static PortalAnimation CreateAnimation(PortalAnimationRequest request)
            {
                if (_root == null || request.Info == null || request.Info.m_mesh == null || request.Info.m_material == null)
                    return null;

                GameObject visual = new GameObject(request.Arrival ? "UPG vehicle arrival" : "UPG vehicle departure");
                visual.transform.parent = _root.transform;
                visual.transform.position = request.Start;
                visual.transform.rotation = request.InitialRotation;
                // A newly created renderer must never get one frame at full
                // scale before Update applies the current visibility phase.
                visual.transform.localScale = Vector3.zero;

                MeshFilter filter = visual.AddComponent<MeshFilter>();
                filter.sharedMesh = request.Info.m_mesh;
                MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = request.Info.m_material;
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                properties.SetColor(
                    "_Color",
                    request.SurfaceColor);
                renderer.SetPropertyBlock(properties);
                bool waitingForArrivalHandoff = request.Arrival && request.VehicleId != 0;
                renderer.enabled = false;

                PortalAnimation animation = new PortalAnimation(
                    visual,
                    request.Start,
                    request.Approach,
                    request.Portal,
                    request.End,
                    request.Arrival,
                    request.FacilityId,
                    request.VehicleId,
                    renderer,
                    request.PortalControl,
                    request.ShowSurfacePortal,
                    request.Duration,
                    request.ControlledTmpeEntrance,
                    request.SurfaceColor,
                    properties,
                    request.Info);
                if (!waitingForArrivalHandoff)
                    renderer.enabled = IsPortalAnimationVisible(animation);
                if (request.Arrival && request.VehicleId != 0)
                    UndergroundParkingOccupancyHarmony.MarkRoutedArrivalAnimationReady(request.VehicleId);
                if (!waitingForArrivalHandoff && _startedLogCount++ < 24)
                    UndergroundParkingLog.Advanced("UPG visible vehicle animation started: facility="
                                                + request.FacilityId
                                                + " arrival="
                                                + request.Arrival
                                                + " model="
                                                + request.Info.name);
                if (!request.Arrival && request.VehicleId != 0)
                    UndergroundParkingLifecycleDiagnostics.TraceVehicle(
                        request.VehicleId,
                        "retrieval-proxy-animation-started",
                        request.FacilityId,
                        "position=" + UndergroundParkingLifecycleDiagnostics.FormatPosition(request.Start));
                return animation;
            }

            private static Vector3 EvaluatePath(PortalAnimation animation, float t)
            {
                if (!animation.Arrival)
                {
                    if ((animation.Start - animation.Portal).sqrMagnitude <= 0.001f)
                    {
                        return EvaluateArrivalTraversalPath(
                            animation.End,
                            animation.Approach,
                            animation.PortalControl,
                            animation.Portal,
                            animation.Portal,
                            1f - t);
                    }
                    // Exact reverse of the arrival route below: drive up the
                    // sloped ramp, then follow the same ordered apron cubic
                    // backwards from the P to the road pose.
                    if (t <= 0.55f)
                        return EvaluateRamp(
                            animation.Portal,
                            animation.Start,
                            1f - t / 0.55f);

                    float approachT = (t - 0.55f) / 0.45f;
                    return EvaluateArrivalTraversalPath(
                        animation.End,
                        animation.Approach,
                        animation.PortalControl,
                        animation.Portal,
                        animation.Portal,
                        1f - approachT);
                }

                // The live road car ends on the connected lane. The visual
                // proxy owns the collision-free entrance movement: first turn
                // from that lane to the surface portal, then drive down one
                // inward sloped ramp to the underground endpoint. It can never
                // draw a straight above-ground chord through the host building.
                return EvaluateArrivalTraversalPath(
                    animation.Start,
                    animation.Approach,
                    animation.PortalControl,
                    animation.Portal,
                    animation.End,
                    t);
            }

            private void OnDestroy()
            {
                bool ownsCurrentLifecycle =
                    LifecycleGeneration == _lifecycleGeneration;
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].Root != null)
                        Object.Destroy(_active[i].Root);
                    if (!ownsCurrentLifecycle)
                        continue;

                    ReleaseAnimationFacility(_active[i]);
                    if (_active[i].Arrival)
                    {
                        UndergroundParkingOccupancyHarmony.CompleteRoutedArrivalAnimation(
                            _active[i].ParkedId);
                        UndergroundParkingOccupancyHarmony.AbortRoutedArrivalAnimation(
                            _active[i].VehicleId);
                    }
                    else
                        CompleteDeparture(_active[i].VehicleId);
                }
                _active.Clear();
            }
        }

        private struct PortalAnimationRequest
        {
            public readonly VehicleInfo Info;
            public readonly Vector3 Start;
            public readonly Vector3 Approach;
            public readonly Vector3 Portal;
            public readonly Vector3 End;
            public readonly bool Arrival;
            public readonly Quaternion InitialRotation;
            public readonly Color SurfaceColor;
            public readonly int FacilityId;
            public readonly ushort VehicleId;
            public readonly bool ShowSurfacePortal;
            public readonly Vector3 PortalControl;
            public readonly float Duration;
            public readonly bool ControlledTmpeEntrance;

            public PortalAnimationRequest(
                VehicleInfo info,
                Vector3 start,
                Vector3 approach,
                Vector3 portal,
                Vector3 end,
                bool arrival,
                Quaternion initialRotation,
                Color surfaceColor,
                int facilityId,
                ushort vehicleId,
                bool showSurfacePortal,
                Vector3 portalControl,
                float duration,
                bool controlledTmpeEntrance = false)
            {
                Info = info;
                Start = start;
                Approach = approach;
                Portal = portal;
                End = end;
                Arrival = arrival;
                InitialRotation = initialRotation;
                SurfaceColor = surfaceColor;
                FacilityId = facilityId;
                VehicleId = vehicleId;
                ShowSurfacePortal = showSurfacePortal;
                PortalControl = portalControl;
                Duration = duration;
                ControlledTmpeEntrance = controlledTmpeEntrance;
            }
        }

        private sealed class PortalAnimation
        {
            public readonly GameObject Root;
            public Vector3 Start;
            public Vector3 Approach;
            public readonly Vector3 Portal;
            public readonly Vector3 End;
            public readonly bool Arrival;
            public Quaternion InitialRotation;
            public readonly int FacilityId;
            public readonly ushort VehicleId;
            public readonly MeshRenderer Renderer;
            public Vector3 PortalControl;
            public readonly bool ShowSurfacePortal;
            public float Duration;
            public readonly bool ControlledTmpeEntrance;
            public readonly Color SurfaceColor;
            public readonly MaterialPropertyBlock ColorProperties;
            public readonly VehicleInfo Info;
            public float Elapsed;
            public bool WaitingForArrivalHandoff;
            public ushort ParkedId;
            public bool CompletionRequested;
            public bool PresentationColorInitialized;
            public bool TransportHighlightApplied;
            public bool EntranceAdmissionReleaseScheduled;

            public PortalAnimation(
                GameObject root,
                Vector3 start,
                Vector3 approach,
                Vector3 portal,
                Vector3 end,
                bool arrival,
                int facilityId,
                ushort vehicleId,
                MeshRenderer renderer,
                Vector3 portalControl,
                bool showSurfacePortal,
                float duration,
                bool controlledTmpeEntrance,
                Color surfaceColor,
                MaterialPropertyBlock colorProperties,
                VehicleInfo info)
            {
                Root = root;
                Start = start;
                Approach = approach;
                Portal = portal;
                End = end;
                Arrival = arrival;
                InitialRotation = root == null ? Quaternion.identity : root.transform.rotation;
                FacilityId = facilityId;
                VehicleId = vehicleId;
                Renderer = renderer;
                PortalControl = portalControl;
                ShowSurfacePortal = showSurfacePortal;
                Duration = duration;
                ControlledTmpeEntrance = controlledTmpeEntrance;
                SurfaceColor = surfaceColor;
                ColorProperties = colorProperties;
                Info = info;
                Elapsed = 0f;
                WaitingForArrivalHandoff = arrival && vehicleId != 0;
                ParkedId = 0;
                CompletionRequested = false;
                PresentationColorInitialized = false;
                TransportHighlightApplied = false;
                EntranceAdmissionReleaseScheduled = false;
            }
        }

        private static void ScheduleArrivalAdmissionRelease(
            PortalAnimation animation)
        {
            if (animation == null
                || !animation.Arrival
                || animation.EntranceAdmissionReleaseScheduled)
            {
                return;
            }

            animation.EntranceAdmissionReleaseScheduled = true;
            lock (Sync)
            {
                ArrivalAdmissionCooldowns[animation.FacilityId] =
                    ArrivalEntranceClearanceDelay;
            }
        }

        private static void UpdateArrivalAdmissionCooldowns(float delta)
        {
            if (delta <= 0f)
                return;

            lock (Sync)
            {
                ArrivalAdmissionCooldownFacilityBuffer.Clear();
                foreach (KeyValuePair<int, float> cooldown in ArrivalAdmissionCooldowns)
                    ArrivalAdmissionCooldownFacilityBuffer.Add(cooldown.Key);

                for (int i = 0;
                     i < ArrivalAdmissionCooldownFacilityBuffer.Count;
                     i++)
                {
                    int facilityId = ArrivalAdmissionCooldownFacilityBuffer[i];
                    float remaining = ArrivalAdmissionCooldowns[facilityId] - delta;
                    if (remaining > 0f)
                    {
                        ArrivalAdmissionCooldowns[facilityId] = remaining;
                        continue;
                    }

                    ArrivalAdmissionCooldowns.Remove(facilityId);
                    BusyFacilities.Remove(facilityId);
                }
                ArrivalAdmissionCooldownFacilityBuffer.Clear();
            }
        }

        private static void ReleaseAnimationFacility(PortalAnimation animation)
        {
            if (animation == null || animation.EntranceAdmissionReleaseScheduled)
                return;
            ReleaseFacility(animation.FacilityId, animation.Arrival);
        }

        private static void CompleteDeparture(ushort vehicleId)
        {
            if (vehicleId != 0)
                UndergroundParkingOccupancyHarmony.CompleteManagedDepartureAnimation(vehicleId);
        }

        private static void AbortRequest(PortalAnimationRequest request)
        {
            if (request.Arrival)
                UndergroundParkingOccupancyHarmony.AbortRoutedArrivalAnimation(request.VehicleId);
            else
                CompleteDeparture(request.VehicleId);
        }

        private static void ReleaseFacility(int facilityId, bool arrival)
        {
            if (facilityId <= 0)
                return;

            lock (Sync)
            {
                if (arrival && ArrivalAdmissionCooldowns.ContainsKey(facilityId))
                    return;
                BusyFacilities.Remove(facilityId);
            }
        }
    }
}
