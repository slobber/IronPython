// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Scripting.Runtime;

using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

namespace IronPython.Runtime {

    [PythonType("coroutine")]
    [DontMapIDisposableToContextManager, DontMapIEnumerableToContains]
    public sealed class PythonCoroutine : ICodeFormattable, IWeakReferenceable {

#if FEATURE_NET_ASYNC

        // Under .NET async, `async def` is compiled directly to a Task<object?> via the DLR's AsyncExpression + AsyncHelpers.
        // PythonCoroutine is a wrapper over the Task. _interrupts is the AsyncInterruptSource passed into AsyncExpression;
        // throw(exc) interrupts the body with it.
        //
        // Lazy start: the body is not executed at construction. _factory is the thunk that creates (and thereby starts driving) the Task on
        // first access; _task caches the materialized Task. Calling an async def is therefore side-effect free until the coroutine is first
        // driven (send/AsTask/GetAwaiter) — PEP 492.
        private readonly Func<Task<object?>> _factory;
        private Task<object?>? _task;
        private Task<object?>? _asTask;
        private readonly string _name;
        private readonly FunctionCode? _code;
        private readonly AsyncInterruptSource _interrupts;
        private readonly AsyncSuspensionCoordinator _coordinator;
        private readonly CodeContext _context;
        [ThreadStatic]
        private static PythonCoroutine? _current;
        internal static PythonCoroutine? Current => _current;
        internal FunctionCode? Code => _code;
        private static readonly ConditionalWeakTable<Task, PythonCoroutine> AwaitableTasks = new();
        private static readonly BuiltinFunction CompleteFutureIfPendingCallback =
            BuiltinFunction.MakeFunction(
                nameof(CompleteFutureIfPending),
                [
                    typeof(PythonCoroutine).GetMethod(
                        nameof(CompleteFutureIfPending),
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Static)!
                ],
                typeof(PythonCoroutine));
        private WeakRefTracker? _tracker;

        internal PythonCoroutine(
            Func<Task<object?>> factory,
            string name,
            FunctionCode? code,
            AsyncInterruptSource interrupts,
            CodeContext context,
            AsyncSuspensionCoordinator coordinator) {
            _factory = factory;
            _name = name;
            _code = code;
            _interrupts = interrupts;
            _context = context;
            _coordinator = coordinator;
        }

        private Task<object?> EnsureTask() {
            if (_task is null) {
                _task = _factory();
                AwaitableTasks.AddOrUpdate(_task, this);
                _task.ContinueWith(
                    _ => _coordinator.NotifyCompleted(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            return _task;
        }


        [LightThrowing]
        public object? send(object? value) {
            PythonCoroutine? previous = _current;
            _current = this;
            try {
                if (_task is not null) _coordinator.ResumeAndWait(_task, value);
                var task = EnsureTask();
                if (!task.IsCompleted) {
                    if (_coordinator.TryConsumeYield(out object? yielded)) {
                        return yielded;
                    }
                    if (_coordinator.TryGetAwait(out Task? awaited)) {
                        if (AsyncSuspensionCoordinator.IsYield(awaited)) return null;
                        return WaitForCoroutine(awaited);
                    }
                    return WaitForCoroutine(task);
                }
                return SettleCompletedTask(task);
            } finally {
                _current = previous;
            }
        }

        [LightThrowing]
        public object? @throw(object? value) => @throw(value, null, null);

        [LightThrowing]
        public object? @throw(object? type, object? value) => @throw(type, value, null);

        /// <summary>
        ///   Throw an exception into the generator at the suspended <c>yield</c>. Returns an awaitable.
        /// </summary>
        /// <remarks>
        ///   When awaited the exception is rethrown at the resume point:
        ///   if the body catches it and yields again that value is produced;
        ///   otherwise the exception propagates (or StopAsyncIteration if the body finishes).
        ///   <br/>
        ///   In typical use, this is called with a single exception instance similar to the way the raise keyword is used.
        ///   <br/>
        ///   Changed in CPython 3.12: The signature (type[, value[, traceback]]) is deprecated
        ///   and may be removed in a future version of Python.
        /// </remarks>
        [LightThrowing]
        public object? @throw(object? type, object? value, object? traceback) {
            // Validate up front so we don't mutate any state on bad input.
            if (PythonOps.ValidateThrowArguments(type, value, traceback) is Exception error) return LightExceptions.Throw(error);

            // The Task driving this coroutine can complete concurrently with the
            // asyncio Task deciding to inject a cancellation at its next step.
            // Settle the already-completed body first so its real result or
            // exception is not overwritten by the later interruption.
            if (_task is { IsCompleted: true } completedTask) {
                return SettleCompletedTask(completedTask);
            }

            // Construct the actual exception object. DefaultContext.Default suffices here — throw()'s
            // CodeContext role is limited to resolving class-form exception construction, and the
            // coroutine's own context will re-bind the chain when the exception unwinds inside the body.
            Exception ex = PythonOps.MakeExceptionForGenerator(DefaultContext.Default, type, value, traceback);

            // `await` of another Python coroutine is lowered through that coroutine's Task.
            // In CPython, throw() at that await enters the awaited coroutine first, so its
            // try/except/finally blocks observe the injected exception. Interrupt the
            // nested body, then keep waiting on this coroutine so its await can observe
            // the nested result or exception and continue normal exception handling.
            bool interruptedNested = false;
            if (_task is not null
                && _coordinator.TryGetAwait(out Task? nestedAwaited)) {
                if (AwaitableTasks.TryGetValue(
                        nestedAwaited,
                        out PythonCoroutine? awaitedCoroutine)) {
                    interruptedNested = awaitedCoroutine.InterruptCurrentAwait(ex);
                } else if (IsCancellation(ex)) {
                    interruptedNested = PythonOps.TryCancelObservedFuture(nestedAwaited, ex);
                }
            }

            // The body sees `ex` raised at the await it is suspended at, or at its next await if it is running.
            // If the body hasn't started, it doesn't run at all: its task fails with `ex`, as in CPython.
            bool interrupted = interruptedNested || _interrupts.Interrupt(ex);
            if (!interruptedNested) _coordinator.Resume();
            if (!interrupted) {
                // The coroutine has finished. throw() raises the exception immediately —
                // the body has nothing left to observe it. Matches CPython.
                return LightExceptions.Throw(ex);
            }

            var task = EnsureTask();
            if (!task.IsCompleted) {
                if (_coordinator.TryGetAwait(out Task? awaited)) {
                    if (AsyncSuspensionCoordinator.IsYield(awaited)) return null;
                    return WaitForCoroutine(awaited);
                }
                return WaitForCoroutine(task);
            }
            return SettleCompletedTask(task);
        }

        /// <summary>
        /// Delivers an exception at this coroutine's current await. Nested Python
        /// coroutine awaits are traversed first, matching the inline execution of
        /// <c>await</c> in CPython.
        /// </summary>
        private bool InterruptCurrentAwait(Exception exception) {
            // The awaited body already produced its outcome. Let the outer await
            // observe that outcome instead of injecting a later cancellation into
            // a coroutine that can no longer run.
            if (_task is { IsCompleted: true }) {
                return true;
            }
            if (_task is not null
                && _coordinator.TryGetAwait(out Task? nestedAwaited)
                && AwaitableTasks.TryGetValue(nestedAwaited, out PythonCoroutine? awaitedCoroutine)
                && awaitedCoroutine.InterruptCurrentAwait(exception)) {
                return true;
            }
            if (_task is not null
                && _coordinator.TryGetAwait(out Task? observedFutureTask)
                && IsCancellation(exception)) {
                bool cancelled = PythonOps.TryCancelObservedFuture(
                    observedFutureTask,
                    exception);
                if (cancelled) return true;
            }
            if (!_interrupts.Interrupt(exception)) {
                return false;
            }
            _coordinator.Resume();
            return true;
        }

        private static bool IsCancellation(Exception exception) {
            object pythonException = PythonExceptions.ToPython(exception);
            return DynamicHelpers.GetPythonType(pythonException).Name == "CancelledError";
        }

        private object WaitForCoroutine(Task task) {
            object events = Importer.ImportModule(
                _context,
                new PythonDictionary(),
                "asyncio.events",
                false,
                0)!;
            if (!PythonOps.TryGetBoundAttr(_context, events, "get_running_loop", out object? getRunningLoop)) {
                return null!;
            }

            object loop;
            try {
                loop = PythonCalls.Call(_context, getRunningLoop)!;
            } catch (Exception) {
                // Manual coroutine.send() outside an event loop is allowed to observe
                // the current suspension instead of requiring asyncio to drive it.
                return null!;
            }

            if (!PythonOps.TryGetBoundAttr(_context, loop, "create_future", out object? createFuture)) {
                return LightExceptions.Throw(PythonOps.RuntimeError("event loop has no create_future"));
            }

            object future;
            try {
                future = PythonCalls.Call(_context, createFuture)!;
                PythonOps.SetAttr(_context, future, "_asyncio_future_blocking", true);
            } catch (Exception exception) {
                return LightExceptions.Throw(exception);
            }

            task.ContinueWith(
                _ => NotifyFuture(loop, future),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return future;
        }

        private void NotifyFuture(object loop, object future) {
            try {
                if (!PythonOps.TryGetBoundAttr(_context, loop, "call_soon_threadsafe", out object? callSoon)) {
                    return;
                }
                PythonCalls.Call(
                    _context,
                    callSoon,
                    CompleteFutureIfPendingCallback,
                    future);
            } catch {
                // The event loop may already be closed while the coroutine finishes.
            }
        }

        [PythonHidden]
        private static void CompleteFutureIfPending(object future) {
            CodeContext context = DefaultContext.Default;
            if (!PythonOps.TryGetBoundAttr(context, future, "done", out object? done) ||
                PythonOps.IsTrue(PythonCalls.Call(context, done)) ||
                !PythonOps.TryGetBoundAttr(context, future, "set_result", out object? setResult)) {
                return;
            }
            PythonCalls.Call(context, setResult, (object?)null);
        }


        private static object SettleCompletedTask(Task<object?> task) {
            if (task.IsCanceled) {
                // Surfaces as Python CancelledError via the OCE -> CancelledError mapping in PythonExceptions.
                return LightExceptions.Throw(new TaskCanceledException(task));
            }
            if (task.IsFaulted) {
                return LightExceptions.Throw(task.Exception?.InnerException ?? task.Exception);
            }
            return LightExceptions.Throw(new PythonExceptions._StopIteration().InitAndGetClrException(task.Result!));
        }


        [LightThrowing]
        public object? close() => null;

        public object __await__() => new CoroutineWrapper(this);

        public FunctionCode? cr_code => _code;

        // A not-yet-driven coroutine reports 0 (not running) without materializing the Task.
        // Merely inspecting cr_running must not start the body.
        public bool cr_running => _task is not null && !_task.IsCompleted;

        public TraceBackFrame? cr_frame => null;

        public string __name__ => _name;

        public string __qualname__ => _name;

        /// <summary>Returns the underlying <see cref="Task{Object}"/>, starting the body on first call.</summary>
        public Task<object?> AsTask() {
            _coordinator.Disable();
            if (_asTask is null) {
                _asTask = EnsureTask();
                if (_coordinator.SawBareYield) AsyncSuspensionCoordinator.MarkYield(_asTask);
            }
            return _asTask;
        }

        /// <summary>Enables <c>await coroutine</c> from C# code.</summary>
        public TaskAwaiter<object?> GetAwaiter() => AsTask().GetAwaiter();

        internal Task<object?> Task => EnsureTask();

#else  // !FEATURE_NET_ASYNC

        private readonly PythonGenerator _generator;
        private WeakRefTracker? _tracker;

        internal PythonCoroutine(PythonGenerator generator) {
            _generator = generator;
        }

        [LightThrowing]
        public object? send(object? value) {
            return _generator.send(value);
        }

        [LightThrowing]
        public object? @throw(object? type) {
            return _generator.@throw(type);
        }

        [LightThrowing]
        public object? @throw(object? type, object? value) {
            return _generator.@throw(type, value);
        }

        [LightThrowing]
        // The signature (type[, value[, traceback]]) is deprecated in CPython 3.12
        public object? @throw(object? type, object? value, object? traceback) {
            return _generator.@throw(type, value, traceback);
        }

        [LightThrowing]
        public object? close() {
            return _generator.close();
        }

        public object __await__() {
            return new CoroutineWrapper(this);
        }

        public FunctionCode cr_code => _generator.gi_code;

        public bool cr_running => _generator.gi_running;

        public TraceBackFrame cr_frame => _generator.gi_frame;

        public string __name__ => _generator.__name__;

        public string __qualname__ {
            get => _generator.__name__;
        }

        /// <summary>
        /// Converts this coroutine into a .NET <see cref="Task{Object}"/>,
        /// allowing C# code to <c>await</c> an IronPython async method.
        /// The coroutine is driven on a single thread to avoid issues with
        /// thread-local state in the Python generator runtime.
        /// </summary>
        public Task<object?> AsTask() {
            return Task.Run(() => {
                while (true) {
                    object? result = send(null);

                    if (LightExceptions.IsLightException(result)) {
                        var clrExc = LightExceptions.GetLightException(result);
                        if (clrExc is StopIterationException) {
                            var pyExc = ((IPythonAwareException)clrExc).PythonException;
                            return pyExc is PythonExceptions._StopIteration si ? si.value : null;
                        }
                        throw clrExc;
                    }

                    if (result is Task task) {
                        task.Wait();
                    }
                }
            });
        }

        /// <summary>
        /// Enables <c>await coroutine</c> from C# code.
        /// </summary>
        public TaskAwaiter<object?> GetAwaiter() => AsTask().GetAwaiter();

        internal PythonGenerator Generator => _generator;

#endif  // FEATURE_NET_ASYNC - the rest of the members are shared between both implementations of PythonCoroutine

        #region ICodeFormattable Members

        public string __repr__(CodeContext context) {
            return $"<coroutine object {__name__} at {PythonOps.HexId(this)}>";
        }

        #endregion

        #region IWeakReferenceable Members

        WeakRefTracker? IWeakReferenceable.GetWeakRef() {
            return _tracker;
        }

        bool IWeakReferenceable.SetWeakRef(WeakRefTracker value) {
            _tracker = value;
            return true;
        }

        void IWeakReferenceable.SetFinalizer(WeakRefTracker value) {
            _tracker = value;
        }

        #endregion
    }


    [PythonType("coroutine_wrapper")]
    public sealed class CoroutineWrapper : IEnumerable {
        private readonly PythonCoroutine _coroutine;

        internal CoroutineWrapper(PythonCoroutine coroutine) {
            _coroutine = coroutine;
        }

        [LightThrowing]
        public object? __next__() {
            return _coroutine.send(null);
        }

        [LightThrowing]
        public object? send(object? value) {
            return _coroutine.send(value);
        }

        [LightThrowing]
        public object? @throw(object? type) {
            return _coroutine.@throw(type);
        }

        [LightThrowing]
        public object? @throw(object? type, object? value) {
            return _coroutine.@throw(type, value);
        }

        [LightThrowing]
        public object? @throw(object? type, object? value, object? traceback) {
            return _coroutine.@throw(type, value, traceback);
        }

        public object? close() {
            return _coroutine.close();
        }

        public CoroutineWrapper __iter__() {
            return this;
        }

        IEnumerator IEnumerable.GetEnumerator() => new CoroutineWrapperEnumerator(_coroutine);

        private sealed class CoroutineWrapperEnumerator : IEnumerator {
            private readonly PythonCoroutine _coroutine;
            private object? _current;

            public CoroutineWrapperEnumerator(PythonCoroutine coroutine) {
                _coroutine = coroutine;
            }

            public object? Current => _current;

            public bool MoveNext() {
                object? result = _coroutine.send(null);
                if (LightExceptions.IsLightException(result)) {
                    Exception exception = LightExceptions.GetLightException(result);
                    if (exception is StopIterationException) {
                        return false;
                    }
                    throw exception;
                }

                _current = result;
                return true;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose() { }
        }
    }
}
