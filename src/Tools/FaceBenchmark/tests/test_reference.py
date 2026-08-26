from __future__ import annotations

import sys
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from face_benchmark.errors import BenchmarkError
from face_benchmark.reference import SFaceReference, select_dominant_face


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

    def test_selects_dominant_central_face_over_small_false_positives(self) -> None:
        face = select_dominant_face(
            [
                (300, 250, 400, 400),
                (50, 50, 100, 100),
                (850, 80, 90, 90),
            ],
            1024,
            1024,
        )

        self.assertEqual((300, 250, 400, 400), face)

    def test_overlapping_detections_are_not_treated_as_multiple_people(self) -> None:
        face = select_dominant_face(
            [(300, 250, 400, 400), (320, 270, 360, 360)],
            1024,
            1024,
        )

        self.assertEqual((300, 250, 400, 400), face)

    def test_rejects_two_similarly_prominent_separate_faces(self) -> None:
        with self.assertRaisesRegex(BenchmarkError, "multiple similarly prominent"):
            select_dominant_face(
                [(100, 250, 350, 350), (580, 250, 340, 340)],
                1024,
                1024,
            )


if __name__ == "__main__":
    unittest.main()
