# Synchronizing IronPython.StdLib

The standard library is an independent repository:

`../IronPython.StdLib`

The main repository records the exact accepted commit in
`Directory.Build.props` and `Build.proj` as `StdLibCommit`. CI-equivalent builds
fail when the sibling checkout does not match that commit.

## Update procedure

1. In `IronPython.StdLib`, check out branch `python-3.15`.
2. Synchronize from CPython tag `v3.15.2` or the explicitly approved
   `3.15` maintenance revision.
3. Reapply only the patches listed in
   `docs/python-3.15-bootstrap-patches.md`.
4. Run the standard-library package build and applicable conformance tests.
5. Commit the standard-library repository.
6. Copy the resulting full commit SHA into `StdLibCommit` in
   `Directory.Build.props` and `Build.proj`.
7. Run `make.ps1 -frameworks net10.0 build` from the main repository.
8. Commit the main-repository pin update together with any grammar, ASDL, or
   conformance-baseline changes required by the synchronization.

Never point the main repository at a moving branch or an uncommitted standard
library checkout.
