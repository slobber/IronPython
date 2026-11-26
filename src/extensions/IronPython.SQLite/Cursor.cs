// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.
//
// Copyright (c) Jeff Hardy 2010-2012.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Community.CsharpSqlite;
using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

using sqlite3_stmt = Community.CsharpSqlite.Sqlite3.Vdbe;

namespace IronPython.SQLite
{
    public static partial class PythonSQLite
    {
        public static bool _is_cursor(object value) => value is Cursor;

        [PythonType]
        public class Cursor : IEnumerable
        {
            public const string __doc__ = "SQLite database cursor class.";

            private Statement statement;
            private object next_row;
            private int[] next_row_types;
            private int busy_depth;
            private bool executing;
            private Exception pending_recursive_error;
            private PythonList cached_rows;
            private int cached_index;
            private bool resultsDone;
            private int last_step_rc;

            private List<object> row_cast_map = new List<object>();

            public PythonTuple description { get; private set; }

            public int rowcount { get; private set; }

            public int? rownumber
            {
                get { return null; } 
            }

            public BigInteger? lastrowid { get; private set; }

            public object row_factory { get; set; }

            private int _arraysize;

            public object arraysize
            {
                get => _arraysize;
                set => _arraysize = GetSize(value);
            }

            public Connection connection { get; private set; }

            public object callproc(string procname)
            {
                throw PythonSQLite.MakeNotSupportedError();
            }

            private CodeContext context;

            public Cursor(CodeContext context, Connection connection)
            {
                initialize(context, connection);
            }

            public void reinitialize(Connection connection)
            {
                CheckRecursiveUse();
                initialize(DefaultContext.Default, connection);
            }

            private void initialize(CodeContext context, Connection connection)
            {
                this.context = context;
                this.connection = connection;

                this._arraysize = 1;
                this.rowcount = -1;

                if(this.connection != null)
                    this.connection.checkThread();
            }

            ~Cursor()
            {
                if(this.statement != null)
                    this.statement.Reset();
            }

            [Documentation("Closes the cursor.")]
            public void close()
            {
                connection.checkThread(); connection.checkConnection();
                if (pending_recursive_error is not null)
                {
                    Exception error = pending_recursive_error;
                    pending_recursive_error = null;
                    throw error;
                }
                CheckRecursiveUse();

                if(this.statement != null)
                {
                    this.statement.Reset();
                }
            }

            [Documentation("Executes a SQL statement.")]
            public object execute(CodeContext context, object operation, object args=null)
            {
                return queryExecute(context, false, operation, args);
            }

            [Documentation("Repeatedly executes a SQL statement.")]
            public object executemany(CodeContext context, object operation, object args)
            {
                return queryExecute(context, true, operation, args);
            }

            public void __delattr__(string name)
            {
                if (name == "arraysize")
                {
                    throw PythonOps.AttributeError(
                        "cannot be deleted");
                }
                throw PythonOps.AttributeError(
                    "'Cursor' object has no attribute '{0}'",
                    name);
            }

            private object queryExecute(CodeContext context, bool multiple, object operation_obj, object args)
            {
                if(!(operation_obj is string))
                    throw PythonOps.TypeError(
                        "operation parameter must be str");

                string operation = (string)operation_obj;

                CheckRecursiveUse();
                if (pending_recursive_error is not null)
                {
                    Exception error = pending_recursive_error;
                    pending_recursive_error = null;
                    throw error;
                }

                for (int i = 0; i < operation.Length; i++)
                {
                    if (char.IsSurrogate(operation[i]))
                    {
                        throw PythonOps.UnicodeEncodeError(
                            "utf-8",
                            operation,
                            i,
                            i + 1,
                            "surrogates not allowed");
                    }
                    if (operation[i] == '\0')
                    {
                        throw MakeProgrammingError(
                            "the query contains a null character");
                    }
                }

                if(string.IsNullOrEmpty(operation))
                    return null;

                int rc;

                connection.checkThread(); connection.checkConnection();

                this.next_row = null;
                this.cached_rows = null;
                this.cached_index = 0;

                IEnumerator parameters_iter = null;
                if(multiple)
                {
                    if(args != null)
                        parameters_iter = PythonOps.CreatePythonEnumerator(context, args);
                }
                else
                {
                    object[] parameters_list = { args };
                    if(parameters_list[0] == null)
                        parameters_list[0] = new PythonTuple();

                    parameters_iter = parameters_list.GetEnumerator();
                }

                if(this.statement != null)
                    rc = this.statement.Reset();

                this.description = null;
                this.rowcount = -1;

                // TODO: use stmt cache instead ?
                this.statement = this.connection.CreateStatement(operation);

                if (HasSqlContent(this.statement.Tail))
                {
                    throw MakeProgrammingError(
                        "You can only execute one statement at a time");
                }

                if(this.statement.in_use)
                    this.statement = new Statement(connection, operation);

                this.statement.Reset();
                this.statement.MarkDirty();
                executing = true;

                if(!string.IsNullOrEmpty(connection.begin_statement))
                {
                    switch(statement.StatementType)
                    {
                        case StatementType.Update:
                        case StatementType.Insert:
                        case StatementType.Delete:
                        case StatementType.Replace:
                            if(!connection.in_transaction)
                                connection.begin();
                            
                            break;

                        case StatementType.Select:
                            if(multiple)
                                throw MakeProgrammingError("You cannot execute SELECT statements in executemany().");
                            break;

                        default:
                            break;
                    }
                }

                while(true)
                {
                    bool hasNext;
                    ++busy_depth;
                    try
                    {
                        hasNext = parameters_iter.MoveNext();
                    }
                    finally
                    {
                        --busy_depth;
                    }
                    if(!hasNext)
                        break;

                    object parameters = parameters_iter.Current;

                    this.statement.MarkDirty();
                    this.statement.BindParameters(context, parameters);

                    while(true)
                    {
                        rc = this.statement.RawStep();
                        if(rc == Sqlite3.SQLITE_DONE || rc == Sqlite3.SQLITE_ROW)
                            break;

                        rc = this.statement.Reset();
                        if(rc == Sqlite3.SQLITE_SCHEMA)
                        {
                            rc = this.statement.Recompile(context, parameters);
                            if(rc == Sqlite3.SQLITE_OK)
                            {
                                continue;
                            }
                            else
                            {
                                this.statement.Reset();
                                throw GetSqliteError(this.connection.db, null);
                            }
                        }
                        else
                        {
                            this.statement.Reset();
                            throw GetSqliteError(this.connection.db, null);
                        }
                    }

                    if(!buildRowCastMap())
                        throw MakeOperationalError("Error while building row_cast_map");

                    if(rc == Sqlite3.SQLITE_ROW || (rc == Sqlite3.SQLITE_DONE && this.statement.StatementType == StatementType.Select))
                    {
                        if(this.description == null)
                        {
                            int numcols = Sqlite3.sqlite3_column_count(this.statement.st);
                            object[] new_description = new object[numcols];

                            for(int i = 0; i < numcols; ++i)
                            {
                                string name = buildColumnName(Sqlite3.sqlite3_column_name(this.statement.st, i));

                                object descriptor = new object[] {
                                    name,
                                    (object)null,
                                    (int?)null,
                                    (int?)null,
                                    (int?)null,
                                    (int?)null,
                                    (bool?)null
                                };

                                new_description[i] = new PythonTuple(DefaultContext.Default, descriptor);
                            }

                            this.description = new PythonTuple(DefaultContext.Default, new_description);
                        }
                    }

                    if(rc == Sqlite3.SQLITE_ROW)
                    {
                        if(multiple)
                            throw MakeProgrammingError("executemany() can only execute DML statements.");

                        this.next_row = fetchOneRow(context, false);
                    }
                    else if(rc == Sqlite3.SQLITE_DONE && !multiple)
                    {
                        this.statement.Reset();
                    }

                    switch(this.statement.StatementType)
                    {
                        case StatementType.Update:
                        case StatementType.Delete:
                        case StatementType.Insert:
                        case StatementType.Replace:
                            if(this.rowcount == -1)
                                this.rowcount = 0;

                            this.rowcount += Sqlite3.sqlite3_changes(this.connection.db);
                            break;
                    }

                    if(!multiple
                        && (this.statement.StatementType == StatementType.Insert
                            || this.statement.StatementType == StatementType.Replace))
                    {
                        this.lastrowid = Sqlite3.sqlite3_last_insert_rowid(this.connection.db);
                    }
                    else
                    {
                        this.lastrowid = null;
                    }

                    if(multiple)
                        rc = this.statement.Reset();
                }

                executing = false;
                return this;
            }

            private string buildColumnName(string colname)
            {
                if ((connection.detect_types & PARSE_COLNAMES) != 0)
                {
                    int n = colname.IndexOf('[');
                    return n < 0 ? colname : colname.Substring(0, n).Trim();
                }
                return colname;
            }

            private object fetchOneRow(
                CodeContext context,
                bool convertConverters)
            {
                int numcols = Sqlite3.sqlite3_data_count(this.statement.st);
                object[] row = new object[numcols];
                next_row_types = new int[numcols];
                object converter = null;

                for(int i = 0; i < numcols; ++i)
                {
                    object converted = null;

                    if(this.connection.detect_types != 0)
                    {
                        converter = row_cast_map[i];
                    }
                    else
                    {
                        converter = null;
                    }

                    if(converter != null && !convertConverters)
                    {
                        byte[] raw = Sqlite3.sqlite3_column_blob(
                            this.statement.st,
                            i);
                        next_row_types[i] = Sqlite3.SQLITE_BLOB;
                        converted = raw is null || raw.Length == 0
                            ? null
                            : new Bytes(raw);
                    }
                    else if(converter != null)
                    {
                        byte[] val = Sqlite3.sqlite3_column_blob(this.statement.st, i);
                        if(val == null || val.Length == 0)
                        {
                            converted = null;
                        }
                        else
                        {
                            converted = CallConverter(
                                context,
                                converter,
                                new Bytes(val));
                        }
                    }
                    else
                    {
                        int coltype = Sqlite3.sqlite3_column_type(this.statement.st, i);
                        next_row_types[i] = coltype;

                        switch(coltype)
                        {
                            case Sqlite3.SQLITE_NULL:
                                converted = null;
                                break;

                            case Sqlite3.SQLITE_INTEGER:
                                long l = Sqlite3.sqlite3_column_int64(this.statement.st, i);
                                if(l < int.MinValue || l > int.MaxValue)
                                    converted = (BigInteger)l;
                                else
                                    converted = (int)l;

                                break;

                            case Sqlite3.SQLITE_FLOAT:
                                converted = Sqlite3.sqlite3_column_double(this.statement.st, i);
                                break;

                            case Sqlite3.SQLITE_TEXT:
                                object factory = this.connection.text_factory;
                                bool useDefaultFactory = factory is null
                                    || ReferenceEquals(factory, typeof(string))
                                    || (factory is PythonType textType
                                        && PythonType.Get__name__(textType) == "str");
                                string text;
                                if (useDefaultFactory)
                                {
                                    text = Sqlite3.sqlite3_column_text(
                                        this.statement.st,
                                        i);
                                    if (text is not null
                                        && text.Contains('\ufffd'))
                                    {
                                        string column =
                                            Sqlite3.sqlite3_column_name(
                                                this.statement.st,
                                                i);
                                        string display = text.Replace(
                                            '\ufffd',
                                            '?');
                                        throw MakeOperationalError(
                                            "Could not decode to UTF-8 column "
                                            + "'" + column + "' with text '"
                                            + display + "'");
                                    }
                                    converted = text;
                                }
                                else
                                {
                                    text = Sqlite3.sqlite3_column_text(
                                        this.statement.st,
                                        i);
                                    var raw = new Bytes(
                                        System.Text.Encoding.UTF8.GetBytes(
                                            text));
                                    converted = PythonCalls.Call(
                                        context,
                                        factory,
                                        raw);
                                }
                                break;

                            case Sqlite3.SQLITE_BLOB:
                            default:
                                converted = new Bytes(Sqlite3.sqlite3_column_blob(this.statement.st, i) ?? Array.Empty<byte>()); // TODO: avoid creating a copy
                                break;
                        }
                    }

                    row[i] = converted;
                }

                return new PythonTuple(DefaultContext.Default, row);
            }

            private object ConvertRow(
                CodeContext context,
                object rowValue)
            {
                if (rowValue is not PythonTuple tuple
                    || row_cast_map.Count == 0)
                {
                    return rowValue;
                }

                object[] values = tuple.ToArray();
                for (int i = 0; i < values.Length; ++i)
                {
                    object converter = row_cast_map[i];
                    if (converter is not null && values[i] is not null)
                    {
                        values[i] = CallConverter(
                            context,
                            converter,
                            values[i]);
                    }
                }
                return new PythonTuple(
                    DefaultContext.Default,
                    values);
            }

            private object CallConverter(
                CodeContext context,
                object converter,
                object value)
            {
                ++busy_depth;
                try
                {
                    return PythonCalls.Call(context, converter, value);
                }
                catch (Exception ex) when (
                    executing
                    && ex.Message.Contains(
                        "Recursive use of cursors not allowed"))
                {
                    pending_recursive_error = ex;
                    return null;
                }
                finally
                {
                    --busy_depth;
                }
            }

            private void CheckRecursiveUse()
            {
                if (busy_depth != 0)
                {
                    throw MakeProgrammingError(
                        "Recursive use of cursors not allowed");
                }
            }

            public Cursor executescript(string operation)
            {
                connection.checkThread(); connection.checkConnection();

                if (this.connection.AutocommitMode
                    == PythonSQLite.Connection.LEGACY_TRANSACTION_CONTROL)
                {
                    this.connection.commit();
                }

                sqlite3_stmt statement = null;
                string script = operation;
                while (!string.IsNullOrWhiteSpace(script))
                {
                    string current = script;

                    int rc = Sqlite3.sqlite3_prepare(this.connection.db,
                                                     current,
                                                     -1,
                                                     ref statement,
                                                     ref script);

                    if(rc != Sqlite3.SQLITE_OK)
                        throw GetSqliteError(this.connection.db, null);

                    /* execute statement, and ignore results of SELECT statements */
                    rc = Sqlite3.SQLITE_ROW;
                    while(rc == Sqlite3.SQLITE_ROW)
                        rc = Sqlite3.sqlite3_step(statement);

                    if(rc != Sqlite3.SQLITE_DONE)
                    {
                        Sqlite3.sqlite3_finalize(statement);
                        throw GetSqliteError(this.connection.db, null);
                    }

                    rc = Sqlite3.sqlite3_finalize(statement);
                    if(rc != Sqlite3.SQLITE_OK)
                        throw GetSqliteError(this.connection.db, null);
                }

                return this;
            }

            public object __iter__()
            {
                return this;
            }

            public object __next__(CodeContext context)
            {
                object next_row_tuple, next_row;

                connection.checkThread(); connection.checkConnection();
                if (pending_recursive_error is not null)
                {
                    Exception error = pending_recursive_error;
                    pending_recursive_error = null;
                    throw error;
                }
                CheckRecursiveUse();

                if (cached_rows is not null)
                {
                    if (cached_index >= cached_rows.Count)
                    {
                        throw new StopIterationException();
                    }
                    next_row_tuple = cached_rows[cached_index++];
                    return row_factory is null
                        ? next_row_tuple
                        : PythonCalls.Call(
                            context,
                            row_factory,
                            this,
                            next_row_tuple);
                }

                if(this.next_row == null)
                {
                    if(this.statement != null)
                    {
                        this.statement.Reset();
                        this.statement = null;
                    }

                    throw new StopIterationException();
                }

                next_row_tuple = this.next_row;
                this.next_row = null;
                next_row_tuple = ConvertRow(context, next_row_tuple);

                object currentTextFactory = connection.text_factory;
                bool defaultTextFactory =
                    currentTextFactory is null
                    || ReferenceEquals(currentTextFactory, typeof(string))
                    || (currentTextFactory is PythonType textType
                        && PythonType.Get__name__(textType) == "str");
                if (defaultTextFactory
                    && next_row_tuple is PythonTuple tuple
                    && next_row_types is not null)
                {
                    object[] values = tuple.ToArray();
                    for (int i = 0; i < values.Length; ++i)
                    {
                        if (next_row_types[i] == Sqlite3.SQLITE_TEXT
                            && values[i] is IBufferProtocol buffer)
                        {
                            using IPythonBuffer pythonBuffer =
                                buffer.GetBuffer(BufferFlags.FullRO);
                            values[i] = System.Text.Encoding.UTF8.GetString(
                                pythonBuffer.ToArray());
                        }
                    }
                    next_row_tuple = new PythonTuple(
                        DefaultContext.Default,
                        values);
                }

                if(this.row_factory != null)
                {
                    next_row = PythonCalls.Call(context, this.row_factory, this, next_row_tuple);
                }
                else
                {
                    next_row = next_row_tuple;
                }

                if(this.statement != null)
                {
                    int rc = this.statement.RawStep();
                    if(rc != Sqlite3.SQLITE_DONE && rc != Sqlite3.SQLITE_ROW)
                    {
                        this.statement.Reset();
                        throw GetSqliteError(this.connection.db, this.statement.st);
                    }

                    if(rc == Sqlite3.SQLITE_ROW)
                        this.next_row = fetchOneRow(context, false);
                }

                return next_row;
            }

            internal void MaterializeRemaining()
            {
                if (statement is null || next_row is null)
                {
                    return;
                }

                cached_rows = new PythonList();
                while (next_row is not null)
                {
                    cached_rows.Add(ConvertRow(
                        DefaultContext.Default,
                        next_row));
                    next_row = null;
                    int rc = statement.RawStep();
                    if (rc == Sqlite3.SQLITE_ROW)
                    {
                        next_row = fetchOneRow(DefaultContext.Default, false);
                    }
                    else
                    {
                        break;
                    }
                }
                cached_index = 0;
                statement.Reset();
                statement = null;
            }

            [Documentation("Fetches one row from the resultset.")]
            public object fetchone(CodeContext context)
            {
                try
                {
                    return this.__next__(context);
                }
                catch(StopIterationException)
                {
                    return null;
                }
            }

            public object fetchmany(CodeContext context)
            {
                return fetchmany(context, _arraysize);
            }

            [Documentation("Fetches several rows from the resultset.")]
            public object fetchmany(CodeContext context, object size)
            {
                int count = GetSize(size);
                PythonList result = new PythonList();
                for (int i = 0; i < count; ++i)
                {
                    object item = fetchone(context);
                    if (item is null)
                    {
                        break;
                    }
                    result.Add(item);
                }

                return result;
            }

            private static int GetSize(object value)
            {
                object index = PythonOps.Index(value);
                if (index is int intValue)
                {
                    if (intValue < 0)
                    {
                        throw PythonOps.ValueError(
                            "size must be non-negative");
                    }
                    return intValue;
                }
                var bigValue = (BigInteger)index;
                if (bigValue < 0)
                {
                    throw PythonOps.ValueError(
                        "size must be non-negative");
                }
                if (bigValue > int.MaxValue)
                {
                    throw PythonOps.OverflowError(
                        "size does not fit into a C int");
                }
                return (int)bigValue;
            }

            private static bool HasSqlContent(string tail)
            {
                if (string.IsNullOrEmpty(tail))
                {
                    return false;
                }

                for (int i = 0; i < tail.Length;)
                {
                    if (char.IsWhiteSpace(tail[i]) || tail[i] == ';')
                    {
                        i++;
                        continue;
                    }
                    if (tail[i] == '-' && i + 1 < tail.Length
                        && tail[i + 1] == '-')
                    {
                        i += 2;
                        while (i < tail.Length
                            && tail[i] != '\n'
                            && tail[i] != '\r')
                        {
                            i++;
                        }
                        continue;
                    }
                    if (tail[i] == '/' && i + 1 < tail.Length
                        && tail[i + 1] == '*')
                    {
                        int end = tail.IndexOf("*/", i + 2,
                            StringComparison.Ordinal);
                        if (end < 0)
                        {
                            return false;
                        }
                        i = end + 2;
                        continue;
                    }
                    return true;
                }
                return false;
            }

            [Documentation("Fetches all rows from the resultset.")]
            public object fetchall(CodeContext context)
            {
                PythonList result = new PythonList();
                object item = fetchone(context);
                while(item != null)
                {
                    result.Add(item);
                    item = fetchone(context);
                }

                return result;
            }

            public object nextset()
            {
                return null;
            }

            [Documentation("Required by DB-API. Does nothing in IronPython.Sqlite3.")]
            public void setinputsizes(object sizes) { }

            [Documentation("Required by DB-API. Does nothing in IronPython.Sqlite3.")]
            public void setoutputsize(params object[] args) { }

            private bool buildRowCastMap()
            {
                if(this.connection.detect_types == 0)
                    return true;

                row_cast_map = new List<object>();

                object converter = null;
                for(int i = 0; i < Sqlite3.sqlite3_column_count(this.statement.st); ++i)
                {
                    converter = null;

                    if((this.connection.detect_types & PARSE_COLNAMES) != 0)
                    {
                        string colname = Sqlite3.sqlite3_column_name(this.statement.st, i);
                        if(colname != null)
                        {
                            Regex matchColname = new Regex(@"\[(\w+)\]");
                            Match match = matchColname.Match(colname);
                            if(match.Success)
                            {
                                string key = match.Groups[1].ToString();
                                converter = getConverter(key);
                            }
                        }
                    }

                    if((converter == null) && ((this.connection.detect_types & PARSE_DECLTYPES) != 0))
                    {
                        string decltype = Sqlite3.sqlite3_column_decltype(this.statement.st, i);
                        if(decltype != null)
                        {
                            Regex matchDecltype = new Regex(@"\b(\w+)\b");
                            Match match = matchDecltype.Match(decltype);
                            if(match.Success)
                            {
                                string py_decltype = match.Groups[1].ToString();
                                converter = getConverter(py_decltype);
                            }
                        }
                    }

                    row_cast_map.Add(converter);
                }

                return true;
            }

            private object getConverter(string key)
            {
                object converter;
                return converters.TryGetValue(key.ToUpperInvariant(), out converter) ? converter : null;
            }

            #region IEnumerable Members

            public IEnumerator GetEnumerator()
            {
                PythonList results = new PythonList();
                try
                {
                    while(true)
                        results.append(this.__next__(this.context));
                }
                catch(StopIterationException) { }

                return results.GetEnumerator();
            }

            #endregion
        }
    }
}
