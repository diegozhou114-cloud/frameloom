import tempfile
import unittest
from pathlib import Path

from validate_trx import validate


class TrxValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def write(self, outcomes):
        rows = "".join(
            f'<UnitTestResult testName="test{index}" outcome="{outcome}" />'
            for index, outcome in enumerate(outcomes)
        )
        (self.root / "results.trx").write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'<Results>{rows}</Results></TestRun>', encoding="utf-8")

    def test_accepts_nonzero_passed_tests(self):
        self.write(["Passed", "Passed"])
        self.assertEqual(validate(self.root, 1), [])

    def test_rejects_zero_tests(self):
        self.write([])
        self.assertRegex("\n".join(validate(self.root, 1)), "minimum")

    def test_rejects_failed_or_skipped_results(self):
        self.write(["Passed", "Failed", "NotExecuted"])
        self.assertRegex("\n".join(validate(self.root, 1)), "not Passed")


if __name__ == "__main__":
    unittest.main()
