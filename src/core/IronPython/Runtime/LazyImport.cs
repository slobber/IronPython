// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;

using IronPython.Runtime.Operations;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("lazy_import")]
    public sealed class LazyImport {
        private readonly string _moduleName;
        private readonly string? _memberName;
        private readonly int _level;
        private readonly object _sync = new object();
        private object? _value;

        internal LazyImport(string moduleName, string? memberName, int level) {
            _moduleName = moduleName;
            _memberName = memberName;
            _level = level;
        }

        public object resolve(CodeContext context) => Load(context);

        public object __getattribute__(CodeContext context, [NotNone] string name)
            => PythonOps.GetBoundAttr(context, Load(context), name);

        [SpecialName]
        public object GetCustomMember(CodeContext context, [NotNone] string name)
            => PythonOps.GetBoundAttr(context, Load(context), name);

        public object __call__(
            CodeContext context,
            [NotNone] params object?[] args)
            => PythonCalls.Call(context, Load(context), args)!;

        public object __call__(
            CodeContext context,
            [ParamDictionary] IDictionary<object, object> keywordArguments,
            [NotNone] params object?[] args)
            => PythonCalls.CallWithKeywordArgs(
                context,
                Load(context),
                args,
                keywordArguments)!;

        public string __repr__(CodeContext context)
            => _memberName is null
                ? $"<lazy import module '{_moduleName}'>"
                : $"<lazy import {_moduleName}.{_memberName}>";

        private object Load(CodeContext context) {
            if (_value is not null) return _value;
            lock (_sync) {
                if (_value is null) {
                    if (_memberName is null) {
                        _value = PythonOps.ImportTop(context, _moduleName, _level);
                    } else {
                        object module = PythonOps.ImportWithNames(
                            context,
                            _moduleName,
                            new[] { _memberName },
                            _level);
                        _value = PythonOps.ImportFrom(context, module, _memberName);
                    }
                }
                return _value;
            }
        }
    }
}
