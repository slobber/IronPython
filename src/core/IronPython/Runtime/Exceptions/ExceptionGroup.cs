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

            public _BaseExceptionGroup([NotNone] PythonType type) : base(type) {
            }

            public void __init__([NotNone] object message, [NotNone] object exceptions) {
                if (exceptions is null || exceptions is SetCollection) {
                    throw PythonOps.TypeError("second argument (exceptions) must be a sequence");
                }
                PythonTuple items = PythonTuple.Make(exceptions);
                Initialize(message, items);
            }

            private void Initialize(object message, PythonTuple items) {
                if (message is not string) {
                    throw PythonOps.TypeError(
                        "argument 1 must be str, not {0}",
                        PythonOps.GetPythonTypeName(message));
                }
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
                            "Cannot nest BaseExceptions in an {0}",
                            PythonOps.GetPythonTypeName(this));
                    }
                }
                InitializeUnchecked(message, items);
            }

            private void InitializeUnchecked(object message, PythonTuple items) {
                _exceptions = items;
                base.__init__(message, items);
            }

            public object? message => args is PythonTuple tuple && tuple.Count > 0 ? tuple[0] : null;

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
                    group.InitializeUnchecked(message ?? string.Empty, derivedExceptions);
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
                var matching = new List<object>();
                var rest = new List<object>();
                SplitChildren(context, condition, matching, rest);
                return PythonTuple.MakeTuple(
                    matching.Count == 0 ? null : derive(context, PythonTuple.Make(matching)),
                    rest.Count == 0 ? null : derive(context, PythonTuple.Make(rest)));
            }

            public object? subgroup(CodeContext context, [NotNone] object condition) {
                var matching = new List<object>();
                var rest = new List<object>();
                SplitChildren(context, condition, matching, rest);
                return matching.Count == 0 ? null : derive(context, PythonTuple.Make(matching));
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
                return $"{DynamicHelpers.GetPythonType(this).Name}({PythonOps.Repr(context, message)}, [{items}])";
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
