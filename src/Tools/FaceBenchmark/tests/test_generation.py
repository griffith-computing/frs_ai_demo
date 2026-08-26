from __future__ import annotations

import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from face_benchmark.contract import Identity
from face_benchmark.errors import BenchmarkError
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


class _FakeTokenizer:
    calls: list[tuple[str, dict[str, object]]] = []

    @classmethod
    def from_pretrained(cls, model_id: str, **options: object) -> object:
        cls.calls.append((model_id, options))
        return types.SimpleNamespace(model_id=model_id, options=options)


class _SlowTokenizerFailure:
    @classmethod
    def from_pretrained(cls, model_id: str, **options: object) -> object:
        raise ValueError(
            "You set `add_prefix_space`. The tokenizer needs to be converted "
            "from the slow tokenizers"
        )


class GenerationTests(unittest.TestCase):
    def test_flux_generator_pins_revision_and_seed(self) -> None:
        fake_torch = types.SimpleNamespace(
            cuda=types.SimpleNamespace(is_available=lambda: False),
            float32="float32",
            bfloat16="bfloat16",
            Generator=_FakeGenerator,
        )
        fake_diffusers = types.SimpleNamespace(FluxPipeline=_FakePipeline)
        fake_transformers = types.SimpleNamespace(
            CLIPTokenizerFast=_FakeTokenizer,
            T5TokenizerFast=_FakeTokenizer,
        )
        config = {
            "modelId": "owner/model",
            "revision": "immutable-revision",
            "inferenceSteps": 4,
            "guidanceScale": 0.0,
            "maxSequenceLength": 256,
        }

        with patch.dict(
            sys.modules,
            {
                "torch": fake_torch,
                "diffusers": fake_diffusers,
                "transformers": fake_transformers,
            },
        ):
            _FakeTokenizer.calls.clear()
            generator = FluxIdentityGenerator(config)
            image = generator.generate(Identity("synthetic-1", 1234))

        loaded_model_id, loaded_options = _FakePipeline.loaded
        self.assertEqual("owner/model", loaded_model_id)
        self.assertEqual("immutable-revision", loaded_options["revision"])
        self.assertEqual("float32", loaded_options["torch_dtype"])
        self.assertTrue(loaded_options["use_safetensors"])
        self.assertEqual("owner/model", loaded_options["tokenizer"].model_id)
        self.assertEqual("owner/model", loaded_options["tokenizer_2"].model_id)
        self.assertEqual(
            [
                (
                    "owner/model",
                    {
                        "subfolder": "tokenizer",
                        "revision": "immutable-revision",
                        "add_prefix_space": False,
                    },
                ),
                (
                    "owner/model",
                    {
                        "subfolder": "tokenizer_2",
                        "revision": "immutable-revision",
                    },
                ),
            ],
            _FakeTokenizer.calls,
        )
        self.assertEqual("RGB", image.mode)
        self.assertEqual(1234, generator._pipeline.call_options["generator"].seed)
        self.assertEqual(4, generator._pipeline.call_options["num_inference_steps"])
        self.assertEqual(0.0, generator._pipeline.call_options["guidance_scale"])

    def test_slow_tokenizer_failure_has_actionable_refresh_command(self) -> None:
        fake_torch = types.SimpleNamespace(
            cuda=types.SimpleNamespace(is_available=lambda: False),
            float32="float32",
            bfloat16="bfloat16",
        )
        fake_transformers = types.SimpleNamespace(
            CLIPTokenizerFast=_SlowTokenizerFailure,
            T5TokenizerFast=_FakeTokenizer,
        )
        with patch.dict(
            sys.modules,
            {
                "torch": fake_torch,
                "diffusers": types.SimpleNamespace(FluxPipeline=_FakePipeline),
                "transformers": fake_transformers,
            },
        ):
            with self.assertRaisesRegex(BenchmarkError, "uv sync.*--refresh"):
                FluxIdentityGenerator(
                    {
                        "modelId": "owner/model",
                        "revision": "immutable-revision",
                    }
                )

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
