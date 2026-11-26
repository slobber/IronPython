# Python 3.15 platform validation

Native-backed modules are validated separately from the full CPython
conformance run:

```bash
IRONPYTHONPATH="../IronPython.StdLib/lib" \
  dotnet bin/Release/net10.0/ipy.dll \
  tests/conformance/platform_modules_smoke.py
```

The smoke exercises:

- `zlib`, `bz2`, and `compression.zstd` round trips
- `hashlib` SHA-256 and MD5
- IPv4 loopback `socket` traffic
- TLS context construction through `ssl`
- native calls through `ctypes`
- in-memory `sqlite3` CRUD and version discovery

CI runs this script on Windows x64/ARM64, Linux x64/ARM64, and macOS ARM64
for both `net10.0` and `net11.0`.

`_lzma` has no IronPython-native implementation and remains unsupported under
the documented C-extension policy. This is separate from the supported
`zlib`, `bz2`, and `zstd` modules.

## Local verification record

On 2026-11-26, the platform module smoke passed on Linux x64 for both
`net10.0` and `net11.0` with SQLite 3.53.4. Additional Linux x64 checks:

- `ctypes` size, libc, and from-buffer suites: 17 passed, 2 skipped
- `test.test_zstd`: 121 passed, 3 skipped
- concurrent `BZ2File.write` regression: passed
- `test_sqlite3`: 514 passed, 8 skipped, and the two documented
  process-isolation waivers

Full CPython module suites remain part of the final conformance milestone;
their unrelated open failures are not treated as platform-smoke passes.
