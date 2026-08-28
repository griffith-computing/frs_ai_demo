from __future__ import annotations

import unittest

from face_benchmark.errors import BenchmarkError
from face_benchmark.pairs import build_pairs


def _artifacts() -> list[dict[str, object]]:
    artifacts: list[dict[str, object]] = []
    for identity in ("one", "two"):
        artifacts.append(
            {
                "imageId": f"{identity}-enrollment",
                "identityId": identity,
                "split": "evaluation",
                "role": "enrollment",
            }
        )
        for target in (95, 90):
            artifacts.append(
                {
                    "imageId": f"{identity}-probe-{target}",
                    "identityId": identity,
                    "split": "evaluation",
                    "role": "probe",
                    "targetPercentage": target,
                }
            )
    return artifacts


class PairTests(unittest.TestCase):
    def test_builds_all_genuine_and_cross_identity_probe_pairs(self) -> None:
        pairs = build_pairs(_artifacts())

        genuine = [pair for pair in pairs if pair["sameIdentity"]]
        impostor = [pair for pair in pairs if not pair["sameIdentity"]]
        self.assertEqual(4, len(genuine))
        self.assertEqual(4, len(impostor))
        self.assertEqual({90, 95}, {pair["targetPercentage"] for pair in genuine})
        self.assertTrue(all(pair["targetPercentage"] is None for pair in impostor))

    def test_rejects_identity_without_enrollment(self) -> None:
        artifacts = [
            artifact for artifact in _artifacts() if artifact["imageId"] != "one-enrollment"
        ]

        with self.assertRaisesRegex(BenchmarkError, "has no enrollment"):
            build_pairs(artifacts)


if __name__ == "__main__":
    unittest.main()
