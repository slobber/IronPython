# Python 3.15 bootstrap patches

These patches exist only to exercise the CPython 3.15.2 standard library before
the replacement parser and runtime features are complete. Each item must be
removed when its permanent implementation lands.

| Patch | Reason | Replacement | Upstream relation | Owner | Status | Review |
|---|---|---|---|---|---|---|
| `lib/site.py`: eager imports | Avoid 3.15 lazy-import syntax during startup | Native lazy imports | Diverges from CPython 3.15 `Lib/site.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/site.py`: parenthesized `except` | Avoid 3.14 bracketless `except` | New parser support | Diverges from CPython 3.15 `Lib/site.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/site.py`: temporary `frozendict` | Satisfy startup imports | Native `frozendict` | Diverges from CPython 3.15 `Lib/site.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/site.py`: temporary `sentinel` | Satisfy `functools` startup | Native PEP 661 sentinel | Diverges from CPython 3.15 `Lib/site.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/types.py`: fallback `GenericAlias` | Allow stdlib imports | Native PEP 585 generic alias | Diverges from CPython 3.15 `Lib/types.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/types.py`: fallback `UnionType` | Allow stdlib imports | Native PEP 604 union type | Diverges from CPython 3.15 `Lib/types.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/_collections_abc.py`: fallback generic alias | Avoid `list[int]` at import | Native generic alias | Diverges from CPython 3.15 `Lib/_collections_abc.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/_collections_abc.py`: optional mapping registrations | Avoid missing 3.15 mapping types | Native mapping types | Diverges from CPython 3.15 `Lib/_collections_abc.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/collections/__init__.py`: temporary `frozendict` | Avoid startup `NameError` | Native `frozendict` | Diverges from CPython 3.15 `Lib/collections/__init__.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/copy.py`: temporary `frozendict` | Avoid copy-table initialization failure | Native `frozendict` | Diverges from CPython 3.15 `Lib/copy.py` | CyberStone maintainers | Open | 2026-12-31 |
| `lib/copyreg.py`: fallback union type | Avoid `int | str` construction failure | Native `UnionType` | Diverges from CPython 3.15 `Lib/copyreg.py` | CyberStone maintainers | Open | 2026-12-31 |
| `Parser.cs`: eager `lazy import` lowering | Allow the legacy parser to read 3.15 stdlib | Generated 3.15 parser and lazy import runtime | Main-repository migration shim | CyberStone maintainers | Open | 2026-12-31 |

The patches are not compatibility claims and must not be used as expected
behavior in conformance tests.
