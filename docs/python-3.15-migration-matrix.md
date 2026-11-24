# Python 3.15 Migration Feature Matrix

## Target and scope

- Final compatibility oracle: CPython `v3.15.2`.
- Current repository baseline: Python 3.4, with selected later features implemented independently.
- Development order: follow the final 3.15 behavior and dependency graph, not CPython's release history.
- Supported product targets: `net10.0` and later on Windows/Linux x64 and arm64, and macOS arm64.
- This matrix tracks language, compiler, public AST/introspection, async/runtime semantics, and standard-library changes.
- CPython C API/ABI behavior, C extensions, GIL/free-threading scheduling, reference-counting/GC timing, interpreter internals, and performance parity are out of scope.
- Third-party packages are best-effort; NumPy/Matplotlib-style CPython extensions are not supported.

Priority meanings:

- **P0**: first implementation tracks requested by the user: parameter/type annotations, async programming, and matrix multiplication.
- **P1**: required by the final CPython 3.15.2 language/runtime conformance target.
- **P2**: standard-library or usability work that can follow after the syntax/runtime foundation.

## Current baseline checkpoints

This is a planning checkpoint for the current `main` branch, not a claim of
3.15 conformance.

| Area | Current state | Main gap |
|---|---|---|
| Parser | Hand-written LL(1)-style parser based on the Python 3.4 grammar. | PEG parser, 3.15 grammar/action mapping, and a generated public AST. |
| Matrix multiplication | Parser/operator support exists for `@`, `@=`, `__matmul__`, `__rmatmul__`, and `__imatmul__`. | Validate final 3.15 precedence, dispatch, errors, AST shape, and tests after the front-end replacement. |
| Annotations and typing | Basic parameter/return/variable annotations and runtime `__annotations__` support exist. | `typing` runtime parity, annotation scopes, PEP 563 compatibility, PEP 649/749, PEP 695 type parameters, and 3.15 typing forms. |
| Async programming | `async def`, `await`, `async for`, `async with`, and async-generator lowering exist, including .NET Task integration. | Exception groups, `except*`, `asyncio.TaskGroup`, context-variable conformance, and the full 3.15 `asyncio` test surface. |
| Standard library | Submodule has 3.4, 3.6, and 3.7 branches. | A controlled CPython 3.15.2 snapshot, IronPython patch inventory, and final 3.15 module-removal set. |
| Build/platforms | CI spans several old TFMs and does not define the required architecture matrix. | `net10.0+` only, with Windows/Linux x64+arm64 and macOS arm64. |

## P0 implementation tracks

### Parameter, return, and variable annotations

| Priority | Release | Capability | Dependencies / notes |
|---|---|---|---|
| P0 | 3.5 | Function parameter and return annotations are retained at runtime; `typing` is introduced. | Public AST must preserve `arg.annotation` and `FunctionDef.returns`; function objects and `inspect.signature` must expose them. See [What's New 3.5](https://docs.python.org/3.5/whatsnew/3.5.html), [PEP 484](https://peps.python.org/pep-0484/). |
| P0 | 3.6 | Variable annotations (`x: T`) and annotated assignments. | Requires annotation scopes, module/class `__annotations__`, and correct assignment semantics. See [What's New 3.6](https://docs.python.org/3.6/whatsnew/3.6.html), [PEP 526](https://peps.python.org/pep-0526/). |
| P0 | 3.7 | `from __future__ import annotations` stores annotations as strings. | Must coexist with the 3.14 default deferred-annotation system. See [What's New 3.7](https://docs.python.org/3.7/whatsnew/3.7.html), [PEP 563](https://peps.python.org/pep-0563/). |
| P0 | 3.8 | Runtime typing additions include `Literal`, `Final`, `TypedDict`, and `Protocol`. | Mostly Python-level `typing` behavior backed by managed runtime types. See [What's New 3.8](https://docs.python.org/3.8/whatsnew/3.8.html), [PEP 544](https://peps.python.org/pep-0544/), [PEP 586](https://peps.python.org/pep-0586/), [PEP 589](https://peps.python.org/pep-0589/), [PEP 591](https://peps.python.org/pep-0591/). |
| P0 | 3.9 | Built-in generics such as `list[int]`; decorators may use arbitrary expressions. | Requires `__class_getitem__` and a 3.15-compatible parser. See [What's New 3.9](https://docs.python.org/3.9/whatsnew/3.9.html), [PEP 585](https://peps.python.org/pep-0585/), [PEP 614](https://peps.python.org/pep-0614/). |
| P0 | 3.10 | PEP 604 union types, `ParamSpec`, explicit type aliases, and user-defined type guards. | `types.UnionType`; `typing` runtime objects; correct annotation evaluation. See [What's New 3.10](https://docs.python.org/3.10/whatsnew/3.10.html), [PEP 604](https://peps.python.org/pep-0604/), [PEP 612](https://peps.python.org/pep-0612/), [PEP 613](https://peps.python.org/pep-0613/), [PEP 647](https://peps.python.org/pep-0647/). |
| P0 | 3.11 | Variadic generics, `Self`, and exception notes used by typing/introspection. | Requires `TypeVarTuple`/`Unpack` runtime objects and generic protocol support. See [What's New 3.11](https://docs.python.org/3.11/whatsnew/3.11.html), [PEP 646](https://peps.python.org/pep-0646/). |
| P0 | 3.12 | Function/class type parameter syntax and the `type` alias statement. | Adds annotation scopes, `TypeVar`/`ParamSpec`/`TypeVarTuple` objects, subscriptable generic aliases, and a new public AST shape. See [What's New 3.12](https://docs.python.org/3.12/whatsnew/3.12.html), [PEP 695](https://peps.python.org/pep-0695/). |
| P0 | 3.13 | Type parameter defaults, `ReadOnly`, `TypeIs`, and runtime-visible deprecation metadata. | Extends the 3.12 type-parameter object model. See [What's New 3.13](https://docs.python.org/3.13/whatsnew/3.13.html), [PEP 696](https://peps.python.org/pep-0696/), [PEP 702](https://peps.python.org/pep-0702/), [PEP 705](https://peps.python.org/pep-0705/), [PEP 742](https://peps.python.org/pep-0742/). |
| P0 | 3.14 | Deferred evaluation of annotations and `annotationlib`. | Changes `__annotations__`, class/function/module annotation semantics, and introspection behavior by default. See [What's New 3.14](https://docs.python.org/3.14/whatsnew/3.14.html), [PEP 649](https://peps.python.org/pep-0649/), [PEP 749](https://peps.python.org/pep-0749/). |
| P0 | 3.15 | Typed extra items, `TypeForm`, and disjoint bases. | Completes the 3.15 typing runtime surface after `annotationlib` is working. See [What's New 3.15](https://docs.python.org/3.15/whatsnew/3.15.html), [PEP 728](https://peps.python.org/pep-0728/), [PEP 747](https://peps.python.org/pep-0747/), [PEP 800](https://peps.python.org/pep-0800/). |

Acceptance must include:

- Parameter, positional-only, keyword-only, variadic, and return annotations.
- Module/class/variable annotations.
- `inspect.signature`, `inspect.get_annotations`, `typing.get_type_hints`,
  `annotationlib.get_annotations`, and `__annotate__`.
- String/future/deferred/forward-reference modes and annotation-scope name resolution.
- Public AST nodes and source positions for annotations and type parameters.

### Async programming

| Priority | Release | Capability | Dependencies / notes |
|---|---|---|---|
| P0 | 3.5 | `async def`, `await`, `async for`, and `async with`. | Requires awaitable/coroutine/async iterator/async context-manager protocols and generator lowering. See [What's New 3.5](https://docs.python.org/3.5/whatsnew/3.5.html), [PEP 492](https://peps.python.org/pep-0492/). |
| P0 | 3.6 | Async generators and asynchronous comprehensions. | Requires `asend`/`athrow`/`aclose`, async yield lowering, and async comprehension scopes. See [What's New 3.6](https://docs.python.org/3.6/whatsnew/3.6.html), [PEP 525](https://peps.python.org/pep-0525/), [PEP 530](https://peps.python.org/pep-0530/). |
| P0 | 3.7 | `async`/`await` become reserved keywords; `contextvars` is introduced. | Async context propagation must be implemented on .NET execution contexts/tasks. See [What's New 3.7](https://docs.python.org/3.7/whatsnew/3.7.html), [PEP 567](https://peps.python.org/pep-0567/). |
| P1 | 3.11 | Exception groups and `asyncio.TaskGroup`. | Task cancellation and child failures must produce CPython-compatible exception groups and `except*` behavior. See [What's New 3.11](https://docs.python.org/3.11/whatsnew/3.11.html), [PEP 654](https://peps.python.org/pep-0654/). |
| P2 | 3.14 | Improved `asyncio` task/socket introspection and debugging. | Depends on stable task/frame correlation. See [What's New 3.14](https://docs.python.org/3.14/whatsnew/3.14.html). |

The implementation must cover coroutine, generator-based coroutine compatibility
where 3.15 still exposes it, task cancellation, context variables, async
comprehensions, async context managers, exception groups, and public
`inspect`/`asyncio` introspection.

### Matrix multiplication

| Priority | Release | Capability | Dependencies / notes |
|---|---|---|---|
| P0 | 3.5 | Binary `@` and augmented `@=` operators; `__matmul__`, `__rmatmul__`, `__imatmul__`; `operator.matmul`/`operator.imatmul`. | Parser precedence, numeric/operator dispatch, in-place fallback, reflected dispatch, error messages, and public AST `MatMult`/`MatMult`-equivalent nodes. See [What's New 3.5](https://docs.python.org/3.5/whatsnew/3.5.html) and [PEP 465](https://peps.python.org/pep-0465/). |

## Release-by-release matrix

The rows below are migration-relevant release highlights. They intentionally omit
pathological or implementation-only changes that do not affect the agreed
pure-Python compatibility boundary.

### Python 3.5

Source: [What's New in Python 3.5](https://docs.python.org/3.5/whatsnew/3.5.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P0 | Async | PEP 492 coroutines and async protocols. | New parser/compiler/runtime state machine and event-loop integration. |
| P0 | Operators | PEP 465 matrix multiplication. | New binary/in-place operators, AST nodes, operators, and dispatch. |
| P0 | Typing | PEP 484 type hints and `typing`. | Runtime annotations and introspection. |
| P1 | Grammar | PEP 448 generalized iterable/dictionary unpacking. | Parser and call/display AST changes. |
| P1 | Exceptions | PEP 479 converts escaping `StopIteration` in generators to `RuntimeError`. | Generator lowering and exception behavior. |
| P2 | Stdlib | New `zipapp`; `math.isclose`, `os.scandir`, and `pathlib` improvements. | Standard-library snapshot and managed implementations. |

### Python 3.6

Source: [What's New in Python 3.6](https://docs.python.org/3.6/whatsnew/3.6.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P0 | Strings | PEP 498 f-strings. | Tokenizer, parser, AST, and runtime formatting must be reimplemented under the 3.15 f-string model, not the current whole-token parser. |
| P0 | Typing | PEP 526 variable annotations. | Annotation scopes and `__annotations__`. |
| P0 | Async | PEP 525 async generators and PEP 530 async comprehensions. | Async state machine and scope rules. |
| P1 | Lexing | PEP 515 underscores in numeric literals. | Tokenizer only. |
| P1 | Object model | `__init_subclass__`, `__set_name__`, and descriptor/class-creation updates. | Class construction and descriptors. |
| P2 | Stdlib | New `secrets`; `datetime.fold`; improved `asyncio`. | Standard-library synchronization. |

### Python 3.7

Source: [What's New in Python 3.7](https://docs.python.org/3.7/whatsnew/3.7.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P0 | Typing | PEP 563 postponed annotation evaluation via `from __future__ import annotations`. | Must support both this mode and the final 3.14 deferred-annotation model. |
| P0 | Async/context | New `contextvars`; `async` and `await` become reserved keywords. | Task-local context propagation and parser keyword changes. |
| P1 | Stdlib | New `dataclasses`. | Uses annotations, class creation, descriptors, and `typing`. |
| P1 | Import | New importlib resources APIs; improved import system behavior. | Standard-library snapshot and import adapter. |
| P1 | Syntax | `await` and async comprehensions are allowed inside f-strings. | Requires the 3.15 f-string tokenizer/parser. |
| P2 | Runtime | Dictionary insertion order is a language guarantee. | Ordering behavior for built-in mappings. |

### Python 3.8

Source: [What's New in Python 3.8](https://docs.python.org/3.8/whatsnew/3.8.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P0 | Grammar | Assignment expressions (`:=`). | PEG action and name-binding checks. Current repository has a recent implementation, but it must be regenerated under the final grammar. |
| P0 | Parameters | PEP 570 positional-only `/` parameters. | Signature parsing, call binding, `inspect`, and AST. |
| P0 | Typing | `Literal`, `Final`, `TypedDict`, and `Protocol`. | Runtime typing objects and annotation behavior. |
| P1 | Strings | `f"{expr=}"` debug specifier. | Tokenizer/PEG f-string actions and evaluation/repr rules. |
| P1 | Control flow | `continue` is allowed in `finally`. | Compiler control-flow checks. |
| P2 | Stdlib | New `importlib.metadata`; `math.comb`, `math.perm`, `math.isqrt` improvements. | Package metadata and standard-library behavior. |

### Python 3.9

Source: [What's New in Python 3.9](https://docs.python.org/3.9/whatsnew/3.9.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P1 | Parser | PEP 617 PEG parser replaces the LL(1) parser. | This is the architectural basis for the 3.15 parser and the reason the old parser should be replaced, not extended indefinitely. |
| P0 | Typing | PEP 585 built-in generics (`list[int]`). | `__class_getitem__` and generic alias runtime objects. |
| P1 | Operators | PEP 584 dictionary union and in-place update operators. | `dict` protocol and operator dispatch. |
| P1 | Syntax | PEP 614 allows arbitrary decorator expressions. | PEG grammar and AST construction. |
| P1 | Runtime | Module `__file__` becomes absolute for command-line scripts. | Script loading and traceback behavior. |
| P2 | Stdlib | New `zoneinfo`, `graphlib`; string `removeprefix`/`removesuffix`. | Standard-library snapshot and platform time-zone support. |

### Python 3.10

Source: [What's New in Python 3.10](https://docs.python.org/3.10/whatsnew/3.10.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P1 | Grammar | PEP 634/635/636 structural pattern matching; soft `match`/`case` keywords. | Major new AST nodes, pattern binding, literal/class/sequence/mapping patterns, guards, and lowering. |
| P0 | Typing | PEP 604 union types, PEP 612 `ParamSpec`, PEP 613 type aliases, and PEP 647 type guards. | New runtime type objects and annotation behavior. |
| P1 | Syntax | Parenthesized context managers. | PEG grammar and context-manager lowering. |
| P1 | Builtins | `zip(strict=True)`; lossless integer/index conversions. | Built-in protocol behavior. |
| P2 | Stdlib | `int.bit_count`; mapping view `mapping` attribute; improved traceback locations. | Standard-library and compiler diagnostics. |
| P2 | Removed | `parser` module and old-parser compatibility APIs. | Do not reintroduce removed stdlib modules. |

### Python 3.11

Source: [What's New in Python 3.11](https://docs.python.org/3.11/whatsnew/3.11.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P1 | Exceptions | PEP 654 exception groups, `BaseExceptionGroup`, `ExceptionGroup`, and `except*`. | Runtime exception model, traceback rendering, and `except*` lowering. |
| P1 | Tracing | PEP 657 fine-grained expression ranges. | Public tracebacks and compiler source ranges. |
| P1 | Async | `asyncio.TaskGroup`. | Child-task cancellation and exception-group aggregation. |
| P0 | Typing | `TypeVarTuple`, `Unpack`, `Self`, and related runtime typing behavior. | Generic runtime objects and introspection. |
| P1 | Exceptions | PEP 678 exception notes (`add_note`). | `BaseException`, traceback formatting. |
| P1 | Grammar | Starred unpacking in `for`; nested async comprehensions. | PEG grammar and scope rules. |
| P2 | Stdlib | New `tomllib`; removed legacy `asyncio.coroutine` and dead APIs. | Standard-library snapshot and removal tracking. |

### Python 3.12

Source: [What's New in Python 3.12](https://docs.python.org/3.12/whatsnew/3.12.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P0 | Typing | PEP 695 type parameter syntax (`def f[T]`, `class C[T]`) and `type` aliases. | New annotation scopes and runtime type-parameter objects. |
| P1 | Strings | PEP 701 formal f-string grammar. | New `FSTRING_START/MIDDLE/END` tokenization, arbitrary nested expressions, multiline expressions, comments, and precise errors. |
| P1 | Data model | PEP 688 Python-level buffer protocol (`__buffer__`). | Buffer protocol/object model integration. |
| P0 | Typing | PEP 692 `TypedDict` `Unpack`; PEP 698 `typing.override`. | Typing runtime behavior. |
| P1 | Removal | `distutils`, `asynchat`, and `asyncore` removed. | The 3.15 stdlib snapshot must not expose removed modules. |
| P2 | Stdlib | `pathlib` subclassing, SQLite CLI, improved error suggestions. | Standard-library and user-facing diagnostics. |

### Python 3.13

Source: [What's New in Python 3.13](https://docs.python.org/3.13/whatsnew/3.13.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P0 | Typing | PEP 696 type parameter defaults. | Extends PEP 695 parser and runtime objects. |
| P0 | Typing | PEP 705 `ReadOnly`, PEP 742 `TypeIs`, and `NoDefault`. | `typing` runtime surface. |
| P1 | Reflection | PEP 667 defined `locals()`/`exec`/`eval`/`f_locals` mutation semantics. | Debugger and frame-locals model. |
| P1 | Compiler | Common leading whitespace is stripped from docstrings. | Constant folding/compiler behavior and `__doc__`. |
| P1 | Removals | PEP 594 removes 19 legacy stdlib modules; `2to3`/`lib2to3` and `tkinter.tix` are removed. | Standard-library snapshot must match the removal set. |
| P2 | REPL | New interactive interpreter with multiline editing, color, and commands. | IronPython-specific interactive host behavior; not language semantics. |
| Out of scope | Interpreter | PEP 703 free-threaded mode and PEP 744 JIT are implementation features. | Do not emulate CPython’s implementation. |

Do not ship these removed modules merely because the current IronPython tree has
a managed implementation: `aifc`, `audioop`, `cgi`, `cgitb`, `chunk`, `crypt`,
`imghdr`, `mailcap`, `msilib`, `nis`, `nntplib`, `ossaudiodev`, `pipes`,
`sndhdr`, `spwd`, `sunau`, `telnetlib`, `uu`, and `xdrlib`.

### Python 3.14

Source: [What's New in Python 3.14](https://docs.python.org/3.14/whatsnew/3.14.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P0 | Annotations | PEP 649/749 deferred annotation evaluation and `annotationlib`. | Default annotation semantics change for functions, classes, and modules. |
| P1 | Strings | PEP 750 template strings (`t"..."`) and `string.templatelib`. | New `TSTRING_START/MIDDLE/END` scanner states, public `Template`/`Interpolation` AST and runtime objects, conversion/format-spec behavior. |
| P1 | Syntax | PEP 758 allows bracketless `except A, B:` and `except* A, B:` when there is no `as` clause. | PEG grammar and error locations. |
| P1 | Compiler warnings | PEP 765 warns when `return`/`break`/`continue` exits a `finally` block. | Compiler control-flow analysis and diagnostics. |
| P1 | Stdlib | New `compression.zstd`, `concurrent.interpreters`, and richer `asyncio` introspection. | Native compression replacement, interpreter isolation model, and task observability. |
| P2 | Runtime | Mixed real/complex arithmetic C99 rules; improved syntax/error messages. | Numeric behavior and diagnostics. |
| Out of scope | Interpreter | Free-threaded mode and tail-call interpreter internals. | Do not emulate CPython scheduling or bytecode implementation. |

`concurrent.interpreters` is a public standard-library API and therefore needs a
separate implementation decision: either emulate isolated Python interpreters
with IronPython `ScriptRuntime` instances, or record an explicit product-boundary
waiver. It is not covered by the general “interpreter internals” exclusion.

### Python 3.15

Source: [What's New in Python 3.15](https://docs.python.org/3.15/whatsnew/3.15.html)

| Priority | Area | Feature | Migration impact |
|---|---|---|---|
| P1 | Imports | PEP 810 explicit `lazy import` via the `lazy` soft keyword. | Parser, import machinery, module object behavior, lazy-name resolution, error reporting, and packaging startup behavior. |
| P1 | Builtins | PEP 814 immutable, hashable `frozendict`. | New built-in mapping type and support in `copy`, `json`, `marshal`, `pickle`, `pprint`, `xml.etree.ElementTree`, `eval`/`exec`, `type`, and `str.maketrans`. |
| P1 | Builtins | PEP 661 `sentinel` built-in type. | Identity-preserving, copy-, type-expression-, and pickle-compatible sentinels. |
| P1 | I/O | PEP 686 UTF-8 becomes the default encoding. | File/text I/O, CLI/environment behavior, and Windows code-page interactions. |
| P1 | Grammar | PEP 798 unpacking in list/set/dict comprehensions and generator expressions. | New AST/lowering forms and scope behavior. |
| P0 | Typing | PEP 728 typed extra items, PEP 747 `TypeForm`, and PEP 800 disjoint bases. | Final typing runtime surface and annotation introspection. |
| P1 | Stdlib | New `profiling` package with tracing and `profiling.sampling`/Tachyon. | Requires managed tracing/sampling design or an explicit compatibility fallback. |
| P1 | Stdlib | New `math.integer` (PEP 791). | Managed integer math implementation and import registration. |
| P1 | AST | AST constructors reject missing required fields and unknown keywords. | Public AST constructors and compatibility with pickled/constructed trees. |
| P2 | Startup | PEP 829 package startup configuration files. | Packaging/startup integration. |
| P2 | Stdlib | `argparse` color/suggestions, SQLite parameter cleanup, locale `@` modifiers. | Public API behavior in the 3.15 stdlib snapshot. |

## Cross-cutting foundation work

1. **3.15 tokenizer and PEG grammar**
   - Soft keywords, indentation, comments, f-strings, t-strings, and four-position spans.
   - Grammar/action mapping must be validated against CPython 3.15.2.

2. **Two-layer AST**
   - Generated public AST must match `Python.asdl` and `_ast` behavior.
   - Preserve `lineno`, `col_offset`, `end_lineno`, and `end_col_offset`.
   - Lowering converts public AST to the existing compiler AST.

3. **Annotation system**
   - Annotation scopes, `__annotations__`, `__annotate__`, `annotationlib`, and type parameters.
   - PEP 563 and PEP 649/749 must be distinguishable.

4. **Exception/traceback model**
   - Exception groups, `except*`, notes, chained exceptions, and expression-level traceback locations.

5. **Async model**
   - Coroutines, async generators, `TaskGroup`, `contextvars`, cancellation, and debug/introspection hooks.

6. **Import system**
   - Lazy imports, importlib resource APIs, package metadata, startup files, and removal of legacy import paths.

7. **Object model**
   - Built-in generic aliases, typing runtime objects, `frozendict`, `sentinel`, buffer protocol, descriptors, class-construction hooks, and exception-group objects.

8. **Standard library synchronization**
   - Pin the CPython 3.15.2 stdlib snapshot.
   - Keep IronPython patches explicit and reviewable in the controlled stdlib fork.
   - Map C-backed modules to .NET implementations or platform services; do not implement CPython C-ABI loading.
   - Remove modules deleted by 3.13's PEP 594 and other final-3.15 removals.

## Open scope decisions exposed by the matrix

These are not C-API details and therefore require an explicit decision rather
than an automatic exclusion:

- `concurrent.interpreters` (3.14): implement via isolated IronPython runtimes or
  declare a bounded stdlib gap.
- `profiling.sampling`/Tachyon (3.15): implement a managed sampling profiler or
  provide a documented fallback while keeping the module importable.
- `compression.zstd` (3.14): use a platform/.NET zstd binding and define packaging
  requirements.
- Native-backed stdlib modules on arm64: validate `ssl`, `sqlite3`, compression,
  `ctypes`, and platform modules for Windows/Linux arm64 and macOS arm64.

## Primary source index

- [Python 3.5 What's New](https://docs.python.org/3.5/whatsnew/3.5.html)
- [Python 3.6 What's New](https://docs.python.org/3.6/whatsnew/3.6.html)
- [Python 3.7 What's New](https://docs.python.org/3.7/whatsnew/3.7.html)
- [Python 3.8 What's New](https://docs.python.org/3.8/whatsnew/3.8.html)
- [Python 3.9 What's New](https://docs.python.org/3.9/whatsnew/3.9.html)
- [Python 3.10 What's New](https://docs.python.org/3.10/whatsnew/3.10.html)
- [Python 3.11 What's New](https://docs.python.org/3.11/whatsnew/3.11.html)
- [Python 3.12 What's New](https://docs.python.org/3.12/whatsnew/3.12.html)
- [Python 3.13 What's New](https://docs.python.org/3.13/whatsnew/3.13.html)
- [Python 3.14 What's New](https://docs.python.org/3.14/whatsnew/3.14.html)
- [Python 3.15 What's New](https://docs.python.org/3.15/whatsnew/3.15.html)
- [CPython 3.15.2 grammar](https://github.com/python/cpython/blob/v3.15.2/Grammar/python.gram)
- [CPython 3.15.2 ASDL](https://github.com/python/cpython/blob/v3.15.2/Parser/Python.asdl)
