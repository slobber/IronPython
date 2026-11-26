#!/usr/bin/env python3

"""Publish IronPython.SQLite for the supported native RID matrix."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys


DEFAULT_RIDS = (
    "win-x64",
    "win-arm64",
    "linux-x64",
    "linux-arm64",
    "osx-arm64",
)

EXPECTED_ASSETS = {
    "win-x64": ("e_sqlite3.dll",),
    "win-arm64": ("e_sqlite3.dll",),
    "linux-x64": ("libe_sqlite3.so", "libMono.Unix.so"),
    "linux-arm64": ("libe_sqlite3.so", "libMono.Unix.so"),
    "osx-arm64": ("libe_sqlite3.dylib", "libMono.Unix.dylib"),
}

REQUIRED_MANAGED_ASSETS = (
    "IronPython.SQLite.dll",
    "SQLitePCLRaw.batteries_v2.dll",
    "SQLitePCLRaw.core.dll",
    "SQLitePCLRaw.provider.e_sqlite3.dll",
)

ARCHITECTURE_MARKERS = {
    "win-x64": "x86-64",
    "win-arm64": "aarch64",
    "linux-x64": "x86-64",
    "linux-arm64": "aarch64",
    "osx-arm64": "arm64",
}


def run(command: list[str], root: Path) -> None:
    print("+", " ".join(command), flush=True)
    subprocess.run(command, cwd=root, check=True)


def locate_stdlib(root: Path) -> Path:
    configured = os.environ.get("CYBERSTONE_IRONPYTHON_STDLIB")
    candidates = [
        Path(configured) if configured else None,
        root.parent / "IronPython.StdLib",
        root / "src" / "core" / "IronPython.StdLib",
    ]
    for candidate in candidates:
        if candidate is not None and (candidate / "lib").is_dir():
            return candidate.resolve()
    raise SystemExit(
        "Set CYBERSTONE_IRONPYTHON_STDLIB to an IronPython.StdLib checkout."
    )


def verify_output(output: Path, rid: str) -> None:
    expected = EXPECTED_ASSETS[rid] + REQUIRED_MANAGED_ASSETS
    missing = [
        name
        for name in expected
        if not (output / name).is_file()
        or (output / name).stat().st_size == 0
    ]
    if missing:
        raise SystemExit(
            f"{rid}: missing native or managed assets: {', '.join(missing)}"
        )
    if shutil.which("file"):
        marker = ARCHITECTURE_MARKERS[rid].casefold()
        for asset in EXPECTED_ASSETS[rid]:
            description = subprocess.check_output(
                ["file", str(output / asset)],
                text=True,
            ).casefold()
            if marker not in description:
                raise SystemExit(
                    f"{rid}: {asset} does not have the expected "
                    f"{ARCHITECTURE_MARKERS[rid]} architecture"
                )
    print(f"NATIVE_MATRIX_OK {rid}: {', '.join(expected)}", flush=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--framework", default="net10.0")
    parser.add_argument(
        "--rid",
        action="append",
        choices=DEFAULT_RIDS,
        dest="rids",
    )
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[2]
    stdlib = locate_stdlib(root)
    rids = tuple(args.rids or DEFAULT_RIDS)

    for rid in rids:
        output = root / "artifacts" / "native-matrix" / args.framework / rid
        if output.exists():
            shutil.rmtree(output)
        run(
            [
                args.dotnet,
                "publish",
                "src/extensions/IronPython.SQLite/IronPython.SQLite.csproj",
                "-f",
                args.framework,
                "-r",
                rid,
                "--self-contained",
                "false",
                f"-p:StdLibRoot={stdlib}",
                "-p:EnableWindowsTargeting=false",
                "-o",
                str(output),
                "-v:minimal",
            ],
            root,
        )
        verify_output(output, rid)

    return 0


if __name__ == "__main__":
    sys.exit(main())
