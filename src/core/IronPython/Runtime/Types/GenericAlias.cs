// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using IronPython.Runtime;
using IronPython.Runtime.Operations;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime.Types {
    [PythonType("GenericAlias")]
    public class GenericAlias : ICodeFormattable, IPythonMembersList, IWeakReferenceable {
        private bool _unpacked;
        private PythonTuple? _parameters;
        private WeakRefTracker? _weakref;

        protected GenericAlias() {
            __origin__ = TypeCache.Object;
            __args__ = PythonTuple.MakeTuple(Array.Empty<object?>());
        }

        public GenericAlias([NotNone] PythonType origin, [NotNone] params object?[] arguments)
            : this((object)origin, arguments) { }

        public GenericAlias([NotNone] object origin, [NotNone] params object?[] arguments) {
            __origin__ = origin;
            PythonTuple tupleArguments = arguments.Length == 0
                ? PythonTuple.MakeTuple(new object?[] { PythonTuple.EMPTY })
                : arguments.Length == 1 && arguments[0] is PythonTuple tuple
                    ? tuple
                    : PythonTuple.MakeTuple(arguments);
            __args__ = tupleArguments;
        }

        public GenericAlias(
            [NotNone] object origin,
            [NotNone] PythonTuple arguments,
            bool unpacked)
            : this(origin, arguments.ToArray()) {
            _unpacked = unpacked;
        }

        private GenericAlias(
            [NotNone] object origin,
            [NotNone] PythonTuple arguments,
            bool unpacked,
            bool normalized) {
            __origin__ = origin;
            __args__ = arguments;
            _unpacked = unpacked;
        }

        public object __origin__ { get; }

        public PythonTuple __args__ { get; }

        public bool __unpacked__ => _unpacked;

        public object? __typing_unpacked_tuple_args__ {
            get {
                if (!_unpacked) return null;
                if (__origin__ is PythonType origin && origin.Name is "tuple") {
                    if (__args__.Count == 1
                        && __args__[0] is PythonTuple emptyTuple
                        && emptyTuple.Count == 0) {
                        return PythonTuple.EMPTY;
                    }
                    return __args__;
                }
                if (PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        __origin__,
                        "__typing_unpacked_tuple_args__",
                        out object? nestedUnpacked)
                    && nestedUnpacked is PythonTuple nestedUnpackedTuple) {
                    return nestedUnpackedTuple;
                }
                if (PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        __origin__,
                        "__origin__",
                        out object? nestedOrigin)
                    && nestedOrigin is PythonType nestedType
                    && nestedType.Name is "tuple"
                    && PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        __origin__,
                        "__args__",
                        out object? nestedArguments)
                    && nestedArguments is PythonTuple nestedTuple) {
                    return nestedTuple;
                }
                throw PythonOps.TypeError(
                    "Unpack[...] must be used with a tuple type");
            }
        }

        public PythonTuple __parameters__ {
            get {
                if (_parameters is null) {
                    var parameters = new List<object>();
                    AddParameters(parameters, __args__);
                    _parameters = PythonTuple.MakeTuple(parameters.ToArray());
                }
                return _parameters;
            }
        }

        public object __getitem__([NotNone] object item) {
            object?[] arguments = item is PythonTuple tuple
                ? tuple.ToArray()
                : new[] { item };
            PythonTuple normalized = NormalizeArguments(PythonTuple.MakeTuple(arguments));
            PythonTuple parameters = __parameters__;
            if (parameters.Count == 0) {
                throw PythonOps.TypeError(
                    "{0} is not a generic class",
                    PythonOps.Repr(DefaultContext.Default, this));
            }
            if (parameters.Count(IsTypeVarTuple) > 1) {
                throw PythonOps.TypeError(
                    "More than one TypeVarTuple parameter in {0}",
                    PythonOps.Repr(DefaultContext.Default, this));
            }
            if (parameters.Count > 1
                && parameters.Any(IsParamSpec)
                && !parameters.Any(IsTypeVarTuple)
                && !__args__.Any(IsConcatenateAlias)
                && normalized.Count != parameters.Count) {
                throw PythonOps.TypeError(
                    "{0} arguments for {1}; actual {2}, expected {3}",
                    normalized.Count > parameters.Count ? "Too many" : "Too few",
                    PythonOps.Repr(DefaultContext.Default, this),
                    normalized.Count,
                    parameters.Count);
            }
            if (parameters.Any(parameter =>
                    IsTypeVarTuple(parameter)
                    && __args__.Any(argument =>
                        PythonOps.EqualRetBool(argument, parameter)))) {
                throw PythonOps.TypeError(
                    "TypeVarTuple must be unpacked in a generic alias");
            }
            PythonTuple replacements = normalized;
            if (parameters.Any(IsTypeVarTuple) || normalized.Count != parameters.Count) {
                replacements = BindReplacements(parameters, normalized);
            }
            var specialized = new List<object?>();
            foreach (object? argument in __args__) {
                int parameterIndex = FindParameterIndex(argument, parameters);
                if (parameterIndex < 0
                    && argument is not null
                    && PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        argument,
                        "__args__",
                        out object? unpackedArguments)
                    && unpackedArguments is PythonTuple unpackedTuple
                    && unpackedTuple.Count == 1) {
                    parameterIndex = FindParameterIndex(unpackedTuple[0], parameters);
                }
                if (parameterIndex >= 0
                    && IsTypeVarTuple(parameters[parameterIndex])
                    && replacements[parameterIndex] is PythonTuple expansion) {
                    specialized.AddRange(expansion);
                } else {
                    specialized.Add(Substitute(argument, parameters, replacements));
                }
            }
            return new GenericAlias(
                __origin__,
                PythonTuple.MakeTuple(specialized.ToArray()),
                false,
                true);
        }

        public object __getitem__(
            CodeContext context,
            [NotNone] object instance,
            [NotNone] object item) {
            object getItem = PythonOps.GetBoundAttr(context, __origin__, "__getitem__");
            return PythonCalls.Call(context, getItem, instance, item)!;
        }

        public object __call__(CodeContext context, [NotNone] params object?[] args) {
            object result = PythonCalls.Call(context, __origin__, args)!;
            try {
                PythonOps.SetAttr(context, result, "__orig_class__", this);
            } catch (Exception) {
                // Instances without a writable dictionary cannot retain the alias.
            }
            return result;
        }

        public object __call__(
            CodeContext context,
            [ParamDictionary] IDictionary<object, object> keywordArgs,
            [NotNone] params object?[] args) {
            object result = PythonCalls.CallWithKeywordArgs(context, __origin__, args, keywordArgs)!;
            try {
                PythonOps.SetAttr(context, result, "__orig_class__", this);
            } catch (Exception) {
                // Instances without a writable dictionary cannot retain the alias.
            }
            return result;
        }

        [SpecialName]
        public object? GetCustomMember(CodeContext context, [NotNone] string name) {
            if (name is "__args__" or "__bases__" or "__call__" or "__copy__" or "__deepcopy__"
                or "__eq__" or "__getitem__"
                or "__instancecheck__" or "__subclasscheck__"
                or "__hash__" or "__iter__" or "__mro_entries__" or "__ne__"
                or "__or__" or "__origin__" or "__parameters__" or "__reduce__"
                or "__reduce_ex__" or "__repr__" or "__ror__" or "__unpacked__") {
                return OperationFailed.Value;
            }
            if (__origin__ is not null) {
                if (PythonOps.TryGetBoundAttr(context, __origin__, name, out object? value)) {
                    return value;
                }
            }
            return OperationFailed.Value;
        }

        public PythonTuple __mro_entries__([NotNone] object bases) =>
            PythonTuple.MakeTuple(__origin__);

        public GenericAliasIterator __iter__() =>
            new GenericAliasIterator(new GenericAlias(__origin__, __args__, true));

        public object __or__(object? other) {
            return UnionType.MakeUnion(this, other);
        }

        public object __ror__(object? other) {
            return UnionType.MakeUnion(other, this);
        }

        public string __repr__(CodeContext context) {
            string arguments;
            PythonType? originType = __origin__ as PythonType;
            bool isCollectionsCallable = originType?.Name is "Callable";
            if (isCollectionsCallable
                && __args__.Count != 0
                && !IsUnbracketedCallableParameter(__args__[0])) {
                int parameterCount = __args__.Count - 1;
                string parameters = string.Join(
                    ", ",
                    Enumerable.Range(0, parameterCount)
                        .Select(index => FormatAliasArgument(context, __args__[index])));
                arguments = $"[{parameters}]";
                if (parameterCount < __args__.Count) {
                    arguments += ", " + FormatAliasArgument(context, __args__[__args__.Count - 1]);
                }
            } else {
                arguments = __args__.Count == 0
                    ? "()"
                    : string.Join(
                        ", ",
                        __args__.Select(item => FormatAliasArgument(context, item)));
            }
            string origin = __origin__ is PythonType type
                ? type.IsSystemType
                    ? type.Name
                    : $"{PythonType.Get__module__(context, type)}.{type.QualName}"
                : __origin__ is TypeAliasObject alias
                    && alias.__module__ is string aliasModule
                    && aliasModule != "<module>"
                    ? $"{aliasModule}.{alias.__qualname__}"
                    : PythonOps.Repr(context, __origin__);
            return $"{(_unpacked ? "*" : string.Empty)}{origin}[{arguments}]";
        }

        private static bool IsUnbracketedCallableParameter(object? item) {
            if (item is Ellipsis) return true;
            string typeName = DynamicHelpers.GetPythonType(item).Name;
            return typeName is "ParamSpec" or "_ConcatenateGenericAlias";
        }

        private static string FormatAliasArgument(CodeContext context, object? item) {
            if (item is PythonType type) return type.Name;
            if (item is Ellipsis) return "...";
            if (item is PythonList list) {
                int count = list.Count;
                var elements = new List<string>(count);
                for (int i = 0; i < count; i++) {
                    elements.Add(FormatAliasArgument(context, list[i]));
                }
                return "[" + string.Join(", ", elements) + "]";
            }
            if (item is PythonTuple tuple) {
                int count = tuple.Count;
                var elements = new List<string>(count);
                for (int i = 0; i < count; i++) {
                    elements.Add(FormatAliasArgument(context, tuple[i]));
                }
                return "(" + string.Join(", ", elements) + ")";
            }
            return PythonOps.Repr(context, item);
        }

        public int __hash__() {
            int result = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__origin__);
            foreach (object? argument in __args__) {
                result = unchecked(result * 31 + (argument?.GetHashCode() ?? 0));
            }
            return result;
        }

        public object __eq__([NotNone] object other) {
            return other is GenericAlias alias
                && PythonOps.EqualRetBool(__origin__, alias.__origin__)
                && PythonOps.EqualRetBool(__args__, alias.__args__)
                && _unpacked == alias._unpacked;
        }

        public object __ne__([NotNone] object other) =>
            other is GenericAlias alias
                && PythonOps.EqualRetBool(__origin__, alias.__origin__)
                && PythonOps.EqualRetBool(__args__, alias.__args__)
                && _unpacked == alias._unpacked
                ? false
                : true;

        public PythonTuple __reduce_ex__(int protocol) =>
            PythonTuple.MakeTuple(
                DynamicHelpers.GetPythonType(this),
                PythonTuple.MakeTuple(__origin__, __args__, _unpacked));

        public PythonTuple __reduce__() => __reduce_ex__(0);

        IList<object?> IPythonMembersList.GetMemberNames(CodeContext context) {
            var names = new List<object?>(PythonOps.GetAttrNames(context, __origin__));
            foreach (object? name in DynamicHelpers.GetPythonType(this).GetMemberNames(context, this)) {
                if (!names.Contains(name)) names.Add(name);
            }
            return names;
        }

        IList<string> IMembersList.GetMemberNames() => PythonOps.GetStringMemberList(this);

        WeakRefTracker? IWeakReferenceable.GetWeakRef() => _weakref;

        bool IWeakReferenceable.SetWeakRef(WeakRefTracker value) {
            _weakref = value;
            return true;
        }

        void IWeakReferenceable.SetFinalizer(WeakRefTracker value) =>
            ((IWeakReferenceable)this).SetWeakRef(value);

        internal static void AddParameters(List<object> parameters, object? value) {
            if (value is PythonTuple tuple) {
                foreach (object? item in tuple) AddParameters(parameters, item);
                return;
            }
            if (value is PythonList list) {
                foreach (object? item in list) AddParameters(parameters, item);
                return;
            }
            if (value is GenericAlias alias) {
                AddParameters(parameters, alias.__args__);
                return;
            }

            string typeName = DynamicHelpers.GetPythonType(value).Name;
            if (typeName is "TypeVar" or "TypeVarTuple" or "ParamSpec"
                && !parameters.Any(parameter => PythonOps.EqualRetBool(parameter, value))) {
                parameters.Add(value!);
                return;
            }

            if (value is not null
                && value is not PythonType
                && PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__typing_subst__",
                    out object? unlistedSubstitution)
                && unlistedSubstitution is not null
                && PythonOps.IsCallable(DefaultContext.Default, unlistedSubstitution)) {
                if (!parameters.Any(parameter => PythonOps.EqualRetBool(parameter, value))) {
                    parameters.Add(value);
                }
                return;
            }

            if (value is not null
                && value is not PythonType
                && PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__parameters__",
                    out object? nestedParameters)
                && nestedParameters is PythonTuple nestedTuple) {
                if (nestedTuple.Count == 0
                    && PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        value,
                        "__args__",
                        out object? nestedArguments)
                    && nestedArguments is PythonTuple nestedArgumentsTuple) {
                    AddParameters(parameters, nestedArgumentsTuple);
                    return;
                }
                foreach (object? parameter in nestedTuple) {
                    if (!parameters.Any(existing => PythonOps.EqualRetBool(existing, parameter))) {
                        parameters.Add(parameter!);
                    }
                }
                return;
            }
        }

        internal static PythonTuple NormalizeArguments(PythonTuple arguments) {
            if (arguments.Count == 1
                && arguments[0] is PythonTuple emptyTuple
                && emptyTuple.Count == 0) {
                return PythonTuple.EMPTY;
            }
            var normalized = new List<object?>(arguments.Count);
            foreach (object? argument in arguments) {
                if (argument is GenericAlias directAlias && directAlias.__unpacked__) {
                    AddUnpackedArguments(normalized, directAlias);
                    continue;
                }

                string typeName = DynamicHelpers.GetPythonType(argument).Name;
                if (typeName != "_UnpackGenericAlias") {
                    normalized.Add(argument);
                    continue;
                }

                if (PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        argument,
                        "__typing_unpacked_tuple_args__",
                        out object? tupleArguments)
                    && tupleArguments is PythonTuple tupleArgumentsTuple) {
                    if (tupleArgumentsTuple.Count != 0
                        && tupleArgumentsTuple[tupleArgumentsTuple.Count - 1] is Ellipsis) {
                        normalized.Add(argument);
                    } else {
                        normalized.AddRange(tupleArgumentsTuple);
                    }
                    continue;
                }

                object? unpacked = PythonOps.GetBoundAttr(
                    DefaultContext.Default,
                    argument,
                    "__args__") is PythonTuple unpackedArgs
                    && unpackedArgs.Count == 1
                        ? unpackedArgs[0]
                        : null;
                if (unpacked is GenericAlias alias) {
                    AddUnpackedArguments(normalized, alias);
                } else if (unpacked is not null
                    && DynamicHelpers.GetPythonType(unpacked).Name == "TypeVarTuple") {
                    normalized.Add(argument);
                } else {
                    throw PythonOps.TypeError("Unpack[...] must be used with a tuple type");
                }
            }
            return PythonTuple.MakeTuple(normalized.ToArray());
        }

        internal static object? RemapTypeParameters(
            object? value,
            PythonTuple typeParameters) {
            if (value is PythonList list) {
                var result = new PythonList();
                foreach (object? item in list) {
                    result.Add(RemapTypeParameters(item, typeParameters));
                }
                return result;
            }
            if (value is PythonTuple tuple) {
                return PythonTuple.MakeTuple(tuple
                    .Select(item => RemapTypeParameters(item, typeParameters))
                    .ToArray());
            }
            if (value is GenericAlias alias) {
                return new GenericAlias(
                    alias.__origin__,
                    PythonTuple.MakeTuple(alias.__args__
                        .Select(item => RemapTypeParameters(item, typeParameters))
                        .ToArray()),
                    alias.__unpacked__);
            }
            if (value is not null
                && (DynamicHelpers.GetPythonType(value).Name is
                    "TypeVar" or "TypeVarTuple" or "ParamSpec")
                && PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__name__",
                    out object? valueName)
                && valueName is string name) {
                foreach (object typeParameter in typeParameters) {
                    if (PythonOps.TryGetBoundAttr(
                            DefaultContext.Default,
                            typeParameter,
                            "__name__",
                            out object? typeParameterName)
                        && string.Equals(
                            typeParameterName as string,
                            name,
                            StringComparison.Ordinal)) {
                        return typeParameter;
                    }
                }
            }
            if (value is not null
                && value is not PythonType
                && PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__args__",
                    out object? nestedArgs)
                && nestedArgs is PythonTuple nestedTuple) {
                PythonTuple remapped = PythonTuple.MakeTuple(nestedTuple
                    .Select(item => RemapTypeParameters(item, typeParameters))
                    .ToArray());
                object typing = Importer.ImportModule(
                    DefaultContext.Default,
                    null,
                    "typing",
                    false,
                    0);
                object rebuild = PythonOps.GetBoundAttr(
                    DefaultContext.Default,
                    typing,
                    "_rebuild_generic_alias");
                return PythonCalls.Call(
                    DefaultContext.Default,
                    rebuild,
                    value,
                    remapped);
            }
            return value;
        }

        private static void AddUnpackedArguments(
            List<object?> normalized,
            GenericAlias alias) {
            if (!ReferenceEquals(alias.__origin__, TypeCache.PythonTuple)) {
                throw PythonOps.TypeError("Unpack[...] must be used with a tuple type");
            }
            if (alias.__args__.Any(item => item is Ellipsis)) {
                normalized.Add(alias);
                return;
            }
            if (alias.__args__.Count == 1
                && alias.__args__[0] is PythonTuple emptyTuple
                && emptyTuple.Count == 0) {
                return;
            }
            normalized.AddRange(alias.__args__);
        }

        private static object? Substitute(
            object? value,
            PythonTuple parameters,
            PythonTuple replacements) {
            for (int i = 0; i < parameters.Count; i++) {
                if (PythonOps.EqualRetBool(value, parameters[i])) {
                    if (PythonOps.TryGetBoundAttr(
                            DefaultContext.Default,
                            parameters[i],
                            "__typing_subst__",
                            out object? substitute)
                        && substitute is not null) {
                        object substituted = PythonCalls.Call(
                            DefaultContext.Default,
                            substitute,
                            replacements[i])!;
                        if (PythonOps.TryGetBoundAttr(
                                DefaultContext.Default,
                                parameters[i],
                                "__typing_is_unpacked_typevartuple__",
                                out object? isUnpacked)
                            && PythonOps.IsTrue(isUnpacked)
                            && substituted is not PythonTuple) {
                            throw PythonOps.TypeError(
                                "Invalid result from __typing_subst__: expected tuple, got {0}",
                                PythonOps.GetPythonTypeName(substituted));
                        }
                        return substituted;
                    }
                    return replacements[i];
                }
            }
            if (value is not null
                && value is not PythonType
                && PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__typing_subst__",
                    out object? unlistedSubstitution)
                && unlistedSubstitution is not null
                && PythonOps.IsCallable(DefaultContext.Default, unlistedSubstitution)) {
                throw PythonOps.TypeError(
                    "{0} is not found in __parameters__",
                    PythonOps.Repr(DefaultContext.Default, value));
            }
            if (value is PythonList list) {
                var result = new PythonList();
                foreach (object? item in list) {
                    result.Add(Substitute(item, parameters, replacements));
                }
                return result;
            }
            if (value is PythonTuple tuple) {
                return PythonTuple.MakeTuple(tuple.Select(item =>
                    Substitute(item, parameters, replacements)).ToArray());
            }
            if (value is GenericAlias alias) {
                return new GenericAlias(
                    alias.__origin__,
                    PythonTuple.MakeTuple(alias.__args__.Select(item =>
                        Substitute(item, parameters, replacements)).ToArray()),
                    alias.__unpacked__);
            }
            if (value is not null
                && value is not PythonType
                && PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__parameters__",
                    out object? nestedParameters)
                && nestedParameters is PythonTuple nestedTuple
                && nestedTuple.Count != 0) {
                var nestedReplacements = new List<object?>(nestedTuple.Count);
                bool changed = false;
                foreach (object? parameter in nestedTuple) {
                    object? replacement = parameter;
                    for (int i = 0; i < parameters.Count; i++) {
                        if (PythonOps.EqualRetBool(parameter, parameters[i])) {
                            replacement = replacements[i];
                            changed = true;
                            break;
                        }
                    }
                    nestedReplacements.Add(replacement);
                }
                if (changed) {
                    object index = nestedReplacements.Count == 1
                        ? nestedReplacements[0]!
                        : PythonTuple.MakeTuple(nestedReplacements.ToArray());
                    return PythonOps.GetIndex(DefaultContext.Default, value, index);
                }
            }
            return value;
        }

        internal static object? SubstituteTypeArgument(
            object? value,
            PythonTuple parameters,
            PythonTuple replacements)
            => Substitute(value, parameters, replacements);

        private static PythonTuple BindReplacements(
            PythonTuple parameters,
            PythonTuple arguments) {
            int spreadIndex = -1;
            PythonTuple? spreadArguments = null;
            for (int i = 0; i < arguments.Count; i++) {
                PythonTuple? candidate = GetSpreadArguments(arguments[i]);
                if (candidate is null) continue;
                if (spreadIndex >= 0) {
                    throw PythonOps.TypeError(
                        "Multiple unpacked variadic tuples are not supported");
                }
                spreadIndex = i;
                spreadArguments = candidate;
            }

            int typeVarTupleIndex = -1;
            for (int i = 0; i < parameters.Count; i++) {
                if (IsTypeVarTuple(parameters[i])) {
                    typeVarTupleIndex = i;
                    break;
                }
            }
            if (spreadIndex >= 0
                && spreadArguments is not null
                && typeVarTupleIndex >= 0) {
                object spreadElement = spreadArguments[0]!;
                var bound = new object?[parameters.Count];
                var consumed = new bool[arguments.Count];

                for (int i = 0; i < typeVarTupleIndex; i++) {
                    if (i < spreadIndex) {
                        bound[i] = arguments[i];
                        consumed[i] = true;
                    } else {
                        bound[i] = spreadElement;
                    }
                }

                int fixedAfter = parameters.Count - typeVarTupleIndex - 1;
                for (int offset = 1; offset <= fixedAfter; offset++) {
                    int fixedArgumentIndex = arguments.Count - offset;
                    int parameterIndex = parameters.Count - offset;
                    if (fixedArgumentIndex > spreadIndex) {
                        bound[parameterIndex] = arguments[fixedArgumentIndex];
                        consumed[fixedArgumentIndex] = true;
                    } else {
                        bound[parameterIndex] = spreadElement;
                    }
                }

                var variadic = new List<object?>();
                for (int i = 0; i < arguments.Count; i++) {
                    if (!consumed[i]) variadic.Add(arguments[i]);
                }
                bound[typeVarTupleIndex] = PythonTuple.MakeTuple(variadic.ToArray());
                return PythonTuple.MakeTuple(bound);
            }

            var replacements = new List<object?>(parameters.Count);
            int argumentIndex = 0;
            for (int i = 0; i < parameters.Count; i++) {
                object parameter = parameters[i]!;
                if (IsTypeVarTuple(parameter)) {
                    int fixedAfter = 0;
                    for (int j = i + 1; j < parameters.Count; j++) {
                        if (!IsTypeVarTuple(parameters[j]!)) fixedAfter++;
                    }
                    int count = arguments.Count - argumentIndex - fixedAfter;
                    if (count < 0) {
                        throw PythonOps.TypeError(
                            $"Too few arguments for {PythonOps.Repr(DefaultContext.Default, parameters)}");
                    }
                    var expanded = new object?[count];
                    for (int j = 0; j < count; j++) {
                        expanded[j] = arguments[argumentIndex++];
                    }
                    replacements.Add(PythonTuple.MakeTuple(expanded));
                } else {
                    if (argumentIndex >= arguments.Count) {
                        throw PythonOps.TypeError(
                            $"Too few arguments for {PythonOps.Repr(DefaultContext.Default, parameters)}");
                    }
                    replacements.Add(arguments[argumentIndex++]);
                }
            }
            if (argumentIndex != arguments.Count) {
                throw PythonOps.TypeError(
                    $"Too many arguments for {PythonOps.Repr(DefaultContext.Default, parameters)}");
            }
            return PythonTuple.MakeTuple(replacements.ToArray());
        }

        private static int FindParameterIndex(object? value, PythonTuple parameters) {
            for (int i = 0; i < parameters.Count; i++) {
                if (PythonOps.EqualRetBool(value, parameters[i])) return i;
            }
            return -1;
        }

        private static bool IsTypeVarTuple(object? value) =>
            value is not null
            && DynamicHelpers.GetPythonType(value).Name == "TypeVarTuple";

        private static bool IsParamSpec(object? value) =>
            value is not null
            && DynamicHelpers.GetPythonType(value).Name == "ParamSpec";

        private static bool IsConcatenateAlias(object? value) =>
            value is not null
            && DynamicHelpers.GetPythonType(value).Name == "_ConcatenateGenericAlias";

        private static PythonTuple? GetSpreadArguments(object? value) {
            if (value is null
                || !PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__typing_unpacked_tuple_args__",
                    out object? unpacked)
                || unpacked is not PythonTuple tuple
                || tuple.Count == 0
                || tuple[tuple.Count - 1] is not Ellipsis) {
                return null;
            }
            return tuple;
        }
    }

    [PythonType("generic_alias_iterator")]
    public sealed class GenericAliasIterator : IEnumerable<object?>, IEnumerator<object?> {
        private readonly object? _item;
        private int _index = -1;

        internal GenericAliasIterator(object? item) {
            _item = item;
        }

        public GenericAliasIterator __iter__() => this;

        public object? __next__() {
            if (!MoveNext()) throw PythonOps.StopIteration();
            return Current;
        }

        public object? Current => _index == 0
            ? _item
            : throw PythonOps.StopIteration();

        object? IEnumerator.Current => Current;

        public bool MoveNext() {
            if (_index >= 0) return false;
            _index++;
            return true;
        }

        public void Reset() => throw PythonOps.TypeError("iterator cannot be reset");

        public void Dispose() { }

        public IEnumerator<object?> GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => this;
    }

    [PythonType("UnionType")]
    public sealed class UnionType : ICodeFormattable, IPythonMembersList {
        private readonly PythonTuple _hashableArgs;
        private readonly PythonTuple _unhashableArgs;

        public static object __class_getitem__([NotNone] object parameters) {
            if (parameters is not PythonTuple tuple) {
                return parameters;
            }
            if (tuple.Count == 2) {
                return MakeUnion(tuple[0], tuple[1]);
            }
            if (tuple.Count == 0) {
                throw PythonOps.TypeError("UnionType requires at least two arguments");
            }
            object result = tuple[0]!;
            for (int i = 1; i < tuple.Count; i++) {
                result = MakeUnion(result, tuple[i]);
            }
            return result;
        }

        internal static object MakeUnion(object? left, object? right) {
            var union = new UnionType(left, right);
            return union.__args__.Count == 1
                ? union.__args__[0]!
                : union;
        }

        private UnionType(object? left, object? right) {
            var members = new List<object>();
            var hashable = new List<object>();
            var unhashable = new List<object>();
            AddMembers(members, hashable, unhashable, left);
            AddMembers(members, hashable, unhashable, right);
            __args__ = PythonTuple.MakeTuple(members.ToArray());
            _hashableArgs = PythonTuple.MakeTuple(hashable.ToArray());
            _unhashableArgs = PythonTuple.MakeTuple(unhashable.ToArray());
        }

        public static object __new__(
            [NotNone] PythonType cls,
            [NotNone] params object[] args) {
            throw PythonOps.TypeError(
                "cannot create 'types.UnionType' instances");
        }

        public PythonTuple __args__ { get; }

        public string __name__ => "Union";

        public string __qualname__ => "Union";

        public string __module__ => "typing";

        public PythonType __origin__ => DynamicHelpers.GetPythonType(this);

        [SpecialName]
        public object? GetCustomMember(CodeContext context, [NotNone] string name) {
            if (name == "__origin__") {
                return DynamicHelpers.GetPythonType(this);
            }
            return OperationFailed.Value;
        }

        public object __mro_entries__(
            CodeContext context,
            [NotNone] object bases) {
            throw PythonOps.TypeError(
                "Cannot subclass {0}",
                PythonOps.Repr(context, this));
        }

        IList<object?> IPythonMembersList.GetMemberNames(
            CodeContext context) {
            var names = DynamicHelpers.GetPythonType(this)
                .GetMemberNames(context, this);
            if (!names.Contains("__origin__")) {
                names.Add("__origin__");
            }
            if (!names.Contains("__mro_entries__")) {
                names.Add("__mro_entries__");
            }
            return names;
        }

        IList<string> IMembersList.GetMemberNames()
            => PythonOps.GetStringMemberList(this);

        public bool _typing_is_non_iterable => true;

        public object? __iter__() => throw PythonOps.TypeError(
            "'UnionType' object is not iterable");

        public PythonTuple __parameters__ {
            get {
                var parameters = new List<object>();
                GenericAlias.AddParameters(parameters, __args__);
                return PythonTuple.MakeTuple(parameters.ToArray());
            }
        }

        public object __getitem__(CodeContext context, [NotNone] object item) {
            PythonTuple parameters = __parameters__;
            if (parameters.Count == 0) {
                throw PythonOps.TypeError(
                    "{0} is not a generic class",
                    PythonOps.Repr(context, this));
            }
            PythonTuple replacements = item is PythonTuple tuple
                ? tuple
                : PythonTuple.MakeTuple(item);
            if (replacements.Count != parameters.Count) {
                throw PythonOps.TypeError(
                    "Too {0} arguments for {1}; actual {2}, expected {3}",
                    replacements.Count > parameters.Count ? "many" : "few",
                    PythonOps.Repr(context, this),
                    replacements.Count,
                    parameters.Count);
            }
            object? result = null;
            foreach (object? member in __args__) {
                object specialized = GenericAlias.SubstituteTypeArgument(
                    member,
                    parameters,
                    replacements)!;
                result = result is null
                    ? specialized
                    : MakeUnion(result, specialized);
            }
            return result!;
        }

        public object __or__(object? other) {
            return MakeUnion(this, other);
        }

        public object __ror__(object? other) {
            return MakeUnion(other, this);
        }

        public string __repr__(CodeContext context) {
            return string.Join(
                " | ",
                __args__.Select(item => FormatUnionMember(context, item)));
        }

        public int __hash__(CodeContext context) {
            if (_unhashableArgs.Count != 0) {
                foreach (object? argument in _unhashableArgs) {
                    PythonOps.Hash(context, argument!);
                }
                throw PythonOps.TypeError(
                    "union contains {0} unhashable elements",
                    _unhashableArgs.Count);
            }
            return PythonOps.Hash(
                context,
                FrozenSetCollection.Make(_hashableArgs));
        }

        public PythonTuple __reduce_ex__(int protocol) {
            object typing = Importer.ImportModule(
                DefaultContext.Default,
                null,
                "typing",
                false,
                0);
            object union = PythonOps.GetBoundAttr(
                DefaultContext.Default,
                typing,
                "Union");
            object operatorModule = Importer.ImportModule(
                DefaultContext.Default,
                null,
                "operator",
                false,
                0);
            object getItem = PythonOps.GetBoundAttr(
                DefaultContext.Default,
                operatorModule,
                "getitem");
            return PythonTuple.MakeTuple(
                getItem,
                PythonTuple.MakeTuple(union, __args__));
        }

        public PythonTuple __reduce__() => __reduce_ex__(0);

        public object __eq__([NotNone] object other) {
            IReadOnlyList<object?> otherMembers = GetUnionMembers(other);
            if (otherMembers.Count == 0) return false;
            var otherHashable = new List<object>();
            var otherUnhashable = new List<object>();
            foreach (object? member in otherMembers) {
                ClassifyMember(member, otherHashable, otherUnhashable);
            }
            return _hashableArgs.Count == otherHashable.Count
                && _unhashableArgs.Count == otherUnhashable.Count
                && ContainsAll(_hashableArgs, otherHashable)
                && ContainsAll(_unhashableArgs, otherUnhashable);
        }

        public object __ne__([NotNone] object other) {
            return !PythonOps.IsTrue(__eq__(other));
        }

        public bool __instancecheck__(
            CodeContext context,
            object? instance) {
            foreach (object? member in _hashableArgs) {
                EnsureRuntimeType(member!);
                if (InstanceMatches(context, instance, member!)) {
                    return true;
                }
            }
            foreach (object? member in _unhashableArgs) {
                EnsureRuntimeType(member!);
                if (InstanceMatches(context, instance, member!)) {
                    return true;
                }
            }
            return false;
        }

        private static bool InstanceMatches(
            CodeContext context,
            object? instance,
            object member) {
            if (member is PythonType type) {
                return PythonOps.IsInstance(instance, type);
            }
            if (PythonOps.TryGetBoundAttr(
                    context,
                    member,
                    "__instancecheck__",
                    out object? instanceCheck)
                && instanceCheck is not null) {
                return PythonOps.IsTrue(
                    PythonCalls.Call(context, instanceCheck, instance));
            }
            throw PythonOps.TypeError(
                "isinstance() arg 2 must be a type, a tuple of types, or a union");
        }

        public bool __subclasscheck__(
            CodeContext context,
            object? instance) {
            PythonType cls = instance as PythonType
                ?? DynamicHelpers.GetPythonType(instance);
            foreach (object? member in _hashableArgs) {
                EnsureRuntimeType(member!);
                if (PythonOps.IsSubClass(context, cls, member)) {
                    return true;
                }
            }
            foreach (object? member in _unhashableArgs) {
                EnsureRuntimeType(member!);
                if (PythonOps.IsSubClass(context, cls, member)) {
                    return true;
                }
            }
            return false;
        }

        private static void EnsureRuntimeType(object member) {
            if (member is GenericAlias) {
                throw PythonOps.TypeError(
                    "Subscripted generics cannot be used with class and instance checks");
            }
        }

        private static void AddMembers(
            List<object> members,
            List<object> hashable,
            List<object> unhashable,
            object? value) {
            value = TypeCheck(value);
            IReadOnlyList<object?> nestedMembers = GetUnionMembers(value);
            if (nestedMembers.Count != 0) {
                foreach (object? member in nestedMembers) {
                    AddMembers(members, hashable, unhashable, member);
                }
                return;
            }
            bool isHashable;
            try {
                PythonOps.Hash(DefaultContext.Default, value!);
                isHashable = true;
            } catch (Exception) {
                isHashable = false;
            }
            List<object> category = isHashable ? hashable : unhashable;
            if (category.Any(existing =>
                    PythonOps.EqualRetBool(existing, value))) {
                return;
            }
            category.Add(value!);
            members.Add(value!);
        }

        private static object TypeCheck(object? value) {
            if (value is null) {
                return TypeCache.Null;
            }
            if (value is PythonType
                or GenericAlias
                or UnionType
                or TypeAliasObject) {
                return value;
            }
            if (PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        value,
                        "__typing_subst__",
                        out _)
                    || PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        value,
                        "__origin__",
                        out _)
                    || PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        value,
                        "__supertype__",
                        out _)
                    || PythonOps.TryGetBoundAttr(
                        DefaultContext.Default,
                        value,
                        "__forward_arg__",
                        out _)) {
                return value;
            }
            PythonType runtimeType = DynamicHelpers.GetPythonType(value);
            PythonOps.GetBoundAttr(
                DefaultContext.Default,
                runtimeType,
                "__module__");
            if (runtimeType.Name == "_SpecialForm"
                && PythonType.Get__module__(
                    DefaultContext.Default,
                    runtimeType) is "typing" or "typing_extensions") {
                return value;
            }
            object typing = Importer.ImportModule(
                DefaultContext.Default,
                null,
                "typing",
                false,
                0);
            object typeCheck = PythonOps.GetBoundAttr(
                DefaultContext.Default,
                typing,
                "_type_check");
            object checkedValue = PythonCalls.Call(
                DefaultContext.Default,
                typeCheck,
                value,
                "Union[arg, ...]: each arg must be a type.")!;
            if (checkedValue is PythonType
                or GenericAlias
                or UnionType
                or TypeAliasObject
                || PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    checkedValue,
                    "__typing_subst__",
                    out _)
                || PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    checkedValue,
                    "__origin__",
                    out _)
                || PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    checkedValue,
                    "__supertype__",
                    out _)
                || PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    checkedValue,
                    "__forward_arg__",
                    out _)) {
                return checkedValue;
            }
            throw PythonOps.TypeError(
                "Union[arg, ...]: each arg must be a type.");
        }

        private static void ClassifyMember(
            object? value,
            List<object> hashable,
            List<object> unhashable) {
            try {
                PythonOps.Hash(DefaultContext.Default, value!);
                hashable.Add(value!);
            } catch (Exception) {
                unhashable.Add(value!);
            }
        }

        private static bool ContainsAll(
            IReadOnlyList<object?> left,
            IReadOnlyList<object> right) {
            return left.All(argument =>
                right.Any(candidate =>
                    PythonOps.EqualRetBool(argument, candidate)));
        }

        private static IReadOnlyList<object?> GetUnionMembers(object? value) {
            if (value is UnionType union) return union.__args__;
            if (value is not null
                && PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__origin__",
                    out object? origin)
                && origin is PythonType originType
                && originType.Name == "Union"
                && PythonType.Get__module__(DefaultContext.Default, originType) as string
                    is ("typing" or "_typing")
                && PythonOps.TryGetBoundAttr(
                    DefaultContext.Default,
                    value,
                    "__args__",
                    out object? arguments)
                && arguments is PythonTuple tuple) {
                return tuple;
            }
            return Array.Empty<object?>();
        }

        private static string FormatUnionMember(CodeContext context, object? item) {
            if (item is PythonType type) {
                string? module = PythonType.Get__module__(context, type) as string;
                if (module == "builtins") {
                    return type.Name == "NoneType" ? "None" : type.Name;
                }
                return module + "." + type.QualName;
            }
            if (item is PythonFunction
                && PythonOps.TryGetBoundAttr(context, item, "__module__", out object? moduleValue)
                && moduleValue is string moduleName
                && PythonOps.TryGetBoundAttr(context, item, "__qualname__", out object? qualNameValue)
                && qualNameValue is string qualName) {
                return moduleName == "builtins" ? qualName : moduleName + "." + qualName;
            }
            return PythonOps.Repr(context, item);
        }
    }
}
