CyberStone.IronPython 3
=======================
**There is still much that needs to be done to support Python 3.x. We are working on it, albeit slowly. We welcome all those who would like to help!**

[Official Website](https://ironpython.net)

IronPython is an open-source implementation of the Python programming language that is tightly integrated with .NET. IronPython can use .NET and Python libraries, and other .NET languages can use Python code just as easily.

CyberStone.IronPython targets CPython 3.15.2 semantics, including the
reorganized standard library, Unicode strings, t-strings, type parameters, and
lazy imports.


| **What?** | **Where?** |
| --------: | :------------: |
| **Windows/Linux/macOS Builds** | [![Build status](https://dotnet.visualstudio.com/IronLanguages/_apis/build/status/ironpython3)](https://dotnet.visualstudio.com/IronLanguages/_build/latest?definitionId=43) [![Github build status](https://github.com/IronLanguages/ironpython3/workflows/CI/badge.svg)](https://github.com/IronLanguages/ironpython3/actions?workflow=CI) |
| **Downloads** | [![NuGet](https://img.shields.io/nuget/vpre/CyberStone.IronPython.svg)](https://www.nuget.org/packages/CyberStone.IronPython/latest) |
| **Help** | [![Gitter chat](https://badges.gitter.im/IronLanguages/ironpython.svg)](https://gitter.im/IronLanguages/ironpython) [![StackExchange](https://img.shields.io/badge/stack%20overflow-ironpython-informational?logo=stack-overflow&logoColor=white)](https://stackoverflow.com/questions/tagged/ironpython) |


## Examples

To see how to use in PowerShell (either directly embedded or executable invocation), skip to [Installation](#Installation)

The following C# program:

```cs
using System.Windows.Forms;

MessageBox.Show("Hello World!", "Greetings", MessageBoxButtons.OKCancel);
```

can be written in IronPython as follows:

```py
import clr
clr.AddReference("System.Windows.Forms")
from System.Windows.Forms import MessageBox, MessageBoxButtons

MessageBox.Show("Hello World!", "Greetings", MessageBoxButtons.OKCancel)
```

Here is an example how to call Python code from a C# program.

```cs
var eng = IronPython.Hosting.Python.CreateEngine();
var scope = eng.CreateScope();
eng.Execute(@"
def greetings(name):
    return 'Hello ' + name.title() + '!'
", scope);
dynamic greetings = scope.GetVariable("greetings");
System.Console.WriteLine(greetings("world"));
```
This example assumes that `CyberStone.IronPython` has been added to the C#
project as a NuGet package.

## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

## State of the Project

This repository is migrating directly to final CPython 3.15.2 semantics. The
migration is in progress; the presence of Python 3.15 version metadata does not
yet mean that the interpreter is fully compatible.

- [OpenSpec change](openspec/changes/python-315-support/proposal.md)
- [Migration task list](openspec/changes/python-315-support/tasks.md)
- [Python 3.15 feature matrix](docs/python-3.15-migration-matrix.md)
- [Phase 0 baseline](docs/python-3.15-phase0-baseline.md)

## Contributing

For details on contributing see the [Contributing](CONTRIBUTING.md) article.

## Upgrading from IronPython 2

For details on upgrading from IronPython 2 to 3 see the [Upgrading from IronPython 2 to 3](https://github.com/IronLanguages/ironpython3/wiki/Upgrading-from-IronPython2) article.

## Differences with CPython

While compatibility with CPython is one of our main goals with IronPython 3, there are still some differences that may cause issues. See [Differences from CPython](https://github.com/IronLanguages/ironpython3/wiki/Differences-from-CPython) for details.

## Package compatibility

See the [Package compatibility](https://github.com/IronLanguages/ironpython3/wiki/Package-compatibility) document for information on compatibility with popular packages.

## Installation

Builds are produced as `.msi`, `.zip`, `.deb`, and `.pkg` packages. The engine
and console are distributed as `CyberStone.IronPython`,
`CyberStone.IronPython.StdLib`, and `CyberStone.IronPython.Console`.

### PowerShell

For usage in PowerShell, you can install using the Install-IronPython.ps1 within the aforementioned `.zip` file or by simply using this one-liner:

```pwsh
& ([scriptblock]::Create((iwr `
    -Uri 'https://raw.githubusercontent.com/IronLanguages/ironpython3/main/eng/scripts/Install-IronPython.ps1').Content)) `
    -Path "~/ipyenv/v3.15.2"

# Optionally, ensure pip:
# & "~/ipyenv/v3.15.2/ipy" -m ensurepip
```

Once installed, you can start using IronPython directly in PowerShell!

To use the ipy shim, you can use:
```pwsh
& "~/ipyenv/v3.15.2/Enter-IronPythonEnvironment.ps1"

ipy -c "print('Hello from IronPython!')"
```

... or to use IronPython embedded in PowerShell, you can use:
```pwsh
Import-Module "~/ipyenv/v3.15.2/IronPython.dll"

$engine = & {
    $engine = [IronPython.Hosting.Python]::CreateEngine()

    # You need to add the correct paths, as IronPython will use PowerShell's installation path by default
    $paths = $engine.GetSearchPaths()
    $paths.Add("$(Resolve-Path "~/ipyenv/v3.15.2/lib")")
    $paths.Add("$(Resolve-Path "~/ipyenv/v3.15.2/lib/site-packages")")

    # To use `wpf` and `sqlite3` you have to add the DLLs search path
    # - the [IronPython.SQLite] and [IronPython.WPF] powershell namespaces will become available on python import
    $paths.Add("$(Resolve-Path "~/ipyenv/v3.15.2/DLLs")")

    # or if you prefer to have the powershell namespaces early, you can use:
    # - just note, you will have to initialize _sqlite3 (see further down the script)
    # Import-Module "~/ipyenv/v3.15.2/DLLs/IronPython.SQLite.dll"
    # Import-Module "~/ipyenv/v3.15.2/DLLs/IronPython.WPF.dll"

    $engine.SetSearchPaths($paths)

    # Then have fun!
    $engine.Execute("print('Hello from IronPython!')")

    # Optionally, if you need to initialize _sqlite3:
    # $engine.Execute("import sqlite3")

    return $engine
}
```

## Build

See the [building document](https://github.com/IronLanguages/ironpython3/wiki/Building). Since the main development is on Windows, bugs on other platforms may inadvertently be introduced - please report them!

## Supported Platforms

CyberStone.IronPython targets .NET 10 and later on:

- Windows x64 and arm64
- Linux x64 and arm64
- macOS arm64

See [Python 3.15 compatibility](docs/python-3.15-compatibility.md) for version
field semantics, the unsupported CPython C-extension boundary, and the
NumSharp/.NET numerical path.

.NET Framework, .NET 8/9, x86, and Intel macOS are not supported targets for
the Python 3.15 line.
