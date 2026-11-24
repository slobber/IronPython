// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Generic;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("sentinel")]
    public sealed class SentinelObject : ICodeFormattable {
        private readonly string? _repr;

        private SentinelObject(string name, object? module, string? repr) {
            __name__ = name;
            __module__ = module;
            _repr = repr;
        }

        public string __name__ { get; }

        public object? __module__ { get; set; }

        public static SentinelObject __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] string name) {
            return Create(context, cls, name, null);
        }

        public static SentinelObject __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] string name,
            [ParamDictionary] IDictionary<object, object> keywordArguments) {
            string? repr = null;
            if (keywordArguments.TryGetValue("repr", out object? rawRepr)) {
                if (rawRepr is not null && rawRepr is not string) {
                    throw PythonOps.TypeError(
                        "sentinel() argument 'repr' must be str or None");
                }
                repr = rawRepr as string;
            }
            return Create(context, cls, name, repr);
        }

        private static SentinelObject Create(
            CodeContext context,
            PythonType cls,
            string name,
            string? repr) {
            if (cls != TypeCache.Sentinel) {
                throw PythonOps.TypeError("sentinel cannot be subclassed");
            }
            context.GlobalDict.TryGetValue("__name__", out object? module);
            return new SentinelObject(name, module, repr);
        }

        public SentinelObject __copy__() => this;

        public SentinelObject __deepcopy__([NotNone] object memo) => this;

        public string __reduce__() => __name__;

        public string __repr__(CodeContext context) => _repr ?? __name__;
    }
}
