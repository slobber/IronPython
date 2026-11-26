// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.
//
// Copyright (c) Jeff Hardy 2010.
// Copyright (c) Dan Eloff 2008-2009.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using IronPython.Compiler;
using IronPython.Compiler.Ast;
using IronPython.Compiler.PublicAst;
using IronPython.Runtime;
using IronPython.Runtime.Binding;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

using AstExpression = IronPython.Compiler.Ast.Expression;
using Generic = System.Collections.Generic;
using LinqExpression = System.Linq.Expressions.Expression;
using PublicAstNode = IronPython.Compiler.Syntax.Ast.PythonAstNode;

[assembly: PythonModule("_ast", typeof(IronPython.Modules._ast))]
namespace IronPython.Modules {
    public static class _ast {
        public const string __version__ = "62047";
        public const int PyCF_IGNORE_COOKIE = 0x800;
        public const int PyCF_TYPE_COMMENTS = 0x1000;
        public const int PyCF_ALLOW_TOP_LEVEL_AWAIT = 0x2000;
        public const int PyCF_OPTIMIZED_AST = 0x4000;
        public const int PyCF_ONLY_AST = 0x400;

        static _ast() {
            InstallSystemAnnotations();
        }

        private static void InstallSystemAnnotations() {
            foreach (Type type in typeof(_ast).GetNestedTypes()) {
                if (!typeof(AST).IsAssignableFrom(type)) {
                    continue;
                }

                PythonDictionary annotations;
                if (type.IsAbstract) {
                    annotations = new PythonDictionary();
                } else if (type == typeof(arguments)) {
                    annotations = CreateArgumentsAnnotations();
                } else {
                    object instance = Activator.CreateInstance(
                        type,
                        nonPublic: true);
                    annotations = CreateNodeAnnotations(
                        type,
                        (AST)instance);
                }

                DynamicHelpers.GetPythonTypeFromType(type)
                    .SetSystemAnnotations(annotations);
            }
        }

        private static PythonDictionary CreateNodeAnnotations(
            Type type,
            AST instance) {
            var annotations = new PythonDictionary();
            foreach (object field in instance._fields) {
                if (field is not string name) {
                    continue;
                }
                annotations[name] = AST.SequenceFieldNames.Contains(name)
                    ? new GenericAlias(
                        TypeCache.PythonList,
                        PythonTuple.MakeTuple(TypeCache.Object).ToArray())
                    : GetAnnotationType(
                        type.GetProperty(name)?.PropertyType);
            }
            return annotations;
        }

        private static PythonDictionary CreateArgumentsAnnotations() {
            PythonType argType = DynamicHelpers.GetPythonTypeFromType(
                typeof(ArgType));
            PythonType exprType = DynamicHelpers.GetPythonTypeFromType(
                typeof(expr));
            object optionalArgType = UnionType.MakeUnion(argType, null);
            var annotations = new PythonDictionary {
                ["posonlyargs"] = new GenericAlias(
                    TypeCache.PythonList,
                    PythonTuple.MakeTuple(argType).ToArray()),
                ["args"] = new GenericAlias(
                    TypeCache.PythonList,
                    PythonTuple.MakeTuple(argType).ToArray()),
                ["vararg"] = optionalArgType,
                ["kwonlyargs"] = new GenericAlias(
                    TypeCache.PythonList,
                    PythonTuple.MakeTuple(argType).ToArray()),
                ["kw_defaults"] = new GenericAlias(
                    TypeCache.PythonList,
                    PythonTuple.MakeTuple(exprType).ToArray()),
                ["kwarg"] = optionalArgType,
                ["defaults"] = new GenericAlias(
                    TypeCache.PythonList,
                    PythonTuple.MakeTuple(exprType).ToArray())
            };
            return annotations;
        }

        private static object GetAnnotationType(Type type) {
            if (type is null || type == typeof(object)) {
                return TypeCache.Object;
            }
            if (type == typeof(string)) {
                return TypeCache.String;
            }
            if (type == typeof(int)) {
                return DynamicHelpers.GetPythonType(BigInteger.Zero);
            }
            if (typeof(AST).IsAssignableFrom(type)) {
                return DynamicHelpers.GetPythonTypeFromType(type);
            }
            if (type == typeof(PythonList)) {
                return new GenericAlias(
                    TypeCache.PythonList,
                    PythonTuple.MakeTuple(TypeCache.Object).ToArray());
            }
            return TypeCache.Object;
        }

        public static bool _is_abstract(object cls) {
            if (cls is not PythonType type) {
                return false;
            }
            return type.Name is "AST"
                or "mod"
                or "stmt"
                or "expr"
                or "expr_context"
                or "boolop"
                or "operator"
                or "unaryop"
                or "cmpop"
                or "excepthandler"
                or "pattern"
                or "type_ignore"
                or "type_param"
                or "slice";
        }

        public static bool _is_deleted(object value) =>
            AST.IsDeletedField(value);

        public static void _install_match_args() {
            foreach (Type type in typeof(_ast).GetNestedTypes()) {
                if (!typeof(AST).IsAssignableFrom(type)
                    || type.IsAbstract) {
                    continue;
                }
                object instance = Activator.CreateInstance(
                    type,
                    nonPublic: true);
                DynamicHelpers.GetPythonTypeFromType(type)
                    .SetSystemMember(
                        "__match_args__",
                        ((AST)instance)._fields);
            }
        }

        [ThreadStatic]
        internal static bool _skipBuildAstBinding;
        private static readonly HashSet<string> IdentifierFieldNames =
            new HashSet<string>(StringComparer.Ordinal) {
                "arg", "asname", "attr", "id", "name", "names", "rest"
            };

        private class ThrowingErrorSink : ErrorSink {
            public static new readonly ThrowingErrorSink/*!*/ Default = new ThrowingErrorSink();

            private ThrowingErrorSink() {
            }

            public override void Add(SourceUnit sourceUnit, string message, SourceSpan span, int errorCode, Severity severity) {
                if (severity == Severity.Warning) {
                    PythonOps.SyntaxWarning(message, sourceUnit, span, errorCode);
                } else {
                    throw PythonOps.SyntaxError(message, sourceUnit, span, errorCode);
                }
            }
        }

        internal static PythonAst ConvertToPythonAst(
            CodeContext codeContext,
            AST source,
            string filename,
            string moduleName = null) {
            PythonCompilerOptions options = new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode);
            options.ModuleName = moduleName;
            SourceUnit unit = new SourceUnit(codeContext.LanguageContext, NullTextContentProvider.Null, filename, SourceCodeKind.AutoDetect);
            CompilerContext compilerContext = new CompilerContext(unit, options, ErrorSink.Default);
            CheckForCycles(source);
            ValidateAstTree(source);
            PublicAstNode publicAst = ConvertToPublicAst(source);
            return publicAst switch {
                IronPython.Compiler.Syntax.Ast.Expression expression
                    => new PythonAst(
                        new ReturnStatement(
                            PublicAstLowerer.LowerExpression(expression.Body)),
                        false,
                        ModuleOptions.ExecOrEvalCode,
                        false,
                        compilerContext,
                        Array.Empty<int>()),
                IronPython.Compiler.Syntax.Ast.Module module
                    => PublicAstLowerer.LowerModule(
                        module,
                        compilerContext,
                        true,
                        false),
                IronPython.Compiler.Syntax.Ast.Interactive interactive
                    => PublicAstLowerer.LowerStatements(
                        interactive.Body,
                        compilerContext,
                        true),
                _ => throw PythonOps.TypeError(
                    "unsupported type of AST: {0}",
                    source.GetType())
            };
        }

        private static PublicAstNode ConvertToPublicAst(AST source) {
            Type astType = GetAstSystemType(source);
            string syntaxTypeName = astType == typeof(ArgType)
                ? "Arg"
                : astType.Name switch {
                    "arguments" => "Arguments",
                    "alias" => "Alias",
                    "keyword" => "Keyword",
                    "withitem" => "Withitem",
                    "match_case" => "MatchCase",
                    "comprehension" => "Comprehension",
                    _ => astType.Name
                };
            Type syntaxType = typeof(PublicAstLowerer).Assembly.GetType(
                "IronPython.Compiler.Syntax.Ast." + syntaxTypeName);
            if (syntaxType is null) {
                throw PythonOps.TypeError(
                    "unsupported type of AST: {0}",
                    source.GetType());
            }

            System.Reflection.ConstructorInfo constructor =
                syntaxType.GetConstructors().Single();
            System.Reflection.ParameterInfo[] parameters =
                constructor.GetParameters();
            int locationCount = HasLocationParameters(parameters) ? 4 : 0;
            int fieldCount = parameters.Length - locationCount;
            object[] arguments = new object[parameters.Length];
            for (int i = 0; i < fieldCount; i++) {
                string fieldName = i < source._fields.Count
                    ? source._fields[i] as string
                    : null;
                if (fieldName is null
                    || !source.TryGetFieldValue(
                        fieldName,
                        out object fieldValue)) {
                    fieldValue = AST.SequenceFieldNames.Contains(
                        fieldName ?? string.Empty)
                        ? new PythonList()
                        : null;
                }
                if (astType == typeof(Constant)
                    && fieldName == "value") {
                    fieldValue = NormalizeParsedConstant(fieldValue);
                }
                if (fieldValue is string identifier
                    && IdentifierFieldNames.Contains(
                        fieldName ?? string.Empty)) {
                    fieldValue = string.Intern(identifier);
                }
                if (fieldValue is null
                    && !source.IsOptionalField(fieldName)
                    && !IsNullValueAllowed(astType, fieldName)) {
                    throw PythonOps.ValueError(
                        "field '{0}' is required for {1}",
                        fieldName,
                        DynamicHelpers.GetPythonType(source).Name);
                }
                if (astType == typeof(Constant)
                    && !source._isParsedAst
                    && fieldName == "value"
                    && FindInvalidConstantValue(
                        fieldValue,
                        out object invalidConstant)) {
                    throw PythonOps.TypeError(
                        "got an invalid type in Constant: {0}",
                        PythonOps.GetPythonTypeName(invalidConstant));
                }
                ValidateIdentifierField(
                    astType,
                    fieldName,
                    fieldValue);
                arguments[i] = ConvertToPublicValue(
                    fieldValue,
                    parameters[i].ParameterType);
            }

            if (locationCount != 0) {
                source.GetLocation(
                    out int? line,
                    out int? column,
                    out int? endLine,
                    out int? endColumn);
                bool validatePosition =
                    (source is stmt or expr)
                    && source is not Import
                    && source is not ImportFrom;
                if (validatePosition
                    && (line < 0
                        || column < 0
                        || endLine is < 0
                        || endColumn is < 0
                        || line != null
                            && endLine != null
                            && endLine < line
                        || line == endLine
                            && column != null
                            && endColumn != null
                            && endColumn < column)) {
                    throw PythonOps.ValueError(
                        "invalid position information");
                }
                arguments[fieldCount] = line ?? 0;
                arguments[fieldCount + 1] = column ?? 0;
                arguments[fieldCount + 2] = endLine;
                arguments[fieldCount + 3] = endColumn;
            }

            return (PublicAstNode)constructor.Invoke(arguments);
        }

        private static object NormalizeParsedConstant(object value) {
            if (value is byte[] bytes) {
                return new IronPython.Runtime.Bytes(bytes);
            }
            if (value is IEnumerable values
                && value is not string
                && value is not byte[]
                && value is not IronPython.Runtime.Bytes
                && value is not PythonTuple
                && value is not FrozenSetCollection) {
                object[] items = values.Cast<object>().ToArray();
                if (items.All(item =>
                    item is byte
                    || item is int integer
                        && integer >= 0
                        && integer <= 255)) {
                    return new IronPython.Runtime.Bytes(
                        items.Select(item => item is byte b
                            ? b
                            : checked((byte)(int)item)));
                }
            }
            return value;
        }

        private static void ValidateIdentifierField(
            Type astType,
            string fieldName,
            object fieldValue) {
            if (!IdentifierFieldNames.Contains(fieldName ?? string.Empty)) {
                return;
            }
            bool isIdentifier = fieldName switch {
                "name" => astType != typeof(TypeAlias),
                _ => true
            };
            if (!isIdentifier) {
                return;
            }
            if (fieldName == "names"
                && fieldValue is AST) {
                return;
            }
            if (fieldValue is Extensible<string> extensibleName) {
                ValidateIdentifierField(
                    astType,
                    fieldName,
                    extensibleName.Value);
                return;
            }
            if (fieldValue is not null
                && fieldValue is not string
                && fieldValue is not IEnumerable) {
                throw PythonOps.TypeError(
                    "identifier must be of type str ({0}.{1}: {2})",
                    astType.Name,
                    fieldName,
                    fieldValue.GetType().FullName);
            }
            if (fieldValue is string name
                && name is "True" or "False" or "None") {
                throw PythonOps.ValueError(
                    "identifier field can't represent '{0}' constant",
                    name);
            }
            if (fieldValue is IEnumerable values
                && fieldValue is not string) {
                foreach (object value in values) {
                    ValidateIdentifierField(
                        astType,
                        fieldName,
                        value);
                }
            }
        }

        private static bool IsNullValueAllowed(
            Type astType,
            string fieldName) {
            return astType == typeof(Constant)
                    && fieldName == "value"
                || astType == typeof(MatchSingleton)
                    && fieldName == "value";
        }

        private static bool FindInvalidConstantValue(
            object value,
            out object invalid) {
            if (value is null
                or string
                or IronPython.Runtime.Bytes
                or bool
                or int
                or BigInteger
                or double
                or Complex
                or IronPython.Runtime.Types.Ellipsis) {
                invalid = null;
                return false;
            }
            if (value is PythonTuple tuple) {
                foreach (object item in tuple) {
                    if (FindInvalidConstantValue(item, out invalid)) {
                        return true;
                    }
                }
                invalid = null;
                return false;
            }
            if (value is FrozenSetCollection frozenSet) {
                foreach (object item in frozenSet) {
                    if (FindInvalidConstantValue(item, out invalid)) {
                        return true;
                    }
                }
                invalid = null;
                return false;
            }
            invalid = value;
            return true;
        }

        private static bool HasLocationParameters(
            System.Reflection.ParameterInfo[] parameters) {
            if (parameters.Length < 4) {
                return false;
            }
            Type[] types = parameters
                .Skip(parameters.Length - 4)
                .Select(parameter => parameter.ParameterType)
                .ToArray();
            return types[0] == typeof(int)
                && types[1] == typeof(int)
                && (types[2] == typeof(int?)
                    && types[3] == typeof(int?)
                    || types[2] == typeof(int)
                    && types[3] == typeof(int));
        }

        private static object ConvertToPublicValue(
            object value,
            Type targetType) {
            if (value is null) {
                return null;
            }
            if (value is AST node) {
                return ConvertToPublicAst(node);
            }
            if (targetType.IsArray) {
                var values = ((IEnumerable)value).Cast<object>().ToArray();
                Array result = Array.CreateInstance(
                    targetType.GetElementType(),
                    values.Length);
                for (int i = 0; i < values.Length; i++) {
                    result.SetValue(
                        ConvertToPublicValue(
                            values[i],
                            targetType.GetElementType()),
                        i);
                }
                return result;
            }
            if (targetType.IsGenericType
                && targetType.GetGenericTypeDefinition()
                    == typeof(IReadOnlyList<>)) {
                Type itemType = targetType.GetGenericArguments()[0];
                object[] values = ((IEnumerable)value)
                    .Cast<object>()
                    .Select(item => ConvertToPublicValue(item, itemType))
                    .ToArray();
                Array result = Array.CreateInstance(itemType, values.Length);
                for (int i = 0; i < values.Length; i++) {
                    result.SetValue(values[i], i);
                }
                return result;
            }
            return value;
        }

        private static Type GetAstSystemType(AST source) {
            Type type = source.GetType();
            while (type is not null
                && type.DeclaringType != typeof(_ast)) {
                type = type.BaseType;
            }
            return type ?? source.GetType();
        }

        private static void CheckForCycles(AST source) {
            var active = new HashSet<AST>(
                ReferenceEqualityComparer<AST>.Instance);

            void Visit(AST node) {
                if (!active.Add(node)) {
                    throw PythonOps.RecursionError(
                        "maximum recursion depth exceeded");
                }
                foreach (object field in node._fields) {
                    if (field is not string name) {
                        continue;
                    }
                    object value = node.GetType().GetProperty(name)
                        ?.GetValue(node, null);
                    if (value is AST child) {
                        Visit(child);
                    } else if (value is IEnumerable values
                        && value is not string
                        && value is not byte[]) {
                        foreach (object item in values) {
                            if (item is AST itemNode) {
                                Visit(itemNode);
                            }
                        }
                    }
                }
                active.Remove(node);
            }

            Visit(source);
        }

        private static void ValidateAstTree(AST source) {
            switch (source) {
                case Module module:
                    ValidateStatements(module.body);
                    break;
                case Interactive interactive:
                    ValidateStatements(interactive.body);
                    break;
                case Expression expression:
                    ValidateExpr(expression.body, expectedContext: Load.Instance);
                    break;
            }
        }

        private static void ValidateStatements(PythonList statements) {
            for (int i = 0; i < statements.Count; i++) {
                if (statements[i] is not stmt statement) {
                    throw InvalidAst("None disallowed");
                }
                ValidateStatement(statement);
            }
        }

        private static void ValidateStatement(stmt statement) {
            switch (statement) {
                case FunctionDef function:
                    ValidateBody(function.body, "FunctionDef");
                    ValidateArguments(function.args);
                    ValidateExprList(function.decorator_list);
                    ValidateExpr(function.returns, Load.Instance);
                    break;
                case AsyncFunctionDef function:
                    ValidateBody(function.body, "AsyncFunctionDef");
                    ValidateArguments(function.args);
                    ValidateExprList(function.decorator_list);
                    ValidateExpr(function.returns, Load.Instance);
                    break;
                case ClassDef @class:
                    ValidateBody(@class.body, "ClassDef");
                    ValidateExprList(@class.bases);
                    foreach (object item in @class.keywords) {
                        ValidateKeyword(item);
                    }
                    ValidateExprList(@class.decorator_list);
                    break;
                case Return @return:
                    ValidateExpr(@return.value);
                    break;
                case Delete delete:
                    RequireNonEmpty(delete.targets, "empty targets on Delete");
                    ValidateTargets(delete.targets, Del.Instance, "Del");
                    break;
                case Assign assign:
                    RequireNonEmpty(assign.targets, "empty targets on Assign");
                    ValidateTargets(assign.targets, Store.Instance, "Store");
                    ValidateExpr(assign.value, Load.Instance);
                    break;
                case AnnAssign annAssign:
                    ValidateExpr(
                        annAssign.target,
                        Store.Instance,
                        "Store");
                    ValidateExpr(annAssign.annotation, Load.Instance);
                    ValidateExpr(annAssign.value, Load.Instance);
                    break;
                case AugAssign augAssign:
                    ValidateExpr(
                        augAssign.target,
                        Store.Instance,
                        "Store");
                    ValidateExpr(augAssign.value, Load.Instance);
                    break;
                case For @for:
                    ValidateBody(@for.body, "For");
                    ValidateExpr(
                        @for.target,
                        Store.Instance,
                        "Store");
                    ValidateExpr(@for.iter, Load.Instance);
                    ValidateStatements(@for.orelse);
                    break;
                case AsyncFor @for:
                    ValidateBody(@for.body, "AsyncFor");
                    ValidateExpr(
                        @for.target,
                        Store.Instance,
                        "Store");
                    ValidateExpr(@for.iter, Load.Instance);
                    ValidateStatements(@for.orelse);
                    break;
                case While @while:
                    ValidateBody(@while.body, "While");
                    ValidateExpr(@while.test, Load.Instance);
                    ValidateStatements(@while.orelse);
                    break;
                case If @if:
                    ValidateBody(@if.body, "If");
                    ValidateExpr(@if.test, Load.Instance);
                    ValidateStatements(@if.orelse);
                    break;
                case With @with:
                    RequireNonEmpty(@with.items, "empty items on With");
                    ValidateBody(@with.body, "With");
                    ValidateWithItems(@with.items);
                    break;
                case AsyncWith @with:
                    RequireNonEmpty(@with.items, "empty items on AsyncWith");
                    ValidateBody(@with.body, "AsyncWith");
                    ValidateWithItems(@with.items);
                    break;
                case Raise raise:
                    if (raise.exc is null && raise.cause is not null) {
                        throw InvalidAst(
                            "Raise with cause but no exception");
                    }
                    ValidateExpr(raise.exc, Load.Instance);
                    ValidateExpr(raise.cause, Load.Instance);
                    break;
                case Try @try:
                    ValidateTry(
                        @try.body,
                        @try.handlers,
                        @try.orelse,
                        @try.finalbody,
                        "Try");
                    break;
                case TryStar @try:
                    ValidateTry(
                        @try.body,
                        @try.handlers,
                        @try.orelse,
                        @try.finalbody,
                        "TryStar");
                    break;
                case Assert assert:
                    ValidateExpr(assert.test, Load.Instance);
                    ValidateExpr(assert.msg, Load.Instance);
                    break;
                case Import import:
                    RequireNonEmpty(import.names, "empty names on Import");
                    break;
                case ImportFrom importFrom:
                    if (importFrom.level < 0) {
                        throw InvalidAst("Negative ImportFrom level");
                    }
                    RequireNonEmpty(
                        importFrom.names,
                        "empty names on ImportFrom");
                    break;
                case Global global:
                    RequireNonEmpty(global.names, "empty names on Global");
                    break;
                case Nonlocal nonlocal:
                    RequireNonEmpty(
                        nonlocal.names,
                        "empty names on Nonlocal");
                    break;
                case Expr expression:
                    ValidateExpr(expression.value, Load.Instance);
                    break;
                case Match match:
                    ValidateExpr(match.subject, Load.Instance);
                    foreach (object item in match.cases) {
                        if (item is not match_case matchCase) {
                            throw InvalidAst("None disallowed");
                        }
                        if (matchCase.pattern is null) {
                            throw InvalidAst(
                                "field 'pattern' is required for match_case");
                        }
                        ValidatePattern(matchCase.pattern);
                        ValidateExpr(matchCase.guard, Load.Instance);
                        ValidateStatements(matchCase.body);
                    }
                    break;
                case TypeAlias typeAlias:
                    ValidateExpr(
                        typeAlias.name,
                        Store.Instance,
                        "Store");
                    ValidateExpr(typeAlias.value, Load.Instance);
                    break;
            }
        }

        private static void ValidateTry(
            PythonList body,
            PythonList handlers,
            PythonList orelse,
            PythonList finalBody,
            string name) {
            ValidateBody(body, name);
            foreach (object item in handlers) {
                if (item is not ExceptHandler handler) {
                    throw InvalidAst("None disallowed");
                }
                ValidateBody(handler.body, "ExceptHandler");
                ValidateExpr(handler.type, Load.Instance);
            }
            if (handlers.Count == 0 && finalBody.Count == 0) {
                throw InvalidAst(
                    name + " has neither except handlers nor finalbody");
            }
            if (handlers.Count == 0 && orelse.Count != 0) {
                throw InvalidAst(
                    name + " has orelse but no except handlers");
            }
            ValidateStatements(orelse);
            ValidateStatements(finalBody);
        }

        private static void ValidateWithItems(PythonList items) {
            foreach (object item in items) {
                if (item is not withitem withItem) {
                    throw InvalidAst("None disallowed");
                }
                ValidateExpr(withItem.context_expr, Load.Instance);
                ValidateExpr(
                    withItem.optional_vars,
                    Store.Instance,
                    "Store");
            }
        }

        private static void ValidateArguments(arguments arguments) {
            if (arguments is null) {
                throw InvalidAst("None disallowed");
            }
            ValidateArgList(arguments.posonlyargs);
            ValidateArgList(arguments.args);
            ValidateArgList(arguments.kwonlyargs);
            ValidateArg(arguments.vararg, allowNull: true);
            ValidateArg(arguments.kwarg, allowNull: true);
            int positionalCount =
                arguments.posonlyargs.Count + arguments.args.Count;
            if (arguments.defaults.Count > positionalCount) {
                throw InvalidAst(
                    "more positional defaults than args");
            }
            if (arguments.kw_defaults.Count != arguments.kwonlyargs.Count) {
                throw InvalidAst(
                    "length of kwonlyargs is not the same as kw_defaults");
            }
            ValidateExprList(arguments.defaults, allowNull: true);
            foreach (object item in arguments.kw_defaults) {
                if (item is null) {
                    continue;
                }
                ValidateExpr(item, Load.Instance);
            }
        }

        private static void ValidateArgList(PythonList args) {
            foreach (object item in args) {
                ValidateArg(item, allowNull: false);
            }
        }

        private static void ValidateArg(object item, bool allowNull) {
            if (item is null) {
                if (allowNull) {
                    return;
                }
                throw InvalidAst("None disallowed");
            }
            if (item is not ArgType arg) {
                throw InvalidAst("None disallowed");
            }
            ValidateExpr(arg.annotation, Load.Instance);
        }

        private static void ValidateKeyword(object item) {
            if (item is not keyword keyword) {
                throw InvalidAst("None disallowed");
            }
            ValidateExpr(keyword.value, Load.Instance);
        }

        private static void ValidateExprList(
            PythonList values,
            bool allowNull = false) {
            foreach (object item in values) {
                if (item is null && !allowNull) {
                    throw InvalidAst("None disallowed");
                }
                ValidateExpr(item, Load.Instance);
            }
        }

        private static void ValidateTargets(
            PythonList targets,
            expr_context context,
            string contextName) {
            foreach (object item in targets) {
                if (item is null) {
                    throw InvalidAst("None disallowed");
                }
                ValidateExpr(item, context, contextName);
            }
        }

        private static void ValidateExpr(
            object value,
            expr_context expectedContext = null,
            string contextName = "Load") {
            if (value is null) {
                return;
            }
            if (value is not expr expression) {
                throw InvalidAst("None disallowed");
            }

            if (expression is Name or Attribute
                or Subscript or List or Tuple or Starred) {
                expr_context actualContext = expression switch {
                    Name name => name.ctx,
                    Attribute attribute => attribute.ctx,
                    Subscript subscript => subscript.ctx,
                    List list => list.ctx,
                    Tuple tuple => tuple.ctx,
                    Starred starred => starred.ctx,
                    _ => null
                };
                if (expectedContext is not null
                    && actualContext?.GetType() != expectedContext.GetType()) {
                    throw InvalidAst(
                        "expression must have " + contextName + " context");
                }
                if (actualContext is null) {
                    throw InvalidAst("expression must have " + contextName + " context");
                }
            } else if (expectedContext is Store or Del) {
                throw InvalidAst(
                    "expression which can't be assigned to in "
                    + contextName + " context");
            }

            switch (expression) {
                case BoolOp boolean:
                    if (boolean.values.Count < 2) {
                        throw InvalidAst("BoolOp with less than 2 values");
                    }
                    ValidateExprList(boolean.values);
                    break;
                case BinOp binary:
                    ValidateExpr(binary.left, Load.Instance);
                    ValidateExpr(binary.right, Load.Instance);
                    break;
                case UnaryOp unary:
                    ValidateExpr(unary.operand, Load.Instance);
                    break;
                case Lambda lambda:
                    ValidateArguments(lambda.args);
                    ValidateExpr(lambda.body, Load.Instance);
                    break;
                case IfExp ifExp:
                    ValidateExpr(ifExp.test, Load.Instance);
                    ValidateExpr(ifExp.body, Load.Instance);
                    ValidateExpr(ifExp.orelse, Load.Instance);
                    break;
                case Dict dict:
                    if (dict.keys.Count != dict.values.Count) {
                        throw InvalidAst(
                            "Dict keys and values must have the same number of keys as values");
                    }
                    foreach (object key in dict.keys) {
                        ValidateExpr(key, Load.Instance);
                    }
                    ValidateExprList(dict.values);
                    break;
                case Set set:
                    ValidateExprList(set.elts);
                    break;
                case List list:
                    ValidateTargetsOrExprs(list.elts, list.ctx);
                    break;
                case Tuple tuple:
                    ValidateTargetsOrExprs(tuple.elts, tuple.ctx);
                    break;
                case Starred starred:
                    ValidateExpr(
                        starred.value,
                        starred.ctx,
                        starred.ctx is Store
                            ? "Store"
                            : starred.ctx is Del
                                ? "Del"
                                : "Load");
                    break;
                case Attribute attribute:
                    ValidateExpr(attribute.value, Load.Instance);
                    break;
                case Subscript subscript:
                    ValidateExpr(subscript.value, Load.Instance);
                    ValidateExpr(subscript.slice, Load.Instance);
                    break;
                case Slice slice:
                    ValidateExpr(slice.lower, Load.Instance);
                    ValidateExpr(slice.upper, Load.Instance);
                    ValidateExpr(slice.step, Load.Instance);
                    break;
                case Call call:
                    ValidateExpr(call.func, Load.Instance);
                    ValidateExprList(call.args);
                    foreach (object item in call.keywords) {
                        ValidateKeyword(item);
                    }
                    break;
                case Compare compare:
                    if (compare.comparators.Count == 0) {
                        throw InvalidAst("Compare has no comparators");
                    }
                    if (compare.ops.Count != compare.comparators.Count) {
                        throw InvalidAst(
                            "Compare has different number of comparators and operands");
                    }
                    ValidateExpr(compare.left, Load.Instance);
                    ValidateExprList(compare.comparators);
                    break;
                case ListComp comp:
                    ValidateComprehensions(comp.generators);
                    ValidateExpr(comp.elt, Load.Instance);
                    break;
                case SetComp comp:
                    ValidateComprehensions(comp.generators);
                    ValidateExpr(comp.elt, Load.Instance);
                    break;
                case DictComp comp:
                    ValidateComprehensions(comp.generators);
                    ValidateExpr(comp.key, Load.Instance);
                    ValidateExpr(comp.value, Load.Instance);
                    break;
                case GeneratorExp comp:
                    ValidateComprehensions(comp.generators);
                    ValidateExpr(comp.elt, Load.Instance);
                    break;
                case Yield yield:
                    ValidateExpr(yield.value, Load.Instance);
                    break;
                case YieldFrom yieldFrom:
                    ValidateExpr(yieldFrom.value, Load.Instance);
                    break;
                case Await await:
                    ValidateExpr(await.value, Load.Instance);
                    break;
                case JoinedStr joined:
                    ValidateExprList(joined.values);
                    break;
                case FormattedValue formatted:
                    ValidateExpr(formatted.value, Load.Instance);
                    ValidateExpr(formatted.format_spec, Load.Instance);
                    break;
            }
        }

        private static void ValidateTargetsOrExprs(
            PythonList values,
            expr_context context) {
            foreach (object item in values) {
                if (item is null) {
                    throw InvalidAst("None disallowed");
                }
                ValidateExpr(
                    item,
                    context is Store
                        ? Store.Instance
                        : context is Del
                            ? Del.Instance
                            : Load.Instance,
                    context is Store
                        ? "Store"
                        : context is Del
                            ? "Del"
                            : "Load");
            }
        }

        private static void ValidateComprehensions(PythonList generators) {
            if (generators.Count == 0) {
                throw InvalidAst("comprehension with no generators");
            }
            foreach (object item in generators) {
                if (item is not comprehension comprehension) {
                    throw InvalidAst("None disallowed");
                }
                ValidateExpr(
                    comprehension.target,
                    Store.Instance,
                    "Store");
                ValidateExpr(comprehension.iter, Load.Instance);
                ValidateExprList(comprehension.ifs);
            }
        }

        private static void ValidatePattern(
            pattern pattern,
            bool allowStar = false) {
            if (pattern is null) {
                throw InvalidAst("None disallowed");
            }
            switch (pattern) {
                case MatchValue value:
                    if (!IsLiteralPattern(value.value)) {
                        throw InvalidAst("invalid pattern");
                    }
                    break;
                case MatchSingleton singleton:
                    if (singleton.value is not null
                        && singleton.value is not bool) {
                        throw InvalidAst("invalid MatchSingleton value");
                    }
                    break;
                case MatchSequence sequence:
                    bool foundStar = false;
                    foreach (object item in sequence.patterns) {
                        if (item is not pattern nested) {
                            throw InvalidAst("None disallowed");
                        }
                        if (nested is MatchStar) {
                            if (foundStar) {
                                throw InvalidAst(
                                    "multiple starred patterns");
                            }
                            foundStar = true;
                        }
                        ValidatePattern(
                            nested,
                            allowStar: nested is MatchStar);
                    }
                    break;
                case MatchMapping mapping:
                    if (mapping.keys.Count != mapping.patterns.Count) {
                        throw InvalidAst(
                            "MatchMapping keys and patterns must have the same length");
                    }
                    foreach (object key in mapping.keys) {
                        if (!IsLiteralPattern(key)) {
                            throw InvalidAst("invalid mapping key");
                        }
                    }
                    foreach (object item in mapping.patterns) {
                        ValidatePattern(item as pattern);
                    }
                    ValidateCaptureName(mapping.rest);
                    break;
                case MatchClass @class:
                    if (!IsClassPatternTarget(@class.cls)) {
                        throw InvalidAst("invalid class pattern");
                    }
                    if (@class.kwd_attrs.Count != @class.kwd_patterns.Count) {
                        throw InvalidAst(
                            "MatchClass keyword attributes and patterns must have the same length");
                    }
                    foreach (object item in @class.patterns) {
                        if (item is MatchStar
                            || item is not pattern classPattern) {
                            throw InvalidAst("invalid class pattern");
                        }
                        ValidatePattern(classPattern);
                    }
                    foreach (object item in @class.kwd_attrs) {
                        ValidateIdentifierValue(item);
                    }
                    foreach (object item in @class.kwd_patterns) {
                        if (item is MatchStar) {
                            throw InvalidAst("invalid class pattern");
                        }
                        ValidatePattern(item as pattern);
                    }
                    break;
                case MatchStar star:
                    if (!allowStar) {
                        throw InvalidAst(
                            "MatchStar is only valid in a sequence pattern");
                    }
                    ValidateCaptureName(star.name);
                    break;
                case MatchAs @as:
                    ValidateCaptureName(@as.name);
                    if (@as.pattern is not null) {
                        ValidatePattern(@as.pattern);
                    }
                    break;
                case MatchOr or:
                    if (or.patterns.Count < 2) {
                        throw InvalidAst(
                            "MatchOr requires at least 2 patterns");
                    }
                    foreach (object item in or.patterns) {
                        ValidatePattern(item as pattern);
                    }
                    for (int i = 0; i < or.patterns.Count - 1; i++) {
                        if (IsIrrefutablePattern(or.patterns[i] as pattern)) {
                            throw InvalidAst(
                                "irrefutable pattern is not allowed here");
                        }
                    }
                    break;
            }
        }

        private static bool IsLiteralPattern(object value) {
            if (value is Constant constant) {
                return constant.value is not null
                    && constant.value is not bool
                    && constant.value is not IronPython.Runtime.Types.Ellipsis
                    && constant.value is not PythonTuple
                    && constant.value is not FrozenSetCollection;
            }
            if (value is Attribute attribute) {
                return IsLoadNameOrAttribute(attribute);
            }
            if (value is BinOp binary) {
                return IsLiteralPattern(binary.left)
                    && IsLiteralPattern(binary.right)
                    && binary.op is Add or Sub;
            }
            if (value is UnaryOp unary) {
                return IsLiteralPattern(unary.operand)
                    && unary.op is UAdd or USub;
            }
            return false;
        }

        private static bool IsClassPatternTarget(object value) {
            return value is Name { ctx: Load }
                || value is Attribute attribute
                    && IsLoadNameOrAttribute(attribute);
        }

        private static bool IsLoadNameOrAttribute(expr value) =>
            value switch {
                Name name => name.ctx is Load,
                Attribute attribute => attribute.ctx is Load
                    && IsLoadNameOrAttribute(attribute.value),
                _ => false
            };

        private static bool IsIrrefutablePattern(pattern pattern) =>
            pattern is MatchAs { pattern: null };

        private static void ValidateCaptureName(string name) {
            if (name is not null) {
                ValidateIdentifierValue(name);
            }
        }

        private static void ValidateIdentifierValue(object value) {
            if (value is not string name
                || name is "_" or "True" or "False" or "None") {
                throw InvalidAst("invalid identifier");
            }
        }

        private static void ValidateBody(PythonList body, string name) {
            if (body.Count == 0) {
                throw InvalidAst("empty body on " + name);
            }
            ValidateStatements(body);
        }

        private static void RequireNonEmpty(
            PythonList values,
            string message) {
            if (values.Count == 0) {
                throw InvalidAst(message);
            }
        }

        internal static Exception InvalidAst(string message) =>
            PythonOps.ValueError(message);

        internal static AST BuildAst(
            CodeContext context,
            SourceUnit sourceUnit,
            PythonCompilerOptions opts,
            string mode,
            int optimizeLevel = -1,
            int featureVersion = -1,
            bool typeComments = false) {
            using (Parser parser = Parser.CreateParser(
                new CompilerContext(sourceUnit, opts, ThrowingErrorSink.Default),
                (PythonOptions)context.LanguageContext.Options)) {

                string sourceText = sourceUnit.GetCode() ?? string.Empty;
                if (featureVersion >= 0) {
                    ValidatePep758Source(
                        sourceText,
                        featureVersion);
                }
                AST ast = ConvertPublicAst(
                    parser.ParsePublicAst(mode, featureVersion));
                ApplyConstantKinds(ast, sourceText);
                if (featureVersion >= 0) {
                    ValidateFeatureVersion(ast, featureVersion);
                }
                if (typeComments
                    && ast is Module module) {
                    AddTypeIgnores(
                        module,
                        sourceText);
                }
                NormalizeAstSpansRecursive(ast, sourceText);
                if (optimizeLevel > 0) {
                    OptimizeAst(ast, optimizeLevel);
                }
                return ast;
            }
        }

        internal static AST OptimizeAst(AST node, int optimizeLevel) {
            foreach (object field in node._fields.ToArray()) {
                if (field is not string name
                    || !node.TryGetFieldValue(name, out object value)) {
                    continue;
                }
                if (value is AST child) {
                    AST optimizedChild = OptimizeAst(
                        child,
                        optimizeLevel);
                    if (!ReferenceEquals(child, optimizedChild)) {
                        node.SetFieldValue(
                            DefaultContext.Default,
                            name,
                            optimizedChild);
                    }
                } else if (value is PythonList list) {
                    for (int i = 0; i < list.Count; i++) {
                        if (list[i] is AST item) {
                            list[i] = OptimizeAst(
                                item,
                                optimizeLevel);
                        }
                    }
                }
            }

            if (node is UnaryOp unary
                && unary.operand is Constant unaryConstant
                && IsFoldedNumber(unaryConstant.value)) {
                object value = unary.op is USub
                    ? PythonOps.Negate(unaryConstant.value)
                    : unaryConstant.value;
                return CreateFoldedConstant(node, value);
            }

            if (node is BinOp binary
                && TryFoldBinary(binary, out object foldedValue)) {
                return CreateFoldedConstant(node, foldedValue);
            }

            if (node is BinOp formatting
                && formatting.op is Mod
                && formatting.left is Constant
                    {
                        value: "%s"
                    }
                && formatting.right is Tuple
                    {
                        elts.Count: 1
                    } tuple) {
                var formattedValue = new FormattedValue(
                    (expr)tuple.elts[0],
                    115,
                    null);
                CopyLocation(formattedValue, tuple.elts[0] as AST);
                var joined = new JoinedStr();
                joined.values = new PythonList {
                    formattedValue
                };
                CopyLocation(joined, node);
                return joined;
            }

            if (node is Name { id: "__debug__" }) {
                return CreateFoldedConstant(node, false);
            }

            if (optimizeLevel >= 2) {
                RemoveDocstring(node);
            }
            return node;
        }

        private static void ApplyConstantKinds(
            AST node,
            string source) {
            if (node is Constant { value: string } constant) {
                constant.GetLocation(
                    out int? line,
                    out int? column,
                    out _,
                    out _);
                if (line is int lineNumber
                    && column is int columnOffset) {
                    string[] lines = source.Split(
                        new[] { "\r\n", "\n", "\r" },
                        StringSplitOptions.None);
                    if (lineNumber > 0
                        && lineNumber <= lines.Length
                        && columnOffset >= 0
                        && columnOffset < lines[lineNumber - 1].Length
                        && lines[lineNumber - 1][columnOffset]
                            is 'u' or 'U') {
                        constant.kind = "u";
                        constant.__dict__["kind"] = "u";
                    }
                }
            }
            foreach (object field in node._fields) {
                if (field is not string name
                    || !node.TryGetFieldValue(name, out object value)) {
                    continue;
                }
                if (value is AST child) {
                    ApplyConstantKinds(child, source);
                } else if (value is IEnumerable values
                    && value is not string
                    && value is not byte[]) {
                    foreach (object item in values) {
                        if (item is AST itemNode) {
                            ApplyConstantKinds(itemNode, source);
                        }
                    }
                }
            }
        }

        private static void AddTypeIgnores(
            Module module,
            string source) {
            string[] lines = source.Split(
                new[] { "\r\n", "\n", "\r" },
                StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++) {
                const string marker = "# type: ignore";
                int comment = lines[i].IndexOf(
                    marker,
                    StringComparison.Ordinal);
                if (comment < 0) {
                    continue;
                }
                string tag = lines[i]
                    .Substring(comment + marker.Length)
                    .Trim();
                module.type_ignores.Add(
                    new TypeIgnore(i + 1, tag));
            }
        }

        private static void ValidateFeatureVersion(
            AST node,
            int featureVersion) {
            switch (node) {
                case NamedExpr when featureVersion < 8:
                    throw PythonOps.SyntaxError(
                        "assignment expressions require Python 3.8");
                case Match when featureVersion < 10:
                    throw PythonOps.SyntaxError(
                        "match statements require Python 3.10");
                case TryStar when featureVersion < 11:
                    throw PythonOps.SyntaxError(
                        "exception groups require Python 3.11");
                case TypeAlias when featureVersion < 12:
                    throw PythonOps.SyntaxError(
                        "type alias statements require Python 3.12");
                case TemplateStr when featureVersion < 14:
                case Interpolation when featureVersion < 14:
                    throw PythonOps.SyntaxError(
                        "template strings require Python 3.14");
                case FunctionDef function:
                    if (function.type_params.Count != 0
                        && featureVersion < 12) {
                        throw PythonOps.SyntaxError(
                            "type parameters require Python 3.12");
                    }
                    ValidatePositionalOnlyVersion(
                        function.args,
                        featureVersion);
                    break;
                case AsyncFunctionDef function:
                    if (function.type_params.Count != 0
                        && featureVersion < 12) {
                        throw PythonOps.SyntaxError(
                            "type parameters require Python 3.12");
                    }
                    ValidatePositionalOnlyVersion(
                        function.args,
                        featureVersion);
                    break;
                case ClassDef @class:
                    if (@class.type_params.Count != 0
                        && featureVersion < 12) {
                        throw PythonOps.SyntaxError(
                            "type parameters require Python 3.12");
                    }
                    break;
                case Lambda lambda:
                    ValidatePositionalOnlyVersion(
                        lambda.args,
                        featureVersion);
                    break;
                case TypeVar { default_value: not null }
                    when featureVersion < 13:
                case ParamSpec { default_value: not null }
                    when featureVersion < 13:
                case TypeVarTuple { default_value: not null }
                    when featureVersion < 13:
                    throw PythonOps.SyntaxError(
                        "type parameter defaults require Python 3.13");
            }

            foreach (object field in node._fields) {
                if (field is not string name
                    || !node.TryGetFieldValue(name, out object value)) {
                    continue;
                }
                if (value is AST child) {
                    ValidateFeatureVersion(child, featureVersion);
                } else if (value is IEnumerable values
                    && value is not string
                    && value is not byte[]) {
                    foreach (object item in values) {
                        if (item is AST itemNode) {
                            ValidateFeatureVersion(
                                itemNode,
                                featureVersion);
                        }
                    }
                }
            }
        }

        private static void ValidatePositionalOnlyVersion(
            arguments arguments,
            int featureVersion) {
            if (featureVersion < 8
                && arguments.posonlyargs.Count != 0) {
                throw PythonOps.SyntaxError(
                    "positional-only parameters require Python 3.8");
            }
        }

        private static void ValidatePep758Source(
            string source,
            int featureVersion) {
            if (featureVersion >= 14) {
                return;
            }
            string[] lines = source.Split(
                new[] { "\r\n", "\n", "\r" },
                StringSplitOptions.None);
            foreach (string line in lines) {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith(
                    "except",
                    StringComparison.Ordinal)) {
                    continue;
                }
                int colon = trimmed.IndexOf(':');
                string header = colon < 0
                    ? trimmed
                    : trimmed.Substring(0, colon);
                int comma = header.IndexOf(',');
                int openParen = header.IndexOf('(');
                if (comma >= 0
                    && (openParen < 0 || comma < openParen)) {
                    throw PythonOps.SyntaxError(
                        "multiple unparenthesized exception types require Python 3.14");
                }
            }
        }

        private static void NormalizeAstSpansRecursive(
            AST node,
            string source) {
            foreach (object field in node._fields) {
                if (field is not string name
                    || !node.TryGetFieldValue(name, out object value)) {
                    continue;
                }
                if (value is AST child) {
                    NormalizeAstSpansRecursive(child, source);
                } else if (value is IEnumerable values
                    && value is not string
                    && value is not byte[]) {
                    foreach (object item in values) {
                        if (item is AST itemNode) {
                            NormalizeAstSpansRecursive(
                                itemNode,
                                source);
                        }
                    }
                }
            }
            NormalizeAstSpan(node);
            NormalizeAstSpanFromSource(node, source);
            if (node is Constant constant
                && constant._isParsedAst
                && constant.value is PythonList bytes
                && bytes.All(item =>
                    Converter.TryConvertToInt32(
                        item,
                        out int intValue)
                    && intValue >= 0
                    && intValue <= 255)) {
                var converted = new IronPython.Runtime.Bytes(
                    bytes.Select(item => {
                        Converter.TryConvertToInt32(
                            item,
                            out int intValue);
                        return checked((byte)intValue);
                    }));
                constant.value = converted;
                constant.__dict__["value"] = converted;
            }
        }

        private static bool TryFoldBinary(
            BinOp binary,
            out object value) {
            value = null;
            if (binary.left is not Constant left
                || binary.right is not Constant right
                || !IsFoldedNumber(left.value)
                || !IsFoldedNumber(right.value)) {
                return false;
            }

            if (left.value is Complex || right.value is Complex) {
                Complex leftValue = ToComplex(left.value);
                Complex rightValue = ToComplex(right.value);
                value = binary.op switch {
                    Add => leftValue + rightValue,
                    Sub => leftValue - rightValue,
                    _ => null
                };
                return value is not null;
            }

            if (left.value is double || right.value is double) {
                double leftValue = Convert.ToDouble(left.value);
                double rightValue = Convert.ToDouble(right.value);
                value = binary.op switch {
                    Add => leftValue + rightValue,
                    Sub => leftValue - rightValue,
                    _ => null
                };
                return value is not null;
            }

            BigInteger leftInteger = ToBigInteger(left.value);
            BigInteger rightInteger = ToBigInteger(right.value);
            value = binary.op switch {
                Add => leftInteger + rightInteger,
                Sub => leftInteger - rightInteger,
                _ => null
            };
            return value is not null;
        }

        private static bool IsFoldedNumber(object value) =>
            value is int or BigInteger or double or Complex;

        private static BigInteger ToBigInteger(object value) =>
            value is BigInteger integer
                ? integer
                : new BigInteger(Convert.ToInt64(value));

        private static Complex ToComplex(object value) =>
            value is Complex complex
                ? complex
                : new Complex(Convert.ToDouble(value), 0);

        private static Constant CreateFoldedConstant(
            AST location,
            object value) {
            var result = new Constant(value);
            CopyLocation(result, location);
            return result;
        }

        private static void CopyLocation(AST target, AST source) {
            if (source is null) {
                return;
            }
            source.GetLocation(
                out int? line,
                out int? column,
                out int? endLine,
                out int? endColumn);
            if (line is not null) target.lineno = line.Value;
            if (column is not null) target.col_offset = column.Value;
            if (endLine is not null) target.end_lineno = endLine.Value;
            if (endColumn is not null) target.end_col_offset = endColumn.Value;
            if (line is not null) target.__dict__["lineno"] = line.Value;
            if (column is not null) target.__dict__["col_offset"] = column.Value;
            if (endLine is not null) target.__dict__["end_lineno"] = endLine.Value;
            if (endColumn is not null) target.__dict__["end_col_offset"] = endColumn.Value;
        }

        private static void RemoveDocstring(AST node) {
            PythonList body = node switch {
                Module module => module.body,
                FunctionDef function => function.body,
                AsyncFunctionDef function => function.body,
                ClassDef @class => @class.body,
                _ => null
            };
            if (body is null
                || body.Count == 0
                || body[0] is not Expr
                    {
                        value: Constant
                        {
                            value: string
                        }
                    }) {
                return;
            }

            AST docstring = body[0] as AST;
            body.RemoveAt(0);
            if (body.Count == 0) {
                var pass = new Pass();
                docstring.GetLocation(
                    out int? line,
                    out int? column,
                    out _,
                    out _);
                if (line is not null) {
                    pass.lineno = line.Value;
                    pass.__dict__["lineno"] = line.Value;
                    pass.end_lineno = line.Value;
                    pass.__dict__["end_lineno"] = line.Value;
                }
                if (column is not null) {
                    pass.col_offset = column.Value;
                    pass.__dict__["col_offset"] = column.Value;
                    pass.end_col_offset = column.Value + 4;
                    pass.__dict__["end_col_offset"] = column.Value + 4;
                }
                body.Add(pass);
            }
        }

        private static AST ConvertPublicAst(PublicAstNode node) {
            string typeName = node.NodeKind == "arg"
                ? nameof(ArgType)
                : node.NodeKind;
            Type targetType = typeof(_ast).GetNestedTypes()
                .FirstOrDefault(type => type.Name == typeName);
            if (targetType is null
                || !typeof(AST).IsAssignableFrom(targetType)) {
                throw PythonOps.TypeError(
                    "unsupported AST node type: {0}",
                    node.NodeKind);
            }

            var result = (AST)Activator.CreateInstance(
                targetType,
                nonPublic: true);
            result._isParsedAst = true;
            foreach (IronPython.Compiler.Syntax.Ast.AstFieldValue field
                in node.GetFields()) {
                result.SetFieldValue(
                    DefaultContext.Default,
                    field.Name,
                    field.Name == "is_lazy"
                        && field.Value is null
                        && node.NodeKind is "Import" or "ImportFrom"
                        ? 0
                        : ConvertPublicAstValue(field.Value));
            }
            if (node.LineNumber is int lineNumber) {
                result.lineno = lineNumber;
                result.__dict__["lineno"] = lineNumber;
            }
            if (node.ColumnOffset is int columnOffset) {
                result.col_offset = columnOffset;
                result.__dict__["col_offset"] = columnOffset;
            }
            if (node.EndLineNumber is int endLineNumber) {
                result.end_lineno = endLineNumber;
                result.__dict__["end_lineno"] = endLineNumber;
            }
            if (node.EndColumnOffset is int endColumnOffset) {
                result.end_col_offset = endColumnOffset;
                result.__dict__["end_col_offset"] = endColumnOffset;
            }
            NormalizeAstSpan(result);
            return result;
        }

        private static void NormalizeAstSpan(AST node) {
            int? endLine = null;
            int? endColumn = null;
            foreach (object field in node._fields) {
                if (field is not string name
                    || !node.TryGetFieldValue(name, out object value)) {
                    continue;
                }
                IncludeSpan(value, ref endLine, ref endColumn);
            }

            if (node is Attribute attribute) {
                attribute.value.GetLocation(
                    out _,
                    out _,
                    out int? valueLine,
                    out int? valueColumn);
                if (valueLine is not null && valueColumn is not null) {
                    int attributeColumn = valueColumn.Value
                        + 1
                        + attribute.attr.Length;
                    IncludeSpan(
                        valueLine.Value,
                        attributeColumn,
                        ref endLine,
                        ref endColumn);
                }
            } else if (node is Subscript subscript) {
                subscript.slice.GetLocation(
                    out int? sliceLine,
                    out int? sliceColumn,
                    out int? sliceEndLine,
                    out int? sliceEndColumn);
                subscript.value.GetLocation(
                    out _,
                    out _,
                    out int? valueLine,
                    out int? valueColumn);
                node.GetLocation(
                    out _,
                    out _,
                    out int? subscriptEndLine,
                    out int? subscriptEndColumn);
                int desiredEndColumn = subscript.slice is Slice
                    ? Math.Max(
                        endColumn ?? 0,
                        subscriptEndColumn ?? 0)
                    : (endColumn ?? 0) + 1;
                node.end_lineno =
                    subscriptEndLine ?? sliceEndLine ?? valueLine ?? 0;
                node.end_col_offset = desiredEndColumn;
                if (node.end_lineno is int nodeEndLine) {
                    node.__dict__["end_lineno"] = nodeEndLine;
                }
                node.__dict__["end_col_offset"] = desiredEndColumn;
                endLine = node.end_lineno;
                endColumn = desiredEndColumn;
                int bracketColumn = desiredEndColumn - 1;
                int declaredSliceEndLine = subscript.slice.lineno;
                subscript.slice.end_lineno = declaredSliceEndLine;
                subscript.slice.end_col_offset = bracketColumn;
                subscript.slice.__dict__["end_lineno"] = declaredSliceEndLine;
                subscript.slice.__dict__["end_col_offset"] = bracketColumn;
                if (valueLine is not null
                    && valueColumn is not null) {
                    if (subscript.slice is Slice) {
                        int startColumn = valueColumn.Value + 1;
                        subscript.slice.lineno = valueLine.Value;
                        subscript.slice.col_offset = startColumn;
                        subscript.slice.__dict__["lineno"] = valueLine.Value;
                        subscript.slice.__dict__["col_offset"] = startColumn;
                    }
                }
            }

            if (node is TypeVarTuple typeVarTuple) {
                SetNodeEnd(
                    node,
                    typeVarTuple.name.Length + 1,
                    setAbsolute: false);
            } else if (node is ParamSpec paramSpec) {
                SetNodeEnd(
                    node,
                    paramSpec.name.Length + 2,
                    setAbsolute: false);
            }

            if (node is keyword keyword) {
                keyword.value.GetLocation(
                    out int? valueLine,
                    out int? valueColumn,
                    out _,
                    out _);
                if (valueLine is not null && valueColumn is not null) {
                    int startColumn = Math.Max(
                        0,
                        valueColumn.Value
                            - (keyword.arg is null
                                ? 2
                                : keyword.arg.Length + 1));
                    keyword.col_offset = startColumn;
                    keyword.__dict__["col_offset"] = startColumn;
                }
            } else if (node is Import import) {
                if (import.names.Count != 0
                    && import.names[0] is alias firstAlias) {
                    firstAlias.GetLocation(
                        out _,
                        out int? aliasColumn,
                        out _,
                        out _);
                    if (aliasColumn is not null) {
                        int startColumn = Math.Max(
                            0,
                            aliasColumn.Value
                                - (import.is_lazy == 1 ? 12 : 7));
                        node.col_offset = startColumn;
                        node.__dict__["col_offset"] = startColumn;
                    }
                }
            } else if (node is ImportFrom importFrom) {
                if (importFrom.names.Count != 0
                    && importFrom.names[0] is alias firstAlias) {
                    firstAlias.GetLocation(
                        out int? aliasLine,
                        out int? aliasColumn,
                        out _,
                        out _);
                    node.GetLocation(
                        out int? startLine,
                        out _,
                        out _,
                        out _);
                    if (aliasLine == startLine && aliasColumn is not null) {
                        int prefixLength =
                            (importFrom.is_lazy == 1 ? 5 : 0)
                            + 5
                            + (importFrom.level ?? 0)
                            + (importFrom.module?.Length ?? 0)
                            + 8;
                        int startColumn = Math.Max(
                            0,
                            aliasColumn.Value - prefixLength);
                        node.col_offset = startColumn;
                        node.__dict__["col_offset"] = startColumn;
                        if (firstAlias.name == "*"
                            && aliasColumn == 0) {
                            int aliasColumnValue =
                                startColumn + prefixLength;
                            firstAlias.lineno = startLine ?? 1;
                            firstAlias.col_offset = aliasColumnValue;
                            firstAlias.end_lineno = startLine ?? 1;
                            firstAlias.end_col_offset =
                                aliasColumnValue + 1;
                            firstAlias.__dict__["lineno"] =
                                firstAlias.lineno;
                            firstAlias.__dict__["col_offset"] =
                                firstAlias.col_offset;
                            firstAlias.__dict__["end_lineno"] =
                                firstAlias.end_lineno;
                            firstAlias.__dict__["end_col_offset"] =
                                firstAlias.end_col_offset;
                        }
                    }
                }
            } else if (node is TypeAlias typeAlias) {
                typeAlias.name.GetLocation(
                    out int? nameLine,
                    out int? nameColumn,
                    out _,
                    out _);
                if (nameLine is not null && nameColumn is not null) {
                    int startColumn = Math.Max(0, nameColumn.Value - 5);
                    node.lineno = nameLine.Value;
                    node.col_offset = startColumn;
                    node.__dict__["lineno"] = nameLine.Value;
                    node.__dict__["col_offset"] = startColumn;
                }
            } else if (node is Interpolation interpolation
                && interpolation.format_spec is TemplateStr templateSpec) {
                interpolation.format_spec = new JoinedStr(templateSpec.values);
                CopyLocation(
                    interpolation.format_spec,
                    templateSpec);
            } else if (node is Expr
                {
                    value: NamedExpr
                } namedExprStatement) {
                namedExprStatement.GetLocation(
                    out int? line,
                    out int? column,
                    out _,
                    out _);
                namedExprStatement.value.GetLocation(
                    out _,
                    out _,
                    out int? valueEndLine,
                    out int? valueEndColumn);
                if (line is not null
                    && column is not null
                    && valueEndLine == line
                    && valueEndColumn is not null) {
                    namedExprStatement.col_offset = Math.Max(
                        0,
                        column.Value - 1);
                    namedExprStatement.end_col_offset =
                        valueEndColumn.Value + 1;
                    namedExprStatement.__dict__["col_offset"] =
                        namedExprStatement.col_offset;
                    namedExprStatement.__dict__["end_col_offset"] =
                        namedExprStatement.end_col_offset;
                }
            }

            if (endLine is not null
                && endColumn is not null) {
                node.GetLocation(
                    out _,
                    out _,
                    out int? currentEndLine,
                    out int? currentEndColumn);
                if (currentEndLine is null
                    || currentEndLine < endLine
                    || currentEndLine == endLine
                    && currentEndColumn < endColumn) {
                    node.end_lineno = endLine.Value;
                    node.end_col_offset = endColumn.Value;
                    node.__dict__["end_lineno"] = endLine.Value;
                    node.__dict__["end_col_offset"] = endColumn.Value;
                }
            }
        }

        private static void NormalizeAstSpanFromSource(
            AST node,
            string source) {
            if (string.IsNullOrEmpty(source)) {
                return;
            }
            node.GetLocation(
                out int? line,
                out int? column,
                out int? endLine,
                out int? endColumn);
            if (line is null
                || column is null
                || endLine is null
                || endColumn is null) {
                return;
            }

            int[] lineStarts = GetLineStarts(source);
            if (line < 1
                || endLine < 1
                || line > lineStarts.Length
                || endLine > lineStarts.Length) {
                return;
            }

            int startOffset = OffsetOf(
                source,
                lineStarts,
                line.Value,
                column.Value);
            int endOffset = OffsetOf(
                source,
                lineStarts,
                endLine.Value,
                endColumn.Value);

            if (node is Call or Subscript or Attribute) {
                string nodeText = source.Substring(
                    startOffset,
                    Math.Max(0, endOffset - startOffset));
                if (nodeText.Contains(')')) {
                    int cursor = startOffset - 1;
                    int earliestParen = -1;
                    while (cursor >= 0
                        && (source[cursor] is '(' or ')'
                            || char.IsWhiteSpace(source[cursor]))) {
                        if (source[cursor] == '(') {
                            earliestParen = cursor;
                        }
                        cursor--;
                    }
                    if (earliestParen >= 0) {
                        GetLineColumn(
                            lineStarts,
                            earliestParen,
                            out int newLine,
                            out int newColumn);
                        node.lineno = newLine;
                        node.col_offset = newColumn;
                        node.__dict__["lineno"] = newLine;
                        node.__dict__["col_offset"] = newColumn;
                        startOffset = earliestParen;
                    }
                }
            }

            if (node is BinOp or BoolOp or ImportFrom) {
                bool wrappedInParentheses = false;
                int prefix = startOffset - 1;
                while (prefix >= 0
                    && char.IsWhiteSpace(source[prefix])) {
                    prefix--;
                }
                if (prefix >= 0 && source[prefix] == '(') {
                    wrappedInParentheses = true;
                }
                int cursor = endOffset;
                while (cursor < source.Length
                    && char.IsWhiteSpace(source[cursor])) {
                    cursor++;
                }
                int closeCount = 0;
                int closeCursor = cursor;
                while (closeCursor < source.Length
                    && source[closeCursor] is ')' or ']') {
                    closeCount++;
                    closeCursor++;
                }
                if (!wrappedInParentheses
                    && closeCount == 1
                    || node is BoolOp
                        && closeCount > 0
                    || node is ImportFrom
                        && closeCount > 0) {
                        cursor++;
                        GetLineColumn(
                            lineStarts,
                            cursor,
                            out int newEndLine,
                            out int newEndColumn);
                        node.end_lineno = newEndLine;
                        node.end_col_offset = newEndColumn;
                        node.__dict__["end_lineno"] = newEndLine;
                        node.__dict__["end_col_offset"] = newEndColumn;
                        endOffset = cursor;
                }
            }

            if (node is Subscript) {
                int cursor = endOffset;
                while (cursor < source.Length
                    && char.IsWhiteSpace(source[cursor])) {
                    cursor++;
                }
                if (cursor < source.Length
                    && source[cursor] == ']') {
                    cursor++;
                    GetLineColumn(
                        lineStarts,
                        cursor,
                        out int newEndLine,
                        out int newEndColumn);
                    node.end_lineno = newEndLine;
                    node.end_col_offset = newEndColumn;
                    node.__dict__["end_lineno"] = newEndLine;
                    node.__dict__["end_col_offset"] = newEndColumn;
                    endOffset = cursor;
                }
            }

            if (node is Tuple tuple
                && tuple.elts.Count != 0) {
                if (startOffset < source.Length
                    && source[startOffset] == '(') {
                    int depth = 0;
                    for (int i = startOffset; i < source.Length; i++) {
                        if (source[i] == '(') {
                            depth++;
                        } else if (source[i] == ')') {
                            depth--;
                            if (depth == 0) {
                                GetLineColumn(
                                    lineStarts,
                                    i + 1,
                                    out int newEndLine,
                                    out int newEndColumn);
                                node.end_lineno = newEndLine;
                                node.end_col_offset = newEndColumn;
                                node.__dict__["end_lineno"] =
                                    newEndLine;
                                node.__dict__["end_col_offset"] =
                                    newEndColumn;
                                break;
                            }
                        }
                    }
                } else {
                    if (tuple.elts[tuple.elts.Count - 1] is AST lastElement) {
                        lastElement.GetLocation(
                            out _,
                            out _,
                            out int? lastLine,
                            out int? lastColumn);
                        if (lastLine is not null
                            && lastColumn is not null) {
                            int cursor = OffsetOf(
                                source,
                                lineStarts,
                                lastLine.Value,
                                lastColumn.Value);
                            int elementEnd = cursor;
                            while (cursor < source.Length
                                && source[cursor] is ' ' or '\t') {
                                cursor++;
                            }
                            if (cursor < source.Length
                                && source[cursor] == ',') {
                                cursor++;
                            } else {
                                cursor = elementEnd;
                            }
                        GetLineColumn(
                            lineStarts,
                            cursor,
                            out int newEndLine,
                            out int newEndColumn);
                        node.end_lineno = newEndLine;
                        node.end_col_offset = newEndColumn;
                        node.__dict__["end_lineno"] = newEndLine;
                        node.__dict__["end_col_offset"] = newEndColumn;
                        }
                    }
                }
            }
        }

        private static int[] GetLineStarts(string source) {
            var starts = new List<int> { 0 };
            for (int i = 0; i < source.Length; i++) {
                if (source[i] == '\r') {
                    if (i + 1 < source.Length
                        && source[i + 1] == '\n') {
                        i++;
                    }
                    starts.Add(i + 1);
                } else if (source[i] == '\n') {
                    starts.Add(i + 1);
                }
            }
            return starts.ToArray();
        }

        private static int OffsetOf(
            string source,
            int[] lineStarts,
            int line,
            int column) =>
            Math.Min(
                source.Length,
                lineStarts[line - 1] + Math.Max(0, column));

        private static void GetLineColumn(
            int[] lineStarts,
            int offset,
            out int line,
            out int column) {
            int index = Array.BinarySearch(lineStarts, offset);
            if (index < 0) {
                index = ~index - 1;
            }
            line = index + 1;
            column = offset - lineStarts[index];
        }

        private static void SetNodeEnd(
            AST node,
            int length,
            bool setAbsolute) {
            node.GetLocation(
                out _,
                out int? column,
                out _,
                out _);
            if (column is null) {
                return;
            }
            int endColumn = setAbsolute
                ? length
                : column.Value + length;
            node.end_col_offset = endColumn;
            node.__dict__["end_col_offset"] = endColumn;
        }

        private static void IncludeSpan(
            object value,
            ref int? endLine,
            ref int? endColumn) {
            if (value is AST child) {
                child.GetLocation(
                    out _,
                    out _,
                    out int? line,
                    out int? column);
                if (line is not null && column is not null) {
                    IncludeSpan(
                        line.Value,
                        column.Value,
                        ref endLine,
                        ref endColumn);
                }
            } else if (value is IEnumerable values
                && value is not string
                && value is not byte[]) {
                foreach (object item in values) {
                    IncludeSpan(item, ref endLine, ref endColumn);
                }
            }
        }

        private static void IncludeSpan(
            int line,
            int column,
            ref int? endLine,
            ref int? endColumn) {
            if (endLine is null
                || line > endLine
                || line == endLine && column > endColumn) {
                endLine = line;
                endColumn = column;
            }
        }

        private static object ConvertPublicAstValue(object value) {
            if (value is PublicAstNode node) {
                return ConvertPublicAst(node);
            }
            if (value is string or byte[]
                || value is not IEnumerable values) {
                return value;
            }

            var result = new PythonList();
            foreach (object item in values) {
                result.AddNoLock(ConvertPublicAstValue(item));
            }
            return result;
        }

        private static mod ConvertToAST(PythonAst pythonAst, string kind) {
            ContractUtils.RequiresNotNull(pythonAst, nameof(pythonAst));
            ContractUtils.RequiresNotNull(kind, nameof(kind));
            return ConvertToAST((SuiteStatement)pythonAst.Body, kind);
        }

        private static mod ConvertToAST(SuiteStatement suite, string kind) {
            ContractUtils.RequiresNotNull(suite, nameof(suite));
            ContractUtils.RequiresNotNull(kind, nameof(kind));
            switch (kind) {
                case "exec":
                    return new Module(suite);
                case "eval":
                    return new Expression(suite);
                case "single":
                    return new Interactive(suite);
                default:
                    throw new ArgumentException("kind must be 'exec' or 'eval' or 'single'");
            }
        }

        [PythonType]
        public class AST : IPythonObject, IDynamicMetaObjectProvider {
            private sealed class DeletedFieldValue : IDeletedValueMarker {
            }

            private static readonly object DeletedField =
                new DeletedFieldValue();

            internal static readonly HashSet<string> SequenceFieldNames =
                new HashSet<string>(StringComparer.Ordinal) {
                    "args", "argtypes", "bases", "body", "cases", "comparators",
                    "decorator_list", "defaults", "dims", "elts", "finalbody",
                    "generators", "handlers", "ifs", "items", "keys", "keywords",
                    "kw_defaults", "kwd_attrs", "kwd_patterns", "kwonlyargs",
                    "names", "ops", "orelse", "patterns", "posonlyargs", "targets",
                    "type_ignores", "type_params", "values"
                };

            private static readonly Dictionary<string, HashSet<string>> OptionalFieldNames =
                new(StringComparer.Ordinal) {
                    ["FunctionType"] = ["returns"],
                    ["FunctionDef"] = ["returns", "type_comment"],
                    ["AsyncFunctionDef"] = ["returns", "type_comment"],
                    ["Return"] = ["value"],
                    ["Assign"] = ["type_comment"],
                    ["AnnAssign"] = ["value"],
                    ["For"] = ["type_comment"],
                    ["AsyncFor"] = ["type_comment"],
                    ["With"] = ["type_comment"],
                    ["AsyncWith"] = ["type_comment"],
                    ["Raise"] = ["exc", "cause"],
                    ["Assert"] = ["msg"],
                    ["Import"] = ["is_lazy"],
                    ["ImportFrom"] = ["module", "level", "is_lazy"],
                    ["DictComp"] = ["value"],
                    ["Yield"] = ["value"],
                    ["FormattedValue"] = ["format_spec"],
                    ["Interpolation"] = ["format_spec"],
                    ["Constant"] = ["kind"],
                    ["Slice"] = ["lower", "upper", "step"],
                    ["ExceptHandler"] = ["type", "name"],
                    ["arguments"] = ["vararg", "kwarg"],
                    ["arg"] = ["annotation", "type_comment"],
                    ["keyword"] = ["arg"],
                    ["alias"] = ["asname"],
                    ["withitem"] = ["optional_vars"],
                    ["match_case"] = ["guard"],
                    ["MatchMapping"] = ["rest"],
                    ["MatchStar"] = ["name"],
                    ["MatchAs"] = ["pattern", "name"],
                    ["TypeVar"] = ["bound", "default_value"],
                    ["ParamSpec"] = ["default_value"],
                    ["TypeVarTuple"] = ["default_value"],
                };

            protected int? _lineno; // both lineno and col_offset are expected to be int, in cpython anything is accepted
            protected int? _col_offset;
            protected int? _end_lineno;
            protected int? _end_col_offset;
            private PythonDictionary _customAttributes;
            private PythonType _pythonType;
            private object[] _slots;
            internal bool _isParsedAst;

            public PythonTuple _fields { get; protected set; } = PythonTuple.EMPTY;

            public PythonTuple _attributes { get; protected set; } = PythonTuple.EMPTY;

            public AST __replace__(
                CodeContext context,
                [ParamDictionary] IDictionary<object, object> changes) {
                var values = new Dictionary<object, object>();
                PythonTuple fields = GetSchemaTuple(
                    context,
                    "_fields");
                PythonTuple attributes = GetSchemaTuple(
                    context,
                    "_attributes");
                foreach (object field in fields) {
                    if (field is string name
                        && TryGetFieldValue(name, out object value)) {
                        values[name] = value;
                    }
                }
                foreach (object attribute in attributes) {
                    if (attribute is string name
                        && (TryGetFieldValue(name, out object value)
                            || PythonOps.TryGetBoundAttr(
                                context,
                                this,
                                name,
                                out value))) {
                        values[name] = value;
                    }
                }
                foreach (KeyValuePair<object, object> change in changes) {
                    if (change.Key is not string name) {
                        PythonOps.Warn(
                            context,
                            PythonExceptions.DeprecationWarning,
                            "got an unexpected keyword argument {0}",
                            PythonOps.Repr(context, change.Key));
                        throw PythonOps.TypeError(
                            "got an unexpected keyword argument {0}",
                            PythonOps.Repr(context, change.Key));
                    }
                    if (!fields.Contains(name)
                        && !attributes.Contains(name)) {
                        throw PythonOps.TypeError(
                            "{0}.__init__ got an unexpected keyword argument '{1}'",
                            GetDisplayTypeName(context),
                            name);
                    }
                    values[name] = change.Value;
                }

                foreach (object field in fields) {
                    if (field is not string name
                        || values.ContainsKey(name)
                        || AST.SequenceFieldNames.Contains(name)
                        || IsOptionalField(name)) {
                        continue;
                    }
                    throw PythonOps.TypeError(
                        "{0}.__init__ missing 1 required positional argument: '{1}'",
                        GetDisplayTypeName(context),
                        name);
                }
                var result = (AST)DynamicHelpers.GetPythonType(this)
                    .CreateInstance(context);
                foreach (KeyValuePair<object, object> value in values) {
                    result.SetFieldValue(
                        context,
                        (string)value.Key,
                        value.Value);
                }
                return result;
            }

            private PythonTuple GetSchemaTuple(
                CodeContext context,
                string name) {
                PythonType type = DynamicHelpers.GetPythonType(this);
                if (!type.IsSystemType
                    && PythonOps.TryGetBoundAttr(
                        context,
                        type,
                        name,
                        out object value)
                    && value is IEnumerable values) {
                    return PythonTuple.MakeTuple(
                        values.Cast<object>().ToArray());
                }
                return name == "_fields"
                    ? _fields
                    : _attributes;
            }

            public string __repr__(CodeContext context) {
                var result = new StringBuilder();
                AppendRepr(context, result, 3);
                return result.ToString();
            }

            private void AppendRepr(
                CodeContext context,
                StringBuilder result,
                int depth) {
                string typeName = DynamicHelpers.GetPythonType(this).Name;
                if (depth == 0) {
                    result.Append(typeName);
                    result.Append("(...)");
                    return;
                }

                result.Append(typeName);
                result.Append('(');
                bool first = true;
                foreach (object field in _fields) {
                    if (field is not string name
                        || !TryGetFieldValue(name, out object value)) {
                        continue;
                    }
                    if (!first) {
                        result.Append(", ");
                    }
                    first = false;
                    result.Append(name);
                    result.Append('=');
                    AppendValueRepr(
                        context,
                        result,
                        value,
                        depth - 1);
                }
                result.Append(')');
            }

            private static void AppendValueRepr(
                CodeContext context,
                StringBuilder result,
                object value,
                int depth) {
                if (value is AST node) {
                    node.AppendRepr(context, result, depth);
                    return;
                }
                if (value is PythonList list) {
                    result.Append('[');
                    if (list.Count > 2) {
                        AppendValueRepr(
                            context,
                            result,
                            list[0],
                            depth);
                        result.Append(", ..., ");
                        AppendValueRepr(
                            context,
                            result,
                            list[list.Count - 1],
                            depth);
                    } else {
                        for (int i = 0; i < list.Count; i++) {
                            if (i != 0) {
                                result.Append(", ");
                            }
                            AppendValueRepr(
                                context,
                                result,
                                list[i],
                                depth);
                        }
                    }
                    result.Append(']');
                    return;
                }
                result.Append(PythonOps.Repr(context, value));
            }

            public PythonDictionary _field_types {
                get {
                    var result = new PythonDictionary();
                    foreach (object fieldValue in _fields) {
                        if (fieldValue is not string name) continue;
                        object fieldType = string.Equals(
                            name,
                            "ctx",
                            StringComparison.Ordinal)
                            ? DynamicHelpers.GetPythonTypeFromType(typeof(expr_context))
                            : SequenceFieldNames.Contains(name)
                                ? new IronPython.Runtime.Types.GenericAlias(
                                    TypeCache.PythonList,
                                    TypeCache.Object)
                                : TypeCache.Object;
                        if (IsOptionalField(name)) {
                            fieldType = UnionType.MakeUnion(fieldType, null);
                        }
                        result[name] = fieldType;
                    }
                    return result;
                }
            }

            public static object __new__(
                CodeContext context,
                [NotNone] PythonType pythonType,
                [ParamDictionary] IDictionary<object, object> kwargs,
                [NotNone] params object[] args) {
                object result = pythonType.CreateInstance(context);
                if (result is AST ast) {
                    ast._pythonType = pythonType;
                }
                return result;
            }

            public void __init__(
                CodeContext context,
                [ParamDictionary] IDictionary<object, object> kwargs,
                [NotNone] params object[] args) {
                if (GetType() == typeof(AST)) {
                    PythonOps.Warn(
                        context,
                        PythonExceptions.DeprecationWarning,
                        "AST constructor takes 0 positional arguments");
                    if (args.Length > 0) {
                        throw PythonOps.TypeError(
                            "AST constructor takes 0 positional arguments");
                    }
                }

                if (!PythonOps.TryGetBoundAttr(
                    context,
                    this,
                    "_fields",
                    out object fieldsValue)) {
                    throw PythonOps.AttributeError(
                        "type object '{0}' has no attribute '_fields'",
                        PythonOps.GetPythonTypeName(this));
                }
                PythonOps.TryGetBoundAttr(
                    context,
                    this,
                    "_attributes",
                    out object attributesValue);

                List<string> fields = ToFieldNames(
                    fieldsValue,
                    out object malformedField);
                if (malformedField is not null) {
                    throw PythonOps.TypeError(
                        "Field {0} must be a string",
                        PythonOps.Repr(context, malformedField));
                }
                List<string> attributes = ToFieldNames(
                    attributesValue,
                    out _);

                TryGetFieldTypes(
                    context,
                    out PythonDictionary fieldTypes);
                var values = new Dictionary<string, object>();
                for (int i = 0; i < args.Length; i++) {
                    if (i >= fields.Count) {
                        throw PythonOps.TypeError(
                            "{0}.__init__ got too many positional arguments",
                            GetDisplayTypeName(context));
                    }
                    values[fields[i]] = args[i];
                }
                foreach (KeyValuePair<object, object> item in kwargs) {
                    if (!TryGetKeywordName(
                        context,
                        item.Key,
                        fields,
                        attributes,
                        out string name)) {
                        PythonOps.Warn(
                            context,
                            PythonExceptions.DeprecationWarning,
                            "got an unexpected keyword argument {0}",
                            PythonOps.Repr(context, item.Key));
                        throw PythonOps.TypeError(
                            "got an unexpected keyword argument {0}",
                            PythonOps.Repr(context, item.Key));
                    }
                    if (!fields.Contains(name)
                        && !attributes.Contains(name)
                        && GetType().GetProperty(name) is null) {
                        throw PythonOps.TypeError(
                            "{0}.__init__ got an unexpected keyword argument '{1}'",
                            GetDisplayTypeName(context),
                            name);
                    }
                    if (values.ContainsKey(name)) {
                        throw PythonOps.TypeError(
                            "{0} got multiple values for argument '{1}'",
                            GetDisplayTypeName(context),
                            name);
                    }
                    values[name] = item.Value;
                }

                var missing = new List<string>();
                foreach (string name in fields) {
                    if (values.ContainsKey(name)) {
                        SetFieldValue(context, name, values[name]);
                        continue;
                    }

                    object fieldType = null;
                    bool hasFieldType = fieldTypes is not null
                        && fieldTypes.TryGetValue(name, out fieldType);
                    if (fieldTypes is not null && !hasFieldType) {
                        throw PythonOps.TypeError(
                            "Field '{0}' is missing from {1}._field_types",
                            name,
                            GetQualifiedTypeName(context));
                    }

                    if (IsSequenceField(name, fieldType)) {
                        SetFieldValue(context, name, new PythonList());
                    } else if (name == "ctx"
                        && fieldType is PythonType exprContextType
                        && exprContextType.IsSubclassOf(
                            DynamicHelpers.GetPythonTypeFromType(
                                typeof(expr_context)))) {
                        SetFieldValue(context, name, Load.Instance);
                    } else if (!IsOptionalField(name)
                        && fieldTypes is not null
                        && !IsOptionalType(fieldType)) {
                        missing.Add(name);
                    }
                }

                foreach (KeyValuePair<string, object> item in values) {
                    if (!fields.Contains(item.Key)) {
                        SetFieldValue(context, item.Key, item.Value);
                    }
                }

                if (missing.Count > 0) {
                    string argumentWord =
                        missing.Count == 1 ? "argument" : "arguments";
                    string names = missing.Count == 1
                        ? $"'{missing[0]}'"
                        : string.Join(
                            ", ",
                            missing.Take(missing.Count - 1)
                                .Select(name => $"'{name}'"))
                            + $", and '{missing[^1]}'";
                    throw PythonOps.TypeError(
                        "{0}.__init__ missing {1} required positional {2}: {3}",
                        GetDisplayTypeName(context),
                        missing.Count,
                        argumentWord,
                        names);
                }
            }

            private bool TryGetFieldTypes(
                CodeContext context,
                out PythonDictionary fieldTypes) {
                PythonType type = DynamicHelpers.GetPythonType(this);
                if (!type.IsSystemType) {
                    if (PythonOps.TryGetBoundAttr(
                        context,
                        type,
                        "_field_types",
                        out object fieldTypesValue)) {
                        fieldTypes = fieldTypesValue as PythonDictionary;
                        return fieldTypes is not null;
                    }

                    fieldTypes = null;
                    return false;
                }

                fieldTypes = _field_types;
                return fieldTypes is not null;
            }

            private static bool TryGetKeywordName(
                CodeContext context,
                object key,
                List<string> fields,
                List<string> attributes,
                out string name) {
                if (key is string stringKey) {
                    name = stringKey;
                    return true;
                }

                foreach (string field in fields) {
                    if (PythonOps.EqualRetBool(context, key, field)) {
                        name = field;
                        return true;
                    }
                }
                foreach (string attribute in attributes) {
                    if (PythonOps.EqualRetBool(context, key, attribute)) {
                        name = attribute;
                        return true;
                    }
                }

                name = null;
                return false;
            }

            private string GetDisplayTypeName(CodeContext context) {
                PythonType type = DynamicHelpers.GetPythonType(this);
                object module = PythonType.Get__module__(context, type);
                return Equals(module, "_ast")
                    ? "ast." + type.Name
                    : type.Name;
            }

            private string GetQualifiedTypeName(CodeContext context) {
                PythonType type = DynamicHelpers.GetPythonType(this);
                object module = PythonType.Get__module__(context, type);
                string displayModule = Equals(module, "_ast")
                    ? "ast"
                    : module as string ?? "unknown";
                return displayModule + "." + type.QualName;
            }

            private static List<string> ToFieldNames(
                object value,
                out object malformedField) {
                malformedField = null;
                var result = new List<string>();
                if (value is not IEnumerable enumerable) {
                    return result;
                }
                foreach (object field in enumerable) {
                    if (field is string name) {
                        result.Add(name);
                    } else {
                        malformedField ??= field ?? "None";
                    }
                }
                return result;
            }

            private static bool IsSequenceField(
                string name,
                object fieldType) {
                if (SequenceFieldNames.Contains(name)) {
                    return true;
                }
                if (fieldType is GenericAlias alias
                    && alias.__origin__ is PythonType origin
                    && origin.Name is "list" or "tuple" or "set"
                    or "frozenset") {
                    return true;
                }
                string typeName = fieldType?.ToString() ?? string.Empty;
                return typeName.StartsWith("list[", StringComparison.Ordinal)
                    || typeName.StartsWith(
                        "System.Collections.Generic.List",
                        StringComparison.Ordinal);
            }

            private static bool IsOptionalType(object fieldType) {
                if (fieldType is null) return false;
                if (fieldType is UnionType union) {
                    return union.__args__.Any(member => member is null);
                }
                string typeName = fieldType.ToString() ?? string.Empty;
                return typeName.Contains("None", StringComparison.Ordinal);
            }

            internal bool IsOptionalField(string name) =>
                OptionalFieldNames.TryGetValue(
                    this is ArgType
                        ? "arg"
                        : GetType().Name,
                    out HashSet<string> fields)
                && fields.Contains(name);

            internal void SetFieldValue(
                CodeContext context,
                string name,
                object value) {
                System.Reflection.PropertyInfo property =
                    GetType().GetProperty(name);
                if (property is not null) {
                    try {
                        property.SetValue(this, value, null);
                        _customAttributes?.Remove(name);
                    } catch (ArgumentException) {
                        EnsureCustomAttributes()[name] = value;
                    } catch (System.Reflection.TargetInvocationException e)
                        when (e.InnerException is ArgumentException
                            or InvalidCastException) {
                        EnsureCustomAttributes()[name] = value;
                    }
                } else {
                    PythonOps.SetAttr(context, this, name, value);
                }
            }

            internal bool TryGetFieldValue(
                string name,
                out object value) {
                if (_customAttributes is not null
                    && _customAttributes.TryGetValueNoMissing(
                        name,
                        out value)) {
                    if (IsDeletedField(value)) {
                        value = null;
                        return false;
                    }
                    return true;
                }

                System.Reflection.MemberInfo member =
                    (System.Reflection.MemberInfo)GetType().GetProperty(name)
                    ?? GetType().GetField(name);
                if (member is System.Reflection.PropertyInfo property) {
                    try {
                        value = property.GetValue(this, null);
                        return true;
                    } catch (System.Reflection.TargetInvocationException) {
                        value = null;
                        return false;
                    }
                }
                if (member is System.Reflection.FieldInfo field) {
                    value = field.GetValue(this);
                    return true;
                }

                value = null;
                return false;
            }

            internal void GetLocation(
                out int? line,
                out int? column,
                out int? endLine,
                out int? endColumn) {
                line = _lineno;
                column = _col_offset;
                endLine = _end_lineno;
                endColumn = _end_col_offset;
            }

            private PythonDictionary EnsureCustomAttributes() {
                if (_customAttributes is null) {
                    _customAttributes = new PythonDictionary();
                }
                return _customAttributes;
            }

            public PythonDictionary __dict__ => EnsureCustomAttributes();

            PythonDictionary IPythonObject.Dict => _customAttributes;

            PythonDictionary IPythonObject.SetDict(PythonDictionary dict) {
                System.Threading.Interlocked.CompareExchange(
                    ref _customAttributes,
                    dict,
                    null);
                return _customAttributes;
            }

            bool IPythonObject.ReplaceDict(PythonDictionary dict) {
                return System.Threading.Interlocked.CompareExchange(
                    ref _customAttributes,
                    dict,
                    null) is null;
            }

            PythonType IPythonObject.PythonType
                => _pythonType ?? DynamicHelpers.GetPythonTypeFromType(GetType());

            void IPythonObject.SetPythonType(PythonType newType) {
                if (newType.IsSystemType) {
                    throw PythonOps.TypeError(
                        "__class__ assignment can only be performed on user defined types");
                }
                _pythonType = newType;
            }

            object[] IPythonObject.GetSlots() => _slots;

            object[] IPythonObject.GetSlotsCreate() {
                if (_slots is null) {
                    System.Threading.Interlocked.CompareExchange(
                        ref _slots,
                        new object[1],
                        null);
                }
                return _slots;
            }

            DynamicMetaObject IDynamicMetaObjectProvider.GetMetaObject(
                LinqExpression parameter) {
                return new MetaUserObject(
                    parameter,
                    BindingRestrictions.Empty,
                    null,
                    this);
            }

            public object __getattribute__(
                CodeContext context,
                string name) {
                if (_customAttributes is not null
                    && _customAttributes.TryGetValue(name, out object value)) {
                    if (IsDeletedField(value)) {
                        throw PythonOps.AttributeError(
                            "'{0}' object has no attribute '{1}'",
                            PythonOps.GetPythonTypeName(this),
                            name);
                    }
                    return value;
                }
                if (name == "__dict__") {
                    return this.__dict__;
                }
                if (name == "__match_args__") {
                    return _fields;
                }
                if (name == "__class__") {
                    return DynamicHelpers.GetPythonType(this);
                }
                return ObjectOps.__getattribute__(context, this, name);
            }

            public object __getattr__(CodeContext context, string name) {
                if (_customAttributes is not null
                    && _customAttributes.TryGetValue(name, out object value)) {
                    return value;
                }
                throw PythonOps.AttributeError(
                    "'{0}' object has no attribute '{1}'",
                    PythonOps.GetPythonTypeName(this),
                    name);
            }

            public void __setattr__(
                CodeContext context,
                string name,
                object value) {
                if (name == "_fields") {
                    EnsureCustomAttributes()[name] = value;
                    return;
                }
                System.Reflection.PropertyInfo property =
                    GetType().GetProperty(name);
                if (property is not null && property.CanWrite) {
                    property.SetValue(this, value, null);
                    _customAttributes?.Remove(name);
                } else {
                    EnsureCustomAttributes()[name] = value;
                }
            }

            public void __delattr__(
                CodeContext context,
                string name) {
                if (_customAttributes is not null
                        && _customAttributes.ContainsKey(name)
                    || GetType().GetProperty(name) is not null) {
                    EnsureCustomAttributes()[name] = DeletedField;
                    return;
                }
                throw PythonOps.AttributeError(
                    "'{0}' object has no attribute '{1}'",
                    PythonOps.GetPythonTypeName(this),
                    name);
            }

            public int lineno {
                get {
                    if (_lineno != null) return (int)_lineno;
                    throw PythonOps.AttributeErrorForMissingAttribute(PythonOps.GetPythonTypeName(this), "lineno");
                }
                set { _lineno = value; }
            }

            public int col_offset {
                get {
                    if (_col_offset != null) return (int)_col_offset;
                    throw PythonOps.AttributeErrorForMissingAttribute(PythonOps.GetPythonTypeName(this), "col_offset");
                }
                set { _col_offset = value; }
            }

            public int end_lineno {
                get {
                    if (_end_lineno != null) return (int)_end_lineno;
                    throw PythonOps.AttributeErrorForMissingAttribute(
                        PythonOps.GetPythonTypeName(this),
                        "end_lineno");
                }
                set { _end_lineno = value; }
            }

            public int end_col_offset {
                get {
                    if (_end_col_offset != null) return (int)_end_col_offset;
                    throw PythonOps.AttributeErrorForMissingAttribute(
                        PythonOps.GetPythonTypeName(this),
                        "end_col_offset");
                }
                set { _end_col_offset = value; }
            }

            internal static bool IsDeletedField(object value) =>
                ReferenceEquals(value, DeletedField)
                || ReferenceEquals(value, Uninitialized.Instance);

            public void __setstate__(PythonDictionary state) {
                foreach (KeyValuePair<object, object> item in state) {
                    if (item.Key is string name) {
                        SetFieldValue(DefaultContext.Default, name, item.Value);
                    }
                }
                restoreProperties(_attributes, state);
                restoreProperties(_fields, state);
            }

            internal void restoreProperties(IEnumerable<object> names, IDictionary source) {
                foreach (object name in names) {
                    if (name is string) {
                        try {
                            string key = (string)name;
                            this.GetType().GetProperty(key).SetValue(this, source[key], null);
                        } catch (Generic.KeyNotFoundException) {
                            // ignore missing
                        }
                    }
                }
            }

            internal void storeProperties(IEnumerable<object> names, IDictionary target) {
                foreach (object name in names) {
                    if (name is string) {
                        string key = (string)name;
                        object val;
                        try {
                            val = this.GetType().GetProperty(key).GetValue(this, null);
                            target.Add(key, val);
                        } catch (System.Reflection.TargetInvocationException) {
                            // field not set
                        }
                    }
                }
            }

            internal PythonDictionary getstate() {
                PythonDictionary d = new PythonDictionary(10);
                storeProperties(_fields, d);
                storeProperties(_attributes, d);
                return d;
            }

            public virtual object/*!*/ __reduce__() {
                CodeContext context = DefaultContext.Default;
                object copyreg = context.LanguageContext
                    .GetCopyRegModule();
                object newObject = PythonOps.GetBoundAttr(
                    context,
                    copyreg,
                    "__newobj__");
                return PythonTuple.MakeTuple(
                    newObject,
                    PythonTuple.MakeTuple(DynamicHelpers.GetPythonType(this)),
                    getstate());
            }

            public virtual object/*!*/ __reduce_ex__(int protocol) {
                return __reduce__();
            }

            [PythonHidden]
            protected void GetSourceLocation(Node node) {
                if (!node.IsParentDefined) {
                    _lineno = 1;
                    _col_offset = 0;
                    return;
                }
                _lineno = node.Start.Line;

                // IronPython counts from 1; CPython counts from 0
                _col_offset = node.Start.Column - 1;
                SourceLocation end = node.End;
                if (end.IsValid) {
                    _end_lineno = end.Line;
                    _end_col_offset = end.Column - 1;
                }
            }

            internal static PythonList ConvertStatements(Statement stmt) {
                return ConvertStatements(stmt, false);
            }

            internal static PythonList ConvertStatements(Statement stmt, bool allowNull) {
                if (stmt == null)
                    if (allowNull)
                        return new PythonList(0);
                    else
                        throw new ArgumentNullException(nameof(stmt));

                if (stmt is SuiteStatement) {
                    SuiteStatement suite = (SuiteStatement)stmt;
                    PythonList list = new PythonList(suite.Statements.Count);
                    foreach (Statement s in suite.Statements)
                        if (s is SuiteStatement)  // multiple stmt in a line
                            foreach (Statement s2 in ((SuiteStatement)s).Statements)
                                list.Add(Convert(s2));
                        else
                            list.Add(Convert(s));

                    return list;
                }

                return PythonList.FromArrayNoCopy(Convert(stmt));
            }

            internal static stmt Convert(Statement stmt) {
                stmt ast = stmt switch {
                    FunctionDefinition s => s.IsAsync
                        ? new AsyncFunctionDef(s)
                        : new FunctionDef(s),
                    ReturnStatement s => new Return(s),
                    AnnotatedAssignStatement s => new AnnAssign(s),
                    AssignmentStatement s => new Assign(s),
                    AugmentedAssignStatement s => new AugAssign(s),
                    DelStatement s => new Delete(s),
                    ExpressionStatement s => new Expr(s),
                    ForStatement s => new For(s),
                    WhileStatement s => new While(s),
                    IfStatement s => new If(s),
                    WithStatement s => new With(s),
                    RaiseStatement s => new Raise(s),
                    TryStatement s => new Try(s),
                    AssertStatement s => new Assert(s),
                    ImportStatement s => new Import(s),
                    FromImportStatement s => new ImportFrom(s),
                    GlobalStatement s => new Global(s),
                    NonlocalStatement s => new Nonlocal(s),
                    ClassDefinition s => new ClassDef(s),
                    AsyncForStatement s => new AsyncFor(s),
                    AsyncWithStatement s => new AsyncWith(s),
                    BreakStatement _ => new Break(),
                    ContinueStatement _ => new Continue(),
                    EmptyStatement _ => new Pass(),
                    _ => throw new ArgumentTypeException("Unexpected statement type: " + stmt.GetType()),
                };
                ast.GetSourceLocation(stmt);
                return ast;
            }

            internal static PythonList ConvertAliases(IList<DottedName> names, IList<string> asnames) {
                PythonList list = new PythonList(names.Count);

                if (names == FromImportStatement.Star) // does it ever happen?
                    list.Add(new alias("*", null));
                else
                    for (int i = 0; i < names.Count; i++)
                        list.Add(new alias(names[i].MakeString(), asnames[i]));

                return list;
            }

            internal static PythonList ConvertAliases(IList<string> names, IList<string> asnames) {
                PythonList list = new PythonList(names.Count);

                if (names == FromImportStatement.Star)
                    list.Add(new alias("*", null));
                else
                    for (int i = 0; i < names.Count; i++)
                        list.Add(new alias(names[i], asnames[i]));

                return list;
            }

            internal static slice TrySliceConvert(AstExpression expr) {
                if (expr is SliceExpression)
                    return new Slice((SliceExpression)expr);
                if (expr is TupleExpression tuple
                    && (tuple.IsExpandable || tuple.Items.Any(item => item is SliceExpression))) {
                    var dimensions = new PythonList(tuple.Items.Count);
                    foreach (AstExpression item in tuple.Items) {
                        dimensions.Add(
                            (object)TrySliceConvert(item)
                            ?? new Index(Convert(item))!);
                    }
                    return new ExtSlice(dimensions);
                }
                return null;
            }

            internal static expr Convert(AstExpression expr) {
                return Convert(expr, Load.Instance);
            }

            internal static expr Convert(AstExpression expr, expr_context ctx) {
                var ast = expr switch {
                    ConstantExpression x => Convert(x),
                    NameExpression x => new Name(x, ctx),
                    UnaryExpression x => new UnaryOp(x).TryTrimTrivialUnaryOp(),
                    BinaryExpression x => Convert(x),
                    AndExpression x => new BoolOp(x),
                    OrExpression x => new BoolOp(x),
                    CallExpression x => new Call(x),
                    ParenthesisExpression x => Convert(x.Expression),
                    LambdaExpression x => new Lambda(x),
                    ListExpression x => new List(x, ctx),
                    TupleExpression x => new Tuple(x, ctx),
                    DictionaryExpression x => new Dict(x),
                    ListComprehension x => new ListComp(x),
                    GeneratorExpression x => new GeneratorExp(x),
                    MemberExpression x => new Attribute(x, ctx),
                    YieldExpression x => new Yield(x),
                    YieldFromExpression x => new YieldFrom(x),
                    AwaitExpression x => new Await(x),
                    ConditionalExpression x => new IfExp(x),
                    IndexExpression x => new Subscript(x, ctx),
                    SetExpression x => new Set(x),
                    DictionaryComprehension x => new DictComp(x),
                    SetComprehension x => new SetComp(x),
                    StarredExpression x => new Starred(x, ctx),
                    JoinedStringExpression x => new JoinedStr(x),
                    FormattedValueExpression x => new FormattedValue(x),
                    TemplateStringExpression x => new TemplateStr(x),
                    NamedExpression x => new NamedExpr(x),
                    _ => throw new ArgumentTypeException("Unexpected expression type: " + expr.GetType()),
                };
                if (expr is ParenthesisExpression) return ast; // don't overwrite the source loc
                ast.GetSourceLocation(expr);
                return ast;
            }

            internal static expr Convert(ConstantExpression expr) {
                expr ast;

                if (expr.Value is PythonTuple tuple) {
                    var items = new PythonList(tuple.Count);
                    foreach (object item in tuple) {
                        items.Add(Convert(new ConstantExpression(item)));
                    }
                    ast = new Tuple(items, Load.Instance);
                } else if (expr.Value == null
                    || expr.Value is bool
                    || expr.Value is int
                    || expr.Value is double
                    || expr.Value is long
                    || expr.Value is BigInteger
                    || expr.Value is Complex
                    || expr.Value is string
                    || expr.Value is IronPython.Runtime.Bytes
                    || expr.Value is IronPython.Runtime.Types.Ellipsis) {
                    ast = new Constant(expr.Value);
                } else {
                    throw new ArgumentTypeException("Unexpected constant type: " + expr.Value.GetType());
                }
                return ast;
            }

            internal static expr Convert(BinaryExpression expr) {
                AST op = Convert(expr.Operator);
                if (BinaryExpression.IsComparison(expr)) {
                    return new Compare(expr);
                }

                if (op is @operator) {
                    return new BinOp(expr, (@operator)op);
                }

                throw new ArgumentTypeException("Unexpected operator type: " + op.GetType());
            }

            internal static AST Convert(Node node) {
                AST ast;

                if (node is TryStatementHandler)
                    ast = new ExceptHandler((TryStatementHandler)node);
                else
                    throw new ArgumentTypeException("Unexpected node type: " + node.GetType());

                ast.GetSourceLocation(node);
                return ast;
            }

            internal static PythonList Convert(IReadOnlyList<ComprehensionIterator> iters) {
                var cfCollector = new List<ComprehensionFor>();
                var cifCollector = new List<List<ComprehensionIf>>();
                List<ComprehensionIf> cif = null;
                for (int i = 0; i < iters.Count; i++) {
                    switch (iters[i]) {
                        case ComprehensionFor cf:
                            cfCollector.Add(cf);
                            cif = new List<ComprehensionIf>();
                            cifCollector.Add(cif);
                            break;
                        case ComprehensionIf ci:
                            cif.Add(ci);
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                }

                PythonList comps = new PythonList();
                for (int i = 0; i < cfCollector.Count; i++)
                    comps.Add(new comprehension(cfCollector[i], cifCollector[i].ToArray()));
                return comps;
            }

            internal static AST Convert(PythonOperator op) {
                // We treat operator classes as singletons here to keep overhead down
                // But we cannot fully make them singletons if we wish to keep compatibility wity CPython
                return op switch {
                    PythonOperator.Add => Add.Instance,
                    PythonOperator.BitwiseAnd => BitAnd.Instance,
                    PythonOperator.BitwiseOr => BitOr.Instance,
                    PythonOperator.TrueDivide => Div.Instance,
                    PythonOperator.Equal => Eq.Instance,
                    PythonOperator.FloorDivide => FloorDiv.Instance,
                    PythonOperator.GreaterThan => Gt.Instance,
                    PythonOperator.GreaterThanOrEqual => GtE.Instance,
                    PythonOperator.In => In.Instance,
                    PythonOperator.Invert => Invert.Instance,
                    PythonOperator.Is => Is.Instance,
                    PythonOperator.IsNot => IsNot.Instance,
                    PythonOperator.LeftShift => LShift.Instance,
                    PythonOperator.LessThan => Lt.Instance,
                    PythonOperator.LessThanOrEqual => LtE.Instance,
                    PythonOperator.MatMult => MatMult.Instance,
                    PythonOperator.Mod => Mod.Instance,
                    PythonOperator.Multiply => Mult.Instance,
                    PythonOperator.Negate => USub.Instance,
                    PythonOperator.Not => Not.Instance,
                    PythonOperator.NotEqual => NotEq.Instance,
                    PythonOperator.NotIn => NotIn.Instance,
                    PythonOperator.Pos => UAdd.Instance,
                    PythonOperator.Power => Pow.Instance,
                    PythonOperator.RightShift => RShift.Instance,
                    PythonOperator.Subtract => Sub.Instance,
                    PythonOperator.Xor => BitXor.Instance,
                    _ => throw new ArgumentException("Unexpected PythonOperator: " + op, nameof(op)),
                };
            }
        }

        [PythonType]
        public class alias : AST {
            private static readonly PythonTuple __attributes =
                PythonTuple.MakeTuple(new[] {
                    nameof(lineno),
                    nameof(col_offset),
                    nameof(end_lineno),
                    nameof(end_col_offset)
                });

            public alias() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(name), nameof(asname) });
                _attributes = __attributes;
            }

            public alias(string name, [Optional] string asname)
                : this() {
                this.name = name;
                this.asname = asname;
            }

            public string name { get; set; }

            public string asname { get; set; }
        }

        public static PythonType arg => DynamicHelpers.GetPythonTypeFromType(typeof(ArgType));

        [PythonType("arg"), PythonHidden]
        public class ArgType : AST {
            public static readonly PythonTuple __match_args__ =
                PythonTuple.MakeTuple(
                    nameof(arg),
                    nameof(annotation),
                    nameof(type_comment));

            private static PythonTuple __attributes = PythonTuple.MakeTuple(new[] {
                nameof(lineno),
                nameof(col_offset),
                nameof(end_lineno),
                nameof(end_col_offset)
            });
            private static PythonTuple __fields = PythonTuple.MakeTuple(new[] {
                nameof(arg),
                nameof(annotation),
                nameof(type_comment)
            });

            public ArgType() {
                _attributes = __attributes;
                _fields = __fields;
            }

            public ArgType(
                string arg,
                object annotation,
                [Optional] string type_comment) : this() {
                this.arg = arg;
                this.annotation = annotation;
                this.type_comment = type_comment;
            }

            internal ArgType(Parameter parameter) : this() {
                arg = parameter.Name;
                annotation = parameter.Annotation == null ? null : Convert(parameter.Annotation);
                GetSourceLocation(parameter);
            }

            public string arg { get; set; }
            public object annotation { get; set; }
            public string type_comment { get; set; }
        }

        [PythonType]
        public class arguments : AST {
            public static readonly PythonTuple __match_args__ =
                PythonTuple.MakeTuple(
                    nameof(posonlyargs),
                    nameof(args),
                    nameof(vararg),
                    nameof(kwonlyargs),
                    nameof(kw_defaults),
                    nameof(kwarg),
                    nameof(defaults));

            public arguments() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(posonlyargs),
                    nameof(args),
                    nameof(vararg),
                    nameof(kwonlyargs),
                    nameof(kw_defaults),
                    nameof(kwarg),
                    nameof(defaults)
                });
                posonlyargs = new PythonList();
            }

            public arguments(
                PythonList posonlyargs,
                PythonList args,
                ArgType vararg,
                PythonList kwonlyargs,
                PythonList kw_defaults,
                ArgType kwarg,
                PythonList defaults)
                : this() {
                this.posonlyargs = posonlyargs;
                this.args = args;
                this.vararg = vararg;
                this.kwonlyargs = kwonlyargs;
                this.kw_defaults = kw_defaults;
                this.kwarg = kwarg;
                this.defaults = defaults;
            }

            internal arguments(IList<Parameter> parameters)
                : this() {
                posonlyargs = new PythonList();
                args = new PythonList(parameters.Count);
                defaults = new PythonList(parameters.Count);
                kwonlyargs = new PythonList();
                kw_defaults = new PythonList();
                foreach (Parameter param in parameters) {
                    switch (param.Kind) {
                        case ParameterKind.PositionalOnly:
                            posonlyargs.Add(new ArgType(param));
                            if (param.DefaultValue != null)
                                defaults.Add(Convert(param.DefaultValue));
                            break;
                        case ParameterKind.List:
                            vararg = new ArgType(param);
                            break;
                        case ParameterKind.Dictionary:
                            kwarg = new ArgType(param);
                            break;
                        case ParameterKind.KeywordOnly:
                            kwonlyargs.Add(new ArgType(param));
                            kw_defaults.Add(param.DefaultValue == null ? null : Convert(param.DefaultValue));
                            break;
                        default:
                            args.Add(new ArgType(param));
                            if (param.DefaultValue != null)
                                defaults.Add(Convert(param.DefaultValue));
                            break;
                    }
                }
            }

            internal Parameter[] Revert() {
                var parameters = new List<Parameter>();
                int positionalCount =
                    posonlyargs.Count + args.Count;
                int firstDefault =
                    positionalCount - defaults.Count;
                for (int i = 0; i < positionalCount; i++) {
                    ArgType arg = i < posonlyargs.Count
                        ? (ArgType)posonlyargs[i]
                        : (ArgType)args[i - posonlyargs.Count];
                    parameters.Add(new Parameter(
                        arg.arg,
                        i < posonlyargs.Count
                            ? ParameterKind.PositionalOnly
                            : ParameterKind.Normal) {
                        Annotation = expr.Revert(arg.annotation),
                        DefaultValue = i >= firstDefault
                            ? expr.Revert(defaults[i - firstDefault])
                            : null
                    });
                }
                for (int i = 0; i < kwonlyargs.Count; i++) {
                    var kwonlyarg = (ArgType)kwonlyargs[i];
                    parameters.Add(new Parameter(
                        kwonlyarg.arg,
                        ParameterKind.KeywordOnly) {
                        Annotation = expr.Revert(kwonlyarg.annotation),
                        DefaultValue = expr.Revert(kw_defaults[i])
                    });
                }
                if (vararg != null)
                    parameters.Add(new Parameter(vararg.arg, ParameterKind.List) {
                        Annotation = expr.Revert(vararg.annotation)
                    });
                if (kwarg != null)
                    parameters.Add(new Parameter(kwarg.arg, ParameterKind.Dictionary) {
                        Annotation = expr.Revert(kwarg.annotation)
                    });
                return parameters.ToArray();
            }

            public PythonList posonlyargs { get; set; }

            public PythonList args { get; set; }

            public ArgType vararg { get; set; }

            public PythonList kwonlyargs { get; set; }

            public PythonList kw_defaults { get; set; }

            public ArgType kwarg { get; set; }

            public PythonList defaults { get; set; }
        }

        [PythonType]
        public abstract class boolop : AST {
        }

        [PythonType]
        public abstract class cmpop : AST {
            internal abstract PythonOperator Revert();
        }

        [PythonType]
        public class comprehension : AST {
            public comprehension() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(target),
                    nameof(iter),
                    nameof(ifs),
                    nameof(is_async)
                });
            }

            public comprehension(
                expr target,
                expr iter,
                PythonList ifs,
                [Optional] int is_async)
                : this() {
                this.target = target;
                this.iter = iter;
                this.ifs = ifs;
                this.is_async = is_async;
            }

            internal comprehension(ComprehensionFor listFor, ComprehensionIf[] listIfs)
                : this() {
                target = Convert(listFor.Left, Store.Instance);
                iter = Convert(listFor.List);
                is_async = listFor.IsAsync ? 1 : 0;
                ifs = new PythonList(listIfs.Length);
                foreach (ComprehensionIf listIf in listIfs)
                    ifs.Add(Convert(listIf.Test));
            }

            internal static ComprehensionIterator[] RevertComprehensions(PythonList comprehensions) {
                var comprehensionIterators = new List<ComprehensionIterator>();
                foreach (comprehension comp in comprehensions) {
                    ComprehensionFor cf = new ComprehensionFor(expr.Revert(comp.target), expr.Revert(comp.iter));
                    cf.IsAsync = comp.is_async != 0;
                    comprehensionIterators.Add(cf);
                    foreach (expr ifs in comp.ifs) {
                        comprehensionIterators.Add(new ComprehensionIf(expr.Revert(ifs)));
                    }
                }
                return comprehensionIterators.ToArray();
            }

            public expr target { get; set; }

            public expr iter { get; set; }

            public PythonList ifs { get; set; }

            public int is_async { get; set; }
        }

        [PythonType]
        public class excepthandler : AST {
            public excepthandler() {
                _attributes = PythonTuple.MakeTuple(new[] {
                    nameof(lineno),
                    nameof(col_offset),
                    nameof(end_lineno),
                    nameof(end_col_offset)
                });
            }
        }

        [PythonType]
        public abstract class expr : AST {
            protected expr() {
                _attributes = PythonTuple.MakeTuple(new[] {
                    nameof(lineno),
                    nameof(col_offset),
                    nameof(end_lineno),
                    nameof(end_col_offset)
                });
            }

            internal virtual AstExpression Revert() {
                throw PythonOps.TypeError("Unexpected expr type: {0}", GetType());
            }

            internal static AstExpression Revert(expr ex) {
                return ex?.Revert();
            }

            internal static AstExpression Revert(object ex) {
                if (ex == null)
                    return null;
                Debug.Assert(ex is expr);
                return ((expr)ex).Revert();
            }

            internal static AstExpression[] RevertExprs(PythonList exprs) {
                // it is assumed that list elements are expr
                AstExpression[] ret = new AstExpression[exprs.Count];
                for (int i = 0; i < exprs.Count; i++)
                    ret[i] = ((expr)exprs[i]).Revert();
                return ret;
            }

        }

        [PythonType]
        public abstract class expr_context : AST {
        }

        [PythonType]
        public class keyword : AST {
            private static readonly PythonTuple __attributes =
                PythonTuple.MakeTuple(new[] {
                    nameof(lineno),
                    nameof(col_offset),
                    nameof(end_lineno),
                    nameof(end_col_offset)
                });

            public keyword() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(arg), nameof(value) });
                _attributes = __attributes;
            }

            public keyword(string arg, expr value)
                : this() {
                this.arg = arg;
                this.value = value;
            }

            internal keyword(IronPython.Compiler.Ast.Keyword arg)
                : this() {
                this.arg = arg.Name;
                value = Convert(arg.Expression);
            }

            public string arg { get; set; }

            public expr value { get; set; }
        }

        [PythonType]
        public abstract class mod : AST {
            internal virtual PythonList GetStatements() {
                return new PythonList();
            }
        }

        [PythonType]
        public abstract class @operator : AST {
            internal abstract PythonOperator Revert();
        }

        [PythonType]
        public abstract class slice : expr {
        }

        [PythonType]
        public abstract class stmt : AST {
            protected stmt() {
                _attributes = PythonTuple.MakeTuple(new[] {
                    nameof(lineno),
                    nameof(col_offset),
                    nameof(end_lineno),
                    nameof(end_col_offset)
                });
            }

            internal virtual Statement Revert() {
                throw PythonOps.TypeError("Unexpected statement type: {0}", GetType());
            }

            internal static Statement RevertStmts(PythonList stmts) {
                if (stmts.Count == 1)
                    return ((stmt)stmts[0]).Revert();
                Statement[] statements = new Statement[stmts.Count];
                for (int i = 0; i < stmts.Count; i++)
                    statements[i] = ((stmt)stmts[i]).Revert();
                return new SuiteStatement(statements);
            }
        }

        [PythonType]
        public abstract class unaryop : AST {
            internal abstract PythonOperator Revert();
        }

        [PythonType]
        public class Add : @operator {
            internal static readonly Add Instance = new Add();
            internal override PythonOperator Revert() => PythonOperator.Add;
        }

        [PythonType]
        public class And : boolop {
            internal static readonly And Instance = new And();
        }

        [PythonType]
        public class AnnAssign : stmt {
            public AnnAssign() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(target), nameof(annotation), nameof(value), nameof(simple) });
            }

            public AnnAssign(expr target, expr annotation, expr value, int simple, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.target = target;
                this.annotation = annotation;
                this.value = value;
                this.simple = simple;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal AnnAssign(AnnotatedAssignStatement stmt)
                : this() {
                target = Convert(stmt.Target, Store.Instance);
                annotation = Convert(stmt.Annotation);
                value = stmt.Value is null ? null : Convert(stmt.Value);
                simple = stmt.Simple ? 1 : 0;
            }

            internal override Statement Revert() {
                return new AnnotatedAssignStatement(expr.Revert(target), expr.Revert(annotation), expr.Revert(value), simple != 0);
            }

            public expr target { get; set; }

            public expr annotation { get; set; }

            public expr value { get; set; }

            public int simple { get; set; }
        }

        [PythonType]
        public class Assert : stmt {
            public Assert() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(test), nameof(msg) });
            }

            public Assert(expr test, expr msg, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.test = test;
                this.msg = msg;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Assert(AssertStatement stmt)
                : this() {
                test = Convert(stmt.Test);
                if (stmt.Message != null)
                    msg = Convert(stmt.Message);
            }

            internal override Statement Revert() {
                return new AssertStatement(expr.Revert(test), expr.Revert(msg));
            }

            public expr test { get; set; }

            public expr msg { get; set; }
        }

        [PythonType]
        public class Assign : stmt {
            public Assign() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(targets),
                    nameof(value),
                    nameof(type_comment)
                });
            }

            public Assign(PythonList targets, expr value, [Optional] string type_comment,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.targets = targets;
                this.value = value;
                this.type_comment = type_comment;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Assign(AssignmentStatement stmt)
                : this() {
                targets = new PythonList(stmt.Left.Count);
                foreach (AstExpression expr in stmt.Left)
                    targets.Add(Convert(expr, Store.Instance));

                value = Convert(stmt.Right);
            }

            internal override Statement Revert() {
                return new AssignmentStatement(expr.RevertExprs(targets), expr.Revert(value));
            }

            public PythonList targets { get; set; }

            public expr value { get; set; }

            public string type_comment { get; set; }
        }

        [PythonType]
        public class AsyncFunctionDef : stmt {
            public AsyncFunctionDef() {
                _fields = PythonTuple.MakeTuple(
                    nameof(name),
                    nameof(args),
                    nameof(body),
                    nameof(decorator_list),
                    nameof(returns),
                    nameof(type_comment),
                    nameof(type_params));
            }

            public AsyncFunctionDef(
                string name,
                arguments args,
                PythonList body,
                PythonList decorator_list,
                expr returns,
                [Optional] string type_comment,
                [Optional] PythonList type_params,
                [Optional] int? lineno,
                [Optional] int? col_offset)
                : this() {
                this.name = name;
                this.args = args;
                this.body = body;
                this.decorator_list = decorator_list;
                this.returns = returns;
                this.type_comment = type_comment;
                this.type_params = type_params ?? new PythonList();
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal AsyncFunctionDef(FunctionDefinition def)
                : this() {
                name = def.Name;
                args = new arguments(def.Parameters);
                body = ConvertStatements(def.Body);
                decorator_list = new PythonList();
                if (def.Decorators is not null) {
                    foreach (AstExpression decorator in def.Decorators) {
                        decorator_list.Add(Convert(decorator));
                    }
                }
                returns = def.ReturnAnnotation is null
                    ? null
                    : Convert(def.ReturnAnnotation);
                type_params = new PythonList();
            }

            internal override Statement Revert() {
                FunctionDefinition fd = new FunctionDefinition(
                    name,
                    args.Revert(),
                    RevertStmts(body),
                    isAsync: true);
                fd.IsGenerator = _containsYield;
                _containsYield = false;
                if (decorator_list.Count != 0) {
                    fd.Decorators = expr.RevertExprs(decorator_list);
                }
                return fd;
            }

            public string name { get; set; }
            public arguments args { get; set; }
            public PythonList body { get; set; }
            public PythonList decorator_list { get; set; }
            public expr returns { get; set; }
            public string type_comment { get; set; }
            public PythonList type_params { get; set; }
        }

        [PythonType]
        public class Attribute : expr {
            public Attribute() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), nameof(attr), nameof(ctx) });
            }

            public Attribute(expr value, string attr)
                : this(value, attr, Load.Instance, null, null) { }

            public Attribute(expr value, string attr, expr_context ctx,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                this.attr = attr;
                this.ctx = ctx;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Attribute(MemberExpression attr, expr_context ctx)
                : this() {
                value = Convert(attr.Target);
                this.attr = attr.Name;
                this.ctx = ctx;
            }

            internal override AstExpression Revert() {
                return new MemberExpression(expr.Revert(value), attr);
            }

            public expr value { get; set; }

            public string attr { get; set; }

            public expr_context ctx { get; set; }
        }

        [PythonType]
        public class AugAssign : stmt {
            public AugAssign() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(target), nameof(op), nameof(value) });
            }

            public AugAssign(expr target, @operator op, expr value,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.target = target;
                this.op = op;
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal AugAssign(AugmentedAssignStatement stmt)
                : this() {
                target = Convert(stmt.Left, Store.Instance);
                value = Convert(stmt.Right);
                op = (@operator)Convert(stmt.Operator);
            }

            internal override Statement Revert() {
                return new AugmentedAssignStatement(op.Revert(), expr.Revert(target), expr.Revert(value));
            }

            public expr target { get; set; }

            public @operator op { get; set; }

            public expr value { get; set; }
        }

        /// <summary>
        /// Not used.
        /// </summary>
        [PythonType]
        public class AugLoad : expr_context {
        }

        /// <summary>
        /// Not used.
        /// </summary>
        [PythonType]
        public class AugStore : expr_context {
        }

        [PythonType]
        public class BinOp : expr {
            public BinOp() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(left), nameof(op), nameof(right) });
            }

            public BinOp(expr left, @operator op, expr right, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.left = left;
                this.op = op;
                this.right = right;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal BinOp(BinaryExpression expr, @operator op)
                : this() {
                left = Convert(expr.Left);
                right = Convert(expr.Right);
                this.op = op;
            }

            internal override AstExpression Revert() {
                return new BinaryExpression(op.Revert(), expr.Revert(left), expr.Revert(right));
            }

            public expr left { get; set; }

            public expr right { get; set; }

            public @operator op { get; set; }
        }

        [PythonType]
        public class BitAnd : @operator {
            internal static readonly BitAnd Instance = new BitAnd();
            internal override PythonOperator Revert() => PythonOperator.BitwiseAnd;
        }

        [PythonType]
        public class BitOr : @operator {
            internal static readonly BitOr Instance = new BitOr();
            internal override PythonOperator Revert() => PythonOperator.BitwiseOr;
        }

        [PythonType]
        public class BitXor : @operator {
            internal static readonly BitXor Instance = new BitXor();
            internal override PythonOperator Revert() => PythonOperator.Xor;
        }

        [PythonType]
        public class BoolOp : expr {
            public BoolOp() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(op), nameof(values) });
            }

            public BoolOp(boolop op, PythonList values, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.op = op;
                this.values = values;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal BoolOp(AndExpression and)
                : this() {
                values = PythonList.FromArrayNoCopy(Convert(and.Left), Convert(and.Right));
                op = And.Instance;
            }

            internal BoolOp(OrExpression or)
                : this() {
                values = PythonList.FromArrayNoCopy(Convert(or.Left), Convert(or.Right));
                op = Or.Instance;
            }

            internal override AstExpression Revert() {
                if (op == And.Instance || op is And) {
                    AndExpression ae = new AndExpression(
                        expr.Revert(values[0]),
                        expr.Revert(values[1]));
                    return ae;
                }

                if (op == Or.Instance || op is Or) {
                    OrExpression oe = new OrExpression(
                        expr.Revert(values[0]),
                        expr.Revert(values[1]));
                    return oe;
                }
                throw PythonOps.TypeError("Unexpected boolean operator: {0}", op);
            }

            public boolop op { get; set; }

            public PythonList values { get; set; }
        }

        [PythonType]
        public class Break : stmt {
            internal static readonly Break Instance = new Break();

            internal Break()
                : this(null, null) { }

            public Break([Optional] int? lineno, [Optional] int? col_offset) {
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal override Statement Revert() {
                return new BreakStatement();
            }
        }

        [PythonType]
        public class Bytes : expr {
            public Bytes() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(s), });
            }

            internal Bytes(IronPython.Runtime.Bytes s)
                : this(s, null, null) { }

            public Bytes(IronPython.Runtime.Bytes s, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.s = s;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal override AstExpression Revert() {
                return new ConstantExpression(s);
            }

            public IronPython.Runtime.Bytes s { get; set; }
        }

        [PythonType]
        public class Call : expr {
            public Call() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(func), nameof(args), nameof(keywords) });
            }

            public Call(expr func, PythonList args, PythonList keywords, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.func = func;
                this.args = args;
                this.keywords = keywords;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Call(CallExpression call)
                : this() {
                args = new PythonList(call.Args.Count);
                foreach (var arg in call.Args) {
                    args.Add(Convert(arg));
                }
                keywords = new PythonList(call.Kwargs.Count);
                foreach (var arg in call.Kwargs) {
                    keywords.Add(new keyword(arg));
                }
                func = Convert(call.Target);
            }

            internal override AstExpression Revert() {
                AstExpression target = expr.Revert(func);
                List<AstExpression> newArgs = new();
                List<Keyword> newKwargs = new();
                foreach (expr ex in args) {
                    newArgs.Add(ex.Revert());
                }
                foreach (keyword kw in keywords) {
                    newKwargs.Add(new Keyword(kw.arg, kw.value.Revert()));
                }

                return new CallExpression(target, newArgs, newKwargs);
            }

            public expr func { get; set; }

            public PythonList args { get; set; }

            public PythonList keywords { get; set; }
        }

        [PythonType]
        public class ClassDef : stmt {
            public ClassDef() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(name),
                    nameof(bases),
                    nameof(keywords),
                    nameof(body),
                    nameof(decorator_list),
                    nameof(type_params)
                });
            }

            public ClassDef(
                string name,
                PythonList bases,
                PythonList keywords,
                PythonList body,
                PythonList decorator_list,
                [Optional] PythonList type_params,
                [Optional] int? lineno,
                [Optional] int? col_offset)
                : this() {
                this.name = name;
                this.bases = bases;
                this.keywords = keywords;
                this.body = body;
                this.decorator_list = decorator_list;
                this.type_params = type_params ?? new PythonList();
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal ClassDef(ClassDefinition def)
                : this() {
                name = def.Name;
                bases = new PythonList(def.Bases.Count);
                foreach (var arg in def.Bases) {
                    bases.Add(Convert(arg));
                }
                keywords = new PythonList(def.Keywords.Count);
                foreach (var arg in def.Keywords) {
                    keywords.Add(new keyword(arg));
                }
                body = ConvertStatements(def.Body);
                if (def.Decorators != null) {
                    decorator_list = new PythonList(def.Decorators.Count);
                    foreach (AstExpression expr in def.Decorators)
                        decorator_list.Add(Convert(expr));
                } else {
                    decorator_list = new PythonList(0);
                }
                type_params = new PythonList();
            }

            internal override Statement Revert() {
                List<AstExpression> newBases = new();
                List<Keyword> newKeywords = new();
                foreach (expr ex in bases) {
                    newBases.Add(ex.Revert());
                }
                foreach (keyword kw in keywords) {
                    newKeywords.Add(new Keyword(kw.arg, kw.value.Revert()));
                }

                ClassDefinition cd = new ClassDefinition(name, newBases, newKeywords, RevertStmts(body));
                if (decorator_list.Count != 0)
                    cd.Decorators = expr.RevertExprs(decorator_list);
                return cd;
            }

            public string name { get; set; }

            public PythonList bases { get; set; }

            public PythonList keywords { get; set; }

            public PythonList body { get; set; }

            public PythonList decorator_list { get; set; }

            public PythonList type_params { get; set; }
        }

        [PythonType]
        public class Compare : expr {
            public Compare() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(left), nameof(ops), nameof(comparators) });
            }

            public Compare(expr left, PythonList ops, PythonList comparators,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.left = left;
                this.ops = ops;
                this.comparators = comparators;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Compare(BinaryExpression expr)
                : this() {
                left = Convert(expr.Left);
                ops = new PythonList();
                comparators = new PythonList();
                while (BinaryExpression.IsComparison(expr.Right)) {
                    BinaryExpression right = (BinaryExpression)expr.Right;
                    // start accumulating ops and comparators
                    ops.Add(Convert(expr.Operator));
                    comparators.Add(Convert(right.Left));
                    expr = right;
                }
                ops.Add(Convert(expr.Operator));
                comparators.Add(Convert(expr.Right));
            }

            internal override AstExpression Revert() {
                // the most likely case first
                if (ops.Count == 1) {
                    return new BinaryExpression(
                        ((cmpop)(ops[0])).Revert(),
                        expr.Revert(left),
                        expr.Revert(comparators[0]));
                }

                // chaining of comparators is processed here (a>b>c> ...)
                Debug.Assert(ops.Count > 1, "expected 2 or more ops in chained comparator");
                int i = ops.Count - 1;
                BinaryExpression right = new BinaryExpression(
                        ((cmpop)(ops[i])).Revert(),
                        expr.Revert(comparators[i - 1]),
                        expr.Revert(comparators[i]));
                i--;
                while (i > 0) {
                    right = new BinaryExpression(
                        ((cmpop)(ops[i])).Revert(),
                        expr.Revert(comparators[i - 1]),
                        right);
                    i--;
                }
                return new BinaryExpression(
                        ((cmpop)(ops[0])).Revert(),
                        expr.Revert(left),
                        right);
            }

            public expr left { get; set; }

            public PythonList ops { get; set; }

            public PythonList comparators { get; set; }
        }

        [PythonType]
        public class Constant : expr {
            public new static object __new__(
                CodeContext context,
                [NotNone] PythonType pythonType,
                [ParamDictionary] IDictionary<object, object> kwargs,
                [NotNone] params object[] args) =>
                pythonType.CreateInstance(context);

            public Constant() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), nameof(kind) });
            }

            internal Constant(object value)
                : this(value, null, null) { }

            public Constant(object value, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            public Constant(object value, string kind, [Optional] int? lineno, [Optional] int? col_offset)
                : this(value, lineno, col_offset) {
                this.kind = kind;
            }

            internal override AstExpression Revert() {
                return new ConstantExpression(value);
            }

            public object value { get; set; }
            public string kind { get; set; }
        }

        [PythonType]
        public class Continue : stmt {
            internal static readonly Continue Instance = new Continue();

            internal Continue()
                : this(null, null) { }

            public Continue([Optional] int? lineno, [Optional] int? col_offset) {
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal override Statement Revert() {
                return new ContinueStatement();
            }
        }

        [PythonType]
        public class Del : expr_context {
            internal static readonly Del Instance = new Del();
        }

        [PythonType]
        public class Delete : stmt {
            public Delete() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(targets), });
            }

            public Delete(PythonList targets, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.targets = targets;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Delete(DelStatement stmt)
                : this() {
                targets = new PythonList(stmt.Expressions.Count);
                foreach (AstExpression expr in stmt.Expressions)
                    targets.Add(Convert(expr, Del.Instance));
            }

            internal override Statement Revert() {
                return new DelStatement(expr.RevertExprs(targets));
            }

            public PythonList targets { get; set; }
        }

        [PythonType]
        public class Dict : expr {
            public Dict() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(keys), nameof(values) });
            }

            public Dict(PythonList keys, PythonList values, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.keys = keys;
                this.values = values;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Dict(DictionaryExpression expr)
                : this() {
                keys = new PythonList(expr.Items.Count);
                values = new PythonList(expr.Items.Count);
                foreach (SliceExpression item in expr.Items) {
                    keys.Add(item.SliceStart is null ? null : Convert(item.SliceStart));
                    values.Add(Convert(item.SliceStop));
                }
            }

            internal override AstExpression Revert() {
                SliceExpression[] e = new SliceExpression[values.Count];
                for (int i = 0; i < values.Count; i++) {
                    e[i] = new SliceExpression(
                        expr.Revert(keys[i]),
                        expr.Revert(values[i]),
                        null);
                }
                return new DictionaryExpression(e);
            }

            public PythonList keys { get; set; }

            public PythonList values { get; set; }
        }

        [PythonType]
        public class DictComp : expr {
            public DictComp() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(key), nameof(value), nameof(generators) });
            }

            public DictComp(expr key, expr value, PythonList generators,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.key = key;
                this.value = value;
                this.generators = generators;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal DictComp(DictionaryComprehension comp)
                : this() {
                key = Convert(comp.Key);
                value = Convert(comp.Value);
                generators = Convert(comp.Iterators);
            }

            internal override AstExpression Revert() {
                return new DictionaryComprehension(expr.Revert(key), expr.Revert(value), comprehension.RevertComprehensions(generators));
            }

            public expr key { get; set; }

            public expr value { get; set; }

            public PythonList generators { get; set; }
        }

        [PythonType]
        public class Div : @operator {
            internal static readonly Div Instance = new Div();
            internal override PythonOperator Revert() => PythonOperator.TrueDivide;
        }

        [PythonType]
        public class Ellipsis : expr {
            internal static readonly Ellipsis Instance = new Ellipsis();
            internal override AstExpression Revert() => new ConstantExpression(PythonOps.Ellipsis);
        }

        [PythonType]
        public class Eq : cmpop {
            internal static readonly Eq Instance = new Eq();
            internal override PythonOperator Revert() => PythonOperator.Equal;
        }

        [PythonType]
        public class ExceptHandler : excepthandler {
            public ExceptHandler() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(type), nameof(name), nameof(body) });
            }

            public ExceptHandler([Optional] expr type, [Optional] object name, PythonList body,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.type = type;
                this.name = name;
                this.body = body;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal ExceptHandler(TryStatementHandler stmt)
                : this() {
                if (stmt.Test != null)
                    type = Convert(stmt.Test);
                if (stmt.Target != null)
                    name = ((Name)Convert(stmt.Target, Store.Instance)).id;

                body = ConvertStatements(stmt.Body);
            }

            internal TryStatementHandler RevertHandler() {
                expr target = name switch {
                    null => null,
                    string identifier => new Name(identifier, Store.Instance),
                    expr expression => expression,
                    _ => throw new ArgumentTypeException(
                        "ExceptHandler.name must be a string or None.")
                };
                return new TryStatementHandler(expr.Revert(type), expr.Revert(target), stmt.RevertStmts(body));
            }

            public expr type { get; set; }

            public object name { get; set; }

            public PythonList body { get; set; }
        }

        [PythonType]
        public class Expr : stmt {
            public Expr() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), });
            }

            public Expr(expr value, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Expr(ExpressionStatement stmt)
                : this() {
                value = Convert(stmt.Expression);
            }

            internal override Statement Revert() {
                return new ExpressionStatement(expr.Revert(value));
            }

            public expr value { get; set; }
        }

        [PythonType]
        public class Expression : mod {
            public Expression() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(body), });
            }

            public Expression(expr body)
                : this() {
                this.body = body;
            }

            internal Expression(SuiteStatement suite)
                : this() {
                body = Convert(((ExpressionStatement)suite.Statements[0]).Expression);
            }

            internal override PythonList GetStatements() {
                return PythonList.FromArrayNoCopy(body);
            }

            public expr body { get; set; }
        }

        [PythonType]
        public class ExtSlice : slice {
            public ExtSlice() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(dims), });
            }

            public ExtSlice(PythonList dims)
                : this() {
                this.dims = dims;
            }

            internal new AstExpression[] Revert() {
                List<AstExpression> ret = new List<AstExpression>(dims.Count);
                foreach (expr ex in dims)
                    ret.Add(expr.Revert(ex));
                return ret.ToArray();
            }

            public PythonList dims;
        }

        [PythonType]
        public class FloorDiv : @operator {
            internal static readonly FloorDiv Instance = new FloorDiv();
            internal override PythonOperator Revert() => PythonOperator.FloorDivide;
        }

        [PythonType]
        public class For : stmt {
            public For() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(target),
                    nameof(iter),
                    nameof(body),
                    nameof(orelse),
                    nameof(type_comment)
                });
            }

            public For(expr target, expr iter, PythonList body, [Optional] PythonList orelse, [Optional] string type_comment,
               [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.target = target;
                this.iter = iter;
                this.body = body;
                if (null == orelse)
                    this.orelse = new PythonList();
                else
                    this.orelse = orelse;
                this.type_comment = type_comment;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal For(ForStatement stmt)
                : this() {
                target = Convert(stmt.Left, Store.Instance);
                iter = Convert(stmt.List);
                body = ConvertStatements(stmt.Body);
                orelse = ConvertStatements(stmt.Else, true);
            }

            internal override Statement Revert() {
                return new ForStatement(expr.Revert(target), expr.Revert(iter), RevertStmts(body), RevertStmts(orelse));
            }

            public expr target { get; set; }

            public expr iter { get; set; }

            public PythonList body { get; set; }

            public PythonList orelse { get; set; }

            public string type_comment { get; set; }
        }

        [PythonType]
        public class FormattedValue : expr {
            private static readonly PythonTuple __fields = PythonTuple.MakeTuple(nameof(value), nameof(conversion), nameof(format_spec));
            public FormattedValue() {
                _fields = __fields;
            }

            public FormattedValue(expr value, int conversion, expr format_spec, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                this.conversion = conversion;
                this.format_spec = format_spec;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal FormattedValue(FormattedValueExpression expr)
                : this(Convert(expr.Value), expr.Conversion ?? -1, expr.FormatSpec == null ? null : Convert(expr.FormatSpec)) { }

            internal override AstExpression Revert() {
                char? conv = conversion == -1 ? null : checked((char)conversion);
                return new FormattedValueExpression(Revert(value), conv, format_spec == null ? null : (Revert(format_spec) as JoinedStringExpression));
            }

            public expr value { get; set; }
            public int conversion { get; set; }
            public expr format_spec { get; set; }
        }

        [PythonType]
        public class FunctionDef : stmt {
            public static readonly PythonTuple __match_args__ =
                PythonTuple.MakeTuple(
                    nameof(name),
                    nameof(args),
                    nameof(body),
                    nameof(decorator_list),
                    nameof(returns),
                    nameof(type_comment),
                    nameof(type_params));

            public FunctionDef() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(name),
                    nameof(args),
                    nameof(body),
                    nameof(decorator_list),
                    nameof(returns),
                    nameof(type_comment),
                    nameof(type_params)
                });
            }

            public FunctionDef(
                string name,
                arguments args,
                PythonList body,
                PythonList decorator_list,
                expr returns,
                [Optional] string type_comment,
                [Optional] PythonList type_params,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.name = name;
                this.args = args;
                this.body = body;
                this.decorator_list = decorator_list;
                this.returns = returns;
                this.type_comment = type_comment;
                this.type_params = type_params ?? new PythonList();
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal FunctionDef(FunctionDefinition def)
                : this() {
                name = def.Name;
                args = new arguments(def.Parameters);
                body = ConvertStatements(def.Body);

                if (def.Decorators != null) {
                    decorator_list = new PythonList(def.Decorators.Count);
                    foreach (AstExpression expr in def.Decorators)
                        decorator_list.Add(Convert(expr));
                } else {
                    decorator_list = new PythonList(0);
                }

                if (def.ReturnAnnotation != null)
                    returns = Convert(def.ReturnAnnotation);
                type_params = new PythonList();
            }

            internal override Statement Revert() {
                FunctionDefinition fd = new FunctionDefinition(name, args.Revert(), RevertStmts(body));
                fd.IsGenerator = _containsYield;
                _containsYield = false;
                if (decorator_list.Count != 0)
                    fd.Decorators = expr.RevertExprs(decorator_list);
                return fd;
            }

            public string name { get; set; }

            public arguments args { get; set; }

            public PythonList body { get; set; }

            public PythonList decorator_list { get; set; }

            public expr returns { get; set; }

            public string type_comment { get; set; }

            public PythonList type_params { get; set; }
        }

        [PythonType]
        public class GeneratorExp : expr {
            public GeneratorExp() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(elt), nameof(generators) });
            }

            public GeneratorExp(expr elt, PythonList generators, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.elt = elt;
                this.generators = generators;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            // given following generator: (a for b in c for d in e for f in g)
            // PythonAST (Iron) Representation looks like this:
            //
            // Iterable
            //  NameExpression c
            //
            // Function
            //  FunctionDefinition<genexpr>
            //    Parameter__gen_$_parm__
            //    ForStatement
            //      NameExpression b
            //      NameExpression __gen_$_parm__
            //      ForStatement
            //        NameExpression d
            //        NameExpression e
            //        ForStatement
            //          NameExpression f
            //          NameExpression g
            //          ExpressionStatement
            //            YieldExpression
            //            NameExpression a
            //
            // and corresponding ast (implementation agnostic) looks like this:
            //        ('Expression', ('GeneratorExp', (1, 1),
            //            ('Name', (1, 1), 'a', ('Load',)),
            //            [('comprehension', ('Name', (1, 7), 'b', ('Store',)), ('Name', (1, 12), 'c', ('Load',)), []),
            //             ('comprehension', ('Name', (1, 18), 'd', ('Store',)), ('Name', (1, 23), 'e', ('Load',)), []),
            //             ('comprehension', ('Name', (1, 29), 'f', ('Store',)), ('Name', (1, 34), 'g', ('Load',)), [])
            //            ]))

            internal GeneratorExp(GeneratorExpression expr)
                : this() {
                ExtractListComprehensionIterators walker = new ExtractListComprehensionIterators();
                expr.Function.Body.Walk(walker);
                ComprehensionIterator[] iters = walker.Iterators;
                Debug.Assert(iters.Length != 0, "A generator expression cannot have zero iterators.");
                iters[0] = new ComprehensionFor(((ComprehensionFor)iters[0]).Left, expr.Iterable);
                elt = Convert(walker.Yield.Expression);
                generators = Convert(iters);
            }

            internal class ExtractListComprehensionIterators : PythonWalker {
                private readonly List<ComprehensionIterator> _iterators = new List<ComprehensionIterator>();
                public YieldExpression Yield;

                public ComprehensionIterator[] Iterators {
                    get { return _iterators.ToArray(); }
                }

                public override bool Walk(ForStatement node) {
                    _iterators.Add(new ComprehensionFor(node.Left, node.List));
                    node.Body.Walk(this);
                    return false;
                }

                public override bool Walk(IfStatement node) {
                    _iterators.Add(new ComprehensionIf(node.Tests[0].Test));
                    node.Tests[0].Body.Walk(this);
                    return false;
                }

                public override bool Walk(YieldExpression node) {
                    Yield = node;
                    return false;
                }
            }

            // TODO: following 2 names are copy paste from Parser.cs
            //       it would be better to have them in one place
            private const string generatorFnName = "<genexpr>";
            private const string generatorFnArgName = "__gen_$_parm__";

            internal override AstExpression Revert() {
                Statement stmt = new ExpressionStatement(new YieldExpression(expr.Revert(elt)));
                int comprehensionIdx = generators.Count - 1;
                AstExpression list;
                do {
                    comprehension c = (comprehension)generators[comprehensionIdx];
                    if (c.ifs != null && c.ifs.Count != 0) {
                        int ifIdx = c.ifs.Count - 1;
                        while (ifIdx >= 0) {
                            IfStatementTest ist = new IfStatementTest(expr.Revert(c.ifs[ifIdx]), stmt);
                            stmt = new IfStatement(new IfStatementTest[] { ist }, null);
                            ifIdx--;
                        }
                    }
                    list = expr.Revert(c.iter);
                    stmt = new ForStatement(expr.Revert(c.target), list, stmt, null);
                    comprehensionIdx--;
                } while (comprehensionIdx >= 0);
                ((ForStatement)stmt).List = new NameExpression(generatorFnArgName);
                Parameter parameter = new Parameter(generatorFnArgName, 0);
                FunctionDefinition functionDefinition = new(generatorFnName, [parameter], stmt) {
                    IsGenerator = true,
                    IsGeneratorExpression = true
                };
                return new GeneratorExpression(functionDefinition, list);
            }

            public expr elt { get; set; }

            public PythonList generators { get; set; }
        }

        [PythonType]
        public class Global : stmt {
            public Global() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(names), });
            }

            public Global(PythonList names, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.names = names;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Global(GlobalStatement stmt)
                : this() {
                names = PythonList.FromGenericCollection(stmt.Names);
            }

            internal override Statement Revert() {
                string[] newNames = new string[names.Count];
                for (int i = 0; i < names.Count; i++)
                    newNames[i] = (string)names[i];
                return new GlobalStatement(newNames);
            }

            public PythonList names { get; set; }
        }

        [PythonType]
        public class Gt : cmpop {
            internal static readonly Gt Instance = new Gt();
            internal override PythonOperator Revert() => PythonOperator.GreaterThan;
        }

        [PythonType]
        [Documentation("GtE")]
        public class GtE : cmpop {
            internal static readonly GtE Instance = new GtE();
            internal override PythonOperator Revert() => PythonOperator.GreaterThanOrEqual;
        }

        [PythonType]
        public class If : stmt {
            public If() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(test), nameof(body), nameof(orelse) });
            }

            public If(expr test, PythonList body, [Optional] PythonList orelse,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.test = test;
                this.body = body;
                if (null == orelse)
                    this.orelse = new PythonList();
                else
                    this.orelse = orelse;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal If(IfStatement stmt)
                : this() {
                If current = this;
                If parent = null;
                foreach (IfStatementTest ifTest in stmt.Tests) {
                    if (parent != null) {
                        current = new If();
                        parent.orelse = PythonList.FromArrayNoCopy(current);
                    }

                    current.Initialize(ifTest);
                    parent = current;
                }

                current.orelse = ConvertStatements(stmt.ElseStatement, true);
            }

            internal void Initialize(IfStatementTest ifTest) {
                test = Convert(ifTest.Test);
                body = ConvertStatements(ifTest.Body);
                GetSourceLocation(ifTest);
            }

            internal override Statement Revert() {
                List<IfStatementTest> tests = new List<IfStatementTest>();
                tests.Add(new IfStatementTest(expr.Revert(test), RevertStmts(body)));
                If currIf = this;
                while (currIf.orelse != null && currIf.orelse.Count == 1 && currIf.orelse[0] is If) {
                    If orelse = (If)currIf.orelse[0];
                    tests.Add(new IfStatementTest(expr.Revert(orelse.test), RevertStmts(orelse.body)));
                    currIf = orelse;
                }
                return new IfStatement(tests.ToArray(), RevertStmts(currIf.orelse));
            }

            public expr test { get; set; }

            public PythonList body { get; set; }

            public PythonList orelse { get; set; }
        }

        [PythonType]
        public class IfExp : expr {
            public IfExp() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(test), nameof(body), nameof(orelse) });
            }

            public IfExp(expr test, expr body, expr orelse, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.test = test;
                this.body = body;
                this.orelse = orelse;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal IfExp(ConditionalExpression cond)
                : this() {
                test = Convert(cond.Test);
                body = Convert(cond.TrueExpression);
                orelse = Convert(cond.FalseExpression);
            }

            internal override AstExpression Revert() {
                return new ConditionalExpression(expr.Revert(test), expr.Revert(body), expr.Revert(orelse));
            }

            public expr test { get; set; }

            public expr body { get; set; }

            public expr orelse { get; set; }
        }

        private static char[] MODULE_NAME_SPLITTER = new char[1] { '.' };


        [PythonType]
        public class Import : stmt {
            public Import() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(names),
                    nameof(is_lazy)
                });
            }

            public Import(PythonList names, [Optional] int? is_lazy,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.names = names;
                this.is_lazy = is_lazy;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Import(ImportStatement stmt)
                : this() {
                names = ConvertAliases(stmt.Names, stmt.AsNames);
                is_lazy = stmt.IsLazy ? 1 : null;
            }

            internal override Statement Revert() {
                ModuleName[] moduleNames = new ModuleName[names.Count];
                String[] asNames = new String[names.Count];
                for (int i = 0; i < names.Count; i++) {
                    alias alias = (alias)names[i];
                    moduleNames[i] = new ModuleName(alias.name.Split(MODULE_NAME_SPLITTER));
                    asNames[i] = alias.asname;
                }
                return new ImportStatement(
                    moduleNames,
                    asNames,
                    isLazy: is_lazy == 1);
            }

            public PythonList names { get; set; }
            public int? is_lazy { get; set; }
        }

        [PythonType]
        public class ImportFrom : stmt {
            public ImportFrom() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(module),
                    nameof(names),
                    nameof(level),
                    nameof(is_lazy)
                });
            }

            public ImportFrom([Optional] string module, PythonList names, [Optional] int? level, [Optional] int? is_lazy,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.module = module;
                this.names = names;
                this.level = level;
                this.is_lazy = is_lazy;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            public ImportFrom(FromImportStatement stmt)
                : this() {
                module = stmt.Root.MakeString();
                module = string.IsNullOrEmpty(module) ? null : module;
                names = ConvertAliases(stmt.Names, stmt.AsNames);
                if (stmt.Root is RelativeModuleName)
                    level = ((RelativeModuleName)stmt.Root).DotCount;
                is_lazy = stmt.IsLazy ? 1 : null;
            }

            internal override Statement Revert() {
                ModuleName root;
                if (module is null) {
                    root = new RelativeModuleName(
                        Array.Empty<string>(),
                        level.GetValueOrDefault());
                } else {
                    if (module[0] == '.') // relative module
                        root = new RelativeModuleName(
                            module.Split(MODULE_NAME_SPLITTER),
                            level.GetValueOrDefault());
                    else {
                        root = new ModuleName(module.Split(MODULE_NAME_SPLITTER));
                    }
                }

                if (names.Count == 1 && ((alias)names[0]).name == "*")
                    return new FromImportStatement(
                        root,
                        (string[])FromImportStatement.Star,
                        null,
                        false,
                        isLazy: is_lazy == 1);

                String[] newNames = new String[names.Count];
                String[] asNames = new String[names.Count];
                for (int i = 0; i < names.Count; i++) {
                    alias alias = (alias)names[i];
                    newNames[i] = alias.name;
                    asNames[i] = alias.asname;
                }
                return new FromImportStatement(
                    root,
                    newNames,
                    asNames,
                    false,
                    isLazy: is_lazy == 1);
            }

            public string module { get; set; }

            public PythonList names { get; set; }

            public int? level { get; set; }

            public int? is_lazy { get; set; }
        }

        [PythonType]
        public class In : cmpop {
            internal static readonly In Instance = new In();
            internal override PythonOperator Revert() => PythonOperator.In;
        }

        [PythonType]
        public class Index : slice {
            public Index() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), });
            }

            public Index(expr value)
                : this() {
                this.value = value;
            }

            public expr value { get; set; }
        }

        [PythonType]
        public class Interactive : mod {
            public Interactive() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(body), });
            }

            public Interactive(PythonList body)
                : this() {
                this.body = body;
            }

            internal Interactive(SuiteStatement suite)
                : this() {
                body = ConvertStatements(suite);
            }

            internal override PythonList GetStatements() {
                return body;
            }

            public PythonList body { get; set; }
        }

        [PythonType]
        public class Invert : unaryop {
            internal static readonly Invert Instance = new Invert();
            internal override PythonOperator Revert() => PythonOperator.Invert;
        }

        [PythonType]
        public class Is : cmpop {
            internal static readonly Is Instance = new Is();
            internal override PythonOperator Revert() => PythonOperator.Is;
        }

        [PythonType]
        public class IsNot : cmpop {
            internal static readonly IsNot Instance = new IsNot();
            internal override PythonOperator Revert() => PythonOperator.IsNot;
        }

        [PythonType]
        public class JoinedStr : expr {
            private static readonly PythonTuple __fields = PythonTuple.MakeTuple(nameof(values));

            public JoinedStr() {
                _fields = __fields;
            }

            public JoinedStr(PythonList values, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.values = values;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal JoinedStr(JoinedStringExpression expr)
                : this() {
                values = new PythonList(expr.Values.Count);
                foreach (var val in expr.Values)
                    values.Add(Convert(val));
            }

            internal override AstExpression Revert() {
                var e = new AstExpression[values.Count];
                int i = 0;
                foreach (expr el in values)
                    e[i++] = Revert(el);
                return new JoinedStringExpression(e);
            }

            public PythonList values { get; set; }
        }

        [PythonType]
        public class Interpolation : expr {
            public Interpolation() {
                _fields = PythonTuple.MakeTuple(
                    new[] { nameof(value), nameof(str), nameof(conversion), nameof(format_spec) });
            }

            public Interpolation(expr value, string str, int conversion, expr format_spec)
                : this() {
                this.str = str;
                this.value = value;
                this.conversion = conversion;
                this.format_spec = format_spec;
            }

            internal Interpolation(TemplateInterpolation interpolation)
                : this() {
                str = interpolation.Expression;
                value = Convert(interpolation.Value);
                conversion = interpolation.Conversion is { Length: 1 }
                    ? interpolation.Conversion[0]
                    : -1;
                format_spec = interpolation.FormatSpec is null
                    ? null
                    : Convert(interpolation.FormatSpec);
            }

            public string str { get; set; }
            public expr value { get; set; }
            public int conversion { get; set; }
            public expr format_spec { get; set; }
        }

        [PythonType]
        public class TemplateStr : expr {
            public TemplateStr() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(values) });
            }

            public TemplateStr(PythonList values)
                : this() {
                this.values = values;
            }

            internal TemplateStr(TemplateStringExpression template)
                : this() {
                values = new PythonList();
                for (int i = 0; i < template.Strings.Length; i++) {
                    values.Add(new Constant(template.Strings[i]));
                    if (i < template.Interpolations.Length) {
                        values.Add(new Interpolation(template.Interpolations[i]));
                    }
                }
            }

            public PythonList values { get; set; }
        }

        [PythonType]
        public class Lambda : expr {
            public Lambda() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(args), nameof(body) });
            }

            public Lambda(arguments args, expr body, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.args = args;
                this.body = body;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Lambda(LambdaExpression lambda)
                : this() {
                FunctionDef def = (FunctionDef)Convert(lambda.Function);
                args = def.args;
                Debug.Assert(def.body.Count == 1, "LambdaExpression body should be one statement.");
                stmt statement = (stmt)def.body[0];
                if (statement is Return)
                    body = ((Return)statement).value;
                else if (statement is Expr) {
                    // What should be sufficient is:
                    // _body = ((Expr)statement).value;
                    // but, AST comes with trees containing twice YieldExpression.
                    // For:
                    //   lambda x: (yield x)
                    // it comes back with:
                    //
                    //IronPython.Compiler.Ast.LambdaExpression
                    //IronPython.Compiler.Ast.FunctionDefinition<lambda$334>generator
                    //  IronPython.Compiler.Ast.Parameter  x
                    //  IronPython.Compiler.Ast.ExpressionStatement
                    //    IronPython.Compiler.Ast.YieldExpression     <<<<<<<<
                    //      IronPython.Compiler.Ast.YieldExpression   <<<<<<<< why twice?
                    //        IronPython.Compiler.Ast.NameExpression x

                    body = ((Yield)((Expr)statement).value).value;
                } else
                    throw PythonOps.TypeError("Unexpected statement type: {0}, expected Return or Expr", statement.GetType());
            }

            internal override AstExpression Revert() {
                Statement newBody;
                AstExpression exp = expr.Revert(body);
                if (!_containsYield)
                    newBody = new ReturnStatement(exp);
                else
                    newBody = new ExpressionStatement(exp);
                Parameter[] para = args.Revert();
                FunctionDefinition fd = new FunctionDefinition(null, para, newBody);
                fd.IsGenerator = _containsYield;
                _containsYield = false;
                return new LambdaExpression(fd);
            }

            public arguments args { get; set; }

            public expr body { get; set; }
        }

        [PythonType]
        public class List : expr {
            public List() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(elts), nameof(ctx) });
            }

            public List(PythonList elts)
                : this(elts, Load.Instance, null, null) { }

            public List(PythonList elts, expr_context ctx, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.elts = elts;
                this.ctx = ctx;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal List(ListExpression list, expr_context ctx)
                : this() {
                elts = new PythonList(list.Items.Count);
                foreach (AstExpression expr in list.Items)
                    elts.Add(Convert(expr, ctx));

                this.ctx = ctx;
            }

            internal override AstExpression Revert() {
                AstExpression[] e = new AstExpression[elts.Count];
                int i = 0;
                foreach (expr el in elts)
                    e[i++] = expr.Revert(el);
                return new ListExpression(e);
            }

            public PythonList elts { get; set; }

            public expr_context ctx { get; set; }
        }

        [PythonType]
        public class ListComp : expr {
            public ListComp() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(elt), nameof(generators) });
            }

            public ListComp(expr elt, PythonList generators, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.elt = elt;
                this.generators = generators;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal ListComp(ListComprehension comp)
                : this() {
                elt = Convert(comp.Item);
                generators = Convert(comp.Iterators);
            }

            internal override AstExpression Revert() {
                AstExpression item = expr.Revert(elt);
                ComprehensionIterator[] iters = comprehension.RevertComprehensions(generators);
                return new ListComprehension(item, iters);
            }

            public expr elt { get; set; }

            public PythonList generators { get; set; }
        }

        [PythonType]
        public class Load : expr_context {
            public static readonly PythonTuple __match_args__ =
                PythonTuple.EMPTY;

            internal static readonly Load Instance = new Load();
        }

        [PythonType]
        public class Lt : cmpop {
            internal static readonly Lt Instance = new Lt();
            internal override PythonOperator Revert() => PythonOperator.LessThan;
        }

        [PythonType]
        public class LtE : cmpop {
            internal static readonly LtE Instance = new LtE();
            internal override PythonOperator Revert() => PythonOperator.LessThanOrEqual;
        }

        [PythonType]
        public class LShift : @operator {
            internal static readonly LShift Instance = new LShift();
            internal override PythonOperator Revert() => PythonOperator.LeftShift;
        }

        [PythonType]
        public class MatMult : @operator {
            internal static readonly MatMult Instance = new MatMult();
            internal override PythonOperator Revert() => PythonOperator.MatMult;
        }

        [PythonType]
        public class Mod : @operator {
            internal static readonly Mod Instance = new Mod();
            internal override PythonOperator Revert() => PythonOperator.Mod;
        }

        [PythonType]
        public class Module : mod {
            public Module() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(body), nameof(type_ignores) });
                type_ignores = new PythonList();
            }

            public Module(PythonList body)
                : this() {
                this.body = body;
            }

            internal Module(SuiteStatement suite)
                : this() {
                body = ConvertStatements(suite);
            }

            public PythonList body { get; set; }
            public PythonList type_ignores { get; set; }

            internal override PythonList GetStatements() {
                return body;
            }
        }

        [PythonType]
        public class Mult : @operator {
            internal static readonly Mult Instance = new Mult();
            internal override PythonOperator Revert() => PythonOperator.Multiply;
        }

        [PythonType]
        public class Name : expr {
            public static readonly PythonTuple __match_args__ =
                PythonTuple.MakeTuple(
                    nameof(id),
                    nameof(ctx));

            public Name() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(id), nameof(ctx) });
            }

            public Name(string id)
                : this(id, Load.Instance, null, null) { }

            public Name(string id, expr_context ctx, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.id = id;
                this.ctx = ctx;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            public Name(String id, expr_context ctx)
                : this(id, ctx, null, null) { }

            internal Name(Parameter para)
                : this(para.Name, Param.Instance) {
                GetSourceLocation(para);
            }

            internal Name(NameExpression expr, expr_context ctx)
                : this(expr.Name, ctx) {
                GetSourceLocation(expr);
            }

            internal override AstExpression Revert() {
                return new NameExpression(id);
            }

            public expr_context ctx { get; set; }

            public string id { get; set; }
        }

        [PythonType]
        public class NameConstant : expr {
            public NameConstant() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value) });
            }

            public NameConstant(object value, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            public NameConstant(object value)
                : this(value, null, null) { }

            internal override AstExpression Revert() {
                return new ConstantExpression(value);
            }

            public object value { get; set; }
        }

        [PythonType]
        public class NamedExpr : expr {
            public NamedExpr() {
                _fields = PythonTuple.MakeTuple([nameof(target), nameof(value)]);
            }

            public NamedExpr(expr target, expr value, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.target = target;
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            public NamedExpr(expr target, expr value)
                : this(target, value, null, null) { }

            internal NamedExpr(NamedExpression expr)
                : this() {
                target = Convert(expr.Target, Store.Instance);
                value = Convert(expr.Value);
            }

            internal override AstExpression Revert() {
                if (target is not Name)
                    throw PythonOps.TypeError("NamedExpr target must be a Name");
                return new NamedExpression((NameExpression)Revert(target), Revert(value));
            }

            public expr target { get; set; }

            public expr value { get; set; }
        }

        [PythonType]
        public class Nonlocal : stmt {
            public Nonlocal() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(names), });
            }

            public Nonlocal(PythonList names, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.names = names;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Nonlocal(NonlocalStatement stmt)
                : this() {
                names = PythonList.FromGenericCollection(stmt.Names);
            }

            internal override Statement Revert() {
                string[] newNames = new string[names.Count];
                for (int i = 0; i < names.Count; i++)
                    newNames[i] = (string)names[i];
                return new NonlocalStatement(newNames);
            }

            public PythonList names { get; set; }
        }

        [PythonType]
        public class Not : unaryop {
            internal static readonly Not Instance = new Not();
            internal override PythonOperator Revert() => PythonOperator.Not;
        }

        [PythonType]
        public class NotEq : cmpop {
            internal static readonly NotEq Instance = new NotEq();
            internal override PythonOperator Revert() => PythonOperator.NotEqual;
        }

        [PythonType]
        public class NotIn : cmpop {
            internal static readonly NotIn Instance = new NotIn();
            internal override PythonOperator Revert() => PythonOperator.NotIn;
        }

        [PythonType]
        public class Num : expr {
            public Num() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(n), });
            }

            internal Num(object n)
                : this(n, null, null) { }

            public Num(object n, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.n = n;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal override AstExpression Revert() {
                return new ConstantExpression(n);
            }

            public object n { get; set; }
        }

        [PythonType]
        public class Or : boolop {
            internal static readonly Or Instance = new Or();
        }

        [PythonType]
        public class Param : expr_context {
            internal static readonly Param Instance = new Param();
        }

        [PythonType]
        public class Pass : stmt {
            public static readonly PythonTuple __match_args__ =
                PythonTuple.EMPTY;

            internal static readonly Pass Instance = new Pass();

            internal Pass()
                : this(null, null) { }

            public Pass([Optional] int? lineno, [Optional] int? col_offset) {
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal override Statement Revert() {
                return new EmptyStatement();
            }
        }

        [PythonType]
        public class Pow : @operator {
            internal static readonly Pow Instance = new Pow();
            internal override PythonOperator Revert() => PythonOperator.Power;
        }

        [PythonType]
        public class Raise : stmt {
            public Raise() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(exc), nameof(cause) });
            }

            public Raise(expr exc, expr cause, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.exc = exc;
                this.cause = cause;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Raise(RaiseStatement stmt)
                : this() {
                if (stmt.Exception != null)
                    exc = Convert(stmt.Exception);
                if (stmt.Cause != null)
                    cause = Convert(stmt.Cause);
            }

            internal override Statement Revert() {
                return new RaiseStatement(expr.Revert(exc), expr.Revert(cause));
            }

            public expr exc { get; set; }

            public expr cause { get; set; }
        }


        [PythonType]
        public class Return : stmt {
            public Return() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), });
            }

            public Return([Optional] expr value, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            public Return(ReturnStatement statement)
                : this() {
                // statement.Expression is never null
                //or is it?
                if (statement.Expression == null)
                    value = null;
                else
                    value = Convert(statement.Expression);
            }

            internal override Statement Revert() {
                return new ReturnStatement(expr.Revert(value));
            }

            public expr value { get; set; }
        }

        [PythonType]
        public class RShift : @operator {
            internal static readonly RShift Instance = new RShift();
            internal override PythonOperator Revert() => PythonOperator.RightShift;
        }

        [PythonType]
        public class Set : expr {
            public Set() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(elts) });
            }

            public Set(PythonList elts, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.elts = elts;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Set(SetExpression setExpression)
                : this() {
                elts = new PythonList(setExpression.Items.Count);
                foreach (AstExpression item in setExpression.Items) {
                    elts.Add(Convert(item));
                }
            }

            internal override AstExpression Revert() {
                AstExpression[] e = new AstExpression[elts.Count];
                int i = 0;
                foreach (expr el in elts)
                    e[i++] = expr.Revert(el);
                return new SetExpression(e);
            }

            public PythonList elts { get; set; }
        }

        [PythonType]
        public class SetComp : expr {
            public SetComp() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(elt), nameof(generators) });
            }

            public SetComp(expr elt, PythonList generators, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.elt = elt;
                this.generators = generators;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal SetComp(SetComprehension comp)
                : this() {
                elt = Convert(comp.Item);
                generators = Convert(comp.Iterators);
            }

            internal override AstExpression Revert() {
                AstExpression item = expr.Revert(elt);
                ComprehensionIterator[] iters = comprehension.RevertComprehensions(generators);
                return new SetComprehension(item, iters);
            }

            public expr elt { get; set; }

            public PythonList generators { get; set; }
        }


        [PythonType]
        public class Slice : slice {
            public Slice() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(lower), nameof(upper), nameof(step) });
            }

            public Slice(expr lower, expr upper, expr step)
                // default interpretation of missing step is [:]
                // in order to get [::], please provide explicit Name('None',Load.Instance)
                : this() {
                this.lower = lower;
                this.upper = upper;
                this.step = step;
            }

            internal Slice(SliceExpression expr)
                : this() {
                if (expr.SliceStart != null)
                    lower = Convert(expr.SliceStart);
                if (expr.SliceStop != null)
                    upper = Convert(expr.SliceStop);
                if (expr.SliceStep != null)
                    step = Convert(expr.SliceStep);
            }

            public expr lower { get; set; }

            public expr upper { get; set; }

            public expr step { get; set; }
        }

        [PythonType]
        public class Starred : expr {
            public Starred() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), nameof(ctx) });
            }

            public Starred(expr value, expr_context ctx, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                this.ctx = ctx;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            public Starred(expr value, expr_context ctx)
                : this(value, ctx, null, null) { }

            internal Starred(StarredExpression expr, expr_context ctx)
                : this(Convert(expr.Value), ctx, null, null) { }

            internal override AstExpression Revert() => new StarredExpression(value?.Revert());

            public expr_context ctx { get; set; }

            public expr value { get; set; }
        }

        [PythonType]
        public class Store : expr_context {
            internal static readonly Store Instance = new Store();
        }

        [PythonType]
        public class Str : expr {
            public Str() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(s), });
            }

            internal Str(string s)
                : this(s, null, null) { }

            public Str(string s, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.s = s;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal override AstExpression Revert() {
                return new ConstantExpression(s);
            }

            public string s { get; set; }
        }

        [PythonType]
        public class Sub : @operator {
            internal static readonly Sub Instance = new Sub();
            internal override PythonOperator Revert() => PythonOperator.Subtract;
        }

        [PythonType]
        public class Subscript : expr {
            public Subscript() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), nameof(slice), nameof(ctx) });
            }

            public Subscript(expr value, expr slice)
                : this(value, slice, Load.Instance, null, null) { }

            public Subscript(expr value, expr slice, expr_context ctx,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                this.slice = slice;
                this.ctx = ctx;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Subscript(IndexExpression expr, expr_context ctx)
                : this() {
                value = Convert(expr.Target);
                this.ctx = ctx;
                slice = TrySliceConvert(expr.Index);
                if (slice == null)
                    slice = new Index(Convert(expr.Index));
            }

            internal override AstExpression Revert() {
                AstExpression index = null;
                if (slice is Index)
                    index = expr.Revert(((Index)slice).value);
                else if (slice is Slice) {
                    Slice concreteSlice = (Slice)slice;
                    AstExpression start = null;
                    if (concreteSlice.lower != null)
                        start = expr.Revert(concreteSlice.lower);
                    AstExpression stop = null;
                    if (concreteSlice.upper != null)
                        stop = expr.Revert(concreteSlice.upper);
                    AstExpression step = null;
                    if (concreteSlice.step != null)
                        step = expr.Revert(concreteSlice.step);
                    index = new SliceExpression(start, stop, step);
                } else if (slice is ExtSlice) {
                    index = new TupleExpression(true, ((ExtSlice)slice).Revert());
                } else {
                    Debug.Assert(false, "Unexpected type when converting Subscript: " + slice.GetType());
                }
                return new IndexExpression(expr.Revert(value), index);
            }

            public expr value { get; set; }

            public expr slice { get; set; }

            public expr_context ctx { get; set; }
        }

        /// <summary>
        /// Not an actual node. We don't create this, but it's here for compatibility.
        /// </summary>
        [PythonType]
        public class Suite : mod {
            public Suite() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(body), });
            }

            public Suite(PythonList body)
                : this() {
                this.body = body;
            }

            public PythonList body { get; set; }

            internal override PythonList GetStatements() {
                return body;
            }
        }

        [PythonType]
        public class Try : stmt {
            public Try() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(body), nameof(handlers), nameof(orelse), nameof(finalbody) });
            }

            public Try(PythonList body, PythonList handlers, PythonList orelse, PythonList finalbody,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.body = body;
                this.handlers = handlers;
                this.orelse = orelse;
                this.finalbody = finalbody;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Try(TryStatement stmt)
                : this() {
                body = ConvertStatements(stmt.Body);

                handlers = new PythonList(stmt.Handlers.Count);
                foreach (TryStatementHandler tryStmt in stmt.Handlers)
                    handlers.Add(Convert(tryStmt));

                orelse = ConvertStatements(stmt.Else, true);
                finalbody = ConvertStatements(stmt.Finally, true);
            }

            internal override Statement Revert() {
                TryStatementHandler[] tshs = new TryStatementHandler[handlers.Count];
                for (int i = 0; i < handlers.Count; i++) {
                    tshs[i] = ((ExceptHandler)handlers[i]).RevertHandler();
                }
                return new TryStatement(RevertStmts(body), tshs, orelse.Count == 0 ? null : RevertStmts(orelse), RevertStmts(finalbody));
            }

            public PythonList body { get; set; }

            public PythonList handlers { get; set; }

            public PythonList orelse { get; set; }

            public PythonList finalbody { get; set; }
        }

        [PythonType]
        public class Tuple : expr {
            public Tuple() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(elts), nameof(ctx) });
            }

            public Tuple(PythonList elts)
                : this(elts, Load.Instance, null, null) { }

            public Tuple(PythonList elts, expr_context ctx, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.elts = elts;
                this.ctx = ctx;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Tuple(TupleExpression list, expr_context ctx)
                : this() {
                elts = new PythonList(list.Items.Count);
                foreach (AstExpression expr in list.Items)
                    elts.Add(Convert(expr, ctx));

                this.ctx = ctx;
            }

            internal override AstExpression Revert() {
                AstExpression[] e = new AstExpression[elts.Count];
                int i = 0;
                foreach (expr el in elts)
                    e[i++] = expr.Revert(el);
                return new TupleExpression(false, e);
            }

            public PythonList elts { get; set; }

            public expr_context ctx { get; set; }
        }

        [PythonType]
        public class UnaryOp : expr {
            public UnaryOp() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(op), nameof(operand) });
            }

            internal UnaryOp(UnaryExpression expression)
                : this() {
                op = (unaryop)Convert(expression.Operator);
                operand = Convert(expression.Expression);
            }

            public UnaryOp(unaryop op, expr operand, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.op = op;
                this.operand = operand;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal override AstExpression Revert() {
                return new UnaryExpression(op.Revert(), expr.Revert(operand));
            }

            public unaryop op { get; set; }

            public expr operand { get; set; }

            internal expr TryTrimTrivialUnaryOp() {
                // in case of +constant or -constant returns underlying Num
                // representation, otherwise unmodified itself
                if (!(operand is Num num)) {
                    return this;
                }
                if (op is UAdd) {
                    return num;
                }
                if (!(op is USub)) {
                    return this;
                }
                // list of possible types can be found in:
                // class AST {
                //     internal static expr Convert(ConstantExpression expr);
                // }
                if (num.n is int) {
                    num.n = -(int)num.n;
                } else if (num.n is double) {
                    num.n = -(double)num.n;
                } else if (num.n is Int64) {
                    num.n = -(Int64)num.n;
                } else if (num.n is BigInteger) {
                    num.n = -(BigInteger)num.n;
                } else if (num.n is Complex) {
                    num.n = -(Complex)num.n;
                } else {
                    return this;
                }
                return num;
            }
        }

        [PythonType]
        public class UAdd : unaryop {
            internal static readonly UAdd Instance = new UAdd();
            internal override PythonOperator Revert() => PythonOperator.Pos;
        }

        [PythonType]
        public class USub : unaryop {
            internal static readonly USub Instance = new USub();
            internal override PythonOperator Revert() => PythonOperator.Negate;
        }

        [PythonType]
        public class While : stmt {
            public While() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(test), nameof(body), nameof(orelse) });
            }

            public While(expr test, PythonList body, [Optional] PythonList orelse,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.test = test;
                this.body = body;
                if (null == orelse)
                    this.orelse = new PythonList();
                else
                    this.orelse = orelse;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal While(WhileStatement stmt)
                : this() {
                test = Convert(stmt.Test);
                body = ConvertStatements(stmt.Body);
                orelse = ConvertStatements(stmt.ElseStatement, true);
            }

            internal override Statement Revert() {
                return new WhileStatement(expr.Revert(test), RevertStmts(body), RevertStmts(orelse));
            }

            public expr test { get; set; }

            public PythonList body { get; set; }

            public PythonList orelse { get; set; }
        }

        [PythonType]
        public class With : stmt {
            public With() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(items),
                    nameof(body),
                    nameof(type_comment)
                });
            }

            public With(PythonList items, PythonList body, [Optional] string type_comment,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.items = items;
                this.body = body;
                this.type_comment = type_comment;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal With(WithStatement with)
                : this() {
                items = new PythonList(1);
                items.AddNoLock(new withitem(Convert(with.ContextManager), with.Variable == null ? null : Convert(with.Variable, Store.Instance)));
                body = ConvertStatements(with.Body);
            }

            internal override Statement Revert() {
                Statement statement = RevertStmts(this.body);
                foreach (withitem item in items) {
                    statement = new WithStatement(expr.Revert(item.context_expr), expr.Revert(item.optional_vars), statement);
                }
                return statement;
            }

            public PythonList items { get; set; }

            public PythonList body { get; set; }

            public string type_comment { get; set; }
        }

        [PythonType]
        [Documentation("withitem(expr context_expr, expr? optional_vars)")]
        public class withitem : AST {
            public withitem() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(context_expr), nameof(optional_vars) });
            }

            public withitem(expr context_expr, expr optional_vars,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.context_expr = context_expr;
                this.optional_vars = optional_vars;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            public expr context_expr { get; set; }

            public expr optional_vars { get; set; }
        }

        // if yield is detected, the containing function has to be marked as generator
        internal static bool _containsYield = false;

        [PythonType]
        public class Yield : expr {
            public Yield() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), });
            }

            public Yield([Optional] expr value, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Yield(YieldExpression expr)
                : this() {
                value = expr.Expression == null ? null : Convert(expr.Expression);
            }

            internal override AstExpression Revert() {
                _containsYield = true;
                return new YieldExpression(value == null ? null : expr.Revert(value));
            }

            public expr value { get; set; }
        }

        [PythonType]
        public class YieldFrom : expr {
            public YieldFrom() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), });
            }

            public YieldFrom([Optional] expr value, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal YieldFrom(YieldFromExpression expr)
                : this() {
                // expr.Expression is never null
                value = Convert(expr.Expression);
            }

            internal override AstExpression Revert() {
                _containsYield = true;
                return new YieldFromExpression(expr.Revert(value));
            }

            public expr value { get; set; }
        }

        [PythonType]
        public class Await : expr {
            public Await() {
                _fields = PythonTuple.MakeTuple(new[] { nameof(value), });
            }

            public Await([Optional] expr value, [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.value = value;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal Await(AwaitExpression expr)
                : this() {
                value = Convert(expr.Expression);
            }

            internal override AstExpression Revert() {
                _containsYield = true;
                return new AwaitExpression(expr.Revert(value));
            }

            public expr value { get; set; }
        }

        [PythonType]
        public class AsyncFor : stmt {
            public AsyncFor() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(target),
                    nameof(iter),
                    nameof(body),
                    nameof(orelse),
                    nameof(type_comment)
                });
            }

            public AsyncFor(expr target, expr iter, PythonList body, [Optional] PythonList orelse, [Optional] string type_comment,
               [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.target = target;
                this.iter = iter;
                this.body = body;
                if (null == orelse)
                    this.orelse = new PythonList();
                else
                    this.orelse = orelse;
                this.type_comment = type_comment;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal AsyncFor(AsyncForStatement stmt)
                : this() {
                target = Convert(stmt.Left, Store.Instance);
                iter = Convert(stmt.List);
                body = ConvertStatements(stmt.Body);
                orelse = ConvertStatements(stmt.Else, true);
            }

            internal override Statement Revert() {
                return new AsyncForStatement(expr.Revert(target), expr.Revert(iter), RevertStmts(body), RevertStmts(orelse));
            }

            public expr target { get; set; }

            public expr iter { get; set; }

            public PythonList body { get; set; }

            public PythonList orelse { get; set; }

            public string type_comment { get; set; }
        }

        [PythonType]
        public class AsyncWith : stmt {
            public AsyncWith() {
                _fields = PythonTuple.MakeTuple(new[] {
                    nameof(items),
                    nameof(body),
                    nameof(type_comment)
                });
            }

            public AsyncWith(PythonList items, PythonList body, [Optional] string type_comment,
                [Optional] int? lineno, [Optional] int? col_offset)
                : this() {
                this.items = items;
                this.body = body;
                this.type_comment = type_comment;
                _lineno = lineno;
                _col_offset = col_offset;
            }

            internal AsyncWith(AsyncWithStatement with)
                : this() {
                items = new PythonList(1);
                items.AddNoLock(new withitem(Convert(with.ContextManager), with.Variable == null ? null : Convert(with.Variable, Store.Instance)));
                body = ConvertStatements(with.Body);
            }

            internal override Statement Revert() {
                Statement statement = RevertStmts(this.body);
                foreach (withitem item in items) {
                    statement = new AsyncWithStatement(expr.Revert(item.context_expr), expr.Revert(item.optional_vars), statement);
                }
                return statement;
            }

            public PythonList items { get; set; }

            public PythonList body { get; set; }

            public string type_comment { get; set; }
        }

        [PythonType]
        public abstract class pattern : AST {
            protected pattern() {
                _attributes = PythonTuple.MakeTuple(new[] {
                    nameof(lineno),
                    nameof(col_offset),
                    nameof(end_lineno),
                    nameof(end_col_offset)
                });
            }
        }

        [PythonType]
        public class MatchValue : pattern {
            public MatchValue() {
                _fields = PythonTuple.MakeTuple(nameof(value));
            }

            public MatchValue(expr value) : this() {
                this.value = value;
            }

            public expr value { get; set; }
        }

        [PythonType]
        public class MatchSingleton : pattern {
            public MatchSingleton() {
                _fields = PythonTuple.MakeTuple(nameof(value));
            }

            public MatchSingleton(object value) : this() {
                this.value = value;
            }

            public object value { get; set; }
        }

        [PythonType]
        public class MatchSequence : pattern {
            public MatchSequence() {
                _fields = PythonTuple.MakeTuple(nameof(patterns));
                patterns = new PythonList();
            }

            public MatchSequence(PythonList patterns) : this() {
                if (patterns is not null) this.patterns = patterns;
            }

            public PythonList patterns { get; set; }
        }

        [PythonType]
        public class MatchMapping : pattern {
            public MatchMapping() {
                _fields = PythonTuple.MakeTuple(
                    nameof(keys),
                    nameof(patterns),
                    nameof(rest));
                keys = new PythonList();
                patterns = new PythonList();
            }

            public MatchMapping(
                PythonList keys,
                PythonList patterns,
                [Optional] string rest) : this() {
                if (keys is not null) this.keys = keys;
                if (patterns is not null) this.patterns = patterns;
                this.rest = rest;
            }

            public PythonList keys { get; set; }
            public PythonList patterns { get; set; }
            public string rest { get; set; }
        }

        [PythonType]
        public class MatchClass : pattern {
            public MatchClass() {
                _fields = PythonTuple.MakeTuple(
                    nameof(cls),
                    nameof(patterns),
                    nameof(kwd_attrs),
                    nameof(kwd_patterns));
                patterns = new PythonList();
                kwd_attrs = new PythonList();
                kwd_patterns = new PythonList();
            }

            public MatchClass(
                expr cls,
                [Optional] PythonList patterns,
                [Optional] PythonList kwd_attrs,
                [Optional] PythonList kwd_patterns) : this() {
                this.cls = cls;
                if (patterns is not null) this.patterns = patterns;
                if (kwd_attrs is not null) this.kwd_attrs = kwd_attrs;
                if (kwd_patterns is not null) this.kwd_patterns = kwd_patterns;
            }

            public expr cls { get; set; }
            public PythonList patterns { get; set; }
            public PythonList kwd_attrs { get; set; }
            public PythonList kwd_patterns { get; set; }
        }

        [PythonType]
        public class MatchStar : pattern {
            public MatchStar() {
                _fields = PythonTuple.MakeTuple(nameof(name));
            }

            public MatchStar([Optional] string name) : this() {
                this.name = name;
            }

            public string name { get; set; }
        }

        [PythonType]
        public class MatchAs : pattern {
            public MatchAs() {
                _fields = PythonTuple.MakeTuple(nameof(pattern), nameof(name));
            }

            public MatchAs([Optional] pattern pattern, [Optional] string name)
                : this() {
                this.pattern = pattern;
                this.name = name;
            }

            public pattern pattern { get; set; }
            public string name { get; set; }
        }

        [PythonType]
        public class MatchOr : pattern {
            public MatchOr() {
                _fields = PythonTuple.MakeTuple(nameof(patterns));
                patterns = new PythonList();
            }

            public MatchOr(PythonList patterns) : this() {
                if (patterns is not null) this.patterns = patterns;
            }

            public PythonList patterns { get; set; }
        }

        [PythonType]
        public class match_case : AST {
            public match_case() {
                _fields = PythonTuple.MakeTuple(
                    nameof(pattern),
                    nameof(guard),
                    nameof(body));
                body = new PythonList();
            }

            public match_case(
                pattern pattern,
                [Optional] expr guard,
                [Optional] PythonList body) : this() {
                this.pattern = pattern;
                this.guard = guard;
                if (body is not null) this.body = body;
            }

            public pattern pattern { get; set; }
            public expr guard { get; set; }
            public PythonList body { get; set; }
        }

        [PythonType]
        public class Match : stmt {
            public Match() {
                _fields = PythonTuple.MakeTuple(nameof(subject), nameof(cases));
                cases = new PythonList();
            }

            public Match(expr subject, [Optional] PythonList cases) : this() {
                this.subject = subject;
                if (cases is not null) this.cases = cases;
            }

            public expr subject { get; set; }
            public PythonList cases { get; set; }
        }

        [PythonType]
        public class TypeAlias : stmt {
            public TypeAlias() {
                _fields = PythonTuple.MakeTuple(
                    nameof(name),
                    nameof(type_params),
                    nameof(value));
                type_params = new PythonList();
            }

            public TypeAlias(
                expr name,
                [Optional] PythonList type_params,
                expr value) : this() {
                this.name = name;
                if (type_params is not null) this.type_params = type_params;
                this.value = value;
            }

            public expr name { get; set; }
            public PythonList type_params { get; set; }
            public expr value { get; set; }
        }

        [PythonType]
        public abstract class type_ignore : AST {
        }

        [PythonType]
        public class TypeIgnore : type_ignore {
            public TypeIgnore() {
                _fields = PythonTuple.MakeTuple(nameof(lineno), nameof(tag));
            }

            public TypeIgnore(int lineno, string tag) : this() {
                this.lineno = lineno;
                this.tag = tag;
            }

            public string tag { get; set; }
        }

        [PythonType]
        public abstract class type_param : AST {
            protected type_param() {
                _attributes = PythonTuple.MakeTuple(new[] {
                    nameof(lineno),
                    nameof(col_offset),
                    nameof(end_lineno),
                    nameof(end_col_offset)
                });
            }
        }

        [PythonType]
        public class TypeVar : type_param {
            public TypeVar() {
                _fields = PythonTuple.MakeTuple(
                    nameof(name),
                    nameof(bound),
                    nameof(default_value));
            }

            public TypeVar(
                string name,
                [Optional] expr bound,
                [Optional] expr default_value) : this() {
                this.name = name;
                this.bound = bound;
                this.default_value = default_value;
            }

            public string name { get; set; }
            public expr bound { get; set; }
            public expr default_value { get; set; }
        }

        [PythonType]
        public class ParamSpec : type_param {
            public ParamSpec() {
                _fields = PythonTuple.MakeTuple(
                    nameof(name),
                    nameof(default_value));
            }

            public ParamSpec(
                string name,
                [Optional] expr default_value) : this() {
                this.name = name;
                this.default_value = default_value;
            }

            public string name { get; set; }
            public expr default_value { get; set; }
        }

        [PythonType]
        public class TypeVarTuple : type_param {
            public TypeVarTuple() {
                _fields = PythonTuple.MakeTuple(
                    nameof(name),
                    nameof(default_value));
            }

            public TypeVarTuple(
                string name,
                [Optional] expr default_value) : this() {
                this.name = name;
                this.default_value = default_value;
            }

            public string name { get; set; }
            public expr default_value { get; set; }
        }

        [PythonType]
        public class TryStar : stmt {
            public TryStar() {
                _fields = PythonTuple.MakeTuple(
                    nameof(body),
                    nameof(handlers),
                    nameof(orelse),
                    nameof(finalbody));
            }

            public TryStar(
                PythonList body,
                PythonList handlers,
                PythonList orelse,
                PythonList finalbody) : this() {
                this.body = body;
                this.handlers = handlers;
                this.orelse = orelse;
                this.finalbody = finalbody;
            }

            public PythonList body { get; set; }
            public PythonList handlers { get; set; }
            public PythonList orelse { get; set; }
            public PythonList finalbody { get; set; }
        }

        [PythonType]
        public class FunctionType : mod {
            public FunctionType() {
                _fields = PythonTuple.MakeTuple(
                    nameof(argtypes),
                    nameof(returns));
                argtypes = new PythonList();
            }

            public FunctionType(PythonList argtypes, expr returns) : this() {
                if (argtypes is not null) this.argtypes = argtypes;
                this.returns = returns;
            }

            public PythonList argtypes { get; set; }
            public expr returns { get; set; }
        }
    }
}
