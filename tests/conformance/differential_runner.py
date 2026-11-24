import argparse
import json
import os
import shlex
import subprocess
import sys
import tempfile


AST_HELPER = r"""
import ast
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source_file:
    source = source_file.read()
tree = ast.parse(source, filename=sys.argv[1])
print(json.dumps(ast.dump(tree, include_attributes=True), sort_keys=True))
"""


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cpython", required=True)
    parser.add_argument("--ironpython", required=True)
    parser.add_argument("--cases", required=True)
    parser.add_argument("--classifications")
    parser.add_argument("--report")
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--skip-ast", action="store_true")
    return parser.parse_args()


def split_command(command):
    return shlex.split(command, posix=os.name != "nt")


def normalize(value):
    if os.name == "nt":
        return value.replace("\r\n", "\n")
    return value


def run_command(command, source, timeout):
    process = subprocess.run(
        command,
        input=source,
        text=True,
        capture_output=True,
        timeout=timeout,
    )
    return {
        "exit": process.returncode,
        "stdout": normalize(process.stdout),
        "stderr": normalize(process.stderr),
    }


def run_case(cpython, ironpython, case, timeout, include_ast):
    source = case["source"]
    suffix = ".py"
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", suffix=suffix, delete=False) as handle:
        handle.write(source)
        path = handle.name
    try:
        cpython_result = run_command(cpython + [path], None, timeout)
        ironpython_result = run_command(ironpython + [path], None, timeout)

        result = {
            "name": case["name"],
            "category": case.get("category", "language"),
            "cpython": cpython_result,
            "ironpython": ironpython_result,
            "differences": [],
        }
        for field in ("exit", "stdout", "stderr"):
            if cpython_result[field] != ironpython_result[field]:
                result["differences"].append(field)

        if include_ast:
            cpython_ast = run_command(cpython + ["-c", AST_HELPER, path], None, timeout)
            ironpython_ast = run_command(ironpython + ["-c", AST_HELPER, path], None, timeout)
            result["cpython_ast"] = cpython_ast
            result["ironpython_ast"] = ironpython_ast
            if cpython_ast != ironpython_ast:
                result["differences"].append("ast")
    finally:
        os.unlink(path)

    return result


def main():
    args = parse_args()
    cpython = split_command(args.cpython)
    ironpython = split_command(args.ironpython)
    with open(args.cases, encoding="utf-8") as handle:
        cases = json.load(handle)

    classifications = {}
    if args.classifications:
        with open(args.classifications, encoding="utf-8") as handle:
            classifications = json.load(handle)

    results = [
        run_case(cpython, ironpython, case, args.timeout, not args.skip_ast)
        for case in cases
    ]
    unresolved = []
    for result in results:
        expected = classifications.get(result["name"], {})
        accepted = set(expected.get("acceptedDifferences", []))
        result["classification"] = expected.get("classification")
        if result["differences"]:
            unexpected = sorted(set(result["differences"]) - accepted)
            if unexpected or result["classification"] not in {"intentional-evolution", "bug"}:
                unresolved.append({**result, "unexpectedDifferences": unexpected})

    report = {
        "total": len(results),
        "passed": sum(not result["differences"] for result in results),
        "failed": len(unresolved),
        "classificationsUsed": sum(
            result["classification"] is not None for result in results
        ),
        "results": results,
    }
    if args.report:
        with open(args.report, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2, sort_keys=True)
            handle.write("\n")
    else:
        print(json.dumps(report, indent=2, sort_keys=True))

    return 1 if unresolved else 0


if __name__ == "__main__":
    sys.exit(main())
