#!/usr/bin/env python3

"""Validate conformance waivers and their review metadata."""

from __future__ import annotations

import argparse
from datetime import date
import json
from pathlib import Path
import sys


CATEGORIES = {
    "platform-unavailable",
    "cpython-c-api",
    "cpython-internal",
    "stress-resource",
    "product-boundary",
}


def require_non_empty(entry: dict[str, object], key: str, waiver_id: str) -> str:
    value = entry.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{waiver_id}: {key} must be a non-empty string")
    return value.strip()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "path",
        nargs="?",
        default="tests/conformance/waivers.json",
    )
    args = parser.parse_args()

    path = Path(args.path)
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schemaVersion") != 1:
        raise SystemExit("unsupported waiver schema version")
    waivers = data.get("waivers")
    if not isinstance(waivers, list):
        raise SystemExit("waivers must be a list")

    seen_ids: set[str] = set()
    seen_tests: dict[str, str] = {}
    for entry in waivers:
        if not isinstance(entry, dict):
            raise SystemExit("every waiver must be an object")
        waiver_id = require_non_empty(entry, "id", "<unknown>")
        if waiver_id in seen_ids:
            raise SystemExit(f"duplicate waiver id: {waiver_id}")
        seen_ids.add(waiver_id)

        category = require_non_empty(entry, "category", waiver_id)
        if category not in CATEGORIES:
            raise SystemExit(f"{waiver_id}: unknown category {category!r}")
        require_non_empty(entry, "owner", waiver_id)
        issue = require_non_empty(entry, "linkedIssue", waiver_id)
        if "openspec:" not in issue and "#" not in issue:
            raise SystemExit(f"{waiver_id}: linkedIssue must identify an issue")
        reason = require_non_empty(entry, "reason", waiver_id)
        if len(reason) < 40:
            raise SystemExit(f"{waiver_id}: reason is too short")

        review_date = require_non_empty(entry, "reviewDate", waiver_id)
        try:
            expires = date.fromisoformat(review_date)
        except ValueError as exception:
            raise SystemExit(f"{waiver_id}: invalid reviewDate") from exception
        if expires < date.today():
            raise SystemExit(f"{waiver_id}: waiver expired on {review_date}")

        tests = entry.get("tests")
        if not isinstance(tests, list) or not tests:
            raise SystemExit(f"{waiver_id}: tests must be a non-empty list")
        for pattern in tests:
            if not isinstance(pattern, str) or not pattern.startswith("test."):
                raise SystemExit(
                    f"{waiver_id}: test pattern must start with 'test.'"
                )
            if pattern in seen_tests:
                raise SystemExit(
                    f"{waiver_id}: duplicate test pattern {pattern!r} "
                    f"also used by {seen_tests[pattern]}"
                )
            seen_tests[pattern] = waiver_id

    print(
        f"WAIVER_AUDIT_OK {len(waivers)} waivers, "
        f"{len(seen_tests)} test patterns",
        flush=True,
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
