from __future__ import annotations

from pathlib import Path
from typing import Any

from .errors import BenchmarkError


class SFaceReference:
    def __init__(self, detector_path: Path, recognizer_path: Path) -> None:
        try:
            import cv2
        except ImportError as error:
            raise BenchmarkError(
                "Generation support is not installed. Run 'uv sync --extra generation'."
            ) from error
        self._cv2 = cv2
        self._detector = cv2.FaceDetectorYN.create(
            str(detector_path),
            "",
            (320, 320),
            0.7,
            0.3,
            5000,
        )
        self._recognizer = cv2.FaceRecognizerSF.create(str(recognizer_path), "")

    def feature(self, image: Any) -> Any:
        cv2 = self._cv2
        if not hasattr(image, "shape"):
            image = cv2.cvtColor(self._pil_to_array(image), cv2.COLOR_RGB2BGR)
        height, width = image.shape[:2]
        self._detector.setInputSize((width, height))
        _, faces = self._detector.detect(image)
        count = 0 if faces is None else len(faces)
        if count != 1:
            raise BenchmarkError(
                f"Reference detector expected exactly one face but found {count}."
            )
        aligned = self._recognizer.alignCrop(image, faces[0])
        return self._recognizer.feature(aligned)

    def score(self, first_feature: Any, second_feature: Any) -> float:
        return float(
            self._recognizer.match(
                first_feature,
                second_feature,
                self._cv2.FaceRecognizerSF_FR_COSINE,
            )
        )

    @staticmethod
    def _pil_to_array(image: Any) -> Any:
        try:
            import numpy
        except ImportError as error:
            raise BenchmarkError(
                "Generation support is not installed. Run 'uv sync --extra generation'."
            ) from error
        return numpy.asarray(image.convert("RGB"))
