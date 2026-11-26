# CLR regression track

This track verifies that Python 3.15 migration work does not regress the
IronPython-specific CLR surface. It is intentionally separate from the CPython
conformance suites.

## Environment

Set:

```text
DOTNET_ROOT=<dotnet-root>
CYBERSTONE_IRONPYTHON_STDLIB=<IronPython.StdLib checkout>
IRONPYTHONPATH=<IronPython.StdLib checkout>/lib
```

Build:

```bash
dotnet build tests/IronPython.Tests/IronPython.Tests.csproj \
  -f net10.0 \
  -p:StdLibRoot="$CYBERSTONE_IRONPYTHON_STDLIB" \
  -p:EnableWindowsTargeting=false \
  --no-restore -v:minimal
```

## Hosting and interop

```bash
dotnet test tests/IronPython.Tests/IronPython.Tests.csproj \
  -f net10.0 \
  -p:StdLibRoot="$CYBERSTONE_IRONPYTHON_STDLIB" \
  -p:EnableWindowsTargeting=false \
  --no-build --filter "FullyQualifiedName~EngineTest"

ipy tests/suite/test_clrload.py
ipy tests/suite/test_clrtype.py
ipy tests/suite/test_clruse.py
ipy tests/suite/test_clrexception.py
```

`test_clrload` cases that require an unavailable C# compiler or GAC are
platform-gated by the test suite itself.

## Async CLR interop

```bash
dotnet test tests/IronPython.Tests/IronPython.Tests.csproj \
  -f net10.0 \
  -p:StdLibRoot="$CYBERSTONE_IRONPYTHON_STDLIB" \
  -p:EnableWindowsTargeting=false \
  --no-build --filter "FullyQualifiedName~CoroutineAsTaskTest"
```

The `Task`, `ValueTask`, `IAsyncEnumerable`, and `IAsyncDisposable` Python suites
remain in `tests/suite/test_async.py`.

## SQLite

`IronPython.SQLite` is a thin `_sqlite3` adapter over SQLitePCLRaw's
`bundle_e_sqlite3` package. The legacy managed `Community.CsharpSqlite` sources
are excluded from compilation.

```bash
dotnet build src/extensions/IronPython.SQLite/IronPython.SQLite.csproj \
  -f net10.0 \
  -p:StdLibRoot="$CYBERSTONE_IRONPYTHON_STDLIB" \
  -p:EnableWindowsTargeting=false \
  --no-restore -v:minimal
```

Load `IronPython.SQLite.dll`, import `_sqlite3`, and exercise a small
`sqlite3.connect()` CRUD round trip.

Run the complete database suite:

```bash
IRONPYTHONPATH="$CYBERSTONE_IRONPYTHON_STDLIB/lib" \
  dotnet bin/Debug/net10.0/ipy.dll -m unittest -q test.test_sqlite3
```

The suite requires SQLite's process-isolation cases to be waived while
`fork_exec` is unavailable.

## Platform notes

- WPF and WinForms checks are Windows-only and compile behind `FEATURE_WPF`.
- C# compiler-dependent interop fixtures run only where `csc` is available.
- GAC-only fixtures skip on .NET Core/Unix.

## WPF smoke test

Run this from Windows PowerShell after building the migration workspace. It
exercises the WPF module import, control construction on an STA thread, and the
`wpf.LoadComponent` XAML path for both supported Windows target frameworks.

```powershell
$env:CYBERSTONE_IRONPYTHON_STDLIB = "<IronPython.StdLib checkout>"

dotnet build tests/WpfSmoke/WpfSmoke.csproj -f net10.0-windows
dotnet build tests/WpfSmoke/WpfSmoke.csproj -f net11.0-windows

dotnet run --project tests/WpfSmoke/WpfSmoke.csproj -f net10.0-windows --no-build
dotnet run --project tests/WpfSmoke/WpfSmoke.csproj -f net11.0-windows --no-build
```

Each run must print `WPF_XAML_OK` and exit with code 0. The entry point must
remain marked with `[STAThread]`; WPF controls cannot be constructed from an MTA
thread.
