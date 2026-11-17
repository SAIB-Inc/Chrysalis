#!/usr/bin/env python3
"""Print the sorted multiset of test names and outcomes from VSTest or MTP TRX files."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("results", nargs="+", type=Path, help="TRX files or result directories")
args = parser.parse_args()
files = {file.resolve() for path in args.results
         for file in (path.rglob("*.trx") if path.is_dir() else [path])}
if not files:
    parser.error("no TRX files found")
cases = [(result.attrib["testName"], result.attrib["outcome"]) for file in sorted(files)
         for result in ET.parse(file).iter("{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult")]
if not cases:
    parser.error("no test cases found")
for case in sorted(cases):
    print(json.dumps(case, ensure_ascii=False))
