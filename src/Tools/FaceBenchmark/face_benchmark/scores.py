from __future__ import annotations

import csv
import json
from dataclasses import dataclass
from pathlib import Path

from .errors import BenchmarkError


REQUIRED_COLUMNS = {
    "pair_id",
    "split",
    "enrollment_image_id",
    "probe_image_id",
    "same_identity",
    "target_percentage",
    "raw_score",
}


@dataclass(frozen=True)
class PairScore:
    pair_id: str
    split: str
    enrollment_image_id: str
    probe_image_id: str
    same_identity: bool
    target_percentage: float | None
    raw_score: float


def read_score_csv(path: Path) -> list[PairScore]:
    try:
        stream = path.open("r", encoding="utf-8-sig", newline="")
    except OSError as error:
        raise BenchmarkError(f"Unable to open score file '{path}': {error}") from error

    with stream:
        reader = csv.DictReader(stream)
        columns = set(reader.fieldnames or [])
        missing = sorted(REQUIRED_COLUMNS - columns)
        if missing:
            raise BenchmarkError(f"Score CSV is missing columns: {', '.join(missing)}")
        extra = sorted(columns - REQUIRED_COLUMNS)
        if extra:
            raise BenchmarkError(f"Score CSV has unsupported columns: {', '.join(extra)}")

        rows: list[PairScore] = []
        pair_ids: set[str] = set()
        for line_number, row in enumerate(reader, start=2):
            pair_id = _text(row["pair_id"], "pair_id", line_number)
            if pair_id in pair_ids:
                raise BenchmarkError(f"Duplicate pair_id '{pair_id}' on line {line_number}.")
            pair_ids.add(pair_id)

            split = _text(row["split"], "split", line_number)
            if split not in {"calibration", "evaluation"}:
                raise BenchmarkError(
                    f"split on line {line_number} must be 'calibration' or 'evaluation'."
                )
            same_identity = _boolean(row["same_identity"], line_number)
            target = _optional_percentage(row["target_percentage"], line_number)
            if not same_identity and target is not None:
                raise BenchmarkError(
                    f"Impostor pair '{pair_id}' must not contain a target percentage."
                )
            if split == "calibration" and target is not None:
                raise BenchmarkError(
                    f"Calibration row '{pair_id}' must not contain a target percentage."
                )
            if split == "evaluation" and same_identity and target is None:
                raise BenchmarkError(
                    f"Evaluation genuine pair '{pair_id}' requires a target percentage."
                )
            rows.append(
                PairScore(
                    pair_id=pair_id,
                    split=split,
                    enrollment_image_id=_text(
                        row["enrollment_image_id"], "enrollment_image_id", line_number
                    ),
                    probe_image_id=_text(
                        row["probe_image_id"], "probe_image_id", line_number
                    ),
                    same_identity=same_identity,
                    target_percentage=target,
                    raw_score=_number(row["raw_score"], "raw_score", line_number),
                )
            )

    if not rows:
        raise BenchmarkError("Score CSV contains no data rows.")
    _validate_splits(rows)
    return rows


def validate_scores_against_manifest(
    rows: list[PairScore], manifest_path: Path, benchmark_version: str
) -> None:
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise BenchmarkError(f"Unable to read manifest '{manifest_path}': {error}") from error
    if manifest.get("benchmarkVersion") != benchmark_version:
        raise BenchmarkError("Score manifest benchmark version does not match the specification.")
    manifest_pairs = manifest.get("pairs")
    if not isinstance(manifest_pairs, list) or not manifest_pairs:
        raise BenchmarkError("Score manifest contains no pairs.")
    expected = {pair.get("pairId"): pair for pair in manifest_pairs}
    if None in expected or len(expected) != len(manifest_pairs):
        raise BenchmarkError("Score manifest contains a missing or duplicate pairId.")
    actual = {row.pair_id: row for row in rows}
    missing = sorted(expected.keys() - actual.keys())
    extra = sorted(actual.keys() - expected.keys())
    if missing or extra:
        details = []
        if missing:
            details.append("missing: " + ", ".join(missing[:10]))
        if extra:
            details.append("unexpected: " + ", ".join(extra[:10]))
        raise BenchmarkError("Score CSV pair set does not match manifest (" + "; ".join(details) + ").")

    for pair_id, pair in expected.items():
        row = actual[pair_id]
        expected_values = (
            pair.get("split"),
            pair.get("enrollmentImageId"),
            pair.get("probeImageId"),
            pair.get("sameIdentity"),
            pair.get("targetPercentage"),
        )
        actual_values = (
            row.split,
            row.enrollment_image_id,
            row.probe_image_id,
            row.same_identity,
            row.target_percentage,
        )
        if actual_values != expected_values:
            raise BenchmarkError(
                f"Score CSV metadata for pair '{pair_id}' does not match the manifest."
            )


def _validate_splits(rows: list[PairScore]) -> None:
    calibration = [row for row in rows if row.split == "calibration"]
    evaluation = [row for row in rows if row.split == "evaluation"]
    if not calibration or not evaluation:
        raise BenchmarkError("Score CSV requires both calibration and evaluation rows.")
    for split_name, split_rows in (("calibration", calibration), ("evaluation", evaluation)):
        labels = {row.same_identity for row in split_rows}
        if labels != {False, True}:
            raise BenchmarkError(
                f"The {split_name} split requires both genuine and impostor pairs."
            )

    calibration_images = {
        image_id
        for row in calibration
        for image_id in (row.enrollment_image_id, row.probe_image_id)
    }
    evaluation_images = {
        image_id
        for row in evaluation
        for image_id in (row.enrollment_image_id, row.probe_image_id)
    }
    overlap = sorted(calibration_images & evaluation_images)
    if overlap:
        raise BenchmarkError(
            "Calibration and evaluation image IDs overlap: " + ", ".join(overlap)
        )


def _text(value: str | None, field: str, line: int) -> str:
    if value is None or not value.strip():
        raise BenchmarkError(f"{field} on line {line} must be non-empty.")
    return value.strip()


def _boolean(value: str | None, line: int) -> bool:
    normalized = _text(value, "same_identity", line).lower()
    if normalized not in {"true", "false"}:
        raise BenchmarkError(f"same_identity on line {line} must be true or false.")
    return normalized == "true"


def _number(value: str | None, field: str, line: int) -> float:
    try:
        number = float(_text(value, field, line))
    except ValueError as error:
        raise BenchmarkError(f"{field} on line {line} must be numeric.") from error
    if number != number or number in {float("inf"), float("-inf")}:
        raise BenchmarkError(f"{field} on line {line} must be finite.")
    return number


def _optional_percentage(value: str | None, line: int) -> float | None:
    if value is None or not value.strip():
        return None
    percentage = _number(value, "target_percentage", line)
    if not 0 <= percentage <= 100:
        raise BenchmarkError(f"target_percentage on line {line} must be between 0 and 100.")
    return percentage
