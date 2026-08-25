from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from face_benchmark.errors import BenchmarkError
from face_benchmark.model_files import ensure_reference_models


class ModelFileTests(unittest.TestCase):
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
