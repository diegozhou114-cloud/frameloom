#!/usr/bin/env python3
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def validate(results_path, minimum):
    root = Path(results_path)
    files = sorted(root.rglob("*.trx")) if root.is_dir() else [root]
    if not files:
        return [f"no TRX files found beneath {root}"]

    results = []
    errors = []
    for trx in files:
        try:
            document = ET.parse(trx).getroot()
        except (ET.ParseError, OSError) as error:
            errors.append(f"invalid TRX {trx}: {error}")
            continue
        results.extend(
            node.attrib for node in document.iter()
            if local_name(node.tag) == "UnitTestResult"
        )

    passed = [result for result in results if result.get("outcome") == "Passed"]
    if len(passed) < minimum:
        errors.append(f"server tests minimum={minimum} actualPassed={len(passed)}")
    for result in results:
        if result.get("outcome") != "Passed":
            errors.append(
                "server test not Passed: "
                f"{result.get('testName', '')} outcome={result.get('outcome', '')}")
    return sorted(set(errors))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", required=True)
    parser.add_argument("--minimum", type=int, required=True)
    args = parser.parse_args()
    errors = validate(args.results, args.minimum)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print("[CI_ServerProjects_BuildAndTestReleaseWarnAsError] PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
