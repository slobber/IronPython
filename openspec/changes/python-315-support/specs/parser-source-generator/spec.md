# Parser source generator

## ADDED Requirements

### Requirement: C# incremental parser generation

The compiler SHALL generate parser code from grammar inputs with a Roslyn
incremental source generator.

#### Scenario: Normal build

- WHEN a normal .NET build compiles IronPython
- THEN the source generator reads its grammar inputs
- AND emits parser C# into the compilation
- AND does not write generated artifacts into the source tree.

#### Scenario: Generated code inspection

- WHEN `EmitCompilerGeneratedFiles` is enabled
- THEN generated parser files are written under the configured `obj` directory.

### Requirement: Deterministic output

The generator SHALL produce byte-for-byte deterministic output for unchanged
inputs.

#### Scenario: Repeated generation

- WHEN the same grammar, ASDL, and action map are generated more than once
- THEN the generated source is identical.

### Requirement: Action coverage validation

The generator SHALL reject invalid grammar/action mappings before parser code is
compiled.

#### Scenario: Missing action

- WHEN a grammar alternative requires an action but no C# action is mapped
- THEN generation fails with a diagnostic identifying the rule and alternative.

#### Scenario: Stale action

- WHEN an action references a rule or alternative absent from the grammar
- THEN generation fails with a diagnostic identifying the stale action.

### Requirement: Preserve the parser facade

The generated parser SHALL be reachable through the existing
`IronPython.Compiler.Parser` interface during migration.

#### Scenario: Existing caller

- WHEN a host calls `Parser.CreateParser`, `ParseFile`, or
  `ParseInteractiveCode`
- THEN it does not need to reference generated parser implementation types.

### Requirement: Remove the legacy implementation

The legacy parser SHALL be deleted after generated-parser parity is established.

#### Scenario: Migration complete

- WHEN the conformance gate passes
- THEN no public or internal Python-version parser selector remains.
