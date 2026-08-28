from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from face_benchmark.contract import load_spec, validate_manifest
from face_benchmark.errors import BenchmarkError
from face_benchmark.pairs import build_pairs

from .helpers import write_spec


class ContractTests(unittest.TestCase):
    def test_load_spec_rejects_split_identity_overlap(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = write_spec(Path(directory) / "spec.json")
            raw = json.loads(path.read_text(encoding="utf-8"))
            raw["calibrationIdentities"][0]["identityId"] = "evaluation-1"
            path.write_text(json.dumps(raw), encoding="utf-8")

            with self.assertRaisesRegex(BenchmarkError, "identities overlap"):
                load_spec(path)

    def test_load_spec_rejects_unpinned_models(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = write_spec(Path(directory) / "spec.json")
            raw = json.loads(path.read_text(encoding="utf-8"))
            raw["generator"]["revision"] = "PINNED_REVISION_REQUIRED"
            path.write_text(json.dumps(raw), encoding="utf-8")

            with self.assertRaisesRegex(BenchmarkError, "pins are incomplete"):
                load_spec(path)

    def test_validate_manifest_checks_image_hash(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            spec = load_spec(write_spec(root / "spec.json"))
            artifacts = []
            for split, identities in (
                ("evaluation", ("evaluation-1", "evaluation-2")),
                ("calibration", ("calibration-1", "calibration-2")),
            ):
                for identity in identities:
                    roles = [("enrollment", None)]
                    roles += (
                        [("probe", 95), ("probe", 90)]
                        if split == "evaluation"
                        else [("probe", None)]
                    )
                    for index, (role, target) in enumerate(roles):
                        image_id = f"{identity}-{role}-{index}"
                        image = root / f"{image_id}.png"
                        contents = image_id.encode("utf-8")
                        image.write_bytes(contents)
                        artifacts.append(
                            {
                                "imageId": image_id,
                                "identityId": identity,
                                "split": split,
                                "role": role,
                                "path": image.name,
                                "sha256": hashlib.sha256(contents).hexdigest(),
                                "targetPercentage": target,
                            }
                        )
            manifest = {
                "benchmarkVersion": "1.0.0",
                "artifacts": artifacts,
                "pairs": build_pairs(artifacts),
            }
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

            result = validate_manifest(manifest_path, root, spec)

            self.assertEqual(10, len(result["artifacts"]))


if __name__ == "__main__":
    unittest.main()
