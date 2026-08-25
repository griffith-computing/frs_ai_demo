from __future__ import annotations

import sys
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from face_benchmark.reference import SFaceReference


class _FakeCascade:
    def __init__(self, path: str) -> None:
        self.path = path

    def empty(self) -> bool:
        return False


class _FakeRecognizerFactory:
    @staticmethod
    def create(path: str, config: str) -> object:
        return object()


class ReferenceTests(unittest.TestCase):
    def test_bundled_detector_loads_without_yunet_model(self) -> None:
        detector_calls = []
        fake_cv2 = types.SimpleNamespace(
            data=types.SimpleNamespace(haarcascades="bundled-cascades"),
            CascadeClassifier=_FakeCascade,
            FaceRecognizerSF=_FakeRecognizerFactory,
            FaceDetectorYN=types.SimpleNamespace(
                create=lambda *args: detector_calls.append(args)
            ),
        )

        with patch.dict(sys.modules, {"cv2": fake_cv2}):
            reference = SFaceReference(None, Path("recognizer.onnx"))

        self.assertIsNone(reference._detector)
        self.assertEqual([], detector_calls)
        self.assertTrue(
            reference._haar_detector.path.endswith(
                "haarcascade_frontalface_default.xml"
            )
        )


if __name__ == "__main__":
    unittest.main()
