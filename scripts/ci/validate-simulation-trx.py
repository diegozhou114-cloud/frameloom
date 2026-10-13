#!/usr/bin/env python3
import argparse
import sys
import xml.etree.ElementTree as element_tree


def executed_classes(path):
    try:
        root = element_tree.parse(path).getroot()
    except (OSError, element_tree.ParseError) as error:
        raise ValueError(f"invalid TRX {path}: {error}") from error
    definitions = {}
    for node in root.iter():
        if node.tag.rsplit("}", 1)[-1] != "UnitTest":
            continue
        method = next((child for child in node.iter()
                       if child.tag.rsplit("}", 1)[-1] == "TestMethod"), None)
        if method is not None:
            definitions[node.attrib.get("id", "")] = method.attrib.get("className", "")
    executed = set()
    unresolved = set()
    for node in root.iter():
        if node.tag.rsplit("}", 1)[-1] != "UnitTestResult":
            continue
        test_id = node.attrib.get("testId", "")
        class_name = definitions.get(test_id)
        if class_name:
            executed.add(class_name)
        else:
            unresolved.add(test_id or "<missing>")
    return executed, unresolved


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", required=True)
    parser.add_argument("--classes", required=True)
    args = parser.parse_args()
    expected = {value for value in args.classes.split(",") if value}
    if not expected:
        print("expected class list is empty", file=sys.stderr)
        return 1
    try:
        actual, unresolved = executed_classes(args.results)
    except ValueError as error:
        print(error, file=sys.stderr)
        return 1
    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    print(f"mixed classes missing={missing} extra={extra} unresolved={sorted(unresolved)}")
    return int(bool(missing or extra or unresolved))


if __name__ == "__main__":
    raise SystemExit(main())
