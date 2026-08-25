from __future__ import annotations

import io
import random
from dataclasses import dataclass, asdict
from typing import Any

from .errors import BenchmarkError


@dataclass(frozen=True)
class AugmentationParameters:
    strength: float
    rotation_degrees: float
    zoom: float
    brightness: float
    contrast: float
    blur_radius: float
    noise_standard_deviation: float
    jpeg_quality: int
    occlusion_fraction: float

    def to_dict(self) -> dict[str, float | int]:
        return asdict(self)


def augment(image: Any, strength: float, seed: int) -> tuple[Any, AugmentationParameters]:
    try:
        import numpy
        from PIL import Image, ImageEnhance, ImageFilter, ImageOps
    except ImportError as error:
        raise BenchmarkError(
            "Generation support is not installed. Run 'uv sync --extra generation'."
        ) from error
    if not 0 <= strength <= 1:
        raise BenchmarkError("Augmentation strength must be between 0 and 1.")

    randomizer = random.Random(seed)
    rotation = randomizer.uniform(-12, 12) * strength
    zoom = 1.0 + randomizer.uniform(0.02, 0.30) * strength
    brightness = 1.0 + randomizer.uniform(-0.38, 0.32) * strength
    contrast = 1.0 + randomizer.uniform(-0.28, 0.38) * strength
    blur = randomizer.uniform(0.2, 5.0) * strength
    noise = randomizer.uniform(2.0, 26.0) * strength
    jpeg_quality = round(96 - randomizer.uniform(10, 48) * strength)
    occlusion = (
        randomizer.uniform(0.04, 0.18) * strength
        if strength >= 0.55 and randomizer.random() < 0.55
        else 0.0
    )
    parameters = AugmentationParameters(
        strength=strength,
        rotation_degrees=rotation,
        zoom=zoom,
        brightness=brightness,
        contrast=contrast,
        blur_radius=blur,
        noise_standard_deviation=noise,
        jpeg_quality=jpeg_quality,
        occlusion_fraction=occlusion,
    )

    source = image.convert("RGB")
    width, height = source.size
    transformed = source.rotate(
        rotation,
        resample=Image.Resampling.BICUBIC,
        expand=False,
        fillcolor=(127, 127, 127),
    )
    crop_width = max(1, round(width / zoom))
    crop_height = max(1, round(height / zoom))
    horizontal_shift = round(randomizer.uniform(-0.04, 0.04) * width * strength)
    vertical_shift = round(randomizer.uniform(-0.03, 0.04) * height * strength)
    left = max(0, min(width - crop_width, (width - crop_width) // 2 + horizontal_shift))
    top = max(0, min(height - crop_height, (height - crop_height) // 2 + vertical_shift))
    transformed = transformed.crop(
        (left, top, left + crop_width, top + crop_height)
    ).resize((width, height), Image.Resampling.LANCZOS)
    transformed = ImageEnhance.Brightness(transformed).enhance(brightness)
    transformed = ImageEnhance.Contrast(transformed).enhance(contrast)
    if blur > 0:
        transformed = transformed.filter(ImageFilter.GaussianBlur(blur))
    if noise > 0:
        pixels = numpy.asarray(transformed, dtype=numpy.float32)
        noise_generator = numpy.random.default_rng(seed)
        pixels += noise_generator.normal(0, noise, pixels.shape)
        transformed = Image.fromarray(numpy.clip(pixels, 0, 255).astype(numpy.uint8))
    if occlusion > 0:
        occlusion_height = max(1, round(height * occlusion))
        center = round(height * randomizer.uniform(0.42, 0.62))
        top = max(0, min(height - occlusion_height, center - occlusion_height // 2))
        overlay = Image.new("RGB", (width, occlusion_height), (45, 45, 45))
        transformed.paste(overlay, (0, top))

    encoded = io.BytesIO()
    transformed.save(
        encoded,
        format="JPEG",
        quality=jpeg_quality,
        subsampling=0,
        optimize=False,
        progressive=False,
    )
    encoded.seek(0)
    return Image.open(encoded).convert("RGB"), parameters
