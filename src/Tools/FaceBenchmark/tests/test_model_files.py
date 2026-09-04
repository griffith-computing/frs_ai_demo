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
import tempfile
import unittest
from pathlib import Path

from face_benchmark.errors import BenchmarkError
from face_benchmark.model_files import ensure_reference_models


class ModelFileTests(unittest.TestCase):
    def test_bundled_detector_requires_no_downloaded_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            model_root = root / "models"
            model_root.mkdir()
            recognizer = model_root / "recognizer.onnx"
            recognizer.write_bytes(b"recognizer")
            models = {
                "detector": {
                    "source": "opencv-package",
                    "filename": "haarcascade_frontalface_default.xml",
                },
                "recognizer": {
                    "url": "https://example.test/recognizer.onnx",
                    "sha256": hashlib.sha256(b"recognizer").hexdigest(),
                    "filename": "recognizer.onnx",
                },
            }

            paths = ensure_reference_models(models, model_root)

            self.assertIsNone(paths["detector"])
            self.assertEqual(recognizer.resolve(), paths["recognizer"])

    def test_local_recognizer_override_bypasses_download_and_checks_hash(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            recognizer = root / "approved-recognizer.onnx"
            recognizer.write_bytes(b"approved recognizer")
            models = {
                "detector": {"source": "opencv-package"},
                "recognizer": {
                    "url": "https://invalid.test/recognizer.onnx",
                    "sha256": hashlib.sha256(b"approved recognizer").hexdigest(),
                    "filename": "recognizer.onnx",
                },
            }

            paths = ensure_reference_models(
                models,
                root / "models",
                {"recognizer": recognizer},
            )

            self.assertEqual(recognizer.resolve(), paths["recognizer"])
            self.assertFalse((root / "models" / "recognizer.onnx").exists())

    def test_local_recognizer_override_rejects_wrong_hash(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            recognizer = root / "recognizer.onnx"
            recognizer.write_bytes(b"wrong file")
            models = {
                "detector": {"source": "opencv-package"},
                "recognizer": {
                    "url": "https://example.test/recognizer.onnx",
                    "sha256": "a" * 64,
                    "filename": "recognizer.onnx",
                },
            }

            with self.assertRaisesRegex(BenchmarkError, "checksum mismatch"):
                ensure_reference_models(
                    models,
                    root / "models",
                    {"recognizer": recognizer},
                )

    def test_rejects_filename_outside_model_directory_without_deleting_it(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            protected = root / "protected.onnx"
            protected.write_bytes(b"do not delete")
            models = {
                "detector": {
                    "url": "https://example.test/detector.onnx",
                    "sha256": "a" * 64,
                    "filename": "../protected.onnx",
                },
                "recognizer": {
                    "url": "https://example.test/recognizer.onnx",
                    "sha256": "b" * 64,
                    "filename": "recognizer.onnx",
                },
            }

            with self.assertRaisesRegex(BenchmarkError, "plain basename"):
                ensure_reference_models(models, root / "models")

            self.assertEqual(b"do not delete", protected.read_bytes())


if __name__ == "__main__":
    unittest.main()
