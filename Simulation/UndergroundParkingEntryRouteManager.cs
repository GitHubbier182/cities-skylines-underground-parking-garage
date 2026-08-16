using ColossalFramework;
using ColossalFramework.Math;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UndergroundParkingGarage
{
    // Narrow long-stay arrival route armed only by PassengerCarAI's authoritative
    // parking phase. It creates an ordinary road path ending on the entrance's
    // exact connected lane without replacing the preceding through-journey.
    internal static class UndergroundParkingEntryRouteManager
    {
        private const float StartSearchRadius = 128f;
        private const float MaxPathLength = 20000f;
        private const int LogLimit = 24;
        private const int AuthoritativeDecisionLogLimit = 512;
        // Large cities and low-throughput simulation can take substantially more
        // than the old 32,768-frame window to finish a legitimate cross-city
        // journey. Stale state is now cleared by path replacement and vehicle
        // release, so a longer bound no longer risks vehicle-ID reuse claims.
        private const uint RouteLifetimeFrames = 262144u;
        private static int _logCount;
        private static int _failureLogCount;
        private static int _outcomeLogCount;
        private static int _authoritativeAttemptLogCount;
        private static int _authoritativeRejectionLogCount;
        private static readonly DetailedStartPathFind DetailedPathBuilder = CreateDetailedPathBuilder();
        private static readonly GetDriverInstanceDelegate DriverInstanceGetter =
            CreateDriverInstanceGetter();
        private static readonly RoutedVehicle[] RoutedVehicles = new RoutedVehicle[65536];
        private static readonly bool[] RerouteRequired = new bool[65536];
        private static readonly object HighlightSync = new object();
        private static readonly Dictionary<ushort, RoutedVehicleHighlight>
            HighlightByVehicle = new Dictionary<ushort, RoutedVehicleHighlight>();
        private static volatile RoutedVehicleHighlight[] PublishedHighlights =
            new RoutedVehicleHighlight[0];
        private static readonly RoutedVehicleHighlight[] PublishedHighlightsByVehicle =
            new RoutedVehicleHighlight[65536];
        // Ordinary non-TM:PE route state only: 0 = no terminal and 2 = the
        // exact parking terminal reached. TM:PE compatibility owns its complete
        // candidate/prepared/adopted/stopped sequence in one separate
        // identity-bound transaction and never uses this array as parallel
        // arrival authority.
        private static readonly byte[] TerminalEntryStates = new byte[65536];

        public static bool TryStartEntryRoute(
            PassengerCarAI ai,
            ushort vehicleId,
            ref Vehicle vehicleData,
            uint nativeContinuationPath,
            int nativeContinuationPositionIndex,
            byte nativeContinuationOffset,
            out bool pathStarted)
        {
            return TryStartEntryRouteCore(
                ai,
                vehicleId,
                ref vehicleData,
                nativeContinuationPath,
                nativeContinuationPositionIndex,
                nativeContinuationOffset,
                -1f,
                0,
                out pathStarted);
        }

        private static bool TryStartEntryRouteCore(
            PassengerCarAI ai,
            ushort vehicleId,
            ref Vehicle vehicleData,
            uint nativeContinuationPath,
            int nativeContinuationPositionIndex,
            byte nativeContinuationOffset,
            float afterCandidateDistance,
            int afterCandidateFacilityId,
            out bool pathStarted)
        {
            pathStarted = false;
            if (ai == null || vehicleId == 0 || ai.m_info == null)
                return RejectAuthoritativeParking(
                    vehicleId,
                    "route-input-invalid",
                    "ai=" + (ai != null)
                    + " info=" + (ai != null && ai.m_info != null));
            if (RoutedVehicles[vehicleId].FacilityId > 0)
                return RejectAuthoritativeParking(
                    vehicleId,
                    "route-already-active",
                    "facility=" + RoutedVehicles[vehicleId].FacilityId);

            ushort driverInstance = DriverInstanceGetter == null
                ? (ushort)0
                : DriverInstanceGetter(ai, vehicleId, ref vehicleData);
            CitizenManager citizenManager = CitizenManager.instance;
            if (driverInstance == 0
                || citizenManager == null
                || driverInstance >= citizenManager.m_instances.m_size)
            {
                return RejectAuthoritativeParking(
                    vehicleId,
                    "driver-instance-unavailable",
                    "driverInstance=" + driverInstance
                    + " citizenManager=" + (citizenManager != null)
                    + " instanceCapacity="
                    + (citizenManager == null ? 0 : citizenManager.m_instances.m_size));
            }
            ref CitizenInstance driver =
                ref citizenManager.m_instances.m_buffer[driverInstance];
            if ((driver.m_flags & CitizenInstance.Flags.Created) == 0
                || driver.Info == null
                || !(driver.Info.m_citizenAI is HumanAI)
                || driver.m_citizen == 0u
                || driver.m_citizen >= citizenManager.m_citizens.m_size)
            {
                return RejectAuthoritativeParking(
                    vehicleId,
                    "driver-invalid",
                    "driverInstance=" + driverInstance
                    + " driverFlags=" + driver.m_flags
                    + " driverCitizen=" + driver.m_citizen
                    + " driverPath=" + driver.m_path
                    + " targetBuilding=" + driver.m_targetBuilding
                    + " driverInfo=" + (driver.Info == null ? "null" : driver.Info.name));
            }

            string occupantReason;
            string occupantDetail;
            if (!HasValidArrivalOccupants(
                    vehicleId,
                    ref vehicleData,
                    out occupantReason,
                    out occupantDetail))
            {
                return RejectAuthoritativeParking(
                    vehicleId,
                    occupantReason,
                    "driverInstance=" + driverInstance
                    + " driverPath=" + driver.m_path
                    + " targetBuilding=" + driver.m_targetBuilding
                    + " " + occupantDetail);
            }

            Vector3 destination;
            if (!TryGetDestination(
                    ai,
                    vehicleId,
                    ref driver,
                    out destination))
                return RejectAuthoritativeParking(
                    vehicleId,
                    "destination-unavailable",
                    "targetPos3=" + FormatVector(vehicleData.m_targetPos3)
                    + " driverTarget=" + driver.m_targetBuilding);

            if (UndergroundParkingOccupancyManager.IsAuthoritativeDriverReturningHome(
                    driver.m_citizen,
                    driver.m_targetBuilding,
                    (driver.m_flags & CitizenInstance.Flags.TargetIsNode) != 0,
                    destination))
            {
                return RejectAuthoritativeParking(
                    vehicleId,
                    "authoritative-driver-returning-home",
                    "driverCitizen=" + driver.m_citizen
                    + " driverTarget=" + driver.m_targetBuilding
                    + " destination=" + FormatVector(destination));
            }

            int facilityId;
            float facilityDistance;
            int attemptedFacilityCount = 0;
            while (UndergroundParkingOccupancyManager.TryFindParkingFacility(
                       destination,
                       afterCandidateDistance,
                       afterCandidateFacilityId,
                       out facilityId,
                       out facilityDistance))
            {
                // The occupancy manager returns the closest still-eligible
                // entrance not already attempted. A failed portal, handoff,
                // reservation or native road path advances to the next-nearest
                // candidate instead of abandoning the complete garage offer.
                afterCandidateDistance = facilityDistance;
                afterCandidateFacilityId = facilityId;
                attemptedFacilityCount++;

                UndergroundParkingFacility facility;
                UndergroundParkingRoadConnection connection;
            if (!UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                        facilityId, out facility, out connection))
                    continue;

                Vector3 pavementHandoff;
                uint pedestrianLaneId;
                int pedestrianLaneIndex;
                if (!UndergroundParkingOccupancyHarmony.TryResolvePavementHandoff(
                        facility,
                        connection,
                        out pavementHandoff,
                        out pedestrianLaneId,
                        out pedestrianLaneIndex)
                    || pedestrianLaneId == 0u
                    || pedestrianLaneIndex < 0)
                {
                    continue;
                }

                int reservedSlotIndex;
                if (!UndergroundParkingOccupancyManager.TryReserveRoutedArrivalSlot(
                        vehicleId, facilityId, out reservedSlotIndex))
                    continue;

                PathManager pathManager = PathManager.instance;
                SimulationManager simulationManager = SimulationManager.instance;
                VehicleInfo info = ai.m_info;
                if (pathManager == null || simulationManager == null || info.m_generatedInfo == null)
                {
                    LogFailure(vehicleId, facilityId, "path-services-missing", vehicleData.GetLastFramePosition(), connection.LanePosition);
                    UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(vehicleId, facilityId, reservedSlotIndex);
                    return false;
                }

                NetInfo.LaneType laneTypes = NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;
                PathUnit.Position startA;
                PathUnit.Position startB;
                float distanceA;
                float distanceB;
                Vector3 livePosition = vehicleData.GetLastFramePosition();
                if (!TryFindStartPosition(
                        livePosition,
                        ref vehicleData,
                        info,
                        laneTypes,
                        out startA,
                        out startB,
                        out distanceA,
                        out distanceB))
                {
                    // Incoming/private cars can begin at vanilla's outside-
                    // connection handoff. Reuse PassengerCarAI's builder for
                    // that source while supplying only this candidate portal.
                    // An ordinary city car must never enter this both-directions
                    // fallback: if its exact live road lane cannot be proved,
                    // leave this parking attempt wholly with vanilla instead of
                    // fabricating a source that can U-turn or approach against
                    // traffic.
                    bool outsideConnectionHandoff =
                        (vehicleData.m_flags
                         & (Vehicle.Flags.Importing | Vehicle.Flags.Exporting)) != 0;
                    if (!outsideConnectionHandoff)
                    {
                        LogFailure(
                            vehicleId,
                            facilityId,
                            "ordinary-live-road-source-unavailable",
                            livePosition,
                            connection.LanePosition);
                        UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(
                            vehicleId,
                            facilityId,
                            reservedSlotIndex);
                        return RejectAuthoritativeParking(
                            vehicleId,
                            "ordinary-live-road-source-unavailable",
                            "facility=" + facilityId
                            + " position=" + FormatVector(livePosition));
                    }

                    if (DetailedPathBuilder == null)
                    {
                        LogFailure(vehicleId, facilityId, "vanilla-detailed-hook-missing", livePosition, connection.LanePosition);
                        UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(vehicleId, facilityId, reservedSlotIndex);
                        return false;
                    }

                    pathStarted = DetailedPathBuilder(
                        ai,
                        vehicleId,
                        ref vehicleData,
                        livePosition,
                        connection.LanePosition,
                        true,
                        false,
                        false);
                    if (!pathStarted)
                    {
                        LogFailure(vehicleId, facilityId, "vanilla-detailed-path-failed", livePosition, connection.LanePosition);
                        UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(vehicleId, facilityId, reservedSlotIndex);
                        continue;
                    }

                    RegisterRoute(
                        vehicleId,
                        facilityId,
                        reservedSlotIndex,
                        vehicleData.m_path,
                        nativeContinuationPath,
                        nativeContinuationPositionIndex,
                        nativeContinuationOffset,
                        facilityDistance,
                        ref vehicleData);
                    LogSuccess(vehicleId, facilityId, connection, "authoritative-vanilla-detailed");
                    return true;
                }

                if (distanceA < 10f)
                    startB = default(PathUnit.Position);

                PathUnit.Position endA = new PathUnit.Position
                {
                    m_segment = connection.SegmentId,
                    m_lane = connection.LaneIndex,
                    m_offset = connection.SegmentOffset
                };
                PathUnit.Position endB = default(PathUnit.Position);
                uint path;
                if (!pathManager.CreatePath(
                        out path,
                        ref simulationManager.m_randomizer,
                        simulationManager.m_currentBuildIndex,
                        startA,
                        startB,
                        endA,
                        endB,
                        laneTypes,
                        info.m_vehicleType,
                        info.vehicleCategory,
                        MaxPathLength,
                        false,
                        false,
                        false,
                        false))
                {
                    LogFailure(vehicleId, facilityId, "create-path-failed", livePosition, connection.LanePosition);
                    UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(vehicleId, facilityId, reservedSlotIndex);
                    continue;
                }

                if (vehicleData.m_path != 0)
                    pathManager.ReleasePath(vehicleData.m_path);
                vehicleData.m_path = path;
                vehicleData.m_flags |= Vehicle.Flags.WaitingPath;
                pathStarted = true;

                RegisterRoute(
                    vehicleId,
                    facilityId,
                    reservedSlotIndex,
                    vehicleData.m_path,
                    nativeContinuationPath,
                    nativeContinuationPositionIndex,
                    nativeContinuationOffset,
                    facilityDistance,
                    ref vehicleData);
                LogSuccess(vehicleId, facilityId, connection, "authoritative-exact-lane");
                return true;
            }

            return RejectAuthoritativeParking(
                vehicleId,
                attemptedFacilityCount == 0
                    ? "garage-route-target-unavailable"
                    : "garage-route-candidates-exhausted",
                "destination=" + FormatVector(destination)
                + " driverTarget=" + driver.m_targetBuilding
                + " attemptedFacilities=" + attemptedFacilityCount);
        }

        public static bool TryBeginArrival(
            ushort vehicleId,
            ref Vehicle vehicleData,
            Vector3 position,
            out int facilityId)
        {
            facilityId = 0;
            if (vehicleId == 0)
                return false;

            uint frame = SimulationManager.instance == null ? 0u : SimulationManager.instance.m_currentFrameIndex;
            RoutedVehicle route = RoutedVehicles[vehicleId];
            if (route.FacilityId <= 0)
                return false;

            bool tmpeArrival = TmpeParkingCompatibilityManager.IsAdoptedArrival(
                vehicleId);
            if (tmpeArrival
                ? !TmpeParkingCompatibilityManager.IsStoppedArrival(vehicleId)
                : TerminalEntryStates[vehicleId] != 2)
                return false;

            if (!tmpeArrival && !route.IsValid(frame))
            {
                FailArrival(vehicleId, "expired");
                return false;
            }

            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            if (!UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                    route.FacilityId, out facility, out connection))
            {
                FailArrival(vehicleId, "connection-lost");
                return false;
            }

            if (!tmpeArrival
                && route.PathId != 0
                && vehicleData.m_path != 0
                && route.PathId != vehicleData.m_path)
            {
                FailArrival(vehicleId, "path-replaced");
                return false;
            }

            if (tmpeArrival
                && !TmpeParkingCompatibilityManager.TryGetArrivalConnection(
                    vehicleId,
                    out connection))
                return false;

            // Candidate selection and an accepted TM:PE ParkVehicle transaction
            // prove identity and intent only. The active road car must still
            // reach the exact captured lane, direction and portal stop before
            // UPG can take over its visible entrance movement. This preserves
            // native collision and traffic-signal ownership for the complete
            // road approach in both compatibility and ordinary arrivals.
            if (!UndergroundParkingOccupancyManager.IsAtRoadConnectionPortal(
                    connection,
                    position,
                    vehicleData.GetLastFrameData().m_rotation * Vector3.forward))
                return false;

            facilityId = route.FacilityId;
            LogOutcome(vehicleId, route.FacilityId, "portal-reached");
            return true;
        }

        public static bool TryHoldRoadPortalArrival(
            ushort vehicleId,
            ref Vehicle vehicleData,
            Vector3 position,
            out int facilityId)
        {
            if (!TryProbeRoadPortalArrival(
                    vehicleId,
                    ref vehicleData,
                    position,
                    out facilityId))
                return false;

            if (TmpeParkingCompatibilityManager.IsAdoptedArrival(vehicleId))
            {
                return TmpeParkingCompatibilityManager.TryMarkStopped(
                    vehicleId,
                    position,
                    vehicleData.GetLastFrameData().m_rotation * Vector3.forward,
                    out facilityId);
            }

            TerminalEntryStates[vehicleId] = 2;
            return true;
        }

        public static bool TryProbeRoadPortalArrival(
            ushort vehicleId,
            ref Vehicle vehicleData,
            Vector3 position,
            out int facilityId)
        {
            facilityId = 0;
            if (vehicleId == 0)
                return false;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            if (route.FacilityId <= 0)
                return false;

            Vector3 forward =
                vehicleData.GetLastFrameData().m_rotation * Vector3.forward;
            if (TmpeParkingCompatibilityManager.IsAdoptedArrival(vehicleId))
            {
                UndergroundParkingRoadConnection connection;
                if (!TmpeParkingCompatibilityManager.TryGetArrivalConnection(
                        vehicleId,
                        out connection)
                    || !UndergroundParkingOccupancyManager
                        .IsAtRoadConnectionPortal(
                            connection,
                            position,
                            forward))
                    return false;
            }
            else if (!UndergroundParkingOccupancyManager.IsAtFacilityPortal(
                         route.FacilityId,
                         position,
                         forward))
            {
                return false;
            }

            facilityId = route.FacilityId;
            return true;
        }

        public static bool TryPrepareTmpePreselectedArrival(
            ushort vehicleId,
            ref Vehicle vehicleData,
            TmpeParkingCandidate candidate,
            PathUnit.Position terminalPosition,
            uint nativeContinuationPath,
            int nativeContinuationPositionIndex,
            byte nativeContinuationOffset,
            out UndergroundParkingRoadConnection arrivalConnection)
        {
            arrivalConnection = default(UndergroundParkingRoadConnection);
            if (vehicleId == 0
                || candidate.FacilityId <= 0
                || candidate.SlotIndex < 0
                || candidate.SegmentId == 0
                || terminalPosition.m_segment != candidate.SegmentId
                || RoutedVehicles[vehicleId].FacilityId > 0)
                return false;

            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            Vector3 undergroundPosition;
            Quaternion undergroundRotation;
            Vector3 approachDirection;
            if (!UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                    candidate.FacilityId,
                    out facility,
                    out connection)
                || connection.SegmentId != candidate.SegmentId
                || !TryGetApproachDirection(
                    ref vehicleData,
                    connection,
                    out approachDirection)
                || !UndergroundParkingAccessManager
                    .TryGetArrivalConnectionBeforeEntrance(
                        facility,
                        terminalPosition.m_lane,
                        connection,
                        TmpeParkingCompatibilityManager.GetEntranceHandoffDistance(
                            vehicleData.Info),
                        approachDirection,
                        out arrivalConnection)
                || !UndergroundParkingOccupancyManager
                    .TryGetRoutedArrivalReservationPose(
                        vehicleId,
                        candidate.FacilityId,
                        candidate.SlotIndex,
                        out undergroundPosition,
                        out undergroundRotation))
                return false;

            RegisterRoute(
                vehicleId,
                candidate.FacilityId,
                candidate.SlotIndex,
                vehicleData.m_path,
                nativeContinuationPath,
                nativeContinuationPositionIndex,
                nativeContinuationOffset,
                0f,
                ref vehicleData);
            LogOutcome(
                vehicleId,
                candidate.FacilityId,
                "tmpe-native-route-prepared-for-actual-lane-portal");
            return true;
        }

        private static bool TryGetApproachDirection(
            ref Vehicle vehicleData,
            UndergroundParkingRoadConnection entranceConnection,
            out Vector3 direction)
        {
            direction = entranceConnection.LanePosition
                        - vehicleData.GetLastFramePosition();
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = vehicleData.GetLastFrameData().m_rotation
                            * Vector3.forward;
                direction.y = 0f;
            }
            if (direction.sqrMagnitude <= 0.001f)
                return false;
            direction.Normalize();
            return true;
        }

        public static bool HasActiveRoute(ushort vehicleId)
        {
            if (vehicleId == 0)
                return false;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            if (route.FacilityId <= 0)
                return false;

            uint frame = SimulationManager.instance == null
                ? 0u
                : SimulationManager.instance.m_currentFrameIndex;
            byte preparedOffset;
            if (TmpeParkingCompatibilityManager.TryGetPreparedArrivalOffset(
                    vehicleId,
                    out preparedOffset))
            {
                // Once TM:PE has created the parked identity and deferred walk,
                // rolling the active road car back through native release is
                // no longer safe. Keep its exact slot alive for the complete
                // portal journey. If external state has already removed that
                // reservation, remain fail-closed with occupants in the car;
                // never turn expiry into a carriageway unload.
                UndergroundParkingOccupancyManager.RenewRoutedArrivalSlot(
                    vehicleId,
                    route.FacilityId,
                    route.SlotIndex);
                return true;
            }
            if (route.IsValid(frame))
                return true;

            ClearRoute(vehicleId, "expired-before-parking", true);
            return false;
        }

        public static bool IsTmpeAdoptedArrival(ushort vehicleId)
        {
            return vehicleId != 0
                   && RoutedVehicles[vehicleId].FacilityId > 0
                   && TmpeParkingCompatibilityManager.IsAdoptedArrival(vehicleId);
        }

        public static bool TryRepathAdoptedArrival(
            ushort vehicleId,
            UndergroundParkingRoadConnection connection)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            PathManager pathManager = PathManager.instance;
            SimulationManager simulationManager = SimulationManager.instance;
            if (vehicleId == 0
                || !connection.IsValid
                || vehicleManager == null
                || pathManager == null
                || simulationManager == null
                || vehicleId >= vehicleManager.m_vehicles.m_size)
                return false;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            ref Vehicle vehicleData =
                ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            VehicleInfo info = vehicleData.Info;
            if (route.FacilityId <= 0
                || route.FacilityId != connection.FacilityId
                || (vehicleData.m_flags & Vehicle.Flags.Created) == 0
                || info == null
                || !(info.m_vehicleAI is PassengerCarAI))
                return false;

            NetInfo.LaneType laneTypes =
                NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;
            PathUnit.Position startA;
            PathUnit.Position startB;
            float distanceA;
            float distanceB;
            Vector3 livePosition = vehicleData.GetLastFramePosition();
            if (!TryFindStartPosition(
                    livePosition,
                    ref vehicleData,
                    info,
                    laneTypes,
                    out startA,
                    out startB,
                    out distanceA,
                    out distanceB))
                return false;

            if (distanceA < 10f)
                startB = default(PathUnit.Position);

            PathUnit.Position endA = new PathUnit.Position
            {
                m_segment = connection.SegmentId,
                m_lane = connection.LaneIndex,
                m_offset = connection.SegmentOffset
            };
            uint replacementPath;
            if (!pathManager.CreatePath(
                    out replacementPath,
                    ref simulationManager.m_randomizer,
                    simulationManager.m_currentBuildIndex,
                    startA,
                    startB,
                    endA,
                    default(PathUnit.Position),
                    laneTypes,
                    info.m_vehicleType,
                    info.vehicleCategory,
                    MaxPathLength,
                    false,
                    false,
                    false,
                    false))
                return false;

            uint previousPath = vehicleData.m_path;
            vehicleData.m_path = replacementPath;
            vehicleData.m_flags |= Vehicle.Flags.WaitingPath;
            if (previousPath != 0u && previousPath != replacementPath)
                pathManager.ReleasePath(previousPath);

            uint frame = simulationManager.m_currentFrameIndex;
            RoutedVehicles[vehicleId] = new RoutedVehicle(
                route.FacilityId,
                route.SlotIndex,
                replacementPath,
                frame + RouteLifetimeFrames,
                route.NativeContinuationPath,
                route.NativeContinuationPositionIndex,
                route.NativeContinuationOffset,
                route.CandidateDistance);
            PublishHighlight(vehicleId, ref vehicleData);
            UndergroundParkingLog.Advanced(
                "UPG adopted arrival repathed to relocated entrance: vehicle="
                + vehicleId
                + " facility="
                + route.FacilityId
                + " oldPath="
                + previousPath
                + " newPath="
                + replacementPath
                + " segment="
                + connection.SegmentId
                + " lane="
                + connection.LaneIndex
                + " offset="
                + connection.SegmentOffset);
            return true;
        }

        public static void RetryFailedRoute(
            PassengerCarAI ai,
            ushort vehicleId,
            ref Vehicle vehicleData)
        {
            if (ai == null || vehicleId == 0)
                return;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            if (route.FacilityId <= 0
                || route.PathId == 0u
                || vehicleData.m_path != route.PathId)
                return;

            PathManager pathManager = PathManager.instance;
            if (pathManager == null || route.PathId >= pathManager.m_pathUnits.m_size)
                return;

            PathUnit path = pathManager.m_pathUnits.m_buffer[route.PathId];
            if ((path.m_pathFindFlags & PathUnit.FLAG_FAILED) == 0)
                return;

            if (IsTmpeAdoptedArrival(vehicleId))
            {
                // This accepted transaction already owns TM:PE's parked
                // identity and deferred walk. It may not fall into the
                // ordinary UPG late-route retry process or release occupants
                // in the carriageway. Keep the exact car and reservation
                // intact for TM:PE/native recovery or explicit diagnosis.
                UndergroundParkingOccupancyManager.RenewRoutedArrivalSlot(
                    vehicleId,
                    route.FacilityId,
                    route.SlotIndex);
                LogOutcome(
                    vehicleId,
                    route.FacilityId,
                    "tmpe-adopted-path-failed-hold");
                return;
            }

            uint nativeContinuationPath = route.NativeContinuationPath;
            int nativeContinuationPositionIndex = route.NativeContinuationPositionIndex;
            byte nativeContinuationOffset = route.NativeContinuationOffset;
            float failedCandidateDistance = route.CandidateDistance;
            int failedFacilityId = route.FacilityId;

            ClearRoute(vehicleId, "path-failed-try-next");
            bool pathStarted;
            if (TryStartEntryRouteCore(
                    ai,
                    vehicleId,
                    ref vehicleData,
                    nativeContinuationPath,
                    nativeContinuationPositionIndex,
                    nativeContinuationOffset,
                    failedCandidateDistance,
                    failedFacilityId,
                    out pathStarted)
                && pathStarted)
                return;

            // Every eligible candidate was exhausted. The established postfix
            // consumes this once and returns the car to native path ownership.
            RerouteRequired[vehicleId] = true;
        }

        public static bool HasActivityForFacility(int facilityId)
        {
            if (facilityId <= 0)
                return false;

            uint frame = SimulationManager.instance == null
                ? 0u
                : SimulationManager.instance.m_currentFrameIndex;
            for (int vehicleId = 1; vehicleId < RoutedVehicles.Length; vehicleId++)
            {
                RoutedVehicle route = RoutedVehicles[vehicleId];
                if (route.FacilityId == facilityId
                    && (route.IsValid(frame)
                        || IsTmpeAdoptedArrival((ushort)vehicleId)))
                    return true;
            }

            return false;
        }

        public static void CompleteArrival(ushort vehicleId)
        {
            if (vehicleId != 0)
            {
                RoutedVehicles[vehicleId] = default(RoutedVehicle);
                RerouteRequired[vehicleId] = false;
                TerminalEntryStates[vehicleId] = 0;
                RemoveHighlight(vehicleId);
                TmpeParkingCompatibilityManager.CompleteArrival(vehicleId);
            }
        }

        public static void ReleaseVehicle(ushort vehicleId)
        {
            if (vehicleId != 0 && RoutedVehicles[vehicleId].FacilityId > 0)
                ClearRoute(vehicleId, "vehicle-released");
            if (vehicleId != 0)
            {
                RerouteRequired[vehicleId] = false;
                TerminalEntryStates[vehicleId] = 0;
            }
        }

        public static void ReturnToNativePath(ushort vehicleId, string outcome)
        {
            if (vehicleId == 0)
                return;

            if (RoutedVehicles[vehicleId].FacilityId > 0)
                ClearRoute(vehicleId, outcome);
            RerouteRequired[vehicleId] = false;
            TerminalEntryStates[vehicleId] = 0;
        }

        public static void FailArrival(ushort vehicleId, string outcome)
        {
            if (vehicleId != 0 && RoutedVehicles[vehicleId].FacilityId > 0)
            {
                if (IsTmpeAdoptedArrival(vehicleId))
                {
                    RoutedVehicle route = RoutedVehicles[vehicleId];
                    UndergroundParkingOccupancyManager.RenewRoutedArrivalSlot(
                        vehicleId,
                        route.FacilityId,
                        route.SlotIndex);
                    RerouteRequired[vehicleId] = false;
                    LogOutcome(
                        vehicleId,
                        route.FacilityId,
                        "tmpe-adopted-hold-" + outcome);
                    return;
                }
                ClearRoute(vehicleId, outcome, true);
            }
        }

        public static bool ConsumeRerouteRequired(ushort vehicleId)
        {
            if (vehicleId == 0 || !RerouteRequired[vehicleId])
                return false;

            RerouteRequired[vehicleId] = false;
            return true;
        }

        public static bool TryGetReservedSlot(ushort vehicleId, out int slotIndex)
        {
            slotIndex = -1;
            if (vehicleId == 0)
                return false;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            if (route.FacilityId <= 0 || route.SlotIndex < 0)
                return false;

            slotIndex = route.SlotIndex;
            return true;
        }

        public static bool TryGetReservedParkingPose(
            ushort vehicleId,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (vehicleId == 0)
                return false;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            return route.FacilityId > 0
                   && route.SlotIndex >= 0
                   && UndergroundParkingOccupancyManager
                       .TryGetRoutedArrivalReservationPose(
                           vehicleId,
                           route.FacilityId,
                           route.SlotIndex,
                           out position,
                           out rotation);
        }

        public static bool TryGetNativePedestrianContinuation(
            ushort vehicleId,
            out uint path,
            out int positionIndex,
            out byte segmentOffset)
        {
            path = 0u;
            positionIndex = -1;
            segmentOffset = 0;
            if (vehicleId == 0)
                return false;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            if (route.FacilityId <= 0
                || route.NativeContinuationPath == 0u
                || route.NativeContinuationPositionIndex < 0)
                return false;

            path = route.NativeContinuationPath;
            positionIndex = route.NativeContinuationPositionIndex;
            segmentOffset = route.NativeContinuationOffset;
            return true;
        }

        public static bool AdoptNativePedestrianContinuation(
            ushort vehicleId,
            uint path,
            int positionIndex,
            byte segmentOffset)
        {
            if (vehicleId == 0 || path == 0u || positionIndex < 0)
                return false;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            if (route.FacilityId <= 0 || route.SlotIndex < 0)
                return false;

            // The prefix stores pre-call proof only long enough for vanilla's
            // original ParkVehicle transaction to run. Its post-call occupant
            // state is authoritative: cursor and offset are opaque traversal
            // state and may legitimately advance inside that native method.
            RoutedVehicles[vehicleId] = new RoutedVehicle(
                route.FacilityId,
                route.SlotIndex,
                route.PathId,
                route.ExpiresAt,
                path,
                positionIndex,
                segmentOffset,
                route.CandidateDistance);
            return true;
        }

        public static bool AdoptNativeDeferredPedestrianContinuation(
            ushort vehicleId)
        {
            if (vehicleId == 0)
                return false;

            RoutedVehicle route = RoutedVehicles[vehicleId];
            if (route.FacilityId <= 0 || route.SlotIndex < 0)
                return false;

            // TM:PE Parking AI can complete its one parking transaction by
            // creating the parked identity and publishing
            // RequiresWalkingPathToTarget instead of assigning nextPath. Keep
            // that third-party-owned deferred state explicit; UPG never writes
            // a replacement pedestrian path.
            RoutedVehicles[vehicleId] = new RoutedVehicle(
                route.FacilityId,
                route.SlotIndex,
                route.PathId,
                route.ExpiresAt,
                0u,
                -1,
                0,
                route.CandidateDistance);
            return true;
        }

        private static void RegisterRoute(
            ushort vehicleId,
            int facilityId,
            int slotIndex,
            uint pathId,
            uint nativeContinuationPath,
            int nativeContinuationPositionIndex,
            byte nativeContinuationOffset,
            float candidateDistance,
            ref Vehicle vehicleData)
        {
            uint frame = SimulationManager.instance == null ? 0u : SimulationManager.instance.m_currentFrameIndex;
            RoutedVehicles[vehicleId] = new RoutedVehicle(
                facilityId,
                slotIndex,
                pathId,
                frame + RouteLifetimeFrames,
                nativeContinuationPath,
                nativeContinuationPositionIndex,
                nativeContinuationOffset,
                candidateDistance);
            PublishHighlight(vehicleId, ref vehicleData);
        }

        private static void ClearRoute(ushort vehicleId, string outcome, bool reroute = false)
        {
            RoutedVehicle route = RoutedVehicles[vehicleId];
            RoutedVehicles[vehicleId] = default(RoutedVehicle);
            TerminalEntryStates[vehicleId] = 0;
            UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(
                vehicleId, route.FacilityId, route.SlotIndex);
            TmpeParkingCompatibilityManager.CompleteArrival(vehicleId);
            RerouteRequired[vehicleId] = reroute;
            RemoveHighlight(vehicleId);
            LogOutcome(vehicleId, route.FacilityId, outcome);
        }

        public static RoutedVehicleHighlight[] GetPublishedHighlights()
        {
            return PublishedHighlights;
        }

        public static bool IsPublishedHighlight(
            ushort vehicleId,
            ref Vehicle vehicleData)
        {
            if (vehicleId == 0)
                return false;

            RoutedVehicleHighlight highlight = PublishedHighlightsByVehicle[vehicleId];
            RoutedVehicle route = RoutedVehicles[vehicleId];
            VehicleInfo info = vehicleData.Info;
            uint frame = SimulationManager.instance == null
                ? 0u
                : SimulationManager.instance.m_currentFrameIndex;
            return highlight != null
                   && route.IsValid(frame)
                   && info != null
                   && string.Equals(info.name, highlight.PrefabName, StringComparison.Ordinal)
                   && vehicleData.m_citizenUnits == highlight.CitizenUnits;
        }

        private static void PublishHighlight(
            ushort vehicleId,
            ref Vehicle vehicleData)
        {
            VehicleInfo info = vehicleData.Info;
            if (vehicleId == 0
                || info == null
                || string.IsNullOrEmpty(info.name)
                || vehicleData.m_citizenUnits == 0u)
                return;

            lock (HighlightSync)
            {
                RoutedVehicleHighlight highlight = new RoutedVehicleHighlight(
                    vehicleId,
                    info.name,
                    vehicleData.m_citizenUnits);
                HighlightByVehicle[vehicleId] = highlight;
                PublishedHighlightsByVehicle[vehicleId] = highlight;
                PublishHighlightSnapshot();
            }
        }

        private static void RemoveHighlight(ushort vehicleId)
        {
            if (vehicleId == 0)
                return;
            lock (HighlightSync)
            {
                PublishedHighlightsByVehicle[vehicleId] = null;
                if (!HighlightByVehicle.Remove(vehicleId))
                    return;
                PublishHighlightSnapshot();
            }
        }

        private static void PublishHighlightSnapshot()
        {
            RoutedVehicleHighlight[] snapshot =
                new RoutedVehicleHighlight[HighlightByVehicle.Count];
            HighlightByVehicle.Values.CopyTo(snapshot, 0);
            PublishedHighlights = snapshot;
        }

        private static void LogOutcome(ushort vehicleId, int facilityId, string outcome)
        {
            if (_outcomeLogCount++ >= 64)
                return;

            UndergroundParkingLog.Advanced("UPG routed vehicle outcome: vehicle=" + vehicleId
                                        + " facility=" + facilityId
                                        + " outcome=" + outcome);
        }

        private static DetailedStartPathFind CreateDetailedPathBuilder()
        {
            MethodInfo method = typeof(PassengerCarAI).GetMethod(
                "StartPathFind",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(ushort),
                    typeof(Vehicle).MakeByRefType(),
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool)
                },
                null);
            if (method == null)
                return null;

            return (DetailedStartPathFind)Delegate.CreateDelegate(
                typeof(DetailedStartPathFind),
                null,
                method);
        }

        private static void LogSuccess(
            ushort vehicleId,
            int facilityId,
            UndergroundParkingRoadConnection connection,
            string mode)
        {
            if (_logCount++ >= LogLimit)
                return;

            UndergroundParkingLog.Advanced("UPG real arrival path started: vehicle=" + vehicleId
                                        + " facility=" + facilityId
                                        + " mode=" + mode
                                        + " segment=" + connection.SegmentId
                                        + " lane=" + connection.LaneIndex
                                        + " offset=" + connection.SegmentOffset);
        }

        private static bool TryFindStartPosition(
            Vector3 position,
            ref Vehicle vehicleData,
            VehicleInfo info,
            NetInfo.LaneType laneTypes,
            out PathUnit.Position startA,
            out PathUnit.Position startB,
            out float distanceA,
            out float distanceB)
        {
            return PathManager.FindPathPosition(
                    position,
                    ItemClass.Service.Road,
                    laneTypes,
                    info.m_vehicleType,
                    info.vehicleCategory,
                    (vehicleData.m_flags & (Vehicle.Flags.Importing | Vehicle.Flags.Exporting)) != 0,
                    false,
                    StartSearchRadius,
                    false,
                    false,
                    (vehicleData.m_flags2 & Vehicle.Flags2.EventRoadPass) != 0,
                    out startA,
                    out startB,
                    out distanceA,
                    out distanceB);
        }

        public static void Clear()
        {
            _logCount = 0;
            _failureLogCount = 0;
            _outcomeLogCount = 0;
            _authoritativeAttemptLogCount = 0;
            _authoritativeRejectionLogCount = 0;
            Array.Clear(RoutedVehicles, 0, RoutedVehicles.Length);
            Array.Clear(RerouteRequired, 0, RerouteRequired.Length);
            Array.Clear(TerminalEntryStates, 0, TerminalEntryStates.Length);
            lock (HighlightSync)
            {
                HighlightByVehicle.Clear();
                PublishedHighlights = new RoutedVehicleHighlight[0];
                Array.Clear(
                    PublishedHighlightsByVehicle,
                    0,
                    PublishedHighlightsByVehicle.Length);
            }
        }

        private static void LogFailure(
            ushort vehicleId,
            int facilityId,
            string reason,
            Vector3 start,
            Vector3 end)
        {
            if (_failureLogCount++ >= LogLimit)
                return;

            UndergroundParkingLog.Advanced("UPG real arrival path not started: vehicle=" + vehicleId
                                        + " facility=" + facilityId
                                        + " reason=" + reason
                                        + " start=(" + start.x.ToString("0.0") + ","
                                        + start.y.ToString("0.0") + "," + start.z.ToString("0.0") + ")"
                                        + " end=(" + end.x.ToString("0.0") + ","
                                        + end.y.ToString("0.0") + "," + end.z.ToString("0.0") + ")");
        }

        private static bool TryGetDestination(
            PassengerCarAI ai,
            ushort vehicleId,
            ref CitizenInstance driver,
            out Vector3 destination)
        {
            destination = Vector3.zero;
            try
            {
                ushort targetId = driver.m_targetBuilding;
                if (targetId == 0)
                    return false;

                if ((driver.m_flags & CitizenInstance.Flags.TargetIsNode) != 0)
                {
                    NetManager netManager = NetManager.instance;
                    if (netManager != null && targetId < netManager.m_nodes.m_size)
                    {
                        destination = netManager.m_nodes.m_buffer[targetId].m_position;
                        return destination != Vector3.zero;
                    }
                    return false;
                }

                BuildingManager buildingManager = BuildingManager.instance;
                if (buildingManager != null
                    && targetId < buildingManager.m_buildings.m_size)
                {
                    ref Building building =
                        ref buildingManager.m_buildings.m_buffer[targetId];
                    BuildingAI buildingAi = building.Info == null
                        ? null
                        : building.Info.m_buildingAI;
                    if (buildingAi != null)
                    {
                        Randomizer randomizer = new Randomizer(vehicleId);
                        Vector3 spawnPosition;
                        buildingAi.CalculateUnspawnPosition(
                            targetId,
                            ref building,
                            ref randomizer,
                            ai.m_info,
                            out spawnPosition,
                            out destination);
                        return destination != Vector3.zero;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool HasValidArrivalOccupants(
            ushort vehicleId,
            ref Vehicle vehicleData,
            out string reason,
            out string detail)
        {
            reason = "occupants-valid";
            detail = string.Empty;
            CitizenManager manager = CitizenManager.instance;
            if (manager == null)
            {
                reason = "citizen-manager-unavailable";
                return false;
            }
            if (vehicleData.m_citizenUnits == 0u)
            {
                reason = "citizen-units-missing";
                return false;
            }

            int count = 0;
            uint unitId = vehicleData.m_citizenUnits;
            int unitGuard = 0;
            while (unitId != 0u && unitGuard++ < 16)
            {
                if (unitId >= manager.m_units.m_size)
                {
                    reason = "citizen-unit-out-of-range";
                    detail = "unit=" + unitId + " capacity=" + manager.m_units.m_size;
                    return false;
                }

                CitizenUnit unit = manager.m_units.m_buffer[unitId];
                for (int i = 0; i < 5; i++)
                {
                    uint citizenId = unit.GetCitizen(i);
                    if (citizenId == 0u)
                        continue;
                    if (citizenId >= manager.m_citizens.m_size)
                    {
                        reason = "citizen-out-of-range";
                        detail = "citizen=" + citizenId;
                        return false;
                    }

                    ref Citizen citizen = ref manager.m_citizens.m_buffer[citizenId];
                    ushort instanceId = citizen.m_instance;
                    if (instanceId == 0 || instanceId >= manager.m_instances.m_size)
                    {
                        reason = "occupant-instance-unavailable";
                        detail = "citizen=" + citizenId
                                 + " instance=" + instanceId
                                 + " citizenVehicle=" + citizen.m_vehicle
                                 + " citizenParked=" + citizen.m_parkedVehicle;
                        return false;
                    }

                    ref CitizenInstance instance =
                        ref manager.m_instances.m_buffer[instanceId];
                    detail = "citizen=" + citizenId
                             + " instance=" + instanceId
                             + " instanceFlags=" + instance.m_flags
                             + " instanceCitizen=" + instance.m_citizen
                             + " instancePath=" + instance.m_path
                             + " instancePathFlags=" + GetPathFlags(instance.m_path)
                             + " instanceInfo=" + (instance.Info == null ? "null" : instance.Info.name)
                             + " citizenVehicle=" + citizen.m_vehicle
                             + " citizenParked=" + citizen.m_parkedVehicle;
                    if ((instance.m_flags & CitizenInstance.Flags.Created) == 0)
                    {
                        reason = "occupant-instance-not-created";
                        return false;
                    }
                    if (instance.m_citizen != citizenId)
                    {
                        reason = "occupant-instance-identity-mismatch";
                        return false;
                    }
                    if (instance.m_path != 0u)
                    {
                        reason = "occupant-path-already-present";
                        return false;
                    }
                    if (instance.Info == null || !(instance.Info.m_citizenAI is HumanAI))
                    {
                        reason = "occupant-ai-invalid";
                        return false;
                    }
                    if (citizen.m_vehicle != vehicleId)
                    {
                        reason = "occupant-active-vehicle-mismatch";
                        return false;
                    }
                    count++;
                    if (count > 32)
                    {
                        reason = "occupant-count-exceeded";
                        detail = "count=" + count;
                        return false;
                    }
                }
                unitId = unit.m_nextUnit;
            }

            if (unitId != 0u)
            {
                reason = "citizen-unit-chain-too-long";
                detail = "nextUnit=" + unitId;
                return false;
            }
            if (count == 0)
            {
                reason = "occupants-empty";
                return false;
            }

            detail = "count=" + count;
            return true;
        }

        public static void TraceAuthoritativeParkingAttempt(
            ushort vehicleId,
            ref Vehicle vehicleData,
            PathUnit.Position pathPos,
            uint nextPath,
            int nextPositionIndex,
            byte segmentOffset)
        {
            if (_authoritativeAttemptLogCount++ >= AuthoritativeDecisionLogLimit)
                return;

            UndergroundParkingLog.Advanced(
                "UPG authoritative parking entered: vehicle=" + vehicleId
                + " vehiclePath=" + vehicleData.m_path
                + " vehicleFlags=" + vehicleData.m_flags
                + " citizenUnits=" + vehicleData.m_citizenUnits
                + " pathSegment=" + pathPos.m_segment
                + " pathLane=" + pathPos.m_lane
                + " pathOffset=" + pathPos.m_offset
                + " nextPath=" + nextPath
                + " nextPathFlags=" + GetPathFlags(nextPath)
                + " nextPositionIndex=" + nextPositionIndex
                + " segmentOffset=" + segmentOffset);
        }

        public static void TraceAuthoritativeParkingRejection(
            ushort vehicleId,
            string reason,
            string detail)
        {
            RejectAuthoritativeParking(vehicleId, reason, detail);
        }

        private static bool RejectAuthoritativeParking(
            ushort vehicleId,
            string reason,
            string detail)
        {
            if (_authoritativeRejectionLogCount++ < AuthoritativeDecisionLogLimit)
            {
                UndergroundParkingLog.Advanced(
                    "UPG authoritative parking rejected: vehicle=" + vehicleId
                    + " reason=" + reason
                    + " detail=" + detail);
            }
            return false;
        }

        private static string GetPathFlags(uint pathId)
        {
            PathManager manager = PathManager.instance;
            if (pathId == 0u || manager == null || pathId >= manager.m_pathUnits.m_size)
                return "0";
            return manager.m_pathUnits.m_buffer[pathId].m_pathFindFlags.ToString();
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString("0.0")
                   + "," + value.y.ToString("0.0")
                   + "," + value.z.ToString("0.0") + ")";
        }

        private static GetDriverInstanceDelegate CreateDriverInstanceGetter()
        {
            MethodInfo method = typeof(PassengerCarAI).GetMethod(
                "GetDriverInstance",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() },
                null);
            if (method == null)
                return null;

            return (GetDriverInstanceDelegate)Delegate.CreateDelegate(
                typeof(GetDriverInstanceDelegate), null, method);
        }

        private delegate bool DetailedStartPathFind(
            PassengerCarAI ai,
            ushort vehicleId,
            ref Vehicle vehicleData,
            Vector3 startPosition,
            Vector3 endPosition,
            bool startBothWays,
            bool endBothWays,
            bool undergroundTarget);

        private delegate ushort GetDriverInstanceDelegate(
            PassengerCarAI ai,
            ushort vehicleId,
            ref Vehicle vehicleData);

        internal sealed class RoutedVehicleHighlight
        {
            public readonly ushort VehicleId;
            public readonly string PrefabName;
            public readonly uint CitizenUnits;

            public RoutedVehicleHighlight(
                ushort vehicleId,
                string prefabName,
                uint citizenUnits)
            {
                VehicleId = vehicleId;
                PrefabName = prefabName;
                CitizenUnits = citizenUnits;
            }
        }

        private struct RoutedVehicle
        {
            public readonly int FacilityId;
            public readonly int SlotIndex;
            public readonly uint PathId;
            public readonly uint ExpiresAt;
            public readonly uint NativeContinuationPath;
            public readonly int NativeContinuationPositionIndex;
            public readonly byte NativeContinuationOffset;
            public readonly float CandidateDistance;

            public RoutedVehicle(
                int facilityId,
                int slotIndex,
                uint pathId,
                uint expiresAt,
                uint nativeContinuationPath,
                int nativeContinuationPositionIndex,
                byte nativeContinuationOffset,
                float candidateDistance)
            {
                FacilityId = facilityId;
                SlotIndex = slotIndex;
                PathId = pathId;
                ExpiresAt = expiresAt;
                NativeContinuationPath = nativeContinuationPath;
                NativeContinuationPositionIndex = nativeContinuationPositionIndex;
                NativeContinuationOffset = nativeContinuationOffset;
                CandidateDistance = candidateDistance;
            }

            public bool IsValid(uint frame)
            {
                return FacilityId > 0 && unchecked((int)(ExpiresAt - frame)) > 0;
            }
        }
    }
}
