import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import verify_export


class ExportVerificationTests(unittest.TestCase):
    def test_failed_launch_or_missing_marker_is_never_accepted(self):
        cases = [subprocess.CompletedProcess([], 1, "Godot Engine", ""),
                 subprocess.CompletedProcess([], 0, "", ""),
                 subprocess.CompletedProcess([], 0, "Godot Engine", "ERROR: missing script"),
                 subprocess.CompletedProcess([], 0, "Godot Engine", "System.IO.IOException: missing asset")]
        with tempfile.TemporaryDirectory() as directory, patch.object(verify_export, "LOGS", Path(directory)):
            for result in cases:
                with self.subTest(result=result), patch.object(verify_export.subprocess, "run", return_value=result):
                    with self.assertRaises(RuntimeError):
                        verify_export.checked_run("launch", ["game"], directory, ["Godot Engine"])
                    self.assertEqual((Path(directory) / "launch.log").read_text(), result.stdout + result.stderr)

    def test_launch_requires_success_and_leaves_log(self):
        result = subprocess.CompletedProcess([], 0, "Godot Engine\n", "")
        with tempfile.TemporaryDirectory() as directory, patch.object(verify_export, "LOGS", Path(directory)), \
             patch.object(verify_export.subprocess, "run", return_value=result) as run:
            verify_export.checked_run("launch", ["game", "scene with spaces"], directory, ["Godot Engine"])
            self.assertEqual(run.call_args.args[0], ["game", "scene with spaces"])
            self.assertEqual(run.call_args.kwargs["timeout"], 180)
            self.assertTrue((Path(directory) / "launch.log").is_file())


if __name__ == "__main__":
    unittest.main()
