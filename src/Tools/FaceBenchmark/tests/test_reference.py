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

    def test_probe_can_use_deterministic_center_crop_when_detection_fails(self) -> None:
        try:
            import numpy
        except ImportError:
            self.skipTest("generation dependencies are not installed")

        class Detector:
            @staticmethod
            def detectMultiScale(*args: object, **kwargs: object) -> list[object]:
                return []

        class Recognizer:
            @staticmethod
            def feature(image: object) -> tuple[int, ...]:
                return image.shape

        fake_cv2 = types.SimpleNamespace(
            COLOR_BGR2GRAY=1,
            INTER_AREA=2,
            cvtColor=lambda image, mode: image[:, :, 0],
            equalizeHist=lambda image: image,
            resize=lambda image, size, interpolation: numpy.zeros(
                (size[1], size[0], 3), dtype=numpy.uint8
            ),
        )
        reference = object.__new__(SFaceReference)
        reference._cv2 = fake_cv2
        reference._haar_detector = Detector()
        reference._recognizer = Recognizer()
        image = numpy.zeros((1024, 1024, 3), dtype=numpy.uint8)

        with self.assertRaisesRegex(BenchmarkError, "found no face"):
            reference._haar_feature(image, allow_center_fallback=False)
        self.assertEqual(
            (112, 112, 3),
            reference._haar_feature(image, allow_center_fallback=True),
        )

    def test_features_own_their_memory_when_recognizer_reuses_output(self) -> None:
        try:
            import numpy
        except ImportError:
            self.skipTest("generation dependencies are not installed")

        shared = numpy.zeros((1, 4), dtype=numpy.float32)

        class Recognizer:
            @staticmethod
            def feature(image: object) -> object:
                shared.fill(float(image))
                return shared

        reference = object.__new__(SFaceReference)
        reference._recognizer = Recognizer()

        first = reference._owned_feature(1)
        second = reference._owned_feature(2)

        self.assertEqual([1.0, 1.0, 1.0, 1.0], first.tolist()[0])
        self.assertEqual([2.0, 2.0, 2.0, 2.0], second.tolist()[0])
        self.assertIsNot(first, second)

    def test_detected_probe_uses_same_fixed_crop_as_fallback(self) -> None:
        try:
            import numpy
        except ImportError:
            self.skipTest("generation dependencies are not installed")

        class Detector:
            @staticmethod
            def detectMultiScale(*args: object, **kwargs: object) -> list[object]:
                return [(100, 120, 500, 500)]

        class Recognizer:
            @staticmethod
            def feature(image: object) -> object:
                return image.copy()

        fake_cv2 = types.SimpleNamespace(
            COLOR_BGR2GRAY=1,
            INTER_AREA=2,
            cvtColor=lambda image, mode: image[:, :, 0],
            equalizeHist=lambda image: image,
            resize=lambda image, size, interpolation: numpy.full(
                (size[1], size[0], 3), image.shape[0], dtype=numpy.int32
            ),
        )
        reference = object.__new__(SFaceReference)
        reference._cv2 = fake_cv2
        reference._haar_detector = Detector()
        reference._recognizer = Recognizer()
        image = numpy.zeros((1000, 800, 3), dtype=numpy.uint8)

        feature = reference._haar_feature(image, allow_center_fallback=False)

        self.assertTrue(numpy.all(feature == 576))


if __name__ == "__main__":
    unittest.main()
