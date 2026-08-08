using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal struct UndergroundParkingCarVisual
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public VehicleInfo Info;

        public UndergroundParkingCarVisual(Vector3 position, Quaternion rotation, VehicleInfo info)
        {
            Position = position;
            Rotation = rotation;
            Info = info;
        }
    }

    internal struct UndergroundParkingInternalJourneyPlan
    {
        public UndergroundParkingFacility Facility;
        public Quaternion FinalRotation;
        public VehicleInfo Info;
    }

    internal static class UndergroundParkingOccupancyManager
    {
        private const float InternalTrafficLaneOffset = 1.12f;
        private const float SlotWidth = 3.5f;
        private const float SlotLength = 6f;
        private const float SlotEdgePadding = 1.6f;
        private const float ManagedParkingHeightTolerance = 2.6f;
        private const float ParkingRouteCatchmentRadius = 500f;
        private const float EntranceParkingClearanceAlongRoad = 12f;
        private const float EntranceParkingClearanceAcrossRoad = 14f;
        private const float EntranceParkingHeightTolerance = 4f;
        private const uint RoutedReservationLifetimeFrames = 262144u;
        private static readonly HashSet<ushort> HeldParkedVisuals = new HashSet<ushort>();
        private const uint PendingSlotClaimLifetimeFrames = 2048u;
        private const int CacheWarmupParkedScanBudget = 192;
        // Vehicle-bound routed reservations are structurally bounded.
        private const int MaxOutstandingReservations = 512;
        private const int OfferLogLimit = 24;
        private const int DisabledIngressLogLimit = 12;
        private const int ReservationCapLogLimit = 12;
        private const int PersistentAssignmentVersion = 2;
        private const int MaximumPersistentAssignments = 32767;
        private const int VanillaReleaseBudgetPerUpdate = 32;

        private static readonly ushort CreatedFlag = (ushort)VehicleParked.Flags.Created;
        private static readonly ushort DeletedFlag = (ushort)VehicleParked.Flags.Deleted;
        private static readonly ushort UpdatedFlag = (ushort)VehicleParked.Flags.Updated;
        private static readonly ushort ParkingFlag = (ushort)VehicleParked.Flags.Parking;

        private static readonly List<UndergroundParkingFacility> Facilities =
            new List<UndergroundParkingFacility>();
        private static readonly List<FacilityCache> FacilityCaches =
            new List<FacilityCache>();
        private static readonly Dictionary<ulong, SlotReservation> Reservations = new Dictionary<ulong, SlotReservation>();
        private static readonly Dictionary<ulong, SlotOccupancy> OccupiedSlots = new Dictionary<ulong, SlotOccupancy>();
        private static readonly Dictionary<ushort, ulong> ParkedVehicleSlots = new Dictionary<ushort, ulong>();
        private static readonly Dictionary<int, int> NextFreeSlotHints = new Dictionary<int, int>();
        private static readonly List<ulong> ReservationKeysToRemove = new List<ulong>();
        private static readonly List<ulong> OccupiedKeysToRemove = new List<ulong>();
        private static readonly List<ushort> ParkedIdsToRelease = new List<ushort>();
        private static readonly List<PersistentAssignment> PendingPersistentAssignments = new List<PersistentAssignment>();
        private static readonly Dictionary<ushort, PendingVanillaRelease> PendingVanillaReleases =
            new Dictionary<ushort, PendingVanillaRelease>();
        private static readonly List<ushort> PendingVanillaReleaseIds = new List<ushort>();

        private static int _facilityCacheRevision = -1;
        private static int _facilityCacheCount;
        private static bool _warmupActive;
        private static bool _legacyMigrationActive;
        private static int _warmupNextParkedId = 1;
        private static int _routeLogCount;
        private static int _preserveLogCount;
        private static int _disabledIngressLogCount;
        private static int _reservationCapLogCount;
        private static int _entranceBlockerRelocationLogCount;
        private static int _entranceBlockerRetainedLogCount;
        private static int _lastLoggedFacilities = -1;
        private static int _lastLoggedParked = -1;
        private static int _lastLoggedPending = -1;
        private static int _lastLoggedReservations = -1;

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void RebuildAll()
        {
            Clear();
            int facilityCount = EnsureFacilityCache();
            if (facilityCount > 0)
            {
                BeginWarmup();
                RestorePersistentAssignments();
                UndergroundParkingLog.Advanced("Parking occupancy manager rebuilt: facilities="
                                            + facilityCount
                                            + " connected="
                                            + CountConnectedFacilities()
                                            + " totalCapacity="
                                            + GetCachedTotalCapacity(facilityCount)
                                            + " cacheWarmupBudget="
                                            + CacheWarmupParkedScanBudget);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void Clear()
        {
            Reservations.Clear();
            OccupiedSlots.Clear();
            ParkedVehicleSlots.Clear();
            HeldParkedVisuals.Clear();
            NextFreeSlotHints.Clear();
            ReservationKeysToRemove.Clear();
            OccupiedKeysToRemove.Clear();
            ParkedIdsToRelease.Clear();
            Facilities.Clear();
            FacilityCaches.Clear();
            _facilityCacheRevision = -1;
            _facilityCacheCount = 0;
            _warmupActive = false;
            _legacyMigrationActive = false;
            _warmupNextParkedId = 1;
            _routeLogCount = 0;
            _preserveLogCount = 0;
            _disabledIngressLogCount = 0;
            _reservationCapLogCount = 0;
            _entranceBlockerRelocationLogCount = 0;
            _entranceBlockerRetainedLogCount = 0;
            _lastLoggedFacilities = -1;
            _lastLoggedParked = -1;
            _lastLoggedPending = -1;
            _lastLoggedReservations = -1;
        }

        public static bool HasPendingVanillaReleases
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get { return PendingVanillaReleases.Count > 0; }
        }

        public static int PendingVanillaReleaseCount
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get { return PendingVanillaReleases.Count; }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal static bool IsPendingVanillaRelease(ushort parkedId)
        {
            return parkedId != 0 && PendingVanillaReleases.ContainsKey(parkedId);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void ClearPendingVanillaReleases()
        {
            PendingVanillaReleases.Clear();
            PendingVanillaReleaseIds.Clear();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static byte[] SerializePersistentAssignments()
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(PersistentAssignmentVersion);
                long countPosition = stream.Position;
                writer.Write(0);
                int count = 0;
                foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
                {
                    SlotOccupancy occupancy = pair.Value;
                    if (occupancy.ParkedId == 0
                        || vehicleManager == null
                        || occupancy.ParkedId >= vehicleManager.m_parkedVehicles.m_size)
                    {
                        continue;
                    }

                    VehicleParked parkedData = vehicleManager.m_parkedVehicles.m_buffer[occupancy.ParkedId];
                    VehicleInfo info = parkedData.Info;
                    if (!IsCreated(parkedData) || info == null)
                        continue;

                    writer.Write(occupancy.ParkedId);
                    writer.Write(occupancy.FacilityId);
                    writer.Write(occupancy.SlotIndex);
                    writer.Write(info.name ?? string.Empty);
                    count++;
                }

                long endPosition = stream.Position;
                stream.Position = countPosition;
                writer.Write(count);
                stream.Position = endPosition;
                writer.Write(PendingVanillaReleases.Count);
                foreach (KeyValuePair<ushort, PendingVanillaRelease> pair in PendingVanillaReleases)
                {
                    PendingVanillaRelease release = pair.Value;
                    writer.Write(release.ParkedId);
                    writer.Write(release.ExpectedOwnerCitizen);
                    writer.Write(release.PrefabName ?? string.Empty);
                    writer.Write(release.SegmentId);
                    WriteVector(writer, release.ReferencePosition);
                    WriteVector(writer, release.LaneDirection);
                }
                UndergroundParkingLog.Advanced("Saved persistent UPG parked assignments: count="
                                            + count
                                            + " pendingVanillaReleases="
                                            + PendingVanillaReleases.Count);
                return stream.ToArray();
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void StagePersistentAssignments(byte[] data)
        {
            PendingPersistentAssignments.Clear();
            PendingVanillaReleases.Clear();
            PendingVanillaReleaseIds.Clear();
            if (data == null || data.Length == 0)
                return;

            List<PersistentAssignment> stagedAssignments =
                new List<PersistentAssignment>();
            using (MemoryStream stream = new MemoryStream(data, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int version = reader.ReadInt32();
                if (version < 1 || version > PersistentAssignmentVersion)
                {
                    UndergroundParkingLog.Warning("Ignoring unsupported UPG parked-assignment data version: " + version);
                    return;
                }

                int count = reader.ReadInt32();
                if (count < 0 || count > MaximumPersistentAssignments)
                {
                    throw new InvalidDataException(
                        "UPG parked-assignment count is outside the supported range: " + count);
                }

                for (int i = 0; i < count; i++)
                {
                    stagedAssignments.Add(new PersistentAssignment(
                        reader.ReadUInt16(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadString()));
                }

                if (version >= 2)
                {
                    int releaseCount = reader.ReadInt32();
                    if (releaseCount < 0 || releaseCount > MaximumPersistentAssignments)
                    {
                        throw new InvalidDataException(
                            "UPG pending vanilla-release count is outside the supported range: "
                            + releaseCount);
                    }

                    for (int i = 0; i < releaseCount; i++)
                    {
                        PendingVanillaRelease release = new PendingVanillaRelease(
                            reader.ReadUInt16(),
                            reader.ReadUInt32(),
                            reader.ReadString(),
                            reader.ReadUInt16(),
                            ReadVector(reader),
                            ReadVector(reader));
                        if (release.ParkedId != 0)
                            PendingVanillaReleases[release.ParkedId] = release;
                    }
                }
            }

            PendingPersistentAssignments.AddRange(stagedAssignments);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal static void AppendPersistentAssignment(
            ushort parkedId,
            int facilityId,
            int slotIndex,
            string prefabName)
        {
            if (parkedId == 0 || facilityId <= 0 || slotIndex < 0)
                return;

            PendingPersistentAssignments.Add(new PersistentAssignment(
                parkedId,
                facilityId,
                slotIndex,
                prefabName));
        }

        private static void RestorePersistentAssignments()
        {
            if (PendingPersistentAssignments.Count == 0)
                return;

            VehicleManager vehicleManager = VehicleManager.instance;
            int restored = 0;
            int rejected = 0;
            for (int i = 0; i < PendingPersistentAssignments.Count; i++)
            {
                PersistentAssignment assignment = PendingPersistentAssignments[i];
                UndergroundParkingFacility facility;
                UndergroundParkingRoadConnection connection;
                if (vehicleManager == null
                    || assignment.ParkedId == 0
                    || assignment.ParkedId >= vehicleManager.m_parkedVehicles.m_size
                    || assignment.SlotIndex < 0
                    || PendingVanillaReleases.ContainsKey(assignment.ParkedId)
                    || !TryGetPortalForFacility(assignment.FacilityId, out facility, out connection)
                    || assignment.SlotIndex >= GetManagedSlotCapacity(facility))
                {
                    rejected++;
                    continue;
                }

                VehicleParked parkedData = vehicleManager.m_parkedVehicles.m_buffer[assignment.ParkedId];
                VehicleInfo info = parkedData.Info;
                ulong key = MakeSlotKey(facility, assignment.SlotIndex);
                if (!IsCreated(parkedData)
                    || info == null
                    || info.name != assignment.PrefabName
                    || OccupiedSlots.ContainsKey(key))
                {
                    rejected++;
                    continue;
                }

                MoveParkedVehicle(
                    assignment.ParkedId,
                    ref parkedData,
                    GetUndergroundParkingSlotPosition(facility, assignment.SlotIndex),
                    GetUndergroundParkingSlotRotation(facility, assignment.SlotIndex),
                    true);
                RegisterManagedParkedVehicle(assignment.ParkedId, facility, assignment.SlotIndex);
                restored++;
            }

            PendingPersistentAssignments.Clear();
            UndergroundParkingLog.Advanced("Restored persistent UPG parked assignments: restored="
                                        + restored
                                        + " rejected="
                                        + rejected);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void ReleaseFacility(UndergroundParkingFacility facility)
        {
            if (!facility.IsValid)
                return;

            RemoveReservationsForFacility(facility.Id);
            int queued = QueueAssignedParkedVehiclesForVanillaRelease(facility);
            if (queued > 0)
            {
                UndergroundParkingLog.Advanced(
                    "Queued managed parked cars and owners for verified vanilla release: facility="
                    + facility.Id
                    + " queued="
                    + queued
                    + " pending="
                    + PendingVanillaReleases.Count);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal static void ReleaseParkedVehicleRecord(
            ushort parkedId,
            uint expectedOwnerCitizen = 0u)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null
                || parkedId == 0
                || parkedId >= vehicleManager.m_parkedVehicles.m_size)
            {
                return;
            }

            VehicleParked parkedData =
                vehicleManager.m_parkedVehicles.m_buffer[parkedId];
            if (!IsCreated(parkedData)
                || (expectedOwnerCitizen != 0u
                    && parkedData.m_ownerCitizen != expectedOwnerCitizen))
                return;

            // VehicleManager is vanilla's direct parked-record owner and clears
            // the exact citizen link internally. Do not invoke the public
            // Citizen.SetParkedVehicle callback: other parking integrations may
            // interpret it as a retrieval transition and materialize the owner.
            vehicleManager.ReleaseParkedVehicle(parkedId);
        }

        public static bool IsResidentialHomeParking(ushort homeId, Vector3 referencePosition)
        {
            BuildingManager manager = BuildingManager.instance;
            if (manager == null || homeId == 0 || homeId >= manager.m_buildings.m_size)
                return false;

            Building home = manager.m_buildings.m_buffer[homeId];
            BuildingInfo info = home.Info;
            if ((home.m_flags & Building.Flags.Created) == 0
                || info == null
                || info.m_class == null
                || info.m_class.m_service != ItemClass.Service.Residential)
            {
                return false;
            }

            float halfWidth = Mathf.Max(4f, info.m_cellWidth * 4f);
            float halfLength = Mathf.Max(4f, info.m_cellLength * 4f);
            float homeCatchment = Mathf.Max(32f, Mathf.Sqrt(halfWidth * halfWidth + halfLength * halfLength) + 16f);
            return UndergroundParkingGeometry.FlatSqrDistance(home.m_position, referencePosition)
                   <= homeCatchment * homeCatchment;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryFindParkingFacility(
            Vector3 destination,
            float afterDistance,
            int afterFacilityId,
            out int facilityId,
            out float facilityDistance)
        {
            facilityId = 0;
            facilityDistance = 0f;

            int count = EnsureFacilityCache();
            float bestDistance = ParkingRouteCatchmentRadius * ParkingRouteCatchmentRadius;
            int bestIndex = -1;
            for (int i = 0; i < count; i++)
            {
                FacilityCache cache = FacilityCaches[i];
                if (!cache.Facility.IsValid
                    || !IsFacilityAcceptingArrivals(cache.Facility)
                    || !cache.HasConnection
                    || !IsRoadConnectionStillUsable(cache.Connection)
                    || FindFirstFreeSlot(cache.Facility, cache.SlotCapacity, 0) < 0)
                {
                    continue;
                }

                float distance = UndergroundParkingGeometry.FlatSqrDistance(
                    cache.Connection.LanePosition,
                    destination);
                if (distance < afterDistance
                    || (distance == afterDistance
                        && cache.Facility.Id <= afterFacilityId)
                    || distance > bestDistance
                    || (distance == bestDistance
                        && bestIndex >= 0
                        && cache.Facility.Id
                           >= FacilityCaches[bestIndex].Facility.Id))
                    continue;

                bestDistance = distance;
                bestIndex = i;
            }

            if (bestIndex < 0)
                return false;

            facilityId = FacilityCaches[bestIndex].Facility.Id;
            facilityDistance = bestDistance;
            if (_routeLogCount < OfferLogLimit)
            {
                _routeLogCount++;
                UndergroundParkingLog.Advanced("UPG arrival road route candidate: facility="
                                            + FacilityCaches[bestIndex].Facility.Id
                                            + " destination="
                                            + FormatVector(destination)
                                            + " entranceLane="
                                            + FormatVector(FacilityCaches[bestIndex].Connection.LanePosition));
            }
            return true;
        }

        public static bool IsAuthoritativeDriverReturningHome(
            uint citizenId,
            ushort targetBuilding,
            bool targetIsNode,
            Vector3 destination)
        {
            CitizenManager manager = CitizenManager.instance;
            if (manager == null
                || citizenId == 0u
                || citizenId >= manager.m_citizens.m_size)
                return false;

            ushort homeId = manager.m_citizens.m_buffer[citizenId].m_homeBuilding;
            if (homeId == 0)
                return false;

            // The authoritative driver instance is more reliable here than the
            // vehicle's transient citizen-unit chain. A homebound driver must
            // never be diverted into public parking, even when vanilla's
            // calculated unspawn point lies just outside the home footprint.
            if (!targetIsNode && targetBuilding == homeId)
                return true;

            return IsResidentialHomeParking(homeId, destination);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool IsAtFacilityPortal(
            int facilityId,
            Vector3 position,
            Vector3 vehicleForward)
        {
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            return TryGetPortalForFacility(facilityId, out facility, out connection)
                   && IsAtRoadConnectionPortal(
                       connection,
                       position,
                       vehicleForward,
                       facility.Direction);
        }

        public static bool IsAtRoadConnectionPortal(
            UndergroundParkingRoadConnection connection,
            Vector3 position,
            Vector3 vehicleForward)
        {
            return IsAtRoadConnectionPortal(
                connection,
                position,
                vehicleForward,
                Vector3.forward);
        }

        private static bool IsAtRoadConnectionPortal(
            UndergroundParkingRoadConnection connection,
            Vector3 position,
            Vector3 vehicleForward,
            Vector3 fallbackDirection)
        {
            if (!connection.IsValid
                || Mathf.Abs(connection.LanePosition.y - position.y) > 2f)
                return false;

            Vector3 laneDirection = NormalizeFlat(
                connection.LaneDirection,
                fallbackDirection);
            vehicleForward = NormalizeFlat(vehicleForward, laneDirection);
            if (Vector3.Dot(vehicleForward, laneDirection) < 0.8f)
                return false;

            Vector3 delta = position - connection.LanePosition;
            delta.y = 0f;
            float longitudinal = Mathf.Abs(Vector3.Dot(delta, laneDirection));
            Vector3 lateralVector = delta - laneDirection * Vector3.Dot(delta, laneDirection);
            // Admit only the exact connected lane centre and travel direction.
            // The previous ±12m/8m gate accepted several nearby cars at once,
            // including bodies that had not yet reached the portal stop line.
            return longitudinal <= 1f
                   && lateralVector.sqrMagnitude <= 0.75f * 0.75f;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool IsRoadsideParkingAtEntrance(
            ushort segmentId,
            Vector3 parkedPosition)
        {
            if (segmentId == 0)
                return false;

            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                FacilityCache cache = FacilityCaches[i];
                if (!cache.Facility.IsValid
                    || !cache.HasConnection
                    || cache.Connection.SegmentId != segmentId
                    || !IsRoadConnectionStillUsable(cache.Connection))
                {
                    continue;
                }

                if (IsInsideEntranceParkingClearance(cache, parkedPosition))
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryGetRoutedArrivalReservationPose(
            ushort vehicleId,
            int facilityId,
            int reservedSlotIndex,
            out Vector3 undergroundPosition,
            out Quaternion undergroundRotation)
        {
            undergroundPosition = Vector3.zero;
            undergroundRotation = Quaternion.identity;
            if (vehicleId == 0 || facilityId <= 0 || reservedSlotIndex < 0)
                return false;

            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                FacilityCache cache = FacilityCaches[i];
                if (cache.Facility.Id != facilityId
                    || !cache.Facility.IsValid
                    || !cache.HasConnection
                    || !IsRoadConnectionStillUsable(cache.Connection)
                    || reservedSlotIndex >= cache.SlotCapacity)
                    continue;

                ulong key = MakeSlotKey(cache.Facility, reservedSlotIndex);
                SlotReservation reservation;
                SlotOccupancy existing;
                if (!Reservations.TryGetValue(key, out reservation)
                    || reservation.RoutedVehicleId != vehicleId
                    || reservation.FacilityId != facilityId
                    || reservation.SlotIndex != reservedSlotIndex
                    || OccupiedSlots.TryGetValue(key, out existing))
                    return false;

                undergroundPosition =
                    GetUndergroundParkingSlotPosition(cache.Facility, reservedSlotIndex);
                undergroundRotation = GetUndergroundParkingSlotRotation(cache.Facility, reservedSlotIndex);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryClaimRoutedArrivalSlot(
            ushort vehicleId,
            int facilityId,
            int reservedSlotIndex,
            out Vector3 undergroundPosition,
            out Quaternion undergroundRotation,
            out int slotIndex)
        {
            undergroundPosition = Vector3.zero;
            undergroundRotation = Quaternion.identity;
            slotIndex = -1;

            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                FacilityCache cache = FacilityCaches[i];
                if (cache.Facility.Id != facilityId
                    || !cache.Facility.IsValid
                    || !cache.HasConnection
                    || !IsRoadConnectionStillUsable(cache.Connection))
                    continue;

                if (reservedSlotIndex < 0 || reservedSlotIndex >= cache.SlotCapacity)
                    return false;

                ulong key = MakeSlotKey(cache.Facility, reservedSlotIndex);
                SlotReservation reservation;
                if (!Reservations.TryGetValue(key, out reservation)
                    || reservation.RoutedVehicleId != vehicleId
                    || reservation.FacilityId != facilityId
                    || reservation.SlotIndex != reservedSlotIndex)
                    return false;

                SlotOccupancy existing;
                if (OccupiedSlots.TryGetValue(key, out existing))
                    return false;

                Reservations.Remove(key);
                slotIndex = reservedSlotIndex;

                undergroundPosition = GetUndergroundParkingSlotPosition(cache.Facility, slotIndex);
                undergroundRotation = GetUndergroundParkingSlotRotation(cache.Facility, slotIndex);
                MarkSlotPending(cache.Facility, slotIndex);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryReserveRoutedArrivalSlot(
            ushort vehicleId,
            int facilityId,
            out int slotIndex)
        {
            slotIndex = -1;
            if (vehicleId == 0)
                return false;

            // Routed journeys share the same structural cap as short-lived
            // local parking offers. The check sits immediately before the
            // only routed insertion, so existing reservations can complete or
            // expire normally while the 513th claim falls back to vanilla.
            if (Reservations.Count >= MaxOutstandingReservations)
            {
                if (_reservationCapLogCount++ < ReservationCapLogLimit)
                {
                    UndergroundParkingLog.Warning(
                        "UPG routed reservation cap reached: reservations="
                        + Reservations.Count
                        + " cap="
                        + MaxOutstandingReservations
                        + " vehicle="
                        + vehicleId
                        + " facility="
                        + facilityId
                        + " vanillaFallback=True");
                }
                return false;
            }

            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                FacilityCache cache = FacilityCaches[i];
                if (cache.Facility.Id != facilityId
                    || !cache.Facility.IsValid
                    || !IsFacilityAcceptingArrivals(cache.Facility)
                    || !cache.HasConnection
                    || !IsRoadConnectionStillUsable(cache.Connection))
                    continue;

                slotIndex = FindFirstFreeSlot(cache.Facility, cache.SlotCapacity, 0);
                if (slotIndex < 0)
                    return false;

                ulong key = MakeSlotKey(cache.Facility, slotIndex);
                Reservations[key] = new SlotReservation(
                    facilityId,
                    slotIndex,
                    GetCurrentFrame() + RoutedReservationLifetimeFrames,
                    vehicleId);
                NextFreeSlotHints[facilityId] = slotIndex + 1;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void ReleaseRoutedArrivalSlot(
            ushort vehicleId,
            int facilityId,
            int slotIndex)
        {
            if (facilityId <= 0 || slotIndex < 0)
                return;

            ulong key = MakeSlotKey(facilityId, slotIndex);
            SlotReservation reservation;
            if (Reservations.TryGetValue(key, out reservation)
                && reservation.RoutedVehicleId == vehicleId
                && reservation.FacilityId == facilityId
                && reservation.SlotIndex == slotIndex)
            {
                Reservations.Remove(key);
                NextFreeSlotHints[facilityId] = slotIndex;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool RenewRoutedArrivalSlot(
            ushort vehicleId,
            int facilityId,
            int slotIndex)
        {
            if (vehicleId == 0 || facilityId <= 0 || slotIndex < 0)
                return false;

            ulong key = MakeSlotKey(facilityId, slotIndex);
            SlotReservation reservation;
            if (!Reservations.TryGetValue(key, out reservation)
                || reservation.RoutedVehicleId != vehicleId
                || reservation.FacilityId != facilityId
                || reservation.SlotIndex != slotIndex)
                return false;

            Reservations[key] = new SlotReservation(
                facilityId,
                slotIndex,
                GetCurrentFrame() + RoutedReservationLifetimeFrames,
                vehicleId);
            return true;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryRestoreRoutedArrivalSlot(
            ushort vehicleId,
            int facilityId,
            int slotIndex)
        {
            if (vehicleId == 0 || facilityId <= 0 || slotIndex < 0)
                return false;

            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                FacilityCache cache = FacilityCaches[i];
                if (cache.Facility.Id != facilityId
                    || !cache.Facility.IsValid
                    || !cache.HasConnection
                    || !IsRoadConnectionStillUsable(cache.Connection)
                    || slotIndex >= cache.SlotCapacity)
                    continue;

                ulong key = MakeSlotKey(cache.Facility, slotIndex);
                SlotOccupancy occupancy;
                SlotReservation reservation;
                if (OccupiedSlots.TryGetValue(key, out occupancy))
                    return false;
                if (Reservations.TryGetValue(key, out reservation))
                {
                    return reservation.RoutedVehicleId == vehicleId
                           && reservation.FacilityId == facilityId
                           && reservation.SlotIndex == slotIndex;
                }

                Reservations[key] = new SlotReservation(
                    facilityId,
                    slotIndex,
                    GetCurrentFrame() + RoutedReservationLifetimeFrames,
                    vehicleId);
                NextFreeSlotHints[facilityId] = slotIndex + 1;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryPreserveManagedParkedVehicle(
            ushort parkedId,
            ref VehicleParked parkedData,
            out int facilityId,
            out int slotIndex)
        {
            facilityId = 0;
            slotIndex = -1;
            if (parkedId == 0)
                return false;

            if (PendingVanillaReleases.ContainsKey(parkedId))
            {
                // The persisted NUKE ledger is the exclusive owner until it
                // verifies a conventional relocation or exact release. A new
                // facility must never reclaim the old underground coordinate.
                ReleaseParkedVehicleSlot(parkedId);
                return false;
            }

            if (!IsCreated(parkedData))
            {
                ReleaseParkedVehicleSlot(parkedId);
                return false;
            }

            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                UndergroundParkingFacility facility = FacilityCaches[i].Facility;
                if (!facility.IsValid)
                    continue;

                int matchedSlot;
                if (!TryGetManagedSlotIndex(facility, parkedData.m_position, out matchedSlot))
                    continue;

                facilityId = facility.Id;
                slotIndex = matchedSlot;
                SetParkedFlags(ref parkedData, ParkingFlag);
                RegisterManagedParkedVehicle(parkedId, facility, matchedSlot);
                RemoveReservationForSlot(facility, matchedSlot);
                return true;
            }

            ReleaseParkedVehicleSlot(parkedId);
            return false;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryGetPortalForFacility(
            int facilityId,
            out UndergroundParkingFacility facility,
            out UndergroundParkingRoadConnection connection)
        {
            facility = UndergroundParkingFacility.None;
            connection = default(UndergroundParkingRoadConnection);
            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                FacilityCache cache = FacilityCaches[i];
                if (cache.Facility.Id != facilityId
                    || !cache.HasConnection
                    || !IsRoadConnectionStillUsable(cache.Connection))
                {
                    continue;
                }

                facility = cache.Facility;
                connection = cache.Connection;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool CommitRoutedArrival(
            ushort parkedId,
            int facilityId,
            int slotIndex,
            Vector3 undergroundPosition,
            Quaternion undergroundRotation)
        {
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            if (parkedId == 0
                || slotIndex < 0
                || !TryGetPortalForFacility(facilityId, out facility, out connection))
            {
                CancelPendingSlotClaim(facilityId, slotIndex);
                return false;
            }

            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null
                || parkedId >= vehicleManager.m_parkedVehicles.m_size)
            {
                CancelPendingSlotClaim(facilityId, slotIndex);
                return false;
            }

            VehicleParked parkedData = vehicleManager.m_parkedVehicles.m_buffer[parkedId];
            if (!IsCreated(parkedData))
            {
                CancelPendingSlotClaim(facilityId, slotIndex);
                return false;
            }

            MoveParkedVehicle(
                parkedId,
                ref parkedData,
                undergroundPosition,
                undergroundRotation,
                true);
            RegisterManagedParkedVehicle(parkedId, facility, slotIndex);
            UndergroundParkingLog.Advanced("UPG routed arrival committed underground: parked="
                                        + parkedId
                                        + " facility="
                                        + facilityId
                                        + " slot="
                                        + slotIndex);
            return true;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool IsUsableParkedVehicle(ushort parkedId)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            return parkedId != 0
                   && vehicleManager != null
                   && parkedId < vehicleManager.m_parkedVehicles.m_size
                   && IsCreated(vehicleManager.m_parkedVehicles.m_buffer[parkedId]);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void CancelPendingSlotClaim(int facilityId, int slotIndex)
        {
            if (facilityId <= 0 || slotIndex < 0)
                return;

            ulong key = MakeSlotKey(facilityId, slotIndex);
            SlotOccupancy occupancy;
            if (OccupiedSlots.TryGetValue(key, out occupancy) && occupancy.ParkedId == 0)
            {
                OccupiedSlots.Remove(key);
                NextFreeSlotHints[facilityId] = slotIndex;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryGetManagedVehiclePortal(
            ushort parkedId,
            out UndergroundParkingFacility facility,
            out UndergroundParkingRoadConnection connection)
        {
            facility = UndergroundParkingFacility.None;
            connection = default(UndergroundParkingRoadConnection);
            ulong slotKey;
            if (parkedId == 0 || !ParkedVehicleSlots.TryGetValue(parkedId, out slotKey))
                return false;

            SlotOccupancy occupancy;
            if (!OccupiedSlots.TryGetValue(slotKey, out occupancy) || occupancy.ParkedId != parkedId)
                return false;

            return TryGetPortalForFacility(occupancy.FacilityId, out facility, out connection);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryVirtualizeManagedVehicleAtPortal(
            ushort parkedId,
            out UndergroundParkingFacility facility,
            out UndergroundParkingRoadConnection connection,
            out Vector3 retrievalPosition,
            out uint pedestrianLaneId,
            out Vector3 originalPosition,
            out Quaternion originalRotation)
        {
            facility = UndergroundParkingFacility.None;
            connection = default(UndergroundParkingRoadConnection);
            retrievalPosition = Vector3.zero;
            pedestrianLaneId = 0u;
            originalPosition = Vector3.zero;
            originalRotation = Quaternion.identity;

            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null
                || parkedId == 0
                || parkedId >= vehicleManager.m_parkedVehicles.m_size
                || !TryGetManagedVehiclePortal(parkedId, out facility, out connection))
            {
                return false;
            }

            ref VehicleParked parkedData = ref vehicleManager.m_parkedVehicles.m_buffer[parkedId];
            if (!HasParkedFlag(parkedData, CreatedFlag))
                return false;

            int pedestrianLaneIndex;
            if (!UndergroundParkingOccupancyHarmony.TryResolvePavementHandoff(
                    facility,
                    connection,
                    out retrievalPosition,
                    out pedestrianLaneId,
                    out pedestrianLaneIndex))
            {
                // Never make an underground car retrievable from a traffic lane.
                // Without a validated same-side pavement lane, leave the parked
                // identity underground and let vanilla choose another travel mode.
                return false;
            }

            originalPosition = parkedData.m_position;
            originalRotation = parkedData.m_rotation;
            Vector3 direction = NormalizeFlat(connection.LaneDirection, facility.Direction);
            Quaternion portalRotation = Quaternion.LookRotation(direction, Vector3.up);
            MoveParkedVehicle(
                parkedId,
                ref parkedData,
                retrievalPosition,
                portalRotation,
                true);
            return true;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void RestoreManagedVehicleAfterPortalVirtualization(
            ushort parkedId,
            Vector3 originalPosition,
            Quaternion originalRotation)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            if (vehicleManager == null
                || parkedId == 0
                || parkedId >= vehicleManager.m_parkedVehicles.m_size
                || !TryGetManagedVehiclePortal(parkedId, out facility, out connection))
            {
                return;
            }

            ref VehicleParked parkedData = ref vehicleManager.m_parkedVehicles.m_buffer[parkedId];
            if (!HasParkedFlag(parkedData, CreatedFlag))
                return;

            MoveParkedVehicle(
                parkedId,
                ref parkedData,
                originalPosition,
                originalRotation,
                true);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void CommitManagedDeparture(ushort parkedId)
        {
            ReleaseParkedVehicleSlot(parkedId);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void UpdateHousekeeping()
        {
            uint frame = GetCurrentFrame();
            ExpireReservations(frame);
            ExpirePendingSlotClaims(frame);
            WarmupManagedParkedVehicleCache(CacheWarmupParkedScanBudget);
            ProcessPendingVanillaReleases(VanillaReleaseBudgetPerUpdate);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static int CountAssignedParkedCars(UndergroundParkingFacility facility)
        {
            if (!facility.IsValid)
                return 0;

            int count = 0;
            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                if (pair.Value.FacilityId == facility.Id && pair.Value.ParkedId != 0)
                    count++;
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool HasTransientActivityForFacility(int facilityId)
        {
            if (facilityId <= 0)
                return false;

            foreach (KeyValuePair<ulong, SlotReservation> pair in Reservations)
            {
                if (pair.Value.FacilityId == facilityId)
                    return true;
            }

            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                if (pair.Value.FacilityId == facilityId
                    && pair.Value.ParkedId == 0)
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void RefreshFacilitySlotPositions(UndergroundParkingFacility facility)
        {
            VehicleManager manager = VehicleManager.instance;
            if (!facility.IsValid || manager == null)
                return;

            int retained = 0;
            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                SlotOccupancy occupancy = pair.Value;
                if (occupancy.FacilityId != facility.Id
                    || occupancy.ParkedId == 0
                    || occupancy.ParkedId >= manager.m_parkedVehicles.m_size)
                    continue;

                VehicleParked parked =
                    manager.m_parkedVehicles.m_buffer[occupancy.ParkedId];
                if (!IsCreated(parked))
                    continue;

                // Surviving floor slots retain the same stable identity. The
                // garage top plane is fixed, so adding or removing lower floors
                // does not change the world pose of an existing slot.
                MoveParkedVehicle(
                    occupancy.ParkedId,
                    ref parked,
                    GetUndergroundParkingSlotPosition(
                        facility,
                        occupancy.SlotIndex),
                    GetUndergroundParkingSlotRotation(facility, occupancy.SlotIndex),
                    true);
                retained++;
            }

            // TrySetFloorCount has already proved that no parked or transient
            // claim belongs to a removed floor. Preserve every in-range offer
            // and pending arrival so traffic using surviving floors continues.
            RemoveTransientClaimsOutsideCapacity(
                facility.Id,
                GetManagedSlotCapacity(facility));

            _facilityCacheRevision = -1;
            UndergroundParkingVisualManager.RequestParkedCarRefresh();
            UndergroundParkingLog.Advanced("UPG occupied slots preserved after floor change: facility="
                                        + facility.Id + " retained=" + retained);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static int CopyParkedCarVisuals(List<UndergroundParkingCarVisual> buffer)
        {
            if (buffer == null)
                return 0;

            buffer.Clear();
            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null)
                return 0;

            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                SlotOccupancy occupancy = pair.Value;
                if (occupancy.ParkedId == 0
                    || HeldParkedVisuals.Contains(occupancy.ParkedId))
                    continue;

                UndergroundParkingFacility facility;
                UndergroundParkingRoadConnection connection;
                if (!TryGetPortalForFacility(occupancy.FacilityId, out facility, out connection))
                    continue;

                // Standalone structures follow their live placed entrance and
                // road frame. Render occupied slots from that exact same current
                // centre rather than the older serialized centre used only by
                // the hidden parked record.
                facility.GarageCenter =
                    UndergroundParkingGeometry.ResolveCurrentVisualGarageCenter(facility);

                if (occupancy.ParkedId >= vehicleManager.m_parkedVehicles.m_size)
                    continue;
                VehicleInfo info = vehicleManager.m_parkedVehicles.m_buffer[occupancy.ParkedId].Info;
                if (info == null)
                    continue;

                buffer.Add(new UndergroundParkingCarVisual(
                    GetUndergroundParkingSlotPosition(facility, occupancy.SlotIndex),
                    GetUndergroundParkingSlotRotation(facility, occupancy.SlotIndex),
                    info));
            }

            return buffer.Count;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void SetParkedCarVisualHeld(ushort parkedId, bool held)
        {
            if (parkedId == 0)
                return;

            bool changed = held
                ? HeldParkedVisuals.Add(parkedId)
                : HeldParkedVisuals.Remove(parkedId);
            if (changed)
                UndergroundParkingVisualManager.RequestParkedCarRefresh();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryBuildInternalParkingJourney(
            ushort parkedId,
            Vector3 start,
            List<Vector3> waypoints,
            out UndergroundParkingInternalJourneyPlan plan)
        {
            plan = default(UndergroundParkingInternalJourneyPlan);
            if (parkedId == 0 || waypoints == null)
                return false;

            ulong slotKey;
            SlotOccupancy destination;
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            if (!ParkedVehicleSlots.TryGetValue(parkedId, out slotKey)
                || !OccupiedSlots.TryGetValue(slotKey, out destination)
                || destination.ParkedId != parkedId
                || !TryGetPortalForFacility(destination.FacilityId, out facility, out connection))
            {
                return false;
            }

            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null || parkedId >= vehicleManager.m_parkedVehicles.m_size)
                return false;
            VehicleInfo info = vehicleManager.m_parkedVehicles.m_buffer[parkedId].Info;
            if (info == null)
                return false;

            facility.GarageCenter =
                UndergroundParkingGeometry.ResolveCurrentVisualGarageCenter(facility);
            int slotsPerLevel = GetSpacesPerFloor(facility);
            int destinationLevel = Mathf.Clamp(
                destination.SlotIndex / slotsPerLevel,
                0,
                facility.FloorCount - 1);

            UndergroundParkingLaneLayout layout;
            UndergroundParkingBay bay;
            if (!TryGetLaneLayout(facility, out layout)
                || !TryGetBay(facility, destination.SlotIndex, layout, out bay))
            {
                return false;
            }

            waypoints.Clear();
            Vector3 currentRampPoint;
            Vector3 forward;
            Vector3 right;
            float halfWidth;
            float halfLength;
            AppendInternalRampRoute(
                facility,
                start,
                destinationLevel,
                layout,
                waypoints,
                out currentRampPoint,
                out forward,
                out right,
                out halfWidth,
                out halfLength);
            Vector3 centre = facility.GarageCenter;

            Quaternion garageRotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 localCurrent = Quaternion.Inverse(garageRotation)
                                   * (currentRampPoint - centre);
            Vector3 localCrossStart;
            Vector3 localCrossTarget;
            if (layout.AislesAlongForward)
            {
                localCrossStart = new Vector3(
                    Mathf.Clamp(localCurrent.x, -halfWidth, halfWidth),
                    0f,
                    layout.CrossAisleCoordinate);
                localCrossTarget = new Vector3(
                    bay.LocalLanePosition.x,
                    0f,
                    layout.CrossAisleCoordinate);
            }
            else
            {
                localCrossStart = new Vector3(
                    layout.CrossAisleCoordinate,
                    0f,
                    Mathf.Clamp(localCurrent.z, -halfLength, halfLength));
                localCrossTarget = new Vector3(
                    layout.CrossAisleCoordinate,
                    0f,
                    bay.LocalLanePosition.z);
            }

            float levelY = GetGarageLevelY(facility, destinationLevel);
            AddDistinctJourneyWaypoint(
                waypoints,
                LocalGaragePointToWorld(centre, garageRotation, localCrossStart, levelY));
            AddDistinctJourneyWaypoint(
                waypoints,
                LocalGaragePointToWorld(centre, garageRotation, localCrossTarget, levelY));
            AddDistinctJourneyWaypoint(
                waypoints,
                LocalGaragePointToWorld(centre, garageRotation, bay.LocalLanePosition, levelY));
            AddDistinctJourneyWaypoint(
                waypoints,
                LocalGaragePointToWorld(centre, garageRotation, bay.LocalPosition, levelY));
            ApplyInternalTrafficLaneOffset(waypoints);
            plan.Facility = facility;
            plan.FinalRotation = GetUndergroundParkingSlotRotation(facility, destination.SlotIndex);
            plan.Info = info;
            return waypoints.Count >= 2;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool TryGetManagedParkedSlot(
            ushort parkedId,
            out int facilityId,
            out int slotIndex)
        {
            facilityId = 0;
            slotIndex = -1;
            ulong slotKey;
            SlotOccupancy occupancy;
            if (parkedId == 0
                || !ParkedVehicleSlots.TryGetValue(parkedId, out slotKey)
                || !OccupiedSlots.TryGetValue(slotKey, out occupancy)
                || occupancy.ParkedId != parkedId)
            {
                return false;
            }

            facilityId = occupancy.FacilityId;
            slotIndex = occupancy.SlotIndex;
            return facilityId > 0 && slotIndex >= 0;
        }

        public static bool TryBuildInternalDepartureJourney(
            UndergroundParkingFacility facility,
            int slotIndex,
            Vector3 portalEnd,
            List<Vector3> waypoints,
            out Quaternion finalRotation)
        {
            finalRotation = Quaternion.identity;
            if (!facility.IsValid || slotIndex < 0 || waypoints == null)
                return false;

            facility.GarageCenter =
                UndergroundParkingGeometry.ResolveCurrentVisualGarageCenter(facility);
            UndergroundParkingLaneLayout layout;
            UndergroundParkingBay bay;
            if (!TryGetLaneLayout(facility, out layout)
                || !TryGetBay(facility, slotIndex, layout, out bay))
            {
                return false;
            }

            int slotsPerLevel = GetSpacesPerFloor(facility);
            int level = Mathf.Clamp(
                slotIndex / slotsPerLevel,
                0,
                facility.FloorCount - 1);
            Vector3 centre = facility.GarageCenter;
            List<Vector3> arrivalRoute = new List<Vector3>();
            Vector3 currentRampPoint;
            Vector3 forward;
            Vector3 right;
            float halfWidth;
            float halfLength;
            AppendInternalRampRoute(
                facility,
                portalEnd,
                level,
                layout,
                arrivalRoute,
                out currentRampPoint,
                out forward,
                out right,
                out halfWidth,
                out halfLength);

            Quaternion garageRotation = Quaternion.LookRotation(forward, Vector3.up);
            Quaternion inverseRotation = Quaternion.Inverse(garageRotation);
            Vector3 localCurrent = inverseRotation * (currentRampPoint - centre);
            Vector3 localCrossStart;
            Vector3 localCrossTarget;
            if (layout.AislesAlongForward)
            {
                localCrossStart = new Vector3(
                    Mathf.Clamp(localCurrent.x, -halfWidth, halfWidth),
                    0f,
                    layout.CrossAisleCoordinate);
                localCrossTarget = new Vector3(
                    bay.LocalLanePosition.x,
                    0f,
                    layout.CrossAisleCoordinate);
            }
            else
            {
                localCrossStart = new Vector3(
                    layout.CrossAisleCoordinate,
                    0f,
                    Mathf.Clamp(localCurrent.z, -halfLength, halfLength));
                localCrossTarget = new Vector3(
                    layout.CrossAisleCoordinate,
                    0f,
                    bay.LocalLanePosition.z);
            }

            float levelY = GetGarageLevelY(facility, level);
            AddDistinctJourneyWaypoint(
                arrivalRoute,
                LocalGaragePointToWorld(centre, garageRotation, localCrossStart, levelY));
            AddDistinctJourneyWaypoint(
                arrivalRoute,
                LocalGaragePointToWorld(centre, garageRotation, localCrossTarget, levelY));
            AddDistinctJourneyWaypoint(
                arrivalRoute,
                LocalGaragePointToWorld(centre, garageRotation, bay.LocalLanePosition, levelY));
            AddDistinctJourneyWaypoint(
                arrivalRoute,
                LocalGaragePointToWorld(centre, garageRotation, bay.LocalPosition, levelY));

            waypoints.Clear();
            for (int i = arrivalRoute.Count - 1; i >= 0; i--)
                AddDistinctJourneyWaypoint(waypoints, arrivalRoute[i]);
            ApplyInternalTrafficLaneOffset(waypoints);
            if (waypoints.Count < 2)
                return false;
            Vector3 finalDirection = waypoints[waypoints.Count - 1]
                                     - waypoints[waypoints.Count - 2];
            finalRotation = finalDirection.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(finalDirection.normalized, Vector3.up)
                : GetUndergroundParkingSlotRotation(facility, slotIndex);
            return true;
        }

        internal static bool TryGetInternalRampTopGeometry(
            UndergroundParkingFacility facility,
            float worldY,
            out Vector3 rampTop,
            out Vector3 mouthAxis,
            out Vector3 wallNormal)
        {
            rampTop = Vector3.zero;
            mouthAxis = Vector3.right;
            wallNormal = Vector3.forward;
            if (!facility.IsValid)
                return false;

            facility.GarageCenter =
                UndergroundParkingGeometry.ResolveCurrentVisualGarageCenter(facility);
            UndergroundParkingLaneLayout layout;
            if (!TryGetLaneLayout(facility, out layout))
                return false;

            Vector3 forward = GetGarageForward(facility);
            Vector3 right = GetGarageRight(forward);
            Quaternion garageRotation = Quaternion.LookRotation(forward, Vector3.up);
            rampTop = LocalGaragePointToWorld(
                facility.GarageCenter,
                garageRotation,
                layout.LocalRampTopPosition,
                worldY);
            if (layout.AislesAlongForward)
            {
                mouthAxis = right;
                wallNormal = forward * layout.EntranceSign;
            }
            else
            {
                mouthAxis = forward;
                wallNormal = right * layout.EntranceSign;
            }
            mouthAxis = NormalizeFlat(mouthAxis, Vector3.right);
            wallNormal = NormalizeFlat(wallNormal, Vector3.forward);
            return true;
        }

        private static void AppendInternalRampRoute(
            UndergroundParkingFacility facility,
            Vector3 portalEnd,
            int destinationLevel,
            UndergroundParkingLaneLayout layout,
            List<Vector3> waypoints,
            out Vector3 currentRampPoint,
            out Vector3 forward,
            out Vector3 right,
            out float halfWidth,
            out float halfLength)
        {
            forward = GetGarageForward(facility);
            right = GetGarageRight(forward);
            Vector3 centre = facility.GarageCenter;
            halfWidth = Mathf.Max(2f, facility.GarageWidth * 0.5f - 2f);
            halfLength = Mathf.Max(2f, facility.GarageLength * 0.5f - 2f);
            Quaternion garageRotation = Quaternion.LookRotation(forward, Vector3.up);
            float entryLevelY = GetGarageLevelY(facility, 0);
            Vector3 rampHigh;
            if (!TryGetInternalRampTopGeometry(
                    facility,
                    entryLevelY,
                    out rampHigh,
                    out _,
                    out _))
            {
                rampHigh = LocalGaragePointToWorld(
                    centre,
                    garageRotation,
                    layout.LocalRampTopPosition,
                    entryLevelY);
            }
            Vector3 rampLow = LocalGaragePointToWorld(
                centre,
                garageRotation,
                layout.LocalIngressPosition,
                entryLevelY);
            AddDistinctJourneyWaypoint(waypoints, portalEnd);
            Vector3 tunnelSurfaceEntry = Vector3.zero;
            Vector3 tunnelGarageExit = Vector3.zero;
            bool hasBuildingTunnel = facility.TargetBuildingId != 0
                && UndergroundParkingVisualManager.TryGetExistingTunnelTraversal(
                    facility,
                    out tunnelSurfaceEntry,
                    out tunnelGarageExit)
                && FlatDistance(portalEnd, tunnelSurfaceEntry) <= 1f;
            if (hasBuildingTunnel)
            {
                // Building-attached arrivals hand off in the upper chamber.
                // The neutral journey owns the complete existing tunnel from
                // that exact point to the projected level-0 chamber.
                AddDistinctJourneyWaypoint(waypoints, tunnelGarageExit);
            }
            if (facility.TargetBuildingId == 0)
            {
                // The kiosk handoff is already the high end of its internal
                // ramp. Descend directly and smoothly from that exact pose;
                // inserting the wall-derived floor point first caused both a
                // horizontal reversal and a vertical correction.
                AddDistinctJourneyWaypoint(
                    waypoints,
                    Vector3.Lerp(portalEnd, rampLow, 0.5f));
            }
            else
            {
                AddDistinctJourneyWaypoint(waypoints, rampHigh);
                AddDistinctJourneyWaypoint(
                    waypoints,
                    Vector3.Lerp(rampHigh, rampLow, 0.5f));
            }
            AddDistinctJourneyWaypoint(waypoints, rampLow);

            float availableRun = Mathf.Max(4f, FlatDistance(rampHigh, rampLow));
            Vector3 rampDirection = NormalizeFlat(rampHigh - rampLow, -facility.Side);
            currentRampPoint = rampLow;
            for (int level = 0; level < destinationLevel; level++)
            {
                Vector3 nextRampLow = currentRampPoint + rampDirection * availableRun;
                nextRampLow = ClampJourneyPointToGarage(
                    facility,
                    nextRampLow,
                    right,
                    forward,
                    2f);
                nextRampLow.y = GetGarageLevelY(facility, level + 1);
                AddDistinctJourneyWaypoint(
                    waypoints,
                    Vector3.Lerp(currentRampPoint, nextRampLow, 0.5f));
                AddDistinctJourneyWaypoint(waypoints, nextRampLow);
                currentRampPoint = nextRampLow;

                if (level + 1 < destinationLevel)
                {
                    Vector3 cross = right * (level % 2 == 0 ? 3.85f : -3.85f);
                    Vector3 returnPoint = ClampJourneyPointToGarage(
                        facility,
                        currentRampPoint + cross - rampDirection * availableRun,
                        right,
                        forward,
                        2f);
                    returnPoint.y = currentRampPoint.y;
                    AddDistinctJourneyWaypoint(waypoints, currentRampPoint + cross * 0.5f);
                    AddDistinctJourneyWaypoint(waypoints, returnPoint);
                    currentRampPoint = returnPoint;
                    rampDirection = -rampDirection;
                }
            }
        }

        private static float FlatDistance(Vector3 left, Vector3 right)
        {
            Vector3 delta = left - right;
            delta.y = 0f;
            return delta.magnitude;
        }

        private static void AddDistinctJourneyWaypoint(List<Vector3> waypoints, Vector3 point)
        {
            if (waypoints.Count == 0
                || (waypoints[waypoints.Count - 1] - point).sqrMagnitude > 0.04f)
            {
                waypoints.Add(point);
            }
        }

        private static void ApplyInternalTrafficLaneOffset(List<Vector3> waypoints)
        {
            if (waypoints == null || waypoints.Count < 3)
                return;

            SimulationManager simulationManager = SimulationManager.instance;
            bool leftHandTraffic = simulationManager != null
                                   && simulationManager.m_metaData != null
                                   && (int)simulationManager.m_metaData.m_invertTraffic == 2;
            float side = leftHandTraffic ? -1f : 1f;
            Vector3[] centreLine = waypoints.ToArray();
            for (int i = 1; i < centreLine.Length - 1; i++)
            {
                Vector3 tangent = centreLine[i + 1] - centreLine[i - 1];
                tangent.y = 0f;
                if (tangent.sqrMagnitude <= 0.001f)
                    continue;
                tangent.Normalize();
                Vector3 roadRight = Vector3.Cross(Vector3.up, tangent);
                waypoints[i] = centreLine[i]
                               + roadRight * (InternalTrafficLaneOffset * side);
            }
        }

        private static Vector3 ClampJourneyPointToGarage(
            UndergroundParkingFacility facility,
            Vector3 point,
            Vector3 right,
            Vector3 forward,
            float clearance)
        {
            Vector3 delta = point - facility.GarageCenter;
            float x = Mathf.Clamp(
                Vector3.Dot(delta, right),
                -facility.GarageWidth * 0.5f + clearance,
                facility.GarageWidth * 0.5f - clearance);
            float z = Mathf.Clamp(
                Vector3.Dot(delta, forward),
                -facility.GarageLength * 0.5f + clearance,
                facility.GarageLength * 0.5f - clearance);
            return facility.GarageCenter + right * x + forward * z + Vector3.up * delta.y;
        }

        private static int FindNearestOpenJourneyCell(
            UndergroundParkingFacility facility,
            int level,
            Vector3 position,
            ushort arrivingParkedId,
            int columns,
            int rows,
            int destinationCell)
        {
            int slotsPerLevel = columns * rows;
            int bestCell = -1;
            float bestDistance = float.MaxValue;
            for (int cell = 0; cell < slotsPerLevel; cell++)
            {
                if (cell != destinationCell
                    && IsJourneyCellBlocked(
                        facility.Id,
                        level,
                        cell,
                        arrivingParkedId,
                        slotsPerLevel))
                {
                    continue;
                }

                Vector3 cellPosition = GetUndergroundParkingSlotPosition(
                    facility,
                    level * slotsPerLevel + cell);
                float distance = (cellPosition - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestCell = cell;
                }
            }

            return bestCell;
        }

        private static bool AppendCollisionFreeGridRoute(
            UndergroundParkingFacility facility,
            int level,
            int startCell,
            int destinationCell,
            ushort arrivingParkedId,
            int columns,
            int rows,
            List<Vector3> waypoints)
        {
            int cellCount = columns * rows;
            if (startCell < 0 || startCell >= cellCount
                || destinationCell < 0 || destinationCell >= cellCount)
            {
                return false;
            }

            float[] scores = new float[cellCount];
            int[] previous = new int[cellCount];
            bool[] open = new bool[cellCount];
            bool[] closed = new bool[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                scores[i] = float.MaxValue;
                previous[i] = -1;
            }

            scores[startCell] = 0f;
            open[startCell] = true;
            while (true)
            {
                int current = -1;
                float best = float.MaxValue;
                for (int cell = 0; cell < cellCount; cell++)
                {
                    if (!open[cell])
                        continue;
                    int x = cell % columns;
                    int z = cell / columns;
                    int targetX = destinationCell % columns;
                    int targetZ = destinationCell / columns;
                    float estimate = scores[cell]
                                     + Mathf.Abs(x - targetX)
                                     + Mathf.Abs(z - targetZ);
                    if (estimate < best)
                    {
                        best = estimate;
                        current = cell;
                    }
                }

                if (current < 0)
                    return false;
                if (current == destinationCell)
                    break;

                open[current] = false;
                closed[current] = true;
                int currentX = current % columns;
                int currentZ = current / columns;
                int[] neighbours =
                {
                    currentX > 0 ? current - 1 : -1,
                    currentX + 1 < columns ? current + 1 : -1,
                    currentZ > 0 ? current - columns : -1,
                    currentZ + 1 < rows ? current + columns : -1
                };
                for (int neighbourIndex = 0; neighbourIndex < neighbours.Length; neighbourIndex++)
                {
                    int neighbour = neighbours[neighbourIndex];
                    if (neighbour < 0
                        || closed[neighbour]
                        || (neighbour != destinationCell
                            && IsJourneyCellBlocked(
                                facility.Id,
                                level,
                                neighbour,
                                arrivingParkedId,
                                cellCount)))
                    {
                        continue;
                    }

                    float candidate = scores[current] + 1f;
                    if (candidate >= scores[neighbour])
                        continue;
                    scores[neighbour] = candidate;
                    previous[neighbour] = current;
                    open[neighbour] = true;
                }
            }

            List<int> reverseRoute = new List<int>();
            int routeCell = destinationCell;
            while (routeCell >= 0)
            {
                reverseRoute.Add(routeCell);
                if (routeCell == startCell)
                    break;
                routeCell = previous[routeCell];
            }
            if (reverseRoute.Count == 0
                || reverseRoute[reverseRoute.Count - 1] != startCell)
            {
                return false;
            }

            for (int routeIndex = reverseRoute.Count - 1; routeIndex >= 0; routeIndex--)
            {
                int cell = reverseRoute[routeIndex];
                AddDistinctJourneyWaypoint(
                    waypoints,
                    GetUndergroundParkingSlotPosition(
                        facility,
                        level * cellCount + cell));
            }

            return true;
        }

        private static bool IsJourneyCellBlocked(
            int facilityId,
            int level,
            int cell,
            ushort arrivingParkedId,
            int slotsPerLevel)
        {
            SlotOccupancy occupancy;
            return OccupiedSlots.TryGetValue(
                       MakeSlotKey(facilityId, level * slotsPerLevel + cell),
                       out occupancy)
                   && occupancy.ParkedId != 0
                   && occupancy.ParkedId != arrivingParkedId;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool HasAssignedCarsOnRemovedFloors(
            UndergroundParkingFacility facility,
            int remainingFloorCount,
            out int parkedCars)
        {
            parkedCars = 0;
            if (!facility.IsValid)
                return false;

            int firstRemovedSlot = GetManagedSlotCapacity(
                facility.WithFloorCount(remainingFloorCount));
            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                SlotOccupancy occupancy = pair.Value;
                if (occupancy.FacilityId == facility.Id
                    && occupancy.ParkedId != 0
                    && occupancy.SlotIndex >= firstRemovedSlot)
                {
                    parkedCars++;
                }
            }

            return parkedCars > 0;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool HasTransientClaimsOnRemovedFloors(
            UndergroundParkingFacility facility,
            int remainingFloorCount,
            out int claims)
        {
            claims = 0;
            if (!facility.IsValid)
                return false;

            int firstRemovedSlot = GetManagedSlotCapacity(
                facility.WithFloorCount(remainingFloorCount));
            foreach (KeyValuePair<ulong, SlotReservation> pair in Reservations)
            {
                SlotReservation reservation = pair.Value;
                if (reservation.FacilityId == facility.Id
                    && reservation.SlotIndex >= firstRemovedSlot)
                {
                    claims++;
                }
            }

            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                SlotOccupancy occupancy = pair.Value;
                if (occupancy.FacilityId == facility.Id
                    && occupancy.ParkedId == 0
                    && occupancy.SlotIndex >= firstRemovedSlot)
                {
                    claims++;
                }
            }

            return claims > 0;
        }

        internal static bool IsFacilityAcceptingArrivals(UndergroundParkingFacility facility)
        {
            BuildingManager manager = BuildingManager.instance;
            if (!facility.IsValid
                || manager == null
                || !UndergroundParkingRegistry.IsFacilityOpen(facility)
                || UndergroundParkingRegistry.IsEntranceRelocationPending(facility.Id))
            {
                return false;
            }

            if (facility.TargetBuildingId != 0)
            {
                Building target;
                return UndergroundParkingGeometry.TryGetUsableBuilding(facility.TargetBuildingId, out target);
            }

            if (facility.EntranceBuildingId == 0 || facility.EntranceBuildingId >= manager.m_buildings.m_size)
                return false;

            Building building = manager.m_buildings.m_buffer[facility.EntranceBuildingId];
            bool accepting = (building.m_flags & Building.Flags.Created) != 0
                             && (building.m_flags & Building.Flags.Deleted) == 0
                             && (building.m_flags & Building.Flags.Active) != 0;
            if (!accepting && _disabledIngressLogCount < DisabledIngressLogLimit)
            {
                _disabledIngressLogCount++;
                UndergroundParkingLog.Advanced("UPG arrival rejected because entrance is switched off: facility="
                                            + facility.Id
                                            + " building="
                                            + facility.EntranceBuildingId);
            }

            return accepting;
        }

        private static bool IsMotorcycleParkedVehicle(ushort parkedId)
        {
            VehicleManager manager = VehicleManager.instance;
            if (manager == null || parkedId == 0 || parkedId >= manager.m_parkedVehicles.m_size)
                return false;

            VehicleInfo info = manager.m_parkedVehicles.m_buffer[parkedId].Info;
            string name = info == null ? string.Empty : info.name ?? string.Empty;
            string lower = name.ToLowerInvariant();
            return lower.Contains("motorcycle")
                   || lower.Contains("motorbike")
                   || lower.Contains("scooter")
                   || lower.Contains("moped")
                   || lower.Contains("personal electric transport");
        }

        private static int CountAssignedParkedCars()
        {
            int count = 0;
            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                if (pair.Value.ParkedId != 0)
                    count++;
            }

            return count;
        }

        private static int CountPendingSlotClaims()
        {
            int count = 0;
            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                if (pair.Value.ParkedId == 0)
                    count++;
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void LogOccupancySnapshot()
        {
            int facilityCount = EnsureFacilityCache();
            if (facilityCount == 0)
                return;

            int parked = CountAssignedParkedCars();
            int pending = CountPendingSlotClaims();

            if (facilityCount == _lastLoggedFacilities
                && parked == _lastLoggedParked
                && pending == _lastLoggedPending
                && Reservations.Count == _lastLoggedReservations)
            {
                return;
            }

            _lastLoggedFacilities = facilityCount;
            _lastLoggedParked = parked;
            _lastLoggedPending = pending;
            _lastLoggedReservations = Reservations.Count;
            UndergroundParkingLog.Advanced("UPG parking occupancy: facilities="
                                        + facilityCount
                                        + " parked="
                                        + parked
                                        + " pending="
                                        + pending
                                        + " capacity="
                                        + GetCachedTotalCapacity(facilityCount)
                                        + " reservations="
                                        + Reservations.Count);
        }

        private static int FindFirstFreeSlot(
            UndergroundParkingFacility facility,
            int slotCapacity,
            ushort ignoreParked)
        {
            if (slotCapacity <= 0)
                return -1;

            int startSlot;
            if (!NextFreeSlotHints.TryGetValue(facility.Id, out startSlot)
                || startSlot < 0
                || startSlot >= slotCapacity)
            {
                startSlot = 0;
            }

            // Allocation follows the ordinary stable slot sequence from the
            // rotating free-slot hint. Tunnel feasibility and dedicated aisle
            // geometry now protect circulation, so distance from the entrance
            // is no longer an assignment restriction.
            for (int offset = 0; offset < slotCapacity; offset++)
            {
                int slot = (startSlot + offset) % slotCapacity;
                ulong key = MakeSlotKey(facility, slot);
                if (Reservations.ContainsKey(key))
                    continue;

                SlotOccupancy occupancy;
                if (OccupiedSlots.TryGetValue(key, out occupancy)
                    && (ignoreParked == 0 || occupancy.ParkedId != ignoreParked))
                {
                    continue;
                }

                NextFreeSlotHints[facility.Id] = slot;
                return slot;
            }

            return -1;
        }

        private static int QueueAssignedParkedVehiclesForVanillaRelease(
            UndergroundParkingFacility facility)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null)
                return 0;

            UndergroundParkingRoadConnection connection;
            string message;
            bool hasConnection = UndergroundParkingAccessManager.TryGetRoadConnection(
                facility,
                out connection,
                out message);
            uint groupId = GetSlotGroupId(facility);
            ParkedIdsToRelease.Clear();
            foreach (KeyValuePair<ushort, ulong> pair in ParkedVehicleSlots)
            {
                if (GetSlotGroupId(pair.Value) == groupId)
                    ParkedIdsToRelease.Add(pair.Key);
            }

            int queued = 0;
            for (int i = 0; i < ParkedIdsToRelease.Count; i++)
            {
                ushort parkedId = ParkedIdsToRelease[i];
                if (parkedId == 0 || parkedId >= vehicleManager.m_parkedVehicles.m_size)
                    continue;

                VehicleParked data =
                    vehicleManager.m_parkedVehicles.m_buffer[parkedId];
                if (!IsCreated(data))
                {
                    ReleaseParkedVehicleSlot(parkedId);
                    continue;
                }

                PendingVanillaReleases[parkedId] = new PendingVanillaRelease(
                    parkedId,
                    data.m_ownerCitizen,
                    data.Info == null ? string.Empty : data.Info.name,
                    hasConnection ? connection.SegmentId : (ushort)0,
                    hasConnection ? connection.LanePosition : facility.SurfaceRoadPosition,
                    hasConnection ? connection.LaneDirection : facility.Direction);
                ReleaseParkedVehicleSlot(parkedId);
                queued++;
            }

            ParkedIdsToRelease.Clear();
            RemoveOccupiedSlotsForFacility(facility.Id);
            return queued;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static int ProcessPendingVanillaReleases(int budget)
        {
            if (budget <= 0 || PendingVanillaReleases.Count == 0)
                return 0;

            int pendingBefore = PendingVanillaReleases.Count;
            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null)
                return 0;

            PendingVanillaReleaseIds.Clear();
            foreach (KeyValuePair<ushort, PendingVanillaRelease> pair in PendingVanillaReleases)
            {
                PendingVanillaReleaseIds.Add(pair.Key);
                if (PendingVanillaReleaseIds.Count >= budget)
                    break;
            }

            int resolved = 0;
            for (int i = 0; i < PendingVanillaReleaseIds.Count; i++)
            {
                ushort parkedId = PendingVanillaReleaseIds[i];
                PendingVanillaRelease release;
                if (!PendingVanillaReleases.TryGetValue(parkedId, out release))
                    continue;

                if (IsPendingVanillaReleaseIdentityGone(vehicleManager, release))
                {
                    PendingVanillaReleases.Remove(parkedId);
                    resolved++;
                    continue;
                }

                VehicleParked parkedData =
                    vehicleManager.m_parkedVehicles.m_buffer[parkedId];
                if (!IsPendingOwnerQuiescent(release, parkedData))
                {
                    // A live vehicle occupant or pedestrian instance may still
                    // own this exact parking transition. Keep both identities
                    // untouched until native simulation reaches a quiescent
                    // state; never publish either side at the old entrance.
                    continue;
                }

                if (!IsLiveRoadSegment(release.SegmentId)
                    || !TmpeParkingCompatibilityManager.HasNativeRelocationService)
                {
                    // Call the parked-record owner directly. Citizen.SetParkedVehicle
                    // is intentionally not invoked here because other parking
                    // integrations can treat that public callback as a request
                    // to materialize the exact owner at the stale retrieval pose.
                    vehicleManager.ReleaseParkedVehicle(parkedId);
                    if (IsPendingVanillaReleaseIdentityGone(vehicleManager, release))
                    {
                        PendingVanillaReleases.Remove(parkedId);
                        resolved++;
                    }
                    continue;
                }

                Vector3 parkPosition;
                Quaternion parkRotation;
                if (!TmpeParkingCompatibilityManager
                    .TryFindRelocationForEntranceBlockingParkedVehicle(
                        parkedId,
                        parkedData.Info,
                        release.SegmentId,
                        release.ReferencePosition,
                        release.LaneDirection,
                        out parkPosition,
                        out parkRotation))
                {
                    continue;
                }

                MoveParkedVehicle(
                    parkedId,
                    ref parkedData,
                    parkPosition,
                    parkRotation,
                    false);
                VehicleParked published =
                    vehicleManager.m_parkedVehicles.m_buffer[parkedId];
                if (IsCreated(published)
                    && !HasParkedFlag(published, ParkingFlag)
                    && (published.m_position - parkPosition).sqrMagnitude <= 0.01f)
                {
                    if (!TryRestorePendingVanillaReleaseOwner(release))
                        continue;

                    PendingVanillaReleases.Remove(parkedId);
                    resolved++;
                }
            }

            PendingVanillaReleaseIds.Clear();
            if (resolved > 0)
            {
                UndergroundParkingLog.Advanced(
                    "Verified pending UPG car/citizen identities released to vanilla: resolved="
                    + resolved
                    + " remaining="
                    + PendingVanillaReleases.Count);
            }
            if (pendingBefore > 0 && PendingVanillaReleases.Count == 0)
            {
                UndergroundParkingLog.Info(
                    "Verified all former UPG car/citizen identities released to vanilla; pendingVanillaReleases=0.");
                TmpeParkingCompatibilityManager.ReleaseRelocationServiceIfInactive();
            }
            return resolved;
        }

        private static bool IsPendingOwnerQuiescent(
            PendingVanillaRelease release,
            VehicleParked parkedData)
        {
            uint ownerCitizen = release.ExpectedOwnerCitizen;
            if (ownerCitizen == 0u)
                return parkedData.m_ownerCitizen == 0u;

            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null
                || ownerCitizen >= citizenManager.m_citizens.m_size
                || parkedData.m_ownerCitizen != ownerCitizen)
                return false;

            Citizen citizen = citizenManager.m_citizens.m_buffer[ownerCitizen];
            if ((citizen.m_parkedVehicle != 0
                 && citizen.m_parkedVehicle != release.ParkedId)
                || citizen.m_vehicle != 0)
                return false;

            ushort instanceId = citizen.m_instance;
            if (instanceId == 0)
                return true;
            if (instanceId >= citizenManager.m_instances.m_size)
                return false;

            CitizenInstance instance =
                citizenManager.m_instances.m_buffer[instanceId];
            return (instance.m_flags & CitizenInstance.Flags.Created) == 0;
        }

        private static bool TryRestorePendingVanillaReleaseOwner(
            PendingVanillaRelease release)
        {
            uint ownerCitizen = release.ExpectedOwnerCitizen;
            if (ownerCitizen == 0u)
                return true;

            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null
                || ownerCitizen >= citizenManager.m_citizens.m_size)
            {
                return false;
            }

            ref Citizen citizen = ref citizenManager.m_citizens.m_buffer[ownerCitizen];
            if (citizen.m_vehicle != 0)
                return false;

            ushort instanceId = citizen.m_instance;
            if (instanceId != 0)
            {
                if (instanceId >= citizenManager.m_instances.m_size)
                    return false;

                CitizenInstance instance =
                    citizenManager.m_instances.m_buffer[instanceId];
                if ((instance.m_flags & CitizenInstance.Flags.Created) != 0)
                    return false;
            }

            if (citizen.m_parkedVehicle == release.ParkedId)
                return true;
            if (citizen.m_parkedVehicle != 0)
                return false;

            // The owner is quiescent and the exact parked record has already
            // reached a verified conventional roadside position. Restore only
            // this nonzero association; never publish the cim at an entrance.
            citizen.SetParkedVehicle(ownerCitizen, release.ParkedId);
            return citizen.m_parkedVehicle == release.ParkedId;
        }

        private static bool IsPendingVanillaReleaseIdentityGone(
            VehicleManager vehicleManager,
            PendingVanillaRelease release)
        {
            if (release.ParkedId == 0
                || release.ParkedId >= vehicleManager.m_parkedVehicles.m_size)
            {
                return true;
            }

            VehicleParked data =
                vehicleManager.m_parkedVehicles.m_buffer[release.ParkedId];
            if (!IsCreated(data))
                return true;
            if (data.Info == null || data.Info.name != release.PrefabName)
                return true;
            return release.ExpectedOwnerCitizen != 0u
                   && data.m_ownerCitizen != release.ExpectedOwnerCitizen;
        }

        private static bool IsLiveRoadSegment(ushort segmentId)
        {
            NetManager netManager = NetManager.instance;
            if (netManager == null
                || segmentId == 0
                || segmentId >= netManager.m_segments.m_size)
            {
                return false;
            }

            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            return (segment.m_flags & NetSegment.Flags.Created) != 0
                   && (segment.m_flags & NetSegment.Flags.Deleted) == 0
                   && segment.Info != null;
        }

        private static bool TryGetManagedSlotIndex(
            UndergroundParkingFacility facility,
            Vector3 position,
            out int slotIndex)
        {
            slotIndex = -1;
            int slotsPerLevel = GetSpacesPerFloor(facility);
            int matchedLevel = -1;
            for (int level = 0; level < facility.FloorCount; level++)
            {
                if (Mathf.Abs(position.y - GetGarageLevelY(facility, level)) <= ManagedParkingHeightTolerance)
                {
                    matchedLevel = level;
                    break;
                }
            }

            if (matchedLevel < 0)
                return false;

            UndergroundParkingLaneLayout layout;
            if (!TryGetLaneLayout(facility, out layout))
                return TryGetLegacyManagedSlotIndex(facility, position, matchedLevel, out slotIndex);

            Quaternion rotation = Quaternion.LookRotation(GetGarageForward(facility), Vector3.up);
            Vector3 local = Quaternion.Inverse(rotation) * (position - facility.GarageCenter);
            int bestBay = -1;
            float bestDistance = float.MaxValue;
            for (int bayIndex = 0; bayIndex < layout.Bays.Count; bayIndex++)
            {
                Vector3 delta = layout.Bays[bayIndex].LocalPosition - local;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestBay = bayIndex;
                }
            }
            if (bestBay < 0 || bestDistance > 12.25f)
                return false;

            slotIndex = matchedLevel * slotsPerLevel + bestBay;
            return slotIndex < GetManagedSlotCapacity(facility);
        }

        private static bool TryGetLegacyManagedSlotIndex(
            UndergroundParkingFacility facility,
            Vector3 position,
            int matchedLevel,
            out int slotIndex)
        {
            slotIndex = -1;
            int columns;
            int rows;
            GetSlotGrid(facility, out columns, out rows);
            Vector3 forward = GetGarageForward(facility);
            Vector3 right = GetGarageRight(forward);
            Vector3 delta = position - facility.GarageCenter;
            delta.y = 0f;
            float usableWidth = GetUsableWidth(facility);
            float usableLength = GetUsableLength(facility);
            float localX = Vector3.Dot(delta, right) + usableWidth * 0.5f;
            float localZ = Vector3.Dot(delta, forward) + usableLength * 0.5f;
            int column = Mathf.FloorToInt(localX / SlotWidth);
            int row = Mathf.FloorToInt(localZ / SlotLength);
            if (column < 0 || column >= columns || row < 0 || row >= rows)
                return false;
            slotIndex = matchedLevel * columns * rows + row * columns + column;
            return slotIndex >= 0 && slotIndex < GetManagedSlotCapacity(facility);
        }

        private static int EnsureFacilityCache()
        {
            int revision = UndergroundParkingRegistry.Revision;
            if (_facilityCacheRevision == revision)
                return _facilityCacheCount;

            _facilityCacheRevision = revision;
            _facilityCacheCount = 0;
            FacilityCaches.Clear();

            int count = UndergroundParkingRegistry.CopyTo(Facilities);
            for (int i = 0; i < count; i++)
            {
                UndergroundParkingFacility facility = Facilities[i];
                if (!facility.IsValid)
                    continue;

                UndergroundParkingRoadConnection connection;
                string message;
                bool hasConnection = UndergroundParkingAccessManager.TryGetRoadConnection(
                    facility,
                    out connection,
                    out message);

                FacilityCaches.Add(new FacilityCache(
                    facility,
                    connection,
                    hasConnection,
                    GetManagedSlotCapacity(facility)));
                _facilityCacheCount++;
            }

            return _facilityCacheCount;
        }

        private static int CountConnectedFacilities()
        {
            int connected = 0;
            for (int i = 0; i < _facilityCacheCount; i++)
            {
                if (FacilityCaches[i].HasConnection)
                    connected++;
            }

            return connected;
        }

        private static bool IsRoadConnectionStillUsable(UndergroundParkingRoadConnection connection)
        {
            if (!connection.IsValid)
                return false;

            NetManager netManager = NetManager.instance;
            if (netManager == null || connection.SegmentId >= netManager.m_segments.m_size)
                return false;

            NetSegment segment = netManager.m_segments.m_buffer[connection.SegmentId];
            return (segment.m_flags & NetSegment.Flags.Created) != 0
                   && (segment.m_flags & NetSegment.Flags.Deleted) == 0
                   && segment.Info != null;
        }

        private static void BeginWarmup()
        {
            _warmupActive = true;
            _legacyMigrationActive = UndergroundParkingRegistry.NeedsLegacyParkingMigration;
            _warmupNextParkedId = 1;
        }

        private static void WarmupManagedParkedVehicleCache(int budget)
        {
            if (!_warmupActive)
                return;

            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null)
                return;

            int facilityCount = EnsureFacilityCache();
            if (facilityCount == 0)
            {
                _warmupActive = false;
                return;
            }

            int size = (int)vehicleManager.m_parkedVehicles.m_size;
            VehicleParked[] parkedVehicles = vehicleManager.m_parkedVehicles.m_buffer;
            int scanned = 0;
            while (_warmupNextParkedId < size && scanned < budget)
            {
                ushort parkedId = (ushort)_warmupNextParkedId;
                VehicleParked parkedData = parkedVehicles[_warmupNextParkedId];
                bool created = IsCreated(parkedData);
                if (created && PendingVanillaReleases.ContainsKey(parkedId))
                {
                    // Pending reset identities belong only to their persisted
                    // release transaction. Geometry discovery after a new
                    // garage is placed cannot adopt their former coordinates.
                    ReleaseParkedVehicleSlot(parkedId);
                    _warmupNextParkedId++;
                    scanned++;
                    continue;
                }

                ulong authoritativeKey;
                if (ParkedVehicleSlots.TryGetValue(parkedId, out authoritativeKey))
                {
                    SlotOccupancy authoritativeOccupancy;
                    if (!created)
                    {
                        ReleaseParkedVehicleSlot(parkedId);
                    }
                    else if (OccupiedSlots.TryGetValue(
                                 authoritativeKey,
                                 out authoritativeOccupancy)
                             && authoritativeOccupancy.ParkedId == parkedId)
                    {
                        // RestorePersistentAssignments and live arrival commits
                        // already own the exact facility/slot identity. Underground
                        // footprints can overlap in world space, so running those
                        // records back through geometry discovery can select an
                        // earlier facility and silently erase or overwrite the
                        // authoritative assignment. Warm-up may discover only
                        // records that do not already have an exact owner.
                        SetParkedFlags(ref parkedData, ParkingFlag);
                        parkedVehicles[_warmupNextParkedId] = parkedData;
                        _warmupNextParkedId++;
                        scanned++;
                        continue;
                    }
                    else
                    {
                        // A reverse-only entry is not authoritative and must not
                        // prevent the normal unassigned-record discovery below.
                        ParkedVehicleSlots.Remove(parkedId);
                    }
                }

                if (created)
                {
                    if (UndergroundParkingOccupancyHarmony
                            .IsPendingTmpeParkedIdentity(parkedId))
                    {
                        // A live TM:PE road transaction owns this early
                        // planning identity. Warm-up discovery must not turn
                        // it into occupancy or consume the reserved bay before
                        // the real car reaches the FIFO head.
                        _warmupNextParkedId++;
                        scanned++;
                        continue;
                    }

                    TryRelocateEntranceBlockingParkedVehicle(
                        parkedId,
                        ref parkedData);
                    UndergroundParkingFacility facility;
                    int slotIndex;
                    if (TryGetManagedSlotIndexFromCache(parkedData.m_position, out facility, out slotIndex))
                    {
                        if (_legacyMigrationActive)
                        {
                            UndergroundParkingRoadConnection connection;
                            string message;
                            if (UndergroundParkingAccessManager.TryGetRoadConnection(
                                    facility,
                                    out connection,
                                    out message))
                            {
                                Vector3 direction = NormalizeFlat(connection.LaneDirection, facility.Direction);
                                MoveParkedVehicle(
                                    parkedId,
                                    ref parkedData,
                                    connection.LanePosition,
                                    Quaternion.LookRotation(direction, Vector3.up),
                                    false);
                            }

                            ReleaseParkedVehicleSlot(parkedId);
                            _warmupNextParkedId++;
                            scanned++;
                            continue;
                        }

                        SetParkedFlags(ref parkedData, ParkingFlag);
                        parkedVehicles[_warmupNextParkedId] = parkedData;
                        RegisterManagedParkedVehicle(parkedId, facility, slotIndex);
                    }
                    else
                    {
                        ReleaseParkedVehicleSlot(parkedId);
                    }
                }

                _warmupNextParkedId++;
                scanned++;
            }

            if (_warmupNextParkedId < size)
                return;

            _warmupActive = false;
            if (_legacyMigrationActive)
            {
                _legacyMigrationActive = false;
                UndergroundParkingRegistry.MarkLegacyParkingMigrated();
                UndergroundParkingLog.Advanced("UPG legacy unrouted underground parked records moved back to surface.");
            }
            UndergroundParkingLog.Advanced("UPG parking occupancy cache warm-up complete: trackedParked="
                                        + ParkedVehicleSlots.Count
                                        + " occupiedSlots="
                                        + OccupiedSlots.Count
                                        + " reservations="
                                        + Reservations.Count);
        }

        private static bool TryRelocateEntranceBlockingParkedVehicle(
            ushort parkedId,
            ref VehicleParked parkedData)
        {
            VehicleInfo info = parkedData.Info;
            if (parkedId == 0 || info == null)
                return false;

            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                FacilityCache cache = FacilityCaches[i];
                if (!cache.Facility.IsValid
                    || !cache.HasConnection
                    || !IsRoadConnectionStillUsable(cache.Connection))
                {
                    continue;
                }

                Vector3 laneDirection = NormalizeFlat(
                    cache.Connection.LaneDirection,
                    cache.Facility.Direction);
                Vector3 parkedForward = NormalizeFlat(
                    parkedData.m_rotation * Vector3.forward,
                    laneDirection);
                if (Mathf.Abs(Vector3.Dot(parkedForward, laneDirection))
                        < 0.75f
                    || !IsInsideEntranceParkingClearance(
                        cache,
                        parkedData.m_position))
                {
                    continue;
                }

                Vector3 replacementPosition;
                Quaternion replacementRotation;
                if (TmpeParkingCompatibilityManager
                    .TryFindRelocationForEntranceBlockingParkedVehicle(
                        parkedId,
                        info,
                        cache.Connection.SegmentId,
                        cache.Connection.LanePosition,
                        cache.Connection.LaneDirection,
                        out replacementPosition,
                        out replacementRotation))
                {
                    Vector3 originalPosition = parkedData.m_position;
                    MoveParkedVehicle(
                        parkedId,
                        ref parkedData,
                        replacementPosition,
                        replacementRotation,
                        false);
                    if (_entranceBlockerRelocationLogCount++ < OfferLogLimit)
                    {
                        UndergroundParkingLog.Advanced(
                            "UPG relocated existing conventional parked car away from garage apron: parked="
                            + parkedId
                            + " facility="
                            + cache.Facility.Id
                            + " from="
                            + FormatVector(originalPosition)
                            + " to="
                            + FormatVector(replacementPosition)
                            + " ownership=tmpe-native-roadside-search");
                    }
                    return true;
                }

                if (_entranceBlockerRetainedLogCount++ < OfferLogLimit)
                {
                    UndergroundParkingLog.Warning(
                        "UPG found an existing parked car inside a garage apron but TM:PE supplied no safe same-road relocation; record retained: parked="
                        + parkedId
                        + " facility="
                        + cache.Facility.Id);
                }
                return false;
            }

            return false;
        }

        private static bool IsInsideEntranceParkingClearance(
            FacilityCache cache,
            Vector3 parkedPosition)
        {
            if (Mathf.Abs(cache.Connection.LanePosition.y
                         - parkedPosition.y)
                > EntranceParkingHeightTolerance)
            {
                return false;
            }

            Vector3 laneDirection = NormalizeFlat(
                cache.Connection.LaneDirection,
                cache.Facility.Direction);
            Vector3 roadPosition;
            Vector3 entrancePosition;
            Vector3 roadDirection;
            Vector3 entranceSide;
            if (!UndergroundParkingGeometry.TryGetCurrentPlacement(
                    cache.Facility,
                    out roadPosition,
                    out entrancePosition,
                    out roadDirection,
                    out entranceSide))
            {
                entranceSide = cache.Facility.Side;
            }
            entranceSide = NormalizeFlat(
                entranceSide,
                new Vector3(-laneDirection.z, 0f, laneDirection.x));
            Vector3 delta = parkedPosition
                            - cache.Connection.LanePosition;
            delta.y = 0f;
            float signedLongitudinal = Vector3.Dot(
                delta,
                laneDirection);
            Vector3 lateral = delta
                              - laneDirection * signedLongitudinal;

            // Protect only the entrance-side curb beside the apron. The
            // opposite curb and the rest of this road segment remain fully
            // TM:PE-owned parking territory.
            return Mathf.Abs(signedLongitudinal)
                       <= EntranceParkingClearanceAlongRoad
                   && lateral.sqrMagnitude
                      <= EntranceParkingClearanceAcrossRoad
                         * EntranceParkingClearanceAcrossRoad
                   && Vector3.Dot(lateral, entranceSide) >= -0.5f;
        }

        private static bool TryGetManagedSlotIndexFromCache(
            Vector3 position,
            out UndergroundParkingFacility facility,
            out int slotIndex)
        {
            facility = UndergroundParkingFacility.None;
            slotIndex = -1;

            int count = EnsureFacilityCache();
            for (int i = 0; i < count; i++)
            {
                UndergroundParkingFacility candidate = FacilityCaches[i].Facility;
                if (!candidate.IsValid)
                    continue;

                if (!TryGetManagedSlotIndex(candidate, position, out slotIndex))
                    continue;

                facility = candidate;
                return true;
            }

            return false;
        }

        private static Vector3 GetUndergroundParkingSlotPosition(UndergroundParkingFacility facility, int slotIndex)
        {
            UndergroundParkingLaneLayout layout;
            UndergroundParkingBay bay;
            if (TryGetLaneLayout(facility, out layout)
                && TryGetBay(facility, slotIndex, layout, out bay))
            {
                int laneLevel = Mathf.Clamp(
                    slotIndex / GetSpacesPerFloor(facility),
                    0,
                    facility.FloorCount - 1);
                Quaternion rotation = Quaternion.LookRotation(GetGarageForward(facility), Vector3.up);
                return LocalGaragePointToWorld(
                    facility.GarageCenter,
                    rotation,
                    bay.LocalPosition,
                    GetGarageLevelY(facility, laneLevel));
            }

            int columns;
            int rows;
            GetSlotGrid(facility, out columns, out rows);
            int slotsPerLevel = Mathf.Max(1, columns * rows);
            int level = Mathf.Clamp(slotIndex / slotsPerLevel, 0, facility.FloorCount - 1);
            int levelSlot = slotIndex - level * slotsPerLevel;
            int row = Mathf.Clamp(levelSlot / columns, 0, rows - 1);
            int column = Mathf.Clamp(levelSlot - row * columns, 0, columns - 1);

            float x = (-GetUsableWidth(facility) * 0.5f) + SlotWidth * 0.5f + column * SlotWidth;
            float z = (-GetUsableLength(facility) * 0.5f) + SlotLength * 0.5f + row * SlotLength;
            Vector3 forward = GetGarageForward(facility);
            Vector3 position = facility.GarageCenter
                               + GetGarageRight(forward) * x
                               + forward * z;
            position.y = GetGarageLevelY(facility, level);
            return position;
        }

        private static Quaternion GetUndergroundParkingSlotRotation(
            UndergroundParkingFacility facility,
            int slotIndex)
        {
            UndergroundParkingLaneLayout layout;
            UndergroundParkingBay bay;
            if (TryGetLaneLayout(facility, out layout)
                && TryGetBay(facility, slotIndex, layout, out bay))
            {
                Quaternion garageRotation = Quaternion.LookRotation(
                    GetGarageForward(facility),
                    Vector3.up);
                Vector3 worldDirection = garageRotation * bay.LocalParkingDirection;
                if (worldDirection.sqrMagnitude > 0.001f)
                    return Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
            }
            return Quaternion.LookRotation(GetGarageForward(facility), Vector3.up);
        }

        private static int GetManagedSlotCapacity(UndergroundParkingFacility facility)
        {
            return GetSpacesPerFloor(facility) * facility.FloorCount;
        }

        private static int GetSpacesPerFloor(UndergroundParkingFacility facility)
        {
            int columns;
            int rows;
            GetSlotGrid(facility, out columns, out rows);
            return Mathf.Max(1, columns * rows);
        }

        private static bool TryGetLaneLayout(
            UndergroundParkingFacility facility,
            out UndergroundParkingLaneLayout layout)
        {
            return UndergroundParkingLaneLayout.TryCreate(
                facility,
                GetSpacesPerFloor(facility),
                out layout);
        }

        internal static bool SupportsAutomatedTunnel(
            UndergroundParkingFacility facility)
        {
            if (!facility.IsValid || facility.TargetBuildingId == 0)
                return facility.IsValid;

            UndergroundParkingLaneLayout layout;
            return TryGetLaneLayout(facility, out layout)
                   && layout.SupportsAutomatedTunnel;
        }

        internal static bool SupportsAutomatedTunnel(int facilityId)
        {
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            return TryGetPortalForFacility(facilityId, out facility, out connection)
                   && SupportsAutomatedTunnel(facility);
        }

        private static bool TryGetBay(
            UndergroundParkingFacility facility,
            int slotIndex,
            UndergroundParkingLaneLayout layout,
            out UndergroundParkingBay bay)
        {
            bay = default(UndergroundParkingBay);
            if (layout == null || layout.Bays.Count == 0 || slotIndex < 0)
                return false;
            int bayIndex = slotIndex % GetSpacesPerFloor(facility);
            if (bayIndex < 0 || bayIndex >= layout.Bays.Count)
                return false;
            bay = layout.Bays[bayIndex];
            return true;
        }

        private static Vector3 LocalGaragePointToWorld(
            Vector3 garageCenter,
            Quaternion garageRotation,
            Vector3 localPoint,
            float worldY)
        {
            Vector3 world = garageCenter + garageRotation * localPoint;
            world.y = worldY;
            return world;
        }

        private static void GetSlotGrid(UndergroundParkingFacility facility, out int columns, out int rows)
        {
            columns = Mathf.Max(1, Mathf.FloorToInt(GetUsableWidth(facility) / SlotWidth));
            rows = Mathf.Max(1, Mathf.FloorToInt(GetUsableLength(facility) / SlotLength));
        }

        private static float GetUsableWidth(UndergroundParkingFacility facility)
        {
            return Mathf.Max(SlotWidth, facility.GarageWidth - SlotEdgePadding * 2f);
        }

        private static float GetUsableLength(UndergroundParkingFacility facility)
        {
            return Mathf.Max(SlotLength, facility.GarageLength - SlotEdgePadding * 2f);
        }

        internal static float GetGarageLevelY(UndergroundParkingFacility facility, int level)
        {
            // The detailed floor slab's upper face is 11% of one floor height
            // above each level's lower boundary and the painted bay prisms end
            // at another 2.4%. Use that exact marking-top plane for both stored
            // slot positions and x-ray car visuals on every floor.
            return facility.GarageCenter.y
                   + UndergroundParkingGeometry.GetGarageHeight(facility.FloorCount) * 0.5f
                   - UndergroundParkingGeometry.GarageFloorHeight * (Mathf.Max(0, level) + 1)
                   + UndergroundParkingGeometry.GarageFloorHeight * 0.134f;
        }

        private static int GetCachedTotalCapacity(int facilityCount)
        {
            int capacity = 0;
            for (int i = 0; i < facilityCount; i++)
                capacity += FacilityCaches[i].SlotCapacity;
            return capacity;
        }

        private static Vector3 GetGarageForward(UndergroundParkingFacility facility)
        {
            return NormalizeFlat(facility.GarageForward, facility.Side);
        }

        private static Vector3 GetGarageRight(Vector3 forward)
        {
            return new Vector3(forward.z, 0f, -forward.x);
        }

        private static void MoveParkedVehicle(
            ushort parkedId,
            ref VehicleParked parkedData,
            Vector3 position,
            Quaternion rotation,
            bool managed)
        {
            if (!UndergroundParkingAccessManager.IsFinite(position)
                || !UndergroundParkingAccessManager.IsFinite(rotation))
            {
                UndergroundParkingLog.Error(
                    "UPG refused invalid parked-vehicle pose publication: parked="
                    + parkedId);
                return;
            }

            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager != null)
            {
                try
                {
                    vehicleManager.RemoveFromGrid(parkedId, ref parkedData);
                }
                catch
                {
                }
            }

            parkedData.m_position = position;
            parkedData.m_rotation = rotation;
            if (managed)
                SetParkedFlags(ref parkedData, (ushort)(ParkingFlag | UpdatedFlag));
            else
            {
                ClearParkedFlags(ref parkedData, ParkingFlag);
                SetParkedFlags(ref parkedData, UpdatedFlag);
            }

            if (vehicleManager != null)
            {
                if (parkedId < vehicleManager.m_parkedVehicles.m_size)
                    vehicleManager.m_parkedVehicles.m_buffer[parkedId] = parkedData;

                try
                {
                    vehicleManager.AddToGrid(parkedId, ref parkedData);
                }
                catch
                {
                }
            }
        }

        private static void MarkSlotPending(UndergroundParkingFacility facility, int slotIndex)
        {
            ulong key = MakeSlotKey(facility, slotIndex);
            SlotOccupancy existing;
            if (OccupiedSlots.TryGetValue(key, out existing) && existing.ParkedId != 0)
                return;

            OccupiedSlots[key] = new SlotOccupancy(
                facility.Id,
                slotIndex,
                0,
                GetCurrentFrame() + PendingSlotClaimLifetimeFrames);
            NextFreeSlotHints[facility.Id] = slotIndex + 1;
        }

        private static void RegisterManagedParkedVehicle(
            ushort parkedId,
            UndergroundParkingFacility facility,
            int slotIndex)
        {
            if (parkedId == 0 || !facility.IsValid || slotIndex < 0)
                return;

            ulong key = MakeSlotKey(facility, slotIndex);
            ulong previousKey;
            if (ParkedVehicleSlots.TryGetValue(parkedId, out previousKey) && previousKey != key)
                OccupiedSlots.Remove(previousKey);

            ParkedVehicleSlots[parkedId] = key;
            OccupiedSlots[key] = new SlotOccupancy(facility.Id, slotIndex, parkedId, 0u);
            Reservations.Remove(key);
            UndergroundParkingVisualManager.RequestParkedCarRefresh();
        }

        private static void ReleaseParkedVehicleSlot(ushort parkedId)
        {
            ulong key;
            if (!ParkedVehicleSlots.TryGetValue(parkedId, out key))
                return;

            ParkedVehicleSlots.Remove(parkedId);
            SlotOccupancy occupancy;
            if (OccupiedSlots.TryGetValue(key, out occupancy) && occupancy.ParkedId == parkedId)
            {
                OccupiedSlots.Remove(key);
                NextFreeSlotHints[occupancy.FacilityId] = occupancy.SlotIndex;
                UndergroundParkingVisualManager.RequestParkedCarRefresh();
            }
        }

        private static void RemoveOccupiedSlotsForFacility(int facilityId)
        {
            if (facilityId <= 0 || OccupiedSlots.Count == 0)
                return;

            OccupiedKeysToRemove.Clear();
            ParkedIdsToRelease.Clear();
            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                if (pair.Value.FacilityId != facilityId)
                    continue;

                OccupiedKeysToRemove.Add(pair.Key);
                if (pair.Value.ParkedId != 0)
                    ParkedIdsToRelease.Add(pair.Value.ParkedId);
            }

            for (int i = 0; i < OccupiedKeysToRemove.Count; i++)
            {
                SlotOccupancy occupancy;
                if (OccupiedSlots.TryGetValue(OccupiedKeysToRemove[i], out occupancy))
                    NextFreeSlotHints[occupancy.FacilityId] = occupancy.SlotIndex;

                OccupiedSlots.Remove(OccupiedKeysToRemove[i]);
            }

            for (int i = 0; i < ParkedIdsToRelease.Count; i++)
                ParkedVehicleSlots.Remove(ParkedIdsToRelease[i]);

            OccupiedKeysToRemove.Clear();
            ParkedIdsToRelease.Clear();
            UndergroundParkingVisualManager.RequestParkedCarRefresh();
        }

        private static void RemoveReservationsForFacility(int facilityId)
        {
            if (Reservations.Count == 0)
                return;

            ReservationKeysToRemove.Clear();
            foreach (KeyValuePair<ulong, SlotReservation> pair in Reservations)
            {
                if (pair.Value.FacilityId == facilityId)
                    ReservationKeysToRemove.Add(pair.Key);
            }

            for (int i = 0; i < ReservationKeysToRemove.Count; i++)
                Reservations.Remove(ReservationKeysToRemove[i]);

            ReservationKeysToRemove.Clear();
        }

        private static void RemoveTransientClaimsOutsideCapacity(int facilityId, int capacity)
        {
            ReservationKeysToRemove.Clear();
            foreach (KeyValuePair<ulong, SlotReservation> pair in Reservations)
            {
                if (pair.Value.FacilityId == facilityId
                    && pair.Value.SlotIndex >= capacity)
                {
                    ReservationKeysToRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < ReservationKeysToRemove.Count; i++)
                Reservations.Remove(ReservationKeysToRemove[i]);
            ReservationKeysToRemove.Clear();

            OccupiedKeysToRemove.Clear();
            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                if (pair.Value.FacilityId == facilityId
                    && pair.Value.ParkedId == 0
                    && pair.Value.SlotIndex >= capacity)
                {
                    OccupiedKeysToRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < OccupiedKeysToRemove.Count; i++)
                OccupiedSlots.Remove(OccupiedKeysToRemove[i]);
            OccupiedKeysToRemove.Clear();
        }

        private static void RemoveReservationForSlot(UndergroundParkingFacility facility, int slotIndex)
        {
            Reservations.Remove(MakeSlotKey(facility, slotIndex));
        }

        private static void ExpireReservations(uint frame)
        {
            if (Reservations.Count == 0)
                return;

            ReservationKeysToRemove.Clear();
            foreach (KeyValuePair<ulong, SlotReservation> pair in Reservations)
            {
                if (IsFrameAtOrAfter(frame, pair.Value.ExpiresAt))
                    ReservationKeysToRemove.Add(pair.Key);
            }

            for (int i = 0; i < ReservationKeysToRemove.Count; i++)
                Reservations.Remove(ReservationKeysToRemove[i]);

            ReservationKeysToRemove.Clear();
        }

        private static void ExpirePendingSlotClaims(uint frame)
        {
            if (OccupiedSlots.Count == 0)
                return;

            OccupiedKeysToRemove.Clear();
            foreach (KeyValuePair<ulong, SlotOccupancy> pair in OccupiedSlots)
            {
                if (pair.Value.ParkedId != 0)
                    continue;

                if (IsFrameAtOrAfter(frame, pair.Value.PendingExpiresAt))
                    OccupiedKeysToRemove.Add(pair.Key);
            }

            for (int i = 0; i < OccupiedKeysToRemove.Count; i++)
                OccupiedSlots.Remove(OccupiedKeysToRemove[i]);

            OccupiedKeysToRemove.Clear();
        }

        private static ulong MakeSlotKey(UndergroundParkingFacility facility, int slotIndex)
        {
            return MakeSlotKey(facility.Id, slotIndex);
        }

        private static ulong MakeSlotKey(int facilityId, int slotIndex)
        {
            return ((ulong)GetSlotGroupId(facilityId) << 32) | (uint)Mathf.Max(0, slotIndex);
        }

        private static uint GetSlotGroupId(UndergroundParkingFacility facility)
        {
            return GetSlotGroupId(facility.Id);
        }

        private static uint GetSlotGroupId(int facilityId)
        {
            return 0x80000000u | (uint)Mathf.Max(0, facilityId);
        }

        private static uint GetSlotGroupId(ulong slotKey)
        {
            return (uint)(slotKey >> 32);
        }

        private static bool IsCreated(VehicleParked data)
        {
            return HasParkedFlag(data, CreatedFlag) && !HasParkedFlag(data, DeletedFlag);
        }

        private static bool HasParkedFlag(VehicleParked data, ushort flag)
        {
            return (data.m_flags & flag) != 0;
        }

        private static void SetParkedFlags(ref VehicleParked data, ushort flags)
        {
            data.m_flags = (ushort)(data.m_flags | flags);
        }

        private static void ClearParkedFlags(ref VehicleParked data, ushort flags)
        {
            data.m_flags = (ushort)(data.m_flags & ~flags);
        }

        private static uint GetCurrentFrame()
        {
            SimulationManager simulationManager = SimulationManager.instance;
            return simulationManager == null ? 0u : simulationManager.m_currentFrameIndex;
        }

        private static bool IsFrameAtOrAfter(uint frame, uint target)
        {
            return (int)(frame - target) >= 0;
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

        private static void WriteVector(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static Vector3 ReadVector(BinaryReader reader)
        {
            return new Vector3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void LogPreserved(ushort parkedId, int facilityId, int slotIndex, Vector3 position)
        {
            if (_preserveLogCount >= OfferLogLimit)
                return;

            _preserveLogCount++;
            UndergroundParkingLog.Advanced("UPG managed parked vehicle preserved: parked="
                                        + parkedId
                                        + " facility="
                                        + facilityId
                                        + " slot="
                                        + slotIndex
                                        + " pos="
                                        + FormatVector(position));
        }

        private struct SlotReservation
        {
            public readonly int FacilityId;
            public readonly int SlotIndex;
            public readonly uint ExpiresAt;
            public readonly ushort RoutedVehicleId;

            public SlotReservation(
                int facilityId,
                int slotIndex,
                uint expiresAt,
                ushort routedVehicleId)
            {
                FacilityId = facilityId;
                SlotIndex = slotIndex;
                ExpiresAt = expiresAt;
                RoutedVehicleId = routedVehicleId;
            }
        }

        private struct PersistentAssignment
        {
            public readonly ushort ParkedId;
            public readonly int FacilityId;
            public readonly int SlotIndex;
            public readonly string PrefabName;

            public PersistentAssignment(ushort parkedId, int facilityId, int slotIndex, string prefabName)
            {
                ParkedId = parkedId;
                FacilityId = facilityId;
                SlotIndex = slotIndex;
                PrefabName = prefabName ?? string.Empty;
            }
        }

        private struct PendingVanillaRelease
        {
            public readonly ushort ParkedId;
            public readonly uint ExpectedOwnerCitizen;
            public readonly string PrefabName;
            public readonly ushort SegmentId;
            public readonly Vector3 ReferencePosition;
            public readonly Vector3 LaneDirection;

            public PendingVanillaRelease(
                ushort parkedId,
                uint expectedOwnerCitizen,
                string prefabName,
                ushort segmentId,
                Vector3 referencePosition,
                Vector3 laneDirection)
            {
                ParkedId = parkedId;
                ExpectedOwnerCitizen = expectedOwnerCitizen;
                PrefabName = prefabName ?? string.Empty;
                SegmentId = segmentId;
                ReferencePosition = referencePosition;
                LaneDirection = laneDirection;
            }
        }

        private struct SlotOccupancy
        {
            public readonly int FacilityId;
            public readonly int SlotIndex;
            public readonly ushort ParkedId;
            public readonly uint PendingExpiresAt;

            public SlotOccupancy(int facilityId, int slotIndex, ushort parkedId, uint pendingExpiresAt)
            {
                FacilityId = facilityId;
                SlotIndex = slotIndex;
                ParkedId = parkedId;
                PendingExpiresAt = pendingExpiresAt;
            }
        }

        private struct FacilityCache
        {
            public readonly UndergroundParkingFacility Facility;
            public readonly UndergroundParkingRoadConnection Connection;
            public readonly bool HasConnection;
            public readonly int SlotCapacity;

            public FacilityCache(
                UndergroundParkingFacility facility,
                UndergroundParkingRoadConnection connection,
                bool hasConnection,
                int slotCapacity)
            {
                Facility = facility;
                Connection = connection;
                HasConnection = hasConnection;
                SlotCapacity = slotCapacity;
            }
        }
    }
}
