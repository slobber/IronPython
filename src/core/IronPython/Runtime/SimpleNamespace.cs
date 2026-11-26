// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonHidden, PythonType("types.SimpleNamespace")]
    public class SimpleNamespace {
        public SimpleNamespace() {
            __dict__ = new PythonDictionary();
        }

        public SimpleNamespace([ParamDictionary] Dictionary<string, object?> kwargs) {
            __dict__ = new PythonDictionary(kwargs);
        }

        public SimpleNamespace(
            CodeContext context,
            [ParamDictionary] IDictionary<object, object> kwargs,
            [NotNone] params object[] args) {
            if (args.Length > 1) {
                throw PythonOps.TypeError(
                    "SimpleNamespace expected at most 1 argument, got {0}",
                    args.Length);
            }
            __dict__ = new PythonDictionary();
            if (args.Length == 1) {
                __dict__.update(context, args[0]);
            }
            __dict__.update(context, kwargs);
            foreach (object? key in __dict__.Keys) {
                if (key is not string) {
                    throw PythonOps.TypeError(
                        "attribute name must be string, not '{0}'",
                        PythonOps.GetPythonTypeName(key));
                }
            }
        }

        public PythonDictionary __dict__ { get; }

        [SpecialName]
        public object GetCustomMember([NotNone] string name) {
            return __dict__.get(name, OperationFailed.Value);
        }

        [SpecialName]
        public void SetMember([NotNone] string name, object? value) {
            __dict__[name] = value;
        }

        [SpecialName]
        public void DeleteMember([NotNone] string name) {
            if (!__dict__.RemoveDirect(name)) {
                throw PythonOps.AttributeError(name);
            }
        }

        public string __repr__(CodeContext context) {
            var infinite = PythonOps.GetAndCheckInfinite(this);
            if (infinite == null) {
                return "namespace(...)";
            }

            int index = infinite.Count;
            infinite.Add(this);
            try {
                var attrs = __dict__.Select(item =>
                    $"{PythonOps.ToString(context, item.Key)}={PythonOps.Repr(context, item.Value)}");
                return $"namespace({string.Join(", ", attrs)})";
            } finally {
                System.Diagnostics.Debug.Assert(index == infinite.Count - 1);
                infinite.RemoveAt(index);
            }
        }

        public bool __eq__(CodeContext context, [NotNone] SimpleNamespace other)
            => PythonOps.IsOrEqualsRetBool(__dict__, other.__dict__);

        [return: MaybeNotImplemented]
        public object __eq__(CodeContext context, object? other) {
            if (other is SimpleNamespace simpleNamespace)
                return __eq__(context, simpleNamespace);
            return NotImplementedType.Value;
        }

        public object __replace__(
            CodeContext context,
            [ParamDictionary] IDictionary<object, object> changes) {
            var values = new PythonDictionary(__dict__);
            values.update(context, changes);
            PythonType cls = DynamicHelpers.GetPythonType(this);
            object? result = PythonCalls.CallWithKeywordArgs(
                context,
                cls,
                System.Array.Empty<object?>(),
                values);
            if (result is not SimpleNamespace) {
                string? module = PythonType.Get__module__(context, cls) as string;
                string typeName = string.IsNullOrEmpty(module)
                    ? cls.QualName
                    : module + "." + cls.QualName;
                throw PythonOps.TypeError(
                    "expect types.SimpleNamespace type, but {0}() returned '{1}' object",
                    typeName,
                    PythonOps.GetPythonTypeName(result));
            }
            return result;
        }

        public PythonTuple __reduce__(CodeContext context) {
            object typesModule = Importer.ImportModule(
                context,
                null,
                "types",
                false,
                0);
            object reconstruct = PythonOps.GetBoundAttr(
                context,
                typesModule,
                "_reconstruct_simple_namespace");
            return PythonTuple.MakeTuple(
                reconstruct,
                PythonTuple.MakeTuple(__dict__));
        }
    }
}
