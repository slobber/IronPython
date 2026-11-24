# Python language compatibility

## ADDED Requirements

### Requirement: Final Python version semantics

The runtime SHALL expose only final CPython 3.15.2 language semantics.

#### Scenario: Runtime version metadata

- WHEN Python code reads `sys.version_info`
- THEN it receives `(3, 15, 2, "final", 0)`
- AND `sys.implementation.name` remains `"ironpython"`
- AND `sys.implementation.version` reports the CyberStone.IronPython product
  version.

#### Scenario: Historical behavior is not selected

- WHEN source code is compiled
- THEN no public option selects Python 3.4, 3.6, or another historical semantic
  mode.

### Requirement: Complete Python 3.15 syntax

The tokenizer and parser SHALL accept and reject all syntax exactly as final
CPython 3.15.2 does.

#### Scenario: Modern syntax is accepted

- WHEN source uses pattern matching, `except*`, type parameters, t-strings,
  lazy imports, or comprehension unpacking
- THEN a public AST is produced with the final 3.15 node types and fields.

#### Scenario: Invalid syntax is rejected

- WHEN source violates the 3.15 grammar
- THEN a compatible syntax error category is raised
- AND line, column, end line, and end column identify the source location.

### Requirement: Public AST behavior

The public AST SHALL match CPython 3.15.2 node fields, source positions,
constructor validation, and round-trip compilation behavior.

#### Scenario: AST positions

- WHEN any AST node is produced by parsing
- THEN `lineno`, `col_offset`, `end_lineno`, and `end_col_offset` are present.

#### Scenario: AST construction

- WHEN an AST constructor omits a required field or receives an unknown field
- THEN it raises the same public exception behavior as CPython 3.15.2.

### Requirement: Annotation and typing semantics

The runtime SHALL implement final 3.15 annotation, typing, generic alias, and
type-parameter behavior.

#### Scenario: Deferred annotations

- WHEN annotations are queried through `annotationlib`, `inspect`, or `typing`
- THEN lazy evaluation, forward references, and annotation scopes follow
  CPython 3.15.2.

#### Scenario: Type parameters

- WHEN a function, class, or type alias declares type parameters
- THEN runtime type-parameter objects and defaults are visible through the
  3.15 public APIs.

### Requirement: Async and exception semantics

The runtime SHALL implement the 3.15 coroutine, async-generator, context,
cancellation, exception-group, and task-group contracts.

#### Scenario: Child-task failure

- WHEN multiple tasks in an `asyncio.TaskGroup` fail
- THEN the resulting exception group and cancellation behavior follow
  CPython 3.15.2.

### Requirement: Matrix multiplication

The runtime SHALL preserve the complete `@`, `@=`, `__matmul__`, `__rmatmul__`,
and `__imatmul__` contracts.

#### Scenario: Matrix dispatch

- WHEN a `@` or `@=` expression is evaluated
- THEN normal, reflected, and in-place protocol dispatch follows CPython
  3.15.2.

### Requirement: Public runtime behavior only

The implementation SHALL match public Python APIs without emulating CPython
implementation internals.

#### Scenario: Non-guaranteed internals differ

- WHEN behavior depends on GIL scheduling, object layout, refcount timing,
  CPython bytecode, or interpreter stack layout
- THEN IronPython may use its .NET implementation model without failing the
  language compatibility requirement.
