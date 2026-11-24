// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Runtime.CompilerServices;

using IronPython.Runtime.Operations;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("LazyImportModule")]
    public sealed class LazyImportModule {
        private readonly string _name;
        private readonly int _level;
        private object? _module;
        private readonly object _sync = new object();

        internal LazyImportModule(string name, int level) {
            _name = name;
            _level = level;
        }

        public object __getattribute__(CodeContext context, [NotNone] string name) {
            return PythonOps.GetBoundAttr(context, Load(context), name);
        }

        [SpecialName]
        public object GetCustomMember(CodeContext context, [NotNone] string name) {
            return PythonOps.GetBoundAttr(context, Load(context), name);
        }

        public string __repr__(CodeContext context) {
            return $"<lazy import module '{_name}'>";
        }

        private object Load(CodeContext context) {
            if (_module is not null) return _module;
            lock (_sync) {
                _module ??= _level == 0
                    ? PythonOps.ImportTop(context, _name, 0)
                    : PythonOps.ImportTop(context, _name, _level);
                return _module;
            }
        }
    }
}
