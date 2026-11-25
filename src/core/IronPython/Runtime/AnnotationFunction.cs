// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using IronPython.Modules;
using IronPython.Runtime.Operations;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    /// <summary>
    /// Lazily evaluates function annotations in the scope that defined the function.
    /// </summary>
    [PythonType("annotation_function")]
    public sealed class AnnotationFunction {
        private readonly List<string> _names = new();
        private readonly List<string> _sources = new();
        private readonly List<Func<object?>> _evaluators = new();
        private readonly bool _stringOnly;
        private readonly CodeContext? _context;
        private string[] _typeParameterNames = Array.Empty<string>();
        private PythonTuple _typeParameters = PythonTuple.EMPTY;

        public AnnotationFunction(
            [NotNone] string[] names,
            [NotNone] string[] sources,
            [NotNone] Func<object?>[] evaluators,
            bool stringOnly = false) {
            _names.AddRange(names);
            _sources.AddRange(sources);
            _evaluators.AddRange(evaluators);
            _stringOnly = stringOnly;
        }

        public AnnotationFunction(
            CodeContext context,
            [NotNone] string[] names,
            [NotNone] string[] sources,
            bool stringOnly = false) {
            _context = context;
            _names.AddRange(names);
            _sources.AddRange(sources);
            _evaluators.AddRange(new Func<object?>[names.Length]);
            _stringOnly = stringOnly;
        }

        public AnnotationFunction(bool stringOnly = false) {
            _stringOnly = stringOnly;
        }

        internal void Add(string name, string source, Func<object?> evaluator) {
            _names.Add(name);
            _sources.Add(source);
            _evaluators.Add(evaluator);
        }

        internal void SetTypeParameters(string[] names, PythonTuple typeParameters) {
            _typeParameterNames = names;
            _typeParameters = typeParameters;
        }

        [SpecialName]
        public object Call(CodeContext context, [NotNone] object format) {
            return Evaluate(Converter.ConvertToInt32(format));
        }

        internal PythonDictionary Evaluate(int annotationFormat = 1) {
            PythonDictionary annotations = new();
            if (_stringOnly) {
                for (int i = 0; i < _names.Count; i++) {
                    annotations.Add(_names[i], _sources[i]);
                }
                return annotations;
            }
            PythonDictionary? locals = null;
            if (_context is not null && _typeParameters.Count != 0) {
                locals = new PythonDictionary();
                foreach (object key in _context.Dict.Keys) {
                    if (_context.Dict.TryGetValueNoMissing(key, out object? value)) {
                        locals[key] = value;
                    }
                }
                for (int i = 0; i < _typeParameters.Count; i++) {
                    object parameter = _typeParameters[i]!;
                    if (PythonOps.TryGetBoundAttr(
                            _context,
                            parameter,
                            "__name__",
                            out object? name)
                        && name is string parameterName) {
                        locals[parameterName] = parameter;
                    }
                    if (i < _typeParameterNames.Length) {
                        locals[_typeParameterNames[i]] = parameter;
                    }
                }
            }
            switch (annotationFormat) {
                case 1:
                case 2:
                case 3:
                    for (int i = 0; i < _names.Count; i++) {
                        Func<object?>? evaluator = _evaluators[i];
                        object? value = evaluator is not null
                            ? evaluator()
                            : Builtin.eval(
                                _context!,
                                _sources[i],
                                _context!.GlobalDict,
                                locals ?? _context.Dict);
                        annotations.Add(_names[i], value);
                    }
                    return annotations;
                case 4:
                    for (int i = 0; i < _names.Count; i++) {
                        annotations.Add(_names[i], _sources[i]);
                    }
                    return annotations;
                default:
                    throw PythonOps.NotImplementedError("unsupported annotation format: {0}", annotationFormat);
            }
        }
    }
}
