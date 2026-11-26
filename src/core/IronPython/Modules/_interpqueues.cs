// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting.Runtime;

[assembly: PythonModule(
    "_interpqueues",
    typeof(IronPython.Modules.PythonInterpreterQueues))]

namespace IronPython.Modules {
    public static class PythonInterpreterQueues {
        private sealed class QueueState {
            internal QueueState(int maxSize, object unboundOperation) {
                MaxSize = maxSize;
                UnboundOperation = unboundOperation;
            }

            internal int MaxSize { get; }
            internal object UnboundOperation { get; }
            internal LinkedList<QueueEntry> Items { get; } = new();
            internal int Bindings { get; set; }
        }

        private sealed class QueueEntry {
            internal QueueEntry(
                object? payload,
                int ownerId,
                int unboundOperation) {
                Payload = payload;
                OwnerId = ownerId;
                UnboundOperation = unboundOperation;
            }

            internal object? Payload { get; set; }
            internal int OwnerId { get; }
            internal int UnboundOperation { get; set; }
            internal bool IsUnbound { get; set; }
        }

        private static readonly object _lock = new object();
        private static readonly Dictionary<int, QueueState> _queues = new();
        private static int _nextId = 1;
        private static object? _queueEmptyType;
        private static object? _queueFullType;

        private static PythonType? _queueError;
        private static PythonType? _queueNotFoundError;

        public static PythonType QueueError {
            get {
                if (_queueError is null) {
                    Interlocked.CompareExchange(
                        ref _queueError,
                        PythonExceptions.CreateSubType(
                            DefaultContext.Default.LanguageContext,
                            PythonExceptions.Exception,
                            "QueueError",
                            "concurrent.interpreters",
                            null,
                            (message, inner) => new RuntimeException(message, inner)),
                        null);
                }
                return _queueError;
            }
        }

        public static PythonType QueueNotFoundError {
            get {
                if (_queueNotFoundError is null) {
                    Interlocked.CompareExchange(
                        ref _queueNotFoundError,
                        PythonExceptions.CreateSubType(
                            DefaultContext.Default.LanguageContext,
                            QueueError,
                            "QueueNotFoundError",
                            "concurrent.interpreters",
                            null,
                            (message, inner) => new RuntimeException(message, inner)),
                        null);
                }
                return _queueNotFoundError;
            }
        }

        public static int create(int maxSize, [NotNone] object unboundOperation, int ownerId) {
            lock (_lock) {
                int id = _nextId++;
                _queues.Add(id, new QueueState(maxSize, unboundOperation));
                return id;
            }
        }

        public static void bind(int id) {
            lock (_lock) {
                GetRequired(id).Bindings++;
            }
        }

        public static void release(int id) {
            lock (_lock) {
                QueueState state = GetRequired(id);
                if (state.Bindings <= 0) {
                    throw PythonExceptions.CreateThrowable(
                        QueueError,
                        "queue is not bound");
                }
                state.Bindings--;
                if (state.Bindings == 0 && state.Items.Count == 0) {
                    _queues.Remove(id);
                }
            }
        }

        public static void destroy(int id) {
            lock (_lock) {
                if (!_queues.Remove(id)) {
                    throw NotFound(id);
                }
                Monitor.PulseAll(_lock);
            }
        }

        public static PythonList list_all() {
            var result = new PythonList();
            lock (_lock) {
                foreach (KeyValuePair<int, QueueState> pair in _queues) {
                    result.Add(PythonTuple.MakeTuple(
                        pair.Key,
                        pair.Value.UnboundOperation,
                        -1));
                }
            }
            return result;
        }

        public static PythonTuple get_queue_defaults(int id) {
            lock (_lock) {
                QueueState state = GetRequired(id);
                return PythonTuple.MakeTuple(state.UnboundOperation, null);
            }
        }

        public static int get_maxsize(int id) {
            lock (_lock) {
                return GetRequired(id).MaxSize;
            }
        }

        public static int get_count(int id) {
            lock (_lock) {
                return GetRequired(id).Items.Count;
            }
        }

        public static bool is_full(int id) {
            lock (_lock) {
                QueueState state = GetRequired(id);
                return state.MaxSize > 0 && state.Items.Count >= state.MaxSize;
            }
        }

        public static void put(
            CodeContext context,
            int id,
            object? value,
            [NotNone] object unboundOperation) {
            int operation = unboundOperation is int valueOperation
                && valueOperation == -1
                ? -1
                : Converter.ConvertToInt32(unboundOperation);
            object payload = Serialize(context, value);
            int ownerId = Converter.ConvertToInt32(
                PythonInterpreters.get_current(context)[0]);
            lock (_lock) {
                QueueState state = GetRequired(id);
                if (state.MaxSize > 0 && state.Items.Count >= state.MaxSize) {
                    throw QueueException(_queueFullType, "queue is full");
                }
                if (operation == -1) {
                    operation = Converter.ConvertToInt32(
                        state.UnboundOperation);
                }
                state.Items.AddLast(
                    new QueueEntry(payload, ownerId, operation));
            }
        }

        public static PythonTuple get(CodeContext context, int id) {
            QueueEntry entry;
            lock (_lock) {
                QueueState state = GetRequired(id);
                if (state.Items.Count == 0) {
                    throw QueueException(_queueEmptyType, "queue is empty");
                }
                entry = state.Items.First!.Value;
                state.Items.RemoveFirst();
            }
            if (entry.IsUnbound) {
                return PythonTuple.MakeTuple(null, entry.UnboundOperation);
            }
            return PythonTuple.MakeTuple(
                Deserialize(context, entry.Payload!),
                null);
        }

        public static void _register_heap_types(
            [NotNone] params object[] types) {
            if (types.Length >= 3
                && _queueEmptyType is null
                && _queueFullType is null) {
                _queueEmptyType = types[1];
                _queueFullType = types[2];
            }
        }

        public static void OnInterpreterDestroyed(int id) {
            lock (_lock) {
                foreach (QueueState state in _queues.Values) {
                    LinkedListNode<QueueEntry>? node = state.Items.First;
                    while (node is not null) {
                        LinkedListNode<QueueEntry>? next = node.Next;
                        QueueEntry entry = node.Value;
                        if (entry.OwnerId == id) {
                            switch (entry.UnboundOperation) {
                                case 1:
                                    state.Items.Remove(node);
                                    break;
                                case 2:
                                case 3:
                                    entry.Payload = null;
                                    entry.IsUnbound = true;
                                    break;
                            }
                        }
                        node = next;
                    }
                }
                Monitor.PulseAll(_lock);
            }
        }

        private static object Serialize(
            CodeContext context,
            object? value) {
            try {
                object pickle = Importer.ImportModule(
                    context,
                    null,
                    "pickle",
                    false,
                    0);
                object dumps = PythonOps.GetBoundAttr(
                    context,
                    pickle,
                    "dumps");
                return PythonCalls.Call(
                    context,
                    dumps,
                    value,
                    5)!;
            } catch (Exception exception) {
                throw PythonExceptions.CreateThrowable(
                    PythonInterpreters.NotShareableError,
                    $"object is not shareable: {exception.Message}");
            }
        }

        private static object? Deserialize(
            CodeContext context,
            object payload) {
            object pickle = Importer.ImportModule(
                context,
                null,
                "pickle",
                false,
                0);
            object loads = PythonOps.GetBoundAttr(
                context,
                pickle,
                "loads");
            return PythonCalls.Call(context, loads, payload);
        }

        private static QueueState GetRequired(int id) {
            if (!_queues.TryGetValue(id, out QueueState? state)) {
                throw NotFound(id);
            }
            return state;
        }

        private static Exception NotFound(int id) {
            return PythonExceptions.CreateThrowable(
                QueueNotFoundError,
                $"queue {id} not found");
        }

        private static Exception QueueException(object? type, string message) {
            return type is PythonType pythonType
                ? PythonExceptions.CreateThrowable(pythonType, message)
                : PythonExceptions.CreateThrowable(QueueError, message);
        }
    }
}
