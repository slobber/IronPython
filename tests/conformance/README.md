# Python 3.15 Conformance

The conformance runner uses CPython `v3.15.2` as the behavioral oracle and the
CPython standard-library tests as the primary release gate.

## Test Categories

- `language`: tokenizer, grammar, AST, compiler, and runtime semantics
- `stdlib`: public standard-library behavior
- `typing`: annotations, typing, inspect, and annotationlib
- `async`: coroutines, generators, tasks, context variables, and exception groups
- `native-module`: IronPython-managed substitutes for native-backed stdlib modules
- `interop`: IronPython/.NET integration, excluded from CPython parity

## Approved Waiver Categories

- `platform-unavailable`: the operating system does not provide the capability
- `cpython-c-api`: behavior requires CPython's C API or extension ABI
- `cpython-internal`: behavior depends on CPython bytecode, object layout,
  refcount timing, GC timing, interpreter stack layout, or scheduling internals
- `stress-resource`: test requires unavailable memory, process, or time budgets
- `product-boundary`: an explicit scope decision excludes the feature

Every waiver must include an owner, reason, linked issue, and review date.
Language, AST, compiler, protocol, and pure-Python stdlib tests should not use a
general waiver.

Approved waivers are recorded in `waivers.json`.

Validate waiver metadata before release:

```text
python3 eng/scripts/verify-waivers.py
```

The audit checks category, owner, linked issue, reason, review date, and
duplicate test patterns.

## Release Gate

- Zero unexplained failures
- Zero unexplained skips
- Deterministic source-generator output
- `net10.0` and `net11.0` pass
- Windows/Linux x64 and arm64 pass
- macOS arm64 passes

## Differential Runner

`differential_runner.py` runs each JSON case through CPython and IronPython,
comparing exit status, normalized stdout/stderr, and an AST dump that includes
all four source-position fields.

```text
python tests/conformance/differential_runner.py \
  --cpython /path/to/python3.15 \
  --ironpython "dotnet bin/Release/net10.0/ipy.dll" \
  --cases tests/conformance/ast_cases.json \
  --classifications tests/conformance/classifications.json \
  --report artifacts/differential-report.json
```

Any non-empty difference must have a case classification of
`intentional-evolution` or `bug` and list the accepted comparison fields.
Unclassified failures return a non-zero exit code.

`ast_cases.json` is the exact-match AST gate. `legacy_cases.json` keeps a
3.4-compatible runtime corpus for migration diagnostics. Run the latter with
`classifications.json`; AST schema evolution is allowed there but runtime
output still has to match.
