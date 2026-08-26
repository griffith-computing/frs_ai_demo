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
    _candidate_pool,
    _decode_image,
    _encode_image,
    _generate_enrollment,
    _write_image,
)
from face_benchmark.errors import BenchmarkError


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

    def test_enrollment_retries_ambiguous_generated_face_deterministically(self) -> None:
        try:
            from PIL import Image
        except ImportError:
            self.skipTest("generation dependencies are not installed")

        class Generator:
            def __init__(self) -> None:
                self.seeds: list[int] = []

            def generate(self, identity: Identity) -> object:
                self.seeds.append(identity.seed)
                return Image.new("RGB", (64, 64), (120, 140, 160))

        class Reference:
            def __init__(self) -> None:
                self.calls = 0

            def feature(self, image: object) -> object:
                self.calls += 1
                if self.calls == 1:
                    raise BenchmarkError(
                        "Bundled reference detector found multiple similarly prominent faces."
                    )
                return "feature"

        generator = Generator()
        _, _, feature, seed = _generate_enrollment(
            generator, Reference(), Identity("identity-1", 42)
        )

        self.assertEqual("feature", feature)
        self.assertEqual(1_000_045, seed)
        self.assertEqual([42, 1_000_045], generator.seeds)

    def test_candidate_pool_spans_genuine_to_donor_scores(self) -> None:
        try:
            from PIL import Image
        except ImportError:
            self.skipTest("generation dependencies are not installed")

        class Reference:
            @staticmethod
            def feature(image: object, allow_center_fallback: bool = False) -> float:
                return image.getpixel((0, 0))[0] / 255

            @staticmethod
            def score(enrollment_feature: float, feature: float) -> float:
                return feature

        class Calibration:
            @staticmethod
            def normalize(score: float) -> float:
                return score * 100

        source = Image.new("RGB", (8, 8), (0, 0, 0))
        donor = Image.new("RGB", (8, 8), (255, 255, 255))
        candidates, failures = _candidate_pool(
            source,
            0.0,
            Identity("identity-1", 42),
            [("identity-2", donor)],
            (95.0, 75.0, 55.0),
            Reference(),
            Calibration(),
        )

        self.assertEqual(0, failures)
        scores = [candidate[3] for candidate in candidates]
        self.assertGreater(len(candidates), 21)
        self.assertEqual(0.0, min(scores))
        self.assertEqual(100.0, max(scores))
        for target in (95.0, 75.0, 55.0):
            self.assertLess(min(abs(score - target) for score in scores), 0.5)
        donor_endpoint = next(
            candidate
            for candidate in candidates
            if candidate[2].morph_fraction == 1.0
        )
        self.assertEqual(
            {
                "morph_fraction": 1.0,
                "donor_identity_id": "identity-2",
            },
            donor_endpoint[2].to_dict(),
        )


if __name__ == "__main__":
    unittest.main()
