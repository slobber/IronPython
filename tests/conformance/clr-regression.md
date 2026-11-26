# CLR regression track

This track verifies that Python 3.15 migration work does not regress the
IronPython-specific CLR surface. It is intentionally separate from the CPython
conformance suites.

## Environment

Set:

```text
DOTNET_ROOT=<dotnet-root>
CYBERSTONE_IRONPYTHON_STDLIB=<IronPython.StdLib checkout>
CYBERSTONE_IRONPYTHON_GENERATED_PARSER=1
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

```bash
dotnet build src/extensions/IronPython.SQLite/IronPython.SQLite.csproj \
  -f net10.0 \
  -p:StdLibRoot="$CYBERSTONE_IRONPYTHON_STDLIB" \
  -p:EnableWindowsTargeting=false \
  --no-restore -v:minimal
```

Load `IronPython.SQLite.dll`, import `_sqlite3`, and exercise a small
`sqlite3.connect()` CRUD round trip.

## Platform notes

- WPF and WinForms checks are Windows-only and compile behind `FEATURE_WPF`.
- C# compiler-dependent interop fixtures run only where `csc` is available.
- GAC-only fixtures skip on .NET Core/Unix.
