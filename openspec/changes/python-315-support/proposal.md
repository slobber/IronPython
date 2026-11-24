# Proposal: Upgrade IronPython to Python 3.15.2 semantics

## Why

The current repository still presents Python 3.4 as its formal target and uses a
hand-written parser based on the 3.4 grammar. Later language and standard
library behavior has been added selectively, which creates an unclear support
contract and prevents reliable use of modern Python syntax and runtime APIs.

The project will converge directly on the final CPython 3.15.2 behavior rather
than replaying the intermediate behavior of each release.

## What Changes

- Replace the hand-written parser with a C# incremental source generator that
  reads a CPython 3.15 grammar and emits parser code.
- Replace the tokenizer with a 3.15-compatible scanner.
- Introduce a generated public AST matching `Python.asdl`, with a separate
  lowering stage into the existing compiler AST.
- Keep the existing `IronPython.Compiler.Parser` facade during migration.
- Pin the grammar, AST schema, standard library, and conformance tests to
  CPython `v3.15.2`.
- Support only `net10.0` and `net11.0` on Windows/Linux x64 and arm64 and on
  macOS arm64.
- Decouple `sys.version_info` from the product assembly version.
- Rename NuGet packages to the `CyberStone.IronPython` family.
- Maintain the standard library in the sibling
  `IronPython.StdLib` repository.
- Preserve IronPython-specific CLR interop and extension modules.

## Compatibility Boundary

In scope:

- Python language syntax and runtime semantics
- Public `ast`, `compile`, `inspect`, and typing behavior
- Public standard-library APIs
- CPython 3.15.2 public test suites applicable to this runtime
- .NET interoperability

Out of scope:

- CPython C API/ABI or binary C-extension loading
- CPython GIL/free-thread scheduling, object layout, refcount timing, GC timing,
  interpreter stack layout, and performance parity
- NumPy and Matplotlib in-process compatibility
- Universal PyPI ecosystem compatibility
- CPython JIT and bytecode implementation details

## Impact

- The compiler front end is effectively rebuilt.
- `sys.version_info` becomes `(3, 15, 2, "final", 0)` independently of the
  IronPython distribution version.
- The obsolete .NET Framework, .NET 8/9, x86, and Intel macOS targets are removed
  from product support.
- Existing parser and AST tests become migration regression tests, not the
  source of truth.
- Standard-library modules removed by CPython no longer ship by default.

## Success Criteria

- A generated 3.15 parser runs behind the existing parser facade.
- Every applicable CPython 3.15.2 language and standard-library test passes.
- Differential tests against CPython 3.15.2 have zero unexplained differences.
- The required platform/architecture matrix builds, tests, and packages
  successfully.
- Only one published language semantic version remains: Python 3.15.2.
