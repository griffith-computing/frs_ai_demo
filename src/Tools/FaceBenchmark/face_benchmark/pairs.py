from __future__ import annotations

from collections import defaultdict
from typing import Any

from .errors import BenchmarkError


def build_pairs(artifacts: list[dict[str, Any]]) -> list[dict[str, Any]]:
    by_split_identity: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    enrollments: dict[tuple[str, str], dict[str, Any]] = {}
    for artifact in artifacts:
        split = artifact.get("split")
        identity_id = artifact.get("identityId")
        role = artifact.get("role")
        image_id = artifact.get("imageId")
        if split not in {"calibration", "evaluation"}:
            raise BenchmarkError(f"Artifact '{image_id}' has invalid split '{split}'.")
        if not isinstance(identity_id, str) or not identity_id:
            raise BenchmarkError(f"Artifact '{image_id}' has no identityId.")
        if role not in {"enrollment", "probe"}:
            raise BenchmarkError(f"Artifact '{image_id}' has invalid role '{role}'.")
        key = (split, identity_id)
        by_split_identity[key].append(artifact)
        if role == "enrollment":
            if key in enrollments:
                raise BenchmarkError(
                    f"Identity '{identity_id}' in {split} has multiple enrollment images."
                )
            enrollments[key] = artifact

    pairs: list[dict[str, Any]] = []
    for (split, identity_id), identity_artifacts in sorted(by_split_identity.items()):
        enrollment = enrollments.get((split, identity_id))
        if enrollment is None:
            raise BenchmarkError(
                f"Identity '{identity_id}' in {split} has no enrollment image."
            )
        probes = sorted(
            (item for item in identity_artifacts if item["role"] == "probe"),
            key=lambda item: item["imageId"],
        )
        if not probes:
            raise BenchmarkError(f"Identity '{identity_id}' in {split} has no probes.")
        for probe in probes:
            pairs.append(
                _pair(
                    split,
                    enrollment,
                    probe,
                    same_identity=True,
                    target_percentage=probe.get("targetPercentage"),
                )
            )

        other_probes = sorted(
            (
                item
                for (other_split, other_identity), items in by_split_identity.items()
                if other_split == split and other_identity != identity_id
                for item in items
                if item["role"] == "probe"
            ),
            key=lambda item: item["imageId"],
        )
        for probe in other_probes:
            pairs.append(
                _pair(
                    split,
                    enrollment,
                    probe,
                    same_identity=False,
                    target_percentage=None,
                )
            )
    return pairs


def _pair(
    split: str,
    enrollment: dict[str, Any],
    probe: dict[str, Any],
    same_identity: bool,
    target_percentage: float | None,
) -> dict[str, Any]:
    kind = "genuine" if same_identity else "impostor"
    pair = {
        "pairId": f"{split}-{kind}-{enrollment['imageId']}--{probe['imageId']}",
        "split": split,
        "enrollmentImageId": enrollment["imageId"],
        "probeImageId": probe["imageId"],
        "sameIdentity": same_identity,
        "targetPercentage": target_percentage,
    }
    return pair
