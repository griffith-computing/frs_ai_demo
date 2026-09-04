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

import tempfile
import unittest
from pathlib import Path

from face_benchmark.errors import BenchmarkError
from face_benchmark.scores import read_score_csv, validate_scores_against_manifest

from .helpers import write_score_manifest, write_scores


class ScoreTests(unittest.TestCase):
    def test_reads_valid_score_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            rows = read_score_csv(write_scores(Path(directory) / "scores.csv"))

            self.assertEqual(12, len(rows))
            self.assertIn(95.0, {row.target_percentage for row in rows})

    def test_rejects_calibration_evaluation_image_leakage(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = write_scores(Path(directory) / "scores.csv")
            contents = path.read_text(encoding="utf-8").replace(
                "evaluation-1-enrollment",
                "calibration-1-enrollment",
            )
            path.write_text(contents, encoding="utf-8")

            with self.assertRaisesRegex(BenchmarkError, "image IDs overlap"):
                read_score_csv(path)

    def test_rejects_target_on_impostor_pair(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = write_scores(Path(directory) / "scores.csv")
            contents = path.read_text(encoding="utf-8").replace(
                ",false,,0.2",
                ",false,55,0.2",
                1,
            )
            path.write_text(contents, encoding="utf-8")

            with self.assertRaisesRegex(BenchmarkError, "Impostor pair"):
                read_score_csv(path)

    def test_rejects_score_metadata_that_differs_from_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            rows = read_score_csv(write_scores(root / "scores.csv"))
            manifest = write_score_manifest(root / "manifest.json")
            rows[0] = type(rows[0])(
                pair_id=rows[0].pair_id,
                split=rows[0].split,
                enrollment_image_id="changed-image",
                probe_image_id=rows[0].probe_image_id,
                same_identity=rows[0].same_identity,
                target_percentage=rows[0].target_percentage,
                raw_score=rows[0].raw_score,
            )

            with self.assertRaisesRegex(BenchmarkError, "does not match"):
                validate_scores_against_manifest(rows, manifest, "1.0.0")


if __name__ == "__main__":
    unittest.main()
