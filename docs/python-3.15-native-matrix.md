# Python 3.15 native dependency matrix

The supported runtime matrix is:

| Platform | RID | Required native assets |
| --- | --- | --- |
| Windows x64 | `win-x64` | `e_sqlite3.dll` |
| Windows ARM64 | `win-arm64` | `e_sqlite3.dll` |
| Linux x64 | `linux-x64` | `libe_sqlite3.so`, `libMono.Unix.so` |
| Linux ARM64 | `linux-arm64` | `libe_sqlite3.so`, `libMono.Unix.so` |
| macOS ARM64 | `osx-arm64` | `libe_sqlite3.dylib`, `libMono.Unix.dylib` |

`SQLitePCLRaw.bundle_e_sqlite3` supplies the platform-specific SQLite build.
`Mono.Unix` supplies its Unix native bridge. `ZstdSharp.Port` is a managed port
and has no RID-specific native asset.

Verify all publish outputs:

```bash
python eng/scripts/verify-native-matrix.py --framework net10.0
```

On hosts with the Unix `file` command, the verifier also rejects a native
asset whose binary architecture does not match its RID.

Verify that the current host can actually load and execute native SQLite:

```bash
dotnet run --project tests/NativeDependencySmoke -f net10.0
```

CI executes the runtime smoke on every supported runner, so each ARM64 and
x64 job verifies the architecture-specific asset instead of only inspecting
NuGet metadata.

## Verification record

On 2026-11-26, both `net10.0` and `net11.0` publishes passed for all five RIDs.
The Unix `file` checks reported:

- `win-x64`: PE32+ x86-64 `e_sqlite3.dll`
- `win-arm64`: PE32+ AArch64 `e_sqlite3.dll`
- `linux-x64`: x86-64 `libe_sqlite3.so` and `libMono.Unix.so`
- `linux-arm64`: AArch64 `libe_sqlite3.so` and `libMono.Unix.so`
- `osx-arm64`: arm64 `libe_sqlite3.dylib` and `libMono.Unix.dylib`

The runtime smoke passed on Linux x64 for both frameworks with
`SQLite 3.53.4`.
