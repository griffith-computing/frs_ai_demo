from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from face_benchmark.cli import main

from .helpers import write_score_manifest, write_scores, write_spec


class CliTests(unittest.TestCase):
    def test_evaluate_writes_machine_readable_reports(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            spec = write_spec(root / "spec.json")
            scores = write_scores(root / "scores.csv")
            manifest = write_score_manifest(root / "manifest.json")
            output = root / "output"

            exit_code = main(
                [
                    "evaluate",
                    "--spec",
                    str(spec),
                    "--scores",
                    str(scores),
                    "--manifest",
                    str(manifest),
                    "--image-root",
                    str(root),
                    "--sdk",
                    "Example SDK",
                    "--model-version",
                    "1.2.3",
                    "--score-direction",
                    "higher-is-match",
                    "--output",
                    str(output),
                ]
            )

            self.assertEqual(1, exit_code)
            report = json.loads((output / "report.json").read_text(encoding="utf-8"))
            self.assertEqual("Example SDK", report["sdk"])
            self.assertEqual(8, len(report["results"]))
            self.assertTrue((output / "results.csv").is_file())


if __name__ == "__main__":
    unittest.main()
