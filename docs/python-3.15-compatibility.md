# Python 3.15 compatibility

CyberStone.IronPython targets CPython 3.15.2 language and standard-library
semantics on supported .NET runtimes.

## Version fields

The two version fields intentionally describe different things:

- `sys.version_info` is the Python language version:
  `(3, 15, 2, "final", 0)`.
- `sys.implementation.version` is the IronPython product version. During
  development this is `3.15.2-alpha1`.

Tooling that selects Python syntax or standard-library behavior should use
`sys.version_info`. Tooling that reports the implementation build should use
`sys.implementation.version`.

## C extensions

CPython C extension modules are not supported. The CPython C API, extension ABI,
reference-count timing, GIL implementation, and CPython object layouts are
outside the product boundary.

Pure-Python packages remain supported when they only depend on Python-level
interfaces available in IronPython and its standard library. Packages with
compiled extension modules need a managed replacement or a .NET interop layer.

## Numerical packages

NumPy, SciPy, and Matplotlib are not supported as CPython extension modules.
The supported numerical path for managed code is NumSharp together with direct
.NET interop. NumSharp is not a drop-in implementation of every NumPy API or
dtype behavior; tests must target the NumSharp API that an application uses.

## Supported products

- .NET 10 and .NET 11
- Windows x64 and arm64
- Linux x64 and arm64
- macOS arm64

x86, macOS x64, .NET Framework, and older .NET product targets are not
supported.
