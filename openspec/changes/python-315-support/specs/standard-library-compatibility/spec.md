# Standard library compatibility

## ADDED Requirements

### Requirement: Pinned standard library baseline

The product SHALL use a pinned CPython 3.15.2 standard-library snapshot from the
sibling standard-library repository.

#### Scenario: Reproducible build

- WHEN the main repository is built
- THEN it consumes an exact standard-library revision
- AND does not consume an unpinned branch head.

### Requirement: Final module availability

The default distribution SHALL expose the public modules available in a CPython
3.15.2 standard build on the same platform.

#### Scenario: Removed module

- WHEN code imports a module removed before Python 3.15
- THEN it is not provided merely because IronPython has a historical
  implementation.

### Requirement: Managed native-module replacements

Public standard-library APIs backed by native code SHALL work through managed or
controlled platform adapters without requiring CPython C-extension compatibility.

#### Scenario: Native-backed public module

- WHEN code uses a public API from `sqlite3`, `ssl`, compression, hashing,
  `ctypes`, or another supported native-backed module
- THEN the public behavior is available on every claimed platform/architecture
  unless CPython itself does not provide it there.

### Requirement: Package installation

The standard library SHALL support `ensurepip`, `pip`, import metadata, and
normal package installation workflows.

#### Scenario: Install a pure-Python package

- WHEN a selected pure-Python package is installed and imported
- THEN package metadata and import behavior do not depend on CPython internals.

### Requirement: Bounded third-party compatibility

Third-party package compatibility SHALL be best-effort except for explicit smoke
tests.

#### Scenario: CPython C extension

- WHEN a package requires the CPython C ABI
- THEN the product does not claim that package as supported.
