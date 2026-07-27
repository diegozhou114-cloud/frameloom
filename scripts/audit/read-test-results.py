#!/usr/bin/env python3
import json
import sys
import xml.etree.ElementTree as ElementTree


def local_name(tag):
    return tag.rsplit('}', 1)[-1]


def main():
    if len(sys.argv) != 2:
        raise ValueError('Usage: read-test-results.py <results.xml>')
    tree = ElementTree.parse(sys.argv[1])
    root = tree.getroot()
    root_name = local_name(root.tag)
    if root_name not in ('test-run', 'TestRun'):
        raise ValueError('Unknown test results root format: ' + root_name)
    records = []
    for element in root.iter():
        name = local_name(element.tag)
        if name == 'test-case':
            case_name = element.get('fullname') or element.get('name')
            result = element.get('result')
        elif name == 'UnitTestResult':
            case_name = element.get('testName')
            result = element.get('outcome')
        else:
            continue
        if case_name and result:
            records.append({'name': case_name, 'result': result})
    if not records:
        raise ValueError('No test results found')
    print(json.dumps(records))


if __name__ == '__main__':
    try:
        main()
    except (ElementTree.ParseError, OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
