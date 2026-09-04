#----------------------------------------------------------------------------------
# THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
# EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
# OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
#
# This sample is not supported under any Microsoft standard support program or
# service. It is provided to you solely for the purpose of illustration and is
# intended to be modified, tested, and validated by the customer prior to any
# production use. The entire risk arising out of the use or performance of this
# code remains with the customer.
#
# Copyright (c) Microsoft Corporation. All rights reserved.
#----------------------------------------------------------------------------------

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
