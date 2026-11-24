# Python 3.15 Phase 0 Baseline

Date: 2026-11-24

## Repository State

- Main repository: `E:\github\slobber\CyberStone.IronPython`
- Standard library repository: `E:\github\slobber\IronPython.StdLib`
- Source upstream: IronPython `main` at `0a40030e`
- Standard-library branch: `python-3.15`
- CPython oracle: `v3.15.2`

## Local Environment

- Windows x64
- .NET SDK `11.0.100`
- .NET 10 runtime `10.0.15`
- .NET 11 runtime `11.0.0`

## Current Build Result

The following build succeeds:

```powershell
pwsh make.ps1 -frameworks net10.0 build
```

The solution builds:

- `IronPython`, modules, console, compiler, SQLite, WPF/window host,
  tests, and DLR references for `net10.0` and `net11.0`
- Source Generator and Analyzer plugins for `netstandard2.0`
- The new parser-generator core, source-generator adapter, and generated
  minimal parser

The generated parser slice test passes for `net10.0` and `net11.0`.

## Version Smoke Test

`-VV` reports:

```text
IronPython 3.15.2a1 (3.15.2.0001)
[.NETCoreApp,Version=v10.0 on .NET 10.0.15 (64-bit)]
```

Python code reports:

```text
sys.version_info(major=3, minor=15, micro=2, releaselevel='final', serial=0)
sys.version_info(major=3, minor=15, micro=2, releaselevel='alpha', serial=1)
3.15.2 (IronPython 3.15.2a1, 3.15.2.0001)
```

The first value is `sys.version_info`; the second is
`sys.implementation.version`.

## Known Blockers

- The stdlib fork now carries temporary bootstrap patches for `site.py` so the
  legacy parser can load it before lazy imports and bracketless `except` are
  implemented.
- The 3.15 import bootstrap now reaches normal stdlib imports rather than
  failing in `_bootstrap_external`.
- Running `ipy` without `-S` currently reaches the 3.15 `traceback.py` and is
  blocked by the legacy parser's lack of structural pattern matching.
- Temporary stdlib bootstrap shims cover generic aliases, union types,
  `frozendict`, and `sentinel` only enough for startup investigation; they are
  not conformance implementations.
- Additional 3.15 `_imp`, `sys`, and path-module interfaces are present, but
  will need full semantic coverage as conformance tests are enabled.
- `ipy -S -c "print(1 + 2)"` succeeds.

## Pre-Migration Test Observations

An incomplete baseline run against the original 3.4 stdlib exposed existing
failures unrelated to the Phase 0 configuration changes, including:

- missing `cp936` codec in several subprocess tests
- socket server detection timeouts
- `_socket.gethostbyname_ex` platform/network expectation failure
- intermittent temporary test files

The complete test baseline must be regenerated once the 3.15 parser, importlib
bootstrap, and conformance manifest are available.
