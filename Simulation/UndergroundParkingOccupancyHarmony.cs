using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework.Math;
using HarmonyLib;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingOccupancyHarmony
    {
        private const string HarmonyId = "ScratchyBald.UndergroundParkingGarage.Occupancy";
        private static Harmony _harmony;
        private static bool _patched;
        private static readonly byte[] ManagedDepartureSpawnStates = new byte[65536];
        private static readonly VehicleInfo[] ManagedDepartureExpectedInfos = new VehicleInfo[65536];
        private static readonly ushort[] ManagedDepartureExpectedOwners = new ushort[65536];
        private static readonly Vector3[] ManagedDepartureReleasePositions = new Vector3[65536];
        private static readonly Quaternion[] ManagedDepartureReleaseRotations = new Quaternion[65536];
        private static readonly uint[] ManagedDepartureReleaseLanes = new uint[65536];
        private static readonly int[] ManagedDepartureReleaseFacilities = new int[65536];
        private static readonly int[] ManagedDepartureGarageSlots = new int[65536];
        private static readonly UndergroundParkingFacility[] ManagedDepartureFacilitySnapshots =
            new UndergroundParkingFacility[65536];
        private static readonly UndergroundParkingRoadConnection[] ManagedDepartureConnectionSnapshots =
            new UndergroundParkingRoadConnection[65536];
        private static readonly bool[] ManagedDepartureReleaseWaitLogged = new bool[65536];
        private static readonly bool[] ManagedDepartureAnimationQueued = new bool[65536];
        private static readonly bool[] ManagedDepartureStagingTracked = new bool[65536];
        private static readonly List<ushort> ManagedDepartureStagingVehicles = new List<ushort>();
        private const int ManagedDepartureChecksPerUpdate = 16;
        private static int _managedDepartureStagingCursor;
        private const byte ArrivalStateNone = 0;
        private const byte ArrivalStateWaitingAtRoadStop = 1;
        private const byte ArrivalStateAnimationQueued = 2;
        private const byte ArrivalStateCommittedOffCamera = 3;
        private const byte ArrivalStateAnimationCancelled = 4;
        private const byte ArrivalStateCommitted = 5;
        private const byte ArrivalStateCommitRequested = 6;
        private const byte ArrivalStateRetireRequested = 7;
        private const byte ArrivalStateRetired = 8;
        private const byte ArrivalStateControlledTraversal = 9;
        private const byte ArrivalStateControlledUnspawnRequested = 10;
        private static readonly byte[] RoutedArrivalAnimationStates = new byte[65536];
        private static readonly bool[] RoutedArrivalVisualsSuppressed = new bool[65536];
        private static readonly bool[] RoutedArrivalReleaseAllowed = new bool[65536];
        private static readonly bool[] RoutedArrivalHandoffPoseValid = new bool[65536];
        private static readonly Vector3[] RoutedArrivalHandoffPositions = new Vector3[65536];
        private static readonly Quaternion[] RoutedArrivalHandoffRotations = new Quaternion[65536];
        private static readonly bool[] RoutedArrivalRenderCandidateValid = new bool[65536];
        private static readonly Vector3[] RoutedArrivalRenderCandidatePositions = new Vector3[65536];
        private static readonly Quaternion[] RoutedArrivalRenderCandidateRotations = new Quaternion[65536];
        private static readonly bool[] RoutedArrivalUnspawnActionQueued = new bool[65536];
        private static readonly int[] RoutedArrivalRoadQueueFacilities = new int[65536];
        private static readonly Dictionary<int, Queue<ushort>> RoutedArrivalRoadQueues =
            new Dictionary<int, Queue<ushort>>();
        private static readonly bool[] AuthoritativeParkingReroutes = new bool[65536];
        private static readonly bool[] NativeDeferredWalkingContinuations = new bool[65536];
        private static readonly bool[] AdoptedRoutedArrivalReleaseGuards = new bool[65536];
        private static readonly VehicleInfo[] AdoptedRoutedArrivalInfos = new VehicleInfo[65536];
        private static readonly uint[] AdoptedRoutedArrivalCitizenUnits = new uint[65536];
        // TM:PE creates its parked identity while it is preparing the mixed
        // vehicle/pedestrian path, before the real car has reached the garage.
        // Bind that exact identity to the still-active road transaction so the
        // ordinary underground parked-vehicle observer cannot publish it or
        // consume its reserved slot before the FIFO handoff commits.
        private static readonly ushort[] PendingTmpeParkedVehicleOwners = new ushort[65536];
        private static readonly ushort[] PendingTmpeParkedVehiclesByVehicle = new ushort[65536];
        private static readonly uint[] PendingTmpeOwnerCitizensByVehicle = new uint[65536];
        // The native pedestrian continuation can be a child of the vehicle's
        // current path. Replacing that road path in our prefix would otherwise
        // release the child before vanilla ParkVehicle can reference it.
        private static readonly uint[] NativeContinuationPathHolds = new uint[65536];
        private static readonly bool[] NativeArrivalFinalizeAllowed = new bool[65536];
        private static readonly RoutedArrivalState[] PendingRoutedArrivals = new RoutedArrivalState[65536];
        private const int MaxArrivalOccupants = 32;
        private static readonly ArrivalOccupant[] ArrivalOccupants =
            new ArrivalOccupant[MaxArrivalOccupants];
        private static readonly Dictionary<ushort, ArrivalPedestrianContinuation>
            ArrivalPedestrianContinuations =
                new Dictionary<ushort, ArrivalPedestrianContinuation>();
        private const int MaxDeferredArrivalAssociations = 2048;
        private const int DeferredArrivalChecksPerUpdate = 64;
        private const uint PostArrivalRetrievalCooldownFrames = 4096u;
        private static readonly Dictionary<uint, DeferredArrivalAssociation>
            DeferredArrivalAssociations =
                new Dictionary<uint, DeferredArrivalAssociation>();
        private static readonly List<uint> DeferredArrivalOrder = new List<uint>();
        private static int _deferredArrivalCursor;
        private const int ManagedRetrievalLogLimit = 24;
        private static int _managedRetrievalLogCount;
        private static int _managedSpawnLogCount;
        private static int _tmpeManagedRetrievalLogCount;
        private static int _departureStagingScheduleLogCount;
        private static int _heldReleaseLogCount;
        private static int _departureLaneWaitLogCount;
        private static int _blockedDemolitionLogCount;
        private static int _arrivalPavementHandoffLogCount;
        private static int _arrivalPresentationLogCount;
        private static int _arrivalRoadQueueLogCount;
        private static int _arrivalRoadQueueDispatchLogCount;
        private static int _arrivalRoadQueueWaitLogCount;
        private static int _arrivalPortalAdmissionWaitLogCount;
        private static int _offscreenArrivalLogCount;
        private static int _nativeContinuationAdoptionLogCount;
        private static int _authoritativeParkingContinuationLogCount;
        private static int _terminalEntryLogCount;
        private static int _deferredArrivalLogCount;
        private static readonly ShowToolInfoDelegate ShowToolInfo = CreateShowToolInfoDelegate();
        private static ManagedDepartureContext _managedDepartureContext;
        private static ManagedDepartureState _pendingOwnerDeparture;
        private static readonly TopLevelStartPathFindDelegate TopLevelStartPathFind =
            CreateTopLevelStartPathFindDelegate();
        private static object _tmpeExtCitizenInstanceManager;
        private static PropertyInfo _tmpeExtInstancesProperty;
        private static FieldInfo _tmpePathModeField;
        private static bool _levelActive;

        public static bool IsApplied
        {
            get { return _patched; }
        }

        public static void BeginLevel()
        {
            _levelActive = true;
            RefreshForFacilityCount();
        }

        public static void EndLevel()
        {
            _levelActive = false;
            Release();
        }

        public static void RefreshForFacilityCount()
        {
            if (!_levelActive
                || !UndergroundParkingFeatures.ParkingOccupancyEnabled
                || UndergroundParkingRegistry.Count == 0)
            {
                bool wasApplied = _patched || _harmony != null;
                Release();
                if (_levelActive
                    && UndergroundParkingFeatures.ParkingOccupancyEnabled
                    && UndergroundParkingRegistry.Count == 0)
                {
                    UndergroundParkingLog.Advanced(
                        "UPG parking occupancy Harmony inactive: no registered facilities"
                        + (wasApplied ? "; previous global hooks released." : "."));
                }
                return;
            }

            Apply();
        }

        public static void Apply()
        {
            if (!UndergroundParkingFeatures.ParkingOccupancyEnabled)
            {
                Release();
                UndergroundParkingLog.Advanced("UPG parking occupancy Harmony not applied: disabled by feature flag.");
                return;
            }

            if (!_levelActive || UndergroundParkingRegistry.Count == 0)
            {
                Release();
                UndergroundParkingLog.Advanced(
                    "UPG parking occupancy Harmony not applied: loaded city has no registered facilities.");
                return;
            }

            if (_patched)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);
                int patchedTargets = 0;

                MethodBase updateParkedVehicle = GetPassengerCarUpdateParkedVehicleTarget();
                if (updateParkedVehicle == null)
                    throw new MissingMethodException("Required PassengerCarAI.UpdateParkedVehicle target not found.");
                _harmony.Patch(
                    updateParkedVehicle,
                    prefix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "PassengerCarUpdateParkedVehiclePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodBase passengerCarGetColor = typeof(PassengerCarAI).GetMethod(
                    "GetColor",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(ushort),
                        typeof(Vehicle).MakeByRefType(),
                        typeof(InfoManager.InfoMode),
                        typeof(InfoManager.SubInfoMode)
                    },
                    null);
                if (passengerCarGetColor == null)
                    throw new MissingMethodException("Required PassengerCarAI.GetColor target not found.");
                _harmony.Patch(
                    passengerCarGetColor,
                    postfix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "PassengerCarGetColorPostfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodBase createVehicle = GetVehicleManagerCreateVehicleTarget();
                if (createVehicle == null)
                    throw new MissingMethodException("Required VehicleManager.CreateVehicle departure target not found.");
                _harmony.Patch(
                    createVehicle,
                    prefix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "VehicleManagerCreateVehiclePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "VehicleManagerCreateVehiclePostfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodBase releaseActiveVehicle = typeof(VehicleManager).GetMethod(
                    "ReleaseVehicle",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(ushort) },
                    null);
                if (releaseActiveVehicle == null)
                    throw new MissingMethodException("Required VehicleManager.ReleaseVehicle target not found.");
                _harmony.Patch(
                    releaseActiveVehicle,
                    prefix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "VehicleManagerReleaseVehiclePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                PatchManagedOwnerRetrievalTargets(ref patchedTargets);

                int tmpeParkingTargets =
                    TmpeParkingCompatibilityManager.PatchOptionalTargets(_harmony);
                patchedTargets += tmpeParkingTargets;

                MethodBase parkVehicle = GetPassengerCarParkVehicleTarget();
                if (parkVehicle == null)
                    throw new MissingMethodException("Required PassengerCarAI.ParkVehicle arrival target not found.");
                HarmonyMethod authoritativeParkingPrefix = new HarmonyMethod(
                    typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "PassengerCarParkVehiclePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic));
                    // TM:PE's ParkVehicle prefix always returns false after
                    // delegating to ParkPassengerCar. Harmony then skips later
                    // result-changing prefixes, so UPG must inspect the exact
                    // vanilla parking event first. A declined UPG claim returns
                    // true and leaves TM:PE's complete parking path untouched.
                authoritativeParkingPrefix.priority = Priority.First;
                _harmony.Patch(
                    parkVehicle,
                    prefix: authoritativeParkingPrefix,
                    postfix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "PassengerCarParkVehiclePostfix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                    finalizer: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "PassengerCarParkVehicleFinalizer",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodBase releaseVehicle = typeof(PassengerCarAI).GetMethod(
                    "ReleaseVehicle",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() },
                    null);
                if (releaseVehicle == null)
                    throw new MissingMethodException("Required PassengerCarAI.ReleaseVehicle target not found.");
                _harmony.Patch(
                    releaseVehicle,
                    prefix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "PassengerCarReleaseVehiclePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodBase arriveAtDestination = typeof(PassengerCarAI).GetMethod(
                    "ArriveAtDestination",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() },
                    null);
                if (arriveAtDestination == null)
                    throw new MissingMethodException(
                        "Required PassengerCarAI.ArriveAtDestination target not found.");
                _harmony.Patch(
                    arriveAtDestination,
                    prefix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "PassengerCarArriveAtDestinationPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodBase simulationStep = typeof(PassengerCarAI).GetMethod(
                    "SimulationStep",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3) },
                    null);
                if (simulationStep != null)
                {
                    _harmony.Patch(
                        simulationStep,
                        prefix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                            "PassengerCarSimulationStepPrefix",
                            BindingFlags.Static | BindingFlags.NonPublic)),
                        postfix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                            "PassengerCarSimulationStepPostfix",
                            BindingFlags.Static | BindingFlags.NonPublic)));
                    patchedTargets++;
                }
                else
                {
                    throw new MissingMethodException(
                        "Required PassengerCarAI.SimulationStep(ushort, ref Vehicle, Vector3) target not found.");
                }

                MethodBase bulldozeToolUpdate = typeof(BulldozeTool).GetMethod(
                    "OnToolUpdate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (bulldozeToolUpdate == null)
                    throw new MissingMethodException("Required BulldozeTool.OnToolUpdate target not found.");
                _harmony.Patch(
                    bulldozeToolUpdate,
                    postfix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "BulldozeToolOnToolUpdatePostfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                int expectedPatchedTargets = 14 + tmpeParkingTargets;
                if (patchedTargets != expectedPatchedTargets)
                    throw new InvalidOperationException("Incomplete UPG occupancy patch ledger: expected="
                                                        + expectedPatchedTargets
                                                        + " actual="
                                                        + patchedTargets);
                _patched = true;
                UndergroundParkingLog.Advanced("UPG parking occupancy Harmony active: patchedTargets=" + patchedTargets + ".");
            }
            catch (Exception e)
            {
                _patched = false;
                try
                {
                    if (_harmony != null)
                        _harmony.UnpatchAll(HarmonyId);
                }
                catch (Exception cleanupException)
                {
                    UndergroundParkingLog.Warning(
                        "UPG parking occupancy Harmony failed to roll back partial patches: "
                        + cleanupException.Message);
                }
                _harmony = null;
                // Optional compatibility targets can become active before a
                // later required vanilla target fails. Run the complete state
                // rollback even though the partial Harmony instance has
                // already been removed, so no compatibility transaction,
                // highlight or held vehicle survives a failed installation.
                Release();
                UndergroundParkingLog.Error("UPG parking occupancy Harmony failed: " + e);
            }
        }

        public static bool HasLifecycleActivityForFacility(int facilityId)
        {
            if (facilityId <= 0)
                return false;

            if ((_managedDepartureContext.IsManaged
                 && _managedDepartureContext.Facility.Id == facilityId)
                || (_pendingOwnerDeparture.IsManaged
                    && _pendingOwnerDeparture.Facility.Id == facilityId))
                return true;

            if (TmpeParkingCompatibilityManager.HasFacilityActivity(facilityId))
                return true;

            for (int vehicleId = 1; vehicleId < PendingRoutedArrivals.Length; vehicleId++)
            {
                if ((PendingRoutedArrivals[vehicleId].IsPending
                     && PendingRoutedArrivals[vehicleId].FacilityId == facilityId)
                    || IsManagedDepartureBlockingRelocation((ushort)vehicleId, facilityId))
                    return true;
            }

            return false;
        }

        public static int RefreshManagedDeparturesForRelocatedFacility(
            int facilityId)
        {
            if (facilityId <= 0)
                return 0;

            int refreshed = 0;
            for (int vehicleId = 1;
                 vehicleId < ManagedDepartureSpawnStates.Length;
                 vehicleId++)
            {
                if (ManagedDepartureSpawnStates[vehicleId] == 0
                    || ManagedDepartureReleaseFacilities[vehicleId] != facilityId
                    || !IsManagedDepartureVehicleHeld((ushort)vehicleId))
                    continue;

                UndergroundParkingFacility facility;
                UndergroundParkingRoadConnection connection;
                if (TryRefreshManagedDepartureConnection(
                        (ushort)vehicleId,
                        out facility,
                        out connection))
                    refreshed++;
            }
            return refreshed;
        }

        public static bool IsPendingTmpeParkedIdentity(ushort parkedId)
        {
            if (parkedId == 0)
                return false;
            ushort owner = PendingTmpeParkedVehicleOwners[parkedId];
            return owner != 0
                   && UndergroundParkingEntryRouteManager
                       .IsTmpeAdoptedArrival(owner);
        }

        public static void Release()
        {
            // Clean up whenever a Harmony instance exists, even if Apply failed
            // before it could mark the complete patch set active.
            if (_harmony != null)
            {
                try
                {
                    _harmony.UnpatchAll(HarmonyId);
                }
                catch (Exception e)
                {
                    UndergroundParkingLog.Warning("UPG parking occupancy Harmony failed to release cleanly: " + e.Message);
                }
            }

            _harmony = null;
            _patched = false;
            RestoreControlledTraversalsOnShutdown();
            UndergroundParkingEntryRouteManager.Clear();
            ReleasePendingDeparturesOnShutdown();
            Array.Clear(ManagedDepartureSpawnStates, 0, ManagedDepartureSpawnStates.Length);
            Array.Clear(ManagedDepartureExpectedInfos, 0, ManagedDepartureExpectedInfos.Length);
            Array.Clear(ManagedDepartureExpectedOwners, 0, ManagedDepartureExpectedOwners.Length);
            Array.Clear(ManagedDepartureReleaseWaitLogged, 0, ManagedDepartureReleaseWaitLogged.Length);
            Array.Clear(ManagedDepartureAnimationQueued, 0, ManagedDepartureAnimationQueued.Length);
            Array.Clear(ManagedDepartureFacilitySnapshots, 0, ManagedDepartureFacilitySnapshots.Length);
            Array.Clear(ManagedDepartureConnectionSnapshots, 0, ManagedDepartureConnectionSnapshots.Length);
            Array.Clear(ManagedDepartureGarageSlots, 0, ManagedDepartureGarageSlots.Length);
            Array.Clear(ManagedDepartureStagingTracked, 0, ManagedDepartureStagingTracked.Length);
            ManagedDepartureStagingVehicles.Clear();
            _managedDepartureStagingCursor = 0;
            Array.Clear(RoutedArrivalAnimationStates, 0, RoutedArrivalAnimationStates.Length);
            Array.Clear(RoutedArrivalVisualsSuppressed, 0, RoutedArrivalVisualsSuppressed.Length);
            Array.Clear(RoutedArrivalReleaseAllowed, 0, RoutedArrivalReleaseAllowed.Length);
            Array.Clear(RoutedArrivalHandoffPoseValid, 0, RoutedArrivalHandoffPoseValid.Length);
            Array.Clear(RoutedArrivalHandoffPositions, 0, RoutedArrivalHandoffPositions.Length);
            Array.Clear(RoutedArrivalHandoffRotations, 0, RoutedArrivalHandoffRotations.Length);
            Array.Clear(RoutedArrivalRenderCandidateValid, 0, RoutedArrivalRenderCandidateValid.Length);
            Array.Clear(RoutedArrivalRenderCandidatePositions, 0, RoutedArrivalRenderCandidatePositions.Length);
            Array.Clear(RoutedArrivalRenderCandidateRotations, 0, RoutedArrivalRenderCandidateRotations.Length);
            Array.Clear(RoutedArrivalUnspawnActionQueued, 0, RoutedArrivalUnspawnActionQueued.Length);
            Array.Clear(
                RoutedArrivalRoadQueueFacilities,
                0,
                RoutedArrivalRoadQueueFacilities.Length);
            RoutedArrivalRoadQueues.Clear();
            Array.Clear(AuthoritativeParkingReroutes, 0, AuthoritativeParkingReroutes.Length);
            Array.Clear(
                PendingTmpeParkedVehicleOwners,
                0,
                PendingTmpeParkedVehicleOwners.Length);
            Array.Clear(
                PendingTmpeParkedVehiclesByVehicle,
                0,
                PendingTmpeParkedVehiclesByVehicle.Length);
            Array.Clear(
                PendingTmpeOwnerCitizensByVehicle,
                0,
                PendingTmpeOwnerCitizensByVehicle.Length);
            Array.Clear(
                NativeDeferredWalkingContinuations,
                0,
                NativeDeferredWalkingContinuations.Length);
            Array.Clear(
                AdoptedRoutedArrivalReleaseGuards,
                0,
                AdoptedRoutedArrivalReleaseGuards.Length);
            Array.Clear(
                AdoptedRoutedArrivalInfos,
                0,
                AdoptedRoutedArrivalInfos.Length);
            Array.Clear(
                AdoptedRoutedArrivalCitizenUnits,
                0,
                AdoptedRoutedArrivalCitizenUnits.Length);
            ReleaseAllNativeContinuationPathHolds();
            Array.Clear(NativeArrivalFinalizeAllowed, 0, NativeArrivalFinalizeAllowed.Length);
            Array.Clear(PendingRoutedArrivals, 0, PendingRoutedArrivals.Length);
            Array.Clear(ArrivalOccupants, 0, ArrivalOccupants.Length);
            RestoreAllDeferredArrivalAssociations();
            ArrivalPedestrianContinuations.Clear();
            _managedRetrievalLogCount = 0;
            _managedSpawnLogCount = 0;
            _tmpeManagedRetrievalLogCount = 0;
            _departureStagingScheduleLogCount = 0;
            _heldReleaseLogCount = 0;
            _departureLaneWaitLogCount = 0;
            _blockedDemolitionLogCount = 0;
            _arrivalPavementHandoffLogCount = 0;
            _arrivalPresentationLogCount = 0;
            _arrivalRoadQueueLogCount = 0;
            _arrivalRoadQueueDispatchLogCount = 0;
            _arrivalRoadQueueWaitLogCount = 0;
            _arrivalPortalAdmissionWaitLogCount = 0;
            _offscreenArrivalLogCount = 0;
            _terminalEntryLogCount = 0;
            _deferredArrivalLogCount = 0;
            _authoritativeParkingContinuationLogCount = 0;
            UndergroundParkingLifecycleDiagnostics.Reset();
            _managedDepartureContext = default(ManagedDepartureContext);
            _pendingOwnerDeparture = default(ManagedDepartureState);
            _tmpeExtCitizenInstanceManager = null;
            _tmpeExtInstancesProperty = null;
            _tmpePathModeField = null;
            TmpeParkingCompatibilityManager.Clear();
        }

        public static int CompletePortalArrivalsForCityReset()
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager == null)
                return 0;

            int completed = 0;
            for (int i = 1; i < RoutedArrivalAnimationStates.Length; i++)
            {
                byte state = RoutedArrivalAnimationStates[i];
                if (state != ArrivalStateWaitingAtRoadStop
                    && state != ArrivalStateAnimationQueued
                    && state != ArrivalStateCommitRequested
                    && state != ArrivalStateControlledTraversal
                    && state != ArrivalStateControlledUnspawnRequested)
                {
                    continue;
                }

                ushort vehicleId = (ushort)i;
                RoutedArrivalState arrival = PendingRoutedArrivals[vehicleId];
                if (!arrival.IsPending
                    || vehicleId >= vehicleManager.m_vehicles.m_size)
                {
                    continue;
                }

                ref Vehicle vehicleData =
                    ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                PassengerCarAI ai = vehicleData.Info == null
                    ? null
                    : vehicleData.Info.m_vehicleAI as PassengerCarAI;
                if (ai == null
                    || (vehicleData.m_flags & Vehicle.Flags.Created) == 0
                    || vehicleData.Info != arrival.Info)
                {
                    continue;
                }

                // Portal admission has already proved the exact facility,
                // lane, direction and pavement transaction. NUKE must finish
                // that existing transaction before removing its facility;
                // shutdown rollback would otherwise respawn an unspawned
                // traversal car at the road stop and then clear its route.
                RoutedArrivalAnimationStates[vehicleId] =
                    ArrivalStateCommitRequested;
                ExecuteRoutedArrival(ai, vehicleId, ref vehicleData, arrival);
                byte completedState = RoutedArrivalAnimationStates[vehicleId];
                if (completedState == ArrivalStateCommittedOffCamera
                    || completedState == ArrivalStateRetireRequested
                    || completedState == ArrivalStateRetired)
                {
                    completed++;
                }
            }

            if (completed > 0)
            {
                UndergroundParkingLog.Advanced(
                    "Completed validated portal arrivals before NUKE facility removal: count="
                    + completed);
            }
            return completed;
        }

        public static void UpdateDeferredArrivalAssociations()
        {
            UndergroundParkingLifecycleDiagnostics.Update();
            UpdateManagedDepartureStaging();
            TmpeParkingCompatibilityManager.Update();
            if (DeferredArrivalAssociations.Count == 0)
                return;

            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null)
                return;

            int checkedCount = 0;
            while (checkedCount < DeferredArrivalChecksPerUpdate
                   && _deferredArrivalCursor < DeferredArrivalOrder.Count)
            {
                uint citizenId = DeferredArrivalOrder[_deferredArrivalCursor++];
                checkedCount++;
                DeferredArrivalAssociation association;
                if (!DeferredArrivalAssociations.TryGetValue(citizenId, out association))
                    continue;

                if (ShouldRestoreDeferredArrivalAssociation(
                        citizenManager,
                        citizenId,
                        association))
                {
                    RestoreDeferredArrivalAssociation(
                        citizenManager,
                        citizenId,
                        association,
                        "pedestrian-idle-after-cooldown");
                    DeferredArrivalAssociations.Remove(citizenId);
                }
            }

            if (_deferredArrivalCursor < DeferredArrivalOrder.Count)
                return;

            DeferredArrivalOrder.Clear();
            foreach (KeyValuePair<uint, DeferredArrivalAssociation> pair
                     in DeferredArrivalAssociations)
            {
                DeferredArrivalOrder.Add(pair.Key);
            }
            _deferredArrivalCursor = 0;
        }

        private static void PatchManagedOwnerRetrievalTargets(ref int patchedTargets)
        {
            MethodBase ownerPath = typeof(CitizenAI).GetMethod(
                "StartPathFind",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(ushort),
                    typeof(CitizenInstance).MakeByRefType(),
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(VehicleInfo),
                    typeof(bool),
                    typeof(bool)
                },
                null);
            if (ownerPath == null)
                throw new MissingMethodException("Required CitizenAI.StartPathFind retrieval target not found.");

            _harmony.Patch(
                ownerPath,
                prefix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                    "CitizenStartPathFindPrefix",
                    BindingFlags.Static | BindingFlags.NonPublic)),
                finalizer: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                    "ManagedRetrievalFinalizer",
                    BindingFlags.Static | BindingFlags.NonPublic)));
            patchedTargets++;

            Type[] spawnSignature =
            {
                typeof(ushort),
                typeof(CitizenInstance).MakeByRefType(),
                typeof(PathUnit.Position)
            };
            Type[] ownerTypes = { typeof(ResidentAI), typeof(TouristAI) };
            for (int i = 0; i < ownerTypes.Length; i++)
            {
                MethodBase startPathFind = ownerTypes[i].GetMethod(
                    "StartPathFind",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(ushort),
                        typeof(CitizenInstance).MakeByRefType()
                    },
                    null);
                if (startPathFind == null)
                    throw new MissingMethodException("Required " + ownerTypes[i].Name + ".StartPathFind retrieval target not found.");

                // The concrete owner AIs are the outer retrieval boundary. TM:PE
                // can complete a parked-to-active transition inside this call
                // without reaching CitizenAI's detailed pathfinding overload.
                // Keep the exact managed identity and departure context alive
                // across the complete call so any nested CreateVehicle remains
                // observable by the existing native-spawn transaction.
                HarmonyMethod ownerStartPrefix = new HarmonyMethod(
                    typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "OwnerSpawnVehiclePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic));
                ownerStartPrefix.priority = Priority.First;
                HarmonyMethod ownerStartPostfix = new HarmonyMethod(
                    typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "OwnerSpawnVehiclePostfix",
                        BindingFlags.Static | BindingFlags.NonPublic));
                ownerStartPostfix.priority = Priority.Last;
                _harmony.Patch(
                    startPathFind,
                    prefix: ownerStartPrefix,
                    postfix: ownerStartPostfix,
                    finalizer: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "OwnerSpawnVehicleFinalizer",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;

                MethodBase spawnVehicle = ownerTypes[i].GetMethod(
                    "SpawnVehicle",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    spawnSignature,
                    null);
                if (spawnVehicle == null)
                    throw new MissingMethodException("Required " + ownerTypes[i].Name + ".SpawnVehicle target not found.");

                _harmony.Patch(
                    spawnVehicle,
                    prefix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "OwnerSpawnVehiclePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "OwnerSpawnVehiclePostfix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                    finalizer: new HarmonyMethod(typeof(UndergroundParkingOccupancyHarmony).GetMethod(
                        "OwnerSpawnVehicleFinalizer",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patchedTargets++;
            }
        }

        private static MethodBase GetPassengerCarUpdateParkedVehicleTarget()
        {
            Type[] signature =
            {
                typeof(ushort),
                typeof(VehicleParked).MakeByRefType()
            };

            return typeof(PassengerCarAI).GetMethod(
                "UpdateParkedVehicle",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                signature,
                null);
        }

        private static MethodBase GetVehicleManagerCreateVehicleTarget()
        {
            return typeof(VehicleManager).GetMethod(
                "CreateVehicle",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(ushort).MakeByRefType(),
                    typeof(Randomizer).MakeByRefType(),
                    typeof(VehicleInfo),
                    typeof(Vector3),
                    typeof(TransferManager.TransferReason),
                    typeof(bool),
                    typeof(bool)
                },
                null);
        }

        private static MethodBase GetPassengerCarParkVehicleTarget()
        {
            return typeof(PassengerCarAI).GetMethod(
                "ParkVehicle",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(ushort),
                    typeof(Vehicle).MakeByRefType(),
                    typeof(PathUnit.Position),
                    typeof(uint),
                    typeof(int),
                    typeof(byte).MakeByRefType()
                },
                null);
        }

        private static bool PassengerCarArriveAtDestinationPrefix(
            ushort vehicleID,
            ref Vehicle vehicleData,
            ref bool __result)
        {
            if (vehicleID == 0 || NativeArrivalFinalizeAllowed[vehicleID])
                return true;

            int pendingFacilityId;
            if (UndergroundParkingEntryRouteManager.TryProbeRoadPortalArrival(
                    vehicleID,
                    ref vehicleData,
                    vehicleData.GetLastFramePosition(),
                    out pendingFacilityId)
                && UndergroundParkingPortalAnimationManager
                    .HasActivityForFacility(pendingFacilityId))
            {
                // Admission belongs before the terminal transaction. Suppress
                // only this premature native parking callback while the prior
                // portal owner finishes; do not mark the route stopped, build
                // a UPG commit, or freeze the car in the custom road FIFO.
                // Vanilla continues producing coherent road frames and retries
                // this terminal callback once the entrance is clear.
                __result = false;
                if (_arrivalPortalAdmissionWaitLogCount++ < 64)
                {
                    UndergroundParkingLog.Advanced(
                        "UPG arrival commit deferred before terminal stop: vehicle="
                        + vehicleID
                        + " facility="
                        + pendingFacilityId
                        + " reason=portal-occupied ownership=vanilla-road");
                }
                return false;
            }

            int facilityId;
            if (!UndergroundParkingEntryRouteManager.TryHoldRoadPortalArrival(
                    vehicleID,
                    ref vehicleData,
                    vehicleData.GetLastFramePosition(),
                    out facilityId))
                return true;

            __result = false;
            if (_terminalEntryLogCount++ < 64)
            {
                UndergroundParkingLog.Advanced(
                    "UPG real arrival terminal reached: vehicle="
                    + vehicleID
                    + " facility="
                    + facilityId
                    + " action=hold-at-exact-road-portal-before-native-pedestrian-exit");
            }
            return false;
        }

        private static bool PassengerCarParkVehiclePrefix(
            PassengerCarAI __instance,
            ushort vehicleID,
            ref Vehicle vehicleData,
            PathUnit.Position pathPos,
            uint nextPath,
            int nextPositionIndex,
            ref byte segmentOffset,
            ref bool __result,
            out bool __state)
        {
            // Harmony carries this value with this exact invocation. The
            // vehicle-wide reroute flag deliberately survives until the outer
            // SimulationStep postfix, so it cannot safely identify which of
            // several ParkVehicle calls in that step actually started UPG's
            // route.
            __state = false;
            UndergroundParkingEntryRouteManager.TraceAuthoritativeParkingAttempt(
                vehicleID,
                ref vehicleData,
                pathPos,
                nextPath,
                nextPositionIndex,
                segmentOffset);

            if (__instance == null || vehicleID == 0)
            {
                UndergroundParkingEntryRouteManager.TraceAuthoritativeParkingRejection(
                    vehicleID,
                    __instance == null
                        ? "ai-missing"
                        : "vehicle-id-zero",
                    "nextPath=" + nextPath
                    + " nextPositionIndex=" + nextPositionIndex);
                return true;
            }

            if (UndergroundParkingEntryRouteManager.HasActiveRoute(vehicleID))
            {
                byte preparedOffset;
                if (TmpeParkingCompatibilityManager.TryGetPreparedArrivalOffset(
                        vehicleID,
                        out preparedOffset))
                {
                    // TM:PE's mixed car/pedestrian path retains the same
                    // transition until the road car reaches its rewritten
                    // portal offset. Its first parking transaction already
                    // created the parked identity and deferred walking state;
                    // rerunning that mutation would create duplicate parked
                    // records. Return the established result and offset only.
                    segmentOffset = preparedOffset;
                    __result = true;
                    AuthoritativeParkingReroutes[vehicleID] = true;
                    return false;
                }

                UndergroundParkingEntryRouteManager.TraceAuthoritativeParkingRejection(
                    vehicleID,
                    "route-already-active",
                    "nextPath=" + nextPath
                    + " nextPositionIndex=" + nextPositionIndex);
                return true;
            }

            TmpeParkingCandidate tmpeCandidate;
            bool hasTmpeCandidate =
                TmpeParkingCompatibilityManager.TryGetPreselectedCandidate(
                    vehicleID,
                    ref vehicleData,
                    pathPos,
                    out tmpeCandidate);
            if (hasTmpeCandidate
                && !IsTmpeParkingAiDrivingState(vehicleID, true))
            {
                TmpeParkingCompatibilityManager.ReleaseVehicle(vehicleID);
                hasTmpeCandidate = false;
            }
            if (!hasTmpeCandidate
                && TmpeParkingCompatibilityManager.IsActive
                && IsTmpeParkingAiDrivingState(vehicleID, false))
            {
                // TM:PE owns this journey, but it did not select a UPG in its
                // pre-trip search. Do not introduce the superseded late-route
                // replacement at the terminal parking callback.
                UndergroundParkingEntryRouteManager.TraceAuthoritativeParkingRejection(
                    vehicleID,
                    "tmpe-native-parking-without-upg-candidate",
                    "segment=" + pathPos.m_segment);
                return true;
            }

            // TM:PE invokes ParkVehicle from UpdatePathTargetPositions while
            // constructing the future vehicle-to-pedestrian transition. The
            // exact live vehicle identity, reserved bay and entrance segment
            // are the source proof at this stage; physical portal proof belongs
            // exclusively to the later ArriveAtDestination transaction. TM:PE
            // supplies its current vehicle path rather than vanilla's distinct
            // pedestrian continuation, so the vanilla continuation gate does
            // not apply to this preselected branch.
            if (hasTmpeCandidate)
            {
                UndergroundParkingRoadConnection arrivalConnection;
                if (!UndergroundParkingEntryRouteManager
                        .TryPrepareTmpePreselectedArrival(
                            vehicleID,
                            ref vehicleData,
                            tmpeCandidate,
                            pathPos,
                            nextPath,
                            nextPositionIndex,
                            segmentOffset,
                            out arrivalConnection))
                {
                    TmpeParkingCompatibilityManager.ReleaseVehicle(vehicleID);
                    return true;
                }

                if (!TmpeParkingCompatibilityManager.BeginTerminalParkingScope(
                        vehicleID,
                        tmpeCandidate,
                        arrivalConnection))
                {
                    UndergroundParkingEntryRouteManager.FailArrival(
                        vehicleID,
                        "tmpe-transaction-transition-failed");
                    TmpeParkingCompatibilityManager.ReleaseVehicle(vehicleID);
                    return true;
                }
                AuthoritativeParkingReroutes[vehicleID] = true;
                __state = true;
                return true;
            }

            string sourceReason;
            if (!HasNativeParkingSourceProof(
                    vehicleID,
                    ref vehicleData,
                    pathPos,
                    nextPath,
                    nextPositionIndex,
                    segmentOffset,
                    out sourceReason))
            {
                UndergroundParkingEntryRouteManager.TraceAuthoritativeParkingRejection(
                    vehicleID,
                    "source-gate-ignored",
                    sourceReason);
                return true;
            }

            if (!TryHoldNativeContinuationPath(vehicleID, nextPath))
            {
                UndergroundParkingEntryRouteManager.TraceAuthoritativeParkingRejection(
                    vehicleID,
                    "native-continuation-hold-unavailable",
                    "nextPath=" + nextPath);
                return true;
            }

            bool pathStarted;
            if (!UndergroundParkingEntryRouteManager.TryStartEntryRoute(
                    __instance,
                    vehicleID,
                    ref vehicleData,
                    nextPath,
                    nextPositionIndex,
                    segmentOffset,
                    out pathStarted)
                || !pathStarted)
            {
                ReleaseNativeContinuationPathHold(vehicleID);
                return true;
            }

            // Let the one original vanilla parking transaction run in its
            // natural caller context. It owns the parked identity and transfers
            // the exact ready nextPath/cursor/offset to every occupant. The
            // enclosing SimulationStep postfix clears only the transient Parking
            // flag so the newly-created portal road path can continue travelling.
            AuthoritativeParkingReroutes[vehicleID] = true;
            __state = true;
            return true;
        }

        private static void PassengerCarParkVehiclePostfix(
            ushort vehicleID,
            ref Vehicle vehicleData,
            uint nextPath,
            int nextPositionIndex,
            byte segmentOffset,
            bool __result,
            bool __state)
        {
            // Inspect and adopt native output only for the exact call whose
            // prefix started this route. An existing-save vehicle may enter
            // ParkVehicle again before SimulationStep returns; that later call
            // must not consume or cancel the first call's transaction.
            if (vehicleID == 0 || !__state)
                return;

            TmpeParkingCompatibilityManager.ClearTerminalScope(vehicleID);

            uint transferredPath = 0u;
            int transferredPositionIndex = -1;
            byte transferredOffset = 0;
            int transferredOccupants = 0;
            ushort transferredParkedId = 0;
            string transferReason = __result
                ? "transfer-not-inspected"
                : "native-parking-transaction-rejected";
            bool transferCaptured = __result
                && TryCaptureNativeContinuationTransfer(
                    vehicleID,
                    nextPath,
                    out transferredPath,
                    out transferredPositionIndex,
                    out transferredOffset,
                    out transferredOccupants,
                    out transferredParkedId,
                    out transferReason);
            bool deferredWalkingCaptured = false;
            if (__result && !transferCaptured)
            {
                deferredWalkingCaptured =
                    TryCaptureTmpeDeferredWalkingTransfer(
                        vehicleID,
                        out transferredOccupants,
                        out transferredParkedId,
                        out transferReason);
            }
            // Vanilla has now either attached its own reference to every
            // occupant, or TM:PE Parking AI has published its explicit deferred
            // walking state after creating the parked identity. Our single
            // lifetime hold has completed its only job and must not survive the
            // call.
            ReleaseNativeContinuationPathHold(vehicleID);
            bool continuationAdopted = transferCaptured
                ? UndergroundParkingEntryRouteManager.AdoptNativePedestrianContinuation(
                    vehicleID,
                    transferredPath,
                    transferredPositionIndex,
                    transferredOffset)
                : deferredWalkingCaptured
                  && UndergroundParkingEntryRouteManager
                      .AdoptNativeDeferredPedestrianContinuation(vehicleID);
            if ((!transferCaptured && !deferredWalkingCaptured)
                || !continuationAdopted)
            {
                if (__result)
                {
                    // TM:PE may already have created its parked identity and
                    // detached its deferred walking bookkeeping. That mutation
                    // cannot safely be rolled back while the real car is still
                    // in the carriageway. Adopt and hold the one transaction
                    // for diagnosis instead of releasing occupants or falling
                    // into a second parking process.
                    NativeDeferredWalkingContinuations[vehicleID] =
                        deferredWalkingCaptured;
                    ProtectAdoptedRoutedArrival(vehicleID, ref vehicleData);
                    TmpeParkingCompatibilityManager
                        .MarkPreparedArrivalAdopted(vehicleID);
                    if (transferredParkedId != 0)
                    {
                        BindPendingTmpeParkedIdentity(
                            vehicleID,
                            transferredParkedId,
                            FindPendingTmpeOwnerCitizen(
                                vehicleID,
                                ref vehicleData,
                                transferredParkedId));
                    }
                    UndergroundParkingEntryRouteManager.FailArrival(
                        vehicleID,
                        "native-pedestrian-continuation-not-transferred");
                }
                else
                {
                    AuthoritativeParkingReroutes[vehicleID] = false;
                    NativeDeferredWalkingContinuations[vehicleID] = false;
                    UndergroundParkingEntryRouteManager.FailArrival(
                        vehicleID,
                        "native-parking-transaction-rejected");
                    TmpeParkingCompatibilityManager.ReleaseVehicle(vehicleID);
                }
                UndergroundParkingEntryRouteManager.TraceAuthoritativeParkingRejection(
                    vehicleID,
                    "native-transaction-not-preserved",
                    "result=" + __result
                    + " nextPath=" + nextPath
                    + " nextPositionIndex=" + nextPositionIndex
                    + " segmentOffset=" + segmentOffset
                    + " transferReason=" + transferReason);
                return;
            }

            NativeDeferredWalkingContinuations[vehicleID] =
                deferredWalkingCaptured;
            ProtectAdoptedRoutedArrival(vehicleID, ref vehicleData);
            TmpeParkingCompatibilityManager.MarkPreparedArrivalAdopted(
                vehicleID);
            if (deferredWalkingCaptured && transferredParkedId != 0)
            {
                BindPendingTmpeParkedIdentity(
                    vehicleID,
                    transferredParkedId,
                    FindPendingTmpeOwnerCitizen(
                        vehicleID,
                        ref vehicleData,
                        transferredParkedId));
            }
            if (_nativeContinuationAdoptionLogCount++ < 64)
            {
                UndergroundParkingLog.Advanced(
                    "UPG native parking continuation adopted: vehicle="
                    + vehicleID
                    + " occupants="
                    + transferredOccupants
                    + " parked="
                    + transferredParkedId
                    + " path="
                    + transferredPath
                    + " cursor="
                    + transferredPositionIndex
                    + " offset="
                    + transferredOffset
                    + " source="
                    + (deferredWalkingCaptured
                        ? "tmpe-post-ParkVehicle-deferred-walk"
                        : "vanilla-post-ParkVehicle")
                    + " continuationLifetime=temporary-reference-hold"
                    + " upgPathWrite=False");
            }
        }

        private static Exception PassengerCarParkVehicleFinalizer(
            Exception __exception,
            ushort vehicleID,
            bool __state)
        {
            if (__state)
            {
                TmpeParkingCompatibilityManager.ClearTerminalScope(vehicleID);
                if (__exception != null)
                {
                    AuthoritativeParkingReroutes[vehicleID] = false;
                    NativeDeferredWalkingContinuations[vehicleID] = false;
                    ReleaseNativeContinuationPathHold(vehicleID);
                    UndergroundParkingEntryRouteManager.FailArrival(
                        vehicleID,
                        "parking-transaction-exception");
                    TmpeParkingCompatibilityManager.ReleaseVehicle(vehicleID);
                }
            }
            return __exception;
        }

        private static bool TryHoldNativeContinuationPath(
            ushort vehicleId,
            uint path)
        {
            if (vehicleId == 0 || path == 0u)
                return false;

            PathManager pathManager = PathManager.instance;
            if (pathManager == null || path >= pathManager.m_pathUnits.m_size)
                return false;

            ReleaseNativeContinuationPathHold(vehicleId);
            if (!pathManager.AddPathReference(path))
                return false;

            NativeContinuationPathHolds[vehicleId] = path;
            return true;
        }

        private static void ReleaseNativeContinuationPathHold(ushort vehicleId)
        {
            if (vehicleId == 0)
                return;

            uint path = NativeContinuationPathHolds[vehicleId];
            NativeContinuationPathHolds[vehicleId] = 0u;
            PathManager pathManager = PathManager.instance;
            if (path != 0u
                && pathManager != null
                && path < pathManager.m_pathUnits.m_size)
            {
                pathManager.ReleasePath(path);
            }
        }

        private static void ReleaseAllNativeContinuationPathHolds()
        {
            for (int vehicleId = 1;
                 vehicleId < NativeContinuationPathHolds.Length;
                 vehicleId++)
            {
                if (NativeContinuationPathHolds[vehicleId] != 0u)
                    ReleaseNativeContinuationPathHold((ushort)vehicleId);
            }
        }

        private static bool HasNativeParkingSourceProof(
            ushort vehicleId,
            ref Vehicle vehicleData,
            PathUnit.Position parkingPosition,
            uint nextPath,
            int nextPositionIndex,
            byte segmentOffset,
            out string reason)
        {
            reason = "source-proof-unavailable";
            if (vehicleId == 0
                || vehicleData.m_citizenUnits == 0u
                || !IsValidNativeParkingPosition(parkingPosition))
            {
                reason = "parking-position-invalid";
                return false;
            }

            PathManager pathManager = PathManager.instance;
            if (pathManager == null
                || nextPath == 0u
                || nextPath == vehicleData.m_path
                || nextPath >= pathManager.m_pathUnits.m_size)
            {
                reason = "continuation-path-missing";
                return false;
            }

            PathUnit continuation = pathManager.m_pathUnits.m_buffer[nextPath];
            if ((continuation.m_pathFindFlags & PathUnit.FLAG_READY) == 0
                || (continuation.m_pathFindFlags & PathUnit.FLAG_FAILED) != 0)
            {
                reason = "continuation-path-not-ready";
                return false;
            }

            int positionCount = Mathf.Clamp(
                continuation.m_positionCount,
                0,
                PathUnit.MAX_POSITIONS);
            if (nextPositionIndex < 0 || positionCount == 0)
            {
                reason = "continuation-cursor-invalid";
                return false;
            }

            // This cursor is opaque vanilla traversal state, not an index into
            // the first PathUnit. The distinct ready continuation is the
            // positive source proof; UPG does not decode or rewrite it.
            reason = "native-parking-plus-ready-continuation"
                     + " cursor=" + nextPositionIndex
                     + " positions=" + positionCount
                     + " offset=" + segmentOffset;
            return true;
        }

        private static bool TryCaptureNativeContinuationTransfer(
            ushort vehicleId,
            uint expectedPath,
            out uint transferredPath,
            out int transferredPositionIndex,
            out byte transferredOffset,
            out int occupantCount,
            out ushort parkedId,
            out string reason)
        {
            transferredPath = 0u;
            transferredPositionIndex = -1;
            transferredOffset = 0;
            occupantCount = 0;
            parkedId = 0;
            reason = "transfer-unavailable";
            CitizenManager citizenManager = CitizenManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            PathManager pathManager = PathManager.instance;
            if (citizenManager == null
                || vehicleManager == null
                || pathManager == null
                || vehicleId == 0
                || vehicleId >= vehicleManager.m_vehicles.m_size
                || expectedPath == 0u)
            {
                reason = "manager-or-input-invalid";
                return false;
            }

            ref Vehicle vehicleData =
                ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            uint unitId = vehicleData.m_citizenUnits;
            int unitGuard = 0;
            while (unitId != 0u && unitGuard++ < 16)
            {
                if (unitId >= citizenManager.m_units.m_size)
                {
                    reason = "citizen-unit-out-of-range";
                    return false;
                }

                CitizenUnit unit = citizenManager.m_units.m_buffer[unitId];
                for (int i = 0; i < 5; i++)
                {
                    uint citizenId = unit.GetCitizen(i);
                    if (citizenId == 0u)
                        continue;
                    if (citizenId >= citizenManager.m_citizens.m_size)
                    {
                        reason = "citizen-out-of-range";
                        return false;
                    }

                    ref Citizen citizen =
                        ref citizenManager.m_citizens.m_buffer[citizenId];
                    ushort instanceId = citizen.m_instance;
                    if (instanceId == 0
                        || instanceId >= citizenManager.m_instances.m_size)
                    {
                        reason = "occupant-instance-unavailable";
                        return false;
                    }

                    ref CitizenInstance instance =
                        ref citizenManager.m_instances.m_buffer[instanceId];
                    if ((instance.m_flags & CitizenInstance.Flags.Created) == 0
                        || instance.m_citizen != citizenId
                        || citizen.m_vehicle != vehicleId
                        || instance.Info == null
                        || !(instance.Info.m_citizenAI is HumanAI))
                    {
                        reason = "occupant-identity-or-vehicle-not-preserved";
                        return false;
                    }

                    uint occupantPath = instance.m_path;
                    int occupantPositionIndex = instance.m_pathPositionIndex;
                    byte occupantOffset = instance.m_lastPathOffset;
                    if (occupantPath == 0u
                        || occupantPath != expectedPath
                        || occupantPath >= pathManager.m_pathUnits.m_size)
                    {
                        reason = "native-continuation-path-mismatch"
                                 + " expected=" + expectedPath
                                 + " actual=" + occupantPath;
                        return false;
                    }

                    PathUnit path = pathManager.m_pathUnits.m_buffer[occupantPath];
                    if ((path.m_pathFindFlags & PathUnit.FLAG_READY) == 0
                        || (path.m_pathFindFlags & PathUnit.FLAG_FAILED) != 0)
                    {
                        reason = "native-continuation-not-ready";
                        return false;
                    }

                    if (occupantCount == 0)
                    {
                        transferredPath = occupantPath;
                        transferredPositionIndex = occupantPositionIndex;
                        transferredOffset = occupantOffset;
                    }
                    else if (occupantPath != transferredPath
                             || occupantPositionIndex != transferredPositionIndex
                             || occupantOffset != transferredOffset)
                    {
                        reason = "occupant-continuations-diverged";
                        return false;
                    }

                    // Vanilla creates one parked identity for the car owner.
                    // Passengers normally have no personal parked-vehicle link;
                    // requiring one on every occupant rejected valid cohorts.
                    if (citizen.m_parkedVehicle != 0)
                    {
                        if (parkedId == 0)
                            parkedId = citizen.m_parkedVehicle;
                    }
                    occupantCount++;
                }
                unitId = unit.m_nextUnit;
            }

            if (unitId != 0u)
            {
                reason = "citizen-unit-chain-too-long";
                return false;
            }
            if (occupantCount == 0)
            {
                reason = "occupants-empty";
                return false;
            }
            if (parkedId != 0
                && !UndergroundParkingOccupancyManager.IsUsableParkedVehicle(parkedId))
            {
                reason = "native-owner-parked-identity-invalid";
                return false;
            }

            // A successful native ParkVehicle call can transfer the exact
            // pedestrian continuation without producing a surface parked-car
            // identity. That is still a complete native continuation result;
            // the already-reserved UPG transaction creates its parked identity
            // at the exact underground slot immediately before unloading.
            reason = parkedId == 0
                ? "native-transfer-captured-without-surface-parked-identity"
                : "native-transfer-captured";
            return true;
        }

        private static bool TryCaptureTmpeDeferredWalkingTransfer(
            ushort vehicleId,
            out int occupantCount,
            out ushort parkedId,
            out string reason)
        {
            occupantCount = 0;
            parkedId = 0;
            reason = "tmpe-deferred-walking-unavailable";
            CitizenManager citizenManager = CitizenManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            if (citizenManager == null
                || vehicleManager == null
                || vehicleId == 0
                || vehicleId >= vehicleManager.m_vehicles.m_size)
            {
                reason = "tmpe-manager-or-input-invalid";
                return false;
            }

            ref Vehicle vehicleData =
                ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            uint unitId = vehicleData.m_citizenUnits;
            int unitGuard = 0;
            while (unitId != 0u && unitGuard++ < 16)
            {
                if (unitId >= citizenManager.m_units.m_size)
                {
                    reason = "tmpe-citizen-unit-out-of-range";
                    return false;
                }

                CitizenUnit unit = citizenManager.m_units.m_buffer[unitId];
                for (int i = 0; i < 5; i++)
                {
                    uint citizenId = unit.GetCitizen(i);
                    if (citizenId == 0u)
                        continue;
                    if (citizenId >= citizenManager.m_citizens.m_size)
                    {
                        reason = "tmpe-citizen-out-of-range";
                        return false;
                    }

                    ref Citizen citizen =
                        ref citizenManager.m_citizens.m_buffer[citizenId];
                    ushort instanceId = citizen.m_instance;
                    if (instanceId == 0
                        || instanceId >= citizenManager.m_instances.m_size)
                    {
                        reason = "tmpe-occupant-instance-unavailable";
                        return false;
                    }

                    ref CitizenInstance instance =
                        ref citizenManager.m_instances.m_buffer[instanceId];
                    if ((instance.m_flags & CitizenInstance.Flags.Created) == 0
                        || instance.m_citizen != citizenId
                        || citizen.m_vehicle != vehicleId
                        || instance.m_path != 0u
                        || instance.Info == null
                        || !(instance.Info.m_citizenAI is HumanAI)
                        || !IsTmpeDeferredWalkingToTarget(instanceId))
                    {
                        reason =
                            "tmpe-deferred-walking-state-not-preserved";
                        return false;
                    }

                    if (citizen.m_parkedVehicle != 0)
                    {
                        if (parkedId == 0)
                            parkedId = citizen.m_parkedVehicle;
                        else if (parkedId != citizen.m_parkedVehicle)
                        {
                            reason = "tmpe-parked-identities-diverged";
                            return false;
                        }
                    }

                    occupantCount++;
                }
                unitId = unit.m_nextUnit;
            }

            if (unitId != 0u)
            {
                reason = "tmpe-citizen-unit-chain-too-long";
                return false;
            }
            if (occupantCount == 0 || parkedId == 0)
            {
                reason = occupantCount == 0
                    ? "tmpe-occupants-empty"
                    : "tmpe-owner-parked-identity-missing";
                return false;
            }
            if (!UndergroundParkingOccupancyManager.IsUsableParkedVehicle(
                    parkedId))
            {
                reason = "tmpe-owner-parked-identity-invalid";
                return false;
            }

            reason = "tmpe-deferred-walking-transfer-captured";
            return true;
        }

        private static bool IsTmpeDeferredWalkingToTarget(
            ushort citizenInstanceId)
        {
            if (citizenInstanceId == 0)
                return false;

            try
            {
                if (_tmpeExtCitizenInstanceManager == null
                    || _tmpeExtInstancesProperty == null
                    || _tmpePathModeField == null)
                {
                    Type managerType = null;
                    Assembly[] assemblies =
                        AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < assemblies.Length; i++)
                    {
                        managerType = assemblies[i].GetType(
                            "TrafficManager.Manager.Impl.ExtCitizenInstanceManager",
                            false);
                        if (managerType != null)
                            break;
                    }
                    if (managerType == null)
                        return false;

                    FieldInfo instanceField = managerType.GetField(
                        "Instance",
                        BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Static);
                    PropertyInfo instancesProperty = managerType.GetProperty(
                        "ExtInstances",
                        BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance);
                    object manager = instanceField == null
                        ? null
                        : instanceField.GetValue(null);
                    Array instances = manager == null
                        || instancesProperty == null
                        ? null
                        : instancesProperty.GetValue(manager, null) as Array;
                    Type elementType = instances == null
                        ? null
                        : instances.GetType().GetElementType();
                    FieldInfo pathModeField = elementType == null
                        ? null
                        : elementType.GetField(
                            "pathMode",
                            BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.Instance);
                    if (manager == null
                        || instancesProperty == null
                        || pathModeField == null)
                    {
                        return false;
                    }

                    _tmpeExtCitizenInstanceManager = manager;
                    _tmpeExtInstancesProperty = instancesProperty;
                    _tmpePathModeField = pathModeField;
                }

                Array extInstances =
                    _tmpeExtInstancesProperty.GetValue(
                        _tmpeExtCitizenInstanceManager,
                        null) as Array;
                if (extInstances == null
                    || citizenInstanceId >= extInstances.Length)
                {
                    return false;
                }

                object extInstance =
                    extInstances.GetValue(citizenInstanceId);
                object pathMode = extInstance == null
                    ? null
                    : _tmpePathModeField.GetValue(extInstance);
                return pathMode != null
                       && string.Equals(
                           pathMode.ToString(),
                           "RequiresWalkingPathToTarget",
                           StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTmpeParkingAiDrivingState(
            ushort vehicleId,
            bool requirePreselectedParking)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            CitizenManager citizenManager = CitizenManager.instance;
            if (vehicleId == 0
                || vehicleManager == null
                || citizenManager == null
                || vehicleId >= vehicleManager.m_vehicles.m_size)
                return false;

            ref Vehicle vehicleData =
                ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            uint unitId = vehicleData.m_citizenUnits;
            int unitGuard = 0;
            while (unitId != 0u && unitGuard++ < 16)
            {
                if (unitId >= citizenManager.m_units.m_size)
                    return false;
                CitizenUnit unit = citizenManager.m_units.m_buffer[unitId];
                for (int i = 0; i < 5; i++)
                {
                    uint citizenId = unit.GetCitizen(i);
                    if (citizenId == 0u
                        || citizenId >= citizenManager.m_citizens.m_size)
                        continue;
                    ushort instanceId =
                        citizenManager.m_citizens.m_buffer[citizenId].m_instance;
                    string pathMode;
                    if (TryReadTmpePathMode(instanceId, out pathMode))
                    {
                        bool preselected = string.Equals(
                                               pathMode,
                                               "DrivingToKnownParkPos",
                                               StringComparison.Ordinal)
                                           || string.Equals(
                                               pathMode,
                                               "DrivingToAltParkPos",
                                               StringComparison.Ordinal);
                        return preselected
                               || (!requirePreselectedParking
                                   && string.Equals(
                                       pathMode,
                                       "DrivingToTarget",
                                       StringComparison.Ordinal));
                    }
                }
                unitId = unit.m_nextUnit;
            }
            return false;
        }

        private static bool TryReadTmpePathMode(
            ushort citizenInstanceId,
            out string pathModeName)
        {
            pathModeName = string.Empty;
            if (citizenInstanceId == 0)
                return false;

            // Reuse the guarded soft-reflection initialization already owned
            // by the deferred-walking reader.
            IsTmpeDeferredWalkingToTarget(citizenInstanceId);
            if (_tmpeExtCitizenInstanceManager == null
                || _tmpeExtInstancesProperty == null
                || _tmpePathModeField == null)
                return false;

            try
            {
                Array extInstances = _tmpeExtInstancesProperty.GetValue(
                    _tmpeExtCitizenInstanceManager,
                    null) as Array;
                if (extInstances == null
                    || citizenInstanceId >= extInstances.Length)
                    return false;
                object extInstance = extInstances.GetValue(citizenInstanceId);
                object pathMode = extInstance == null
                    ? null
                    : _tmpePathModeField.GetValue(extInstance);
                if (pathMode == null)
                    return false;
                pathModeName = pathMode.ToString();
                return !string.IsNullOrEmpty(pathModeName);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidNativeParkingPosition(
            PathUnit.Position parkingPosition)
        {
            NetManager netManager = NetManager.instance;
            if (netManager == null
                || parkingPosition.m_segment == 0
                || parkingPosition.m_segment >= netManager.m_segments.m_size)
                return false;

            NetSegment segment =
                netManager.m_segments.m_buffer[parkingPosition.m_segment];
            NetInfo info = segment.Info;
            if (info == null
                || info.m_lanes == null
                || parkingPosition.m_lane >= info.m_lanes.Length)
                return false;

            NetInfo.Lane lane = info.m_lanes[parkingPosition.m_lane];
            if ((lane.m_laneType
                 & (NetInfo.LaneType.Vehicle
                    | NetInfo.LaneType.TransportVehicle)) == 0
                || (lane.m_vehicleType & VehicleInfo.VehicleType.Car) == 0)
                return false;

            try
            {
                uint laneId = PathManager.GetLaneID(parkingPosition);
                return laneId != 0u && laneId < netManager.m_lanes.m_size;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryPrepareRoutedArrival(
            PassengerCarAI __instance,
            ushort vehicleID,
            ref Vehicle vehicleData,
            out RoutedArrivalState state)
        {
            state = default(RoutedArrivalState);

            Vector3 surfacePosition;
            Quaternion surfaceRotation;
            vehicleData.GetSmoothPosition(vehicleID, out surfacePosition, out surfaceRotation);
            if (!UndergroundParkingAccessManager.IsFinite(surfacePosition)
                || !UndergroundParkingAccessManager.IsFinite(surfaceRotation))
                return false;

            int facilityId;
            if (!UndergroundParkingEntryRouteManager.TryBeginArrival(
                    vehicleID, ref vehicleData, surfacePosition, out facilityId))
                return false;
            if (!IsRoutedArrivalSurfacePoseValid(facilityId, surfacePosition))
            {
                UndergroundParkingEntryRouteManager.FailArrival(
                    vehicleID, "surface-pose-not-on-live-facility-road");
                return false;
            }

            // PassengerCarAI.ArriveAtTarget converts this existing record back
            // into the parked car. Prove it exists before that destructive call
            // unloads passengers and removes the original building target.
            uint ownerCitizen;
            ushort associatedParkedId = FindDriverParkedVehicle(
                vehicleID, ref vehicleData, out ownerCitizen);
            bool tmpeAdopted = UndergroundParkingEntryRouteManager
                .IsTmpeAdoptedArrival(vehicleID);
            if (tmpeAdopted && ownerCitizen == 0)
                ownerCitizen = PendingTmpeOwnerCitizensByVehicle[vehicleID];
            ushort parkedId = tmpeAdopted
                ? GetPendingTmpeParkedIdentity(vehicleID, vehicleData.Info)
                : associatedParkedId;
            if (parkedId != 0
                && !UndergroundParkingOccupancyManager.IsUsableParkedVehicle(parkedId))
                parkedId = 0;
            if (parkedId == 0 && ownerCitizen == 0)
            {
                UndergroundParkingEntryRouteManager.FailArrival(
                    vehicleID, "terminal-owner-identity-unavailable");
                return false;
            }

            Vector3 undergroundPosition;
            Quaternion undergroundRotation;
            int slotIndex;
            int reservedSlotIndex;
            if (!UndergroundParkingEntryRouteManager.TryGetReservedSlot(
                    vehicleID, out reservedSlotIndex))
            {
                UndergroundParkingEntryRouteManager.FailArrival(
                    vehicleID, "terminal-slot-reservation-unavailable");
                return false;
            }

            if (!UndergroundParkingOccupancyManager.TryGetRoutedArrivalReservationPose(
                    vehicleID,
                    facilityId,
                    reservedSlotIndex,
                    out undergroundPosition,
                    out undergroundRotation))
            {
                UndergroundParkingEntryRouteManager.FailArrival(
                    vehicleID, "slot-reservation-lost");
                return false;
            }
            slotIndex = reservedSlotIndex;

            VehicleInfo info = vehicleData.Info;
            VehicleAI vehicleAI = info == null ? null : info.m_vehicleAI;
            Color surfaceColor = vehicleAI == null
                ? Color.white
                : vehicleAI.GetColor(
                    vehicleID,
                    ref vehicleData,
                    InfoManager.InfoMode.None,
                    InfoManager.SubInfoMode.Default);
            state = new RoutedArrivalState(
                vehicleID,
                parkedId,
                facilityId,
                slotIndex,
                surfacePosition,
                undergroundPosition,
                undergroundRotation,
                info,
                surfaceColor,
                surfaceRotation,
                ownerCitizen);
            return true;
        }

        private static bool IsRoutedArrivalSurfacePoseValid(
            int facilityId,
            Vector3 surfacePosition)
        {
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            Vector3 lanePosition;
            Vector3 laneDirection;
            if (!UndergroundParkingAccessManager.IsFinite(surfacePosition)
                || !UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                    facilityId,
                    out facility,
                    out connection)
                || !UndergroundParkingAccessManager.TryGetLiveLanePose(
                    connection,
                    out lanePosition,
                    out laneDirection))
                return false;

            Vector3 delta = surfacePosition - lanePosition;
            return facility.IsValid
                   && Mathf.Abs(delta.y) <= 32f
                   && delta.sqrMagnitude <= 96f * 96f;
        }

        private static bool PassengerCarSimulationStepPrefix(
            PassengerCarAI __instance,
            ushort vehicleID,
            ref Vehicle data)
        {
            if (vehicleID == 0)
                return true;

            bool hasAdoptedArrivalRoute =
                UndergroundParkingEntryRouteManager.HasActiveRoute(vehicleID);
            bool parkingFlagSet =
                (data.m_flags & Vehicle.Flags.Parking) != 0;
            if (hasAdoptedArrivalRoute && parkingFlagSet)
            {
                // PassengerCarAI owns Vehicle.Flags.Parking as a persistent
                // top-level release instruction: if it survives to the next
                // SimulationStep, vanilla immediately releases the active car
                // without calling ArriveAtDestination. TM:PE sets that flag
                // while it prepares the future road-to-pedestrian transition.
                // Clear it on every tick of this exact adopted route, not only
                // on a tick preceded by another ParkVehicle invocation, so the
                // native road car can reach the portal and queue normally.
                data.m_flags &= ~Vehicle.Flags.Parking;
                if (_authoritativeParkingContinuationLogCount++ < 64)
                {
                    UndergroundParkingLog.Advanced(
                        "UPG preserved adopted parking vehicle for garage route: vehicle="
                        + vehicleID
                        + " action=clear-persistent-Parking-before-native-release-check");
                }
            }

            if (AuthoritativeParkingReroutes[vehicleID])
            {
                AuthoritativeParkingReroutes[vehicleID] = false;
            }

            UndergroundParkingEntryRouteManager.RetryFailedRoute(
                __instance,
                vehicleID,
                ref data);

            // Retry may clear or replace the route. Decide terminal ownership
            // only from the post-retry live compatibility token so a failed
            // transaction immediately returns to native movement.
            bool isTmpeAdoptedArrival =
                UndergroundParkingEntryRouteManager.IsTmpeAdoptedArrival(vehicleID);
            byte state = RoutedArrivalAnimationStates[vehicleID];
            if (isTmpeAdoptedArrival
                && (state == ArrivalStateWaitingAtRoadStop
                    || state == ArrivalStateAnimationQueued
                    || state == ArrivalStateControlledTraversal
                    || state == ArrivalStateControlledUnspawnRequested))
            {
                // Every waiting car remains a real, collidable road vehicle.
                // Native traffic therefore forms the physical queue behind
                // this exact head while UPG renews only its still-reserved
                // bay. The exact physical portal gate has already established
                // the terminal stop; running another movement tick here lets
                // the same FIFO owner drive away from that pose.
                UndergroundParkingEntryRouteManager.HasActiveRoute(vehicleID);
                return false;
            }

            if (isTmpeAdoptedArrival
                && state == ArrivalStateCommitRequested)
            {
                // The render-to-simulation commit request is completed by the
                // existing postfix or queued simulation action. Keep the exact
                // real head at its native stop until that atomic transaction
                // either retires it or restores the adopted reservation.
                return false;
            }
            return state != ArrivalStateCommittedOffCamera
                   && state != ArrivalStateRetireRequested;
        }

        private static void PassengerCarSimulationStepPostfix(
            PassengerCarAI __instance,
            ushort vehicleID,
            ref Vehicle data)
        {
            if (__instance == null || vehicleID == 0)
                return;

            byte animationState = RoutedArrivalAnimationStates[vehicleID];
            if (animationState == ArrivalStateWaitingAtRoadStop)
            {
                TryDispatchRoutedArrivalRoadQueue(
                    PendingRoutedArrivals[vehicleID]);
                return;
            }

            if (animationState == ArrivalStateAnimationQueued
                || animationState == ArrivalStateControlledTraversal
                || animationState == ArrivalStateControlledUnspawnRequested
                || animationState == ArrivalStateCommittedOffCamera
                || animationState == ArrivalStateRetireRequested
                || animationState == ArrivalStateAnimationCancelled)
                return;

            if (animationState == ArrivalStateCommitRequested)
            {
                ExecuteRoutedArrival(__instance, vehicleID, ref data, PendingRoutedArrivals[vehicleID]);
                CompleteSuppressedArrivalVisual(vehicleID);
                return;
            }

            if (animationState == ArrivalStateNone
                && UndergroundParkingEntryRouteManager
                    .IsTmpeAdoptedArrival(vehicleID))
            {
                // TM:PE completes realistic parking through its persistent
                // Parking instruction; it does not promise the vanilla
                // ArriveAtDestination callback. Observe vanilla's completed
                // movement output instead. The exact transaction lane,
                // direction and sub-metre portal gate remain mandatory, so
                // this cannot adopt a car merely because it is nearby.
                Vector3 nativePosition;
                Quaternion nativeRotation;
                data.GetSmoothPosition(
                    vehicleID,
                    out nativePosition,
                    out nativeRotation);
                int pendingFacilityId;
                bool portalBusy =
                    UndergroundParkingEntryRouteManager
                        .TryProbeRoadPortalArrival(
                            vehicleID,
                            ref data,
                            nativePosition,
                            out pendingFacilityId)
                    && UndergroundParkingPortalAnimationManager
                        .HasActivityForFacility(pendingFacilityId);
                if (!portalBusy)
                {
                    int stoppedFacility;
                    UndergroundParkingEntryRouteManager
                        .TryHoldRoadPortalArrival(
                            vehicleID,
                            ref data,
                            nativePosition,
                            out stoppedFacility);
                }
            }

            RoutedArrivalState state;
            if (!TryPrepareRoutedArrival(__instance, vehicleID, ref data, out state))
            {
                if (UndergroundParkingEntryRouteManager.ConsumeRerouteRequired(vehicleID))
                    RestartPassengerCarPath(__instance, vehicleID, ref data);
                return;
            }

            PendingRoutedArrivals[vehicleID] = state;
            bool tmpeControlledEntrance =
                UndergroundParkingEntryRouteManager.IsTmpeAdoptedArrival(
                    vehicleID);
            if (!UndergroundParkingOccupancyManager.SupportsAutomatedTunnel(
                    state.FacilityId))
            {
                // The footprint cannot supply a driveable tunnel on any of
                // its four garage walls. Keep the authoritative reservation,
                // occupant handoff and parked identity transaction, but do not
                // invent a steep visual route: commit and expose the car in its
                // assigned underground space exactly like an off-camera arrival.
                RoutedArrivalVisualsSuppressed[vehicleID] = true;
                RoutedArrivalAnimationStates[vehicleID] = ArrivalStateCommitRequested;
                UndergroundParkingLog.Advanced(
                    "UPG arrival tunnel automation omitted for infeasible footprint: vehicle="
                    + vehicleID
                    + " facility="
                    + state.FacilityId
                    + " fallback=direct-parked-space");
                ExecuteRoutedArrival(__instance, vehicleID, ref data, state);
                CompleteSuppressedArrivalVisual(vehicleID);
                return;
            }
            if (!tmpeControlledEntrance
                && !UndergroundParkingVisualManager.IsFacilityVisibleOnCamera(
                    state.FacilityId))
            {
                // A city-wide arrival must not hold a live road vehicle or
                // consume a portal animation slot for a visual the player
                // cannot see. The authoritative transaction still performs
                // every identity, pedestrian and occupancy check.
                RoutedArrivalVisualsSuppressed[vehicleID] = true;
                RoutedArrivalAnimationStates[vehicleID] = ArrivalStateCommitRequested;
                if (_offscreenArrivalLogCount++ < 24)
                {
                    UndergroundParkingLog.Advanced(
                        "UPG off-camera arrival visual suppressed without fallback transaction: vehicle="
                        + vehicleID
                        + " facility="
                        + state.FacilityId);
                }
                ExecuteRoutedArrival(__instance, vehicleID, ref data, state);
                CompleteSuppressedArrivalVisual(vehicleID);
                return;
            }

            if (_arrivalPresentationLogCount++ < 64)
            {
                UndergroundParkingLog.Advanced(
                    "UPG routed arrival presentation: vehicle="
                    + vehicleID
                    + " facility="
                    + state.FacilityId
                    + " action=join-facility-fifo-at-road-stop");
            }

            RoutedArrivalAnimationStates[vehicleID] = ArrivalStateWaitingAtRoadStop;
            if (!EnqueueRoutedArrivalRoadStop(state))
            {
                // This is deliberately fail-closed. Once TM:PE has created the
                // parked identity and deferred walk, a queue bookkeeping fault
                // may not eject occupants into the carriageway or release the
                // real car. Retain it at the native stop for diagnosis.
                UndergroundParkingLog.Error(
                    "UPG failed to register exact road-stop queue ownership: vehicle="
                    + vehicleID
                    + " facility="
                    + state.FacilityId
                    + " action=hold-real-car-at-portal");
                return;
            }

            // If the portal is idle, dispatch in this same simulation step.
            // The proxy remains hidden until the exact real body is either
            // retired by the ordinary flow or unspawned-but-retained by the
            // TM:PE endpoint-commit flow, so no duplicate frame is possible.
            TryDispatchRoutedArrivalRoadQueue(state);
        }

        private static void CompleteSuppressedArrivalVisual(ushort vehicleId)
        {
            if (vehicleId == 0
                || !RoutedArrivalVisualsSuppressed[vehicleId]
                || RoutedArrivalAnimationStates[vehicleId]
                   != ArrivalStateCommittedOffCamera)
            {
                return;
            }

            RoutedArrivalVisualsSuppressed[vehicleId] = false;
            RoutedArrivalAnimationStates[vehicleId] = ArrivalStateRetireRequested;
            SimulationManager simulationManager = SimulationManager.instance;
            if (simulationManager == null)
                RetireRoutedArrivalVehicle(vehicleId);
            else
                simulationManager.AddAction(() => RetireRoutedArrivalVehicle(vehicleId));
        }

        private static void ExecuteRoutedArrival(
            PassengerCarAI instance,
            ushort vehicleID,
            ref Vehicle data,
            RoutedArrivalState state)
        {
            if (vehicleID == 0
                || RoutedArrivalAnimationStates[vehicleID]
                   != ArrivalStateCommitRequested)
                return;

            if (!state.IsPending)
            {
                CancelPendingRoutedArrival(vehicleID, true);
                return;
            }

            UndergroundParkingFacility currentFacility;
            UndergroundParkingRoadConnection currentConnection;
            if (!UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                    state.FacilityId,
                    out currentFacility,
                    out currentConnection))
            {
                CancelPendingRoutedArrival(vehicleID, true);
                UndergroundParkingEntryRouteManager.FailArrival(
                    vehicleID, "entrance-unavailable-before-commit");
                return;
            }

            Vector3 pedestrianHandoff;
            uint pedestrianLaneId;
            int pedestrianLaneIndex;
            if (!TryResolvePavementHandoff(
                    currentFacility,
                    currentConnection,
                    out pedestrianHandoff,
                    out pedestrianLaneId,
                    out pedestrianLaneIndex))
            {
                CancelPendingRoutedArrival(vehicleID, true);
                UndergroundParkingEntryRouteManager.FailArrival(
                    vehicleID, "same-side-pavement-handoff-unavailable");
                return;
            }

            if (!state.SlotClaimed)
            {
                Vector3 claimedPosition;
                Quaternion claimedRotation;
                int claimedSlot;
                if (!UndergroundParkingOccupancyManager.TryClaimRoutedArrivalSlot(
                        vehicleID,
                        state.FacilityId,
                        state.SlotIndex,
                        out claimedPosition,
                        out claimedRotation,
                        out claimedSlot))
                {
                    CancelPendingRoutedArrival(vehicleID, true);
                    UndergroundParkingEntryRouteManager.FailArrival(
                        vehicleID,
                        "slot-reservation-lost-at-animation-start");
                    return;
                }

                state = state.WithSlotClaim(
                    claimedSlot,
                    claimedPosition,
                    claimedRotation);
                PendingRoutedArrivals[vehicleID] = state;
            }

            int occupantCount;
            ArrivalPedestrianContinuation continuation;
            if (ArrivalPedestrianContinuations.TryGetValue(vehicleID, out continuation))
            {
                occupantCount = continuation.Count;
                Array.Copy(
                    continuation.Occupants,
                    ArrivalOccupants,
                    continuation.Count);
            }
            else
            {
                uint nativeContinuationPath;
                int nativeContinuationPositionIndex;
                byte nativeContinuationOffset;
                bool deferredWalking =
                    NativeDeferredWalkingContinuations[vehicleID];
                if (deferredWalking)
                {
                    nativeContinuationPath = 0u;
                    nativeContinuationPositionIndex = -1;
                    nativeContinuationOffset = 0;
                }
                else if (!UndergroundParkingEntryRouteManager
                             .TryGetNativePedestrianContinuation(
                                 vehicleID,
                                 out nativeContinuationPath,
                                 out nativeContinuationPositionIndex,
                                 out nativeContinuationOffset))
                {
                    CancelPendingRoutedArrival(vehicleID, true);
                    UndergroundParkingEntryRouteManager.FailArrival(
                        vehicleID, "native-pedestrian-continuation-unavailable");
                    return;
                }
                if (!TryCaptureArrivalOccupants(
                        vehicleID,
                        state.FacilityId,
                        ref data,
                        nativeContinuationPath,
                        nativeContinuationPositionIndex,
                        nativeContinuationOffset,
                        deferredWalking,
                        out occupantCount))
                {
                    CancelPendingRoutedArrival(vehicleID, true);
                    UndergroundParkingEntryRouteManager.FailArrival(
                        vehicleID, "native-pedestrian-continuation-not-preserved");
                    return;
                }
                if (!SuppressArrivalParkedVehicleAssociations(occupantCount))
                {
                    CancelPendingRoutedArrival(vehicleID, true);
                    UndergroundParkingEntryRouteManager.FailArrival(
                        vehicleID, "pedestrian-parked-association-suppression-unavailable");
                    return;
                }
                continuation = new ArrivalPedestrianContinuation(
                    ArrivalOccupants,
                    occupantCount,
                    nativeContinuationPath,
                    nativeContinuationPositionIndex,
                    nativeContinuationOffset,
                    deferredWalking);
                ArrivalPedestrianContinuations[vehicleID] = continuation;
            }

            ushort parkedId = state.ParkedId;
            if (parkedId == 0)
            {
                parkedId = CreateRoutedParkedVehicle(state);
                if (parkedId != 0)
                {
                    state = state.WithParkedIdentity(parkedId, true);
                    PendingRoutedArrivals[vehicleID] = state;
                }
            }
            if (parkedId == 0)
            {
                RestoreDeferredArrivalAssociationsForOccupants(occupantCount);
                CancelPendingRoutedArrival(vehicleID, true);
                UndergroundParkingEntryRouteManager.FailArrival(
                    vehicleID, "parked-identity-unavailable-before-unload");
                return;
            }

            if (!continuation.ParkedIdentityLinked)
            {
                continuation.ParkedIdentityLinked = true;
                UndergroundParkingLifecycleDiagnostics.LinkParkedVehicle(
                    state.OwnerCitizen,
                    parkedId,
                    vehicleID,
                    state.FacilityId,
                    "arrival-parked-identity-ready");
            }

            int pedestrianContinuations = continuation == null
                ? 0
                : occupantCount;

            // Pedestrian routing is not allowed to veto a completed vehicle
            // arrival. Attach the inspected path when one is available;
            // otherwise retain the game's native pedestrian fallback. In both
            // modes detach every occupant with no old vehicle available to
            // substitute a road-side door and require the authoritative frame
            // to begin on the pavement before the car transaction can commit.
            int pavementHandedOff = PlaceArrivalOccupantsOnPavement(
                vehicleID,
                pedestrianHandoff,
                continuation);
            if (pavementHandedOff < 0)
            {
                if (state.CreatedParkedIdentity)
                    ReleaseCreatedRoutedParkedVehicle(parkedId, state.OwnerCitizen);
                RestoreDeferredArrivalAssociationsForOccupants(occupantCount);
                CancelPendingRoutedArrival(vehicleID, true);
                UndergroundParkingEntryRouteManager.FailArrival(
                    vehicleID, "authoritative-pavement-placement-rejected-before-mutation");
                return;
            }
            if (pavementHandedOff != occupantCount)
            {
                // Once the first occupant has left the car, rolling the vehicle
                // back to a road journey would split the cohort. Keep this exact
                // committed car frozen at the validated portal and retry only
                // the remaining native pavement exits on later simulation ticks.
                return;
            }
            // Vanilla may now finish target bookkeeping and its idempotent
            // citizen-unit unload pass; every occupant is already detached at
            // this exact pavement coordinate.
            if (!continuation.NativeArrivalFinalized)
            {
                Vector4 originalTarget = data.m_targetPos0;
                data.m_targetPos0 = pedestrianHandoff;
                data.m_targetPos0.w = 2f;
                bool arrived;
                NativeArrivalFinalizeAllowed[vehicleID] = true;
                try
                {
                    arrived = instance.ArriveAtDestination(vehicleID, ref data);
                }
                finally
                {
                    NativeArrivalFinalizeAllowed[vehicleID] = false;
                }
                if (!arrived)
                {
                    data.m_targetPos0 = originalTarget;
                    return;
                }
                continuation.NativeArrivalFinalized = true;
            }

            pavementHandedOff = CountPavementArrivalOccupants(
                occupantCount,
                pedestrianHandoff);
            if (pavementHandedOff != occupantCount)
            {
                return;
            }

            bool holdParkedVisual = !RoutedArrivalVisualsSuppressed[vehicleID];
            if (holdParkedVisual)
                UndergroundParkingOccupancyManager.SetParkedCarVisualHeld(parkedId, true);

            bool committed = UndergroundParkingOccupancyManager.CommitRoutedArrival(
                    parkedId,
                    state.FacilityId,
                    state.SlotIndex,
                    state.UndergroundPosition,
                    state.UndergroundRotation);
            if (!committed)
            {
                if (holdParkedVisual)
                    UndergroundParkingOccupancyManager.SetParkedCarVisualHeld(parkedId, false);
                return;
            }

            ClearPendingTmpeParkedIdentity(vehicleID, parkedId);

            if (RoutedArrivalVisualsSuppressed[vehicleID])
            {
                // Off-camera arrivals have no proxy completion callback.
                // Explicitly publish the committed parked body now, including
                // if this recycled ID was held by an earlier presentation.
                UndergroundParkingOccupancyManager.SetParkedCarVisualHeld(
                    parkedId,
                    false);
            }

            ArrivalPedestrianContinuations.Remove(vehicleID);
            NativeDeferredWalkingContinuations[vehicleID] = false;
            ClearAdoptedRoutedArrivalReleaseGuard(vehicleID);
            RoutedArrivalAnimationStates[vehicleID] = ArrivalStateCommitted;

            UndergroundParkingEntryRouteManager.CompleteArrival(vehicleID);

            if (RoutedArrivalVisualsSuppressed[vehicleID])
            {
                RoutedArrivalAnimationStates[vehicleID] = ArrivalStateCommittedOffCamera;
            }
            else
            {
                // QueueRoutedArrivalAnimation created the identical hidden
                // proxy before it requested this simulation action. Retire the
                // real car now, in this same successful commit action, so the
                // next render update can expose the proxy immediately at the
                // captured pose. State 8 remains the atomic acknowledgement:
                // there is still never a frame with two visible identities.
                RoutedArrivalAnimationStates[vehicleID] = ArrivalStateRetireRequested;
                RetireRoutedArrivalVehicle(vehicleID);
            }

            LogPavementPassengerHandoff(
                occupantCount,
                vehicleID,
                state.FacilityId,
                parkedId,
                pedestrianHandoff,
                pedestrianLaneId,
                pedestrianLaneIndex,
                pavementHandedOff,
                pedestrianContinuations);
        }

        private static bool TryCaptureArrivalOccupants(
            ushort vehicleId,
            int facilityId,
            ref Vehicle vehicleData,
            uint nativeContinuationPath,
            int nativeContinuationPositionIndex,
            byte nativeContinuationOffset,
            bool deferredWalking,
            out int count)
        {
            count = 0;
            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null)
                return false;

            UndergroundParkingLifecycleDiagnostics.BeginArrivalVehicle(vehicleId);

            uint unitId = vehicleData.m_citizenUnits;
            int unitGuard = 0;
            while (unitId != 0u
                && unitGuard++ < 16
                && count < ArrivalOccupants.Length)
            {
                if (unitId >= citizenManager.m_units.m_size)
                    break;

                CitizenUnit unit = citizenManager.m_units.m_buffer[unitId];
                for (int i = 0; i < 5 && count < ArrivalOccupants.Length; i++)
                {
                    uint citizenId = unit.GetCitizen(i);
                    if (citizenId == 0u || citizenId >= citizenManager.m_citizens.m_size)
                        continue;

                    ref Citizen citizen =
                        ref citizenManager.m_citizens.m_buffer[citizenId];
                    ushort instanceId = citizen.m_instance;
                    if (instanceId == 0 || instanceId >= citizenManager.m_instances.m_size)
                        return false;

                    ref CitizenInstance citizenInstance =
                        ref citizenManager.m_instances.m_buffer[instanceId];
                    // TM:PE's RequiresWalkingPathToTarget value is transient
                    // planning state. Its exact value was already proved in
                    // the successful ParkVehicle postfix before this vehicle
                    // received its durable adopted-route token. During a long
                    // serialized entrance traversal TM:PE may legitimately
                    // advance that external state. At commit, require the same
                    // created instance in the same unreleased vehicle and no
                    // replacement pedestrian path; do not demand the expired
                    // external planning flag a second time.
                    bool continuationPreserved = deferredWalking
                        ? citizenInstance.m_path == 0u
                        : citizenInstance.m_path == nativeContinuationPath
                          && citizenInstance.m_pathPositionIndex
                             == unchecked((byte)nativeContinuationPositionIndex)
                          && citizenInstance.m_lastPathOffset
                             == nativeContinuationOffset;
                    if ((citizenInstance.m_flags & CitizenInstance.Flags.Created) == 0
                        || citizenInstance.m_citizen != citizenId
                        || citizen.m_vehicle != vehicleId
                        || !continuationPreserved
                        || citizenInstance.Info == null
                        || !(citizenInstance.Info.m_citizenAI is HumanAI))
                    {
                        return false;
                    }

                    if (DeferredArrivalAssociations.ContainsKey(citizenId))
                        return false;

                    ArrivalOccupants[count++] = new ArrivalOccupant(
                        citizenId,
                        instanceId,
                        citizen.m_parkedVehicle);
                    UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                        citizenId,
                        "arrival-occupant-captured",
                        vehicleId,
                        citizen.m_parkedVehicle,
                        facilityId,
                        "citizenUnit=" + unitId
                        + " continuation="
                        + (deferredWalking
                            ? "tmpe-deferred-walk"
                            : "native-path"));
                }

                unitId = unit.m_nextUnit;
            }

            return unitId == 0u && count > 0;
        }

        private static int CountPavementArrivalOccupants(
            int occupantCount,
            Vector3 pedestrianHandoff)
        {
            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null)
                return 0;

            int handedOff = 0;
            int count = Mathf.Clamp(occupantCount, 0, ArrivalOccupants.Length);
            for (int i = 0; i < count; i++)
            {
                ArrivalOccupant occupant = ArrivalOccupants[i];
                if (occupant.CitizenId == 0u
                    || occupant.CitizenId >= citizenManager.m_citizens.m_size
                    || occupant.InstanceId == 0
                    || occupant.InstanceId >= citizenManager.m_instances.m_size)
                {
                    continue;
                }

                ref Citizen citizen =
                    ref citizenManager.m_citizens.m_buffer[occupant.CitizenId];
                ref CitizenInstance citizenInstance =
                    ref citizenManager.m_instances.m_buffer[occupant.InstanceId];
                if ((citizenInstance.m_flags & CitizenInstance.Flags.Created) == 0
                    || citizenInstance.m_citizen != occupant.CitizenId
                    || citizen.m_vehicle != 0)
                {
                    continue;
                }

                Vector3 delta = citizenInstance.GetLastFramePosition() - pedestrianHandoff;
                delta.y = 0f;
                if (delta.sqrMagnitude <= 4f)
                    handedOff++;
            }

            return handedOff;
        }

        private static int PlaceArrivalOccupantsOnPavement(
            ushort vehicleId,
            Vector3 pedestrianHandoff,
            ArrivalPedestrianContinuation continuation)
        {
            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null
                || vehicleId == 0
                || continuation == null)
                return -1;

            int count = Mathf.Clamp(continuation.Count, 0, ArrivalOccupants.Length);
            for (int i = 0; i < count; i++)
            {
                if (continuation.Placed[i])
                    continue;

                ArrivalOccupant occupant = continuation.Occupants[i];
                if (occupant.CitizenId == 0u
                    || occupant.CitizenId >= citizenManager.m_citizens.m_size
                    || occupant.InstanceId == 0
                    || occupant.InstanceId >= citizenManager.m_instances.m_size)
                {
                    return continuation.HandoffStarted ? CountPlaced(continuation) : -1;
                }

                ref Citizen citizen =
                    ref citizenManager.m_citizens.m_buffer[occupant.CitizenId];
                ref CitizenInstance citizenInstance =
                    ref citizenManager.m_instances.m_buffer[occupant.InstanceId];
                bool expectedVehicle = citizen.m_vehicle == vehicleId
                    || (continuation.HandoffStarted && citizen.m_vehicle == 0);
                bool expectedPath = continuation.DeferredWalking
                    ? citizenInstance.m_path == 0u
                    : citizenInstance.m_path == continuation.NativePath
                      && citizenInstance.m_pathPositionIndex
                         == unchecked((byte)continuation.NativePositionIndex)
                      && citizenInstance.m_lastPathOffset
                         == continuation.NativeSegmentOffset;
                if (!expectedVehicle
                    || (citizenInstance.m_flags & CitizenInstance.Flags.Created) == 0
                    || citizenInstance.m_citizen != occupant.CitizenId
                    || !expectedPath
                    || citizenInstance.Info == null
                    || !(citizenInstance.Info.m_citizenAI is HumanAI))
                    return continuation.HandoffStarted ? CountPlaced(continuation) : -1;
            }

            continuation.HandoffStarted = true;
            for (int i = 0; i < count; i++)
            {
                if (continuation.Placed[i])
                    continue;

                ArrivalOccupant occupant = continuation.Occupants[i];
                ref Citizen citizen =
                    ref citizenManager.m_citizens.m_buffer[occupant.CitizenId];
                ref CitizenInstance citizenInstance =
                    ref citizenManager.m_instances.m_buffer[occupant.InstanceId];
                CitizenAI citizenAi = citizenInstance.Info.m_citizenAI;

                UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                    occupant.CitizenId,
                    "arrival-native-continuation-preserved-before-exit",
                    vehicleId,
                    occupant.OriginalParkedId,
                    0,
                    "mode="
                    + (continuation.DeferredWalking
                        ? "tmpe-deferred-walk"
                        : "native-path")
                    + " path=" + citizenInstance.m_path
                    + " cursor=" + citizenInstance.m_pathPositionIndex
                    + " offset=" + citizenInstance.m_lastPathOffset
                    + " upgPathWrite=False");

                // Native HumanAI.SetCurrentVehicle remembers the old vehicle and
                // replaces the supplied coordinate with its closest door. Remove
                // that membership first so the pavement coordinate is preserved.
                if (citizen.m_vehicle == vehicleId)
                    citizen.SetVehicle(occupant.CitizenId, 0, 0u);
                UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                    occupant.CitizenId,
                    "arrival-old-vehicle-membership-cleared",
                    vehicleId,
                    occupant.OriginalParkedId,
                    0,
                    "handoff=" + UndergroundParkingLifecycleDiagnostics.FormatPosition(pedestrianHandoff));
                if (!citizenAi.SetCurrentVehicle(
                        occupant.InstanceId,
                        ref citizenInstance,
                        0,
                        0u,
                        pedestrianHandoff))
                {
                    continue;
                }

                Vector3 delta = citizenInstance.GetLastFramePosition()
                                - pedestrianHandoff;
                delta.y = 0f;
                if (citizen.m_vehicle == 0 && delta.sqrMagnitude <= 4f)
                    continuation.Placed[i] = true;
                UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                    occupant.CitizenId,
                    "arrival-native-pavement-exit-completed",
                    vehicleId,
                    occupant.OriginalParkedId,
                    0,
                    "within2m=" + (delta.sqrMagnitude <= 4f));
            }
            return CountPlaced(continuation);
        }

        private static int CountPlaced(ArrivalPedestrianContinuation continuation)
        {
            int placed = 0;
            for (int i = 0; i < continuation.Count; i++)
            {
                if (continuation.Placed[i])
                    placed++;
            }
            return placed;
        }

        private static bool SuppressArrivalParkedVehicleAssociations(int occupantCount)
        {
            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null)
                return false;

            int count = Mathf.Clamp(occupantCount, 0, ArrivalOccupants.Length);
            int required = 0;
            for (int i = 0; i < count; i++)
            {
                ArrivalOccupant occupant = ArrivalOccupants[i];
                if (occupant.CitizenId == 0u
                    || occupant.CitizenId >= citizenManager.m_citizens.m_size)
                {
                    return false;
                }

                ref Citizen citizen =
                    ref citizenManager.m_citizens.m_buffer[occupant.CitizenId];
                if (occupant.OriginalParkedId == 0)
                    continue;
                if (citizen.m_parkedVehicle != occupant.OriginalParkedId
                    || DeferredArrivalAssociations.ContainsKey(occupant.CitizenId))
                {
                    return false;
                }
                required++;
            }

            if (DeferredArrivalAssociations.Count + required
                > MaxDeferredArrivalAssociations)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                ArrivalOccupant occupant = ArrivalOccupants[i];
                if (occupant.OriginalParkedId == 0)
                    continue;

                AddDeferredArrivalAssociation(
                    occupant.CitizenId,
                    occupant.InstanceId,
                    occupant.OriginalParkedId);
                // Citizen.SetParkedVehicle(citizenId, 0) is destructive: native
                // code clears VehicleParked.m_ownerCitizen and releases the
                // parked record. We need only quarantine the citizen's lookup
                // link while its native pedestrian continuation starts. Keep
                // the exact parked identity alive for the underground commit
                // and restore this one field after the bounded quarantine.
                citizenManager.m_citizens.m_buffer[occupant.CitizenId]
                    .m_parkedVehicle = 0;
                UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                    occupant.CitizenId,
                    "arrival-parked-association-suppressed",
                    0,
                    occupant.OriginalParkedId,
                    0,
                    "parkedRecordPreserved=True"
                    + " heldUntilPedestrianIdleAfterCooldown=True"
                    + " cooldownFrames=" + PostArrivalRetrievalCooldownFrames);
            }

            return true;
        }

        private static bool AddDeferredArrivalAssociation(
            uint citizenId,
            ushort instanceId,
            ushort parkedId)
        {
            if (citizenId == 0u || parkedId == 0)
                return true;
            if (DeferredArrivalAssociations.ContainsKey(citizenId))
                return false;
            if (DeferredArrivalAssociations.Count >= MaxDeferredArrivalAssociations)
                return false;

            DeferredArrivalAssociations[citizenId] =
                new DeferredArrivalAssociation(
                    instanceId,
                    parkedId,
                    GetCurrentSimulationFrame()
                    + PostArrivalRetrievalCooldownFrames);
            DeferredArrivalOrder.Add(citizenId);
            return true;
        }

        private static void RestoreDeferredArrivalAssociationsForOccupants(
            int occupantCount)
        {
            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null)
                return;

            int count = Mathf.Clamp(occupantCount, 0, ArrivalOccupants.Length);
            for (int i = 0; i < count; i++)
            {
                uint citizenId = ArrivalOccupants[i].CitizenId;
                DeferredArrivalAssociation association;
                if (!DeferredArrivalAssociations.TryGetValue(
                        citizenId,
                        out association))
                {
                    continue;
                }

                RestoreDeferredArrivalAssociation(
                    citizenManager,
                    citizenId,
                    association,
                    "arrival-transaction-rollback");
                DeferredArrivalAssociations.Remove(citizenId);
            }
        }

        private static bool ShouldRestoreDeferredArrivalAssociation(
            CitizenManager citizenManager,
            uint citizenId,
            DeferredArrivalAssociation association)
        {
            if (citizenId == 0u || citizenId >= citizenManager.m_citizens.m_size)
                return true;

            ref Citizen citizen = ref citizenManager.m_citizens.m_buffer[citizenId];
            if (citizen.m_parkedVehicle != 0)
                return true;
            if (citizen.m_vehicle != 0)
                return false;

            uint frame = GetCurrentSimulationFrame();
            if (!HasReachedSimulationFrame(frame, association.EligibleFrame))
                return false;

            ushort instanceId = citizen.m_instance;
            if (instanceId == 0 || instanceId >= citizenManager.m_instances.m_size)
                return true;

            ref CitizenInstance citizenInstance =
                ref citizenManager.m_instances.m_buffer[instanceId];
            if ((citizenInstance.m_flags & CitizenInstance.Flags.Created) == 0
                || citizenInstance.m_citizen != citizenId)
            {
                return true;
            }

            // The original arrival instance may have despawned and a new trip
            // may already be active. Keep the car quarantined through that
            // whole trip as well; restoring ownership mid-path cannot alter the
            // current route, and would make the next immediate request target
            // the just-parked car again.
            return citizenInstance.m_path == 0u
                   && (citizenInstance.m_flags & CitizenInstance.Flags.WaitingPath) == 0;
        }

        private static uint GetCurrentSimulationFrame()
        {
            SimulationManager simulationManager = SimulationManager.instance;
            return simulationManager == null
                ? 0u
                : simulationManager.m_currentFrameIndex;
        }

        private static bool HasReachedSimulationFrame(uint frame, uint target)
        {
            return unchecked((int)(frame - target)) >= 0;
        }

        private static void RestoreDeferredArrivalAssociation(
            CitizenManager citizenManager,
            uint citizenId,
            DeferredArrivalAssociation association,
            string reason)
        {
            if (citizenId == 0u
                || citizenId >= citizenManager.m_citizens.m_size
                || association.ParkedId == 0
                || !UndergroundParkingOccupancyManager.IsUsableParkedVehicle(
                    association.ParkedId))
            {
                return;
            }

            ref Citizen citizen = ref citizenManager.m_citizens.m_buffer[citizenId];
            if (citizen.m_parkedVehicle != 0)
                return;

            citizen.SetParkedVehicle(citizenId, association.ParkedId);
            UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                citizenId,
                "arrival-parked-association-restored",
                0,
                association.ParkedId,
                0,
                "reason=" + reason
                + " eligibleFrame=" + association.EligibleFrame
                + " restoredFrame=" + GetCurrentSimulationFrame());
            if (_deferredArrivalLogCount++ < 48)
            {
                UndergroundParkingLog.Advanced(
                    "UPG restored parked ownership after pedestrian trip: citizen="
                    + citizenId
                    + " parked=" + association.ParkedId);
            }
        }

        private static void RestoreAllDeferredArrivalAssociations()
        {
            CitizenManager citizenManager = CitizenManager.instance;
            int retainedForReleaseLedger = 0;
            if (citizenManager != null)
            {
                foreach (KeyValuePair<uint, DeferredArrivalAssociation> pair
                         in DeferredArrivalAssociations)
                {
                    if (UndergroundParkingOccupancyManager
                            .IsPendingVanillaRelease(pair.Value.ParkedId))
                    {
                        // NUKE already transferred this exact car and owner to
                        // the persisted release ledger. Reattaching an active
                        // walker here would recreate the stale retrieval that
                        // a later garage could publish at its entrance.
                        retainedForReleaseLedger++;
                        continue;
                    }

                    RestoreDeferredArrivalAssociation(
                        citizenManager,
                        pair.Key,
                        pair.Value,
                        "mod-release");
                }
            }

            DeferredArrivalAssociations.Clear();
            DeferredArrivalOrder.Clear();
            _deferredArrivalCursor = 0;
            if (retainedForReleaseLedger > 0)
            {
                UndergroundParkingLog.Advanced(
                    "Transferred deferred parked associations to the NUKE release ledger without reattaching active walkers: count="
                    + retainedForReleaseLedger);
            }
        }

        private static void LogPavementPassengerHandoff(
            int occupantCount,
            ushort vehicleId,
            int facilityId,
            ushort parkedId,
            Vector3 pedestrianHandoff,
            uint pedestrianLaneId,
            int pedestrianLaneIndex,
            int pavementHandedOff,
            int pedestrianContinuations)
        {
            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null)
                return;

            int valid = 0;
            int walkingPaths = 0;
            int pavementAnchored = 0;
            int parkedOwnershipDeferred = 0;
            int count = Mathf.Clamp(occupantCount, 0, ArrivalOccupants.Length);
            for (int i = 0; i < count; i++)
            {
                ArrivalOccupant occupant = ArrivalOccupants[i];
                if (DeferredArrivalAssociations.ContainsKey(occupant.CitizenId))
                    parkedOwnershipDeferred++;
                if (occupant.CitizenId == 0u
                    || occupant.CitizenId >= citizenManager.m_citizens.m_size)
                {
                    continue;
                }

                if (occupant.InstanceId != 0
                    && occupant.InstanceId < citizenManager.m_instances.m_size)
                {
                    ref CitizenInstance citizenInstance =
                        ref citizenManager.m_instances.m_buffer[occupant.InstanceId];
                    if ((citizenInstance.m_flags & CitizenInstance.Flags.Created) != 0
                        && citizenInstance.m_citizen == occupant.CitizenId)
                    {
                        valid++;
                        if (citizenInstance.m_path != 0u
                            || (citizenInstance.m_flags
                                & CitizenInstance.Flags.WaitingPath) != 0)
                        {
                            walkingPaths++;
                        }
                        Vector3 position = citizenInstance.GetLastFramePosition();
                        Vector3 delta = position - pedestrianHandoff;
                        delta.y = 0f;
                        if (delta.sqrMagnitude <= 4f)
                            pavementAnchored++;
                    }
                }
                ArrivalOccupants[i] = default(ArrivalOccupant);
            }

            if (_arrivalPavementHandoffLogCount++ < 48)
            {
                UndergroundParkingLog.Advanced(
                    "UPG pavement passenger handoff candidate: vehicle="
                    + vehicleId
                    + " facility=" + facilityId
                    + " occupants=" + count
                    + " nativePavementHandoffs=" + pavementHandedOff
                    + " pedestrianContinuations=" + pedestrianContinuations
                    + " validInstances=" + valid
                    + " pavementAnchored=" + pavementAnchored
                    + " walkingPaths=" + walkingPaths
                    + " parkedOwnershipDeferred=" + parkedOwnershipDeferred
                    + " parked=" + parkedId
                    + " pedestrianLane=" + pedestrianLaneId
                    + " laneIndex=" + pedestrianLaneIndex
                    + " handoff=("
                    + pedestrianHandoff.x.ToString("0.0") + ","
                    + pedestrianHandoff.y.ToString("0.0") + ","
                    + pedestrianHandoff.z.ToString("0.0") + ")");
            }
        }

        internal static bool TryResolvePavementHandoff(
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection connection,
            out Vector3 handoff,
            out uint pedestrianLaneId,
            out int pedestrianLaneIndex)
        {
            handoff = Vector3.zero;
            pedestrianLaneId = 0u;
            pedestrianLaneIndex = -1;

            NetManager netManager = NetManager.instance;
            if (netManager == null
                || connection.SegmentId == 0
                || connection.SegmentId >= netManager.m_segments.m_size)
            {
                return false;
            }

            ref NetSegment segment =
                ref netManager.m_segments.m_buffer[connection.SegmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == 0
                || segment.Info == null)
            {
                return false;
            }

            Vector3 roadPosition;
            Vector3 entrancePosition;
            Vector3 roadDirection;
            Vector3 entranceSide;
            if (!UndergroundParkingGeometry.TryGetCurrentPlacement(
                    facility,
                    out roadPosition,
                    out entrancePosition,
                    out roadDirection,
                    out entranceSide))
            {
                return false;
            }

            float laneOffset;
            bool hasPedestrianLane = segment.GetClosestLanePosition(
                    entrancePosition,
                    NetInfo.LaneType.Pedestrian,
                    VehicleInfo.VehicleType.None,
                    VehicleInfo.VehicleCategory.None,
                    out handoff,
                    out pedestrianLaneId,
                    out pedestrianLaneIndex,
                    out laneOffset);

            Vector3 sideDelta = handoff - roadPosition;
            sideDelta.y = 0f;
            Vector3 entranceDelta = handoff - entrancePosition;
            entranceDelta.y = 0f;
            bool validPedestrianLane = hasPedestrianLane
                && pedestrianLaneId != 0u
                && pedestrianLaneIndex >= 0
                && Vector3.Dot(sideDelta, entranceSide) > 0.25f
                && entranceDelta.sqrMagnitude <= 24f * 24f
                && Mathf.Abs(handoff.y - entrancePosition.y) <= 4f;
            if (!validPedestrianLane)
                return false;

            Vector3 finalSideDelta = handoff - roadPosition;
            finalSideDelta.y = 0f;
            Vector3 finalEntranceDelta = handoff - entrancePosition;
            finalEntranceDelta.y = 0f;
            if (Vector3.Dot(finalSideDelta, entranceSide) <= 0.25f
                || finalEntranceDelta.sqrMagnitude > 24f * 24f
                || Mathf.Abs(handoff.y - entrancePosition.y) > 4f)
            {
                handoff = Vector3.zero;
                pedestrianLaneId = 0u;
                pedestrianLaneIndex = -1;
                return false;
            }

            // Keep the exact position returned by the validated pedestrian
            // lane. Rebuilding it from road prefab widths can put a cim between
            // the actual lane and carriageway on asymmetric, custom or sloped
            // roads and also makes retrieval target a different point.
            handoff.y += 0.05f;
            return true;
        }

        private static bool PassengerCarReleaseVehiclePrefix(
            PassengerCarAI __instance,
            ushort vehicleID,
            ref Vehicle data)
        {
            if (vehicleID != 0 && RoutedArrivalReleaseAllowed[vehicleID])
            {
                ClearAdoptedRoutedArrivalReleaseGuard(vehicleID);
                return true;
            }

            if (IsAdoptedRoutedArrivalReleaseProtected(vehicleID))
            {
                int facilityId;
                if (UndergroundParkingEntryRouteManager.TryProbeRoadPortalArrival(
                        vehicleID,
                        ref data,
                        data.GetLastFramePosition(),
                        out facilityId))
                {
                    if (!UndergroundParkingPortalAnimationManager
                            .HasActivityForFacility(facilityId))
                    {
                        UndergroundParkingEntryRouteManager.TryHoldRoadPortalArrival(
                            vehicleID,
                            ref data,
                            data.GetLastFramePosition(),
                            out facilityId);
                    }
                    return false;
                }

                if (RestartPassengerCarPath(__instance, vehicleID, ref data))
                {
                    UndergroundParkingEntryRouteManager.ReturnToNativePath(
                        vehicleID,
                        "native-path-continued-after-release-request");
                    NativeDeferredWalkingContinuations[vehicleID] = false;
                    ClearPendingTmpeParkedIdentity(vehicleID, 0);
                    ClearAdoptedRoutedArrivalReleaseGuard(vehicleID);
                }
                return false;
            }

            if (vehicleID != 0)
            {
                if (UndergroundParkingEntryRouteManager
                        .IsTmpeAdoptedArrival(vehicleID)
                    || IsArrivalPavementHandoffStarted(vehicleID)
                    || IsPendingRoutedArrivalRoadVehicle(vehicleID))
                {
                    // Keep every exact adopted road car intact through its FIFO
                    // wait and commit, irrespective of whether vanilla supplied
                    // a path continuation or TM:PE supplied deferred walking.
                    return false;
                }
            }

            if (vehicleID != 0)
            {
                AuthoritativeParkingReroutes[vehicleID] = false;
                NativeDeferredWalkingContinuations[vehicleID] = false;
                ClearAdoptedRoutedArrivalReleaseGuard(vehicleID);
                ReleaseNativeContinuationPathHold(vehicleID);
                NativeArrivalFinalizeAllowed[vehicleID] = false;
            }
            if (vehicleID != 0
                && (RoutedArrivalAnimationStates[vehicleID]
                    == ArrivalStateWaitingAtRoadStop
                    || RoutedArrivalAnimationStates[vehicleID]
                    == ArrivalStateAnimationQueued
                    || RoutedArrivalAnimationStates[vehicleID]
                    == ArrivalStateCommitRequested
                    || ArrivalPedestrianContinuations.ContainsKey(vehicleID)))
                CancelPendingRoutedArrival(vehicleID, true);
            UndergroundParkingEntryRouteManager.ReleaseVehicle(vehicleID);
            ClearPendingTmpeParkedIdentity(vehicleID, 0);
            return true;
        }

        private static bool RestartPassengerCarPath(
            PassengerCarAI ai,
            ushort vehicleId,
            ref Vehicle vehicleData)
        {
            if (ai == null || TopLevelStartPathFind == null)
                return false;
            return TopLevelStartPathFind(ai, vehicleId, ref vehicleData);
        }

        internal static bool RestartTmpeParkingSearch(ushort vehicleId)
        {
            VehicleManager manager = VehicleManager.instance;
            if (manager == null
                || vehicleId == 0
                || vehicleId >= manager.m_vehicles.m_size)
                return false;

            ref Vehicle vehicleData =
                ref manager.m_vehicles.m_buffer[vehicleId];
            if ((vehicleData.m_flags & Vehicle.Flags.Created) == 0)
                return false;

            VehicleInfo info = vehicleData.Info;
            PassengerCarAI ai = info == null
                ? null
                : info.m_vehicleAI as PassengerCarAI;
            return RestartPassengerCarPath(ai, vehicleId, ref vehicleData);
        }

        private static TopLevelStartPathFindDelegate CreateTopLevelStartPathFindDelegate()
        {
            MethodInfo method = typeof(PassengerCarAI).GetMethod(
                "StartPathFind",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() },
                null);
            if (method == null)
                return null;

            return (TopLevelStartPathFindDelegate)Delegate.CreateDelegate(
                typeof(TopLevelStartPathFindDelegate), null, method);
        }

        private static ushort FindDriverParkedVehicle(
            ushort vehicleId,
            ref Vehicle vehicleData,
            out uint ownerCitizen)
        {
            ownerCitizen = 0;
            CitizenManager manager = CitizenManager.instance;
            if (manager == null)
                return 0;

            uint unitId = vehicleData.m_citizenUnits;
            int guard = 0;
            while (unitId != 0u && guard++ < 16)
            {
                if (unitId >= manager.m_units.m_size)
                    break;

                CitizenUnit unit = manager.m_units.m_buffer[unitId];
                for (int i = 0; i < 5; i++)
                {
                    uint citizenId = unit.GetCitizen(i);
                    if (citizenId == 0u || citizenId >= manager.m_citizens.m_size)
                        continue;

                    Citizen citizen = manager.m_citizens.m_buffer[citizenId];
                    if (citizen.m_vehicle != 0 && citizen.m_vehicle != vehicleId)
                        continue;

                    if (ownerCitizen == 0)
                        ownerCitizen = citizenId;
                    if (citizen.m_parkedVehicle != 0)
                        return citizen.m_parkedVehicle;
                }

                unitId = unit.m_nextUnit;
            }

            return 0;
        }

        private static void BindPendingTmpeParkedIdentity(
            ushort vehicleId,
            ushort parkedId,
            uint ownerCitizen)
        {
            if (vehicleId == 0 || parkedId == 0)
                return;

            ClearPendingTmpeParkedIdentity(vehicleId, 0);
            ushort previousVehicle = PendingTmpeParkedVehicleOwners[parkedId];
            if (previousVehicle != 0 && previousVehicle != vehicleId)
                PendingTmpeParkedVehiclesByVehicle[previousVehicle] = 0;
            PendingTmpeParkedVehicleOwners[parkedId] = vehicleId;
            PendingTmpeParkedVehiclesByVehicle[vehicleId] = parkedId;
            PendingTmpeOwnerCitizensByVehicle[vehicleId] = ownerCitizen;
        }

        private static uint FindPendingTmpeOwnerCitizen(
            ushort vehicleId,
            ref Vehicle vehicleData,
            ushort parkedId)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleManager != null
                && parkedId != 0
                && parkedId < vehicleManager.m_parkedVehicles.m_size)
            {
                uint recordOwner = vehicleManager.m_parkedVehicles
                    .m_buffer[parkedId].m_ownerCitizen;
                if (recordOwner != 0)
                    return recordOwner;
            }

            uint ownerCitizen;
            FindDriverParkedVehicle(vehicleId, ref vehicleData, out ownerCitizen);
            return ownerCitizen;
        }

        private static ushort GetPendingTmpeParkedIdentity(
            ushort vehicleId,
            VehicleInfo expectedInfo)
        {
            if (vehicleId == 0)
                return 0;

            ushort parkedId = PendingTmpeParkedVehiclesByVehicle[vehicleId];
            if (parkedId == 0)
                return 0;
            if (PendingTmpeParkedVehicleOwners[parkedId] != vehicleId
                || !UndergroundParkingOccupancyManager.IsUsableParkedVehicle(parkedId))
            {
                UndergroundParkingLog.Warning(
                    "UPG TM:PE early parked identity unavailable at terminal; exact owner/prefab fallback retained: vehicle="
                    + vehicleId
                    + " parked="
                    + parkedId
                    + " owner="
                    + PendingTmpeOwnerCitizensByVehicle[vehicleId]);
                ClearPendingTmpeParkedRecordBinding(vehicleId, parkedId);
                return 0;
            }

            VehicleManager manager = VehicleManager.instance;
            VehicleInfo parkedInfo = manager == null
                ? null
                : manager.m_parkedVehicles.m_buffer[parkedId].Info;
            if (parkedInfo != expectedInfo)
            {
                UndergroundParkingLog.Warning(
                    "UPG rejected mismatched TM:PE parked identity: vehicle="
                    + vehicleId
                    + " parked="
                    + parkedId
                    + " roadModel="
                    + (expectedInfo == null ? "<none>" : expectedInfo.name)
                    + " parkedModel="
                    + (parkedInfo == null ? "<none>" : parkedInfo.name));
                ClearPendingTmpeParkedRecordBinding(vehicleId, parkedId);
                return 0;
            }

            return parkedId;
        }

        private static void ClearPendingTmpeParkedIdentity(
            ushort vehicleId,
            ushort expectedParkedId)
        {
            if (vehicleId == 0)
                return;

            ushort parkedId = PendingTmpeParkedVehiclesByVehicle[vehicleId];
            if (expectedParkedId != 0
                && parkedId != 0
                && parkedId != expectedParkedId)
                return;
            ClearPendingTmpeParkedRecordBinding(vehicleId, parkedId);
            PendingTmpeOwnerCitizensByVehicle[vehicleId] = 0;
        }

        private static void ClearPendingTmpeParkedRecordBinding(
            ushort vehicleId,
            ushort parkedId)
        {
            if (vehicleId == 0)
                return;

            PendingTmpeParkedVehiclesByVehicle[vehicleId] = 0;
            if (parkedId != 0
                && PendingTmpeParkedVehicleOwners[parkedId] == vehicleId)
            {
                PendingTmpeParkedVehicleOwners[parkedId] = 0;
            }
        }

        private static ushort CreateRoutedParkedVehicle(RoutedArrivalState state)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            SimulationManager simulationManager = SimulationManager.instance;
            if (vehicleManager == null
                || simulationManager == null
                || state.Info == null
                || state.OwnerCitizen == 0
                || !UndergroundParkingAccessManager.IsFinite(
                    state.UndergroundPosition)
                || !UndergroundParkingAccessManager.IsFinite(
                    state.UndergroundRotation))
                return 0;

            ushort parkedId;
            if (!vehicleManager.CreateParkedVehicle(
                    out parkedId,
                    ref simulationManager.m_randomizer,
                    state.Info,
                    state.UndergroundPosition,
                    state.UndergroundRotation,
                    state.OwnerCitizen))
                return 0;

            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null
                || state.OwnerCitizen >= citizenManager.m_citizens.m_size)
            {
                vehicleManager.ReleaseParkedVehicle(parkedId);
                return 0;
            }

            ushort ownerInstance =
                citizenManager.m_citizens.m_buffer[state.OwnerCitizen].m_instance;
            if (!AddDeferredArrivalAssociation(
                    state.OwnerCitizen,
                    ownerInstance,
                    parkedId))
            {
                vehicleManager.ReleaseParkedVehicle(parkedId);
                return 0;
            }

            return parkedId;
        }

        private static void ReleaseCreatedRoutedParkedVehicle(
            ushort parkedId,
            uint ownerCitizen)
        {
            UndergroundParkingOccupancyManager.ReleaseParkedVehicleRecord(
                parkedId,
                ownerCitizen);
        }

        private static bool QueueRoutedArrivalAnimation(RoutedArrivalState state)
        {
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            if (state.Info == null
                || !UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                    state.FacilityId, out facility, out connection))
                return false;

            UndergroundParkingRoadConnection assignedConnection;
            if (TmpeParkingCompatibilityManager.TryGetArrivalConnection(
                    state.VehicleId,
                    out assignedConnection))
            {
                connection = assignedConnection;
            }

            return UndergroundParkingPortalAnimationManager.QueueArrival(
                state.Info,
                facility,
                connection,
                state.SurfacePosition,
                state.SurfaceRotation,
                state.SurfaceColor,
                state.VehicleId,
                UndergroundParkingEntryRouteManager.IsTmpeAdoptedArrival(
                    state.VehicleId));
        }

        private static bool EnqueueRoutedArrivalRoadStop(RoutedArrivalState state)
        {
            if (!state.IsPending)
                return false;

            int existingFacility =
                RoutedArrivalRoadQueueFacilities[state.VehicleId];
            if (existingFacility != 0)
                return existingFacility == state.FacilityId;

            Queue<ushort> queue;
            if (!RoutedArrivalRoadQueues.TryGetValue(state.FacilityId, out queue))
            {
                queue = new Queue<ushort>();
                RoutedArrivalRoadQueues[state.FacilityId] = queue;
            }

            RoutedArrivalRoadQueueFacilities[state.VehicleId] = state.FacilityId;
            queue.Enqueue(state.VehicleId);
            if (_arrivalRoadQueueLogCount++ < 64)
            {
                UndergroundParkingLog.Advanced(
                    "UPG real arrival joined facility road queue: vehicle="
                    + state.VehicleId
                    + " facility="
                    + state.FacilityId
                    + " queueDepth="
                    + queue.Count
                    + " ownership=native-road-collision");
            }
            return true;
        }

        private static void TryDispatchRoutedArrivalRoadQueue(
            RoutedArrivalState state)
        {
            if (!state.IsPending
                || RoutedArrivalAnimationStates[state.VehicleId]
                   != ArrivalStateWaitingAtRoadStop
                || RoutedArrivalRoadQueueFacilities[state.VehicleId]
                   != state.FacilityId)
            {
                return;
            }

            Queue<ushort> queue;
            if (!RoutedArrivalRoadQueues.TryGetValue(state.FacilityId, out queue))
                return;

            while (queue.Count > 0)
            {
                ushort queuedVehicle = queue.Peek();
                RoutedArrivalState queuedState =
                    PendingRoutedArrivals[queuedVehicle];
                if (queuedVehicle != 0
                    && RoutedArrivalRoadQueueFacilities[queuedVehicle]
                       == state.FacilityId
                    && RoutedArrivalAnimationStates[queuedVehicle]
                       == ArrivalStateWaitingAtRoadStop
                    && queuedState.IsPending
                    && queuedState.FacilityId == state.FacilityId)
                {
                    break;
                }

                queue.Dequeue();
                if (queuedVehicle != 0
                    && RoutedArrivalRoadQueueFacilities[queuedVehicle]
                       == state.FacilityId)
                {
                    RoutedArrivalRoadQueueFacilities[queuedVehicle] = 0;
                }
            }

            if (queue.Count == 0)
            {
                RoutedArrivalRoadQueues.Remove(state.FacilityId);
                return;
            }

            if (queue.Peek() != state.VehicleId)
                return;

            if (UndergroundParkingPortalAnimationManager.HasActivityForFacility(
                    state.FacilityId))
            {
                if (_arrivalRoadQueueWaitLogCount++ < 64)
                {
                    UndergroundParkingLog.Advanced(
                        "UPG real arrival waiting at facility road stop: vehicle="
                        + state.VehicleId
                        + " facility="
                        + state.FacilityId
                        + " reason=portal-occupied");
                }
                return;
            }

            // Publish the state before the render-thread queue entry. The
            // animation driver may dequeue immediately; it must always see the
            // exact car as animation-queued before requesting commit.
            RoutedArrivalAnimationStates[state.VehicleId] =
                ArrivalStateAnimationQueued;
            if (!QueueRoutedArrivalAnimation(state))
            {
                RoutedArrivalAnimationStates[state.VehicleId] =
                    ArrivalStateWaitingAtRoadStop;
                return;
            }

            queue.Dequeue();
            RoutedArrivalRoadQueueFacilities[state.VehicleId] = 0;
            if (queue.Count == 0)
                RoutedArrivalRoadQueues.Remove(state.FacilityId);

            if (_arrivalRoadQueueDispatchLogCount++ < 64)
            {
                UndergroundParkingLog.Advanced(
                    "UPG facility road queue dispatched exact head: vehicle="
                    + state.VehicleId
                    + " facility="
                    + state.FacilityId
                    + " remaining="
                    + queue.Count
                    + " handoff="
                    + (UndergroundParkingEntryRouteManager
                            .IsTmpeAdoptedArrival(state.VehicleId)
                        ? "unspawn-proxy-traverse-then-endpoint-commit"
                        : "commit-then-despawn-then-proxy"));
            }
        }

        private static void RemoveRoutedArrivalRoadQueueOwnership(
            ushort vehicleId)
        {
            if (vehicleId != 0)
                RoutedArrivalRoadQueueFacilities[vehicleId] = 0;
        }

        private static bool IsPendingRoutedArrivalRoadVehicle(ushort vehicleId)
        {
            if (vehicleId == 0)
                return false;

            byte state = RoutedArrivalAnimationStates[vehicleId];
            return (state == ArrivalStateWaitingAtRoadStop
                    || state == ArrivalStateAnimationQueued
                    || state == ArrivalStateControlledTraversal
                    || state == ArrivalStateControlledUnspawnRequested
                    || state == ArrivalStateCommitRequested)
                   && PendingRoutedArrivals[vehicleId].IsPending
                   && PendingRoutedArrivals[vehicleId].VehicleId == vehicleId;
        }

        private static void CancelPendingRoutedArrival(ushort vehicleId, bool cancelAnimation)
        {
            if (vehicleId == 0)
                return;

            byte priorAnimationState =
                RoutedArrivalAnimationStates[vehicleId];

            ArrivalPedestrianContinuation continuation;
            if (ArrivalPedestrianContinuations.TryGetValue(vehicleId, out continuation))
            {
                // A road fallback is safe only while the entire cohort is still
                // in the exact active car. Once native pavement handoff begins,
                // retain the adopted transaction and let later simulation ticks
                // finish only the occupants not yet placed.
                if (continuation.HandoffStarted)
                    return;

                Array.Copy(
                    continuation.Occupants,
                    ArrivalOccupants,
                    continuation.Count);
                RestoreDeferredArrivalAssociationsForOccupants(continuation.Count);
                ArrivalPedestrianContinuations.Remove(vehicleId);
            }

            RoutedArrivalState state = PendingRoutedArrivals[vehicleId];
            bool preserveAdoptedReservation =
                UndergroundParkingEntryRouteManager
                    .IsTmpeAdoptedArrival(vehicleId);
            if (state.IsPending)
            {
                if (state.SlotClaimed)
                {
                    UndergroundParkingOccupancyManager.CancelPendingSlotClaim(
                        state.FacilityId,
                        state.SlotIndex);
                    if (preserveAdoptedReservation)
                    {
                        UndergroundParkingOccupancyManager
                            .TryRestoreRoutedArrivalSlot(
                                vehicleId,
                                state.FacilityId,
                                state.SlotIndex);
                    }
                }
                else if (!preserveAdoptedReservation)
                {
                    UndergroundParkingOccupancyManager.ReleaseRoutedArrivalSlot(
                        vehicleId,
                        state.FacilityId,
                        state.SlotIndex);
                }
            }
            PendingRoutedArrivals[vehicleId] = default(RoutedArrivalState);
            RoutedArrivalHandoffPoseValid[vehicleId] = false;
            RoutedArrivalRenderCandidateValid[vehicleId] = false;
            RoutedArrivalUnspawnActionQueued[vehicleId] = false;
            RemoveRoutedArrivalRoadQueueOwnership(vehicleId);
            if (!preserveAdoptedReservation)
                NativeDeferredWalkingContinuations[vehicleId] = false;
            bool hadQueuedVisual = !RoutedArrivalVisualsSuppressed[vehicleId]
                                   && (priorAnimationState
                                       == ArrivalStateAnimationQueued
                                       || priorAnimationState
                                       == ArrivalStateCommitRequested);
            RoutedArrivalVisualsSuppressed[vehicleId] = false;
            RoutedArrivalAnimationStates[vehicleId] = cancelAnimation && hadQueuedVisual
                ? ArrivalStateAnimationCancelled
                : ArrivalStateNone;
        }

        private static bool IsArrivalPavementHandoffStarted(ushort vehicleId)
        {
            ArrivalPedestrianContinuation continuation;
            return vehicleId != 0
                   && ArrivalPedestrianContinuations.TryGetValue(vehicleId, out continuation)
                   && continuation.HandoffStarted;
        }

        public static void MarkRoutedArrivalAnimationReady(ushort vehicleId)
        {
            if (vehicleId == 0
                || RoutedArrivalAnimationStates[vehicleId]
                   != ArrivalStateAnimationQueued)
                return;

            bool controlledTmpeTraversal =
                UndergroundParkingEntryRouteManager.IsTmpeAdoptedArrival(
                    vehicleId);
            if (controlledTmpeTraversal)
                ObserveRoutedArrivalRenderPose(vehicleId);
            RoutedArrivalAnimationStates[vehicleId] = controlledTmpeTraversal
                ? ArrivalStateControlledUnspawnRequested
                : ArrivalStateCommitRequested;
            SimulationManager simulationManager = SimulationManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            if (simulationManager == null || vehicleManager == null)
            {
                AbortRoutedArrivalAnimation(vehicleId);
                return;
            }

            // A controlled handoff must leave the first observation on screen
            // for one complete Unity render cycle before native unspawn can be
            // requested. The animation driver queues that action on its next
            // update after promoting the now-confirmed sample.
            if (controlledTmpeTraversal)
                return;

            simulationManager.AddAction(() =>
            {
                if (RoutedArrivalAnimationStates[vehicleId]
                    != ArrivalStateCommitRequested
                    || vehicleId >= vehicleManager.m_vehicles.m_size)
                    return;

                ref Vehicle vehicleData = ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                PassengerCarAI ai = vehicleData.Info == null
                    ? null
                    : vehicleData.Info.m_vehicleAI as PassengerCarAI;
                if (ai == null || (vehicleData.m_flags & Vehicle.Flags.Created) == 0)
                {
                    AbortRoutedArrivalAnimation(vehicleId);
                    return;
                }

                ExecuteRoutedArrival(
                    ai,
                    vehicleId,
                    ref vehicleData,
                    PendingRoutedArrivals[vehicleId]);
            });
        }

        public static void RequestRoutedArrivalNativeUnspawn(ushort vehicleId)
        {
            if (vehicleId == 0
                || RoutedArrivalAnimationStates[vehicleId]
                   != ArrivalStateControlledUnspawnRequested
                || !RoutedArrivalHandoffPoseValid[vehicleId]
                || RoutedArrivalUnspawnActionQueued[vehicleId])
                return;

            SimulationManager simulationManager = SimulationManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            if (simulationManager == null || vehicleManager == null)
            {
                AbortRoutedArrivalAnimation(vehicleId);
                return;
            }

            RoutedArrivalUnspawnActionQueued[vehicleId] = true;
            simulationManager.AddAction(() =>
            {
                if (RoutedArrivalAnimationStates[vehicleId]
                    != ArrivalStateControlledUnspawnRequested
                    || vehicleId >= vehicleManager.m_vehicles.m_size)
                    return;

                ref Vehicle vehicleData =
                    ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                PassengerCarAI ai = vehicleData.Info == null
                    ? null
                    : vehicleData.Info.m_vehicleAI as PassengerCarAI;
                if (ai == null
                    || (vehicleData.m_flags & Vehicle.Flags.Created) == 0)
                {
                    AbortRoutedArrivalAnimation(vehicleId);
                    return;
                }

                // Hide, but do not release, the exact native vehicle. The
                // published handoff pose is deliberately one render-confirmed
                // sample behind the observation currently being prepared on
                // the Unity thread. A concurrent unspawn can therefore never
                // publish an interpolation pose which no native render showed.
                vehicleData.Unspawn(vehicleId);
                if ((vehicleData.m_flags & Vehicle.Flags.Spawned) != 0)
                {
                    AbortRoutedArrivalAnimation(vehicleId);
                    return;
                }
                RoutedArrivalAnimationStates[vehicleId] =
                    ArrivalStateControlledTraversal;
            });
        }

        public static void ObserveRoutedArrivalRenderPose(ushort vehicleId)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            SimulationManager simulationManager = SimulationManager.instance;
            if (vehicleId == 0
                || vehicleManager == null
                || simulationManager == null
                || vehicleId >= vehicleManager.m_vehicles.m_size)
                return;

            ref Vehicle vehicleData =
                ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            VehicleInfo info = vehicleData.Info;
            if (info == null
                || (vehicleData.m_flags & Vehicle.Flags.Created) == 0
                || (vehicleData.m_flags & Vehicle.Flags.Spawned) == 0)
                return;

            // Only a candidate which survived spawned through the following
            // Unity update is known to have crossed a render boundary. Promote
            // it before calculating the next candidate. If simulation unspawn
            // races the next render, that newer unconfirmed sample is ignored.
            if (RoutedArrivalRenderCandidateValid[vehicleId])
            {
                RoutedArrivalHandoffPositions[vehicleId] =
                    RoutedArrivalRenderCandidatePositions[vehicleId];
                RoutedArrivalHandoffRotations[vehicleId] =
                    RoutedArrivalRenderCandidateRotations[vehicleId];
                RoutedArrivalHandoffPoseValid[vehicleId] = true;
            }

            // Match Vehicle.RenderInstance's frame selection and interpolation
            // exactly. The portal animation update repeats this observation
            // while it waits for the simulation-thread unspawn acknowledgement.
            // Once Spawned clears, this method stops updating the stored pose,
            // leaving the last pose at which the native body was still visible.
            uint targetFrame = vehicleData.GetTargetFrame(info, vehicleId);
            Vehicle.Frame older = vehicleData.GetFrameData(targetFrame - 32u);
            Vehicle.Frame newer = vehicleData.GetFrameData(targetFrame - 16u);
            float interpolation = ((targetFrame & 15u)
                                   + simulationManager.m_referenceTimer) * 0.0625f;
            Bezier3 positionCurve = new Bezier3
            {
                a = older.m_position,
                b = older.m_position + older.m_velocity * 0.333f,
                c = newer.m_position - newer.m_velocity * 0.333f,
                d = newer.m_position
            };
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

            // VehicleAI.CalculateBodyMatrix applies the interpolated sway as
            // vertical displacement, nod and lean after the frame transform.
            // Preserve that actual body pose as well as the frame pose. The
            // difference is small for cars but conspicuous on motorcycles and
            // narrow personal EVs, which otherwise snap upright at handoff.
            Vector3 renderedPosition = positionCurve.Position(interpolation);
            renderedPosition.y += sway.y;
            Quaternion renderedRotation = Quaternion.Lerp(
                older.m_rotation,
                newer.m_rotation,
                interpolation);
            renderedRotation *= Quaternion.Euler(
                sway.z * Mathf.Rad2Deg,
                0f,
                -sway.x * Mathf.Rad2Deg);

            RoutedArrivalRenderCandidatePositions[vehicleId] = renderedPosition;
            RoutedArrivalRenderCandidateRotations[vehicleId] = renderedRotation;
            RoutedArrivalRenderCandidateValid[vehicleId] = true;
        }

        public static void RequestRoutedArrivalCommitAtTransfer(
            ushort vehicleId)
        {
            if (vehicleId == 0
                || RoutedArrivalAnimationStates[vehicleId]
                   != ArrivalStateControlledTraversal)
                return;

            RoutedArrivalAnimationStates[vehicleId] = ArrivalStateCommitRequested;
            SimulationManager simulationManager = SimulationManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            if (simulationManager == null || vehicleManager == null)
            {
                AbortRoutedArrivalAnimation(vehicleId);
                return;
            }

            simulationManager.AddAction(() =>
            {
                if (RoutedArrivalAnimationStates[vehicleId]
                    != ArrivalStateCommitRequested
                    || vehicleId >= vehicleManager.m_vehicles.m_size)
                    return;

                ref Vehicle vehicleData =
                    ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                PassengerCarAI ai = vehicleData.Info == null
                    ? null
                    : vehicleData.Info.m_vehicleAI as PassengerCarAI;
                if (ai == null
                    || (vehicleData.m_flags & Vehicle.Flags.Created) == 0)
                {
                    AbortRoutedArrivalAnimation(vehicleId);
                    return;
                }

                ExecuteRoutedArrival(
                    ai,
                    vehicleId,
                    ref vehicleData,
                    PendingRoutedArrivals[vehicleId]);
            });
        }

        public static int ConsumeRoutedArrivalAnimationSignal(
            ushort vehicleId,
            out ushort parkedId)
        {
            parkedId = 0;
            if (vehicleId == 0)
                return 1;

            byte state = RoutedArrivalAnimationStates[vehicleId];
            if (state == ArrivalStateControlledTraversal)
                return 2;
            if (state == ArrivalStateRetired)
            {
                parkedId = PendingRoutedArrivals[vehicleId].ParkedId;
                PendingRoutedArrivals[vehicleId] = default(RoutedArrivalState);
                RoutedArrivalHandoffPoseValid[vehicleId] = false;
                RoutedArrivalRenderCandidateValid[vehicleId] = false;
                RoutedArrivalUnspawnActionQueued[vehicleId] = false;
                RoutedArrivalVisualsSuppressed[vehicleId] = false;
                RoutedArrivalAnimationStates[vehicleId] = ArrivalStateNone;
                return 1;
            }
            if (state == ArrivalStateAnimationCancelled)
            {
                RoutedArrivalAnimationStates[vehicleId] = ArrivalStateNone;
                PendingRoutedArrivals[vehicleId] = default(RoutedArrivalState);
                RoutedArrivalHandoffPoseValid[vehicleId] = false;
                RoutedArrivalRenderCandidateValid[vehicleId] = false;
                RoutedArrivalUnspawnActionQueued[vehicleId] = false;
                return -1;
            }
            return 0;
        }

        public static bool TryGetRoutedArrivalHandoffPose(
            ushort vehicleId,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (vehicleId == 0 || !RoutedArrivalHandoffPoseValid[vehicleId])
                return false;

            position = RoutedArrivalHandoffPositions[vehicleId];
            rotation = RoutedArrivalHandoffRotations[vehicleId];
            return true;
        }

        public static void CompleteRoutedArrivalAnimation(ushort parkedId)
        {
            UndergroundParkingOccupancyManager.SetParkedCarVisualHeld(
                parkedId,
                false);
        }

        private static void RetireRoutedArrivalVehicle(ushort vehicleId)
        {
            if (vehicleId == 0
                || RoutedArrivalAnimationStates[vehicleId]
                   != ArrivalStateRetireRequested)
                return;

            VehicleManager vehicleManager = VehicleManager.instance;
            RoutedArrivalState state = PendingRoutedArrivals[vehicleId];
            bool retired = false;
            if (vehicleManager != null && vehicleId < vehicleManager.m_vehicles.m_size)
            {
                ref Vehicle vehicleData = ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                if ((vehicleData.m_flags & Vehicle.Flags.Created) == 0)
                {
                    retired = true;
                }
                else if (vehicleData.Info == state.Info)
                {
                    // External release requests remain blocked throughout the
                    // road queue and commit. Authorize exactly this transaction
                    // so UPG cannot suppress its own final native release and
                    // then expose a duplicate proxy over a still-created car.
                    RoutedArrivalReleaseAllowed[vehicleId] = true;
                    try
                    {
                        vehicleData.Unspawn(vehicleId);
                        vehicleManager.ReleaseVehicle(vehicleId);
                    }
                    finally
                    {
                        RoutedArrivalReleaseAllowed[vehicleId] = false;
                    }

                    retired = (vehicleManager.m_vehicles.m_buffer[vehicleId].m_flags
                               & Vehicle.Flags.Created) == 0;
                }
            }

            if (!retired)
            {
                // Never acknowledge or expose the proxy while a created real
                // car still owns this ID. The committed parked body is valid,
                // so publish it and cancel only the presentation.
                CompleteRoutedArrivalAnimation(state.ParkedId);
                RoutedArrivalAnimationStates[vehicleId] =
                    ArrivalStateAnimationCancelled;
                UndergroundParkingLog.Error(
                    "UPG refused arrival proxy exposure because the exact real car was not retired: vehicle="
                    + vehicleId
                    + " facility="
                    + state.FacilityId);
                return;
            }

            // This is the sole render-thread acknowledgement: the exact real
            // vehicle is no longer created. Keep the proxy hidden until the
            // animation driver consumes it.
            RoutedArrivalAnimationStates[vehicleId] = ArrivalStateRetired;
        }

        public static void AbortRoutedArrivalAnimation(ushort vehicleId)
        {
            if (vehicleId == 0)
                return;

            byte state = RoutedArrivalAnimationStates[vehicleId];
            ushort parkedId = PendingRoutedArrivals[vehicleId].ParkedId;
            if (state == ArrivalStateCommittedOffCamera)
            {
                CompleteRoutedArrivalAnimation(parkedId);
                RoutedArrivalAnimationStates[vehicleId] = ArrivalStateRetireRequested;
                RetireRoutedArrivalVehicle(vehicleId);
                return;
            }
            if (state == ArrivalStateRetireRequested
                || state == ArrivalStateRetired)
            {
                CompleteRoutedArrivalAnimation(parkedId);
                return;
            }

            if (state == ArrivalStateControlledTraversal
                || state == ArrivalStateControlledUnspawnRequested
                || (state == ArrivalStateCommitRequested
                    && UndergroundParkingEntryRouteManager
                        .IsTmpeAdoptedArrival(vehicleId)
                    && !IsArrivalPavementHandoffStarted(vehicleId)))
            {
                RestoreControlledTraversalVehicle(vehicleId);
                return;
            }

            CancelPendingRoutedArrival(vehicleId, false);
            UndergroundParkingEntryRouteManager.FailArrival(
                vehicleId, "arrival-animation-unavailable");
        }

        private static void RestoreControlledTraversalVehicle(
            ushort vehicleId)
        {
            SimulationManager simulationManager = SimulationManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            RoutedArrivalState state = PendingRoutedArrivals[vehicleId];
            if (simulationManager == null
                || vehicleManager == null
                || !state.IsPending)
            {
                CancelPendingRoutedArrival(vehicleId, false);
                return;
            }

            simulationManager.AddAction(() =>
            {
                if (vehicleId >= vehicleManager.m_vehicles.m_size)
                    return;
                ref Vehicle vehicleData =
                    ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                if ((vehicleData.m_flags & Vehicle.Flags.Created) == 0
                    || vehicleData.Info != state.Info)
                    return;

                Vehicle.Frame frame = vehicleData.GetLastFrameData();
                if (!UndergroundParkingAccessManager.IsFinite(
                        state.SurfacePosition)
                    || !UndergroundParkingAccessManager.IsFinite(
                        state.SurfaceRotation))
                {
                    UndergroundParkingLog.Error(
                        "UPG refused invalid routed-arrival rollback pose: vehicle="
                        + vehicleId
                        + " facility="
                        + state.FacilityId);
                    return;
                }
                frame.m_position = state.SurfacePosition;
                frame.m_rotation = state.SurfaceRotation;
                frame.m_velocity = Vector3.zero;
                vehicleData.m_frame0 = frame;
                vehicleData.m_frame1 = frame;
                vehicleData.m_frame2 = frame;
                vehicleData.m_frame3 = frame;
                if ((vehicleData.m_flags & Vehicle.Flags.Spawned) == 0)
                    vehicleData.Spawn(vehicleId);
                RoutedArrivalAnimationStates[vehicleId] =
                    ArrivalStateWaitingAtRoadStop;
                EnqueueRoutedArrivalRoadStop(state);
            });
        }

        private static void RestoreControlledTraversalsOnShutdown()
        {
            SimulationManager simulationManager = SimulationManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            if (simulationManager == null || vehicleManager == null)
                return;

            for (int i = 1; i < RoutedArrivalAnimationStates.Length; i++)
            {
                byte animationState = RoutedArrivalAnimationStates[i];
                if (animationState != ArrivalStateControlledTraversal
                    && animationState
                       != ArrivalStateControlledUnspawnRequested)
                    continue;

                ushort vehicleId = (ushort)i;
                RoutedArrivalState arrival = PendingRoutedArrivals[vehicleId];
                if (!arrival.IsPending)
                    continue;
                simulationManager.AddAction(() =>
                {
                    if (vehicleId >= vehicleManager.m_vehicles.m_size)
                        return;
                    ref Vehicle vehicleData =
                        ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                    if ((vehicleData.m_flags & Vehicle.Flags.Created) == 0
                        || vehicleData.Info != arrival.Info)
                        return;
                    Vehicle.Frame frame = vehicleData.GetLastFrameData();
                    if (!UndergroundParkingAccessManager.IsFinite(
                            arrival.SurfacePosition)
                        || !UndergroundParkingAccessManager.IsFinite(
                            arrival.SurfaceRotation))
                    {
                        UndergroundParkingLog.Error(
                            "UPG refused invalid routed-arrival shutdown pose: vehicle="
                            + vehicleId
                            + " facility="
                            + arrival.FacilityId);
                        return;
                    }
                    frame.m_position = arrival.SurfacePosition;
                    frame.m_rotation = arrival.SurfaceRotation;
                    frame.m_velocity = Vector3.zero;
                    vehicleData.m_frame0 = frame;
                    vehicleData.m_frame1 = frame;
                    vehicleData.m_frame2 = frame;
                    vehicleData.m_frame3 = frame;
                    if ((vehicleData.m_flags & Vehicle.Flags.Spawned) == 0)
                        vehicleData.Spawn(vehicleId);
                });
            }
        }

        private static bool PassengerCarUpdateParkedVehiclePrefix(
            ushort parkedID,
            ref VehicleParked parkedData)
        {
            ushort pendingOwner = parkedID == 0
                ? (ushort)0
                : PendingTmpeParkedVehicleOwners[parkedID];
            if (pendingOwner != 0)
            {
                if (UndergroundParkingEntryRouteManager
                        .IsTmpeAdoptedArrival(pendingOwner))
                {
                    // This is TM:PE's early identity, not a completed parking
                    // event. Keep it inert and below ground until the same road
                    // vehicle wins the portal FIFO and commits atomically.
                    return false;
                }

                // A stale binding must never mask an unrelated recycled parked
                // ID after its owning transaction has gone away.
                PendingTmpeParkedVehicleOwners[parkedID] = 0;
                if (PendingTmpeParkedVehiclesByVehicle[pendingOwner] == parkedID)
                    PendingTmpeParkedVehiclesByVehicle[pendingOwner] = 0;
                PendingTmpeOwnerCitizensByVehicle[pendingOwner] = 0;
            }

            if (UndergroundParkingOccupancyManager
                    .IsPendingVanillaRelease(parkedID))
            {
                // The zero-facility release ledger remains the sole lifecycle
                // owner even after another garage activates the patch set.
                // Suppress both native publication and geometry-based UPG
                // re-adoption until that exact ledger token resolves.
                return false;
            }

            int facilityId;
            int slotIndex;
            if (!UndergroundParkingOccupancyManager.TryPreserveManagedParkedVehicle(
                    parkedID,
                    ref parkedData,
                    out facilityId,
                    out slotIndex))
            {
                return true;
            }

            UndergroundParkingOccupancyManager.LogPreserved(
                parkedID,
                facilityId,
                slotIndex,
                parkedData.m_position);
            return false;
        }

        private static void VehicleManagerCreateVehiclePrefix(
            VehicleInfo info,
            ref Vector3 position,
            out ManagedDepartureState __state)
        {
            __state = default(ManagedDepartureState);
            PassengerCarAI passengerCarAI = info == null ? null : info.m_vehicleAI as PassengerCarAI;
            if (passengerCarAI == null)
                return;

            if (!_managedDepartureContext.IsManaged)
                return;

            UndergroundParkingFacility facility = _managedDepartureContext.Facility;
            UndergroundParkingRoadConnection connection = _managedDepartureContext.Connection;
            ushort parkedId = _managedDepartureContext.ParkedId;

            Vector3 livePosition;
            Vector3 liveDirection;
            if (!UndergroundParkingAccessManager.TryGetLiveLanePose(
                    connection,
                    out livePosition,
                    out liveDirection))
            {
                UndergroundParkingLog.Error(
                    "UPG refused managed departure creation because its live road pose was unavailable: parked="
                    + parkedId
                    + " facility="
                    + facility.Id);
                return;
            }

            connection.LanePosition = livePosition;
            connection.LaneDirection = liveDirection;
            position = livePosition;
            __state = new ManagedDepartureState(
                info,
                facility,
                connection,
                parkedId);
        }

        private static void BulldozeToolOnToolUpdatePostfix(
            BulldozeTool __instance,
            InstanceID ___m_hoverInstance)
        {
            ushort building = ___m_hoverInstance.Building;
            UndergroundParkingFacility facility;
            if (!UndergroundParkingRegistry.TryGetForBuilding(building, out facility)
                || ShowToolInfo == null)
                return;

            int parked = UndergroundParkingOccupancyManager.CountAssignedParkedCars(facility);
            if (parked <= 0)
                return;

            BuildingManager manager = BuildingManager.instance;
            if (manager == null || building >= manager.m_buildings.m_size)
                return;

            string message = "Cannot demolish: "
                             + parked
                             + (parked == 1 ? " vehicle is" : " vehicles are")
                             + " still parked here. Switch the car park off and allow "
                             + (parked == 1 ? "it" : "them")
                             + " to leave first.";
            ShowToolInfo(__instance, true, message, manager.m_buildings.m_buffer[building].m_position);
            if (_blockedDemolitionLogCount < 8)
            {
                _blockedDemolitionLogCount++;
                UndergroundParkingLog.Advanced("UPG blocked demolition of occupied entrance: facility="
                                            + facility.Id
                                            + " building="
                                            + building
                                            + " parked="
                                            + UndergroundParkingOccupancyManager.CountAssignedParkedCars(facility));
            }
        }

        private static ShowToolInfoDelegate CreateShowToolInfoDelegate()
        {
            MethodInfo method = typeof(ToolBase).GetMethod(
                "ShowToolInfo",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool), typeof(string), typeof(Vector3) },
                null);
            return method == null
                ? null
                : (ShowToolInfoDelegate)Delegate.CreateDelegate(
                    typeof(ShowToolInfoDelegate), null, method);
        }

        private static void CitizenStartPathFindPrefix(
            ref CitizenInstance citizenData,
            out ManagedRetrievalState __state)
        {
            __state = default(ManagedRetrievalState);
            ushort parkedId = GetCitizenParkedVehicle(ref citizenData);
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            Vector3 retrievalPosition;
            uint pedestrianLaneId;
            Vector3 originalPosition;
            Quaternion originalRotation;
            if (!UndergroundParkingOccupancyManager.TryVirtualizeManagedVehicleAtPortal(
                    parkedId,
                    out facility,
                    out connection,
                    out retrievalPosition,
                    out pedestrianLaneId,
                    out originalPosition,
                    out originalRotation))
            {
                return;
            }

            __state = new ManagedRetrievalState(parkedId, originalPosition, originalRotation);
            UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                citizenData.m_citizen,
                "retrieval-path-target-virtualized",
                0,
                parkedId,
                facility.Id,
                "pedestrianLane=" + pedestrianLaneId
                + " target=" + UndergroundParkingLifecycleDiagnostics.FormatPosition(retrievalPosition));
            if (_managedRetrievalLogCount < ManagedRetrievalLogLimit)
            {
                _managedRetrievalLogCount++;
                UndergroundParkingLog.Advanced(
                    "UPG managed owner retrieval targeted entrance: parked=" + parkedId
                    + " facility=" + facility.Id
                    + " target=pavement"
                    + " pedestrianLane=" + pedestrianLaneId
                    + " position=("
                    + retrievalPosition.x.ToString("0.0") + ","
                    + retrievalPosition.y.ToString("0.0") + ","
                    + retrievalPosition.z.ToString("0.0") + ")");
            }
        }

        private static Exception ManagedRetrievalFinalizer(
            Exception __exception,
            ManagedRetrievalState __state)
        {
            RestoreManagedRetrieval(__state);
            return __exception;
        }

        private static void OwnerSpawnVehiclePrefix(
            ushort instanceID,
            ref CitizenInstance citizenData,
            out ManagedRetrievalState __state)
        {
            __state = default(ManagedRetrievalState);
            _pendingOwnerDeparture = default(ManagedDepartureState);
            ushort parkedId = GetCitizenParkedVehicle(ref citizenData);
            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            Vector3 retrievalPosition;
            uint pedestrianLaneId;
            Vector3 originalPosition;
            Quaternion originalRotation;
            if (!UndergroundParkingOccupancyManager.TryVirtualizeManagedVehicleAtPortal(
                    parkedId,
                    out facility,
                    out connection,
                    out retrievalPosition,
                    out pedestrianLaneId,
                    out originalPosition,
                    out originalRotation))
            {
                return;
            }

            __state = new ManagedRetrievalState(parkedId, originalPosition, originalRotation);
            _managedDepartureContext = new ManagedDepartureContext(
                parkedId,
                facility,
                connection);
            UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                citizenData.m_citizen,
                "retrieval-real-vehicle-spawn-requested",
                0,
                parkedId,
                facility.Id,
                "pedestrianLane=" + pedestrianLaneId
                + " target=" + UndergroundParkingLifecycleDiagnostics.FormatPosition(retrievalPosition));
            if (_managedSpawnLogCount < ManagedRetrievalLogLimit)
            {
                _managedSpawnLogCount++;
                UndergroundParkingLog.Advanced(
                    "UPG managed owner vehicle spawn staged: parked=" + parkedId
                    + " facility=" + facility.Id
                    + " target=pavement"
                    + " pedestrianLane=" + pedestrianLaneId
                    + " position=("
                    + retrievalPosition.x.ToString("0.0") + ","
                    + retrievalPosition.y.ToString("0.0") + ","
                    + retrievalPosition.z.ToString("0.0") + ")");
            }
        }

        private static void TmpeEnterParkedCarPrefix(
            ushort instanceId,
            ref CitizenInstance instanceData,
            ushort parkedVehicleId,
            out ManagedRetrievalState __state)
        {
            __state = default(ManagedRetrievalState);
            _pendingOwnerDeparture = default(ManagedDepartureState);
            _managedDepartureContext = default(ManagedDepartureContext);

            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            Vector3 retrievalPosition;
            uint pedestrianLaneId;
            Vector3 originalPosition;
            Quaternion originalRotation;
            if (!UndergroundParkingOccupancyManager.TryVirtualizeManagedVehicleAtPortal(
                    parkedVehicleId,
                    out facility,
                    out connection,
                    out retrievalPosition,
                    out pedestrianLaneId,
                    out originalPosition,
                    out originalRotation))
            {
                return;
            }

            __state = new ManagedRetrievalState(
                parkedVehicleId,
                originalPosition,
                originalRotation);
            _managedDepartureContext = new ManagedDepartureContext(
                parkedVehicleId,
                facility,
                connection);
            UndergroundParkingLifecycleDiagnostics.TraceCitizen(
                instanceData.m_citizen,
                "retrieval-tmpe-enter-parked-car",
                0,
                parkedVehicleId,
                facility.Id,
                "instance=" + instanceId
                + " pedestrianLane=" + pedestrianLaneId
                + " target="
                + UndergroundParkingLifecycleDiagnostics.FormatPosition(retrievalPosition));
            if (_tmpeManagedRetrievalLogCount < ManagedRetrievalLogLimit)
            {
                _tmpeManagedRetrievalLogCount++;
                UndergroundParkingLog.Advanced(
                    "UPG TM:PE exact parked-car transition staged: parked="
                    + parkedVehicleId
                    + " facility="
                    + facility.Id
                    + " instance="
                    + instanceId
                    + " pedestrianLane="
                    + pedestrianLaneId
                    + " position="
                    + UndergroundParkingLifecycleDiagnostics.FormatPosition(retrievalPosition));
            }

        }

        private static void OwnerSpawnVehiclePostfix(bool __result)
        {
            ManagedDepartureState departure = _pendingOwnerDeparture;
            _pendingOwnerDeparture = default(ManagedDepartureState);
            if (!__result || !departure.IsManaged)
                return;

            VehicleManager vehicleManager = VehicleManager.instance;
            ushort vehicleId = departure.VehicleId;
            if (vehicleManager == null
                || vehicleId == 0
                || vehicleId >= vehicleManager.m_vehicles.m_size)
            {
                return;
            }

            ref Vehicle vehicleData = ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            if ((vehicleData.m_flags & Vehicle.Flags.Created) == 0
                || departure.Info == null
                || vehicleData.Info != departure.Info)
                return;

            uint releaseLane;
            Vector3 releasePosition;
            Vector3 releaseDirection;
            if (!UndergroundParkingAccessManager.TryGetLiveLanePose(
                    departure.Connection,
                    out releaseLane,
                    out releasePosition,
                    out releaseDirection))
            {
                // The prefix already supplied a validated native creation
                // coordinate. If the road changes before the enclosing owner
                // transaction completes, keep that exact spawned vehicle and
                // abandon only UPG's optional hidden departure presentation.
                UndergroundParkingOccupancyManager.CommitManagedDeparture(
                    departure.ParkedId);
                UndergroundParkingLog.Error(
                    "UPG kept managed departure in its native spawned state because its release lane changed during creation: vehicle="
                    + vehicleId
                    + " facility="
                    + departure.Facility.Id);
                return;
            }

            UndergroundParkingRoadConnection validatedConnection =
                departure.Connection;
            validatedConnection.LanePosition = releasePosition;
            validatedConnection.LaneDirection = releaseDirection;

            int parkedFacilityId;
            int parkedSlotIndex;
            bool hasGarageSlot =
                UndergroundParkingOccupancyManager.TryGetManagedParkedSlot(
                    departure.ParkedId,
                    out parkedFacilityId,
                    out parkedSlotIndex)
                && parkedFacilityId == departure.Facility.Id;

            // Do not remove the underground assignment in the nested
            // VehicleManager.CreateVehicle postfix. The enclosing vanilla owner
            // spawn is the authoritative transaction: only its successful result
            // proves that the initialized replacement car was adopted.
            UndergroundParkingOccupancyManager.CommitManagedDeparture(
                departure.ParkedId);

            // Vanilla has now transferred the citizen path/owner and completed
            // TrySpawn. Every managed departure must now use the same physical
            // presentation, even when the garage was outside the camera when
            // vanilla requested the car: hide that fully initialized real
            // vehicle before render, then release the same ID only after the
            // proxy has emerged from underground and reached the road.
            if ((vehicleData.m_flags & Vehicle.Flags.Spawned) != 0)
                vehicleData.Unspawn(vehicleId);

            VehicleInfo departureInfo = vehicleData.Info;
            ManagedDepartureSpawnStates[vehicleId] = 1;
            ManagedDepartureExpectedInfos[vehicleId] = departureInfo;
            ManagedDepartureExpectedOwners[vehicleId] = vehicleData.m_transferSize;
            ManagedDepartureReleaseFacilities[vehicleId] = departure.Facility.Id;
            ManagedDepartureFacilitySnapshots[vehicleId] = departure.Facility;
            ManagedDepartureConnectionSnapshots[vehicleId] = validatedConnection;
            ManagedDepartureGarageSlots[vehicleId] = hasGarageSlot
                ? parkedSlotIndex + 1
                : 0;
            ManagedDepartureReleasePositions[vehicleId] = releasePosition;
            ManagedDepartureReleaseRotations[vehicleId] =
                Quaternion.LookRotation(releaseDirection, Vector3.up);
            ManagedDepartureReleaseLanes[vehicleId] = releaseLane;
            ManagedDepartureReleaseWaitLogged[vehicleId] = false;
            ManagedDepartureAnimationQueued[vehicleId] = false;
            TrackManagedDepartureStaging(vehicleId);
        }

        private static Exception OwnerSpawnVehicleFinalizer(
            Exception __exception,
            ManagedRetrievalState __state)
        {
            _pendingOwnerDeparture = default(ManagedDepartureState);
            _managedDepartureContext = default(ManagedDepartureContext);
            RestoreManagedRetrieval(__state);
            return __exception;
        }

        private static void RestoreManagedRetrieval(ManagedRetrievalState state)
        {
            if (!state.IsManaged)
                return;

            UndergroundParkingOccupancyManager.RestoreManagedVehicleAfterPortalVirtualization(
                state.ParkedId,
                state.OriginalPosition,
                state.OriginalRotation);
        }

        private static ushort GetCitizenParkedVehicle(ref CitizenInstance citizenData)
        {
            CitizenManager citizenManager = CitizenManager.instance;
            uint citizenId = citizenData.m_citizen;
            if (citizenManager == null
                || citizenId == 0
                || citizenId >= citizenManager.m_citizens.m_size)
            {
                return 0;
            }

            return citizenManager.m_citizens.m_buffer[citizenId].m_parkedVehicle;
        }

        private static void VehicleManagerCreateVehiclePostfix(
            ref ushort vehicle,
            bool __result,
            ManagedDepartureState __state)
        {
            if (!__result || vehicle == 0 || !__state.IsManaged)
                return;

            UndergroundParkingLifecycleDiagnostics.LinkDepartureVehicle(
                __state.ParkedId,
                vehicle,
                __state.Facility.Id,
                "retrieval-real-vehicle-created");
            _pendingOwnerDeparture = __state.WithVehicle(vehicle);
        }

        private static bool VehicleManagerReleaseVehiclePrefix(ushort vehicle)
        {
            if (vehicle != 0 && RoutedArrivalReleaseAllowed[vehicle])
            {
                ClearAdoptedRoutedArrivalReleaseGuard(vehicle);
                return true;
            }

            if (vehicle != 0)
            {
                byte arrivalState = RoutedArrivalAnimationStates[vehicle];
                if (IsAdoptedRoutedArrivalReleaseProtected(vehicle)
                    || UndergroundParkingEntryRouteManager
                        .IsTmpeAdoptedArrival(vehicle)
                    || arrivalState == ArrivalStateCommittedOffCamera
                    || arrivalState == ArrivalStateRetireRequested
                    || IsPendingRoutedArrivalRoadVehicle(vehicle)
                    || (arrivalState == ArrivalStateCommitRequested
                        && IsArrivalPavementHandoffStarted(vehicle)))
                    return false;

                TmpeParkingCompatibilityManager.ReleaseVehicle(vehicle);
            }

            if (vehicle == 0 || ManagedDepartureExpectedInfos[vehicle] == null)
                return true;

            if (_heldReleaseLogCount < ManagedRetrievalLogLimit)
            {
                _heldReleaseLogCount++;
                UndergroundParkingLog.Advanced(
                    "UPG held managed departure through premature release request: vehicle=" + vehicle);
            }
            return false;
        }

        private static void ProtectAdoptedRoutedArrival(
            ushort vehicleId,
            ref Vehicle vehicleData)
        {
            if (vehicleId == 0
                || (vehicleData.m_flags & Vehicle.Flags.Created) == 0
                || vehicleData.Info == null
                || vehicleData.m_citizenUnits == 0u
                || !UndergroundParkingEntryRouteManager.HasActiveRoute(vehicleId))
                return;

            AdoptedRoutedArrivalReleaseGuards[vehicleId] = true;
            AdoptedRoutedArrivalInfos[vehicleId] = vehicleData.Info;
            AdoptedRoutedArrivalCitizenUnits[vehicleId] = vehicleData.m_citizenUnits;
        }

        internal static bool IsAdoptedRoutedArrivalReleaseProtected(
            ushort vehicleId)
        {
            if (vehicleId == 0 || !AdoptedRoutedArrivalReleaseGuards[vehicleId])
                return false;

            VehicleManager manager = VehicleManager.instance;
            if (manager == null || vehicleId >= manager.m_vehicles.m_size)
                return true;

            ref Vehicle vehicleData = ref manager.m_vehicles.m_buffer[vehicleId];
            if ((vehicleData.m_flags & Vehicle.Flags.Created) != 0
                && vehicleData.Info == AdoptedRoutedArrivalInfos[vehicleId]
                && vehicleData.m_citizenUnits
                   == AdoptedRoutedArrivalCitizenUnits[vehicleId])
                return true;

            ClearAdoptedRoutedArrivalReleaseGuard(vehicleId);
            return false;
        }

        private static void ClearAdoptedRoutedArrivalReleaseGuard(
            ushort vehicleId)
        {
            if (vehicleId == 0)
                return;

            AdoptedRoutedArrivalReleaseGuards[vehicleId] = false;
            AdoptedRoutedArrivalInfos[vehicleId] = null;
            AdoptedRoutedArrivalCitizenUnits[vehicleId] = 0u;
        }

        private static void PassengerCarGetColorPostfix(
            ushort vehicleID,
            ref Vehicle data,
            InfoManager.InfoMode infoMode,
            ref Color __result)
        {
            if (infoMode != InfoManager.InfoMode.Transport
                || !UndergroundParkingEntryRouteManager.IsPublishedHighlight(
                    vehicleID,
                    ref data))
            {
                return;
            }

            // Match buses and other line vehicles: supply the body colour to
            // vanilla's normal vehicle render call so full-detail and shared
            // LOD batches use one stable source at every camera height.
            __result = new Color32(0, 102, 178, 255);
        }

        public static void CompleteManagedDepartureAnimation(ushort vehicleId)
        {
            if (vehicleId == 0
                || ManagedDepartureExpectedInfos[vehicleId] == null
                || ManagedDepartureSpawnStates[vehicleId] == 2)
                return;

            ManagedDepartureSpawnStates[vehicleId] = 2;
            VehicleInfo expectedInfo = ManagedDepartureExpectedInfos[vehicleId];
            ushort expectedOwner = ManagedDepartureExpectedOwners[vehicleId];
            QueueManagedDepartureRelease(vehicleId, expectedInfo, expectedOwner);
        }

        public static void RestartManagedDepartureAnimation(ushort vehicleId)
        {
            if (vehicleId == 0
                || ManagedDepartureExpectedInfos[vehicleId] == null
                || ManagedDepartureSpawnStates[vehicleId] != 1
                || !IsManagedDepartureVehicleHeld(vehicleId))
                return;

            ManagedDepartureAnimationQueued[vehicleId] = false;
            TrackManagedDepartureStaging(vehicleId);
            UndergroundParkingLog.Advanced(
                "UPG managed departure presentation restarted for relocated entrance: vehicle="
                + vehicleId
                + " facility="
                + ManagedDepartureReleaseFacilities[vehicleId]);
        }

        public static bool IsManagedDepartureReleasePending(ushort vehicleId)
        {
            return vehicleId != 0
                   && ManagedDepartureExpectedInfos[vehicleId] != null;
        }

        private static bool HasManagedDepartureLaneSpace(
            ushort vehicleId,
            VehicleInfo info,
            float extraClearance = 0f)
        {
            float vehicleLength = info == null || info.m_generatedInfo == null
                ? 8f
                : Mathf.Max(5f, info.m_generatedInfo.m_size.z + 2f);
            vehicleLength += Mathf.Max(0f, extraClearance);
            uint laneId = ManagedDepartureReleaseLanes[vehicleId];
            NetManager netManager = NetManager.instance;
            return laneId != 0u
                   && netManager != null
                   && laneId < netManager.m_lanes.m_size
                   && netManager.m_lanes.m_buffer[laneId].CheckSpace(
                       vehicleLength,
                       vehicleId);
        }

        private static void TrackManagedDepartureStaging(ushort vehicleId)
        {
            if (vehicleId == 0 || ManagedDepartureStagingTracked[vehicleId])
                return;

            ManagedDepartureStagingTracked[vehicleId] = true;
            ManagedDepartureStagingVehicles.Add(vehicleId);
            if (_departureStagingScheduleLogCount++ < ManagedRetrievalLogLimit)
            {
                UndergroundParkingLog.Advanced(
                    "UPG managed departure staged after authoritative owner transition: vehicle="
                    + vehicleId
                    + " facility="
                    + ManagedDepartureReleaseFacilities[vehicleId]
                    + " polling=bounded-simulation-update"
                    + " checksPerUpdate="
                    + ManagedDepartureChecksPerUpdate
                    + " ownerAuthority=vanilla-spawn-complete"
                    + " pedestrianRepoll=False");
            }
        }

        private static void UpdateManagedDepartureStaging()
        {
            int checkedCount = 0;
            while (checkedCount++ < ManagedDepartureChecksPerUpdate
                   && ManagedDepartureStagingVehicles.Count > 0)
            {
                if (_managedDepartureStagingCursor >= ManagedDepartureStagingVehicles.Count)
                    _managedDepartureStagingCursor = 0;

                ushort vehicleId = ManagedDepartureStagingVehicles[
                    _managedDepartureStagingCursor];
                if (vehicleId == 0
                    || ManagedDepartureSpawnStates[vehicleId] == 0)
                {
                    RemoveManagedDepartureStagingAt(_managedDepartureStagingCursor);
                    continue;
                }

                if (!IsManagedDepartureVehicleHeld(vehicleId))
                {
                    RemoveManagedDepartureStagingAt(_managedDepartureStagingCursor);
                    ClearManagedDepartureTicket(vehicleId);
                    continue;
                }

                if (ManagedDepartureSpawnStates[vehicleId] == 2)
                {
                    VehicleInfo releaseInfo = ManagedDepartureExpectedInfos[vehicleId];
                    ushort releaseOwner = ManagedDepartureExpectedOwners[vehicleId];
                    RemoveManagedDepartureStagingAt(_managedDepartureStagingCursor);
                    QueueManagedDepartureRelease(vehicleId, releaseInfo, releaseOwner);
                    continue;
                }

                if (ManagedDepartureSpawnStates[vehicleId] != 1
                    || ManagedDepartureAnimationQueued[vehicleId])
                {
                    RemoveManagedDepartureStagingAt(_managedDepartureStagingCursor);
                    continue;
                }

                // ResidentAI/TouristAI.SpawnVehicle has already completed the
                // one native ownership transaction, transferred the citizen's
                // path to this exact initialized car and consumed the pedestrian
                // instance. That successful completion is the authoritative
                // walking-to-driving transition. Re-polling the former citizen
                // instance here can never become a later approach signal and
                // stranded 193 of 207 observed departures. From this point only
                // portal and road-lane admission may delay presentation.

                VehicleInfo info = ManagedDepartureExpectedInfos[vehicleId];
                UndergroundParkingFacility facility;
                UndergroundParkingRoadConnection connection;
                if (info == null)
                {
                    RemoveManagedDepartureStagingAt(_managedDepartureStagingCursor);
                    ClearManagedDepartureTicket(vehicleId);
                    continue;
                }

                if (!TryRefreshManagedDepartureConnection(vehicleId, out facility, out connection))
                {
                    _managedDepartureStagingCursor++;
                    continue;
                }

                // Wait before taking the facility's visual lock. The extra
                // approach clearance reduces the chance that ordinary traffic
                // enters the handoff while the reverse surface leg is running.
                // The exact footprint is still checked again immediately before
                // the real vehicle is spawned.
                if (!HasManagedDepartureLaneSpace(vehicleId, info, 24f))
                {
                    if (!ManagedDepartureReleaseWaitLogged[vehicleId]
                        && _departureLaneWaitLogCount++ < 24)
                    {
                        ManagedDepartureReleaseWaitLogged[vehicleId] = true;
                        UndergroundParkingLog.Advanced(
                            "UPG departure animation held before portal until buffered lane admission: vehicle="
                            + vehicleId
                            + " facility="
                            + facility.Id);
                    }
                    _managedDepartureStagingCursor++;
                    continue;
                }

                if (!UndergroundParkingOccupancyManager.SupportsAutomatedTunnel(facility))
                {
                    // This facility deliberately owns no tunnel presentation.
                    // Vanilla's already initialized exact departure vehicle is
                    // released through the validated road ticket without a
                    // fabricated underground or portal animation.
                    VehicleInfo releaseInfo = ManagedDepartureExpectedInfos[vehicleId];
                    ushort releaseOwner = ManagedDepartureExpectedOwners[vehicleId];
                    ManagedDepartureSpawnStates[vehicleId] = 2;
                    RemoveManagedDepartureStagingAt(_managedDepartureStagingCursor);
                    QueueManagedDepartureRelease(vehicleId, releaseInfo, releaseOwner);
                    UndergroundParkingLog.Advanced(
                        "UPG departure tunnel automation omitted for infeasible footprint: vehicle="
                        + vehicleId
                        + " facility="
                        + facility.Id
                        + " fallback=validated-road-release");
                    continue;
                }

                Color color = Color.white;
                VehicleManager vehicleManager = VehicleManager.instance;
                if (vehicleManager != null
                    && vehicleId < vehicleManager.m_vehicles.m_size)
                {
                    ref Vehicle vehicleData =
                        ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                    VehicleAI vehicleAI = info.m_vehicleAI;
                    if (vehicleAI != null)
                    {
                        color = vehicleAI.GetColor(
                            vehicleId,
                            ref vehicleData,
                            InfoManager.InfoMode.None,
                            InfoManager.SubInfoMode.Default);
                    }
                }

                ManagedDepartureReleaseWaitLogged[vehicleId] = false;
                ManagedDepartureAnimationQueued[vehicleId] = true;
                RemoveManagedDepartureStagingAt(_managedDepartureStagingCursor);
                int encodedGarageSlot = ManagedDepartureGarageSlots[vehicleId];
                bool queued = encodedGarageSlot > 0
                    && UndergroundParkingVisualManager.TryStartInternalDepartureJourney(
                        info,
                        facility,
                        connection,
                        encodedGarageSlot - 1,
                        vehicleId,
                        color);
                if (!queued)
                {
                    queued = UndergroundParkingPortalAnimationManager.QueueDeparture(
                        info,
                        facility,
                        connection,
                        vehicleId,
                        color);
                }
                if (!queued)
                {
                    ManagedDepartureAnimationQueued[vehicleId] = false;
                    TrackManagedDepartureStaging(vehicleId);
                }
            }
        }

        private static bool TryRefreshManagedDepartureConnection(
            ushort vehicleId,
            out UndergroundParkingFacility facility,
            out UndergroundParkingRoadConnection connection)
        {
            facility = UndergroundParkingFacility.None;
            connection = default(UndergroundParkingRoadConnection);
            int facilityId = ManagedDepartureReleaseFacilities[vehicleId];
            if (!UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                    facilityId,
                    out facility,
                    out connection))
            {
                facility = ManagedDepartureFacilitySnapshots[vehicleId];
                connection = ManagedDepartureConnectionSnapshots[vehicleId];
            }

            UndergroundParkingRoadConnection entranceConnection = connection;
            UndergroundParkingRoadConnection releaseConnection;
            uint laneId;
            if (facility.IsValid
                && entranceConnection.IsValid
                && TryResolveManagedDepartureRoadPose(
                    vehicleId,
                    facility,
                    entranceConnection,
                    out releaseConnection,
                    out laneId))
            {
                connection = releaseConnection;
                UpdateManagedDepartureReleaseConnection(
                    vehicleId,
                    facility,
                    entranceConnection,
                    releaseConnection,
                    laneId);
                return true;
            }

            // A road upgrade can replace the saved segment and every lane ID
            // while leaving the approved entrance point beside a valid road.
            // Resolve that current vanilla road from the immutable entrance
            // snapshot; do not retain or release the car against a deleted lane.
            UndergroundParkingFacility snapshot =
                ManagedDepartureFacilitySnapshots[vehicleId];
            UndergroundParkingFacility roadDraft;
            string message;
            if (!snapshot.IsValid
                || !UndergroundParkingGeometry.TryCreateFacilityFromTerrainPosition(
                    snapshot.EntrancePosition,
                    out roadDraft,
                    out message)
                || !UndergroundParkingAccessManager.TryGetRoadConnection(
                    roadDraft,
                    out entranceConnection,
                    out message))
            {
                return false;
            }

            facility = snapshot;
            entranceConnection.FacilityId = facilityId;
            if (!TryResolveManagedDepartureRoadPose(
                    vehicleId,
                    facility,
                    entranceConnection,
                    out releaseConnection,
                    out laneId))
                return false;
            connection = releaseConnection;
            UpdateManagedDepartureReleaseConnection(
                vehicleId,
                facility,
                entranceConnection,
                releaseConnection,
                laneId);
            return true;
        }

        private static bool TryResolveManagedDepartureRoadPose(
            ushort vehicleId,
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection entranceConnection,
            out UndergroundParkingRoadConnection releaseConnection,
            out uint laneId)
        {
            releaseConnection = default(UndergroundParkingRoadConnection);
            laneId = 0u;
            VehicleInfo info = ManagedDepartureExpectedInfos[vehicleId];
            Vector3 entrancePosition;
            Vector3 entranceDirection;
            uint entranceLaneId;
            if (info == null
                || !UndergroundParkingAccessManager.TryGetLiveLanePose(
                    entranceConnection,
                    out entranceLaneId,
                    out entrancePosition,
                    out entranceDirection))
                return false;

            entranceConnection.LanePosition = entrancePosition;
            entranceConnection.LaneDirection = entranceDirection;
            if (!UndergroundParkingAccessManager.TryGetDepartureConnectionAfterEntrance(
                    facility,
                    entranceConnection.LaneIndex,
                    entranceConnection,
                    TmpeParkingCompatibilityManager.GetEntranceHandoffDistance(info),
                    entranceDirection,
                    out releaseConnection))
                return false;

            Vector3 releasePosition;
            Vector3 releaseDirection;
            if (!UndergroundParkingAccessManager.TryGetLiveLanePose(
                    releaseConnection,
                    out laneId,
                    out releasePosition,
                    out releaseDirection))
                return false;
            releaseConnection.LanePosition = releasePosition;
            releaseConnection.LaneDirection = releaseDirection;
            return true;
        }

        private static void UpdateManagedDepartureReleaseConnection(
            ushort vehicleId,
            UndergroundParkingFacility facility,
            UndergroundParkingRoadConnection entranceConnection,
            UndergroundParkingRoadConnection releaseConnection,
            uint laneId)
        {
            Vector3 releaseDirection = releaseConnection.LaneDirection;
            releaseDirection.y = 0f;
            ManagedDepartureReleasePositions[vehicleId] =
                releaseConnection.LanePosition;
            if (releaseDirection.sqrMagnitude > 0.001f)
            {
                ManagedDepartureReleaseRotations[vehicleId] =
                    Quaternion.LookRotation(releaseDirection.normalized, Vector3.up);
            }
            ManagedDepartureReleaseLanes[vehicleId] = laneId;
            ManagedDepartureFacilitySnapshots[vehicleId] = facility;
            ManagedDepartureConnectionSnapshots[vehicleId] = entranceConnection;
        }

        private static bool IsManagedDepartureVehicleHeld(ushort vehicleId)
        {
            VehicleManager vehicleManager = VehicleManager.instance;
            if (vehicleId == 0
                || ManagedDepartureSpawnStates[vehicleId] == 0
                || ManagedDepartureExpectedInfos[vehicleId] == null
                || vehicleManager == null
                || vehicleId >= vehicleManager.m_vehicles.m_size)
            {
                return false;
            }

            ref Vehicle vehicleData = ref vehicleManager.m_vehicles.m_buffer[vehicleId];
            return (vehicleData.m_flags & Vehicle.Flags.Created) != 0
                   && (vehicleData.m_flags & Vehicle.Flags.Spawned) == 0
                   && vehicleData.Info == ManagedDepartureExpectedInfos[vehicleId];
        }

        private static bool IsManagedDepartureBlockingRelocation(
            ushort vehicleId,
            int facilityId)
        {
            if (ManagedDepartureReleaseFacilities[vehicleId] != facilityId
                || !IsManagedDepartureVehicleHeld(vehicleId))
            {
                return false;
            }

            if (ManagedDepartureAnimationQueued[vehicleId])
                return true;

            UndergroundParkingFacility facility;
            UndergroundParkingRoadConnection connection;
            return UndergroundParkingOccupancyManager.TryGetPortalForFacility(
                facilityId,
                out facility,
                out connection);
        }

        private static void RemoveManagedDepartureStagingAt(int index)
        {
            if (index < 0 || index >= ManagedDepartureStagingVehicles.Count)
                return;

            ushort vehicleId = ManagedDepartureStagingVehicles[index];
            ManagedDepartureStagingTracked[vehicleId] = false;
            int last = ManagedDepartureStagingVehicles.Count - 1;
            ManagedDepartureStagingVehicles[index] =
                ManagedDepartureStagingVehicles[last];
            ManagedDepartureStagingVehicles.RemoveAt(last);
            if (_managedDepartureStagingCursor > ManagedDepartureStagingVehicles.Count)
                _managedDepartureStagingCursor = ManagedDepartureStagingVehicles.Count;
        }

        private static void QueueManagedDepartureRelease(
            ushort vehicleId,
            VehicleInfo expectedInfo,
            ushort expectedOwner)
        {
            SimulationManager simulationManager = SimulationManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            if (simulationManager == null || vehicleManager == null)
                return;

            simulationManager.AddAction(() =>
            {
                if (vehicleId == 0
                    || vehicleId >= vehicleManager.m_vehicles.m_size
                    || ManagedDepartureSpawnStates[vehicleId] != 2)
                    return;

                ref Vehicle vehicleData = ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                if ((vehicleData.m_flags & Vehicle.Flags.Created) == 0
                    || vehicleData.Info != expectedInfo)
                {
                    ClearManagedDepartureTicket(vehicleId);
                    UndergroundParkingLog.Warning(
                        "UPG managed departure real vehicle no longer matched release ticket: vehicle="
                        + vehicleId
                        + " created="
                        + ((vehicleData.m_flags & Vehicle.Flags.Created) != 0)
                        + " expectedOwner="
                        + expectedOwner
                        + " actualOwner="
                        + vehicleData.m_transferSize);
                    return;
                }

                uint laneId = ManagedDepartureReleaseLanes[vehicleId];
                UndergroundParkingFacility refreshedFacility;
                UndergroundParkingRoadConnection refreshedConnection;
                if (!TryRefreshManagedDepartureConnection(
                        vehicleId,
                        out refreshedFacility,
                        out refreshedConnection))
                {
                    TrackManagedDepartureStaging(vehicleId);
                    return;
                }
                laneId = ManagedDepartureReleaseLanes[vehicleId];
                if (!HasManagedDepartureLaneSpace(vehicleId, expectedInfo))
                {
                    if (!ManagedDepartureReleaseWaitLogged[vehicleId])
                    {
                        ManagedDepartureReleaseWaitLogged[vehicleId] = true;
                        if (_departureLaneWaitLogCount++ < 24)
                        {
                            UndergroundParkingLog.Advanced(
                                "UPG departure held underground until exact lane release: vehicle="
                                + vehicleId
                                + " facility="
                                + ManagedDepartureReleaseFacilities[vehicleId]
                                + " lane="
                                + laneId);
                        }
                    }

                    // Do not enqueue another action from inside this action:
                    // Cities can drain newly-added actions in the same simulation
                    // frame, turning a blocked lane into an unbounded busy loop.
                    // Return the exact held ticket to the ordinary bounded UPG
                    // staging poll, which cannot retry it before a later update.
                    TrackManagedDepartureStaging(vehicleId);
                    return;
                }

                Vector3 releasePosition = ManagedDepartureReleasePositions[vehicleId];
                Quaternion releaseRotation = ManagedDepartureReleaseRotations[vehicleId];
                int facilityId = ManagedDepartureReleaseFacilities[vehicleId];
                AlignManagedDepartureVehicleForPublication(
                    ref vehicleData,
                    releasePosition,
                    releaseRotation);

                ClearManagedDepartureTicket(vehicleId);
                vehicleData.Spawn(vehicleId);
                UndergroundParkingLifecycleDiagnostics.TraceVehicle(
                    vehicleId,
                    "retrieval-real-vehicle-released-to-road",
                    facilityId,
                    "expectedOwner=" + expectedOwner
                    + " lane=" + laneId
                    + " position="
                    + UndergroundParkingLifecycleDiagnostics.FormatPosition(releasePosition));
                UndergroundParkingLog.Advanced(
                    "UPG managed departure real vehicle released at exact lane: vehicle="
                    + vehicleId
                    + " facility=" + facilityId
                    + " lane=" + laneId
                    + " position="
                    + UndergroundParkingLifecycleDiagnostics.FormatPosition(releasePosition));
            });
        }

        private static void AlignManagedDepartureVehicleForPublication(
            ref Vehicle vehicleData,
            Vector3 releasePosition,
            Quaternion releaseRotation)
        {
            Vehicle.Frame releaseFrame = vehicleData.GetLastFrameData();
            releaseFrame.m_position = releasePosition;
            releaseFrame.m_rotation = releaseRotation;
            releaseFrame.m_velocity = Vector3.zero;
            vehicleData.m_frame0 = releaseFrame;
            vehicleData.m_frame1 = releaseFrame;
            vehicleData.m_frame2 = releaseFrame;
            vehicleData.m_frame3 = releaseFrame;

            // The native owner initialized this exact car before UPG held it
            // for the accepted departure presentation. Align its buffered
            // movement targets with the validated proxy endpoint so the first
            // post-spawn vanilla tick cannot steer back toward that obsolete
            // creation pose. This also covers the same validated publication
            // during shutdown or NUKE. Path selection and every later target
            // update remain wholly native after the ticket is cleared.
            vehicleData.m_targetPos0 = AlignManagedDepartureTargetPosition(
                vehicleData.m_targetPos0,
                releasePosition);
            vehicleData.m_targetPos1 = AlignManagedDepartureTargetPosition(
                vehicleData.m_targetPos1,
                releasePosition);
            vehicleData.m_targetPos2 = AlignManagedDepartureTargetPosition(
                vehicleData.m_targetPos2,
                releasePosition);
            vehicleData.m_targetPos3 = AlignManagedDepartureTargetPosition(
                vehicleData.m_targetPos3,
                releasePosition);
        }

        private static Vector4 AlignManagedDepartureTargetPosition(
            Vector4 nativeTarget,
            Vector3 releasePosition)
        {
            // VehicleAI owns the fourth component's speed metadata. Correct
            // only the obsolete spatial coordinates and preserve that native
            // value for the first continuing movement update.
            nativeTarget.x = releasePosition.x;
            nativeTarget.y = releasePosition.y;
            nativeTarget.z = releasePosition.z;
            return nativeTarget;
        }

        private static void ClearManagedDepartureTicket(ushort vehicleId)
        {
            if (vehicleId == 0)
                return;

            ManagedDepartureSpawnStates[vehicleId] = 0;
            ManagedDepartureExpectedInfos[vehicleId] = null;
            ManagedDepartureExpectedOwners[vehicleId] = 0;
            ManagedDepartureReleasePositions[vehicleId] = Vector3.zero;
            ManagedDepartureReleaseRotations[vehicleId] = Quaternion.identity;
            ManagedDepartureReleaseLanes[vehicleId] = 0u;
            ManagedDepartureReleaseFacilities[vehicleId] = 0;
            ManagedDepartureGarageSlots[vehicleId] = 0;
            ManagedDepartureFacilitySnapshots[vehicleId] = UndergroundParkingFacility.None;
            ManagedDepartureConnectionSnapshots[vehicleId] = default(UndergroundParkingRoadConnection);
            ManagedDepartureReleaseWaitLogged[vehicleId] = false;
            ManagedDepartureAnimationQueued[vehicleId] = false;
            ManagedDepartureStagingTracked[vehicleId] = false;
        }

        private static void ReleasePendingDeparturesOnShutdown()
        {
            SimulationManager simulationManager = SimulationManager.instance;
            VehicleManager vehicleManager = VehicleManager.instance;
            if (simulationManager == null || vehicleManager == null)
                return;

            for (int i = 1; i < ManagedDepartureSpawnStates.Length; i++)
            {
                if (ManagedDepartureSpawnStates[i] == 0)
                    continue;

                ushort vehicleId = (ushort)i;
                UndergroundParkingFacility releaseFacility;
                UndergroundParkingRoadConnection releaseConnection;
                bool hasRefreshedConnection =
                    TryRefreshManagedDepartureConnection(
                        vehicleId,
                        out releaseFacility,
                        out releaseConnection);
                Vector3 releasePosition = Vector3.zero;
                Vector3 releaseDirection = Vector3.zero;
                bool hasLiveReleasePose = hasRefreshedConnection
                    && releaseFacility.IsValid
                    && UndergroundParkingAccessManager.TryGetLiveLanePose(
                        releaseConnection,
                        out releasePosition,
                        out releaseDirection);
                Quaternion releaseRotation = hasLiveReleasePose
                    ? Quaternion.LookRotation(releaseDirection, Vector3.up)
                    : Quaternion.identity;
                ClearManagedDepartureTicket(vehicleId);
                simulationManager.AddAction(() =>
                {
                    if (vehicleId >= vehicleManager.m_vehicles.m_size)
                        return;

                    ref Vehicle vehicleData = ref vehicleManager.m_vehicles.m_buffer[vehicleId];
                    if ((vehicleData.m_flags & Vehicle.Flags.Created) != 0
                        && (vehicleData.m_flags & Vehicle.Flags.Spawned) == 0)
                    {
                        if (!hasLiveReleasePose)
                        {
                            UndergroundParkingLog.Error(
                                "UPG retained held departure during shutdown because no validated live release lane remained: vehicle="
                                + vehicleId);
                            return;
                        }

                        AlignManagedDepartureVehicleForPublication(
                            ref vehicleData,
                            releasePosition,
                            releaseRotation);
                        vehicleData.Spawn(vehicleId);
                    }
                });
            }
        }

        private struct ManagedDepartureState
        {
            public readonly VehicleInfo Info;
            public readonly UndergroundParkingFacility Facility;
            public readonly UndergroundParkingRoadConnection Connection;
            public readonly ushort ParkedId;
            public readonly ushort VehicleId;

            public bool IsManaged
            {
                get { return ParkedId != 0; }
            }

            public ManagedDepartureState(
                VehicleInfo info,
                UndergroundParkingFacility facility,
                UndergroundParkingRoadConnection connection,
                ushort parkedId,
                ushort vehicleId = 0)
            {
                Info = info;
                Facility = facility;
                Connection = connection;
                ParkedId = parkedId;
                VehicleId = vehicleId;
            }

            public ManagedDepartureState WithVehicle(ushort vehicleId)
            {
                return new ManagedDepartureState(
                    Info,
                    Facility,
                    Connection,
                    ParkedId,
                    vehicleId);
            }
        }

        private struct ManagedRetrievalState
        {
            public readonly ushort ParkedId;
            public readonly Vector3 OriginalPosition;
            public readonly Quaternion OriginalRotation;

            public bool IsManaged
            {
                get { return ParkedId != 0; }
            }

            public ManagedRetrievalState(
                ushort parkedId,
                Vector3 originalPosition,
                Quaternion originalRotation)
            {
                ParkedId = parkedId;
                OriginalPosition = originalPosition;
                OriginalRotation = originalRotation;
            }
        }

        private struct ManagedDepartureContext
        {
            public readonly ushort ParkedId;
            public readonly UndergroundParkingFacility Facility;
            public readonly UndergroundParkingRoadConnection Connection;

            public bool IsManaged
            {
                get { return ParkedId != 0; }
            }

            public ManagedDepartureContext(
                ushort parkedId,
                UndergroundParkingFacility facility,
                UndergroundParkingRoadConnection connection)
            {
                ParkedId = parkedId;
                Facility = facility;
                Connection = connection;
            }
        }

        private struct RoutedArrivalState
        {
            public readonly ushort VehicleId;
            public readonly ushort ParkedId;
            public readonly int FacilityId;
            public readonly int SlotIndex;
            public readonly Vector3 SurfacePosition;
            public readonly Vector3 UndergroundPosition;
            public readonly Quaternion UndergroundRotation;
            public readonly VehicleInfo Info;
            public readonly Color SurfaceColor;
            public readonly Quaternion SurfaceRotation;
            public readonly uint OwnerCitizen;
            public readonly bool CreatedParkedIdentity;
            public readonly bool SlotClaimed;

            public bool IsPending
            {
                get { return VehicleId != 0 && FacilityId > 0 && SlotIndex >= 0; }
            }

            public RoutedArrivalState(
                ushort vehicleId,
                ushort parkedId,
                int facilityId,
                int slotIndex,
                Vector3 surfacePosition,
                Vector3 undergroundPosition,
                Quaternion undergroundRotation,
                VehicleInfo info,
                Color surfaceColor,
                Quaternion surfaceRotation,
                uint ownerCitizen,
                bool createdParkedIdentity = false,
                bool slotClaimed = false)
            {
                VehicleId = vehicleId;
                ParkedId = parkedId;
                FacilityId = facilityId;
                SlotIndex = slotIndex;
                SurfacePosition = surfacePosition;
                UndergroundPosition = undergroundPosition;
                UndergroundRotation = undergroundRotation;
                Info = info;
                SurfaceColor = surfaceColor;
                SurfaceRotation = surfaceRotation;
                OwnerCitizen = ownerCitizen;
                CreatedParkedIdentity = createdParkedIdentity;
                SlotClaimed = slotClaimed;
            }

            public RoutedArrivalState WithParkedIdentity(
                ushort parkedId,
                bool createdParkedIdentity)
            {
                return new RoutedArrivalState(
                    VehicleId,
                    parkedId,
                    FacilityId,
                    SlotIndex,
                    SurfacePosition,
                    UndergroundPosition,
                    UndergroundRotation,
                    Info,
                    SurfaceColor,
                    SurfaceRotation,
                    OwnerCitizen,
                    createdParkedIdentity,
                    SlotClaimed);
            }

            public RoutedArrivalState WithSlotClaim(
                int slotIndex,
                Vector3 undergroundPosition,
                Quaternion undergroundRotation)
            {
                return new RoutedArrivalState(
                    VehicleId,
                    ParkedId,
                    FacilityId,
                    slotIndex,
                    SurfacePosition,
                    undergroundPosition,
                    undergroundRotation,
                    Info,
                    SurfaceColor,
                    SurfaceRotation,
                    OwnerCitizen,
                    CreatedParkedIdentity,
                    true);
            }
        }

        private delegate bool TopLevelStartPathFindDelegate(
            PassengerCarAI ai,
            ushort vehicleId,
            ref Vehicle vehicleData);

        private delegate void ShowToolInfoDelegate(
            ToolBase instance,
            bool show,
            string text,
            Vector3 worldPosition);

        private struct ArrivalOccupant
        {
            public readonly uint CitizenId;
            public readonly ushort InstanceId;
            public readonly ushort OriginalParkedId;

            public ArrivalOccupant(
                uint citizenId,
                ushort instanceId,
                ushort originalParkedId)
            {
                CitizenId = citizenId;
                InstanceId = instanceId;
                OriginalParkedId = originalParkedId;
            }
        }

        private struct DeferredArrivalAssociation
        {
            public readonly ushort InstanceId;
            public readonly ushort ParkedId;
            public readonly uint EligibleFrame;

            public DeferredArrivalAssociation(
                ushort instanceId,
                ushort parkedId,
                uint eligibleFrame)
            {
                InstanceId = instanceId;
                ParkedId = parkedId;
                EligibleFrame = eligibleFrame;
            }
        }

        private sealed class ArrivalPedestrianContinuation
        {
            public readonly ArrivalOccupant[] Occupants;
            public readonly bool[] Placed;
            public readonly int Count;
            public readonly uint NativePath;
            public readonly int NativePositionIndex;
            public readonly byte NativeSegmentOffset;
            public readonly bool DeferredWalking;
            public bool HandoffStarted;
            public bool ParkedIdentityLinked;
            public bool NativeArrivalFinalized;

            public ArrivalPedestrianContinuation(
                ArrivalOccupant[] occupants,
                int count,
                uint nativePath,
                int nativePositionIndex,
                byte nativeSegmentOffset,
                bool deferredWalking)
            {
                Count = Mathf.Clamp(count, 0, MaxArrivalOccupants);
                Occupants = new ArrivalOccupant[Count];
                Placed = new bool[Count];
                Array.Copy(occupants, Occupants, Count);
                NativePath = nativePath;
                NativePositionIndex = nativePositionIndex;
                NativeSegmentOffset = nativeSegmentOffset;
                DeferredWalking = deferredWalking;
                HandoffStarted = false;
                ParkedIdentityLinked = false;
                NativeArrivalFinalized = false;
            }

        }

    }

    internal static class UndergroundParkingLifecycleDiagnostics
    {
        private const int MaxTracedCitizens = 4096;
        private const int MaxLifecycleEvents = 16384;
        private const int SampleBudget = 64;
        private const uint SampleIntervalFrames = 64u;
        private const float MovementLogDistanceSqr = 16f;
        private static readonly Dictionary<uint, TraceRecord> Records =
            new Dictionary<uint, TraceRecord>();
        private static readonly List<uint> Order = new List<uint>();
        private static readonly Dictionary<ushort, List<uint>> VehicleCitizens =
            new Dictionary<ushort, List<uint>>();
        private static readonly Dictionary<ushort, uint> ParkedOwners =
            new Dictionary<ushort, uint>();
        private static uint _nextTraceId = 1u;
        private static uint _lastSampleFrame;
        private static int _sampleCursor;
        private static int _eventCount;

        public static void Reset()
        {
            Records.Clear();
            Order.Clear();
            VehicleCitizens.Clear();
            ParkedOwners.Clear();
            _nextTraceId = 1u;
            _lastSampleFrame = 0u;
            _sampleCursor = 0;
            _eventCount = 0;
        }

        public static string FormatPosition(Vector3 position)
        {
            return "("
                   + position.x.ToString("0.0") + ","
                   + position.y.ToString("0.0") + ","
                   + position.z.ToString("0.0") + ")";
        }

        public static void TraceCitizen(
            uint citizenId,
            string stage,
            ushort activeVehicle,
            ushort parkedId,
            int facilityId,
            string detail)
        {
            if (citizenId == 0u || _eventCount >= MaxLifecycleEvents)
                return;

            TraceRecord record = GetOrCreate(citizenId);
            if (record == null)
                return;

            if (activeVehicle != 0)
                LinkVehicle(activeVehicle, citizenId);
            if (parkedId != 0)
                ParkedOwners[parkedId] = citizenId;

            Write(record, stage, activeVehicle, parkedId, facilityId, detail);
            CaptureState(record);
        }

        public static void LinkParkedVehicle(
            uint citizenId,
            ushort parkedId,
            ushort activeVehicle,
            int facilityId,
            string stage)
        {
            TraceCitizen(
                citizenId,
                stage,
                activeVehicle,
                parkedId,
                facilityId,
                "link=active-to-parked");
        }

        public static void LinkDepartureVehicle(
            ushort parkedId,
            ushort activeVehicle,
            int facilityId,
            string stage)
        {
            uint citizenId;
            if (parkedId == 0 || !ParkedOwners.TryGetValue(parkedId, out citizenId))
                return;

            VehicleCitizens.Remove(activeVehicle);
            TraceCitizen(
                citizenId,
                stage,
                activeVehicle,
                parkedId,
                facilityId,
                "link=parked-to-active");
        }

        public static void BeginArrivalVehicle(ushort vehicleId)
        {
            if (vehicleId != 0)
                VehicleCitizens.Remove(vehicleId);
        }

        public static void TraceVehicle(
            ushort vehicleId,
            string stage,
            int facilityId,
            string detail)
        {
            List<uint> citizens;
            if (vehicleId == 0 || !VehicleCitizens.TryGetValue(vehicleId, out citizens))
                return;

            for (int i = 0; i < citizens.Count; i++)
            {
                TraceCitizen(
                    citizens[i],
                    stage,
                    vehicleId,
                    0,
                    facilityId,
                    detail);
            }
        }

        public static void Update()
        {
            SimulationManager simulationManager = SimulationManager.instance;
            CitizenManager citizenManager = CitizenManager.instance;
            if (simulationManager == null || citizenManager == null || Order.Count == 0)
                return;

            uint frame = simulationManager.m_currentFrameIndex;
            if (frame - _lastSampleFrame < SampleIntervalFrames)
                return;
            _lastSampleFrame = frame;

            int checkedCount = 0;
            while (checkedCount++ < SampleBudget && Order.Count > 0)
            {
                if (_sampleCursor >= Order.Count)
                    _sampleCursor = 0;
                uint citizenId = Order[_sampleCursor++];
                TraceRecord record;
                if (!Records.TryGetValue(citizenId, out record)
                    || citizenId >= citizenManager.m_citizens.m_size)
                {
                    continue;
                }

                ref Citizen citizen = ref citizenManager.m_citizens.m_buffer[citizenId];
                ushort instanceId = citizen.m_instance;
                uint path = 0u;
                Vector3 position = Vector3.zero;
                if (instanceId != 0 && instanceId < citizenManager.m_instances.m_size)
                {
                    ref CitizenInstance instance =
                        ref citizenManager.m_instances.m_buffer[instanceId];
                    path = instance.m_path;
                    position = instance.GetLastFramePosition();
                }

                Vector3 movement = position - record.LastPosition;
                movement.y = 0f;
                bool changed = citizen.m_vehicle != record.LastVehicle
                               || citizen.m_parkedVehicle != record.LastParked
                               || path != record.LastPath
                               || movement.sqrMagnitude >= MovementLogDistanceSqr;
                if (!changed)
                    continue;

                Write(
                    record,
                    "sampled-state-change",
                    citizen.m_vehicle,
                    citizen.m_parkedVehicle,
                    0,
                    "movement=" + Mathf.Sqrt(movement.sqrMagnitude).ToString("0.0"));
                CaptureState(record);
            }
        }

        private static TraceRecord GetOrCreate(uint citizenId)
        {
            TraceRecord record;
            if (Records.TryGetValue(citizenId, out record))
                return record;
            if (Records.Count >= MaxTracedCitizens)
                return null;

            record = new TraceRecord(_nextTraceId++, citizenId);
            Records[citizenId] = record;
            Order.Add(citizenId);
            CaptureState(record);
            return record;
        }

        private static void LinkVehicle(ushort vehicleId, uint citizenId)
        {
            List<uint> citizens;
            if (!VehicleCitizens.TryGetValue(vehicleId, out citizens))
            {
                citizens = new List<uint>();
                VehicleCitizens[vehicleId] = citizens;
            }
            if (!citizens.Contains(citizenId))
                citizens.Add(citizenId);
        }

        private static void CaptureState(TraceRecord record)
        {
            CitizenManager citizenManager = CitizenManager.instance;
            if (citizenManager == null
                || record.CitizenId == 0u
                || record.CitizenId >= citizenManager.m_citizens.m_size)
            {
                return;
            }

            ref Citizen citizen =
                ref citizenManager.m_citizens.m_buffer[record.CitizenId];
            record.LastVehicle = citizen.m_vehicle;
            record.LastParked = citizen.m_parkedVehicle;
            record.LastPath = 0u;
            record.LastPosition = Vector3.zero;
            ushort instanceId = citizen.m_instance;
            if (instanceId != 0 && instanceId < citizenManager.m_instances.m_size)
            {
                ref CitizenInstance instance =
                    ref citizenManager.m_instances.m_buffer[instanceId];
                record.LastPath = instance.m_path;
                record.LastPosition = instance.GetLastFramePosition();
            }
        }

        private static void Write(
            TraceRecord record,
            string stage,
            ushort activeVehicle,
            ushort parkedId,
            int facilityId,
            string detail)
        {
            if (_eventCount++ >= MaxLifecycleEvents)
                return;

            CitizenManager citizenManager = CitizenManager.instance;
            PathManager pathManager = PathManager.instance;
            SimulationManager simulationManager = SimulationManager.instance;
            ushort instanceId = 0;
            ushort citizenVehicle = 0;
            ushort citizenParked = 0;
            uint path = 0u;
            byte pathFlags = 0;
            Vector3 position = Vector3.zero;
            if (citizenManager != null
                && record.CitizenId < citizenManager.m_citizens.m_size)
            {
                ref Citizen citizen =
                    ref citizenManager.m_citizens.m_buffer[record.CitizenId];
                instanceId = citizen.m_instance;
                citizenVehicle = citizen.m_vehicle;
                citizenParked = citizen.m_parkedVehicle;
                if (instanceId != 0 && instanceId < citizenManager.m_instances.m_size)
                {
                    ref CitizenInstance instance =
                        ref citizenManager.m_instances.m_buffer[instanceId];
                    path = instance.m_path;
                    position = instance.GetLastFramePosition();
                    if (path != 0u
                        && pathManager != null
                        && path < pathManager.m_pathUnits.m_size)
                    {
                        pathFlags = pathManager.m_pathUnits.m_buffer[path].m_pathFindFlags;
                    }
                }
            }

            uint frame = simulationManager == null
                ? 0u
                : simulationManager.m_currentFrameIndex;
            UndergroundParkingLog.Advanced(
                "UPG-LIFECYCLE trace=" + record.TraceId
                + " stage=" + stage
                + " frame=" + frame
                + " citizen=" + record.CitizenId
                + " instance=" + instanceId
                + " active=" + activeVehicle
                + " parked=" + parkedId
                + " citizenVehicle=" + citizenVehicle
                + " citizenParked=" + citizenParked
                + " path=" + path
                + " pathFlags=" + pathFlags
                + " position=" + FormatPosition(position)
                + " facility=" + facilityId
                + " detail=" + (detail ?? string.Empty));
        }

        private sealed class TraceRecord
        {
            public readonly uint TraceId;
            public readonly uint CitizenId;
            public ushort LastVehicle;
            public ushort LastParked;
            public uint LastPath;
            public Vector3 LastPosition;

            public TraceRecord(uint traceId, uint citizenId)
            {
                TraceId = traceId;
                CitizenId = citizenId;
            }
        }
    }
}
