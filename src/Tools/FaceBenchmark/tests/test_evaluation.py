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

from face_benchmark.calibration import fit_isotonic
from face_benchmark.metrics import evaluate, roc_auc, tar_at_far
from face_benchmark.scores import read_score_csv

from .helpers import write_scores


class EvaluationTests(unittest.TestCase):
    def test_isotonic_calibration_is_monotonic(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            rows = read_score_csv(write_scores(Path(directory) / "scores.csv"))
            calibration = fit_isotonic(rows, "higher-is-match")

            normalized = [calibration.normalize(score) for score in [0.1, 0.2, 0.8, 0.9]]

            self.assertEqual(normalized, sorted(normalized))
            self.assertEqual([0.0, 0.0, 100.0, 100.0], normalized)
            self.assertAlmostEqual(50.0, calibration.normalize(0.5))

    def test_lower_is_match_reverses_score_direction(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = write_scores(Path(directory) / "scores.csv")
            lines = path.read_text(encoding="utf-8").splitlines()
            inverted = [lines[0]]
            for line in lines[1:]:
                fields = line.split(",")
                fields[-1] = str(1.0 - float(fields[-1]))
                inverted.append(",".join(fields))
            path.write_text("\n".join(inverted) + "\n", encoding="utf-8")
            rows = read_score_csv(path)
            calibration = fit_isotonic(rows, "lower-is-match")

            self.assertGreater(calibration.normalize(0.1), calibration.normalize(0.9))

    def test_evaluate_reports_target_and_identity_metrics(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            rows = read_score_csv(write_scores(Path(directory) / "scores.csv"))
            calibration = fit_isotonic(rows, "higher-is-match")

            evaluated, metrics = evaluate(rows, calibration, tolerance=5)

            self.assertEqual(8, len(evaluated))
            self.assertEqual(1.0, metrics["identityAccuracy"])
            self.assertEqual(1.0, metrics["rocAuc"])
            self.assertEqual(0.5, metrics["withinToleranceRate"])

    def test_auc_and_tar_handle_ties_deterministically(self) -> None:
        labels = [True, True, False, False]
        scores = [90.0, 50.0, 50.0, 10.0]

        self.assertEqual(0.875, roc_auc(labels, scores))
        self.assertEqual(0.5, tar_at_far(labels, scores, 0.01))


if __name__ == "__main__":
    unittest.main()
