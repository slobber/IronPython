// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SQLitePCL;

namespace Community.CsharpSqlite;

/// <summary>
/// Thin compatibility adapter between the IronPython _sqlite3 wrapper and
/// SQLite's C API through SQLitePCLRaw.
/// </summary>
internal static class Sqlite3
{
    private static readonly PropertyInfo Sqlite3ValuePointer =
        typeof(SQLitePCL.sqlite3_value).GetProperty(
            "ptr",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(
            typeof(SQLitePCL.sqlite3_value).FullName,
            "ptr");

    static Sqlite3()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(Sqlite3).Assembly,
            ResolveNativeLibrary);
        Assembly provider = Assembly.Load("SQLitePCLRaw.provider.e_sqlite3");
        NativeLibrary.SetDllImportResolver(provider, ResolveNativeLibrary);
        Batteries_V2.Init();
    }

    internal sealed class sqlite3 : IDisposable
    {
        internal SQLitePCL.sqlite3 Handle { get; private set; }
        internal bool IsClosed => Handle is null || Handle.IsClosed;

        internal sqlite3(SQLitePCL.sqlite3 handle)
        {
            Handle = handle;
        }

        public void Dispose()
        {
            if (Handle is not null)
            {
                Handle.Dispose();
                Handle = null;
            }
        }

        internal int ManualClose()
        {
            if (Handle is null)
            {
                return SQLITE_OK;
            }
            int rc = Handle.manual_close();
            Handle = null;
            return rc;
        }

        internal int ManualCloseV2()
        {
            if (Handle is null)
            {
                return SQLITE_OK;
            }
            int rc = Handle.manual_close_v2();
            Handle = null;
            return rc;
        }
    }

    internal sealed class Vdbe : IDisposable
    {
        internal SQLitePCL.sqlite3_stmt Handle { get; private set; }
        internal sqlite3 db { get; }

        internal Vdbe(SQLitePCL.sqlite3_stmt handle, sqlite3 database)
        {
            Handle = handle;
            db = database;
        }

        public void Dispose()
        {
            if (Handle is not null)
            {
                Handle.Dispose();
                Handle = null;
            }
        }

        internal int ManualClose()
        {
            if (Handle is null)
            {
                return SQLITE_OK;
            }
            int rc = Handle.manual_close();
            Handle = null;
            return rc;
        }
    }

    internal sealed class Backup : IDisposable
    {
        internal SQLitePCL.sqlite3_backup Handle { get; private set; }

        internal Backup(SQLitePCL.sqlite3_backup handle)
        {
            Handle = handle;
        }

        public void Dispose()
        {
            if (Handle is not null)
            {
                Handle.Dispose();
                Handle = null;
            }
        }

        internal int ManualClose()
        {
            if (Handle is null)
            {
                return SQLITE_OK;
            }
            int rc = Handle.manual_close();
            Handle = null;
            return rc;
        }
    }

    internal sealed class Mem
    {
        internal SQLitePCL.sqlite3_value Handle { get; }

        internal Mem(SQLitePCL.sqlite3_value handle)
        {
            Handle = handle;
        }
    }

    internal sealed class sqlite3_context
    {
        private readonly SQLitePCL.sqlite3_context _context;
        private IntPtr _nativeContext;
        private object _state;

        internal sqlite3_context(SQLitePCL.sqlite3_context context)
        {
            _context = context;
        }

        internal sqlite3_context(IntPtr nativeContext)
        {
            _nativeContext = nativeContext;
        }

        internal object State
        {
            get => _context is null ? _state : _context.state;
            set
            {
                if (_context is null)
                {
                    _state = value;
                }
                else
                {
                    _context.state = value;
                }
            }
        }

        internal SQLitePCL.sqlite3_context Context => _context;
        internal IntPtr NativeContext => _nativeContext;

        internal void SetNativeContext(IntPtr context)
        {
            _nativeContext = context;
        }
    }

    public delegate void user_function(
        sqlite3_context context,
        int argc,
        Mem[] argv);

    public delegate void aggregate_step(
        sqlite3_context context,
        int argc,
        Mem[] argv);

    public delegate void aggregate_final(sqlite3_context context);

    public delegate int compare_callback(
        object data,
        int size1,
        string key1,
        int size2,
        string key2);

    public delegate int progress_callback(object data);

    public delegate int authorizer_callback(
        object data,
        int action,
        string arg1,
        string arg2,
        string database,
        string source);

    public delegate void trace_callback(object data, string statement);

    public delegate void window_step(
        sqlite3_context context,
        int argc,
        Mem[] argv);

    public delegate void window_inverse(
        sqlite3_context context,
        int argc,
        Mem[] argv);

    public delegate void window_value(sqlite3_context context);

    public delegate void window_final(sqlite3_context context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void native_window_step(
        IntPtr context,
        int argc,
        IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void native_window_inverse(
        IntPtr context,
        int argc,
        IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void native_window_value(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void native_window_final(IntPtr context);

    private sealed class WindowRegistration
    {
        internal window_step Step { get; }
        internal window_inverse Inverse { get; }
        internal window_value Value { get; }
        internal window_final Final { get; }

        internal WindowRegistration(
            window_step step,
            window_inverse inverse,
            window_value value,
            window_final final)
        {
            Step = step;
            Inverse = inverse;
            Value = value;
            Final = final;
        }
    }

    private static readonly Dictionary<
        (sqlite3 Database, string Name),
        GCHandle> WindowRegistrations = [];
    private static readonly Dictionary<sqlite3, GCHandle>
        TraceRegistrations = [];
    private static readonly IntPtr Transient = new(-1);
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(false, true);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int native_trace_callback(
        int eventCode,
        IntPtr context,
        IntPtr statement,
        IntPtr expandedStatement);

    private sealed class TraceRegistration
    {
        internal trace_callback Callback { get; }
        internal object UserData { get; }
        internal Action<string> ReportExpansionError { get; }

        internal TraceRegistration(
            trace_callback callback,
            object userData,
            Action<string> reportExpansionError)
        {
            Callback = callback;
            UserData = userData;
            ReportExpansionError = reportExpansionError;
        }
    }

    public const int SQLITE_OK = raw.SQLITE_OK;
    public const int SQLITE_ERROR = raw.SQLITE_ERROR;
    public const int SQLITE_INTERNAL = raw.SQLITE_INTERNAL;
    public const int SQLITE_PERM = raw.SQLITE_PERM;
    public const int SQLITE_ABORT = raw.SQLITE_ABORT;
    public const int SQLITE_BUSY = raw.SQLITE_BUSY;
    public const int SQLITE_LOCKED = raw.SQLITE_LOCKED;
    public const int SQLITE_NOMEM = raw.SQLITE_NOMEM;
    public const int SQLITE_READONLY = raw.SQLITE_READONLY;
    public const int SQLITE_INTERRUPT = raw.SQLITE_INTERRUPT;
    public const int SQLITE_IOERR = raw.SQLITE_IOERR;
    public const int SQLITE_CORRUPT = raw.SQLITE_CORRUPT;
    public const int SQLITE_NOTFOUND = raw.SQLITE_NOTFOUND;
    public const int SQLITE_FULL = raw.SQLITE_FULL;
    public const int SQLITE_CANTOPEN = raw.SQLITE_CANTOPEN;
    public const int SQLITE_PROTOCOL = raw.SQLITE_PROTOCOL;
    public const int SQLITE_EMPTY = raw.SQLITE_EMPTY;
    public const int SQLITE_SCHEMA = raw.SQLITE_SCHEMA;
    public const int SQLITE_TOOBIG = raw.SQLITE_TOOBIG;
    public const int SQLITE_CONSTRAINT = raw.SQLITE_CONSTRAINT;
    public const int SQLITE_MISMATCH = raw.SQLITE_MISMATCH;
    public const int SQLITE_MISUSE = raw.SQLITE_MISUSE;
    public const int SQLITE_INTEGER = raw.SQLITE_INTEGER;
    public const int SQLITE_FLOAT = raw.SQLITE_FLOAT;
    public const int SQLITE_TEXT = raw.SQLITE_TEXT;
    public const int SQLITE_BLOB = raw.SQLITE_BLOB;
    public const int SQLITE_NULL = raw.SQLITE_NULL;
    public const int SQLITE_ROW = raw.SQLITE_ROW;
    public const int SQLITE_DONE = raw.SQLITE_DONE;
    public const int SQLITE_OPEN_READONLY = raw.SQLITE_OPEN_READONLY;
    public const int SQLITE_OPEN_READWRITE = raw.SQLITE_OPEN_READWRITE;
    public const int SQLITE_OPEN_CREATE = raw.SQLITE_OPEN_CREATE;
    public const int SQLITE_OPEN_URI = raw.SQLITE_OPEN_URI;
    public const int SQLITE_UTF8 = raw.SQLITE_UTF8;
    public const int SQLITE_ANY = raw.SQLITE_ANY;
    public const int SQLITE_DENY = raw.SQLITE_DENY;
    public const int SQLITE_IGNORE = raw.SQLITE_IGNORE;
    public const int SQLITE_DBCONFIG_ENABLE_FKEY = raw.SQLITE_DBCONFIG_ENABLE_FKEY;
    public const int SQLITE_DBCONFIG_ENABLE_TRIGGER = raw.SQLITE_DBCONFIG_ENABLE_TRIGGER;
    public const object SQLITE_TRANSIENT = null;

    public static string sqlite3_version =>
        raw.sqlite3_libversion().utf8_to_string();

    public static int sqlite3_open_v2(
        string filename,
        out sqlite3 db,
        int flags,
        string vfs)
    {
        int rc = raw.sqlite3_open_v2(
            filename,
            out SQLitePCL.sqlite3 handle,
            flags,
            vfs);
        db = handle is null ? null : new sqlite3(handle);
        return rc;
    }

    public static int sqlite3_close(sqlite3 db)
    {
        if (db is null || db.IsClosed)
        {
            return SQLITE_OK;
        }

        return db.ManualClose();
    }

    public static int sqlite3_close_v2(sqlite3 db)
    {
        if (db is null || db.IsClosed)
        {
            return SQLITE_OK;
        }

        return db.ManualCloseV2();
    }

    public static int sqlite3_prepare(
        sqlite3 db,
        string sql,
        int unusedLength,
        ref Vdbe statement,
        ref string tail)
    {
        int rc = raw.sqlite3_prepare_v2(
            db.Handle,
            sql,
            out SQLitePCL.sqlite3_stmt handle,
            out tail);
        statement = handle is null ? null : new Vdbe(handle, db);
        return rc;
    }

    public static int sqlite3_step(Vdbe statement)
        => raw.sqlite3_step(statement.Handle);

    public static int sqlite3_finalize(Vdbe statement)
    {
        if (statement is null)
        {
            return SQLITE_OK;
        }

        return statement.ManualClose();
    }

    public static int sqlite3_reset(Vdbe statement)
        => raw.sqlite3_reset(statement.Handle);

    public static int sqlite3_clear_bindings(Vdbe statement)
        => raw.sqlite3_clear_bindings(statement.Handle);

    public static int sqlite3_bind_parameter_count(Vdbe statement)
        => raw.sqlite3_bind_parameter_count(statement.Handle);

    public static string sqlite3_bind_parameter_name(Vdbe statement, int index)
        => ToString(raw.sqlite3_bind_parameter_name(statement.Handle, index));

    public static int sqlite3_bind_null(Vdbe statement, int index)
        => raw.sqlite3_bind_null(statement.Handle, index);

    public static int sqlite3_bind_int(Vdbe statement, int index, int value)
        => raw.sqlite3_bind_int(statement.Handle, index, value);

    public static int sqlite3_bind_int64(Vdbe statement, int index, long value)
        => raw.sqlite3_bind_int64(statement.Handle, index, value);

    public static int sqlite3_bind_double(Vdbe statement, int index, double value)
        => raw.sqlite3_bind_double(statement.Handle, index, value);

    public static int sqlite3_bind_text(
        Vdbe statement,
        int index,
        string value,
        int unusedLength,
        object unusedDestructor)
        => raw.sqlite3_bind_text(
            statement.Handle,
            index,
            Encoding.UTF8.GetBytes(value));

    public static int sqlite3_bind_blob(
        Vdbe statement,
        int index,
        byte[] value,
        int unusedLength,
        object unusedDestructor)
        => raw.sqlite3_bind_blob(statement.Handle, index, value);

    public static int sqlite3_column_count(Vdbe statement)
        => raw.sqlite3_column_count(statement.Handle);

    public static int sqlite3_data_count(Vdbe statement)
        => raw.sqlite3_data_count(statement.Handle);

    public static string sqlite3_column_name(Vdbe statement, int index)
        => ToString(raw.sqlite3_column_name(statement.Handle, index));

    public static string sqlite3_column_decltype(Vdbe statement, int index)
        => ToString(raw.sqlite3_column_decltype(statement.Handle, index));

    public static int sqlite3_column_type(Vdbe statement, int index)
        => raw.sqlite3_column_type(statement.Handle, index);

    public static long sqlite3_column_int64(Vdbe statement, int index)
        => raw.sqlite3_column_int64(statement.Handle, index);

    public static double sqlite3_column_double(Vdbe statement, int index)
        => raw.sqlite3_column_double(statement.Handle, index);

    public static string sqlite3_column_text(Vdbe statement, int index)
    {
        if (raw.sqlite3_column_type(statement.Handle, index) == SQLITE_NULL)
        {
            return null;
        }

        return ReadNativeText(
            sqlite3_column_text_ptr(statement.Handle.DangerousGetHandle(), index),
            raw.sqlite3_column_bytes(statement.Handle, index));
    }

    public static byte[] sqlite3_column_blob(Vdbe statement, int index)
        => raw.sqlite3_column_type(statement.Handle, index) == SQLITE_NULL
            ? null
            : raw.sqlite3_column_blob(statement.Handle, index).ToArray();

    public static int sqlite3_changes(sqlite3 db)
        => raw.sqlite3_changes(db.Handle);

    public static int sqlite3_total_changes(sqlite3 db)
        => raw.sqlite3_total_changes(db.Handle);

    public static long sqlite3_last_insert_rowid(sqlite3 db)
        => raw.sqlite3_last_insert_rowid(db.Handle);

    public static int sqlite3_get_autocommit(sqlite3 db)
        => raw.sqlite3_get_autocommit(db.Handle);

    public static int sqlite3_busy_timeout(sqlite3 db, int milliseconds)
        => raw.sqlite3_busy_timeout(db.Handle, milliseconds);

    public static int sqlite3_limit(sqlite3 db, int category, int newValue)
        => raw.sqlite3_limit(db.Handle, category, newValue);

    public static int sqlite3_db_config(
        sqlite3 db,
        int option,
        int value,
        out int current)
        => raw.sqlite3_db_config(
            db.Handle,
            option,
            value,
            out current);

    public static string sqlite3_errmsg(sqlite3 db)
        => ToString(raw.sqlite3_errmsg(db.Handle));

    public static int sqlite3_errcode(sqlite3 db)
        => raw.sqlite3_errcode(db.Handle);

    public static int sqlite3_extended_errcode(sqlite3 db)
        => raw.sqlite3_extended_errcode(db.Handle);

    public static int sqlite3_enable_load_extension(sqlite3 db, int enabled)
        => raw.sqlite3_enable_load_extension(db.Handle, enabled);

    public static int sqlite3_load_extension(
        sqlite3 db,
        string path,
        string entryPoint,
        ref string error)
    {
        int rc = raw.sqlite3_load_extension(
            db.Handle,
            utf8z.FromString(path),
            utf8z.FromString(entryPoint),
            out utf8z errorMessage);
        error = ToString(errorMessage);
        return rc;
    }

    public static void sqlite3_interrupt(sqlite3 db)
        => raw.sqlite3_interrupt(db.Handle);

    public static Backup sqlite3_backup_init(
        sqlite3 destination,
        string destinationName,
        sqlite3 source,
        string sourceName)
    {
        SQLitePCL.sqlite3_backup backup = raw.sqlite3_backup_init(
            destination.Handle,
            destinationName,
            source.Handle,
            sourceName);
        return backup is null ? null : new Backup(backup);
    }

    public static int sqlite3_backup_step(Backup backup, int pages)
        => raw.sqlite3_backup_step(backup.Handle, pages);

    public static int sqlite3_backup_remaining(Backup backup)
        => raw.sqlite3_backup_remaining(backup.Handle);

    public static int sqlite3_backup_pagecount(Backup backup)
        => raw.sqlite3_backup_pagecount(backup.Handle);

    public static int sqlite3_backup_finish(Backup backup)
    {
        if (backup is null)
        {
            return SQLITE_OK;
        }
        return backup.ManualClose();
    }

    public static byte[] sqlite3_serialize(sqlite3 db, string name)
    {
        IntPtr data = raw.sqlite3_serialize(
            db.Handle,
            name,
            out long size,
            0);
        if (data == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            byte[] result = new byte[size];
            Marshal.Copy(data, result, 0, result.Length);
            return result;
        }
        finally
        {
            raw.sqlite3_free(data);
        }
    }

    public static int sqlite3_deserialize(
        sqlite3 db,
        string name,
        byte[] data)
    {
        IntPtr copy = raw.sqlite3_malloc64(data.Length);
        if (copy == IntPtr.Zero)
        {
            return SQLITE_NOMEM;
        }
        Marshal.Copy(data, 0, copy, data.Length);
        return raw.sqlite3_deserialize(
            db.Handle,
            name,
            copy,
            data.Length,
            data.Length,
            raw.SQLITE_DESERIALIZE_FREEONCLOSE);
    }

    public static void sqlite3ExpirePreparedStatements(sqlite3 db)
    {
    }

    public static int sqlite3_create_function(
        sqlite3 db,
        string name,
        int argc,
        int flags,
        object userData,
        user_function scalar,
        aggregate_step step,
        aggregate_final final)
    {
        if (scalar is not null)
        {
            return raw.sqlite3_create_function(
                db.Handle,
                name,
                argc,
                flags,
                userData,
                (context, _, argv) => scalar(
                    new sqlite3_context(context),
                    argv.Length,
                    Array.ConvertAll(argv, value => new Mem(value))));
        }

        if (step is null)
        {
            return raw.sqlite3_create_function(
                db.Handle,
                name,
                argc,
                flags,
                userData,
                (delegate_function_scalar)null);
        }

        return raw.sqlite3_create_function(
            db.Handle,
            name,
            argc,
            flags,
            userData,
            (context, _, argv) => step(
                new sqlite3_context(context),
                argv.Length,
                Array.ConvertAll(argv, value => new Mem(value))),
            (context, _) => final(new sqlite3_context(context)));
    }

    public static int sqlite3_create_window_function(
        sqlite3 db,
        string name,
        int argc,
        int flags,
        window_step step,
        window_inverse inverse,
        window_value value,
        window_final final)
    {
        var key = (db, name);
        if (WindowRegistrations.Remove(key, out GCHandle oldRegistration))
        {
            oldRegistration.Free();
        }

        byte[] encodedName = Encoding.UTF8.GetBytes(name + "\0");
        if (step is null)
        {
            return NativeCreateWindowFunction(
                db.Handle.DangerousGetHandle(),
                encodedName,
                argc,
                flags,
                IntPtr.Zero,
                null,
                null,
                null,
                null,
                IntPtr.Zero);
        }

        var registration = new WindowRegistration(
            step,
            inverse,
            value,
            final);
        GCHandle registrationHandle =
            GCHandle.Alloc(registration, GCHandleType.Normal);
        int rc = NativeCreateWindowFunction(
            db.Handle.DangerousGetHandle(),
            encodedName,
            argc,
            flags,
            GCHandle.ToIntPtr(registrationHandle),
            WindowStepCallback,
            WindowFinalCallback,
            WindowValueCallback,
            WindowInverseCallback,
            IntPtr.Zero);
        if (rc == SQLITE_OK)
        {
            WindowRegistrations.Add(key, registrationHandle);
        }
        else
        {
            registrationHandle.Free();
        }
        return rc;
    }

    public static object sqlite3_user_data(sqlite3_context context)
        => raw.sqlite3_user_data(context.Context);

    public static int sqlite3_create_collation(
        sqlite3 db,
        string name,
        int encoding,
        object userData,
        compare_callback callback)
    {
        return raw.sqlite3_create_collation(
            db.Handle,
            name,
            userData,
            callback is null
                ? null
                : (_, key1, key2) => callback(
                    userData,
                    Encoding.UTF8.GetByteCount(key1),
                    key1,
                    Encoding.UTF8.GetByteCount(key2),
                    key2));
    }

    public static int sqlite3_progress_handler(
        sqlite3 db,
        int instructions,
        progress_callback callback,
        object userData)
    {
        raw.sqlite3_progress_handler(
            db.Handle,
            instructions,
            callback is null ? null : _ => callback(userData),
            userData);
        return SQLITE_OK;
    }

    public static int sqlite3_set_authorizer(
        sqlite3 db,
        authorizer_callback callback,
        object userData)
    {
        return raw.sqlite3_set_authorizer(
            db.Handle,
            callback is null
                ? null
                : (_, action, arg1, arg2, database, source) =>
                    callback(
                        userData,
                        action,
                        arg1,
                        arg2,
                        database,
                        source),
            userData);
    }

    public static void sqlite3_trace(
        sqlite3 db,
        trace_callback callback,
        object userData,
        Action<string> reportExpansionError)
    {
        if (TraceRegistrations.Remove(db, out GCHandle oldRegistration))
        {
            oldRegistration.Free();
        }

        if (callback is null)
        {
            NativeTraceV2(
                db.Handle.DangerousGetHandle(),
                raw.SQLITE_TRACE_STMT,
                null,
                IntPtr.Zero);
            return;
        }

        GCHandle registrationHandle = GCHandle.Alloc(
            new TraceRegistration(
                callback,
                userData,
                reportExpansionError),
            GCHandleType.Normal);
        int rc = NativeTraceV2(
            db.Handle.DangerousGetHandle(),
            raw.SQLITE_TRACE_STMT,
            TraceCallback,
            GCHandle.ToIntPtr(registrationHandle));
        if (rc == SQLITE_OK)
        {
            TraceRegistrations.Add(db, registrationHandle);
        }
        else
        {
            registrationHandle.Free();
        }
    }

    public static int sqlite3_value_type(Mem value)
        => raw.sqlite3_value_type(value.Handle);

    public static long sqlite3_value_int64(Mem value)
        => raw.sqlite3_value_int64(value.Handle);

    public static double sqlite3_value_double(Mem value)
        => raw.sqlite3_value_double(value.Handle);

    public static string sqlite3_value_text(Mem value)
    {
        if (raw.sqlite3_value_type(value.Handle) == SQLITE_NULL)
        {
            return null;
        }

        return ReadNativeText(
            sqlite3_value_text_ptr(
                (IntPtr)Sqlite3ValuePointer.GetValue(value.Handle)!),
            raw.sqlite3_value_bytes(value.Handle));
    }

    public static byte[] sqlite3_value_blob(Mem value)
        => raw.sqlite3_value_type(value.Handle) == SQLITE_NULL
            ? null
            : raw.sqlite3_value_blob(value.Handle).ToArray();

    public static void sqlite3_result_null(sqlite3_context context)
    {
        if (context.NativeContext != IntPtr.Zero)
        {
            NativeResultNull(context.NativeContext);
        }
        else
        {
            raw.sqlite3_result_null(context.Context);
        }
    }

    public static void sqlite3_result_int64(sqlite3_context context, long value)
    {
        if (context.NativeContext != IntPtr.Zero)
        {
            NativeResultInt64(context.NativeContext, value);
        }
        else
        {
            raw.sqlite3_result_int64(context.Context, value);
        }
    }

    public static void sqlite3_result_double(sqlite3_context context, double value)
    {
        if (context.NativeContext != IntPtr.Zero)
        {
            NativeResultDouble(context.NativeContext, value);
        }
        else
        {
            raw.sqlite3_result_double(context.Context, value);
        }
    }

    public static void sqlite3_result_text(
        sqlite3_context context,
        string value,
        int unusedLength,
        object unusedDestructor)
    {
        byte[] encoded = StrictUtf8.GetBytes(value);
        if (context.NativeContext != IntPtr.Zero)
        {
            NativeResultText(
                context.NativeContext,
                encoded,
                encoded.Length,
                Transient);
        }
        else
        {
            raw.sqlite3_result_text(context.Context, encoded);
        }
    }

    public static void sqlite3_result_blob(
        sqlite3_context context,
        byte[] value,
        int unusedLength,
        object unusedDestructor)
    {
        if (context.NativeContext != IntPtr.Zero)
        {
            NativeResultBlob(
                context.NativeContext,
                value,
                value.Length,
                Transient);
        }
        else
        {
            raw.sqlite3_result_blob(context.Context, value);
        }
    }

    public static void sqlite3_result_blob(
        sqlite3_context context,
        string value,
        int unusedLength,
        object unusedDestructor)
        => sqlite3_result_blob(
            context,
            Encoding.Latin1.GetBytes(value),
            unusedLength,
            unusedDestructor);

    public static void sqlite3_result_error(
        sqlite3_context context,
        string value,
        int unusedLength)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        if (context.NativeContext != IntPtr.Zero)
        {
            NativeResultError(
                context.NativeContext,
                encoded,
                encoded.Length,
                Transient);
        }
        else
        {
            raw.sqlite3_result_error(context.Context, encoded);
        }
    }

    public static void sqlite3_result_error_toobig(
        sqlite3_context context)
    {
        if (context.NativeContext != IntPtr.Zero)
        {
            NativeResultErrorTooBig(context.NativeContext);
        }
        else
        {
            raw.sqlite3_result_error_toobig(context.Context);
        }
    }

    public static void sqlite3_result_error_nomem(
        sqlite3_context context)
    {
        if (context.NativeContext != IntPtr.Zero)
        {
            NativeResultErrorNoMem(context.NativeContext);
        }
        else
        {
            raw.sqlite3_result_error_nomem(context.Context);
        }
    }

    private static string ToString(utf8z value)
        => value.utf8_to_string();

    private static string ReadNativeText(IntPtr text, int byteCount)
        => text == IntPtr.Zero || byteCount == 0
            ? string.Empty
            : Marshal.PtrToStringUTF8(text, byteCount);

    private static readonly native_window_step WindowStepCallback =
        WindowStepImpl;
    private static readonly native_window_inverse WindowInverseCallback =
        WindowInverseImpl;
    private static readonly native_window_value WindowValueCallback =
        WindowValueImpl;
    private static readonly native_window_final WindowFinalCallback =
        WindowFinalImpl;
    private static readonly native_trace_callback TraceCallback =
        TraceImpl;

    private static int TraceImpl(
        int eventCode,
        IntPtr context,
        IntPtr statement,
        IntPtr expandedStatement)
    {
        if (eventCode != raw.SQLITE_TRACE_STMT)
        {
            return 0;
        }

        try
        {
            IntPtr sqlPointer = IntPtr.Zero;
            if (statement != IntPtr.Zero)
            {
                sqlPointer = NativeExpandedSql(statement);
            }
            if (sqlPointer == IntPtr.Zero)
            {
                var registrationForError = (TraceRegistration)GCHandle
                    .FromIntPtr(context)
                    .Target!;
                registrationForError.ReportExpansionError(
                    "Expanded SQL string exceeds the maximum string length");
                sqlPointer = expandedStatement;
                if (sqlPointer == IntPtr.Zero)
                {
                    return 0;
                }
            }
            var registration = (TraceRegistration)GCHandle
                .FromIntPtr(context)
                .Target!;
            try
            {
                registration.Callback(
                    registration.UserData,
                    Marshal.PtrToStringUTF8(sqlPointer));
            }
            finally
            {
                if (sqlPointer != expandedStatement)
                {
                    NativeFree(sqlPointer);
                }
            }
        }
        catch
        {
        }
        return 0;
    }

    private static void WindowStepImpl(
        IntPtr context,
        int argc,
        IntPtr argv)
    {
        try
        {
            GetRegistration(context).Step(
                GetWindowContext(context, out _),
                argc,
                BuildNativeArguments(argc, argv));
        }
        catch (Exception)
        {
            ResultNativeError(context, "user-defined window function raised exception");
        }
    }

    private static void WindowInverseImpl(
        IntPtr context,
        int argc,
        IntPtr argv)
    {
        try
        {
            GetRegistration(context).Inverse(
                GetWindowContext(context, out _),
                argc,
                BuildNativeArguments(argc, argv));
        }
        catch (Exception)
        {
            ResultNativeError(context, "user-defined window function raised exception");
        }
    }

    private static void WindowValueImpl(IntPtr context)
    {
        try
        {
            GetRegistration(context).Value(
                GetWindowContext(context, out _));
        }
        catch (Exception)
        {
            ResultNativeError(context, "user-defined window function raised exception");
        }
    }

    private static void WindowFinalImpl(IntPtr context)
    {
        try
        {
            GetRegistration(context).Final(
                GetWindowContext(context, out IntPtr aggregateContext));
            IntPtr handlePointer = Marshal.ReadIntPtr(aggregateContext);
            if (handlePointer != IntPtr.Zero)
            {
                GCHandle.FromIntPtr(handlePointer).Free();
                Marshal.WriteIntPtr(aggregateContext, IntPtr.Zero);
            }
        }
        catch (Exception)
        {
            ResultNativeError(context, "user-defined window function raised exception");
        }
    }

    private static WindowRegistration GetRegistration(IntPtr context)
    {
        IntPtr userData = NativeUserData(context);
        return (WindowRegistration)GCHandle.FromIntPtr(userData).Target!;
    }

    private static sqlite3_context GetWindowContext(
        IntPtr context,
        out IntPtr aggregateContext)
    {
        aggregateContext = NativeAggregateContext(context, IntPtr.Size);
        if (aggregateContext == IntPtr.Zero)
        {
            throw new OutOfMemoryException();
        }

        IntPtr handlePointer = Marshal.ReadIntPtr(aggregateContext);
        sqlite3_context wrapper;
        if (handlePointer == IntPtr.Zero)
        {
            wrapper = new sqlite3_context(context);
            GCHandle handle = GCHandle.Alloc(
                wrapper,
                GCHandleType.Normal);
            Marshal.WriteIntPtr(
                aggregateContext,
                GCHandle.ToIntPtr(handle));
        }
        else
        {
            wrapper = (sqlite3_context)GCHandle
                .FromIntPtr(handlePointer)
                .Target!;
            wrapper.SetNativeContext(context);
        }
        return wrapper;
    }

    private static Mem[] BuildNativeArguments(int argc, IntPtr argv)
    {
        var args = new Mem[argc];
        int pointerSize = IntPtr.Size;
        for (int i = 0; i < argc; i++)
        {
            IntPtr valuePointer = Marshal.ReadIntPtr(
                argv,
                i * pointerSize);
            args[i] = new Mem(
                new SQLitePCL.sqlite3_value(valuePointer));
        }
        return args;
    }

    private static void ResultNativeError(
        IntPtr context,
        string message)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(message);
        NativeResultError(
            context,
            encoded,
            encoded.Length,
            Transient);
    }

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_column_text",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text_ptr(IntPtr statement, int index);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_value_text",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_value_text_ptr(IntPtr value);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_create_window_function",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeCreateWindowFunction(
        IntPtr database,
        byte[] name,
        int argc,
        int flags,
        IntPtr userData,
        native_window_step step,
        native_window_final final,
        native_window_value value,
        native_window_inverse inverse,
        IntPtr destroy);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_user_data",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NativeUserData(IntPtr context);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_aggregate_context",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NativeAggregateContext(
        IntPtr context,
        int byteCount);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_result_null",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResultNull(IntPtr context);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_result_int64",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResultInt64(
        IntPtr context,
        long value);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_result_double",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResultDouble(
        IntPtr context,
        double value);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_result_text",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResultText(
        IntPtr context,
        byte[] value,
        int byteCount,
        IntPtr destructor);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_result_blob",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResultBlob(
        IntPtr context,
        byte[] value,
        int byteCount,
        IntPtr destructor);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_result_error",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResultError(
        IntPtr context,
        byte[] value,
        int byteCount,
        IntPtr destructor);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_result_error_toobig",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResultErrorTooBig(IntPtr context);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_result_error_nomem",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResultErrorNoMem(IntPtr context);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_trace_v2",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeTraceV2(
        IntPtr database,
        int traceMask,
        native_trace_callback callback,
        IntPtr userData);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_sql",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NativeSql(IntPtr statement);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_expanded_sql",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NativeExpandedSql(IntPtr statement);

    [DllImport(
        "e_sqlite3",
        EntryPoint = "sqlite3_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeFree(IntPtr value);

    private static IntPtr ResolveNativeLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "e_sqlite3", StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        string extension;
        if (OperatingSystem.IsWindows())
        {
            extension = ".dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            extension = ".dylib";
        }
        else
        {
            extension = ".so";
        }
        string fileName = (OperatingSystem.IsWindows() ? "" : "lib")
            + "e_sqlite3"
            + extension;

        string assemblyDirectory =
            Path.GetDirectoryName(typeof(Sqlite3).Assembly.Location)!;
        string runtimeIdentifier = GetRuntimeIdentifier();
        string[] candidates =
        [
            runtimeIdentifier is null
                ? string.Empty
                : Path.Combine(
                    assemblyDirectory,
                    "runtimes",
                    runtimeIdentifier,
                    "native",
                    fileName),
            Path.Combine(assemblyDirectory, fileName),
        ];

        foreach (string candidate in candidates)
        {
            if (candidate.Length > 0
                && File.Exists(candidate)
                && NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static string GetRuntimeIdentifier()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X86 => "x86",
            _ => string.Empty,
        };
        if (architecture.Length == 0)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return $"win-{architecture}";
        }
        if (OperatingSystem.IsMacOS())
        {
            return $"osx-{architecture}";
        }
        if (OperatingSystem.IsLinux())
        {
            string runtimeIdentifier =
                RuntimeInformation.RuntimeIdentifier;
            return runtimeIdentifier.Contains("musl", StringComparison.Ordinal)
                ? $"linux-musl-{architecture}"
                : $"linux-{architecture}";
        }

        return null;
    }
}
