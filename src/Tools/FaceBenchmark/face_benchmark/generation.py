from __future__ import annotations

import io
import json
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from . import __version__
from .augmentation import augment
from .calibration import fit_isotonic
from .contract import BenchmarkSpec, Identity, sha256_file
from .errors import BenchmarkError
from .model_files import ensure_reference_models
from .pairs import build_pairs
from .reference import SFaceReference
from .scores import PairScore


BASE_PROMPT = (
    "photorealistic passport-style studio portrait of one unique fictional adult person, "
    "centered frontal face, neutral expression, even soft lighting, plain gray background, "
    "natural skin texture, 85mm portrait lens, no famous person"
)
CALIBRATION_STRENGTHS = (0.10, 0.28, 0.46, 0.64, 0.82, 1.0)


class FluxIdentityGenerator:
    def __init__(self, generator_config: dict[str, Any]) -> None:
        try:
            import torch
            from diffusers import FluxPipeline
            from transformers import CLIPTokenizerFast, T5TokenizerFast
        except ImportError as error:
            raise BenchmarkError(
                "Generation support is not installed. Run 'uv sync --extra generation'."
            ) from error
        model_id = generator_config.get("modelId")
        revision = generator_config.get("revision")
        if not isinstance(model_id, str) or not isinstance(revision, str):
            raise BenchmarkError("Generator modelId and revision are required.")
        self._torch = torch
        self._device = "cuda" if torch.cuda.is_available() else "cpu"
        dtype = torch.bfloat16 if self._device == "cuda" else torch.float32
        try:
            tokenizer = CLIPTokenizerFast.from_pretrained(
                model_id,
                subfolder="tokenizer",
                revision=revision,
                add_prefix_space=False,
            )
            tokenizer_2 = T5TokenizerFast.from_pretrained(
                model_id,
                subfolder="tokenizer_2",
                revision=revision,
            )
            self._pipeline = FluxPipeline.from_pretrained(
                model_id,
                revision=revision,
                torch_dtype=dtype,
                use_safetensors=True,
                tokenizer=tokenizer,
                tokenizer_2=tokenizer_2,
            )
        except ValueError as error:
            if "add_prefix_space" in str(error):
                raise BenchmarkError(
                    "FLUX requires the pinned fast tokenizer runtime. Run "
                    "'uv sync --project src/Tools/FaceBenchmark --extra generation "
                    "--refresh' and retry."
                ) from error
            raise
        if self._device == "cuda":
            self._pipeline.enable_model_cpu_offload()
        else:
            self._pipeline.to(self._device)
        self._steps = int(generator_config.get("inferenceSteps", 40))
        self._guidance = float(generator_config.get("guidanceScale", 0.0))
        self._max_sequence_length = int(
            generator_config.get("maxSequenceLength", 256)
        )

    def generate(self, identity: Identity) -> Any:
        generator = self._torch.Generator("cpu").manual_seed(identity.seed)
        return self._pipeline(
            prompt=BASE_PROMPT,
            width=1024,
            height=1024,
            num_inference_steps=self._steps,
            guidance_scale=self._guidance,
            max_sequence_length=self._max_sequence_length,
            generator=generator,
        ).images[0].convert("RGB")


def generate_library(
    spec: BenchmarkSpec,
    output_directory: Path,
    model_directory: Path,
    recognizer_model: Path | None = None,
) -> Path:
    output_directory.mkdir(parents=True, exist_ok=True)
    local_overrides = (
        {"recognizer": recognizer_model} if recognizer_model is not None else None
    )
    reference_paths = ensure_reference_models(
        spec.raw["referenceModels"],
        model_directory,
        local_overrides,
    )
    reference = SFaceReference(
        reference_paths["detector"], reference_paths["recognizer"]
    )
    generator = FluxIdentityGenerator(spec.raw["generator"])
    artifacts: list[dict[str, Any]] = []
    enrollment_images: dict[tuple[str, str], Any] = {}
    enrollment_features: dict[tuple[str, str], Any] = {}

    for split, identities in (
        ("calibration", spec.calibration_identities),
        ("evaluation", spec.evaluation_identities),
    ):
        for identity in identities:
            generated_image = generator.generate(identity)
            encoded = _encode_image(generated_image, "PNG")
            image = _decode_image(encoded)
            feature = reference.feature(image)
            path = _write_image(
                encoded,
                output_directory,
                split,
                identity.identity_id,
                f"{identity.identity_id}-enrollment.png",
            )
            enrollment_images[(split, identity.identity_id)] = image
            enrollment_features[(split, identity.identity_id)] = feature
            artifacts.append(
                _artifact(
                    image_id=f"{identity.identity_id}-enrollment",
                    identity_id=identity.identity_id,
                    split=split,
                    role="enrollment",
                    path=path,
                    output_directory=output_directory,
                    target=None,
                    reference_percentage=100.0,
                    parameters={"generatorSeed": identity.seed},
                )
            )

    calibration_features: dict[str, Any] = {}
    for identity in spec.calibration_identities:
        enrollment = enrollment_images[("calibration", identity.identity_id)]
        for index, strength in enumerate(CALIBRATION_STRENGTHS, start=1):
            probe, parameters = augment(
                enrollment,
                strength,
                identity.seed * 100 + index,
            )
            encoded = _encode_image(probe, "JPEG")
            persisted_probe = _decode_image(encoded)
            feature = reference.feature(persisted_probe)
            image_id = f"{identity.identity_id}-probe-{index:03d}"
            path = _write_image(
                encoded,
                output_directory,
                "calibration",
                identity.identity_id,
                f"{image_id}.jpg",
            )
            calibration_features[image_id] = feature
            artifacts.append(
                _artifact(
                    image_id=image_id,
                    identity_id=identity.identity_id,
                    split="calibration",
                    role="probe",
                    path=path,
                    output_directory=output_directory,
                    target=None,
                    reference_percentage=None,
                    parameters=parameters.to_dict(),
                )
            )

    calibration_rows = _reference_calibration_rows(
        spec,
        artifacts,
        enrollment_features,
        calibration_features,
        reference,
    )
    calibration = fit_isotonic(calibration_rows, "higher-is-match")

    for artifact in artifacts:
        if artifact["split"] == "calibration" and artifact["role"] == "probe":
            identity_id = artifact["identityId"]
            raw_score = reference.score(
                enrollment_features[("calibration", identity_id)],
                calibration_features[artifact["imageId"]],
            )
            artifact["referencePercentage"] = calibration.normalize(raw_score)

    for identity in spec.evaluation_identities:
        enrollment = enrollment_images[("evaluation", identity.identity_id)]
        enrollment_feature = enrollment_features[("evaluation", identity.identity_id)]
        candidates, failures = _candidate_pool(
            enrollment,
            enrollment_feature,
            identity,
            reference,
            calibration,
        )
        selected: set[tuple[int, int]] = set()
        for target in spec.targets:
            candidate = _select_candidate(
                identity,
                target,
                spec.tolerance,
                selected,
                candidates,
                failures,
            )
            encoded, feature, parameters, normalized, candidate_key = candidate
            selected.add(candidate_key)
            image_id = f"{identity.identity_id}-probe-{round(target):03d}"
            path = _write_image(
                encoded,
                output_directory,
                "evaluation",
                identity.identity_id,
                f"{image_id}.jpg",
            )
            artifacts.append(
                _artifact(
                    image_id=image_id,
                    identity_id=identity.identity_id,
                    split="evaluation",
                    role="probe",
                    path=path,
                    output_directory=output_directory,
                    target=target,
                    reference_percentage=normalized,
                    parameters=parameters.to_dict(),
                )
            )

    manifest = {
        "benchmarkVersion": spec.version,
        "generatedAtUtc": datetime.now(UTC).isoformat(),
        "toolVersion": __version__,
        "generator": spec.raw["generator"],
        "referenceModels": spec.raw["referenceModels"],
        "referenceCalibration": calibration.to_dict(),
        "artifacts": artifacts,
        "pairs": build_pairs(artifacts),
    }
    manifest_path = output_directory / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest_path


def _reference_calibration_rows(
    spec: BenchmarkSpec,
    artifacts: list[dict[str, Any]],
    enrollment_features: dict[tuple[str, str], Any],
    probe_features: dict[str, Any],
    reference: SFaceReference,
) -> list[PairScore]:
    rows: list[PairScore] = []
    calibration_probes = [
        item
        for item in artifacts
        if item["split"] == "calibration" and item["role"] == "probe"
    ]
    for identity in spec.calibration_identities:
        enrollment_id = f"{identity.identity_id}-enrollment"
        enrollment_feature = enrollment_features[("calibration", identity.identity_id)]
        for probe in calibration_probes:
            same_identity = probe["identityId"] == identity.identity_id
            rows.append(
                PairScore(
                    pair_id=f"reference-{enrollment_id}--{probe['imageId']}",
                    split="calibration",
                    enrollment_image_id=enrollment_id,
                    probe_image_id=probe["imageId"],
                    same_identity=same_identity,
                    target_percentage=None,
                    raw_score=reference.score(
                        enrollment_feature, probe_features[probe["imageId"]]
                    ),
                )
            )
    return rows


def _candidate_pool(
    enrollment: Any,
    enrollment_feature: Any,
    identity: Identity,
    reference: SFaceReference,
    calibration: Any,
) -> tuple[list[tuple[bytes, Any, Any, float, tuple[int, int]]], int]:
    candidates: list[tuple[bytes, Any, Any, float, tuple[int, int]]] = []
    failures = 0
    for strength_index in range(1, 41):
        strength = strength_index / 40
        for variant in range(6):
            key = (strength_index, variant)
            seed = identity.seed * 10000 + strength_index * 10 + variant
            probe, parameters = augment(enrollment, strength, seed)
            encoded = _encode_image(probe, "JPEG")
            persisted_probe = _decode_image(encoded)
            try:
                feature = reference.feature(persisted_probe)
            except BenchmarkError:
                failures += 1
                continue
            normalized = calibration.normalize(
                reference.score(enrollment_feature, feature)
            )
            candidates.append((encoded, feature, parameters, normalized, key))
    return candidates, failures


def _select_candidate(
    identity: Identity,
    target: float,
    tolerance: float,
    selected: set[tuple[int, int]],
    candidates: list[tuple[bytes, Any, Any, float, tuple[int, int]]],
    failures: int,
) -> tuple[bytes, Any, Any, float, tuple[int, int]]:
    available = [candidate for candidate in candidates if candidate[4] not in selected]
    best = min(available, key=lambda candidate: abs(candidate[3] - target), default=None)
    if best is None:
        raise BenchmarkError(
            f"No valid probe candidates remained for identity '{identity.identity_id}' "
            f"and target {target}; {failures} candidates failed face detection."
        )
    best_error = abs(best[3] - target)
    if best_error > tolerance:
        raise BenchmarkError(
            f"Identity '{identity.identity_id}' could not reach target {target} within "
            f"±{tolerance} points; closest candidate was {best[3]:.2f}."
        )
    return best


def _encode_image(image: Any, image_format: str) -> bytes:
    stream = io.BytesIO()
    save_options = (
        {"quality": 95, "subsampling": 0, "optimize": False, "progressive": False}
        if image_format == "JPEG"
        else {}
    )
    image.save(stream, format=image_format, **save_options)
    return stream.getvalue()


def _decode_image(encoded: bytes) -> Any:
    try:
        from PIL import Image
    except ImportError as error:
        raise BenchmarkError(
            "Generation support is not installed. Run 'uv sync --extra generation'."
        ) from error
    with Image.open(io.BytesIO(encoded)) as image:
        return image.convert("RGB").copy()


def _write_image(
    encoded: bytes,
    output_directory: Path,
    split: str,
    identity_id: str,
    filename: str,
) -> Path:
    relative_path = Path(split) / identity_id / filename
    path = output_directory / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(encoded)
    return relative_path


def _artifact(
    image_id: str,
    identity_id: str,
    split: str,
    role: str,
    path: Path,
    output_directory: Path,
    target: float | None,
    reference_percentage: float | None,
    parameters: dict[str, Any],
) -> dict[str, Any]:
    return {
        "imageId": image_id,
        "identityId": identity_id,
        "split": split,
        "role": role,
        "path": path.as_posix(),
        "sha256": sha256_file(output_directory / path),
        "targetPercentage": target,
        "referencePercentage": reference_percentage,
        "parameters": parameters,
    }
