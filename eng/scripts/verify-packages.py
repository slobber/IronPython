#!/usr/bin/env python3

"""Consume the built IronPython packages using only their published layout."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile


PROJECT = "CyberStone.IronPython"
STDLIB = "CyberStone.IronPython.StdLib"
CONSOLE = "CyberStone.IronPython.Console"

CONSUMER_PROGRAM = '''
using IronPython.Hosting;
using IronPython.SQLite;
using Microsoft.Scripting.Hosting;

ScriptEngine engine = Python.CreateEngine();
engine.SetSearchPaths(new[] { Path.Combine(AppContext.BaseDirectory, "lib") });
engine.Runtime.LoadAssembly(typeof(PythonSQLite).Assembly);
ScriptScope scope = engine.CreateScope();
engine.Execute("""
import shutil
import sqlite3
from compression import zstd

connection = sqlite3.connect(":memory:")
connection.execute("create table items(id integer primary key, name text)")
connection.execute("insert into items(name) values (?)", ("IronPython",))
assert connection.execute("select name from items").fetchone() == ("IronPython",)
connection.close()

payload = b"CyberStone.IronPython 3.15.2" * 128
assert zstd.decompress(zstd.compress(payload)) == payload
""", scope);
Console.WriteLine("PACKAGE_CONSUMER_OK");
'''

CONSUMER_PROJECT = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>__FRAMEWORK__</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CyberStone.IronPython" Version="__VERSION__" />
    <PackageReference Include="CyberStone.IronPython.StdLib" Version="__VERSION__" />
  </ItemGroup>
</Project>
"""

NUGET_CONFIG = """
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="__PACKAGE_DIR__" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"""


def run(command: list[str], cwd: Path, env: dict[str, str] | None = None) -> None:
    print("+", " ".join(command), flush=True)
    subprocess.run(command, cwd=cwd, env=env, check=True)


def verify_nupkg_contents(package_dir: Path, version: str, framework: str) -> None:
    package = package_dir / f"{PROJECT}.{version}.nupkg"
    with zipfile.ZipFile(package) as archive:
        names = set(archive.namelist())
    required = {
        f"lib/{framework}/IronPython.dll",
        f"lib/{framework}/IronPython.Compiler.Syntax.dll",
        f"lib/{framework}/IronPython.Modules.dll",
        f"lib/{framework}/IronPython.SQLite.dll",
        f"lib/{framework}/Microsoft.Dynamic.dll",
        f"lib/{framework}/Microsoft.Scripting.dll",
    }
    missing = sorted(required - names)
    if missing:
        raise SystemExit(f"{package.name} is missing: {', '.join(missing)}")


def verify_consumer(
    package_dir: Path,
    version: str,
    framework: str,
    dotnet: str,
) -> None:
    with tempfile.TemporaryDirectory(prefix="ironpython-package-consumer-") as temp:
        root = Path(temp)
        (root / "Program.cs").write_text(CONSUMER_PROGRAM, encoding="utf-8")
        (root / "consumer.csproj").write_text(
            CONSUMER_PROJECT
            .replace("__VERSION__", version)
            .replace("__FRAMEWORK__", framework),
            encoding="utf-8",
        )
        (root / "NuGet.Config").write_text(
            NUGET_CONFIG.replace("__PACKAGE_DIR__", package_dir.as_posix()).strip() + "\n",
            encoding="utf-8",
        )
        env = os.environ.copy()
        env["NUGET_PACKAGES"] = str(root / ".nuget" / "packages")
        run([dotnet, "run", "-c", "Release"], root, env)


def verify_tool(package_dir: Path, version: str, dotnet: str) -> None:
    with tempfile.TemporaryDirectory(prefix="ironpython-tool-") as temp:
        root = Path(temp)
        env = os.environ.copy()
        env["NUGET_PACKAGES"] = str(root / ".nuget" / "packages")
        run(
            [
                dotnet,
                "tool",
                "install",
                CONSOLE,
                "--tool-path",
                str(root),
                "--add-source",
                str(package_dir),
                "--version",
                version,
                "--ignore-failed-sources",
                "--no-cache",
            ],
            root,
            env,
        )
        executable = root / ("ipy.exe" if os.name == "nt" else "ipy")
        run(
            [
                str(executable),
                "-c",
                "import shutil, sqlite3; "
                "from compression import zstd; "
                "assert zstd.decompress(zstd.compress(b'ok')) == b'ok'; "
                "assert sqlite3.connect(':memory:').execute('select 1').fetchone() == (1,); "
                "print('CONSOLE_TOOL_OK')",
            ],
            root,
            env,
        )


def verify_zip(package_dir: Path, version: str, dotnet: str, framework: str) -> None:
    package = package_dir / f"{PROJECT}.{version}.zip"
    with tempfile.TemporaryDirectory(prefix="ironpython-zip-") as temp:
        root = Path(temp)
        with zipfile.ZipFile(package) as archive:
            archive.extractall(root)
        app = root / framework
        required = [
            app / "ipy.dll",
            root / "lib",
            app / "DLLs" / "IronPython.SQLite.dll",
        ]
        missing = [str(path.relative_to(root)) for path in required if not path.exists()]
        if missing:
            raise SystemExit(f"{package.name} is missing: {', '.join(missing)}")
        shutil.copytree(root / "lib", app / "lib")
        run(
            [
                dotnet,
                str(app / "ipy.dll"),
                "-c",
                "import sqlite3; print(sqlite3.sqlite_version)",
            ],
            app,
        )


def verify_deb(package_dir: Path, version: str, dotnet: str) -> None:
    package = package_dir / f"cyberstone-ironpython_{version}.deb"
    if not shutil.which("dpkg-deb"):
        print(f"SKIP {package.name}: dpkg-deb is unavailable", flush=True)
        return
    with tempfile.TemporaryDirectory(prefix="ironpython-deb-") as temp:
        root = Path(temp)
        run(["dpkg-deb", "-x", str(package), str(root)], root)
        app = root / "usr" / "share" / "cyberstone-ironpython3.15"
        env = os.environ.copy()
        env["IRONPYTHONPATH"] = str(root / "usr" / "lib" / "cyberstone-ironpython3.15")
        run([dotnet, str(app / "ipy.dll"), "-c", "import sqlite3; print(sqlite3.sqlite_version)"], app, env)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package-dir", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--framework", default="net10.0")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()

    package_dir = Path(args.package_dir).resolve()
    verify_nupkg_contents(package_dir, args.version, args.framework)
    verify_consumer(package_dir, args.version, args.framework, args.dotnet)
    verify_tool(package_dir, args.version, args.dotnet)
    verify_zip(package_dir, args.version, args.dotnet, args.framework)
    if sys.platform.startswith("linux"):
        verify_deb(package_dir, args.version, args.dotnet)
    print("PACKAGE_MATRIX_OK", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
