# Tasks

## 1. Repository and decision baseline

- [x] 1.1 Create the main repository at
  `E:\github\slobber\CyberStone.IronPython`.
- [x] 1.2 Create the standard-library repository at
  `E:\github\slobber\IronPython.StdLib`.
- [x] 1.3 Create the standard-library `python-3.15` working branch.
- [x] 1.4 Record the CPython 3.15.2 feature matrix in
  `docs/python-3.15-migration-matrix.md`.
- [x] 1.5 Create the OpenSpec proposal, design, tasks, and capability specs.
- [ ] 1.6 Add CyberStone GitHub remotes after the remote repositories exist.
- [x] 1.7 Record the initial `net10.0` build and test baseline, including
  pre-existing failures.

## 2. Version, platform, and build baseline

- [x] 2.1 Split product version from Python language version metadata.
- [x] 2.2 Set the Python language baseline to
  `(3, 15, 2, "final", 0)`.
- [x] 2.3 Make `sys.version_info` use the Python language version.
- [x] 2.4 Make `sys.implementation.version` use the product version.
- [x] 2.5 Make `LanguageVersion` report Python 3.15.2.
- [x] 2.6 Set the product baseline to `3.15.2-alpha1`.
- [x] 2.7 Restrict main product projects to `net10.0` and `net11.0`.
- [x] 2.8 Restrict DLR core projects used by IronPython to `net10.0` and
  `net11.0`.
- [x] 2.9 Remove `Console32` and `Window32` from the solution.
- [x] 2.10 Update CI runner definitions for Windows/Linux x64+arm64 and macOS
  arm64.
- [x] 2.11 Update the default test framework list to `net10.0` and `net11.0`.
- [x] 2.12 Delete obsolete TFM property files and dead build branches for
  `net462`, `net8.0`, `net9.0`, and product `netstandard2.0`.
- [x] 2.13 Update package targets from `net462`/`net8.0` to the final framework
  layout.
- [x] 2.14 Remove x86 executable, registry, installer, and packaging tasks.
- [x] 2.15 Update installer launch conditions from .NET Framework to the
  .NET 10+ runtime model.
- [x] 2.16 Ensure Windows-targeted tools cross-build on Linux CI with
  `EnableWindowsTargeting`.
- [x] 2.17 Verify clean `net10.0` and `net11.0` builds after the standard library
  baseline is updated.
- [ ] 2.18 Verify x64 and arm64 native dependencies on each supported platform.
- [x] 2.19 Configure main-repository build and tests to consume the sibling
  `IronPython.StdLib` checkout.
- [x] 2.20 Standardize the standalone repository directory as
  `IronPython.StdLib`.
- [x] 2.21 Verify a CI-equivalent build with `TreatWarningsAsErrors`.

## 3. Package identity and documentation

- [x] 3.1 Rename the main NuGet package to `CyberStone.IronPython`.
- [x] 3.2 Rename the standard-library package to
  `CyberStone.IronPython.StdLib`.
- [x] 3.3 Rename the console tool package to
  `CyberStone.IronPython.Console`.
- [x] 3.4 Keep CLR namespaces and assembly names under `IronPython`.
- [x] 3.5 Update archive, Chocolatey, MSI, DEB, and PKG identifiers where
  applicable.
- [x] 3.6 Update README, installation scripts, package READMEs, and links for
  Python 3.15.2.
- [x] 3.7 Document `sys.version_info` versus `sys.implementation.version`.
- [x] 3.8 Add documented differences and unsupported C-extension policy.
- [x] 3.9 Document NumSharp/.NET interop as the supported numerical path rather
  than NumPy compatibility.

## 4. CPython 3.15.2 standard-library repository

- [x] 4.1 Create the independently owned standard-library repository.
- [x] 4.2 Import the CPython 3.15.2 `Lib` baseline into the `python-3.15`
  branch.
- [x] 4.2.1 Remove stale 3.4-era files that were retained by the overlay import.
- [x] 4.3 Retain license and IronPython stdlib version metadata.
- [x] 4.4 Remove all CPython 3.13+ removed modules from the published tree.
- [x] 4.5 Inventory all IronPython-specific stdlib patches.
- [x] 4.6 Record patch ownership, reason, upstream relation, and review status.
- [x] 4.7 Pin the main repository to an exact standard-library commit.
- [x] 4.8 Add a repeatable CPython 3.15.x synchronization procedure.
- [x] 4.9 Add validation that no old stdlib branch is used accidentally.
- [x] 4.10 Configure the standard-library package to build from the sibling
  repository.
- [x] 4.11 Make imports start successfully under the 3.15 importlib bootstrap,
  including the `_imp` interfaces expected by 3.15.
- [x] 4.12 Complete CPython 3.14/3.15 t-string and lazy-import tokenizer/parser
  support required before `site.py` and package tests can run.
- [x] 4.13 Record every temporary 3.15 bootstrap patch in
  `docs/python-3.15-bootstrap-patches.md`.
- [ ] 4.14 Remove each bootstrap patch after its permanent implementation lands.

## 5. Conformance harness and migration oracle

- [x] 5.1 Define in-scope test categories and waiver categories.
- [x] 5.2 Add a machine-readable CPython 3.15.2 baseline manifest.
- [x] 5.3 Store the pinned grammar, ASDL, and test-source revisions.
- [x] 5.4 Add CPython 3.15.2 differential-runner infrastructure.
- [x] 5.5 Compare exit status, stdout/stderr, exception fields, `str`/`repr`,
  public AST nodes, and source positions.
- [x] 5.6 Add legacy 3.4-compatible corpus differential tests.
- [x] 5.7 Require every difference to classify as intentional evolution or bug.
- [x] 5.8 Reject unclassified failures and skips in CI.
- [x] 5.9 Add waiver schema with owner, issue, reason, and review date.
- [x] 5.10 Add release-gate reporting for total pass/fail/skip counts.

## 6. Source-generator proof of concept

- [x] 6.1 Create the grammar-model and validation library.
- [x] 6.2 Create the deterministic C# parser emitter.
- [x] 6.3 Create the net10 generator inspection CLI.
- [x] 6.4 Create the netstandard2.0 incremental source-generator adapter.
- [x] 6.5 Read grammar inputs from `AdditionalFiles`.
- [x] 6.6 Emit no generated artifacts to the source tree.
- [x] 6.7 Support `EmitCompilerGeneratedFiles` for inspection.
- [x] 6.8 Add tests for missing actions, duplicate actions, stale actions,
  invalid grammar, and emitter failure.
- [x] 6.9 Add incremental invalidation tests.
- [x] 6.10 Verify deterministic output across repeated builds.

## 7. First end-to-end vertical slice

- [x] 7.1 Generate a parser for a minimal grammar.
- [x] 7.2 Parse `x = 1 + 2` through the existing parser facade.
- [x] 7.3 Produce a public AST with all four source-position fields.
- [x] 7.4 Lower the public AST into the compiler AST.
- [x] 7.5 Execute the lowered module through the runtime.
- [x] 7.6 Preserve `Parser.CreateParser`, `ParseFile`,
  `ParseInteractiveCode`, and `ParserSink` compatibility.
- [x] 7.7 Add generator, facade, AST, lowering, and execution tests.
- [x] 7.8 Keep this slice runnable in CI before importing the full grammar.

## 8. Complete tokenizer for Python 3.15

- [x] 8.1 Define the 3.15 token model and token metadata.
- [x] 8.2 Implement indentation and dedent handling.
- [x] 8.3 Implement encoding declarations and decoded source input.
- [x] 8.4 Implement soft keywords.
- [x] 8.5 Implement `FSTRING_START`, `FSTRING_MIDDLE`, and `FSTRING_END`.
- [x] 8.6 Implement `TSTRING_START`, `TSTRING_MIDDLE`, and `TSTRING_END`.
- [x] 8.7 Implement nested expression tokenization inside f-strings and
  t-strings.
- [x] 8.8 Implement exact token spans and line mapping.
- [x] 8.9 Implement interactive incomplete-input behavior.
- [x] 8.10 Implement tokenizer recovery without corrupting parser state.
- [x] 8.11 Add tokenizer conformance and differential tests.

## 9. PEG parser runtime and grammar

- [x] 9.1 Port the required CPython PEG runtime semantics to C#.
- [x] 9.2 Implement ordered choice, optional, repeat, gather, lookahead, cut, and
  forced alternatives.
- [x] 9.3 Implement direct and indirect left-recursion memoization.
- [x] 9.4 Implement backtracking, invalid-rule recovery, and diagnostic
  selection.
- [x] 9.5 Import the CPython 3.15.2 grammar structure.
- [x] 9.5.1 Parse and validate all 278 CPython 3.15.2 grammar rules at build
  time, including memoized rules and nested gather constructs.
- [x] 9.6 Define stable rule/alternative action identifiers.
- [x] 9.6.1 Generate C# PEG descriptors for all 278 rules and verify top-level
  action IDs are unique.
- [x] 9.6.2 Parse a minimal Python file through descriptors generated from the
  full CPython grammar.
- [x] 9.7 Implement typed C# action dispatch.
- [x] 9.8 Validate action coverage for every alternative.
- [x] 9.9 Implement all grammar invalid rules and version checks.
- [x] 9.10 Generate parser code for module, eval, and interactive modes.
- [x] 9.11 Add parser stack-depth and catastrophic backtracking protection.

## 10. Public AST and lowering

- [x] 10.1 Generate public AST node types from `Python.asdl`.
- [x] 10.2 Generate field metadata, constructors, walkers, and validation.
- [x] 10.3 Preserve all CPython 3.15 AST fields and source positions.
- [x] 10.4 Implement `_ast` conversion and round-tripping.
- [x] 10.5 Implement AST constructor strict-field/unknown-keyword behavior.
- [x] 10.6 Implement lowering for every public AST statement and expression.
- [x] 10.7 Keep lowering free of parsing responsibilities.
- [x] 10.8 Add AST differential tests against CPython 3.15.2.
- [x] 10.9 Add `compile(..., mode="exec"|"eval"|"single")` coverage.

## 11. Parser facade cutover

- [x] 11.1 Add an internal-only legacy/generated parser selector.
- [x] 11.2 Keep legacy parser behavior as the default until parity.
- [ ] 11.3 Run the full existing suite against generated parser.
- [ ] 11.4 Resolve facade and parser-sink compatibility differences.
- [ ] 11.5 Switch the default to generated parser.
- [ ] 11.6 Delete the legacy parser implementation.
- [ ] 11.7 Delete the internal parser selector.
- [ ] 11.8 Delete obsolete parser tests that only assert implementation details.

## 12. P0: annotations and typing

- [x] 12.1 Preserve parameter, return, variable, and module annotations in the
  public AST.
- [ ] 12.2 Implement annotation scopes and name resolution.
- [x] 12.3 Implement module and class `__annotations__`.
- [x] 12.4 Implement `from __future__ import annotations`.
- [ ] 12.5 Implement PEP 649/749 deferred annotation evaluation.
- [ ] 12.6 Implement `annotationlib` and `__annotate__`.
- [ ] 12.7 Implement `inspect.get_annotations`, `inspect.signature`, and
  `typing.get_type_hints` behavior.
- [ ] 12.8 Implement PEP 585 built-in generics and generic aliases.
- [ ] 12.9 Implement PEP 604 union types.
- [ ] 12.10 Implement `Protocol`, `Literal`, `Final`, `TypedDict`, and related
  PEP 544/586/589/591 runtime behavior.
- [ ] 12.11 Implement `ParamSpec`, `Concatenate`, and `TypeAlias`.
- [ ] 12.12 Implement `TypeVarTuple`, `Unpack`, and `Self`.
- [ ] 12.13 Implement PEP 695 type parameters and `type` statements.
- [ ] 12.14 Implement type-parameter defaults and annotation scopes.
- [ ] 12.15 Implement `ReadOnly`, `TypeIs`, `NoDefault`, and `override`.
- [ ] 12.16 Implement PEP 728 typed extra items.
- [ ] 12.17 Implement PEP 747 `TypeForm`.
- [ ] 12.18 Implement PEP 800 disjoint bases.
- [ ] 12.19 Pass the complete CPython 3.15.2 typing and annotation test suites.

## 13. P0: asynchronous programming

- [x] 13.1 Preserve `async def`, `await`, `async for`, and `async with`
  parsing under the new grammar.
- [x] 13.2 Verify lazy-start coroutine semantics.
- [x] 13.3 Implement coroutine `send`, `throw`, and `close` parity.
- [x] 13.4 Verify async-generator `asend`, `athrow`, and `aclose`.
- [ ] 13.5 Implement async comprehensions and async-generator expressions.
- [ ] 13.6 Implement `contextvars` propagation through .NET tasks.
- [ ] 13.7 Implement `ExceptionGroup` and `except*` lowering.
- [ ] 13.8 Implement `asyncio.TaskGroup`.
- [ ] 13.9 Implement `asyncio.timeout` and cancellation-group behavior.
- [ ] 13.10 Implement 3.14 asyncio task/socket introspection.
- [ ] 13.11 Verify event-loop, task, future, lock, stream, subprocess, and SSL
  tests.
- [ ] 13.12 Pass the applicable CPython 3.15.2 asyncio suites.

## 14. P0: matrix multiplication

- [x] 14.1 Verify `@` precedence and AST shape under the new grammar.
- [x] 14.2 Verify `__matmul__` and `__rmatmul__` dispatch.
- [x] 14.3 Verify `@=` and `__imatmul__` dispatch.
- [x] 14.4 Verify `operator.matmul` and `operator.imatmul`.
- [x] 14.5 Verify error messages and unsupported-operand behavior.
- [x] 14.6 Add focused NumSharp/CLR interop smoke tests.

## 15. Remaining Python language features

- [x] 15.1 Implement or verify PEP 448 generalized unpacking.
- [x] 15.2 Implement PEP 479 generator `StopIteration` conversion.
- [x] 15.3 Implement PEP 498/701 final f-string grammar.
- [x] 15.4 Implement numeric underscores.
- [x] 15.5 Implement PEP 519 path-like protocol.
- [x] 15.6 Implement PEP 487 class creation and descriptor hooks.
- [x] 15.7 Implement PEP 520 and PEP 468 order guarantees.
- [ ] 15.8 Implement PEP 563 compatibility mode.
- [x] 15.9 Guarantee dictionary insertion order.
- [x] 15.10 Implement assignment expressions.
- [x] 15.11 Implement positional-only parameters.
- [x] 15.12 Implement f-string debug expressions.
- [x] 15.13 Implement `continue` in `finally`.
- [x] 15.14 Implement dict union operators.
- [x] 15.15 Implement arbitrary decorator expressions.
- [ ] 15.16 Implement structural pattern matching.
- [x] 15.17 Implement parenthesized context managers.
- [x] 15.18 Implement `zip(strict=True)`.
- [ ] 15.19 Implement precise expression error ranges.
- [x] 15.20 Implement exception notes.
- [x] 15.21 Implement starred unpacking in `for`.
- [ ] 15.22 Implement Python-level buffer protocol.
- [ ] 15.23 Implement defined `locals()`/`exec()`/`eval()` semantics.
- [ ] 15.24 Implement 3.13 docstring whitespace normalization.
- [x] 15.25 Implement bracketless `except`/`except*`.
- [ ] 15.26 Implement the PEP 765 `finally` control-flow warning.
- [x] 15.27 Implement unpacking in 3.15 comprehensions.
- [x] 15.28 Implement 3.15 AST constructor strictness.
- [ ] 15.29 Implement 3.15 lazy-import syntax and runtime behavior.
- [x] 15.30 Implement 3.14/3.15 t-string syntax and runtime objects.

## 16. Object model, exceptions, imports, and runtime

- [ ] 16.1 Implement exception groups and exception-group traceback rendering.
- [ ] 16.2 Verify exception chaining, notes, and 3.15 diagnostic fields.
- [ ] 16.3 Implement built-in generic aliases and subscriptable runtime types.
- [x] 16.4 Implement `frozendict`.
- [x] 16.5 Implement `sentinel`.
- [ ] 16.6 Implement lazy imports and importlib integration.
- [ ] 16.7 Implement module attribute customization APIs.
- [ ] 16.8 Implement importlib resources and metadata changes.
- [ ] 16.9 Implement package startup configuration files.
- [ ] 16.10 Implement `concurrent.interpreters` using isolated IronPython
  runtimes.
- [ ] 16.11 Implement UTF-8 default encoding behavior.
- [ ] 16.12 Implement 3.15 built-in and numeric behavior changes.
- [ ] 16.13 Implement precise frame, traceback, and locals semantics required
  by public APIs.
- [x] 16.14 Implement the initial `sys.audit` and `sys.addaudithook` surface.
- [x] 16.15 Implement the import-bootstrap subset of `_imp` required by
  CPython 3.15 (`source_hash`, frozen lookup, hash-pyc policy, and path root).
- [x] 16.16 Implement `_codecs._normalize_encoding`.
- [x] 16.17 Accept the 3.15 `compile(..., module=...)` keyword and expose
  `sys.pycache_prefix`.
- [x] 16.18 Add the import bootstrap subset of `nt`/`posix` path-root APIs.

## 17. Standard library and managed module replacements

- [ ] 17.1 Add `graphlib` and `zoneinfo`.
- [ ] 17.2 Add `tomllib`.
- [ ] 17.3 Add `compression.zstd`.
- [ ] 17.4 Add `profiling` and the public sampling profile API.
- [ ] 17.5 Add `math.integer`.
- [ ] 17.6 Remove `distutils` and other modules absent from CPython 3.15.2.
- [ ] 17.7 Remove PEP 594 modules and `lib2to3`/`2to3`.
- [ ] 17.8 Remove `tkinter.tix`.
- [ ] 17.9 Update `sqlite3` public API for 3.15 behavior.
- [ ] 17.10 Update `locale`, `argparse`, `uuid`, and CLI-output changes.
- [ ] 17.11 Validate `_ssl`, `_socket`, `ctypes`, compression, hashing, and
  SQLite on every supported platform/architecture.
- [ ] 17.12 Validate `asyncio.subprocess` and platform event loops.
- [ ] 17.13 Validate `pip`, `ensurepip`, package metadata, and installation.
- [ ] 17.14 Add selected best-effort pure-Python package smoke tests.

## 18. IronPython interop and extension preservation

- [ ] 18.1 Preserve `clr` and .NET type/object interop.
- [ ] 18.2 Preserve hosting APIs.
- [ ] 18.3 Preserve debugging and tracing APIs.
- [ ] 18.4 Preserve `IronPython.SQLite`.
- [ ] 18.5 Preserve WPF extension support on Windows.
- [ ] 18.6 Preserve CLR-specific tests as a separate regression track.
- [ ] 18.7 Verify `Task`, `ValueTask`, `IAsyncEnumerable`, and
  `IAsyncDisposable` interop under the new async semantics.
- [ ] 18.8 Document NumSharp and other .NET library usage patterns.

## 19. Final conformance and release

- [ ] 19.1 Run the complete applicable CPython 3.15.2 suite on every platform.
- [ ] 19.2 Resolve all non-waived failures and skips.
- [ ] 19.3 Review every waiver for category, owner, issue, and expiry.
- [ ] 19.4 Verify all required x64/arm64 builds and tests.
- [ ] 19.5 Verify deterministic source-generator output.
- [ ] 19.6 Build and test `CyberStone.IronPython` NuGet packages.
- [ ] 19.7 Build and test `CyberStone.IronPython.StdLib`.
- [ ] 19.8 Build and test `CyberStone.IronPython.Console`.
- [ ] 19.9 Verify installer and archive packages for the supported matrix.
- [ ] 19.10 Remove all migration-only switches and compatibility shims.
- [ ] 19.11 Update all public documentation and support declarations.
- [ ] 19.12 Mark the OpenSpec change complete and archive it.
