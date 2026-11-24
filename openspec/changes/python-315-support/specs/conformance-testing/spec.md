# Conformance testing

## ADDED Requirements

### Requirement: CPython test suite gate

All applicable CPython 3.15.2 tests SHALL pass or be covered by an approved
waiver.

#### Scenario: Release candidate

- WHEN the release gate runs
- THEN there are zero unclassified failures
- AND zero unclassified skips.

### Requirement: Differential oracle

IronPython behavior SHALL be compared with pinned CPython 3.15.2.

#### Scenario: Observable difference

- WHEN a differential case produces different observable behavior
- THEN the difference is classified as intentional 3.15 behavior or an
  IronPython bug.

### Requirement: Waiver governance

Every waiver SHALL include an owner, category, reason, linked issue, and review
date.

#### Scenario: New waiver

- WHEN a test is added to the waiver list
- THEN it must fit an approved category and cannot silently disable a language
  feature.

### Requirement: Platform and architecture coverage

Release gates SHALL run on every claimed runtime, platform, and architecture.

#### Scenario: arm64 regression

- WHEN a platform-specific or native-module regression occurs on an arm64 target
- THEN it fails the release gate rather than remaining untested.

### Requirement: Generated-code verification

CI SHALL verify source-generator determinism and build integration.

#### Scenario: Generator drift

- WHEN the generator produces nondeterministic or invalid output
- THEN the conformance build fails.
