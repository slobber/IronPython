// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using IronPython.Modules;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

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
        private readonly PythonDictionary? _capturedLocals;
        private string[] _typeParameterNames = Array.Empty<string>();
        private PythonTuple _typeParameters = PythonTuple.EMPTY;
        private PythonTuple _enclosingTypeParameters = PythonTuple.EMPTY;
        private object? _owner;
        private string _qualname = "__annotate__";
        private int _version;

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

        public AnnotationFunction(
            CodeContext context,
            [NotNone] PythonDictionary locals,
            [NotNone] string[] names,
            [NotNone] string[] sources,
            [NotNone] Func<object?>[] evaluators,
            bool stringOnly = false) {
            _context = context;
            _capturedLocals = locals;
            _names.AddRange(names);
            _sources.AddRange(sources);
            _evaluators.AddRange(evaluators);
            _stringOnly = stringOnly;
        }

        public AnnotationFunction(bool stringOnly = false) {
            _stringOnly = stringOnly;
        }

        internal void Add(string name, string source, Func<object?> evaluator) {
            _names.Add(name);
            _sources.Add(source);
            _evaluators.Add(evaluator);
            _version++;
        }

        internal void SetTypeParameters(string[] names, PythonTuple typeParameters) {
            _typeParameterNames = names;
            _typeParameters = typeParameters;
        }

        internal void SetEnclosingTypeParameters(PythonTuple typeParameters) {
            _enclosingTypeParameters = typeParameters;
        }

        public object? __owner__ {
            get => _owner;
        }

        public PythonDictionary? __captured_locals__ {
            get => _capturedLocals;
        }

        public bool __string_only__ => _stringOnly;

        public int __version__ => _version;

        public void SetOwner([NotNone] object value) => _owner = value;

        public string __qualname__ {
            get => _qualname;
            internal set => _qualname = value;
        }

        public string __name__ => "__annotate__";

        public object __signature__ {
            get {
                object inspect = Importer.ImportModule(
                    _context!,
                    null,
                    "inspect",
                    false,
                    0);
                object signature = PythonOps.GetBoundAttr(
                    _context!,
                    inspect,
                    "Signature");
                object parameter = PythonOps.GetBoundAttr(
                    _context!,
                    inspect,
                    "Parameter");
                object positionalOnly = PythonOps.GetBoundAttr(
                    _context!,
                    parameter,
                    "POSITIONAL_ONLY");
                object formatParameter = PythonCalls.Call(
                    _context!,
                    parameter,
                    "format",
                    positionalOnly)!;
                return PythonCalls.Call(
                    _context!,
                    signature,
                    PythonTuple.MakeTuple(formatParameter))!;
            }
        }

        public object __code__ => new AnnotationCodeInfo();

        [PythonType("annotation_code")]
        public sealed class AnnotationCodeInfo {
            public PythonTuple co_freevars => PythonTuple.EMPTY;
        }

        [SpecialName]
        public object Call(CodeContext context, [NotNone] object format) {
            int annotationFormat = Converter.ConvertToInt32(format);
            if (annotationFormat != 1) {
                throw PythonOps.NotImplementedError(
                    "unsupported annotation format: {0}",
                    annotationFormat);
            }
            return Evaluate(annotationFormat);
        }

        public PythonDictionary _evaluate_format(int annotationFormat) =>
            Evaluate(annotationFormat);

        public PythonDictionary _evaluate_values() =>
            Evaluate(1, forceValues: true);

        internal PythonDictionary Evaluate(int annotationFormat = 1, bool forceValues = false) {
            PythonDictionary annotations = new();
            if (_stringOnly && !forceValues) {
                for (int i = 0; i < _names.Count; i++) {
                    annotations.Add(
                        _names[i],
                        NormalizeAnnotationSource(_sources[i]));
                }
                return annotations;
            }
            PythonDictionary? locals = null;
            if (_context is not null
                && (_capturedLocals is not null || _typeParameters.Count != 0)) {
                locals = new PythonDictionary();
                PythonDictionary scopeLocals = _capturedLocals ?? _context.Dict;
                foreach (object key in scopeLocals.Keys) {
                    if (scopeLocals.TryGetValueNoMissing(key, out object? value)) {
                        if (value is ClosureCell cell) {
                            if (cell.Value == Uninitialized.Instance) {
                                continue;
                            }
                            value = cell.Value;
                        }
                        locals[key] = value;
                        if (value is not null
                            && (DynamicHelpers.GetPythonType(value).Name is
                                "TypeVar" or "TypeVarTuple" or "ParamSpec")
                            && PythonOps.TryGetBoundAttr(
                                _context,
                                value,
                                "__name__",
                                out object? parameterName)
                            && parameterName is string name) {
                            locals[name] = value;
                        }
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
                    for (int i = 0; i < _names.Count; i++) {
                        object? value = EvaluateAnnotation(i, locals);
                        if (value is LazyImport lazyImport && _context is not null) {
                            value = lazyImport.resolve(_context);
                        }
                        annotations.Add(
                            _names[i],
                            value);
                    }
                    return annotations;
                case 3:
                    for (int i = 0; i < _names.Count; i++) {
                        try {
                            object? value = EvaluateAnnotation(i, locals);
                            if (value is LazyImport lazyImport && _context is not null) {
                                value = lazyImport.resolve(_context);
                            }
                            annotations.Add(
                                _names[i],
                                value);
                        } catch (Exception) {
                            annotations.Add(
                                _names[i],
                                MakeForwardRef(_sources[i], _evaluators[i]));
                        }
                    }
                    return annotations;
                case 4:
                    for (int i = 0; i < _names.Count; i++) {
                        annotations.Add(
                            _names[i],
                            NormalizeAnnotationSource(_sources[i]));
                    }
                    return annotations;
                default:
                    throw PythonOps.NotImplementedError("unsupported annotation format: {0}", annotationFormat);
            }
        }

        private object? EvaluateAnnotation(int index, PythonDictionary? locals) {
            string source = _sources[index];
            Func<object?>? evaluator = _evaluators[index];
            if (evaluator is not null) {
                try {
                    return LightExceptions.CheckAndThrow(evaluator());
                } catch (UnboundLocalException error) {
                    string message = error.Message;
                    int start = message.IndexOf('\'');
                    int end = start >= 0 ? message.IndexOf('\'', start + 1) : -1;
                    if (start >= 0 && end > start + 1) {
                        string name = message.Substring(start + 1, end - start - 1);
                        throw new UnboundNameException(
                            $"cannot access free variable '{name}' where it is not associated with a value in enclosing scope",
                            error);
                    }
                    throw;
                }
            }
            if (source.StartsWith('*')) {
                string unpackedSource = "(*(" + source.Substring(1) + "),)[0]";
                return LightExceptions.CheckAndThrow(Builtin.eval(
                    _context!,
                    unpackedSource,
                    BuildEvalGlobals(),
                    locals ?? _context!.Dict));
            }
            object? value = TryGetTypeParameter(source, out object? typeParameter)
                ? typeParameter
                : evaluator is not null
                    ? evaluator()
                    : Builtin.eval(
                        _context!,
                        source,
                        BuildEvalGlobals(),
                        locals ?? _context!.Dict);
            return LightExceptions.CheckAndThrow(value);
        }

        private object MakeForwardRef(string source, Func<object?>? evaluator) {
            object annotationlib = Importer.ImportModule(
                _context!,
                null,
                "annotationlib",
                false,
                0);
            object constructor = PythonOps.GetBoundAttr(
                _context!,
                annotationlib,
                "ForwardRef");
            object result = PythonCalls.Call(_context!, constructor, source)!;
            if (_owner is not null) {
                PythonOps.SetAttr(_context!, result, "__owner__", _owner);
                if (_owner is PythonType) {
                    PythonOps.SetAttr(_context!, result, "__forward_is_class__", true);
                }
            }
            if (evaluator is not null) {
                PythonOps.SetAttr(_context!, result, "__evaluator__", evaluator);
            }
            return result;
        }

        private string NormalizeAnnotationSource(string source) {
            if (_context is null) return source;
            string trimmed = source.Trim();
            if (trimmed == "...") {
                return "...";
            }
            if (trimmed.Length != 0
                && (char.IsDigit(trimmed[0])
                    || trimmed[0] is '\'' or '"' or '-' or '.'
                    || trimmed is "True" or "False" or "None" or "..."
                    || trimmed.StartsWith("b'", StringComparison.Ordinal)
                    || trimmed.StartsWith("b\"", StringComparison.Ordinal))) {
                try {
                    object literal = LightExceptions.CheckAndThrow(
                        Builtin.eval(
                            _context,
                            trimmed,
                            _context.GlobalDict,
                            _context.Dict))!;
                    if (!_stringOnly && literal is string stringLiteral) {
                        return stringLiteral;
                    }
                    if (literal is Ellipsis) {
                        return "...";
                    }
                    return PythonOps.Repr(_context, literal);
                } catch (Exception) {
                    // Fall through to syntax normalization.
                }
            }
            try {
                object ast = Importer.ImportModule(
                    _context,
                    null,
                    "ast",
                    false,
                    0);
                object parse = PythonOps.GetBoundAttr(_context, ast, "parse");
                object module = PythonCalls.Call(_context, parse, source)!;
                object unparse = PythonOps.GetBoundAttr(_context, ast, "unparse");
                return PythonCalls.Call(_context, unparse, module) as string ?? source;
            } catch (Exception) {
                return source;
            }
        }

        private PythonDictionary BuildEvalGlobals() {
            if (_context is null || _enclosingTypeParameters.Count == 0) {
                return _context!.GlobalDict;
            }
            var globals = new PythonDictionary();
            foreach (object key in _context.GlobalDict.Keys) {
                if (_context.GlobalDict.TryGetValueNoMissing(key, out object? value)) {
                    globals[key] = value;
                }
            }
            foreach (object parameter in _enclosingTypeParameters) {
                if (PythonOps.TryGetBoundAttr(
                        _context,
                        parameter,
                        "__name__",
                        out object? name)
                    && name is string parameterName) {
                    globals[parameterName] = parameter;
                }
            }
            return globals;
        }

        private bool TryGetTypeParameter(string source, out object? parameter) {
            string name = source.Trim();
            for (int i = 0; i < _typeParameters.Count; i++) {
                object current = _typeParameters[i]!;
                if (i < _typeParameterNames.Length
                    && string.Equals(_typeParameterNames[i], name, StringComparison.Ordinal)) {
                    parameter = current;
                    return true;
                }
                if (PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        current,
                        "__name__",
                        out object? parameterName)
                    && parameterName is string typeParameterName
                    && string.Equals(typeParameterName, name, StringComparison.Ordinal)) {
                    parameter = current;
                    return true;
                }
            }
            parameter = null;
            return false;
        }
    }
}
