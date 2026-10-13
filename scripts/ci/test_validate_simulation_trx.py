import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("validate-simulation-trx.py")
EXPECTED = "A.One,B.Two"


def trx(executed, defined=()):
    definitions = [(name, name) for name in executed] + list(defined)
    units = "".join(
        f'<UnitTest id="{test_id}"><TestMethod className="{name}" /></UnitTest>'
        for test_id, name in definitions)
    results = "".join(
        f'<UnitTestResult testId="{test_id}" outcome="Passed" />'
        for test_id, _ in [(name, name) for name in executed])
    return f'<TestRun><TestDefinitions>{units}</TestDefinitions><Results>{results}</Results></TestRun>'


class ValidateSimulationTrxTests(unittest.TestCase):
    def run_validator(self, content):
        with tempfile.TemporaryDirectory() as directory:
            result = Path(directory) / "simulation.trx"
            result.write_text(content, encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(SCRIPT), "--results", str(result),
                 "--classes", EXPECTED], text=True, capture_output=True)

    def test_accepts_exact_executed_classes(self):
        self.assertEqual(self.run_validator(trx(["A.One", "B.Two"])).returncode, 0)

    def test_rejects_missing_extra_and_unexecuted_definitions(self):
        missing = self.run_validator(trx(["A.One"]))
        self.assertNotEqual(missing.returncode, 0)
        self.assertIn("B.Two", missing.stdout)
        extra = self.run_validator(trx(["A.One", "B.Two", "C.Three"]))
        self.assertNotEqual(extra.returncode, 0)
        self.assertIn("C.Three", extra.stdout)
        unexecuted = self.run_validator(trx(["A.One"], [("unused", "B.Two")]))
        self.assertNotEqual(unexecuted.returncode, 0)
