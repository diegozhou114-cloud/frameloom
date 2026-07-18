import json
import tempfile
import unittest
from pathlib import Path

from validate_unity_results import validate


class UnityResultValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.manifest = self.root / "required.json"
        self.manifest.write_text(json.dumps({
            "schemaVersion": 1,
            "modes": {
                "PlayMode": {
                    "assemblies": {
                        "ZWebGLFrameWork.Runtime.Tests": {
                            "minimumPassed": 1,
                            "requiredNames": ["LauncherPlayMode_EntersInteractiveVillage"]
                        }
                    }
                }
            }
        }), encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def write_result(self, case_result="Passed", case_name="LauncherPlayMode_EntersInteractiveVillage",
                     assembly_name="ZWebGLFrameWork.Runtime.Tests"):
        (self.root / "result.xml").write_text(f'''<?xml version="1.0"?>
<test-run result="{case_result}">
  <test-suite type="Assembly" name="{assembly_name}" result="{case_result}">
    <test-case fullname="ZWebGLFrameWork.Runtime.Tests.Smoke.{case_name}" result="{case_result}" />
  </test-suite>
</test-run>''', encoding="utf-8")

    def test_accepts_nonzero_named_pass(self):
        self.write_result()
        self.assertEqual(validate("PlayMode", self.manifest, self.root), [])

    def test_accepts_gameci_dll_path_as_assembly_name(self):
        self.write_result(assembly_name="/tmp/results/ZWebGLFrameWork.Runtime.Tests.dll")
        self.assertEqual(validate("PlayMode", self.manifest, self.root), [])

    def test_PlayModeGate_RequiresExpectedNonZeroTestCount(self):
        (self.root / "result.xml").write_text('<test-run result="Passed"/>', encoding="utf-8")
        self.assertRegex("\n".join(validate("PlayMode", self.manifest, self.root)), "minimumPassed")

    def test_rejects_skipped_or_inconclusive(self):
        self.write_result(case_result="Skipped")
        self.assertRegex("\n".join(validate("PlayMode", self.manifest, self.root)), "not passed")

    def test_rejects_missing_required_name(self):
        self.write_result(case_name="DifferentTest")
        self.assertRegex("\n".join(validate("PlayMode", self.manifest, self.root)), "required test")


if __name__ == "__main__":
    unittest.main()
