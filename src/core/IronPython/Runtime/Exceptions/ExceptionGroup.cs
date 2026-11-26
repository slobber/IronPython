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
                    groupType.BaseTypes = new[] { BaseExceptionGroup, Exception };
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

            [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "Usage",
                "IPY01",
                Justification = "Null arguments are rejected with Python-specific constructor errors.")]
            public static object __new__(
                CodeContext context,
                [NotNone] PythonType cls,
                [NotNone] params object[] args) {
                if (args.Length != 2) {
                    throw PythonOps.TypeError(
                        "BaseExceptionGroup.__new__() takes exactly 2 arguments");
                }
                object message = args[0];
                object exceptions = args[1];
                if (message is not string) {
                    throw PythonOps.TypeError(
                        "argument 1 must be str, not {0}",
                        PythonOps.GetPythonTypeName(message));
                }
                if (exceptions is null || exceptions is SetCollection) {
                    throw PythonOps.TypeError(
                        "second argument (exceptions) must be a sequence");
                }
                PythonTuple items;
                try {
                    items = PythonTuple.Make(exceptions);
                } catch (Exception) {
                    throw PythonOps.TypeError(
                        "second argument (exceptions) must be a sequence");
                }
                // CPython validates the sequence repr even when construction succeeds.
                try {
                    PythonOps.Repr(DefaultContext.Default, exceptions);
                } catch (TypeErrorException) {
                    throw PythonOps.TypeError(
                        "{0}.__repr__() must return a str, not NoneType",
                        PythonOps.GetPythonTypeName(exceptions));
                }
                PythonType actualType =
                    cls == BaseExceptionGroup
                    && items.All(item => PythonOps.IsInstance(context, item, Exception))
                        ? ExceptionGroup
                        : cls;
                var group = (_BaseExceptionGroup)Activator.CreateInstance(
                    actualType.UnderlyingSystemType,
                    actualType)!;
                group._exceptionsWereTuple = exceptions is PythonTuple;
                group._exceptionsContainerName = PythonOps.GetPythonTypeName(exceptions);
                group.Initialize(message, items);
                return group;
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

            [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "Usage",
                "IPY01",
                Justification = "The setter deliberately raises a Python-facing read-only error.")]
            public object? message {
                get => _message;
                set => throw PythonOps.AttributeError(
                    "readonly attribute 'message'");
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "Usage",
                "IPY01",
                Justification = "The setter deliberately raises a Python-facing read-only error.")]
            public object exceptions {
                get => _exceptions;
                set => throw PythonOps.AttributeError(
                    "readonly attribute 'exceptions'");
            }

            public object derive(CodeContext context, [NotNone] object exceptions)
            {
                PythonTuple derivedExceptions = exceptions is PythonTuple tuple
                    ? tuple
                    : PythonTuple.Make(exceptions);
                Type resultType = derivedExceptions.All(item =>
                    PythonOps.IsInstance(DefaultContext.Default, item, Exception))
                    ? typeof(_ExceptionGroup)
                    : typeof(_BaseExceptionGroup);
                var group = (_BaseExceptionGroup)Activator.CreateInstance(
                    resultType,
                    resultType == typeof(_ExceptionGroup)
                        ? ExceptionGroup
                        : BaseExceptionGroup)!;
                group.InitializeDerived(message ?? string.Empty, derivedExceptions);
                return group;
            }

            private static object InvokeDerive(
                CodeContext context,
                _BaseExceptionGroup group,
                PythonTuple exceptions) {
                object result = PythonCalls.Call(
                    context,
                    PythonOps.GetBoundAttr(context, group, "derive"),
                    exceptions)!;
                if (result is not _BaseExceptionGroup) {
                    throw PythonOps.TypeError(
                        "derive must return an instance of BaseExceptionGroup");
                }
                var derived = (BaseException)result;
                derived.Cause = group.Cause;
                derived.Context = group.Context;
                derived.Traceback = group.Traceback;
                if (group.__dict__.TryGetValue("__notes__", out object? notes)) {
                    derived.__dict__["__notes__"] = notes is PythonList list
                        ? new PythonList(list)
                        : notes;
                    if (notes is not PythonList) {
                        derived.__dict__.Remove("__notes__");
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
                        : InvokeDerive(context, this, PythonTuple.Make(matching)),
                    rest.Count == 0
                        ? null
                        : InvokeDerive(context, this, PythonTuple.Make(rest)));
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
                if (matching.Count == _exceptions.Count
                    && condition is not PythonType
                    && condition is not PythonTuple) {
                    return this;
                }
                return InvokeDerive(context, this, PythonTuple.Make(matching));
            }

            private void SplitChildren(
                CodeContext context,
                object condition,
                List<object> matching,
                List<object> rest) {
                foreach (object exception in _exceptions) {
                    if (exception is _BaseExceptionGroup nested) {
                        if (Matches(context, condition, nested)) {
                            matching.Add(nested);
                        } else {
                            var nestedMatching = new List<object>();
                            var nestedRest = new List<object>();
                            nested.SplitChildren(
                                context,
                                condition,
                                nestedMatching,
                                nestedRest);
                            if (nestedMatching.Count != 0) {
                                matching.Add(InvokeDerive(
                                    context,
                                    nested,
                                    PythonTuple.Make(nestedMatching)));
                            }
                            if (nestedRest.Count != 0) {
                                rest.Add(InvokeDerive(
                                    context,
                                    nested,
                                    PythonTuple.Make(nestedRest)));
                            }
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

            public static new object __new__(
                CodeContext context,
                [NotNone] PythonType cls,
                [NotNone] params object[] args)
                => _BaseExceptionGroup.__new__(context, cls, args);
        }
    }
}
