// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;

using IronPython.Runtime.Operations;
using IronPython.Modules;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("lazy_import")]
    public sealed class LazyImport {
        private readonly string _moduleName;
        private readonly string? _memberName;
        private readonly int _level;
        private readonly PythonDictionary _globals;
        private readonly bool _bottom;
        private readonly PythonTuple? _fromList;
        private readonly object _sync = new object();
        private object? _value;

        internal LazyImport(
            string moduleName,
            string? memberName,
            int level,
            PythonDictionary globals,
            bool bottom = false,
            PythonTuple? fromList = null) {
            _moduleName = moduleName;
            _memberName = memberName;
            _level = level;
            _globals = globals;
            _bottom = bottom;
            _fromList = fromList;
        }

        public object resolve(CodeContext context) => Load(context);

        [SpecialName]
        public object GetCustomMember(CodeContext context, [NotNone] string name) {
            if (name == "resolve") {
                return new LazyImportResolveMethod(this);
            }
            return PythonOps.GetBoundAttr(context, Load(context), name);
        }

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

        public object __eq__(CodeContext context, object? other)
            => PythonOps.Equal(context, Load(context), other);

        public object __ne__(CodeContext context, object? other)
            => !PythonOps.IsTrue(PythonOps.Equal(context, Load(context), other));

        public string __repr__(CodeContext context)
            => _memberName is null
                ? $"<lazy import module '{_moduleName}'>"
                : $"<lazy import {_moduleName}.{_memberName}>";

        private object Load(CodeContext context) {
            if (_value is not null) return _value;
            lock (_sync) {
                if (_value is null) {
                    if (_memberName is null) {
                        _value = Importer.ImportWithGlobals(
                            context,
                            _moduleName,
                            _fromList,
                            _level,
                            _globals);
                        if (_fromList is not null) {
                            foreach (object member in _fromList) {
                                if (member is string memberName) {
                                    PythonOps.ImportFrom(
                                        context,
                                        _value,
                                        memberName);
                                }
                            }
                        }
                        if (_bottom && _moduleName.Contains('.')) {
                            string[] parts = _moduleName.Split('.');
                            for (int i = 1; i < parts.Length; i++) {
                                _value = PythonOps.GetBoundAttr(
                                    context,
                                    _value,
                                    parts[i]);
                            }
                        }
                    } else {
                        object module = Importer.ImportWithGlobals(
                            context,
                            _moduleName,
                            PythonTuple.MakeTuple(_memberName),
                            _level,
                            _globals);
                        _value = PythonOps.ImportFrom(context, module, _memberName);
                    }
                    SysModule.UntrackLazyModule(
                        context,
                        _memberName is null
                            ? _moduleName
                            : _moduleName + "." + _memberName);
                }
                return _value;
            }
        }
    }

    public sealed class LazyImportResolveMethod {
        private readonly LazyImport _target;

        internal LazyImportResolveMethod(LazyImport target) {
            _target = target;
        }

        public object __call__(CodeContext context) => _target.resolve(context);

        public string __repr__() =>
            $"<built-in method resolve of lazy_import object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_target):X}>";
    }
}
