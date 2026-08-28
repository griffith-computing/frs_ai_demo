from __future__ import annotations

import csv
import json
from datetime import UTC, datetime
from pathlib import Path

from .calibration import IsotonicCalibration
from .metrics import EvaluatedPair


def write_reports(
    output_directory: Path,
    benchmark_version: str,
    sdk: str,
    model_version: str,
    tolerance: float,
    calibration: IsotonicCalibration,
    evaluated: list[EvaluatedPair],
    metrics: dict[str, float],
) -> tuple[Path, Path]:
    output_directory.mkdir(parents=True, exist_ok=True)
    report = {
        "benchmarkVersion": benchmark_version,
        "generatedAtUtc": datetime.now(UTC).isoformat(),
        "sdk": sdk,
        "modelVersion": model_version,
        "tolerancePercentagePoints": tolerance,
        "calibration": calibration.to_dict(),
        "metrics": metrics,
        "results": [_result_dict(item) for item in evaluated],
    }
    json_path = output_directory / "report.json"
    json_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    csv_path = output_directory / "results.csv"
    with csv_path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(
            stream,
            fieldnames=[
                "pair_id",
                "same_identity",
                "target_percentage",
                "raw_score",
                "normalized_percentage",
                "absolute_error",
                "within_tolerance",
            ],
        )
        writer.writeheader()
        for item in evaluated:
            writer.writerow(
                {
                    "pair_id": item.pair.pair_id,
                    "same_identity": str(item.pair.same_identity).lower(),
                    "target_percentage": _optional(item.pair.target_percentage),
                    "raw_score": item.pair.raw_score,
                    "normalized_percentage": item.normalized_percentage,
                    "absolute_error": _optional(item.absolute_error),
                    "within_tolerance": (
                        "" if item.within_tolerance is None else str(item.within_tolerance).lower()
                    ),
                }
            )
    return json_path, csv_path


def _result_dict(item: EvaluatedPair) -> dict[str, object]:
    return {
        "pairId": item.pair.pair_id,
        "enrollmentImageId": item.pair.enrollment_image_id,
        "probeImageId": item.pair.probe_image_id,
        "sameIdentity": item.pair.same_identity,
        "targetPercentage": item.pair.target_percentage,
        "rawScore": item.pair.raw_score,
        "normalizedPercentage": item.normalized_percentage,
        "absoluteError": item.absolute_error,
        "withinTolerance": item.within_tolerance,
    }


def _optional(value: float | None) -> float | str:
    return "" if value is None else value
