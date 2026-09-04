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

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .errors import BenchmarkError


@dataclass(frozen=True)
class Identity:
    identity_id: str
    seed: int


@dataclass(frozen=True)
class BenchmarkSpec:
    version: str
    name: str
    tolerance: float
    targets: tuple[float, ...]
    evaluation_identities: tuple[Identity, ...]
    calibration_identities: tuple[Identity, ...]
    raw: dict[str, Any]


def load_spec(path: Path) -> BenchmarkSpec:
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise BenchmarkError(f"Unable to read benchmark specification '{path}': {error}") from error

    required = {
        "benchmarkVersion",
        "name",
        "tolerancePercentagePoints",
        "targets",
        "evaluationIdentities",
        "calibrationIdentities",
        "generator",
        "referenceModels",
    }
    missing = sorted(required - raw.keys())
    if missing:
        raise BenchmarkError(f"Benchmark specification is missing: {', '.join(missing)}")

    targets = tuple(_percentage(value, "target") for value in raw["targets"])
    if len(targets) != len(set(targets)):
        raise BenchmarkError("Benchmark target percentages must be unique.")
    if tuple(sorted(targets, reverse=True)) != targets:
        raise BenchmarkError("Benchmark target percentages must be in descending order.")

    evaluation = _identities(raw["evaluationIdentities"], "evaluation")
    calibration = _identities(raw["calibrationIdentities"], "calibration")
    evaluation_ids = {item.identity_id for item in evaluation}
    calibration_ids = {item.identity_id for item in calibration}
    overlap = sorted(evaluation_ids & calibration_ids)
    if overlap:
        raise BenchmarkError(
            f"Calibration and evaluation identities overlap: {', '.join(overlap)}"
        )

    placeholder_paths = _find_placeholders(raw)
    if placeholder_paths:
        raise BenchmarkError(
            "Benchmark model pins are incomplete: " + ", ".join(placeholder_paths)
        )

    return BenchmarkSpec(
        version=_required_text(raw["benchmarkVersion"], "benchmarkVersion"),
        name=_required_text(raw["name"], "name"),
        tolerance=_percentage(raw["tolerancePercentagePoints"], "tolerance"),
        targets=targets,
        evaluation_identities=evaluation,
        calibration_identities=calibration,
        raw=raw,
    )


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_manifest(manifest_path: Path, image_root: Path, spec: BenchmarkSpec) -> dict[str, Any]:
    from .pairs import build_pairs

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise BenchmarkError(f"Unable to read manifest '{manifest_path}': {error}") from error

    if manifest.get("benchmarkVersion") != spec.version:
        raise BenchmarkError("Manifest benchmark version does not match the specification.")
    artifacts = manifest.get("artifacts")
    if not isinstance(artifacts, list) or not artifacts:
        raise BenchmarkError("Manifest must contain at least one artifact.")

    image_ids: set[str] = set()
    by_split_identity: dict[tuple[str, str], list[dict[str, Any]]] = {}
    expected_by_split = {
        "evaluation": {item.identity_id for item in spec.evaluation_identities},
        "calibration": {item.identity_id for item in spec.calibration_identities},
    }
    for artifact in artifacts:
        image_id = _required_text(artifact.get("imageId"), "artifact imageId")
        if image_id in image_ids:
            raise BenchmarkError(f"Duplicate manifest imageId '{image_id}'.")
        image_ids.add(image_id)
        split = artifact.get("split")
        identity_id = artifact.get("identityId")
        if split not in expected_by_split:
            raise BenchmarkError(f"Artifact '{image_id}' has invalid split '{split}'.")
        if identity_id not in expected_by_split[split]:
            raise BenchmarkError(
                f"Artifact '{image_id}' has unexpected identity '{identity_id}' in {split}."
            )
        by_split_identity.setdefault((split, identity_id), []).append(artifact)

        relative_path = Path(_required_text(artifact.get("path"), f"{image_id} path"))
        if relative_path.is_absolute() or ".." in relative_path.parts:
            raise BenchmarkError(f"Artifact '{image_id}' has an unsafe relative path.")
        image_path = image_root / relative_path
        if not image_path.is_file():
            raise BenchmarkError(f"Artifact '{image_id}' is missing at '{image_path}'.")
        expected_hash = _required_text(artifact.get("sha256"), f"{image_id} sha256").lower()
        actual_hash = sha256_file(image_path)
        if actual_hash != expected_hash:
            raise BenchmarkError(
                f"Artifact '{image_id}' checksum mismatch: expected {expected_hash}, got {actual_hash}."
            )

    for split, expected_identities in expected_by_split.items():
        for identity_id in expected_identities:
            identity_artifacts = by_split_identity.get((split, identity_id), [])
            enrollments = [
                item for item in identity_artifacts if item.get("role") == "enrollment"
            ]
            probes = [item for item in identity_artifacts if item.get("role") == "probe"]
            if len(enrollments) != 1:
                raise BenchmarkError(
                    f"Identity '{identity_id}' in {split} must have exactly one enrollment image."
                )
            if split == "evaluation":
                probe_targets = [item.get("targetPercentage") for item in probes]
                if len(probes) != len(spec.targets) or set(probe_targets) != set(spec.targets):
                    raise BenchmarkError(
                        f"Evaluation identity '{identity_id}' must have exactly one probe "
                        "for every configured target."
                    )
            elif not probes:
                raise BenchmarkError(
                    f"Calibration identity '{identity_id}' must have at least one probe."
                )

    expected_pairs = build_pairs(artifacts)
    if manifest.get("pairs") != expected_pairs:
        raise BenchmarkError(
            "Manifest pairs are incomplete, out of order, or inconsistent with artifacts."
        )
    return manifest


def _identities(raw: Any, split: str) -> tuple[Identity, ...]:
    if not isinstance(raw, list) or len(raw) < 2:
        raise BenchmarkError(f"The {split} split must contain at least two identities.")
    result: list[Identity] = []
    ids: set[str] = set()
    seeds: set[int] = set()
    for item in raw:
        identity_id = _required_text(item.get("identityId"), f"{split} identityId")
        seed = item.get("seed")
        if not isinstance(seed, int) or isinstance(seed, bool) or seed < 0:
            raise BenchmarkError(f"Identity '{identity_id}' has an invalid seed.")
        if identity_id in ids:
            raise BenchmarkError(f"Duplicate {split} identityId '{identity_id}'.")
        if seed in seeds:
            raise BenchmarkError(f"Duplicate {split} seed '{seed}'.")
        ids.add(identity_id)
        seeds.add(seed)
        result.append(Identity(identity_id, seed))
    return tuple(result)


def _required_text(value: Any, field: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise BenchmarkError(f"{field} must be a non-empty string.")
    return value.strip()


def _percentage(value: Any, field: str) -> float:
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        raise BenchmarkError(f"{field} must be a number.")
    number = float(value)
    if not 0 <= number <= 100:
        raise BenchmarkError(f"{field} must be between 0 and 100.")
    return number


def _find_placeholders(value: Any, path: str = "") -> list[str]:
    if isinstance(value, dict):
        paths: list[str] = []
        for key, child in value.items():
            child_path = f"{path}.{key}" if path else key
            paths.extend(_find_placeholders(child, child_path))
        return paths
    if isinstance(value, list):
        paths = []
        for index, child in enumerate(value):
            paths.extend(_find_placeholders(child, f"{path}[{index}]"))
        return paths
    if isinstance(value, str) and ("REQUIRED" in value or value.startswith("PINNED_")):
        return [path]
    return []
