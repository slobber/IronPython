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

        public PythonTuple __parameters__ {
            get {
                var parameters = new List<object>();
                AddParameters(parameters, __args__);
                return PythonTuple.MakeTuple(parameters.ToArray());
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
                    "There are no type variables left in {0}",
                    PythonOps.Repr(DefaultContext.Default, this));
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
            return PythonCalls.Call(context, __origin__, args)!;
        }

        public object __call__(
            CodeContext context,
            [ParamDictionary] IDictionary<object, object> keywordArgs,
            [NotNone] params object?[] args) {
            return PythonCalls.CallWithKeywordArgs(context, __origin__, args, keywordArgs)!;
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
            return new UnionType(this, other);
        }

        public object __ror__(object? other) {
            return new UnionType(other, this);
        }

        public string __repr__(CodeContext context) {
            string arguments = __args__.Count == 0
                ? "()"
                : string.Join(
                    ", ",
                    __args__.Select(item => FormatAliasArgument(context, item)));
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
                throw PythonOps.TypeError("*tuple[..., ...] is not valid as type argument");
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
                        return PythonCalls.Call(
                            DefaultContext.Default,
                            substitute,
                            replacements[i]);
                    }
                    return replacements[i];
                }
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

        private static PythonTuple BindReplacements(
            PythonTuple parameters,
            PythonTuple arguments) {
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
    public sealed class UnionType : ICodeFormattable {
        public static object __class_getitem__([NotNone] PythonTuple parameters) {
            if (parameters.Count == 2) {
                return new UnionType(parameters[0], parameters[1]);
            }
            if (parameters.Count == 0) {
                throw PythonOps.TypeError("UnionType requires at least two arguments");
            }
            object result = parameters[0]!;
            for (int i = 1; i < parameters.Count; i++) {
                result = new UnionType(result, parameters[i]);
            }
            return result;
        }

        public UnionType(object? left, object? right) {
            var members = new List<object>();
            AddMembers(members, left);
            AddMembers(members, right);
            __args__ = PythonTuple.MakeTuple(members.ToArray());
        }

        public PythonTuple __args__ { get; }

        public PythonTuple __parameters__ {
            get {
                var parameters = new List<object>();
                GenericAlias.AddParameters(parameters, __args__);
                return PythonTuple.MakeTuple(parameters.ToArray());
            }
        }

        public object __getitem__(CodeContext context, [NotNone] object item) {
            object? result = null;
            foreach (object? member in __args__) {
                object specialized = member!;
                if (member is not null
                    && PythonOps.TryGetBoundAttr(
                        context,
                        member,
                        "__parameters__",
                        out object? parameters)
                    && parameters is PythonTuple parameterTuple
                    && parameterTuple.Count != 0
                    && PythonOps.TryGetBoundAttr(
                        context,
                        member,
                        "__getitem__",
                        out object? getItem)
                    && getItem is not null) {
                    specialized = PythonCalls.Call(context, getItem, item)!;
                }
                result = result is null
                    ? specialized
                    : new UnionType(result, specialized);
            }
            return result!;
        }

        public object __or__(object? other) {
            return new UnionType(this, other);
        }

        public object __ror__(object? other) {
            return new UnionType(other, this);
        }

        public string __repr__(CodeContext context) {
            return string.Join(
                " | ",
                __args__.Select(item => FormatUnionMember(context, item)));
        }

        public int __hash__() {
            int result = 0;
            foreach (object? argument in __args__) {
                result ^= argument?.GetHashCode() ?? 0;
            }
            return result;
        }

        public object __eq__([NotNone] object other) {
            var members = new List<object>();
            foreach (object? member in __args__) AddMembers(members, member);
            IReadOnlyList<object?> otherMembers = GetUnionMembers(other);
            return otherMembers.Count != 0
                && members.Count == otherMembers.Count
                && members.All(argument =>
                    otherMembers.Any(candidate => PythonOps.EqualRetBool(argument, candidate)));
        }

        public object __ne__([NotNone] object other) {
            var members = new List<object>();
            foreach (object? member in __args__) AddMembers(members, member);
            IReadOnlyList<object?> otherMembers = GetUnionMembers(other);
            return !(otherMembers.Count != 0
                && members.Count == otherMembers.Count
                && members.All(argument =>
                    otherMembers.Any(candidate => PythonOps.EqualRetBool(argument, candidate))));
        }

        private static void AddMembers(List<object> members, object? value) {
            IReadOnlyList<object?> nestedMembers = GetUnionMembers(value);
            if (nestedMembers.Count != 0) {
                foreach (object? member in nestedMembers) AddMembers(members, member);
            } else if (!members.Any(member => PythonOps.EqualRetBool(member, value))) {
                members.Add(value!);
            }
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
