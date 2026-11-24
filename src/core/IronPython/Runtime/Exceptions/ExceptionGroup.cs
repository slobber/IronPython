// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime.Exceptions {
    public static partial class PythonExceptions {
        [MultiRuntimeAware]
        private static PythonType? BaseExceptionGroupStorage;
        [MultiRuntimeAware]
        private static PythonType? ExceptionGroupStorage;

        public static PythonType BaseExceptionGroup {
            get {
                if (BaseExceptionGroupStorage is null) {
                    System.Threading.Interlocked.CompareExchange(
                        ref BaseExceptionGroupStorage,
                        CreateSubType(
                            TypeCache.BaseException,
                            typeof(_BaseExceptionGroup),
                            (message, innerException) => new PythonException(message, innerException)),
                        null);
                }
                return BaseExceptionGroupStorage!;
            }
        }

        public static PythonType ExceptionGroup {
            get {
                if (ExceptionGroupStorage is null) {
                    PythonType groupType = CreateSubType(
                        Exception,
                        typeof(_ExceptionGroup),
                        (message, innerException) => new PythonException(message, innerException));
                    groupType.BaseTypes = new[] { Exception, BaseExceptionGroup };
                    groupType.ResolutionOrder = Mro.Calculate(groupType, groupType.BaseTypes);
                    System.Threading.Interlocked.CompareExchange(
                        ref ExceptionGroupStorage,
                        groupType,
                        null);
                }
                return ExceptionGroupStorage!;
            }
        }

        [PythonType("BaseExceptionGroup"), PythonHidden, DynamicBaseType, Serializable]
        public class _BaseExceptionGroup : BaseException, ICodeFormattable {
            private PythonTuple _exceptions = PythonTuple.EMPTY;
            private object? _message;
            private bool _exceptionsWereTuple;
            private string? _exceptionsContainerName;

            public _BaseExceptionGroup([NotNone] PythonType type) : base(type) {
            }

            public void __init__([NotNone] object message, [NotNone] object exceptions) {
                if (message is not string) {
                    throw PythonOps.TypeError(
                        "argument 1 must be str, not {0}",
                        PythonOps.GetPythonTypeName(message));
                }
                if (exceptions is null || exceptions is SetCollection) {
                    throw PythonOps.TypeError("second argument (exceptions) must be a sequence");
                }
                _exceptionsWereTuple = exceptions is PythonTuple;
                _exceptionsContainerName = PythonOps.GetPythonTypeName(exceptions);
                PythonTuple items = PythonTuple.Make(exceptions);
                Initialize(message, items);
                base.__init__(message, exceptions);
            }

            public void __init__([NotNone] object message, [NotNone] object exceptions, [NotNone] params object[] extra) {
                throw PythonOps.TypeError("BaseExceptionGroup.__new__() takes exactly 2 arguments");
            }

            private void Initialize(object message, PythonTuple items) {
                if (items.Count == 0) {
                    throw PythonOps.ValueError("second argument (exceptions) must be a non-empty sequence");
                }
                for (int i = 0; i < items.Count; i++) {
                    object? item = items[i];
                    if (item is not BaseException) {
                        throw PythonOps.ValueError(
                            "Item {0} of second argument (exceptions) is not an exception",
                            i);
                    }
                    if (PythonOps.IsInstance(DefaultContext.Default, this, Exception) &&
                        !PythonOps.IsInstance(DefaultContext.Default, item, Exception)) {
                        throw PythonOps.TypeError(
                            "Cannot nest BaseExceptions in {0}",
                            PythonOps.GetPythonTypeName(this) == "ExceptionGroup"
                                ? "an ExceptionGroup"
                                : "'" + PythonOps.GetPythonTypeName(this) + "'");
                    }
                }
                InitializeUnchecked(message, items);
            }

            private void InitializeUnchecked(object message, PythonTuple items) {
                _exceptions = items;
                _message = message;
            }

            private void InitializeDerived(object message, PythonTuple items) {
                InitializeUnchecked(message, items);
                _exceptionsWereTuple = true;
                _exceptionsContainerName = "tuple";
                base.__init__(message, items);
            }

            public object? message => _message;

            public PythonTuple exceptions => _exceptions;

            public object derive(CodeContext context, [NotNone] object exceptions)
            {
                PythonTuple derivedExceptions = exceptions is PythonTuple tuple
                    ? tuple
                    : PythonTuple.Make(exceptions);
                object result;
                if (GetType() == typeof(_BaseExceptionGroup) || GetType() == typeof(_ExceptionGroup)) {
                    var group = (_BaseExceptionGroup)Activator.CreateInstance(
                        GetType(),
                        DynamicHelpers.GetPythonType(this))!;
                    group.InitializeDerived(message ?? string.Empty, derivedExceptions);
                    result = group;
                } else {
                    result = PythonCalls.Call(
                        context,
                        DynamicHelpers.GetPythonType(this),
                        message ?? string.Empty,
                        derivedExceptions)!;
                }
                if (result is BaseException derived) {
                    derived.__cause__ = __cause__;
                    derived.__context__ = __context__;
                    derived.__traceback__ = __traceback__;
                    if (__dict__.TryGetValue("__notes__", out object? notes)) {
                        derived.__dict__["__notes__"] = notes is PythonList list
                            ? new PythonList(list)
                            : notes;
                    }
                }
                return result;
            }

            public PythonTuple split(CodeContext context, [NotNone] object condition) {
                ValidateCondition(context, condition);
                if (condition is PythonType groupMatcher &&
                    PythonOps.IsInstance(context, this, groupMatcher)) {
                    return PythonTuple.MakeTuple(this, null);
                }
                var matching = new List<object>();
                var rest = new List<object>();
                SplitChildren(context, condition, matching, rest);
                return PythonTuple.MakeTuple(
                    matching.Count == 0
                        ? null
                        : matching.Count == _exceptions.Count
                            ? this
                            : derive(context, PythonTuple.Make(matching)),
                    rest.Count == 0
                        ? null
                        : rest.Count == _exceptions.Count
                            ? this
                            : derive(context, PythonTuple.Make(rest)));
            }

            public object? subgroup(CodeContext context, [NotNone] object condition) {
                ValidateCondition(context, condition);
                if (condition is PythonType groupMatcher &&
                    PythonOps.IsInstance(context, this, groupMatcher)) {
                    return this;
                }
                var matching = new List<object>();
                var rest = new List<object>();
                SplitChildren(context, condition, matching, rest);
                if (matching.Count == 0) return null;
                if (matching.Count == _exceptions.Count) return this;
                return derive(context, PythonTuple.Make(matching));
            }

            private void SplitChildren(
                CodeContext context,
                object condition,
                List<object> matching,
                List<object> rest) {
                foreach (object exception in _exceptions) {
                    if (exception is _BaseExceptionGroup nested) {
                        var nestedMatching = new List<object>();
                        var nestedRest = new List<object>();
                        nested.SplitChildren(context, condition, nestedMatching, nestedRest);
                        if (nestedMatching.Count != 0) {
                            matching.Add(nested.derive(context, PythonTuple.Make(nestedMatching)));
                        }
                        if (nestedRest.Count != 0) {
                            rest.Add(nested.derive(context, PythonTuple.Make(nestedRest)));
                        }
                    } else if (Matches(context, condition, exception)) {
                        matching.Add(exception);
                    } else {
                        rest.Add(exception);
                    }
                }
            }

            public string __str__(CodeContext context) {
                int count = _exceptions.Count;
                return count == 1
                    ? $"{PythonOps.ToString(message)} (1 sub-exception)"
                    : $"{PythonOps.ToString(message)} ({count} sub-exceptions)";
            }

            public override string __repr__(CodeContext context) {
                string items = string.Join(
                    ", ",
                    _exceptions.Select(exception => PythonOps.Repr(context, exception)));
                bool tupleStyle = _exceptionsWereTuple
                    || args is not PythonTuple argumentTuple
                    || argumentTuple.Count < 2;
                string sequence;
                if (tupleStyle) {
                    sequence = $"({items}{(_exceptions.Count == 1 ? "," : string.Empty)})";
                } else if (_exceptionsContainerName == "deque") {
                    sequence = $"deque([{items}])";
                } else {
                    sequence = $"[{items}]";
                }
                return $"{DynamicHelpers.GetPythonType(this).Name}({PythonOps.Repr(context, message)}, {sequence})";
            }

            private static void ValidateCondition(CodeContext context, object condition) {
                if (condition is PythonType type &&
                    !PythonOps.IsSubClass(context, type, DynamicHelpers.GetPythonTypeFromType(typeof(BaseException)))) {
                    throw PythonOps.TypeError("expected exception type, tuple of exception types, or callable");
                }
                if (condition is PythonTuple types) {
                    foreach (object value in types) {
                        if (value is not PythonType exceptionType ||
                            !PythonOps.IsSubClass(context, exceptionType, DynamicHelpers.GetPythonTypeFromType(typeof(BaseException)))) {
                            throw PythonOps.TypeError("expected exception type, tuple of exception types, or callable");
                        }
                    }
                    return;
                }
                if (condition is not PythonType && !PythonOps.IsCallable(context, condition)) {
                    throw PythonOps.TypeError("expected a callable object or tuple of types");
                }
            }

            private static bool Matches(CodeContext context, object condition, object exception) {
                if (condition is PythonType || condition is PythonTuple) {
                    return PythonOps.IsInstance(context, exception, condition);
                }
                return PythonOps.IsTrue(PythonCalls.Call(context, condition, exception));
            }
        }

        [PythonType("ExceptionGroup"), PythonHidden, DynamicBaseType, Serializable]
        public class _ExceptionGroup : _BaseExceptionGroup {
            public _ExceptionGroup([NotNone] PythonType type) : base(type) {
            }
        }
    }
}
