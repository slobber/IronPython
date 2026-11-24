# Platform and packaging

## ADDED Requirements

### Requirement: Supported runtime matrix

The product SHALL support:

- `net10.0` and `net11.0`
- Windows x64 and arm64
- Linux x64 and arm64
- macOS arm64

#### Scenario: Unsupported legacy target

- WHEN a build targets `net462`, `net8.0`, `net9.0`, x86, or Intel macOS
- THEN it is not part of the product release gate.

### Requirement: Separate language and product versions

Python language metadata and the IronPython product version SHALL be independent.

#### Scenario: Pre-release product

- WHEN the product version is `3.15.2-alpha1`
- THEN `sys.version_info` still reports Python 3.15.2 final.

### Requirement: CyberStone package identity

The public NuGet package family SHALL use the CyberStone prefix while namespaces
and assemblies retain IronPython.

#### Scenario: Consume packages

- WHEN a user references the runtime packages
- THEN the package IDs are `CyberStone.IronPython`,
  `CyberStone.IronPython.StdLib`, and `CyberStone.IronPython.Console`.

### Requirement: Removed legacy packaging

Packaging SHALL NOT depend on x86 executables, NGen, Mono, .NET Framework, or
retired TFM directories.

#### Scenario: Build a package

- WHEN a supported platform package is built
- THEN it contains only artifacts valid for the new runtime matrix.
