using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UndergroundParkingGarage
{
    /// <summary>
    /// Optional TM:PE Parking AI integration. UPG participates in TM:PE's
    /// pre-trip parking choice and supplies the same reserved bay again at the
    /// terminal roadside lookup. TM:PE remains sole owner of the vehicle path,
    /// target positions, movement, parked identity and deferred walking state.
    /// </summary>
    internal static class TmpeParkingCompatibilityManager
    {
        private const uint CandidateLifetimeFrames = 262144u;
        private const int CandidateChecksPerUpdate = 32;
        private const int LogLimit = 64;
        private const float MinimumEntranceHandoffDistance = 2.75f;
        private const float EntranceNoseClearance = 0.75f;
        private const float MaximumEntranceHandoffDistance = 8f;

        internal static float GetEntranceHandoffDistance(VehicleInfo info)
        {
            float vehicleLength = info != null && info.m_generatedInfo != null
                ? info.m_generatedInfo.m_size.z
                : 4f;
            if (vehicleLength <= 0.1f)
                vehicleLength = 4f;
            return Mathf.Clamp(
                vehicleLength * 0.5f + EntranceNoseClearance,
                MinimumEntranceHandoffDistance,
                MaximumEntranceHandoffDistance);
        }
        private static readonly Transaction[] Transactions = new Transaction[65536];
        private static int _candidateCursor = 1;
        private static Type _parkingLocationType;
        private static object _roadSideParkingLocation;
        private static MethodInfo _terminalRoadsideMethod;
        private static object _parkingManagerInstance;
        private static bool _active;
        private static int _offerLogCount;
        private static int _terminalLogCount;
        private static int _actualLaneLogCount;
        private static int _entranceParkingRejectionLogCount;
        private static int _entranceParkingRelocationLogCount;

        [ThreadStatic]
        private static ushort _terminalVehicleId;

        [ThreadStatic]
        private static ushort _terminalSegmentId;

        [ThreadStatic]
        private static byte _terminalSegmentOffset;

        [ThreadStatic]
        private static ushort _preTripSearchVehicleId;

        public static bool IsActive
        {
            get { return _active; }
        }

        public static bool HasNativeRelocationService
        {
            get
            {
                return _terminalRoadsideMethod != null
                       && _parkingManagerInstance != null;
            }
        }

        public static void ReleaseRelocationServiceIfInactive()
        {
            if (_active
                || UndergroundParkingOccupancyManager.HasPendingVanillaReleases)
            {
                return;
            }

            _terminalRoadsideMethod = null;
            _parkingManagerInstance = null;
        }

        public static int PatchOptionalTargets(Harmony harmony)
        {
            _active = false;
            _parkingLocationType = null;
            _roadSideParkingLocation = null;
            _terminalRoadsideMethod = null;
            _parkingManagerInstance = null;
            Type managerType = FindType(
                "TrafficManager.Manager.Impl.AdvancedParkingManager");
            if (managerType == null)
            {
                UndergroundParkingLog.Advanced(
                    "UPG TM:PE pre-trip parking integration inactive: parking manager not loaded.");
                return 0;
            }

            MethodInfo preTripOwner = FindPreTripOwnerTarget(managerType);
            MethodInfo vicinity = FindVicinityTarget(managerType);
            MethodInfo terminal = FindTerminalRoadsideTarget(managerType);
            MethodInfo enterParkedCar = FindEnterParkedCarTarget(managerType);
            if (preTripOwner == null
                || vicinity == null
                || terminal == null
                || enterParkedCar == null)
            {
                UndergroundParkingLog.Warning(
                    "UPG TM:PE parking integration inactive: supported search or retrieval targets not found.");
                return 0;
            }

            _parkingLocationType = vicinity.GetParameters()[6]
                .ParameterType.GetElementType();
            if (_parkingLocationType == null
                || !_parkingLocationType.IsEnum
                || !Enum.IsDefined(_parkingLocationType, "RoadSide"))
            {
                UndergroundParkingLog.Warning(
                    "UPG TM:PE pre-trip parking integration inactive: RoadSide parking contract not found.");
                _parkingLocationType = null;
                return 0;
            }
            _roadSideParkingLocation = Enum.Parse(
                _parkingLocationType,
                "RoadSide",
                false);
            HarmonyMethod vicinityPostfix = new HarmonyMethod(
                typeof(TmpeParkingCompatibilityManager).GetMethod(
                    "FindParkingSpaceInVicinityPostfix",
                    BindingFlags.Static | BindingFlags.NonPublic));
            vicinityPostfix.priority = Priority.Last;
            HarmonyMethod terminalPostfix = new HarmonyMethod(
                typeof(TmpeParkingCompatibilityManager).GetMethod(
                    "FindParkingSpaceRoadSideForVehiclePosPostfix",
                    BindingFlags.Static | BindingFlags.NonPublic));
            terminalPostfix.priority = Priority.Last;
            HarmonyMethod enterParkedCarPrefix = new HarmonyMethod(
                typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                    "TmpeEnterParkedCarPrefix",
                    BindingFlags.Static | BindingFlags.NonPublic));
            enterParkedCarPrefix.priority = Priority.First;
            HarmonyMethod enterParkedCarPostfix = new HarmonyMethod(
                typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                    "OwnerSpawnVehiclePostfix",
                    BindingFlags.Static | BindingFlags.NonPublic));
            enterParkedCarPostfix.priority = Priority.Last;

            harmony.Patch(
                preTripOwner,
                prefix: new HarmonyMethod(
                    typeof(TmpeParkingCompatibilityManager).GetMethod(
                        "FindParkingSpaceForCitizenPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                postfix: new HarmonyMethod(
                    typeof(TmpeParkingCompatibilityManager).GetMethod(
                        "FindParkingSpaceForCitizenPostfix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                finalizer: new HarmonyMethod(
                    typeof(TmpeParkingCompatibilityManager).GetMethod(
                        "FindParkingSpaceForCitizenFinalizer",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            harmony.Patch(vicinity, postfix: vicinityPostfix);
            harmony.Patch(terminal, postfix: terminalPostfix);
            harmony.Patch(
                enterParkedCar,
                prefix: enterParkedCarPrefix,
                postfix: enterParkedCarPostfix,
                finalizer: new HarmonyMethod(
                    typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "OwnerSpawnVehicleFinalizer",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            _terminalRoadsideMethod = terminal;
            FieldInfo instanceField = managerType.GetField(
                "Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _parkingManagerInstance = instanceField == null
                ? null
                : instanceField.GetValue(null);
            _active = true;
            UndergroundParkingLog.Advanced(
                "UPG TM:PE parking integration active: optionalTargets=4.");
            return 4;
        }

        public static bool TryGetPreselectedCandidate(
            ushort vehicleId,
            ref Vehicle vehicleData,
            PathUnit.Position parkingPosition,
            out TmpeParkingCandidate candidate)
        {
            candidate = default(TmpeParkingCandidate);
            if (!_active || vehicleId == 0)
                return false;

            Transaction stored = Transactions[vehicleId];
            if (stored.State != TransactionState.Candidate
                || !IsCandidateValid(vehicleId, ref vehicleData, stored)
                || parkingPosition.m_segment != stored.SegmentId)
            {
                if (stored.FacilityId > 0)
                    CancelCandidate(vehicleId);
                return false;
            }

            candidate = new TmpeParkingCandidate(
                stored.FacilityId,
                stored.SlotIndex,
                stored.SegmentId,
                stored.SegmentOffset);
            return true;
        }

        public static bool BeginTerminalParkingScope(
            ushort vehicleId,
            TmpeParkingCandidate candidate,
            UndergroundParkingRoadConnection arrivalConnection)
        {
            if (vehicleId == 0)
                return false;

            Transaction stored = Transactions[vehicleId];
            if (stored.State != TransactionState.Candidate
                || stored.FacilityId != candidate.FacilityId
                || stored.SlotIndex != candidate.SlotIndex
                || stored.SegmentId != candidate.SegmentId
                || stored.SegmentOffset != candidate.SegmentOffset
                || !arrivalConnection.IsValid
                || arrivalConnection.FacilityId != candidate.FacilityId
                || arrivalConnection.SegmentId != candidate.SegmentId)
                return false;

            Transactions[vehicleId] = stored.WithPreparedConnection(
                arrivalConnection);
            _terminalVehicleId = vehicleId;
            _terminalSegmentId = candidate.SegmentId;
            _terminalSegmentOffset = arrivalConnection.SegmentOffset;
            if (_actualLaneLogCount++ < LogLimit)
            {
                UndergroundParkingLog.Advanced(
                    "UPG TM:PE arrival bound to actual terminal lane: vehicle="
                    + vehicleId
                    + " facility="
                    + candidate.FacilityId
                    + " segment="
                    + arrivalConnection.SegmentId
                    + " lane="
                    + arrivalConnection.LaneIndex
                    + " offset="
                    + arrivalConnection.SegmentOffset
                    + " stop=("
                    + arrivalConnection.LanePosition.x.ToString("0.0") + ","
                    + arrivalConnection.LanePosition.y.ToString("0.0") + ","
                    + arrivalConnection.LanePosition.z.ToString("0.0") + ")");
            }
            return true;
        }

        public static void MarkPreparedArrivalAdopted(ushort vehicleId)
        {
            if (vehicleId == 0)
                return;
            Transaction transaction = Transactions[vehicleId];
            if (transaction.State == TransactionState.Prepared)
                Transactions[vehicleId] = transaction.WithState(TransactionState.Adopted);
        }

        public static bool IsAdoptedArrival(ushort vehicleId)
        {
            if (vehicleId == 0)
                return false;
            TransactionState state = Transactions[vehicleId].State;
            return state == TransactionState.Adopted
                   || state == TransactionState.Stopped;
        }

        public static bool TryGetPreparedArrivalOffset(
            ushort vehicleId,
            out byte segmentOffset)
        {
            segmentOffset = 0;
            if (vehicleId == 0)
                return false;

            Transaction transaction = Transactions[vehicleId];
            if (transaction.State != TransactionState.Prepared
                && transaction.State != TransactionState.Adopted
                && transaction.State != TransactionState.Stopped)
                return false;
            segmentOffset = transaction.SegmentOffset;
            return segmentOffset > 0;
        }

        public static bool TryGetArrivalConnection(
            ushort vehicleId,
            out UndergroundParkingRoadConnection connection)
        {
            connection = default(UndergroundParkingRoadConnection);
            if (vehicleId == 0)
                return false;
            Transaction transaction = Transactions[vehicleId];
            if ((transaction.State != TransactionState.Prepared
                 && transaction.State != TransactionState.Adopted
                 && transaction.State != TransactionState.Stopped)
                || !transaction.ArrivalConnection.IsValid)
                return false;
            connection = transaction.ArrivalConnection;
            return true;
        }

        public static bool TryMarkStopped(
            ushort vehicleId,
            Vector3 position,
            Vector3 forward,
            out int facilityId)
        {
            facilityId = 0;
            if (vehicleId == 0)
                return false;
            Transaction transaction = Transactions[vehicleId];
            if (transaction.State != TransactionState.Adopted
                && transaction.State != TransactionState.Stopped)
                return false;

            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection facilityConnection;
            UndergroundParkingRoadConnection currentConnection;
            if (!UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                    transaction.FacilityId,
                    out facility,
                    out facilityConnection)
                || facilityConnection.SegmentId != transaction.SegmentId
                || !UndergroundParkingAccessManager.TryGetArrivalConnection(
                    facility,
                    transaction.ArrivalConnection.LaneIndex,
                    transaction.SegmentOffset,
                    forward,
                    out currentConnection))
                return false;
            if (!UndergroundParkingOccupancyManager.IsAtRoadConnectionPortal(
                    currentConnection,
                    position,
                    forward))
                return false;
            transaction = transaction.WithConnectionAndState(
                currentConnection,
                TransactionState.Stopped);
            Transactions[vehicleId] = transaction;
            facilityId = transaction.FacilityId;
            return true;
        }

        public static bool IsStoppedArrival(ushort vehicleId)
        {
            return vehicleId != 0
                   && Transactions[vehicleId].State == TransactionState.Stopped;
        }

        public static void CompleteArrival(ushort vehicleId)
        {
            if (vehicleId == 0)
                return;

            Transactions[vehicleId] = default(Transaction);
            ClearTerminalScope(vehicleId);
        }

        public static void ClearTerminalScope(ushort vehicleId = 0)
        {
            if (vehicleId != 0 && _terminalVehicleId != vehicleId)
                return;

            _terminalVehicleId = 0;
            _terminalSegmentId = 0;
            _terminalSegmentOffset = 0;
        }

        public static bool HasCandidate(ushort vehicleId)
        {
            return vehicleId != 0
                   && Transactions[vehicleId].State == TransactionState.Candidate;
        }

        public static bool HasFacilityActivity(int facilityId)
        {
            if (facilityId <= 0)
                return false;

            for (int i = 1; i < Transactions.Length; i++)
            {
                if (Transactions[i].State != TransactionState.None
                    && Transactions[i].FacilityId == facilityId)
                    return true;
            }
            return false;
        }

        public static int CancelUncommittedCandidatesForFacility(
            int facilityId,
            out int restartedSearches)
        {
            restartedSearches = 0;
            if (facilityId <= 0)
                return 0;

            int cancelled = 0;
            for (int vehicleId = 1; vehicleId < Transactions.Length; vehicleId++)
            {
                Transaction transaction = Transactions[vehicleId];
                if (transaction.State != TransactionState.Candidate
                    || transaction.FacilityId != facilityId)
                    continue;

                CancelCandidate((ushort)vehicleId);
                cancelled++;
                if (UndergroundParkingOccupancyHarmony
                    .RestartTmpeParkingSearch((ushort)vehicleId))
                {
                    restartedSearches++;
                }
            }
            return cancelled;
        }

        public static int RepathAdoptedArrivalsForFacility(
            int facilityId,
            UndergroundParkingRoadConnection connection,
            out int failed)
        {
            failed = 0;
            if (facilityId <= 0
                || !connection.IsValid
                || connection.FacilityId != facilityId)
                return 0;

            int repathed = 0;
            for (int vehicleId = 1; vehicleId < Transactions.Length; vehicleId++)
            {
                Transaction transaction = Transactions[vehicleId];
                if ((transaction.State != TransactionState.Prepared
                     && transaction.State != TransactionState.Adopted)
                    || transaction.FacilityId != facilityId)
                    continue;

                if (!UndergroundParkingEntryRouteManager.TryRepathAdoptedArrival(
                        (ushort)vehicleId,
                        connection))
                {
                    failed++;
                    continue;
                }

                Transactions[vehicleId] = transaction.WithConnectionAndState(
                    connection,
                    transaction.State);
                repathed++;
            }
            return repathed;
        }

        public static void ReleaseVehicle(ushort vehicleId)
        {
            CancelCandidate(vehicleId);
            if (vehicleId != 0)
                Transactions[vehicleId] = default(Transaction);
            ClearTerminalScope(vehicleId);
        }

        public static void Update()
        {
            if (!_active)
                return;

            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null)
                return;

            int checkedCount = 0;
            while (checkedCount++ < CandidateChecksPerUpdate)
            {
                if (_candidateCursor >= Transactions.Length)
                    _candidateCursor = 1;
                ushort vehicleId = (ushort)_candidateCursor++;
                Transaction stored = Transactions[vehicleId];
                if (stored.State != TransactionState.Candidate)
                    continue;

                ref Vehicle vehicleData =
                    ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                if (!IsCandidateValid(vehicleId, ref vehicleData, stored))
                    CancelCandidate(vehicleId);
            }
        }

        public static void Clear()
        {
            for (int i = 1; i < Transactions.Length; i++)
            {
                Transaction stored = Transactions[i];
                if (stored.State == TransactionState.Candidate
                    && stored.FacilityId > 0)
                {
                    UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(
                        (ushort)i,
                        stored.FacilityId,
                        stored.SlotIndex);
                }
                Transactions[i] = default(Transaction);
            }

            _candidateCursor = 1;
            _terminalVehicleId = 0;
            _terminalSegmentId = 0;
            _terminalSegmentOffset = 0;
            _preTripSearchVehicleId = 0;
            _offerLogCount = 0;
            _terminalLogCount = 0;
            _actualLaneLogCount = 0;
            _entranceParkingRejectionLogCount = 0;
            _entranceParkingRelocationLogCount = 0;
            _active = false;
            _parkingLocationType = null;
            _roadSideParkingLocation = null;
            if (!UndergroundParkingOccupancyManager.HasPendingVanillaReleases)
            {
                _terminalRoadsideMethod = null;
                _parkingManagerInstance = null;
            }
        }

        private static void FindParkingSpaceForCitizenPrefix(
            ushort vehicleId,
            out ushort __state)
        {
            __state = _preTripSearchVehicleId;
            _preTripSearchVehicleId = vehicleId;
        }

        private static void FindParkingSpaceForCitizenPostfix(
            bool __result,
            ushort __state)
        {
            ushort vehicleId = _preTripSearchVehicleId;
            if (!__result && vehicleId != 0)
                CancelCandidate(vehicleId);
            _preTripSearchVehicleId = __state;
        }

        private static Exception FindParkingSpaceForCitizenFinalizer(
            Exception __exception,
            ushort __state)
        {
            ushort vehicleId = _preTripSearchVehicleId;
            if (__exception != null && vehicleId != 0)
                CancelCandidate(vehicleId);
            _preTripSearchVehicleId = __state;
            return __exception;
        }

        private static void FindParkingSpaceInVicinityPostfix(
            object[] __args,
            ref bool __result)
        {
            if (!_active
                || __args == null
                || __args.Length != 11
                || _parkingLocationType == null
                || _roadSideParkingLocation == null)
                return;

            Vector3 targetPosition = (Vector3)__args[0];
            VehicleInfo vehicleInfo = __args[2] as VehicleInfo;
            ushort vehicleId = (ushort)__args[4];
            float maxDistance = (float)__args[5];
            bool nativeSucceeded = __result;
            if (vehicleId == 0
                || vehicleId != _preTripSearchVehicleId
                || vehicleInfo == null)
                return;

            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null
                || vehicleId >= vehicleManager.m_vehicles.m_size)
                return;

            ref Vehicle vehicleData =
                ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            if ((vehicleData.m_flags & Vehicle.Flags.Created) == 0
                || vehicleData.Info != vehicleInfo
                || vehicleData.m_citizenUnits == 0u
                || IsDriverReturningHome(vehicleId, targetPosition))
            {
                CancelCandidate(vehicleId);
                return;
            }

            // An accepted UPG arrival already owns one exact reservation and
            // continuation. A later TM:PE pre-trip search for the same active
            // vehicle may still seek conventional parking, but it must not
            // create a second UPG candidate or overwrite the adopted token.
            if (UndergroundParkingOccupancyHarmony
                    .IsAdoptedRoutedArrivalReleaseProtected(vehicleId))
                return;

            int facilityId;
            float facilityDistance;
            if (!UndergroundParkingOccupancyManager.TryFindParkingFacility(
                    targetPosition,
                    -1f,
                    0,
                    out facilityId,
                    out facilityDistance)
                || facilityDistance > maxDistance * maxDistance)
            {
                CancelCandidate(vehicleId);
                return;
            }

            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            UndergroundParkingRoadConnection preTripHandoff;
            Vector3 pavementPosition;
            uint pedestrianLaneId;
            int pedestrianLaneIndex;
            if (!UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                    facilityId,
                    out facility,
                    out connection)
                || !UndergroundParkingOccupancyHarmony.TryResolvePavementHandoff(
                    facility,
                    connection,
                    out pavementPosition,
                    out pedestrianLaneId,
                    out pedestrianLaneIndex)
                || !UndergroundParkingAccessManager
                    .TryGetArrivalConnectionBeforeEntrance(
                        facility,
                        connection.LaneIndex,
                        connection,
                        GetEntranceHandoffDistance(vehicleInfo),
                        connection.LaneDirection,
                        out preTripHandoff))
            {
                CancelCandidate(vehicleId);
                return;
            }

            if (__result)
            {
                Vector3 nativePosition = (Vector3)__args[8];
                Vector3 nativeDelta = nativePosition - targetPosition;
                nativeDelta.y = 0f;
                if (nativeDelta.sqrMagnitude <= facilityDistance)
                {
                    CancelCandidate(vehicleId);
                    return;
                }
            }

            CancelCandidate(vehicleId);
            int slotIndex;
            if (!UndergroundParkingOccupancyManager.TryReserveRoutedArrivalSlot(
                    vehicleId,
                    facilityId,
                    out slotIndex))
                return;

            Vector3 undergroundPosition;
            Quaternion undergroundRotation;
            if (!UndergroundParkingOccupancyManager.TryGetRoutedArrivalReservationPose(
                    vehicleId,
                    facilityId,
                    slotIndex,
                    out undergroundPosition,
                    out undergroundRotation))
            {
                UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(
                    vehicleId,
                    facilityId,
                    slotIndex);
                return;
            }

            Transactions[vehicleId] = Transaction.CreateCandidate(
                facilityId,
                slotIndex,
                connection.SegmentId,
                connection.SegmentOffset,
                vehicleInfo,
                vehicleData.m_citizenUnits,
                CurrentFrame() + CandidateLifetimeFrames);

            __args[6] = _roadSideParkingLocation;
            __args[7] = connection.SegmentId;
            // This is a roadside TM:PE contract. Its route target must be the
            // actual final-approach vehicle lane, never the pavement, building
            // boundary or reserved underground bay. The later terminal lookup
            // refines the same two-metre target against the lane TM:PE
            // actually selected.
            __args[8] = preTripHandoff.LanePosition;
            __args[9] = Quaternion.LookRotation(
                preTripHandoff.LaneDirection,
                Vector3.up);
            __args[10] = EncodeSegmentOffset(preTripHandoff.SegmentOffset);
            __result = true;

            if (_offerLogCount++ < LogLimit)
            {
                UndergroundParkingLog.Advanced(
                    "UPG offered reserved garage to TM:PE pre-trip parking search: vehicle="
                    + vehicleId
                    + " facility="
                    + facilityId
                    + " segment="
                    + connection.SegmentId
                    + " handoffOffset="
                    + preTripHandoff.SegmentOffset
                    + " entranceOffset="
                    + connection.SegmentOffset
                    + " replacedNative="
                    + (nativeSucceeded ? "nearest-upg" : "failed-search"));
            }
        }

        private static void FindParkingSpaceRoadSideForVehiclePosPostfix(
            object[] __args,
            ref bool __result)
        {
            if (!_active || __args == null || __args.Length != 9)
                return;

            if (_terminalVehicleId == 0)
            {
                // TM:PE owns conventional roadside parking. UPG narrows only
                // the curb length physically occupied by a registered garage
                // apron: accepting a parked body there leaves an unowned,
                // permanent obstacle in front of the road-stop handoff. Return
                // a normal failed search so TM:PE can select another space or
                // reroute the still-active vehicle with all occupants intact.
                if (__result)
                {
                    ushort segmentId = (ushort)__args[2];
                    Vector3 parkPosition = (Vector3)__args[4];
                    if (UndergroundParkingOccupancyManager
                        .IsRoadsideParkingAtEntrance(segmentId, parkPosition))
                    {
                        ClearRoadsideParkingResult(__args, ref __result);
                        if (_entranceParkingRejectionLogCount++ < LogLimit)
                        {
                            UndergroundParkingLog.Advanced(
                                "UPG rejected conventional TM:PE roadside space inside garage apron: segment="
                                + segmentId
                                + " position=("
                                + parkPosition.x.ToString("0.0") + ","
                                + parkPosition.y.ToString("0.0") + ","
                                + parkPosition.z.ToString("0.0") + ")");
                        }
                    }
                }
                return;
            }

            if ((ushort)__args[2] != _terminalSegmentId)
            {
                // The scoped UPG transaction must never fall through to an
                // unrelated conventional space after its reservation moved
                // into arrival ownership.
                __result = false;
                return;
            }

            bool nativeSucceeded = __result;

            UndergroundParkingRoadConnection handoffConnection;
            if (!TryGetArrivalConnection(
                    _terminalVehicleId,
                    out handoffConnection)
                || handoffConnection.SegmentId != _terminalSegmentId
                || handoffConnection.SegmentOffset != _terminalSegmentOffset)
            {
                __result = false;
                return;
            }

            // TM:PE uses this position for both its terminal movement target and
            // the early parked identity. Keep both on the exact final-approach
            // lane. UPG holds that identity invisible and moves it to the
            // reserved underground bay only after the entrance animation and
            // validated endpoint commit.
            __args[4] = handoffConnection.LanePosition;
            __args[5] = Quaternion.LookRotation(
                handoffConnection.LaneDirection,
                Vector3.up);
            // TM:PE copies this value into ParkVehicle.segmentOffset. Its
            // enclosing UpdatePathTargetPositions then rewrites the terminal
            // vehicle-lane position from the segment end to this exact portal
            // offset. A negative value leaves the path aimed at an arbitrary
            // segment end instead of the garage entrance.
            __args[6] = EncodeSegmentOffset(_terminalSegmentOffset);
            __args[7] = 0u;
            __args[8] = -1;
            __result = true;

            if (_terminalLogCount++ < LogLimit)
            {
                UndergroundParkingLog.Advanced(
                    "UPG supplied final-approach lane target to TM:PE terminal parking search: vehicle="
                    + _terminalVehicleId
                    + " segment="
                    + _terminalSegmentId
                    + " offset="
                    + _terminalSegmentOffset
                    + " replacedNative="
                    + nativeSucceeded);
            }
        }

        public static bool TryFindRelocationForEntranceBlockingParkedVehicle(
            ushort parkedId,
            VehicleInfo vehicleInfo,
            ushort segmentId,
            Vector3 entranceLanePosition,
            Vector3 laneDirection,
            out Vector3 parkPosition,
            out Quaternion parkRotation)
        {
            parkPosition = Vector3.zero;
            parkRotation = Quaternion.identity;
            if (parkedId == 0
                || vehicleInfo == null
                || segmentId == 0
                || _terminalRoadsideMethod == null
                || _parkingManagerInstance == null)
            {
                return false;
            }

            laneDirection.y = 0f;
            if (laneDirection.sqrMagnitude <= 0.001f)
                return false;
            laneDirection.Normalize();

            // Ask TM:PE itself for a replacement on the same segment, preserving
            // its parking restrictions and collision checks. References begin
            // just beyond the protected apron in each direction and expand only
            // when the nearer native search cannot supply a valid space.
            float[] offsets = { -16f, 16f, -24f, 24f, -36f, 36f, -52f, 52f };
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 reference = entranceLanePosition
                                    + laneDirection * offsets[i];
                object[] args =
                {
                    vehicleInfo,
                    parkedId,
                    segmentId,
                    reference,
                    Vector3.zero,
                    Quaternion.identity,
                    -1f,
                    0u,
                    -1
                };

                try
                {
                    object result = _terminalRoadsideMethod.Invoke(
                        _parkingManagerInstance,
                        args);
                    if (!(result is bool)
                        || !(bool)result)
                    {
                        continue;
                    }

                    Vector3 candidate = (Vector3)args[4];
                    Quaternion candidateRotation = (Quaternion)args[5];
                    if (!UndergroundParkingAccessManager.IsFinite(candidate)
                        || !UndergroundParkingAccessManager.IsFinite(candidateRotation)
                        || UndergroundParkingOccupancyManager
                        .IsRoadsideParkingAtEntrance(segmentId, candidate))
                    {
                        continue;
                    }

                    parkPosition = candidate;
                    parkRotation = candidateRotation;
                    if (_entranceParkingRelocationLogCount++ < LogLimit)
                    {
                        UndergroundParkingLog.Advanced(
                            "UPG obtained native TM:PE relocation for entrance-blocking parked car: parked="
                            + parkedId
                            + " segment="
                            + segmentId
                            + " referenceOffset="
                            + offsets[i].ToString("0.0"));
                    }
                    return true;
                }
                catch (TargetInvocationException exception)
                {
                    Exception cause = exception.InnerException ?? exception;
                    UndergroundParkingLog.Warning(
                        "UPG could not ask TM:PE to relocate entrance-blocking parked car: "
                        + cause.GetType().Name
                        + ": "
                        + cause.Message);
                    return false;
                }
                catch (Exception exception)
                {
                    UndergroundParkingLog.Warning(
                        "UPG could not ask TM:PE to relocate entrance-blocking parked car: "
                        + exception.GetType().Name
                        + ": "
                        + exception.Message);
                    return false;
                }
            }

            return false;
        }

        private static void ClearRoadsideParkingResult(
            object[] args,
            ref bool result)
        {
            args[4] = Vector3.zero;
            args[5] = Quaternion.identity;
            args[6] = -1f;
            args[7] = 0u;
            args[8] = -1;
            result = false;
        }

        private static bool IsCandidateValid(
            ushort vehicleId,
            ref Vehicle vehicleData,
            Transaction candidate)
        {
            if (candidate.State != TransactionState.Candidate
                || candidate.FacilityId <= 0
                || candidate.SlotIndex < 0
                || candidate.SegmentId == 0
                || candidate.Info == null
                || unchecked((int)(candidate.ExpiresAt - CurrentFrame())) <= 0
                || (vehicleData.m_flags & Vehicle.Flags.Created) == 0
                || vehicleData.Info != candidate.Info
                || vehicleData.m_citizenUnits != candidate.CitizenUnits)
                return false;

            Vector3 position;
            Quaternion rotation;
            return UndergroundParkingOccupancyManager.TryGetRoutedArrivalReservationPose(
                vehicleId,
                candidate.FacilityId,
                candidate.SlotIndex,
                out position,
                out rotation);
        }

        private static bool IsDriverReturningHome(
            ushort vehicleId,
            Vector3 destination)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            CitizenManager citizenManager = CitizenManager.instance;
            if (vehicleManager == null || citizenManager == null)
                return true;

            ref Vehicle vehicle =
                ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            uint unitId = vehicle.m_citizenUnits;
            int unitGuard = 0;
            while (unitId != 0u && unitGuard++ < 16)
            {
                if (unitId >= citizenManager.m_units.m_size)
                    return true;
                CitizenUnit unit = citizenManager.m_units.m_buffer[unitId];
                for (int i = 0; i < 5; i++)
                {
                    uint citizenId = unit.GetCitizen(i);
                    if (citizenId == 0u
                        || citizenId >= citizenManager.m_citizens.m_size)
                        continue;
                    ushort instanceId =
                        citizenManager.m_citizens.m_buffer[citizenId].m_instance;
                    if (instanceId == 0
                        || instanceId >= citizenManager.m_instances.m_size)
                        continue;
                    ref CitizenInstance instance =
                        ref citizenManager.m_instances.m_buffer[instanceId];
                    return UndergroundParkingOccupancyManager
                        .IsAuthoritativeDriverReturningHome(
                            citizenId,
                            instance.m_targetBuilding,
                            (instance.m_flags
                             & CitizenInstance.Flags.TargetIsNode) != 0,
                            destination);
                }
                unitId = unit.m_nextUnit;
            }
            return true;
        }

        private static void CancelCandidate(ushort vehicleId)
        {
            if (vehicleId == 0)
                return;
            Transaction stored = Transactions[vehicleId];
            if (stored.State != TransactionState.Candidate)
                return;
            Transactions[vehicleId] = default(Transaction);
            if (stored.FacilityId > 0)
            {
                UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(
                    vehicleId,
                    stored.FacilityId,
                    stored.SlotIndex);
            }
        }

        private static uint CurrentFrame()
        {
            return SimulationManager.instance == null
                ? 0u
                : SimulationManager.instance.m_currentFrameIndex;
        }

        private static float EncodeSegmentOffset(byte offset)
        {
            // TM:PE decodes with a truncating byte cast after multiplying by
            // 255. Bias inside the same byte bucket so floating-point roundoff
            // can never turn an exact portal offset N into N-1.
            int bounded = Mathf.Clamp(offset, 1, 254);
            return (bounded + 0.5f) / 255f;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static MethodInfo FindVicinityTarget(Type managerType)
        {
            MethodInfo[] methods = managerType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "FindParkingSpaceInVicinity"
                    && method.ReturnType == typeof(bool)
                    && parameters.Length == 11
                    && parameters[0].ParameterType == typeof(Vector3)
                    && parameters[2].ParameterType == typeof(VehicleInfo)
                    && parameters[4].ParameterType == typeof(ushort)
                    && parameters[6].ParameterType.IsByRef
                    && parameters[8].ParameterType == typeof(Vector3).MakeByRefType())
                    return method;
            }
            return null;
        }

        private static MethodInfo FindPreTripOwnerTarget(Type managerType)
        {
            MethodInfo[] methods = managerType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "FindParkingSpaceForCitizen"
                    && method.ReturnType == typeof(bool)
                    && parameters.Length == 11
                    && parameters[0].ParameterType == typeof(Vector3)
                    && parameters[1].ParameterType == typeof(VehicleInfo)
                    && parameters[6].ParameterType == typeof(ushort)
                    && parameters[8].ParameterType
                       == typeof(Vector3).MakeByRefType()
                    && parameters[9].ParameterType
                       == typeof(PathUnit.Position).MakeByRefType()
                    && parameters[10].ParameterType
                       == typeof(bool).MakeByRefType())
                    return method;
            }
            return null;
        }

        private static MethodInfo FindTerminalRoadsideTarget(Type managerType)
        {
            MethodInfo[] methods = managerType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "FindParkingSpaceRoadSideForVehiclePos"
                    && method.ReturnType == typeof(bool)
                    && parameters.Length == 9
                    && parameters[0].ParameterType == typeof(VehicleInfo)
                    && parameters[2].ParameterType == typeof(ushort)
                    && parameters[3].ParameterType == typeof(Vector3)
                    && parameters[4].ParameterType == typeof(Vector3).MakeByRefType())
                    return method;
            }
            return null;
        }

        private static MethodInfo FindEnterParkedCarTarget(Type managerType)
        {
            return managerType.GetMethod(
                "EnterParkedCar",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(ushort),
                    typeof(CitizenInstance).MakeByRefType(),
                    typeof(Citizen).MakeByRefType(),
                    typeof(ushort),
                    typeof(ushort).MakeByRefType()
                },
                null);
        }

        private enum TransactionState : byte
        {
            None = 0,
            Candidate = 1,
            Prepared = 2,
            Adopted = 3,
            Stopped = 4
        }

        private struct Transaction
        {
            public readonly TransactionState State;
            public readonly int FacilityId;
            public readonly int SlotIndex;
            public readonly ushort SegmentId;
            public readonly byte SegmentOffset;
            public readonly VehicleInfo Info;
            public readonly uint CitizenUnits;
            public readonly uint ExpiresAt;
            public readonly UndergroundParkingRoadConnection ArrivalConnection;

            private Transaction(
                TransactionState state,
                int facilityId,
                int slotIndex,
                ushort segmentId,
                byte segmentOffset,
                VehicleInfo info,
                uint citizenUnits,
                uint expiresAt,
                UndergroundParkingRoadConnection arrivalConnection)
            {
                State = state;
                FacilityId = facilityId;
                SlotIndex = slotIndex;
                SegmentId = segmentId;
                SegmentOffset = segmentOffset;
                Info = info;
                CitizenUnits = citizenUnits;
                ExpiresAt = expiresAt;
                ArrivalConnection = arrivalConnection;
            }

            public static Transaction CreateCandidate(
                int facilityId,
                int slotIndex,
                ushort segmentId,
                byte segmentOffset,
                VehicleInfo info,
                uint citizenUnits,
                uint expiresAt)
            {
                return new Transaction(
                    TransactionState.Candidate,
                    facilityId,
                    slotIndex,
                    segmentId,
                    segmentOffset,
                    info,
                    citizenUnits,
                    expiresAt,
                    default(UndergroundParkingRoadConnection));
            }

            public Transaction WithPreparedConnection(
                UndergroundParkingRoadConnection connection)
            {
                return new Transaction(
                    TransactionState.Prepared,
                    FacilityId,
                    SlotIndex,
                    SegmentId,
                    connection.SegmentOffset,
                    Info,
                    CitizenUnits,
                    ExpiresAt,
                    connection);
            }

            public Transaction WithState(TransactionState state)
            {
                return new Transaction(
                    state,
                    FacilityId,
                    SlotIndex,
                    SegmentId,
                    SegmentOffset,
                    Info,
                    CitizenUnits,
                    ExpiresAt,
                    ArrivalConnection);
            }

            public Transaction WithConnectionAndState(
                UndergroundParkingRoadConnection connection,
                TransactionState state)
            {
                return new Transaction(
                    state,
                    FacilityId,
                    SlotIndex,
                    SegmentId,
                    SegmentOffset,
                    Info,
                    CitizenUnits,
                    ExpiresAt,
                    connection);
            }
        }
    }

    internal struct TmpeParkingCandidate
    {
        public readonly int FacilityId;
        public readonly int SlotIndex;
        public readonly ushort SegmentId;
        public readonly byte SegmentOffset;

        public TmpeParkingCandidate(
            int facilityId,
            int slotIndex,
            ushort segmentId,
            byte segmentOffset)
        {
            FacilityId = facilityId;
            SlotIndex = slotIndex;
            SegmentId = segmentId;
            SegmentOffset = segmentOffset;
        }
    }
}
