from __future__ import annotations

import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from face_benchmark.contract import Identity
from face_benchmark.generation import (
    FluxIdentityGenerator,
    _decode_image,
    _encode_image,
    _write_image,
)


class _FakeImage:
    def convert(self, mode: str) -> "_FakeImage":
        self.mode = mode
        return self


class _FakePipeline:
    loaded: tuple[str, dict[str, object]] | None = None

    @classmethod
    def from_pretrained(cls, model_id: str, **options: object) -> "_FakePipeline":
        cls.loaded = (model_id, options)
        return cls()

    def to(self, device: str) -> None:
        self.device = device

    def __call__(self, **options: object) -> object:
        self.call_options = options
        return types.SimpleNamespace(images=[_FakeImage()])


class _FakeGenerator:
    def __init__(self, device: str) -> None:
        self.device = device

    def manual_seed(self, seed: int) -> "_FakeGenerator":
        self.seed = seed
        return self


class GenerationTests(unittest.TestCase):
    def test_flux_generator_pins_revision_and_seed(self) -> None:
        fake_torch = types.SimpleNamespace(
            cuda=types.SimpleNamespace(is_available=lambda: False),
            float32="float32",
            bfloat16="bfloat16",
            Generator=_FakeGenerator,
        )
        fake_diffusers = types.SimpleNamespace(FluxPipeline=_FakePipeline)
        config = {
            "modelId": "owner/model",
            "revision": "immutable-revision",
            "inferenceSteps": 4,
            "guidanceScale": 0.0,
            "maxSequenceLength": 256,
        }

        with patch.dict(
            sys.modules,
            {"torch": fake_torch, "diffusers": fake_diffusers},
        ):
            generator = FluxIdentityGenerator(config)
            image = generator.generate(Identity("synthetic-1", 1234))

        self.assertEqual(
            ("owner/model", {"revision": "immutable-revision", "torch_dtype": "float32", "use_safetensors": True}),
            _FakePipeline.loaded,
        )
        self.assertEqual("RGB", image.mode)
        self.assertEqual(1234, generator._pipeline.call_options["generator"].seed)
        self.assertEqual(4, generator._pipeline.call_options["num_inference_steps"])
        self.assertEqual(0.0, generator._pipeline.call_options["guidance_scale"])

    def test_persisted_probe_bytes_are_the_scored_pixels(self) -> None:
        try:
            from PIL import Image
        except ImportError:
            self.skipTest("generation dependencies are not installed")
        image = Image.new("RGB", (64, 64), (80, 120, 160))
        encoded = _encode_image(image, "JPEG")
        scored_image = _decode_image(encoded)

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            relative_path = _write_image(
                encoded, root, "evaluation", "synthetic-1", "probe.jpg"
            )

            self.assertEqual(encoded, (root / relative_path).read_bytes())
            self.assertEqual(
                list(scored_image.getdata()),
                list(Image.open(root / relative_path).convert("RGB").getdata()),
            )


if __name__ == "__main__":
    unittest.main()
