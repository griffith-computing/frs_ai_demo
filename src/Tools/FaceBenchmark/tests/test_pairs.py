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
