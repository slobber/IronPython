// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.
//
// Copyright (c) Jeff Hardy 2010-2012.
//

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Community.CsharpSqlite;
using IronPython.Modules;
using IronPython.Runtime;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

[assembly: PythonModule("_sqlite3", typeof(IronPython.SQLite.PythonSQLite))]

namespace IronPython.SQLite
{
    public static partial class PythonSQLite
    {
        public static readonly string version = typeof(PythonSQLite).Assembly.GetName().Version.ToString(3);
        public static readonly string sqlite_version = Sqlite3.sqlite3_version.Replace("(C#)", "");
        public static bool callback_tracebacks;

        public static void set_callback_tracebacks(bool enable)
        {
            callback_tracebacks = enable;
        }

        public const int LEGACY_TRANSACTION_CONTROL = Connection.LEGACY_TRANSACTION_CONTROL;

        public const int SQLITE_DBCONFIG_ENABLE_FKEY =
            Sqlite3.SQLITE_DBCONFIG_ENABLE_FKEY;
        public const int SQLITE_DBCONFIG_ENABLE_TRIGGER =
            Sqlite3.SQLITE_DBCONFIG_ENABLE_TRIGGER;

        public const int SQLITE_LIMIT_LENGTH = 0;
        public const int SQLITE_LIMIT_SQL_LENGTH = 1;
        public const int SQLITE_LIMIT_COLUMN = 2;
        public const int SQLITE_LIMIT_EXPR_DEPTH = 3;
        public const int SQLITE_LIMIT_COMPOUND_SELECT = 4;
        public const int SQLITE_LIMIT_VDBE_OP = 5;
        public const int SQLITE_LIMIT_FUNCTION_ARG = 6;
        public const int SQLITE_LIMIT_ATTACHED = 7;
        public const int SQLITE_LIMIT_LIKE_PATTERN_LENGTH = 8;
        public const int SQLITE_LIMIT_VARIABLE_NUMBER = 9;
        public const int SQLITE_LIMIT_TRIGGER_DEPTH = 10;
        public const int SQLITE_LIMIT_WORKER_THREADS = 11;

        public static PythonDictionary converters = new PythonDictionary();
        public static PythonDictionary adapters = new PythonDictionary();
        public static readonly Type OptimizedUnicode = typeof(string);

        internal static Encoding Latin1 = Encoding.GetEncoding("iso-8859-1");

        internal static void ReportUnraisable(
            CodeContext context,
            Exception exception,
            object obj = null,
            string errMsg = null)
        {
            if (callback_tracebacks)
            {
                SysModule.ReportUnraisable(
                    context,
                    exception,
                    obj,
                    errMsg);
            }
        }

        [SpecialName]
        public static void PerformModuleReload(PythonContext context, PythonDictionary dict)
        {
            InitModuleExceptions(context, dict);
        }

        public static object connect(CodeContext context,
            string database,
            double timeout=0.0,
            int detect_types=0,
            string isolation_level=null,
            bool check_same_thread=true,
            object factory=null,
            int cached_statements=0)
        {
            if(factory == null)
                return new Connection(database, timeout, detect_types, isolation_level, check_same_thread, factory, cached_statements);
            else
                return PythonCalls.Call(context, factory, database, timeout, detect_types, isolation_level, check_same_thread, factory, cached_statements);
        }

        public static bool complete_statement([NotNone] string sql) {
            bool singleQuote = false;
            bool doubleQuote = false;
            bool bracket = false;
            bool backtick = false;
            bool lineComment = false;
            bool blockComment = false;
            bool terminated = false;

            for (int i = 0; i < sql.Length; i++) {
                char current = sql[i];
                char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

                if (lineComment) {
                    if (current is '\n' or '\r') {
                        lineComment = false;
                    }
                    continue;
                }
                if (blockComment) {
                    if (current == '*' && next == '/') {
                        blockComment = false;
                        i++;
                    }
                    continue;
                }
                if (singleQuote) {
                    if (current == '\'' && next == '\'') {
                        i++;
                    } else if (current == '\'') {
                        singleQuote = false;
                    }
                    continue;
                }
                if (doubleQuote) {
                    if (current == '"' && next == '"') {
                        i++;
                    } else if (current == '"') {
                        doubleQuote = false;
                    }
                    continue;
                }
                if (bracket) {
                    bracket = current != ']';
                    continue;
                }
                if (backtick) {
                    backtick = current != '`';
                    continue;
                }

                if (current == '-' && next == '-') {
                    lineComment = true;
                    i++;
                } else if (current == '/' && next == '*') {
                    blockComment = true;
                    i++;
                } else if (current == '\'') {
                    singleQuote = true;
                } else if (current == '"') {
                    doubleQuote = true;
                } else if (current == '[') {
                    bracket = true;
                } else if (current == '`') {
                    backtick = true;
                } else if (current == ';') {
                    terminated = true;
                } else if (!char.IsWhiteSpace(current)) {
                    terminated = false;
                }
            }

            return terminated
                && !singleQuote
                && !doubleQuote
                && !bracket
                && !backtick
                && !blockComment;
        }

        public static object adapt(CodeContext context, object value)
            => adapt(
                context,
                value,
                DynamicHelpers.GetPythonTypeFromType(typeof(PrepareProtocol)));

        public static object adapt(
            CodeContext context,
            object value,
            object proto) {
            object adapted = Adapt(context, value, proto);
            if (adapted != null) {
                return adapted;
            }
            throw MakeProgrammingError(
                $"can't adapt type '{value.GetType().Name}'");
        }

        public static object adapt(
            CodeContext context,
            object value,
            object proto,
            object alternative) {
            return Adapt(context, value, proto) ?? alternative;
        }

        private static object Adapt(
            CodeContext context,
            object value,
            object proto) {
            if (proto is null) {
                return null;
            }

            PythonType valueType = GetAdapterType(context, value);
            object key = new PythonTuple(
                DefaultContext.Default,
                new[] { valueType, proto });
            if (adapters.TryGetValue(key, out object adapter)) {
                return PythonCalls.Call(context, adapter, value);
            }
            if (TryGetAdapter(valueType, out adapter)) {
                return PythonCalls.Call(context, adapter, value);
            }
            if (adapters.TryGetValue(valueType, out adapter)) {
                return PythonCalls.Call(context, adapter, value);
            }

            if (PythonOps.TryGetBoundAttr(
                    context,
                    value,
                    "__conform__",
                    out object conform)) {
                return PythonCalls.Call(context, conform, proto);
            }

            if (PythonOps.TryGetBoundAttr(
                    context,
                    proto,
                    "__adapt__",
                    out object adaptMethod)) {
                return PythonCalls.Call(context, adaptMethod, value);
            }

            return null;
        }

        internal static PythonType GetAdapterType(
            CodeContext context,
            object value) {
            string builtinName = value switch {
                bool => "bool",
                int => "int",
                System.Numerics.BigInteger => "int",
                double => "float",
                string => "str",
                Bytes => "bytes",
                _ => null
            };
            if (builtinName is not null) {
                object builtins = Importer.ImportModule(
                    context,
                    null,
                    "builtins",
                    false,
                    0);
                if (PythonOps.TryGetBoundAttr(
                        context,
                        builtins,
                        builtinName,
                        out object type)
                    && type is PythonType pythonType) {
                    return pythonType;
                }
            }
            return DynamicHelpers.GetPythonType(value);
        }

        [Documentation(@"register_adapter(type, callable)

Registers an adapter with pysqlite's adapter registry. Non-standard.")]
        public static void register_adapter(CodeContext context, PythonType type, object adapter)
        {
            object proto = DynamicHelpers.GetPythonTypeFromType(typeof(PrepareProtocol));
            object key = new PythonTuple(DefaultContext.Default, new object[] { type, proto });
            adapters[key] = adapter;
        }

        internal static bool TryGetAdapter(
            PythonType type,
            out object adapter)
        {
            foreach (var pair in adapters)
            {
                if (pair.Key is PythonType adapterType
                    && (ReferenceEquals(adapterType, type)
                        || adapterType.Equals(type)))
                {
                    adapter = pair.Value;
                    return true;
                }
            }
            adapter = null;
            return false;
        }

        [Documentation(@"register_converter(typename, callable)

Registers a converter with pysqlite. Non-standard.")]
        public static void register_converter(CodeContext context, string type, object converter)
        {
            converters[type.ToUpperInvariant()] = converter;
        }

        public class PrepareProtocol { }
    }
}
