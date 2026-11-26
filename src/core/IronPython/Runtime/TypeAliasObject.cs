// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Runtime.CompilerServices;
using System.Linq;
using System.Collections.Generic;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("TypeAliasType")]
    public sealed class TypeAliasObject : IWeakReferenceable {
        private WeakRefTracker? _weakref;
        private readonly string _name;
        private readonly string _qualname;
        private object? _module;
        private readonly object? _thunk;
        private readonly PythonTuple _typeParameters;
        private readonly CodeContext? _context;
        private readonly string? _source;
        private readonly bool _useSource;
        private readonly PythonTuple _enclosingTypeParameters;
        private readonly bool _includeClassScope;
        private readonly bool _cacheValue;
        private readonly bool _evaluateThunk;
        private object? _classOwner;
        private object? _value;

        internal TypeAliasObject(
            string name,
            string qualname,
            object? module,
            object? thunk,
            PythonTuple typeParameters,
            CodeContext? context = null,
            string? source = null,
            bool useSource = false,
            PythonTuple? enclosingTypeParameters = null,
            bool includeClassScope = true,
            bool cacheValue = true,
            bool evaluateThunk = true) {
            _name = name;
            _qualname = qualname;
            _module = module;
            _thunk = thunk;
            _typeParameters = typeParameters;
            _context = context;
            _source = source;
            _useSource = useSource;
            _enclosingTypeParameters = enclosingTypeParameters ?? PythonTuple.EMPTY;
            _includeClassScope = includeClassScope;
            _cacheValue = cacheValue;
            _evaluateThunk = evaluateThunk;
            if (context is not null) {
                PythonOps.ConfigureTypeParameterExpressions(context, typeParameters);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "IPY01",
            Justification = "The setter deliberately raises a Python-facing read-only error.")]
        public string __name__ {
            get => _name;
            set => throw PythonOps.AttributeError("readonly attribute '__name__'");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "IPY01",
            Justification = "The setter deliberately raises a Python-facing read-only error.")]
        public string __qualname__ {
            get => _qualname;
            set => throw PythonOps.AttributeError("readonly attribute '__qualname__'");
        }

        public object? __module__ {
            get => _module;
            set => _module = value;
        }

        public void __setattr__(
            CodeContext context,
            [NotNone] string name,
            [NotNone] object value) {
            if (name == "__module__") {
                _module = value;
                return;
            }
            throw PythonOps.TypeError("readonly attribute '{0}'", name);
        }

        public PythonTuple __type_params__ => _typeParameters;

        public PythonTuple __parameters__ {
            get {
                var parameters = new System.Collections.Generic.List<object?>();
                foreach (object parameter in _typeParameters) {
                    if (DynamicHelpers.GetPythonType(parameter).Name == "TypeVarTuple") {
                        var enumerator = PythonOps.GetEnumerator(parameter);
                        while (enumerator.MoveNext()) {
                            parameters.Add(enumerator.Current);
                        }
                    } else {
                        parameters.Add(parameter);
                    }
                }
                return PythonTuple.MakeTuple(parameters.ToArray());
            }
        }

        public object __getitem__([NotNone] object parameters) {
            object?[] arguments = parameters is PythonTuple tuple
                ? tuple.ToArray()
                : new[] { parameters };
            if (_typeParameters.Count == 0) {
                throw PythonOps.TypeError("Only generic type aliases are subscriptable");
            }
            return new GenericAlias(
                this,
                GenericAlias.NormalizeArguments(PythonTuple.MakeTuple(arguments)),
                false);
        }

        public object __or__(object? other) => UnionType.MakeUnion(this, other);

        public object __ror__(object? other) => UnionType.MakeUnion(other, this);

        public GenericAliasIterator __iter__(CodeContext context) {
            object typing = Importer.ImportModule(context, null, "typing", false, 0);
            object unpack = PythonOps.GetBoundAttr(context, typing, "Unpack");
            object getItem = PythonOps.GetBoundAttr(context, unpack, "__getitem__");
            return new GenericAliasIterator(PythonCalls.Call(context, getItem, this));
        }

        [SpecialName]
        public object? GetCustomMember(CodeContext context, [NotNone] string name) {
            if (name == "__value__") {
                if (!_evaluateThunk
                    && _source is not null
                    && _source.TrimStart().StartsWith(
                        "lambda",
                        System.StringComparison.Ordinal)) {
                    return _value ??= new TypeAliasLambda(this, context);
                }
                return EvaluateWithCaching(context);
            }
            if (name == "evaluate_value") {
                return new TypeAliasEvaluator(this, context);
            }
            return OperationFailed.Value;
        }

        public object __repr__(CodeContext context) =>
            _qualname;

        internal object EvaluateValue(int format = 1) {
            CodeContext context = _context ?? DefaultContext.Default;
            object? value = EvaluateWithCaching(context);
            if (format == 4) {
                object annotationlib = Importer.ImportModule(
                    DefaultContext.Default,
                    null,
                    "annotationlib",
                    false,
                    0);
                object typeRepr = PythonOps.GetBoundAttr(
                    DefaultContext.Default,
                    annotationlib,
                    "type_repr");
                return PythonCalls.Call(DefaultContext.Default, typeRepr, value)!;
            }
            return value!;
        }

        internal void SetClassOwner(object owner) {
            _classOwner = owner;
        }

        internal object? EvaluateLambda(CodeContext context) {
            if (_context is null || _source is null) {
                return Evaluate(context);
            }
            return LightExceptions.CheckAndThrow(PythonOps.EvalAnnotationSource(
                _context,
                _source,
                _typeParameters,
                GetEnclosingTypeParameters(context),
                _includeClassScope,
                _classOwner));
        }

        private PythonTuple GetEnclosingTypeParameters(CodeContext context) {
            if (_classOwner is not null
                && PythonOps.TryGetBoundAttr(
                    context,
                    _classOwner,
                    "__type_params__",
                    out object? ownerTypeParameters)
                && ownerTypeParameters is PythonTuple ownerParameters) {
                return ownerParameters;
            }
            return _enclosingTypeParameters;
        }

        private object? Evaluate(CodeContext context) =>
            _useSource && _context is not null && _source is not null
                ? LightExceptions.CheckAndThrow(PythonOps.EvalAnnotationSource(
                    _context,
                    _source,
                    _typeParameters,
                    GetEnclosingTypeParameters(context),
                    _includeClassScope,
                    _classOwner))
                : _evaluateThunk
                    ? PythonCalls.Call(
                        context,
                        _thunk,
                        _typeParameters.ToArray())
                    : _thunk;

        private object? EvaluateWithCaching(CodeContext context) {
            if (!_cacheValue || _includeClassScope) {
                return Evaluate(context);
            }
            return _value ??= Evaluate(context);
        }


        public object __reduce_ex__(CodeContext context, int protocol) {
            if (_module is string moduleName
                && moduleName.Length != 0
                && !_qualname.Contains('.', System.StringComparison.Ordinal)) {
                return _qualname;
            }

            object picklingError = Importer.ImportModule(context, null, "pickle", false, 0);
            object errorType = PythonOps.GetBoundAttr(context, picklingError, "PicklingError");
            object error = PythonCalls.Call(
                context,
                errorType,
                $"Can't pickle {this}: it's not found as a module-level global")!;
            throw PythonOps.MakeException(context, error);
        }

        public object __reduce__(CodeContext context) => __reduce_ex__(context, 0);

        WeakRefTracker? IWeakReferenceable.GetWeakRef() => _weakref;

        bool IWeakReferenceable.SetWeakRef(WeakRefTracker value) {
            _weakref = value;
            return true;
        }

        void IWeakReferenceable.SetFinalizer(WeakRefTracker value) =>
            ((IWeakReferenceable)this).SetWeakRef(value);
    }

    [PythonType("_type_alias_lambda")]
    public sealed class TypeAliasLambda {
        private readonly TypeAliasObject _alias;
        private readonly CodeContext _context;

        internal TypeAliasLambda(TypeAliasObject alias, CodeContext context) {
            _alias = alias;
            _context = context;
        }

        public object? __call__(
            CodeContext context,
            [ParamDictionary] IDictionary<object, object> kwargs,
            [NotNone] params object[] args) {
            object callable = _alias.EvaluateLambda(_context)!;
            return PythonCalls.CallWithKeywordArgs(
                _context,
                callable,
                args,
                kwargs);
        }

        public string __repr__() => "<type alias lambda>";
    }

    [PythonType("_TypeAliasEvaluator")]
    public sealed class TypeAliasEvaluator {
        private readonly TypeAliasObject _alias;
        private readonly CodeContext _context;

        internal TypeAliasEvaluator(TypeAliasObject alias, CodeContext context) {
            _alias = alias;
            _context = context;
        }

        public object __call__(int format = 1) => _alias.EvaluateValue(format);

        public object __signature__ {
            get {
                object inspect = Importer.ImportModule(_context, null, "inspect", false, 0);
                object parameterType = PythonOps.GetBoundAttr(_context, inspect, "Parameter");
                object positionalOnly = PythonOps.GetBoundAttr(
                    _context,
                    parameterType,
                    "POSITIONAL_ONLY");
                object parameter = PythonCalls.CallWithKeywordArgs(
                    _context,
                    parameterType,
                    new object?[] { "format", positionalOnly, 1 },
                    new[] { "default" })!;
                object signatureType = PythonOps.GetBoundAttr(_context, inspect, "Signature");
                return PythonCalls.Call(
                    _context,
                    signatureType,
                    PythonTuple.MakeTuple(parameter))!;
            }
        }
    }
}
