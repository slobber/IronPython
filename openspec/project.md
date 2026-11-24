# Project Context

## Purpose

CyberStone.IronPython is the private migration line of IronPython targeting
CPython 3.15.2 semantics while retaining .NET interoperability.

## Technology

- C# on `net10.0` and `net11.0`
- Roslyn incremental source generators for generated compiler infrastructure
- A separately maintained CPython 3.15.2 standard-library repository
- Windows x64/arm64, Linux x64/arm64, and macOS arm64

## Repository Layout

- Main repository: `E:\github\slobber\CyberStone.IronPython`
- Standard library repository: `E:\github\slobber\IronPython.StdLib`
- Upstream compatibility oracle: CPython `v3.15.2`

## Conventions

- Public namespaces, assembly names, and Python-specific names retain the
  `IronPython` prefix.
- NuGet package IDs use the `CyberStone.IronPython` prefix.
- Python language behavior is selected solely by the final 3.15 semantics.
  Historical CPython release behavior is used only for migration analysis.
- Generated parser artifacts are produced by the Roslyn source generator and
  are not committed.
- CPython implementation details outside the compatibility boundary are not
  emulated.
