using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace ScratchyBald.CitiesSkylines.Shared
{
    /// <summary>
    /// Cross-assembly facade for the cooperative Scratchy's scan scheduler.
    ///
    /// Each mod compiles this source file into its own assembly. The first copy
    /// initialized in a process creates the broker; later copies discover that
    /// broker and communicate with it through a framework-only reflection
    /// contract. This avoids a separately deployed dependency while retaining
    /// one real queue across all participating mods.
    /// </summary>
    public static class ScratchysScanManager
    {
        public const int BackgroundPriority = 0;
        public const int MaintenancePriority = 100;
        public const int StartupPriority = 200;
        public const int PlayerRequestedPriority = 300;

        internal const int MainThreadContext = 0;
        internal const int SimulationThreadContext = 1;
        private const string BrokerObjectName = "ScratchyBald.Scratchy's Scan Manager";
        internal const string BrokerProtocol = "Scratchy's Scan Manager.Protocol.1";

        private static Component _broker;
        private static MethodInfo _registerClientMethod;
        private static MethodInfo _registerClientWithDiagnosticsMethod;
        private static MethodInfo _queueMethod;
        private static MethodInfo _pumpMethod;
        private static MethodInfo _cancelMethod;
        private static MethodInfo _cancelOwnerMethod;
        private static MethodInfo _snapshotMethod;

        /// <summary>
        /// Registers a mod as a client and creates the process-wide broker when
        /// this is the first participating mod to initialize.
        /// </summary>
        public static void Initialize(string ownerId)
        {
            Initialize(ownerId, null);
        }

        /// <summary>
        /// Registers a client and contributes its existing advanced-diagnostics
        /// setting to the process-wide broker. If any registered client enables
        /// advanced diagnostics, routine broker diagnostics are emitted for all
        /// clients; the broker owns no separate player setting.
        /// </summary>
        public static void Initialize(
            string ownerId,
            Func<bool> advancedDiagnosticsEnabled)
        {
            ValidateIdentifier(ownerId, "ownerId");
            Component broker = ResolveBroker(ownerId);
            if (advancedDiagnosticsEnabled != null
                && _registerClientWithDiagnosticsMethod != null)
            {
                Invoke(
                    broker,
                    _registerClientWithDiagnosticsMethod,
                    new object[] { ownerId, advancedDiagnosticsEnabled },
                    "register client diagnostics");
            }
            else
            {
                Invoke(
                    broker,
                    _registerClientMethod,
                    new object[] { ownerId },
                    "register client");
            }
        }

        /// <summary>
        /// Queues cooperative work that is safe to execute from Unity Update.
        /// Each step must perform one small indivisible unit and return true
        /// only when the complete request has finished.
        /// </summary>
        public static string QueueMainThreadScan(
            string ownerId,
            string requestId,
            int priority,
            Func<bool> step,
            Action completed,
            Action<Exception> failed)
        {
            return Queue(
                ownerId,
                requestId,
                priority,
                MainThreadContext,
                step,
                completed,
                failed);
        }

        public static string QueueMainThreadScan(
            string ownerId,
            string requestId,
            int priority,
            Func<bool> step)
        {
            return QueueMainThreadScan(
                ownerId,
                requestId,
                priority,
                step,
                null,
                null);
        }

        /// <summary>
        /// Queues cooperative work that must execute from a participating
        /// mod's simulation-thread callback. The mod must call
        /// PumpSimulationThread from that callback while its level is active.
        /// </summary>
        public static string QueueSimulationThreadScan(
            string ownerId,
            string requestId,
            int priority,
            Func<bool> step,
            Action completed,
            Action<Exception> failed)
        {
            return Queue(
                ownerId,
                requestId,
                priority,
                SimulationThreadContext,
                step,
                completed,
                failed);
        }

        public static string QueueSimulationThreadScan(
            string ownerId,
            string requestId,
            int priority,
            Func<bool> step)
        {
            return QueueSimulationThreadScan(
                ownerId,
                requestId,
                priority,
                step,
                null,
                null);
        }

        /// <summary>
        /// Offers the global broker time in the caller's simulation-thread
        /// callback. Calling this from several mods is safe; the broker admits
        /// at most one pump and one request at a time.
        /// </summary>
        public static void PumpSimulationThread()
        {
            Component broker = ResolveExistingBroker();
            if (broker == null)
                return;

            Invoke(
                broker,
                _pumpMethod,
                new object[] { SimulationThreadContext },
                "pump simulation-thread work");
        }

        public static void Cancel(string ticket)
        {
            if (string.IsNullOrEmpty(ticket))
                return;

            Component broker = ResolveExistingBroker();
            if (broker == null)
                return;

            Invoke(
                broker,
                _cancelMethod,
                new object[] { ticket },
                "cancel request");
        }

        /// <summary>
        /// Cancels queued and active requests owned by a mod. Every
        /// participating mod must call this during level unload before
        /// releasing state captured by its step delegates.
        /// </summary>
        public static void CancelOwner(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
                return;

            Component broker = ResolveExistingBroker();
            if (broker == null)
                return;

            Invoke(
                broker,
                _cancelOwnerMethod,
                new object[] { ownerId },
                "cancel owner requests");
        }

        /// <summary>
        /// Returns a compact diagnostic snapshot without exposing a
        /// cross-assembly custom return type.
        /// </summary>
        public static string GetSnapshot()
        {
            Component broker = ResolveExistingBroker();
            if (broker == null)
                return "owner=none active=none queued=0 clients=0";

            object result = Invoke(
                broker,
                _snapshotMethod,
                new object[0],
                "read scheduler snapshot");
            return result as string ?? "owner=unknown active=unknown queued=unknown clients=unknown";
        }

        private static string Queue(
            string ownerId,
            string requestId,
            int priority,
            int context,
            Func<bool> step,
            Action completed,
            Action<Exception> failed)
        {
            ValidateIdentifier(ownerId, "ownerId");
            ValidateIdentifier(requestId, "requestId");
            if (step == null)
                throw new ArgumentNullException("step");

            Component broker = ResolveBroker(ownerId);
            object result = Invoke(
                broker,
                _queueMethod,
                new object[]
                {
                    ownerId,
                    requestId,
                    priority,
                    context,
                    step,
                    completed,
                    failed
                },
                "queue request");
            string ticket = result as string;
            if (string.IsNullOrEmpty(ticket))
                throw new InvalidOperationException("Scratchy's Scan Manager returned an invalid request ticket.");

            return ticket;
        }

        private static Component ResolveBroker(string firstOwnerId)
        {
            Component broker = ResolveExistingBroker();
            if (broker != null)
                return broker;

            GameObject host = GameObject.Find(BrokerObjectName);
            if (host == null)
            {
                host = new GameObject(BrokerObjectName);
                UnityEngine.Object.DontDestroyOnLoad(host);
            }

            ScratchysScanBroker created = host.AddComponent<ScratchysScanBroker>();
            created.ClaimOwnership(firstOwnerId);
            BindBroker(created);
            return created;
        }

        private static Component ResolveExistingBroker()
        {
            if (_broker != null)
                return _broker;

            GameObject host = GameObject.Find(BrokerObjectName);
            if (host == null)
                return null;

            MonoBehaviour[] components = host.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour candidate = components[i];
                if (candidate == null)
                    continue;

                Type type = candidate.GetType();
                PropertyInfo protocolProperty = type.GetProperty(
                    "Protocol",
                    BindingFlags.Instance | BindingFlags.Public);
                if (protocolProperty == null
                    || protocolProperty.PropertyType != typeof(string))
                {
                    continue;
                }

                string protocol;
                try
                {
                    protocol = protocolProperty.GetValue(candidate, null) as string;
                }
                catch
                {
                    continue;
                }

                if (!string.Equals(protocol, BrokerProtocol, StringComparison.Ordinal))
                    continue;

                BindBroker(candidate);
                return candidate;
            }

            return null;
        }

        private static void BindBroker(Component broker)
        {
            Type type = broker.GetType();
            MethodInfo registerClient = GetRequiredMethod(
                type,
                "RegisterClient",
                new[] { typeof(string) });
            MethodInfo registerClientWithDiagnostics = type.GetMethod(
                "RegisterClient",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(Func<bool>) },
                null);
            MethodInfo queue = GetRequiredMethod(
                type,
                "Queue",
                new[]
                {
                    typeof(string),
                    typeof(string),
                    typeof(int),
                    typeof(int),
                    typeof(Func<bool>),
                    typeof(Action),
                    typeof(Action<Exception>)
                });
            MethodInfo pump = GetRequiredMethod(
                type,
                "Pump",
                new[] { typeof(int) });
            MethodInfo cancel = GetRequiredMethod(
                type,
                "Cancel",
                new[] { typeof(string) });
            MethodInfo cancelOwner = GetRequiredMethod(
                type,
                "CancelOwner",
                new[] { typeof(string) });
            MethodInfo snapshot = GetRequiredMethod(
                type,
                "GetSnapshot",
                Type.EmptyTypes);

            _broker = broker;
            _registerClientMethod = registerClient;
            _registerClientWithDiagnosticsMethod =
                registerClientWithDiagnostics;
            _queueMethod = queue;
            _pumpMethod = pump;
            _cancelMethod = cancel;
            _cancelOwnerMethod = cancelOwner;
            _snapshotMethod = snapshot;
        }

        private static MethodInfo GetRequiredMethod(
            Type type,
            string name,
            Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public,
                null,
                parameterTypes,
                null);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "Scratchy's Scan Manager broker does not implement protocol method "
                    + name
                    + ".");
            }

            return method;
        }

        private static object Invoke(
            Component broker,
            MethodInfo method,
            object[] arguments,
            string operation)
        {
            try
            {
                return method.Invoke(broker, arguments);
            }
            catch (TargetInvocationException exception)
            {
                Exception cause = exception.InnerException ?? exception;
                throw new InvalidOperationException(
                    "Scratchy's Scan Manager could not "
                    + operation
                    + ": "
                    + cause.Message,
                    cause);
            }
        }

        private static void ValidateIdentifier(string value, string argumentName)
        {
            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0)
                throw new ArgumentException("A non-empty identifier is required.", argumentName);
        }
    }

    public sealed class ScratchysScanBroker : MonoBehaviour
        {
            private const double PumpBudgetMilliseconds = 4.0d;
            private const double SlowStepWarningMilliseconds = 12.0d;
            private const int MaximumPriority = 1000000;
            private const int MinimumPriority = -1000000;
            private const int AgingPriorityMaximum = 50;
            private const double AgingPrioritySeconds = 2.0d;

            private sealed class Request
            {
                public string Ticket;
                public string OwnerId;
                public string RequestId;
                public int Priority;
                public int Context;
                public Func<bool> Step;
                public Action Completed;
                public Action<Exception> Failed;
                public long Sequence;
                public long QueuedTimestamp;
                public long StartedTimestamp;
                public int Steps;
                public bool Cancelled;
            }

            private readonly object _sync = new object();
            private readonly List<Request> _queued = new List<Request>();
            private readonly HashSet<string> _clients = new HashSet<string>();
            private readonly Dictionary<string, Func<bool>>
                _advancedDiagnosticsProviders =
                    new Dictionary<string, Func<bool>>();
            private volatile Func<bool>[] _advancedDiagnosticsSnapshot =
                new Func<bool>[0];

            private Request _active;
            private string _ownerId;
            private long _nextSequence;
            private bool _pumpRunning;
            private bool _hasPumpedSimulationUnityFrame;
            private int _lastPumpedSimulationUnityFrame;
            private volatile bool _advancedDiagnosticsEnabled;

            public string Protocol
            {
                get { return ScratchysScanManager.BrokerProtocol; }
            }

            public void ClaimOwnership(string ownerId)
            {
                lock (_sync)
                {
                    if (string.IsNullOrEmpty(_ownerId))
                        _ownerId = ownerId;
                }
            }

            public void RegisterClient(string ownerId)
            {
                RegisterClient(ownerId, null);
            }

            public void RegisterClient(
                string ownerId,
                Func<bool> advancedDiagnosticsEnabled)
            {
                lock (_sync)
                {
                    if (string.IsNullOrEmpty(_ownerId))
                        _ownerId = ownerId;
                    _clients.Add(ownerId);
                    if (advancedDiagnosticsEnabled != null)
                    {
                        _advancedDiagnosticsProviders[ownerId] =
                            advancedDiagnosticsEnabled;
                        RebuildAdvancedDiagnosticsSnapshotLocked();
                    }
                }

                if (advancedDiagnosticsEnabled == null)
                    return;

                try
                {
                    if (advancedDiagnosticsEnabled())
                        _advancedDiagnosticsEnabled = true;
                }
                catch
                {
                    // A client setting must never prevent scheduler use.
                }
            }

            public string Queue(
                string ownerId,
                string requestId,
                int priority,
                int context,
                Func<bool> step,
                Action completed,
                Action<Exception> failed)
            {
                if (context != ScratchysScanManager.MainThreadContext
                    && context != ScratchysScanManager.SimulationThreadContext)
                {
                    throw new ArgumentOutOfRangeException("context");
                }

                if (step == null)
                    throw new ArgumentNullException("step");

                Request request;
                lock (_sync)
                {
                    if (string.IsNullOrEmpty(_ownerId))
                        _ownerId = ownerId;
                    _clients.Add(ownerId);
                    CancelMatchingLocked(ownerId, requestId);

                    long sequence = ++_nextSequence;
                    request = new Request
                    {
                        Ticket = ownerId + ":" + requestId + ":" + sequence,
                        OwnerId = ownerId,
                        RequestId = requestId,
                        Priority = Math.Max(
                            MinimumPriority,
                            Math.Min(MaximumPriority, priority)),
                        Context = context,
                        Step = step,
                        Completed = completed,
                        Failed = failed,
                        Sequence = sequence,
                        QueuedTimestamp = Stopwatch.GetTimestamp()
                    };
                    _queued.Add(request);
                }

                if (_advancedDiagnosticsEnabled)
                {
                    UnityEngine.Debug.Log(
                        "[Scratchy's Scan Manager] Queued "
                        + request.Ticket
                        + " priority="
                        + request.Priority
                        + " context="
                        + GetContextName(request.Context)
                        + ".");
                }
                return request.Ticket;
            }

            public void Pump(int context)
            {
                if (context != ScratchysScanManager.MainThreadContext
                    && context != ScratchysScanManager.SimulationThreadContext)
                {
                    return;
                }

                lock (_sync)
                {
                    if (_pumpRunning)
                        return;
                    if (context == ScratchysScanManager.SimulationThreadContext)
                    {
                        int unityFrame = Time.frameCount;
                        if (_hasPumpedSimulationUnityFrame
                            && unityFrame == _lastPumpedSimulationUnityFrame)
                        {
                            return;
                        }

                        _hasPumpedSimulationUnityFrame = true;
                        _lastPumpedSimulationUnityFrame = unityFrame;
                    }
                    _pumpRunning = true;
                }

                try
                {
                    PumpCore(context);
                }
                finally
                {
                    lock (_sync)
                        _pumpRunning = false;
                }
            }

            public void Cancel(string ticket)
            {
                if (string.IsNullOrEmpty(ticket))
                    return;

                lock (_sync)
                {
                    if (_active != null
                        && string.Equals(
                            _active.Ticket,
                            ticket,
                            StringComparison.Ordinal))
                    {
                        _active.Cancelled = true;
                    }

                    for (int i = _queued.Count - 1; i >= 0; i--)
                    {
                        if (string.Equals(
                                _queued[i].Ticket,
                                ticket,
                                StringComparison.Ordinal))
                        {
                            _queued.RemoveAt(i);
                        }
                    }
                }
            }

            public void CancelOwner(string ownerId)
            {
                if (string.IsNullOrEmpty(ownerId))
                    return;

                int cancelled = 0;
                lock (_sync)
                {
                    if (_active != null
                        && string.Equals(
                            _active.OwnerId,
                            ownerId,
                            StringComparison.Ordinal))
                    {
                        _active.Cancelled = true;
                        cancelled++;
                    }

                    for (int i = _queued.Count - 1; i >= 0; i--)
                    {
                        if (!string.Equals(
                                _queued[i].OwnerId,
                                ownerId,
                                StringComparison.Ordinal))
                        {
                            continue;
                        }

                        _queued.RemoveAt(i);
                        cancelled++;
                    }

                    _clients.Remove(ownerId);
                    if (_advancedDiagnosticsProviders.Remove(ownerId))
                        RebuildAdvancedDiagnosticsSnapshotLocked();
                }

                if (cancelled > 0 && _advancedDiagnosticsEnabled)
                {
                    UnityEngine.Debug.Log(
                        "[Scratchy's Scan Manager] Cancelled owner="
                        + ownerId
                        + " requests="
                        + cancelled
                        + ".");
                }
            }

            public string GetSnapshot()
            {
                lock (_sync)
                {
                    return "owner="
                           + (_ownerId ?? "unknown")
                           + " active="
                           + (_active == null ? "none" : _active.Ticket)
                           + " activeContext="
                           + (_active == null
                               ? "none"
                               : GetContextName(_active.Context))
                           + " queued="
                           + _queued.Count
                           + " clients="
                           + _clients.Count;
                }
            }

            private void Update()
            {
                RefreshAdvancedDiagnostics();
                Pump(ScratchysScanManager.MainThreadContext);
            }

            private void RefreshAdvancedDiagnostics()
            {
                Func<bool>[] providers = _advancedDiagnosticsSnapshot;
                bool enabled = false;
                for (int i = 0; i < providers.Length; i++)
                {
                    try
                    {
                        if (providers[i]())
                        {
                            enabled = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Logging preferences never affect scheduled work.
                    }
                }

                _advancedDiagnosticsEnabled = enabled;
            }

            private void RebuildAdvancedDiagnosticsSnapshotLocked()
            {
                Func<bool>[] providers =
                    new Func<bool>[_advancedDiagnosticsProviders.Count];
                _advancedDiagnosticsProviders.Values.CopyTo(providers, 0);
                _advancedDiagnosticsSnapshot = providers;
            }

            private void PumpCore(int context)
            {
                long deadline = Stopwatch.GetTimestamp()
                                + MillisecondsToTicks(PumpBudgetMilliseconds);

                while (Stopwatch.GetTimestamp() < deadline)
                {
                    Request request;
                    bool started = false;
                    lock (_sync)
                    {
                        DropCancelledActiveLocked();
                        if (_active == null)
                        {
                            _active = TakeNextLocked();
                            if (_active != null)
                            {
                                _active.StartedTimestamp = Stopwatch.GetTimestamp();
                                started = true;
                            }
                        }

                        request = _active;
                        if (request == null || request.Context != context)
                            return;
                    }

                    if (started && _advancedDiagnosticsEnabled)
                    {
                        UnityEngine.Debug.Log(
                            "[Scratchy's Scan Manager] Started "
                            + request.Ticket
                            + " waitedMs="
                            + ElapsedMilliseconds(request.QueuedTimestamp)
                                .ToString("0.0")
                            + ".");
                    }

                    long stepStarted = Stopwatch.GetTimestamp();
                    bool completed;
                    try
                    {
                        completed = request.Step();
                    }
                    catch (Exception exception)
                    {
                        FailRequest(request, exception);
                        continue;
                    }

                    double stepMilliseconds =
                        ElapsedMilliseconds(stepStarted);
                    if (_advancedDiagnosticsEnabled
                        && stepMilliseconds >= SlowStepWarningMilliseconds)
                    {
                        UnityEngine.Debug.LogWarning(
                            "[Scratchy's Scan Manager] Slow atomic step: ticket="
                            + request.Ticket
                            + " stepMs="
                            + stepMilliseconds.ToString("0.0")
                            + ". Split this participant's step into a smaller indivisible unit.");
                    }

                    Action completion = null;
                    lock (_sync)
                    {
                        request.Steps++;
                        if (request.Cancelled)
                        {
                            if (_active == request)
                                _active = null;
                            continue;
                        }

                        if (completed)
                        {
                            if (_active == request)
                                _active = null;
                            completion = request.Completed;
                        }
                        else if (HasHigherPriorityQueuedLocked(request))
                        {
                            if (_active == request)
                            {
                                request.QueuedTimestamp = Stopwatch.GetTimestamp();
                                _queued.Add(request);
                                _active = null;
                            }
                        }
                    }

                    if (!completed)
                        continue;

                    if (_advancedDiagnosticsEnabled)
                    {
                        UnityEngine.Debug.Log(
                            "[Scratchy's Scan Manager] Completed "
                            + request.Ticket
                            + " steps="
                            + request.Steps
                            + " elapsedMs="
                            + ElapsedMilliseconds(request.StartedTimestamp)
                                .ToString("0.0")
                            + ".");
                    }
                    if (completion != null)
                    {
                        try
                        {
                            completion();
                        }
                        catch (Exception exception)
                        {
                            UnityEngine.Debug.LogError(
                                "[Scratchy's Scan Manager] Completion callback failed: ticket="
                                + request.Ticket
                                + " exception="
                                + exception);
                        }
                    }
                }
            }

            private Request TakeNextLocked()
            {
                int bestIndex = -1;
                int bestPriority = int.MinValue;
                long bestSequence = long.MaxValue;
                long now = Stopwatch.GetTimestamp();
                for (int i = 0; i < _queued.Count; i++)
                {
                    Request request = _queued[i];
                    if (request.Cancelled)
                        continue;

                    int effectivePriority =
                        GetEffectivePriority(request, now);
                    if (bestIndex >= 0
                        && (effectivePriority < bestPriority
                            || (effectivePriority == bestPriority
                                && request.Sequence >= bestSequence)))
                    {
                        continue;
                    }

                    bestIndex = i;
                    bestPriority = effectivePriority;
                    bestSequence = request.Sequence;
                }

                if (bestIndex < 0)
                {
                    _queued.Clear();
                    return null;
                }

                Request selected = _queued[bestIndex];
                _queued.RemoveAt(bestIndex);
                return selected;
            }

            private bool HasHigherPriorityQueuedLocked(Request active)
            {
                long now = Stopwatch.GetTimestamp();
                int activePriority = GetEffectivePriority(active, now);
                for (int i = 0; i < _queued.Count; i++)
                {
                    Request queued = _queued[i];
                    if (!queued.Cancelled
                        && GetEffectivePriority(queued, now) > activePriority)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static int GetEffectivePriority(
                Request request,
                long now)
            {
                double waitedSeconds =
                    (double)(now - request.QueuedTimestamp)
                    / Stopwatch.Frequency;
                int aging = Math.Min(
                    AgingPriorityMaximum,
                    (int)(waitedSeconds / AgingPrioritySeconds));
                return request.Priority + aging;
            }

            private void DropCancelledActiveLocked()
            {
                if (_active != null && _active.Cancelled)
                    _active = null;
            }

            private void CancelMatchingLocked(
                string ownerId,
                string requestId)
            {
                if (_active != null
                    && string.Equals(
                        _active.OwnerId,
                        ownerId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        _active.RequestId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    _active.Cancelled = true;
                }

                for (int i = _queued.Count - 1; i >= 0; i--)
                {
                    Request request = _queued[i];
                    if (string.Equals(
                            request.OwnerId,
                            ownerId,
                            StringComparison.Ordinal)
                        && string.Equals(
                            request.RequestId,
                            requestId,
                            StringComparison.Ordinal))
                    {
                        _queued.RemoveAt(i);
                    }
                }
            }

            private void FailRequest(
                Request request,
                Exception exception)
            {
                Action<Exception> failed;
                lock (_sync)
                {
                    if (_active == request)
                        _active = null;
                    failed = request.Failed;
                }

                UnityEngine.Debug.LogError(
                    "[Scratchy's Scan Manager] Request failed: ticket="
                    + request.Ticket
                    + " steps="
                    + request.Steps
                    + " exception="
                    + exception);
                if (failed == null)
                    return;

                try
                {
                    failed(exception);
                }
                catch (Exception callbackException)
                {
                    UnityEngine.Debug.LogError(
                        "[Scratchy's Scan Manager] Failure callback failed: ticket="
                        + request.Ticket
                        + " exception="
                        + callbackException);
                }
            }

            private static long MillisecondsToTicks(double milliseconds)
            {
                return (long)(Stopwatch.Frequency * milliseconds / 1000.0d);
            }

            private static double ElapsedMilliseconds(long startedTimestamp)
            {
                return (Stopwatch.GetTimestamp() - startedTimestamp)
                       * 1000.0d
                       / Stopwatch.Frequency;
            }

            private static string GetContextName(int context)
            {
                return context == ScratchysScanManager.SimulationThreadContext
                    ? "simulation"
                    : "main";
            }
        }
}
