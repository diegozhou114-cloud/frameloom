#!/usr/bin/env python3
import argparse
import json
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


PASS = {"Passed", "Success"}


def collect_cases(node, current_assembly, cases):
    if node.tag == "test-suite" and node.attrib.get("type") == "Assembly":
        current_assembly = Path(node.attrib.get("name", "")).name
        if current_assembly.lower().endswith(".dll"):
            current_assembly = current_assembly[:-4]
    if node.tag == "test-case":
        cases[current_assembly].append({
            "name": node.attrib.get("fullname") or node.attrib.get("name", ""),
            "result": node.attrib.get("result", ""),
        })
    for child in node:
        collect_cases(child, current_assembly, cases)


def validate(mode, manifest_path, results_path):
    errors = []
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    mode_contract = manifest.get("modes", {}).get(mode)
    if mode_contract is None:
        return [f"manifest has no mode {mode}"]

    result_root = Path(results_path)
    xml_files = sorted(result_root.rglob("*.xml")) if result_root.is_dir() else [result_root]
    if not xml_files:
        return [f"no NUnit XML found beneath {result_root}"]

    cases = defaultdict(list)
    for xml_file in xml_files:
        try:
            collect_cases(ET.parse(xml_file).getroot(), "", cases)
        except ET.ParseError as error:
            errors.append(f"invalid XML {xml_file}: {error}")

    for assembly, contract in mode_contract.get("assemblies", {}).items():
        assembly_cases = cases.get(assembly, [])
        passed = [case for case in assembly_cases if case["result"] in PASS]
        if len(passed) < contract["minimumPassed"]:
            errors.append(
                f"{mode}/{assembly} minimumPassed={contract['minimumPassed']} actual={len(passed)}")
        non_passed = [case for case in assembly_cases if case["result"] not in PASS]
        for case in non_passed:
            errors.append(f"{mode}/{assembly} test not passed: {case['name']} result={case['result']}")
        names = {case["name"].split("(", 1)[0].rsplit(".", 1)[-1] for case in passed}
        for required in contract.get("requiredNames", []):
            if required not in names:
                errors.append(f"{mode}/{assembly} required test missing: {required}")

    all_cases = [case for values in cases.values() for case in values]
    for case in all_cases:
        if case["result"] not in PASS:
            errors.append(f"global test not passed: {case['name']} result={case['result']}")
    if not all_cases:
        errors.append(f"{mode} produced zero tests")
    return sorted(set(errors))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", required=True, choices=("EditMode", "PlayMode"))
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--results", required=True)
    args = parser.parse_args()
    errors = validate(args.mode, args.manifest, args.results)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print(f"[PlayModeGate_RequiresExpectedNonZeroTestCount] PASS mode={args.mode}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
