from __future__ import annotations

from pathlib import Path
from typing import Any

from .errors import BenchmarkError


class SFaceReference:
    def __init__(self, detector_path: Path | None, recognizer_path: Path) -> None:
        try:
            import cv2
        except ImportError as error:
            raise BenchmarkError(
                "Generation support is not installed. Run 'uv sync --extra generation'."
            ) from error
        self._cv2 = cv2
        self._detector = (
            cv2.FaceDetectorYN.create(
                str(detector_path),
                "",
                (320, 320),
                0.7,
                0.3,
                5000,
            )
            if detector_path is not None
            else None
        )
        self._haar_detector = None
        if detector_path is None:
            cascade_path = (
                Path(cv2.data.haarcascades) / "haarcascade_frontalface_default.xml"
            )
            self._haar_detector = cv2.CascadeClassifier(str(cascade_path))
            if self._haar_detector.empty():
                raise BenchmarkError(
                    f"OpenCV bundled face detector could not be loaded from '{cascade_path}'."
                )
        self._recognizer = cv2.FaceRecognizerSF.create(str(recognizer_path), "")

    def feature(self, image: Any, allow_center_fallback: bool = False) -> Any:
        cv2 = self._cv2
        if not hasattr(image, "shape"):
            image = cv2.cvtColor(self._pil_to_array(image), cv2.COLOR_RGB2BGR)
        if self._detector is None:
            return self._haar_feature(image, allow_center_fallback)
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

    def _haar_feature(self, image: Any, allow_center_fallback: bool) -> Any:
        cv2 = self._cv2
        image_height, image_width = image.shape[:2]
        minimum_face_size = max(80, min(image_width, image_height) // 6)
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        faces = self._haar_detector.detectMultiScale(
            cv2.equalizeHist(gray),
            scaleFactor=1.1,
            minNeighbors=6,
            minSize=(minimum_face_size, minimum_face_size),
        )
        try:
            x, y, width, height = select_dominant_face(
                [tuple(int(value) for value in face) for face in faces],
                image_width,
                image_height,
            )
        except BenchmarkError:
            if allow_center_fallback:
                return self._center_crop_feature(image)
            raise
        side = round(max(width, height) * 1.35)
        center_x = x + width // 2
        center_y = y + height // 2
        left = max(0, center_x - side // 2)
        top = max(0, center_y - side // 2)
        right = min(image.shape[1], left + side)
        bottom = min(image.shape[0], top + side)
        left = max(0, right - side)
        top = max(0, bottom - side)
        crop = image[top:bottom, left:right]
        if crop.size == 0:
            raise BenchmarkError("Bundled reference detector produced an empty face crop.")
        aligned = cv2.resize(crop, (112, 112), interpolation=cv2.INTER_AREA)
        return self._recognizer.feature(aligned)

    def _center_crop_feature(self, image: Any) -> Any:
        image_height, image_width = image.shape[:2]
        side = round(min(image_width, image_height) * 0.72)
        left = (image_width - side) // 2
        top = (image_height - side) // 2
        crop = image[top : top + side, left : left + side]
        aligned = self._cv2.resize(
            crop,
            (112, 112),
            interpolation=self._cv2.INTER_AREA,
        )
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


def select_dominant_face(
    faces: list[tuple[int, int, int, int]],
    image_width: int,
    image_height: int,
) -> tuple[int, int, int, int]:
    if not faces:
        raise BenchmarkError("Bundled reference detector found no face.")

    image_center_x = image_width / 2
    image_center_y = image_height / 2
    diagonal = max((image_width**2 + image_height**2) ** 0.5, 1.0)

    def rank(face: tuple[int, int, int, int]) -> float:
        x, y, width, height = face
        center_x = x + width / 2
        center_y = y + height / 2
        distance = (
            (center_x - image_center_x) ** 2
            + (center_y - image_center_y) ** 2
        ) ** 0.5
        centrality = max(0.25, 1.0 - distance / diagonal)
        return width * height * centrality

    ranked = sorted(faces, key=rank, reverse=True)
    primary = ranked[0]
    primary_area = primary[2] * primary[3]
    for candidate in ranked[1:]:
        candidate_area = candidate[2] * candidate[3]
        if (
            candidate_area >= primary_area * 0.60
            and _intersection_over_union(primary, candidate) < 0.20
        ):
            raise BenchmarkError(
                "Bundled reference detector found multiple similarly prominent faces."
            )
    return primary


def _intersection_over_union(
    first: tuple[int, int, int, int],
    second: tuple[int, int, int, int],
) -> float:
    first_x, first_y, first_width, first_height = first
    second_x, second_y, second_width, second_height = second
    left = max(first_x, second_x)
    top = max(first_y, second_y)
    right = min(first_x + first_width, second_x + second_width)
    bottom = min(first_y + first_height, second_y + second_height)
    intersection = max(0, right - left) * max(0, bottom - top)
    union = first_width * first_height + second_width * second_height - intersection
    return intersection / union if union else 0.0
