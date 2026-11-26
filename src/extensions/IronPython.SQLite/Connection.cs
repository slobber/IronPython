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
using System.Runtime.InteropServices;
using System.Threading;
using Community.CsharpSqlite;
using IronPython.Runtime;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

using sqlite3_stmt = Community.CsharpSqlite.Sqlite3.Vdbe;
using sqlite3_value = Community.CsharpSqlite.Sqlite3.Mem;

namespace IronPython.SQLite
{
    public static partial class PythonSQLite
    {
        [PythonType]
        public class Connection
        {
            private const int SQLITE_DETERMINISTIC = 0x800;
            internal const int LEGACY_TRANSACTION_CONTROL = -1;

            private int _autocommit = LEGACY_TRANSACTION_CONTROL;

            public object autocommit
            {
                get => _autocommit switch
                {
                    -1 => LEGACY_TRANSACTION_CONTROL,
                    0 => false,
                    1 => true,
                    _ => throw new InvalidOperationException()
                };
                set => setAutocommit(value);
            }

            public bool in_transaction
            {
                get
                {
                    checkConnection();
                    return Sqlite3.sqlite3_get_autocommit(db) == 0;
                }
            }

            public int total_changes
            {
                get
                {
                    checkConnection();
                    return Sqlite3.sqlite3_total_changes(this.db);
                }
            }
            
            public int detect_types { get; set; }
            public bool check_same_thread { get; set; }

            private double _timeout;
            public double timeout
            {
                get { return _timeout; }
                set { _timeout = value; Sqlite3.sqlite3_busy_timeout(this.db, (int)(_timeout * 1000)); }
            }

            private string _isolation_level;
            public object isolation_level
            {
                get { return _isolation_level; }
                set { setIsolationLevel(value); }
            }

            public string begin_statement { get; private set; }

            public int getlimit(int category)
            {
                checkThread();
                checkConnection();
                int value = Sqlite3.sqlite3_limit(db, category, -1);
                if (value < 0)
                {
                    throw MakeProgrammingError("'category' is out of bounds");
                }
                return value;
            }

            public int setlimit(int category, int limit)
            {
                checkThread();
                checkConnection();
                if (limit < 0)
                {
                    throw PythonOps.OverflowError(
                        "limit must be a positive integer");
                }
                int value = Sqlite3.sqlite3_limit(db, category, limit);
                if (value < 0)
                {
                    throw MakeProgrammingError("'category' is out of bounds");
                }
                return value;
            }

            public bool getconfig(int op)
            {
                checkThread();
                checkConnection();
                validateConfigOption(op);
                int value = 0;
                int rc = Sqlite3.sqlite3_db_config(db, op, -1, out value);
                if (rc != Sqlite3.SQLITE_OK)
                {
                    throw GetSqliteError(db, null);
                }
                return value != 0;
            }

            public object setconfig(int op, bool value = true)
            {
                checkThread();
                checkConnection();
                validateConfigOption(op);
                int current = 0;
                int rc = Sqlite3.sqlite3_db_config(
                    db,
                    op,
                    value ? 1 : 0,
                    out current);
                if (rc != Sqlite3.SQLITE_OK)
                {
                    throw GetSqliteError(db, null);
                }
                Sqlite3.sqlite3ExpirePreparedStatements(db);
                return null;
            }

            private static void validateConfigOption(int op)
            {
                if (op != Sqlite3.SQLITE_DBCONFIG_ENABLE_FKEY
                    && op != Sqlite3.SQLITE_DBCONFIG_ENABLE_TRIGGER)
                {
                    throw PythonOps.ValueError(
                        "unknown database configuration option");
                }
            }

            public void interrupt()
            {
                checkThread();
                checkConnection();
                Sqlite3.sqlite3_interrupt(db);
            }

            public object backup_init(Connection target, string name)
            {
                checkThread();
                checkConnection();
                target.checkThread();
                target.checkConnection();
                Sqlite3.Backup backup = Sqlite3.sqlite3_backup_init(
                    target.db,
                    name,
                    db,
                    name);
                if (backup is null)
                {
                    throw GetSqliteError(target.db, null);
                }

                var result = new NativeBackup(backup);
                function_pinboard.Add(result);
                return result;
            }

            public Bytes serialize(string name = "main")
            {
                checkThread();
                checkConnection();
                byte[] data = Sqlite3.sqlite3_serialize(db, name);
                if (data is null)
                {
                    int rc = Sqlite3.sqlite3_errcode(db);
                    if (rc != Sqlite3.SQLITE_OK)
                    {
                        throw GetSqliteError(db, null);
                    }
                    return new Bytes(Array.Empty<byte>());
                }
                return new Bytes(data);
            }

            public void deserialize(Bytes data, string name = "main")
            {
                checkThread();
                checkConnection();
                int rc = Sqlite3.sqlite3_deserialize(
                    db,
                    name,
                    data.ToArray());
                if (rc != Sqlite3.SQLITE_OK)
                {
                    throw GetSqliteError(db, null);
                }
            }

            public void enable_load_extension(object enable)
            {
                checkThread();
                checkConnection();
                int rc = Sqlite3.sqlite3_enable_load_extension(
                    db,
                    PythonOps.IsTrue(enable) ? 1 : 0);
                if (rc != Sqlite3.SQLITE_OK)
                {
                    throw GetSqliteError(db, null);
                }
            }

            public void load_extension(string path)
            {
                checkThread();
                checkConnection();
                string error = null;
                int rc = Sqlite3.sqlite3_load_extension(
                    db,
                    path,
                    null,
                    ref error);
                if (rc != Sqlite3.SQLITE_OK)
                {
                    throw MakeOperationalError(
                        string.IsNullOrEmpty(error)
                            ? Sqlite3.sqlite3_errmsg(db)
                            : error);
                }
            }

            [PythonType]
            public sealed class NativeBackup
            {
                private Sqlite3.Backup backup;
                private int pageCountValue;
                private int remainingValue;

                internal NativeBackup(Sqlite3.Backup backup)
                {
                    this.backup = backup;
                }

                public int step(int pages)
                {
                    if (backup is null)
                    {
                        return Sqlite3.SQLITE_DONE;
                    }

                    int rc = Sqlite3.sqlite3_backup_step(
                        backup,
                        pages);
                    pageCountValue = Sqlite3.sqlite3_backup_pagecount(backup);
                    remainingValue = Sqlite3.sqlite3_backup_remaining(backup);
                    if (rc == Sqlite3.SQLITE_DONE
                        || rc == Sqlite3.SQLITE_ERROR)
                    {
                        Sqlite3.sqlite3_backup_finish(backup);
                        backup = null;
                    }
                    return rc;
                }

                public int pagecount => pageCountValue;

                public int remaining => remainingValue;

                public void close()
                {
                    if (backup is not null)
                    {
                        Sqlite3.sqlite3_backup_finish(backup);
                        backup = null;
                    }
                }

                ~NativeBackup()
                {
                    close();
                }
            }

            public object row_factory { get; set; }
            public object text_factory { get; set; }

            public IDictionary collations = new PythonDictionary();

            private List<WeakReference> statements = new List<WeakReference>();
            private List<WeakReference> cursors = new List<WeakReference>();
            private int created_statements = 0;

            private readonly HashSet<object> function_pinboard = new();
            private readonly List<object> isolationValueKeepAlive = new();

            internal Sqlite3.sqlite3 db;
            private bool initialized;

            internal bool inTransaction = false;
            internal int thread_ident = Environment.CurrentManagedThreadId;
            internal int AutocommitMode => _autocommit;

            private static readonly Dictionary<object, object> emptyKwargs= new Dictionary<object, object>();

            public PythonType Warning = PythonSQLite.Warning;
            public PythonType Error = PythonSQLite.Error;
            public PythonType InterfaceError = PythonSQLite.InterfaceError;
            public PythonType DataError = PythonSQLite.DataError;
            public PythonType DatabaseError = PythonSQLite.DatabaseError;
            public PythonType OperationalError = PythonSQLite.OperationalError;
            public PythonType InternalError = PythonSQLite.InternalError;
            public PythonType IntegrityError = PythonSQLite.IntegrityError;
            public PythonType ProgrammingError = PythonSQLite.ProgrammingError;
            public PythonType NotSupportedError = PythonSQLite.NotSupportedError;

            private enum AllStatmentsAction
            {
                Reset, Finalize
            }

            public Connection(string database=null,
                double timeout=5.0,
                int detect_types=0,
                string isolation_level="",
                bool check_same_thread=true,
                object factory=null,
                int cached_statements=128,
                bool uri=false,
                object autocommit=null,
                bool isReadOnly=false)
            {
                initialize(
                    database,
                    timeout,
                    detect_types,
                    isolation_level,
                    check_same_thread,
                    cached_statements,
                    uri,
                    autocommit,
                    isReadOnly);
            }

            public void reinitialize(
                string database,
                double timeout=5.0,
                int detect_types=0,
                string isolation_level="",
                bool check_same_thread=true,
                int cached_statements=128,
                bool uri=false,
                object autocommit=null,
                bool isReadOnly=false)
            {
                initialize(
                    database,
                    timeout,
                    detect_types,
                    isolation_level,
                    check_same_thread,
                    cached_statements,
                    uri,
                    autocommit,
                    isReadOnly);
            }

            private void initialize(
                string database,
                double timeout,
                int detect_types,
                string isolation_level,
                bool check_same_thread,
                int cached_statements,
                bool uri,
                object autocommit,
                bool isReadOnly)
            {
                this.text_factory = typeof(string);
                this.initialized = false;
                this.db = null;
                this.inTransaction = false;

                if (database is null)
                {
                    return;
                }

                if (System.IO.Directory.Exists(database))
                {
                    throw MakeOperationalError(
                        "unable to open database file");
                }

                int flags = Sqlite3.SQLITE_OPEN_READWRITE | Sqlite3.SQLITE_OPEN_CREATE;
                if (isReadOnly)
                {
                    flags = Sqlite3.SQLITE_OPEN_READONLY;
                }
                if (uri)
                {
                    flags |= Sqlite3.SQLITE_OPEN_URI;
                }
                int rc = Sqlite3.sqlite3_open_v2(database, out this.db, flags, null);
                if(rc != Sqlite3.SQLITE_OK)
                {
                    Exception error = GetSqliteError(this.db, null);
                    if (this.db is not null)
                    {
                        Sqlite3.sqlite3_close(this.db);
                        this.db = null;
                    }
                    throw error;
                }
                this.initialized = true;

                setIsolationLevel(isolation_level);
                setAutocommit(autocommit ?? LEGACY_TRANSACTION_CONTROL);

                this.detect_types = detect_types;
                this.timeout = timeout;
                this.check_same_thread = check_same_thread;
            }

            ~Connection()
            {
                if(this.db != null)
                    Sqlite3.sqlite3_close(this.db);
            }

            [Documentation("Closes the connection.")]
            public void close()
            {
                checkThread();

                doAllStatements(AllStatmentsAction.Finalize);
                if (this.db != null && in_transaction)
                {
                    endTransaction("ROLLBACK", restart: false);
                }

                if(this.db != null)
                {
                    int rc = Sqlite3.SQLITE_OK, i = 0;
                    
                    do
                    {
                        rc = Sqlite3.sqlite3_close(this.db);
                        if(rc == Sqlite3.SQLITE_BUSY)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                    } while(rc == Sqlite3.SQLITE_BUSY && i++ < 3);

                    if(rc != Sqlite3.SQLITE_OK && rc != Sqlite3.SQLITE_BUSY)
                        throw GetSqliteError(this.db, null);

                    this.db = null;
                }
            }

            internal void begin()
            {
                endTransaction(
                    string.IsNullOrEmpty(begin_statement)
                        ? "BEGIN"
                        : begin_statement,
                    restart: false,
                    beginOnly: true);
            }

            private void endTransaction(
                string command,
                bool restart,
                bool beginOnly = false)
            {
                sqlite3_stmt statement = null;
                string tail = null;
                int rc = Sqlite3.sqlite3_prepare(this.db, command, -1, ref statement, ref tail);
                if(rc != Sqlite3.SQLITE_OK)
                    throw GetSqliteError(this.db, statement);

                rc = Util.Step(statement);
                if(rc == Sqlite3.SQLITE_DONE)
                    this.inTransaction = beginOnly;
                else
                    throw GetSqliteError(this.db, statement);

                rc = Sqlite3.sqlite3_finalize(statement);
                if(rc != Sqlite3.SQLITE_OK)
                    GetSqliteError(this.db, null);

                if (!beginOnly)
                {
                    this.inTransaction = false;
                    if (restart)
                    {
                        begin();
                    }
                }
            }

            [Documentation("Commit the current transaction.")]
            public void commit()
            {
                checkThread(); checkConnection();

                if (_autocommit == 1 || !in_transaction)
                {
                    return;
                }

                endTransaction("COMMIT", restart: _autocommit == 0);
            }

            [Documentation("Roll back the current transaction.")]
            public void rollback()
            {
                checkThread(); checkConnection();

                if (_autocommit == 1 || !in_transaction)
                {
                    return;
                }

                materializeCursors();
                doAllStatements(AllStatmentsAction.Reset);
                endTransaction("ROLLBACK", restart: _autocommit == 0);
            }

            private void materializeCursors()
            {
                foreach (WeakReference weakref in cursors)
                {
                    if (weakref.IsAlive && weakref.Target is Cursor cursor)
                    {
                        cursor.MaterializeRemaining();
                    }
                }
            }

            [Documentation("Return a cursor for the connection.")]
            public object cursor(CodeContext context, object factory=null)
            {
                checkThread(); checkConnection();

                object cursor = factory == null ? new Cursor(context, this) : PythonCalls.Call(context, factory, this);
                cursors.Add(new WeakReference(cursor));

                if(this.row_factory != null)
                    context.LanguageContext.Operations.SetMember(cursor, "row_factory", this.row_factory);

                return cursor;
            }

            [Documentation("Executes a SQL statement. Non-standard.")]
            public object execute(CodeContext context, [ParamDictionary]IDictionary<object, object> kwargs, params object[] args)
            {
                object c = cursor(context, null);
                object execute = context.LanguageContext.Operations.GetMember(c, "execute");
                return PythonCalls.CallWithKeywordArgs(context, execute, args, kwargs);
            }

            [Documentation("Repeatedly executes a SQL statement. Non-standard.")]
            public object executemany(CodeContext context, [ParamDictionary]IDictionary<object, object> kwargs, params object[] args)
            {
                object c = cursor(context, null);
                object executemany = context.LanguageContext.Operations.GetMember(c, "executemany");
                return PythonCalls.CallWithKeywordArgs(context, executemany, args, kwargs);
            }

            [Documentation("Executes a multiple SQL statements at once. Non-standard.")]
            public object executescript(CodeContext context, [ParamDictionary]IDictionary<object, object> kwargs, params object[] args)
            {
                object c = cursor(context, null);
                object executescript = context.LanguageContext.Operations.GetMember(c, "executescript");
                return PythonCalls.CallWithKeywordArgs(context, executescript, args, kwargs);
            }

            public object __call__(string sql)
            {
                return CreateStatement(sql);
            }

            internal Statement CreateStatement(string sql)
            {
                dropUnusedStatementReferences();

                Statement statement = new Statement(this, sql);
                this.statements.Add(new WeakReference(statement));
                return statement;
            }

            private void dropUnusedStatementReferences()
            {
                if(this.created_statements++ < 200)
                    return;

                this.created_statements = 0;

                List<WeakReference> new_list = new List<WeakReference>();

                foreach(WeakReference weakref in this.statements)
                    if(weakref.IsAlive)
                        new_list.Add(weakref);

                this.statements = new_list;
            }

            #region User functions

            [Documentation("Creates a new function. Non-standard.")]
            public void create_function(
                CodeContext context,
                string arg0 = null,
                int arg1 = 0,
                object arg2 = null,
                bool arg3 = false)
            {
                checkThread();
                checkConnection();
                if (arg0 is null || arg2 is null) {
                    throw PythonOps.TypeError(
                        "create_function() takes exactly 3 positional arguments");
                }
                if (arg1 < -1 || arg1 > 127)
                {
                    throw MakeProgrammingError(
                        "Error creating function: not " + arg1);
                }
                if (!PythonOps.IsCallable(context, arg2))
                {
                    throw PythonOps.TypeError("func must be callable");
                }
                int rc = Sqlite3.sqlite3_create_function(this.db, 
                    arg0, arg1,
                    Sqlite3.SQLITE_ANY
                        | (arg3 ? SQLITE_DETERMINISTIC : 0),
                    new object[] { context, arg2 },
                    callUserFunction, 
                    null, null);
                
                if(rc != Sqlite3.SQLITE_OK)
                    throw MakeOperationalError("Error creating function");
                else
                    this.function_pinboard.Add(arg2);
            }

            private static void callUserFunction(Sqlite3.sqlite3_context ctx, int argc, sqlite3_value[] argv)
            {
                object[] data = (object[])Sqlite3.sqlite3_user_data(ctx);
                CodeContext context = (CodeContext)data[0];
                object func = data[1];

                object[] args = buildPyParams(context, ctx, argc, argv);

                try
                {
                    object result = PythonCalls.CallWithKeywordArgs(context, func, args, emptyKwargs);
                    setResult(
                        ctx,
                        result,
                        context,
                        func,
                        GetCallbackName(context, func));
                }
                catch(Exception exception)
                {
                    ReportUnraisable(
                        context,
                        exception,
                        func,
                        GetCallbackName(context, func));
                    if (exception is OutOfMemoryException)
                    {
                        Sqlite3.sqlite3_result_error_nomem(ctx);
                    }
                    else if (exception is OverflowException)
                    {
                        Sqlite3.sqlite3_result_error_toobig(ctx);
                    }
                    else
                    {
                        Sqlite3.sqlite3_result_error(
                            ctx,
                            "user-defined function raised exception",
                            -1);
                    }
                }
            }

            private static object[] buildPyParams(CodeContext context, Sqlite3.sqlite3_context ctx, int argc, sqlite3_value[] argv)
            {
                object[] args = new object[argc];

                for(int i = 0; i < argc; ++i)
                {
                    sqlite3_value cur_value = argv[i];
                    object cur_py_value = null;

                    switch(Sqlite3.sqlite3_value_type(cur_value))
                    {
                        case Sqlite3.SQLITE_INTEGER:
                            long integer = Sqlite3.sqlite3_value_int64(cur_value);
                            cur_py_value = integer >= int.MinValue
                                && integer <= int.MaxValue
                                ? (int)integer
                                : (System.Numerics.BigInteger)integer;
                            break;

                        case Sqlite3.SQLITE_FLOAT:
                            cur_py_value = Sqlite3.sqlite3_value_double(cur_value);
                            break;

                        case Sqlite3.SQLITE_TEXT:
                            cur_py_value = Sqlite3.sqlite3_value_text(cur_value);
                            break;

                        case Sqlite3.SQLITE_BLOB:
                            cur_py_value = new Bytes(
                                Sqlite3.sqlite3_value_blob(cur_value)
                                    ?? Array.Empty<byte>()); // TODO: avoid creating a copy
                            break;

                        case Sqlite3.SQLITE_NULL:
                        default:
                            cur_py_value = null;
                            break;
                    }

                    args[i] = cur_py_value;
                }

                return args;
            }

            private static string GetCallbackName(
                CodeContext context,
                object callback)
            {
                if (callback is not null
                    && PythonOps.TryGetBoundAttr(
                        context,
                        callback,
                        "__name__",
                        out object name)
                    && name is string stringName)
                {
                    return stringName;
                }
                return callback?.GetType().Name;
            }

            private static Exception NormalizeCallbackSignatureError(
                Exception exception)
            {
                string message = exception.Message;
                int takesIndex = message.IndexOf(
                    " takes exactly ",
                    StringComparison.Ordinal);
                int givenIndex = message.IndexOf(
                    " arguments (1 given)",
                    StringComparison.Ordinal);
                if (takesIndex < 0 || givenIndex < 0)
                {
                    return exception;
                }

                int valueIndex = takesIndex + " takes exactly ".Length;
                string countText = message.Substring(
                    valueIndex,
                    givenIndex - valueIndex);
                if (!int.TryParse(countText, out int parameterCount))
                {
                    return exception;
                }

                return PythonOps.TypeError(
                    message.Substring(0, takesIndex)
                        + " missing "
                        + (parameterCount - 1)
                        + " required positional arguments");
            }

            private static void setResult(
                Sqlite3.sqlite3_context ctx,
                object result,
                CodeContext context = null,
                object callback = null,
                string callbackName = null)
            {
                if(result == null)
                    Sqlite3.sqlite3_result_null(ctx);
                else if(result is bool)
                    Sqlite3.sqlite3_result_int64(ctx, ((bool)result) ? 1 : 0);
                else if(result is int)
                    Sqlite3.sqlite3_result_int64(ctx, (int)result);
                else if(result is long)
                    Sqlite3.sqlite3_result_int64(ctx, (long)result);
                else if (result is System.Numerics.BigInteger integer)
                {
                    if (integer < long.MinValue || integer > long.MaxValue)
                    {
                        if (context is not null)
                        {
                            ReportUnraisable(
                                context,
                                PythonOps.OverflowError(
                                    "Python int too large to convert to SQLite INTEGER"),
                                callback,
                                callbackName);
                        }
                        Sqlite3.sqlite3_result_error_toobig(ctx);
                        return;
                    }
                    Sqlite3.sqlite3_result_int64(ctx, (long)integer);
                }
                else if(result is float)
                    Sqlite3.sqlite3_result_double(ctx, (float)result);
                else if(result is double)
                    Sqlite3.sqlite3_result_double(ctx, (double)result);
                else if(result is string text)
                {
                    for (int i = 0; i < text.Length; i++)
                    {
                        if (char.IsSurrogate(text[i]))
                        {
                            throw PythonOps.UnicodeEncodeError(
                                "utf-8",
                                text,
                                i,
                                i + 1,
                                "surrogates not allowed");
                        }
                    }
                    Sqlite3.sqlite3_result_text(
                        ctx,
                        text,
                        -1,
                        Sqlite3.SQLITE_TRANSIENT);
                }
                else
                {
                    byte[] b = result switch
                    {
                        byte[] bytes => bytes,
                        IBufferProtocol bufferProtocol => ReadBuffer(
                            bufferProtocol),
                        _ => null
                    };
                    if (b is null)
                    {
                        Sqlite3.sqlite3_result_error(
                            ctx,
                            "user-defined function raised exception",
                            -1);
                        return;
                    }
                    Sqlite3.sqlite3_result_blob(
                        ctx,
                        b,
                        b.Length,
                        Sqlite3.SQLITE_TRANSIENT);
                }
            }

            private static byte[] ReadBuffer(IBufferProtocol value)
            {
                using IPythonBuffer buffer = value.GetBuffer(BufferFlags.FullRO);
                if (!buffer.IsCContiguous())
                {
                    throw PythonOps.BufferError(
                        "memoryview: underlying buffer is not C-contiguous");
                }
                return buffer.ToArray();
            }

            #endregion

            #region User aggregates

            private class UserAggregateThunk
            {
                public UserAggregateThunk(CodeContext context, string name, object aggregate_class)
                {
                    this.context = context;
                    this.aggregate_class = aggregate_class;
                    this.name = name;
                    this.callbackName = GetCallbackName(
                        context,
                        aggregate_class);
                }

                public void stepCallback(Sqlite3.sqlite3_context ctx, int argc, sqlite3_value[] param)
                {
                    if(instance == null)
                    {
                        try
                        {
                            instance = PythonCalls.Call(context, aggregate_class);
                        }
                        catch(Exception exception)
                        {
                            ReportUnraisable(
                                context,
                                exception,
                                aggregate_class,
                                callbackName);
                            Sqlite3.sqlite3_result_error(ctx, "user-defined aggregate's '__init__' method raised error", -1);
                            return;
                        }
                    }

                    try
                    {
                        if (!context.LanguageContext.Operations.TryGetMember(
                                instance,
                                "step",
                                out object step))
                        {
                            ReportUnraisable(
                                context,
                                PythonOps.AttributeError(
                                    "user-defined aggregate's 'step' method not defined"),
                                aggregate_class,
                                callbackName);
                            Sqlite3.sqlite3_result_error(
                                ctx,
                                "user-defined aggregate's 'step' method not defined",
                                -1);
                            return;
                        }
                        object[] args = buildPyParams(context, ctx, argc, param);

                        PythonCalls.CallWithKeywordArgs(context, step, args, new Dictionary<object, object>());
                    }
                    catch(Exception exception)
                    {
                        ReportUnraisable(
                            context,
                            exception,
                            aggregate_class,
                            callbackName);
                        Sqlite3.sqlite3_result_error(ctx, "user-defined aggregate's 'step' method raised error", -1);
                    }
                }

                public void finalCallback(Sqlite3.sqlite3_context ctx)
                {
                    if(instance == null)
                    {
                        Sqlite3.sqlite3_result_null(ctx);
                        return;
                    }

                    try
                    {
                        if (!context.LanguageContext.Operations.TryGetMember(
                                instance,
                                "finalize",
                                out object finalize))
                        {
                            ReportUnraisable(
                                context,
                                PythonOps.AttributeError(
                                    "user-defined aggregate's 'finalize' method not defined"),
                                aggregate_class,
                                callbackName);
                            Sqlite3.sqlite3_result_error(
                                ctx,
                                "user-defined aggregate's 'finalize' method not defined",
                                -1);
                            return;
                        }

                        object function_result = PythonCalls.Call(context, finalize);
                        setResult(
                            ctx,
                            function_result,
                            context,
                            aggregate_class,
                            callbackName);
                    }
                    catch(Exception exception)
                    {
                        ReportUnraisable(
                            context,
                            exception,
                            aggregate_class,
                            callbackName);
                        Sqlite3.sqlite3_result_error(ctx, "user-defined aggregate's 'finalize' method raised error", -1);
                    }
                    finally
                    {
                        instance = null;
                    }
                }

                private CodeContext context;
                private string name;
                private string callbackName;
                private object aggregate_class;
                private object instance;
            }

            [Documentation("Creates a new aggregate. Non-standard.")]
            public void create_aggregate(
                CodeContext context,
                string arg0 = null,
                int arg1 = 0,
                object arg2 = null,
                bool arg3 = false)
            {
                checkThread();
                checkConnection();
                if (arg0 is null || arg2 is null) {
                    throw PythonOps.TypeError(
                        "create_aggregate() takes exactly 3 positional arguments");
                }
                if (arg1 < -1 || arg1 > 127)
                {
                    throw MakeProgrammingError(
                        "Error creating aggregate: not " + arg1);
                }
                if (!PythonOps.IsCallable(context, arg2))
                {
                    throw PythonOps.TypeError(
                        "aggregate_class must be callable");
                }
                UserAggregateThunk thunk = new UserAggregateThunk(
                    context,
                    arg0,
                    arg2);

                int rc = Sqlite3.sqlite3_create_function(this.db,
                    arg0, arg1,
                    Sqlite3.SQLITE_ANY
                        | (arg3 ? SQLITE_DETERMINISTIC : 0),
                    thunk,
                    null,
                    thunk.stepCallback,
                    thunk.finalCallback);

                if(rc != Sqlite3.SQLITE_OK)
                    throw MakeOperationalError("Error creating aggregate");
                else
                    this.function_pinboard.Add(arg2);
            }

            private sealed class WindowAggregateThunk
            {
                private readonly CodeContext context;
                private readonly object aggregateClass;
                private readonly string name;

                public WindowAggregateThunk(
                    CodeContext context,
                    object aggregateClass,
                    string name)
                {
                    this.context = context;
                    this.aggregateClass = aggregateClass;
                    this.name = name;
                }

                public void stepCallback(
                    Sqlite3.sqlite3_context ctx,
                    int argc,
                    sqlite3_value[] argv)
                {
                    object instance = GetInstance(ctx);
                    if (instance is not null)
                    {
                        CallWithArgs(ctx, instance, "step", argc, argv);
                    }
                }

                public void inverseCallback(
                    Sqlite3.sqlite3_context ctx,
                    int argc,
                    sqlite3_value[] argv)
                {
                    object instance = GetInstance(ctx);
                    if (instance is not null)
                    {
                        CallWithArgs(ctx, instance, "inverse", argc, argv);
                    }
                }

                public void valueCallback(Sqlite3.sqlite3_context ctx)
                {
                    object instance = GetInstance(ctx);
                    if (instance is not null)
                    {
                        CallValue(ctx, instance, "value");
                    }
                }

                public void finalCallback(Sqlite3.sqlite3_context ctx)
                {
                    object instance = GetInstance(ctx);
                    if (instance is not null)
                    {
                        CallValue(ctx, instance, "finalize");
                        ctx.State = null;
                    }
                }

                private object GetInstance(Sqlite3.sqlite3_context ctx)
                {
                    if (ctx.State is not null)
                    {
                        return ctx.State;
                    }

                    try
                    {
                        object instance = PythonCalls.Call(
                            context,
                            aggregateClass);
                        ctx.State = instance;
                        return instance;
                    }
                    catch(Exception exception)
                    {
                        ReportUnraisable(
                            context,
                            exception,
                            aggregateClass,
                            name);
                        Sqlite3.sqlite3_result_error(
                            ctx,
                            "user-defined window function's '__init__' method raised error",
                            -1);
                        return null;
                    }
                }

                private void CallWithArgs(
                    Sqlite3.sqlite3_context ctx,
                    object instance,
                    string methodName,
                    int argc,
                    sqlite3_value[] argv)
                {
                    if (!context.LanguageContext.Operations.TryGetMember(
                            instance,
                            methodName,
                            out object method))
                    {
                        ReportUnraisable(
                            context,
                            PythonOps.AttributeError(
                                "user-defined window function's '"
                                    + methodName
                                    + "' method not defined"),
                            aggregateClass,
                            name);
                        Sqlite3.sqlite3_result_error(
                            ctx,
                            "user-defined window function's '"
                                + methodName
                                + "' method not defined",
                            -1);
                        return;
                    }

                    try
                    {
                        PythonCalls.CallWithKeywordArgs(
                            context,
                            method,
                            buildPyParams(context, ctx, argc, argv),
                            emptyKwargs);
                    }
                    catch(Exception exception)
                    {
                        ReportUnraisable(
                            context,
                            exception,
                            aggregateClass,
                            name);
                        Sqlite3.sqlite3_result_error(
                            ctx,
                            "user-defined window function's '"
                                + methodName
                                + "' method raised error",
                            -1);
                    }
                }

                private void CallValue(
                    Sqlite3.sqlite3_context ctx,
                    object instance,
                    string methodName)
                {
                    if (!context.LanguageContext.Operations.TryGetMember(
                            instance,
                            methodName,
                            out object method))
                    {
                        ReportUnraisable(
                            context,
                            PythonOps.AttributeError(
                                "user-defined window function's '"
                                    + methodName
                                    + "' method not defined"),
                            aggregateClass,
                            name);
                        Sqlite3.sqlite3_result_error(
                            ctx,
                            "user-defined window function's '"
                                + methodName
                                + "' method not defined",
                            -1);
                        return;
                    }

                    try
                    {
                        setResult(
                            ctx,
                            PythonCalls.Call(context, method),
                            context,
                            aggregateClass,
                            name);
                    }
                    catch(Exception exception)
                    {
                        ReportUnraisable(
                            context,
                            exception,
                            aggregateClass,
                            name);
                        Sqlite3.sqlite3_result_error(
                            ctx,
                            "user-defined window function's '"
                                + methodName
                                + "' method raised error",
                            -1);
                    }
                }
            }

            #endregion

            private sealed class CallbackData
            {
                public CallbackData(CodeContext context, object callback)
                {
                    Context = context;
                    Callback = callback;
                }

                public CodeContext Context { get; }

                public object Callback { get; }

                public string LastStatement { get; set; }
            }

            private int compareCallback(
                object data,
                int size1,
                string key1,
                int size2,
                string key2)
            {
                var callbackData = (CallbackData)data;
                try
                {
                    object result = PythonCalls.Call(
                        callbackData.Context,
                        callbackData.Callback,
                        key1,
                        key2);
                    return result switch
                    {
                        bool value => value ? 1 : 0,
                        int value => Math.Sign(value),
                        long value => Math.Sign(value),
                        System.Numerics.BigInteger value => value.Sign,
                        _ => throw PythonOps.TypeError(
                            "create_collation callback must return an integer")
                    };
                }
                catch(Exception exception)
                {
                    exception = NormalizeCallbackSignatureError(exception);
                    ReportUnraisable(
                        callbackData.Context,
                        exception,
                        callbackData.Callback,
                        GetCallbackName(
                            callbackData.Context,
                            callbackData.Callback));
                    return 0;
                }
            }

            [Documentation("Creates a new window function. Non-standard.")]
            public void create_window_function(
                CodeContext context,
                string name,
                int n_arg,
                object aggregate_class)
            {
                checkThread();
                checkConnection();
                if (n_arg < -1 || n_arg > 127)
                {
                    throw MakeProgrammingError(
                        "Error creating window function: not " + n_arg);
                }
                if (aggregate_class is null)
                {
                    int rc = Sqlite3.sqlite3_create_window_function(
                        db,
                        name,
                        n_arg,
                        Sqlite3.SQLITE_ANY,
                        null,
                        null,
                        null,
                        null);
                    if (rc != Sqlite3.SQLITE_OK)
                    {
                        throw GetSqliteError(db, null);
                    }
                    return;
                }

                if (!PythonOps.IsCallable(context, aggregate_class))
                {
                    throw PythonOps.TypeError(
                        "aggregate_class must be callable");
                }

                var thunk = new WindowAggregateThunk(
                    context,
                    aggregate_class,
                    name);
                int result = Sqlite3.sqlite3_create_window_function(
                    db,
                    name,
                    n_arg,
                    Sqlite3.SQLITE_ANY,
                    thunk.stepCallback,
                    thunk.inverseCallback,
                    thunk.valueCallback,
                    thunk.finalCallback);
                if (result != Sqlite3.SQLITE_OK)
                {
                    throw GetSqliteError(db, null);
                }
                function_pinboard.Add(thunk);
            }

            [Documentation("Creates a collation function. Non-standard.")]
            public void create_collation(
                CodeContext context,
                string name,
                object callable = null)
            {
                checkThread();
                checkConnection();
                if (name is null)
                {
                    throw PythonOps.TypeError("name must be str");
                }
                for (int i = 0; i < name.Length; i++)
                {
                    if (char.IsSurrogate(name[i]))
                    {
                        throw PythonOps.UnicodeEncodeError(
                            "utf-8",
                            name,
                            i,
                            i + 1,
                            "surrogates not allowed");
                    }
                }
                if (callable is not null
                    && !PythonOps.IsCallable(context, callable))
                {
                    throw PythonOps.TypeError("parameter must be callable");
                }

                CallbackData data = callable is null
                    ? null
                    : new CallbackData(context, callable);
                int rc = Sqlite3.sqlite3_create_collation(
                    db,
                    name,
                    Sqlite3.SQLITE_UTF8,
                    data,
                    data is null ? null : compareCallback);
                if (rc != Sqlite3.SQLITE_OK)
                {
                    throw GetSqliteError(db, null);
                }

                if (data is not null)
                {
                    function_pinboard.Add(data);
                }
            }

            [Documentation("Sets progress handler callback. Non-standard.")]
            public void set_progress_handler(
                object arg0 = null,
                int arg1 = 0,
                [ParamDictionary] IDictionary<object, object> kwargs = null)
            {
                if (kwargs is not null && kwargs.Count != 0)
                {
                    throw PythonOps.TypeError(
                        "set_progress_handler() takes at least 1 positional argument");
                }
                checkThread();
                checkConnection();

                CallbackData data = arg0 is null
                    ? null
                    : new CallbackData(DefaultContext.Default, arg0);
                Sqlite3.sqlite3_progress_handler(
                    db,
                    data is null ? 0 : arg1,
                    data is null ? null : progressCallback,
                    data);
                if (data is not null)
                {
                    function_pinboard.Add(data);
                }
            }

            private int progressCallback(object data)
            {
                var callbackData = (CallbackData)data;
                try
                {
                    object result = PythonCalls.Call(
                        callbackData.Context,
                        callbackData.Callback);
                    return PythonOps.IsTrue(result) ? 1 : 0;
                }
                catch(Exception exception)
                {
                    exception = NormalizeCallbackSignatureError(exception);
                    ReportUnraisable(
                        callbackData.Context,
                        exception,
                        callbackData.Callback,
                        GetCallbackName(
                            callbackData.Context,
                            callbackData.Callback));
                    return 1;
                }
            }

            [Documentation("Sets authorizer callback. Non-standard.")]
            public void set_authorizer(
                object arg0 = null,
                [ParamDictionary] IDictionary<object, object> kwargs = null)
            {
                if (kwargs is not null && kwargs.Count != 0)
                {
                    throw PythonOps.TypeError(
                        "set_authorizer() takes exactly 1 positional argument");
                }
                checkThread();
                checkConnection();

                CallbackData data = arg0 is null
                    ? null
                    : new CallbackData(DefaultContext.Default, arg0);
                int rc = Sqlite3.sqlite3_set_authorizer(
                    db,
                    data is null ? null : authorizerCallback,
                    data);
                if (rc != Sqlite3.SQLITE_OK)
                {
                    throw GetSqliteError(db, null);
                }

                if (data is not null)
                {
                    function_pinboard.Add(data);
                }
            }

            private int authorizerCallback(
                object data,
                int action,
                string arg1,
                string arg2,
                string database,
                string source)
            {
                var callbackData = (CallbackData)data;
                try
                {
                    object result = PythonCalls.Call(
                        callbackData.Context,
                        callbackData.Callback,
                        action,
                        arg1,
                        arg2,
                        database,
                        source);
                    if (result is int code
                        && code >= Sqlite3.SQLITE_OK
                        && code <= Sqlite3.SQLITE_IGNORE)
                    {
                        return code;
                    }
                }
                catch(Exception exception)
                {
                    ReportUnraisable(
                        callbackData.Context,
                        exception,
                        callbackData.Callback,
                        GetCallbackName(
                            callbackData.Context,
                            callbackData.Callback));
                }

                return Sqlite3.SQLITE_DENY;
            }

            [Documentation("Sets trace callback. Non-standard.")]
            public void set_trace_callback(
                object arg0 = null,
                [ParamDictionary] IDictionary<object, object> kwargs = null)
            {
                if (kwargs is not null && kwargs.Count != 0)
                {
                    throw PythonOps.TypeError(
                        "set_trace_callback() takes exactly 1 positional argument");
                }
                checkThread();
                checkConnection();

                CallbackData data = arg0 is null
                    ? null
                    : new CallbackData(DefaultContext.Default, arg0);
                Sqlite3.sqlite3_trace(
                    db,
                    data is null ? null : traceCallback,
                    data,
                    message => ReportUnraisable(
                        DefaultContext.Default,
                        MakeDataError(message),
                        data?.Callback,
                        data is null
                            ? null
                            : GetCallbackName(
                                DefaultContext.Default,
                                data.Callback)));
                if (data is not null)
                {
                    function_pinboard.Add(data);
                }
            }

            private void traceCallback(object data, string statement)
            {
                var callbackData = (CallbackData)data;
                if (callbackData.LastStatement == statement)
                {
                    return;
                }
                callbackData.LastStatement = statement;

                try
                {
                    PythonCalls.Call(
                        callbackData.Context,
                        callbackData.Callback,
                        statement);
                }
                catch(Exception exception)
                {
                    exception = NormalizeCallbackSignatureError(exception);
                    ReportUnraisable(
                        callbackData.Context,
                        exception,
                        callbackData.Callback,
                        GetCallbackName(
                            callbackData.Context,
                            callbackData.Callback));
                }
            }

            [Documentation("For context manager. Non-standard.")]
            public object __enter__()
            {
                checkThread();
                checkConnection();
                return this;
            }

            [Documentation("For context manager. Non-standard.")]
            public object __exit__(CodeContext context, object exc_type, object exc_value, object exc_tb)
            {
                DynamicOperations ops = context.LanguageContext.Operations;
                if(exc_type == null && exc_value == null && exc_tb == null)
                {
                    object commitfn;
                    if(ops.TryGetMember(this, "commit", out commitfn))
                        ops.Invoke(commitfn);
                    else
                        commit();
                }
                else
                {
                    object rollbackfn;
                    if(ops.TryGetMember(this, "rollback", out rollbackfn))
                        ops.Invoke(rollbackfn);
                    else
                        rollback();
                }

                return false;
            }

            public object iterdump(CodeContext context)
            {
                object dump = Importer.ImportModule(
                    context,
                    null,
                    "sqlite3.dump",
                    false,
                    0);
                object iterDump = context.LanguageContext.Operations.GetMember(
                    dump,
                    "_iterdump");
                return PythonCalls.Call(context, iterDump, this);
            }

            internal void checkConnection()
            {
                if (!this.initialized)
                {
                    throw MakeProgrammingError(
                        "Base Connection.__init__ not called.");
                }
                if(this.db == null)
                    throw MakeProgrammingError("Cannot operate on a closed database.");
            }

            internal void checkThread()
            {
                if(this.check_same_thread)
                    if(this.thread_ident != Environment.CurrentManagedThreadId)
                        throw MakeProgrammingError("SQLite objects created in a thread can only be used in that same thread." +
                            "The object was created in thread id {0} and this is thread id {1}".Format(
                            this.thread_ident, Environment.CurrentManagedThreadId));
            }

            internal static void verify(Connection connection)
            {
                verify(connection, false);
            }

            internal static void verify(Connection connection, bool closed)
            {
                if(!closed && (connection == null || connection.db == null))
                    throw MakeProgrammingError("Cannot operate on a closed database.");

                connection.checkThread();
            }

            private void setIsolationLevel(object value)
            {
                string isolation_level;
                if (value is null)
                {
                    isolation_level = null;
                }
                else if (value is string text)
                {
                    isolation_level = text;
                }
                else if (value is ExtensibleString extensible)
                {
                    isolation_level = extensible.Value;
                    isolationValueKeepAlive.Add(value);
                }
                else
                {
                    throw PythonOps.TypeError(
                        "isolation_level must be str or None");
                }

                if (isolation_level is not null)
                {
                    isolation_level = isolation_level.ToUpperInvariant();
                }
                if (isolation_level is not null
                    && isolation_level != ""
                    && isolation_level != "DEFERRED"
                    && isolation_level != "IMMEDIATE"
                    && isolation_level != "EXCLUSIVE")
                {
                    throw PythonOps.ValueError(
                        "isolation_level string must be '', 'DEFERRED', "
                        + "'IMMEDIATE', or 'EXCLUSIVE'");
                }
                if(isolation_level == null)
                {
                    this._isolation_level = null;
                    if (_autocommit == LEGACY_TRANSACTION_CONTROL)
                    {
                        this.commit();
                    }
                }
                else
                {
                    this._isolation_level = isolation_level;
                }
                updateBeginStatement();
            }

            public void set_isolation_level_value(object value)
            {
                setIsolationLevel(value);
            }

            private void updateBeginStatement()
            {
                this.begin_statement =
                    _autocommit == LEGACY_TRANSACTION_CONTROL
                    && _isolation_level is not null
                        ? "BEGIN " + _isolation_level
                        : null;
            }

            private void setAutocommit(object value)
            {
                int mode;
                if (value is bool enabled)
                {
                    mode = enabled ? 1 : 0;
                }
                else if (value is int integer
                    && integer >= LEGACY_TRANSACTION_CONTROL
                    && integer <= 1)
                {
                    mode = integer;
                }
                else
                {
                    throw PythonOps.ValueError(
                        "autocommit must be True, False, or "
                        + "sqlite3.LEGACY_TRANSACTION_CONTROL");
                }

                if (db is null)
                {
                    throw MakeProgrammingError(
                        "Base Connection.__init__ not called.");
                }

                bool wasInTransaction = Sqlite3.sqlite3_get_autocommit(db) == 0;
                int previous = _autocommit;
                _autocommit = mode;

                if (mode == 1 && wasInTransaction)
                {
                    endTransaction("COMMIT", restart: false);
                }
                else if (mode == 0
                    && previous != 0
                    && !wasInTransaction)
                {
                    begin();
                }

                updateBeginStatement();
            }

            private void doAllStatements(AllStatmentsAction action)
            {
                foreach(WeakReference weakref in this.statements)
                {
                    if(weakref.IsAlive)
                    {
                        Statement statement = weakref.Target as Statement;

                        if(statement != null)
                        {
                            if(action == AllStatmentsAction.Reset)
                                statement.Reset();
                            else
                                statement.SqliteFinalize();
                        }
                    }
                }
            }
        }
    }
}
