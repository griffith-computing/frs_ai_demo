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

from dataclasses import dataclass

from .errors import BenchmarkError
from .scores import PairScore


@dataclass(frozen=True)
class CalibrationPoint:
    maximum_score: float
    probability: float
    sample_count: int


@dataclass(frozen=True)
class IsotonicCalibration:
    score_direction: str
    points: tuple[CalibrationPoint, ...]
    sample_count: int
    genuine_count: int
    impostor_count: int
    raw_score_minimum: float
    raw_score_maximum: float
    brier_score: float

    def normalize(self, raw_score: float) -> float:
        score = _oriented(raw_score, self.score_direction)
        if score <= self.points[0].maximum_score:
            return self.points[0].probability * 100.0
        for previous, current in zip(self.points, self.points[1:]):
            if score <= current.maximum_score:
                span = current.maximum_score - previous.maximum_score
                if span == 0:
                    return current.probability * 100.0
                fraction = (score - previous.maximum_score) / span
                probability = previous.probability + fraction * (
                    current.probability - previous.probability
                )
                return probability * 100.0
        return self.points[-1].probability * 100.0

    def to_dict(self) -> dict[str, object]:
        return {
            "method": "isotonic-regression-with-linear-knot-interpolation",
            "scoreDirection": self.score_direction,
            "sampleCount": self.sample_count,
            "genuineCount": self.genuine_count,
            "impostorCount": self.impostor_count,
            "rawScoreMinimum": self.raw_score_minimum,
            "rawScoreMaximum": self.raw_score_maximum,
            "brierScore": self.brier_score,
            "points": [
                {
                    "maximumOrientedScore": point.maximum_score,
                    "probability": point.probability,
                    "sampleCount": point.sample_count,
                }
                for point in self.points
            ],
        }


def fit_isotonic(rows: list[PairScore], score_direction: str) -> IsotonicCalibration:
    if score_direction not in {"higher-is-match", "lower-is-match"}:
        raise BenchmarkError(
            "score direction must be 'higher-is-match' or 'lower-is-match'."
        )
    calibration_rows = [row for row in rows if row.split == "calibration"]
    if not calibration_rows:
        raise BenchmarkError("Cannot calibrate without calibration rows.")
    if {row.same_identity for row in calibration_rows} != {False, True}:
        raise BenchmarkError("Calibration requires both genuine and impostor pairs.")

    grouped: list[list[float]] = []
    for row in sorted(
        calibration_rows, key=lambda item: _oriented(item.raw_score, score_direction)
    ):
        score = _oriented(row.raw_score, score_direction)
        label = 1.0 if row.same_identity else 0.0
        if grouped and grouped[-1][0] == score:
            grouped[-1][1] += label
            grouped[-1][2] += 1.0
        else:
            grouped.append([score, label, 1.0])

    blocks: list[list[float]] = []
    for maximum_score, positives, count in grouped:
        blocks.append([maximum_score, positives, count])
        while len(blocks) >= 2:
            previous = blocks[-2][1] / blocks[-2][2]
            current = blocks[-1][1] / blocks[-1][2]
            if previous <= current:
                break
            right = blocks.pop()
            left = blocks.pop()
            blocks.append(
                [
                    right[0],
                    left[1] + right[1],
                    left[2] + right[2],
                ]
            )

    points = tuple(
        CalibrationPoint(
            maximum_score=maximum_score,
            probability=positives / count,
            sample_count=int(count),
        )
        for maximum_score, positives, count in blocks
    )
    provisional = IsotonicCalibration(
        score_direction=score_direction,
        points=points,
        sample_count=len(calibration_rows),
        genuine_count=sum(row.same_identity for row in calibration_rows),
        impostor_count=sum(not row.same_identity for row in calibration_rows),
        raw_score_minimum=min(row.raw_score for row in calibration_rows),
        raw_score_maximum=max(row.raw_score for row in calibration_rows),
        brier_score=0.0,
    )
    brier_score = sum(
        (
            provisional.normalize(row.raw_score) / 100.0
            - (1.0 if row.same_identity else 0.0)
        )
        ** 2
        for row in calibration_rows
    ) / len(calibration_rows)
    return IsotonicCalibration(
        score_direction=provisional.score_direction,
        points=provisional.points,
        sample_count=provisional.sample_count,
        genuine_count=provisional.genuine_count,
        impostor_count=provisional.impostor_count,
        raw_score_minimum=provisional.raw_score_minimum,
        raw_score_maximum=provisional.raw_score_maximum,
        brier_score=brier_score,
    )


def _oriented(raw_score: float, score_direction: str) -> float:
    return raw_score if score_direction == "higher-is-match" else -raw_score
