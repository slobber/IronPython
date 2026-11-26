using System;
using System.Runtime.InteropServices;
using IronPython.SQLite;
using SQLitePCL;

namespace IronPython.NativeDependencySmoke;

internal static class Program
{
    private static int Main()
    {
        Batteries_V2.Init();
        string version = PythonSQLite.sqlite_version;
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException(
                "IronPython.SQLite did not report a native SQLite version.");
        }

        int rc = raw.sqlite3_open_v2(
            ":memory:",
            out sqlite3 database,
            raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE,
            null);
        if (rc != raw.SQLITE_OK)
        {
            throw new InvalidOperationException(
                $"sqlite3_open_v2 failed with error {rc}.");
        }

        try
        {
            rc = raw.sqlite3_exec(database, "create table probe(value integer)");
            if (rc != raw.SQLITE_OK)
            {
                throw new InvalidOperationException(
                    $"sqlite3_exec failed with error {rc}.");
            }
        }
        finally
        {
            database.Dispose();
        }

        Console.WriteLine(
            $"NATIVE_ASSETS_OK {RuntimeInformation.RuntimeIdentifier} " +
            $"SQLite {version}");
        return 0;
    }
}
