from __future__ import annotations

import hashlib
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

from .errors import BenchmarkError


def ensure_reference_models(
    reference_models: dict[str, Any], model_directory: Path
) -> dict[str, Path | None]:
    model_root = model_directory.resolve()
    model_root.mkdir(parents=True, exist_ok=True)
    result: dict[str, Path | None] = {}
    for role in ("detector", "recognizer"):
        model = reference_models.get(role)
        if not isinstance(model, dict):
            raise BenchmarkError(f"Reference model '{role}' is not configured.")
        if model.get("source") == "opencv-package":
            if role != "detector":
                raise BenchmarkError(
                    "Only the detector may use the OpenCV package as its source."
                )
            result[role] = None
            continue
        url = model.get("url")
        expected_hash = model.get("sha256")
        if not isinstance(url, str) or not url.startswith("https://"):
            raise BenchmarkError(f"Reference model '{role}' requires an HTTPS URL.")
        if (
            not isinstance(expected_hash, str)
            or len(expected_hash) != 64
            or any(character not in "0123456789abcdef" for character in expected_hash.lower())
        ):
            raise BenchmarkError(f"Reference model '{role}' has an invalid SHA-256.")
        filename = model.get("filename") or url.rsplit("/", 1)[-1]
        if (
            not isinstance(filename, str)
            or not filename
            or Path(filename).name != filename
            or Path(filename).is_absolute()
        ):
            raise BenchmarkError(
                f"Reference model '{role}' filename must be a plain basename."
            )
        path = (model_root / filename).resolve()
        if path.parent != model_root:
            raise BenchmarkError(
                f"Reference model '{role}' filename escapes the model directory."
            )
        if path.is_file() and _sha256(path) == expected_hash.lower():
            result[role] = path
            continue
        if path.exists():
            path.unlink()
        try:
            with urllib.request.urlopen(url, timeout=120) as response:
                with path.open("wb") as stream:
                    while chunk := response.read(1024 * 1024):
                        stream.write(chunk)
        except (OSError, urllib.error.URLError) as error:
            if path.exists():
                path.unlink()
            raise BenchmarkError(f"Failed to download reference model '{role}': {error}") from error
        actual_hash = _sha256(path)
        if actual_hash != expected_hash.lower():
            path.unlink()
            raise BenchmarkError(
                f"Reference model '{role}' checksum mismatch: "
                f"expected {expected_hash.lower()}, got {actual_hash}."
            )
        result[role] = path
    return result


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
