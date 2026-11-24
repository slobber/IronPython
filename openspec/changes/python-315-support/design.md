# Design: Python 3.15 compiler front end and runtime baseline

## Design Decisions

### D001: Final-state compatibility

The implementation targets CPython 3.15.2 behavior directly. CPython release
history is represented in a traceability matrix but does not determine
implementation order.

### D002: One semantic target

The product exposes only Python 3.15 semantics. Temporary internal parser
selection switches are permitted during migration and must be removed at
cutover.

### D003: Pinned baseline

Grammar, ASDL, standard library, and tests are pinned to CPython `v3.15.2`.
Maintenance updates follow explicit changes against the CPython `3.15` branch.

### D004: Generated C# parser

A dedicated netstandard2.0 Roslyn incremental generator reads grammar inputs
from `AdditionalFiles`, validates them, and emits parser code into the
compilation. Generator architecture:

1. grammar model and validation library
2. deterministic C# emitter
3. source-generator adapter
4. net10 CLI for inspection and migration debugging

The generator targets netstandard2.0 because it executes in the compiler
process. This is a tooling constraint, not a product support target.

### D005: Upstream grammar with external actions

The grammar remains structurally comparable to CPython's `python.gram`.
Semantic actions are externalized to typed C# mappings keyed by rule and
alternative identity. Missing, duplicate, stale, or unreferenced actions are
build errors.

### D006: Hand-written tokenizer

The tokenizer is a dedicated state machine rather than generated parser output.
It supports indentation, encoding declarations, soft keywords, exact source
spans, incomplete interactive input, and nested f-string/t-string tokens.

### D007: Two-layer AST

- Generated public AST: mirrors CPython 3.15 `Python.asdl`, including
  `lineno`, `col_offset`, `end_lineno`, and `end_col_offset`.
- Compiler AST: retains binding and DLR lowering responsibilities.

The generated parser emits the public AST. A lowering adapter converts it into
the compiler AST.

### D008: Stable parser facade

The existing `IronPython.Compiler.Parser` class remains the migration seam.
Its public entry points, `ParserSink` behavior, and interactive parsing
contract remain stable while its internals are replaced.

### D009: Side-by-side front end, single final implementation

The new tokenizer, parser runtime, generated parser, public AST, and diagnostics
live in new projects. The old parser remains available only while migration is
in progress. After parity, the old implementation and transition switch are
deleted.

### D010: Differential migration oracle

For accepted 3.4-compatible inputs, compare:

1. old parser behavior
2. CPython 3.4.10 behavior
3. new parser behavior

CPython decides correct behavior. The old parser only helps localize regressions.
All differences are classified as an intentional post-3.4 change or a bug.

### D011: Runtime implementation boundary

Public Python APIs must work, but CPython's implementation mechanics are not
emulated. IronPython uses .NET GC, tasks, threads, and platform services.

### D012: Standard library ownership

The standard library is maintained in the sibling repository
`IronPython.StdLib`. Main-repository builds consume a pinned revision
from that repository. Local Python patches are explicit and reviewable.

### D013: Platform baseline

- `net10.0` and `net11.0`
- Windows x64 and arm64
- Linux x64 and arm64
- macOS arm64

No x86 and no Intel macOS product support.

### D014: Version and package identity

- Python language version metadata and .NET product version are separate.
- `sys.version_info` reports `3.15.2 final`.
- `sys.implementation.version` reports the CyberStone.IronPython product version.
- Main NuGet package: `CyberStone.IronPython`
- Standard-library package: `CyberStone.IronPython.StdLib`
- Console tool package: `CyberStone.IronPython.Console`
- Namespaces and assembly names retain `IronPython`.

## Target Architecture

```text
Source
  -> hand-written tokenizer
  -> generated PEG parser
  -> generated public AST
  -> lowering/binding
  -> existing compiler AST and DLR lowering
  -> .NET execution

Grammar + ASDL
  -> grammar model and validation
  -> C# emitter
  -> Roslyn incremental generator
  -> generated parser/public AST in obj/
```

The public seam exposed to existing callers is:

```text
IronPython.Compiler.Parser
  CreateParser(...)
  ParseFile(...)
  ParseInteractiveCode(...)
```

## Dependency Graph

The critical path is:

1. TFM/version/build baseline
2. standard-library pin
3. parser generator and generated parser vertical slice
4. complete 3.15 tokenizer, grammar, public AST, and diagnostics
5. annotation/type-parameter model
6. exception-group and async runtime model
7. import system and remaining object-model changes
8. standard-library and native-module implementations
9. conformance and packaging closure

## Testing Strategy

- Interface tests for the source generator: deterministic output, diagnostics,
  incremental invalidation, and grammar coverage.
- Parser facade tests: parse modes, error recovery, incomplete input, source
  spans, and AST shape.
- CPython 3.15.2 conformance tests: primary release gate.
- Differential tests against pinned CPython 3.15.2: behavior localization.
- Legacy 3.4 corpus: migration diagnostics only.
- IronPython integration tests: CLR interop, hosting, debugging, and extensions.
- Platform matrix: Windows/Linux x64+arm64 and macOS arm64.

## Completion Rules

- No unclassified failing or skipped in-scope CPython test.
- Every waiver records category, owner, reason, linked issue, and review date.
- Generated output must be deterministic.
- Both `net10.0` and `net11.0` pass on every required platform.
- The old parser and all obsolete TFM/package paths are removed.

## Risks

- PEG left recursion and error recovery may be under-tested by ordinary valid
  syntax tests.
- Public AST lowering can become a second implicit parser if not kept narrow.
- Annotation semantics span parser, scopes, object metadata, typing, inspect,
  pickle, and module import behavior.
- Exception groups can leak into generator, async, traceback, and debugger
  contracts.
- Standard-library native replacements vary by platform and architecture.
- Roslyn generator debugging and incremental invalidation require dedicated
  tests.
- A long-lived parser switch can become a permanent dual implementation if the
  cutover tasks are not enforced.
