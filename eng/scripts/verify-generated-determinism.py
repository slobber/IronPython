#!/usr/bin/env python3

"""Verify byte-for-byte generated source determinism across rebuilds."""

from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys


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


def build(root: Path, stdlib: Path, dotnet: str, framework: str, output: Path) -> None:
    if output.exists():
        shutil.rmtree(output)
    command = [
        dotnet,
        "build",
        "src/core/IronPython/IronPython.csproj",
        "-f",
        framework,
        "-t:Rebuild",
        f"-p:StdLibRoot={stdlib}",
        "-p:EnableWindowsTargeting=false",
        "-p:EmitCompilerGeneratedFiles=true",
        f"-p:CompilerGeneratedFilesOutputPath={output}",
        "-v:minimal",
    ]
    print("+", " ".join(command), flush=True)
    subprocess.run(command, cwd=root, check=True)


def hashes(output: Path) -> dict[str, str]:
    result = {}
    for path in sorted(output.rglob("*.cs")):
        relative = path.relative_to(output).as_posix()
        result[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--framework", default="net10.0")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[2]
    stdlib = locate_stdlib(root)
    first = root / "artifacts" / "generated-determinism" / args.framework / "first"
    second = root / "artifacts" / "generated-determinism" / args.framework / "second"

    build(root, stdlib, args.dotnet, args.framework, first)
    build(root, stdlib, args.dotnet, args.framework, second)

    first_hashes = hashes(first)
    second_hashes = hashes(second)
    if not first_hashes:
        raise SystemExit("No generated C# files were emitted.")
    if first_hashes != second_hashes:
        changed = sorted(
            path
            for path in first_hashes.keys() | second_hashes.keys()
            if first_hashes.get(path) != second_hashes.get(path)
        )
        raise SystemExit(
            "Generated output is not deterministic: " + ", ".join(changed)
        )

    print(
        f"GENERATED_DETERMINISTIC {args.framework}: "
        f"{len(first_hashes)} files"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
