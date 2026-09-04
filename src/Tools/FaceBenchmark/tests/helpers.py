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
import csv
import hashlib
from pathlib import Path

from face_benchmark.pairs import build_pairs


def write_spec(path: Path) -> Path:
    spec = {
        "benchmarkVersion": "1.0.0",
        "name": "Test benchmark",
        "tolerancePercentagePoints": 5,
        "targets": [95, 90],
        "evaluationIdentities": [
            {"identityId": "evaluation-1", "seed": 1},
            {"identityId": "evaluation-2", "seed": 2},
        ],
        "calibrationIdentities": [
            {"identityId": "calibration-1", "seed": 3},
            {"identityId": "calibration-2", "seed": 4},
        ],
        "generator": {
            "name": "generator",
            "repository": "https://example.test/generator",
            "revision": "abc123",
            "modelUrl": "https://example.test/generator.bin",
            "modelSha256": "a" * 64,
        },
        "referenceModels": {
            "detector": {
                "name": "detector",
                "url": "https://example.test/detector.onnx",
                "sha256": "b" * 64,
            },
            "recognizer": {
                "name": "recognizer",
                "url": "https://example.test/recognizer.onnx",
                "sha256": "c" * 64,
            },
        },
    }
    path.write_text(json.dumps(spec), encoding="utf-8")
    return path


def write_scores(path: Path) -> Path:
    manifest_path = write_score_manifest(path.parent / "score-manifest.json")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(
            [
                "pair_id",
                "split",
                "enrollment_image_id",
                "probe_image_id",
                "same_identity",
                "target_percentage",
                "raw_score",
            ]
        )
        for pair in manifest["pairs"]:
            target = pair["targetPercentage"]
            if pair["sameIdentity"]:
                raw_score = (
                    target / 100 - 0.05
                    if target is not None
                    else (
                        0.9
                        if "calibration-1" in pair["enrollmentImageId"]
                        else 0.8
                    )
                )
            else:
                raw_score = (
                    0.2
                    if pair["enrollmentImageId"].endswith("1-enrollment")
                    else 0.1
                )
            writer.writerow(
                [
                    pair["pairId"],
                    pair["split"],
                    pair["enrollmentImageId"],
                    pair["probeImageId"],
                    str(pair["sameIdentity"]).lower(),
                    "" if target is None else target,
                    raw_score,
                ]
            )
    return path


def write_score_manifest(path: Path) -> Path:
    artifacts = []
    for split, identities in (
        ("calibration", ("calibration-1", "calibration-2")),
        ("evaluation", ("evaluation-1", "evaluation-2")),
    ):
        for identity in identities:
            artifacts.append(_artifact(path.parent, split, identity, "enrollment", None))
            targets = (None,) if split == "calibration" else (95, 90)
            for target in targets:
                artifacts.append(_artifact(path.parent, split, identity, "probe", target))
    path.write_text(
        json.dumps(
            {
                "benchmarkVersion": "1.0.0",
                "artifacts": artifacts,
                "pairs": build_pairs(artifacts),
            }
        ),
        encoding="utf-8",
    )
    return path


def _artifact(
    root: Path,
    split: str,
    identity: str,
    role: str,
    target: int | None,
) -> dict[str, object]:
    suffix = f"-{target:03d}" if target is not None else ""
    image_id = f"{identity}-{role}{suffix}"
    filename = f"{image_id}.img"
    contents = image_id.encode("utf-8")
    (root / filename).write_bytes(contents)
    return {
        "imageId": image_id,
        "identityId": identity,
        "split": split,
        "role": role,
        "path": filename,
        "sha256": hashlib.sha256(contents).hexdigest(),
        "targetPercentage": target,
    }
