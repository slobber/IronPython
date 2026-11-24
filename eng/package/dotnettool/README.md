IronPython Console
==================

IronPython is an open-source implementation of the Python programming language
that is tightly integrated with .NET 10 and later. IronPython can use .NET and
Python libraries, and other .NET languages can use Python code just as easily.

This package contains a standalone Python interpreter, invokable from the command line as `ipy`. It also includes the Python Standard Library released by the Python project, but slightly modified to work better with IronPython and .NET.

This package is part of the in-progress migration to CPython 3.15.2 semantics.
Compatibility is not claimed until the OpenSpec conformance gate is complete.

## Differences with CPython

While compatibility with CPython is one of our main goals with IronPython 3, there are still some differences that may cause issues. See [Differences from CPython](https://github.com/IronLanguages/ironpython3/wiki/Differences-from-CPython) for details.

## Package compatibility

See the [Package compatibility](https://github.com/IronLanguages/ironpython3/wiki/Package-compatibility) document for information on compatibility with popular Python packages.
