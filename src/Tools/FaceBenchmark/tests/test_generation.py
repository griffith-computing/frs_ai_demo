from __future__ import annotations

import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from face_benchmark.contract import Identity
from face_benchmark.generation import (
    StableDiffusionIdentityGenerator,
    _decode_image,
    _encode_image,
    _write_image,
)


class _FakeImage:
    size = (512, 512)

    def convert(self, mode: str) -> "_FakeImage":
        self.mode = mode
        return self

    def resize(self, size: tuple[int, int], resample: object) -> "_FakeImage":
        self.size = size
        self.resample = resample
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
    def test_stable_diffusion_generator_pins_revision_seed_and_resolution(self) -> None:
        fake_torch = types.SimpleNamespace(
            cuda=types.SimpleNamespace(is_available=lambda: False),
            float32="float32",
            float16="float16",
            Generator=_FakeGenerator,
        )
        fake_diffusers = types.SimpleNamespace(
            StableDiffusionPipeline=_FakePipeline
        )
        config = {
            "modelId": "owner/model",
            "revision": "immutable-revision",
            "inferenceSteps": 30,
            "guidanceScale": 7.5,
            "nativeResolution": 512,
            "artifactResolution": 1024,
        }

        with patch.dict(
            sys.modules,
            {
                "torch": fake_torch,
                "diffusers": fake_diffusers,
            },
        ):
            generator = StableDiffusionIdentityGenerator(config)
            image = generator.generate(Identity("synthetic-1", 1234))

        loaded_model_id, loaded_options = _FakePipeline.loaded
        self.assertEqual("owner/model", loaded_model_id)
        self.assertEqual("immutable-revision", loaded_options["revision"])
        self.assertEqual("float32", loaded_options["torch_dtype"])
        self.assertTrue(loaded_options["use_safetensors"])
        self.assertTrue(loaded_options["low_cpu_mem_usage"])
        self.assertIsNone(loaded_options["safety_checker"])
        self.assertFalse(loaded_options["requires_safety_checker"])
        self.assertEqual("RGB", image.mode)
        self.assertEqual((1024, 1024), image.size)
        self.assertEqual(1234, generator._pipeline.call_options["generator"].seed)
        self.assertEqual(30, generator._pipeline.call_options["num_inference_steps"])
        self.assertEqual(7.5, generator._pipeline.call_options["guidance_scale"])
        self.assertEqual(512, generator._pipeline.call_options["width"])
        self.assertEqual(512, generator._pipeline.call_options["height"])
        self.assertIn("celebrity", generator._pipeline.call_options["negative_prompt"])

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
