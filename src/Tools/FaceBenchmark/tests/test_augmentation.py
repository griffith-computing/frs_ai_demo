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
import importlib.util
import io
import unittest

from face_benchmark.augmentation import augment


GENERATION_DEPENDENCIES_AVAILABLE = all(
    importlib.util.find_spec(package) is not None for package in ("numpy", "PIL")
)


@unittest.skipUnless(
    GENERATION_DEPENDENCIES_AVAILABLE,
    "generation dependencies are not installed",
)
class AugmentationTests(unittest.TestCase):
    def test_augmentation_is_deterministic_for_seed(self) -> None:
        from PIL import Image

        image = Image.new("RGB", (128, 128), (120, 150, 180))

        first, first_parameters = augment(image, 0.7, 12345)
        second, second_parameters = augment(image, 0.7, 12345)

        self.assertEqual(first_parameters, second_parameters)
        self.assertEqual(_image_hash(first), _image_hash(second))

    def test_augmentation_rejects_invalid_strength(self) -> None:
        from PIL import Image
        from face_benchmark.errors import BenchmarkError
        with self.assertRaisesRegex(BenchmarkError, "between 0 and 1"):
            augment(Image.new("RGB", (32, 32)), 1.1, 1)


def _image_hash(image: object) -> str:
    stream = io.BytesIO()
    image.save(stream, format="PNG")  # type: ignore[attr-defined]
    return hashlib.sha256(stream.getvalue()).hexdigest()


if __name__ == "__main__":
    unittest.main()
