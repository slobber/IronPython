# Generated parser determinism

Source-generator output is a release artifact input even when it is emitted
only under `obj/`. The release gate rebuilds the main runtime twice with
`EmitCompilerGeneratedFiles=true`, hashes every generated `.cs` file, and
requires identical relative paths and bytes.

```bash
python eng/scripts/verify-generated-determinism.py --framework net10.0
python eng/scripts/verify-generated-determinism.py --framework net11.0
```

On 2026-11-26, both frameworks emitted the same eight generated files with
identical SHA-256 hashes across two real `Rebuild` invocations.

CI runs this check in an independent job so generated parser determinism is
not inferred from a normal incremental build.
