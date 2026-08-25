# Synthetic face verification benchmark

This package measures how closely a face-verification SDK reproduces a fixed set
of expected match levels while also measuring whether it separates genuine
same-identity pairs from impostor different-identity pairs.

The guide is vendor-neutral and can be sent directly to an SDK provider or
customer. It does not require access to the demo application's source or Azure
infrastructure unless the Azure adapter is used.

## What is in the benchmark

The evaluation set contains 20 synthetic identities. Each identity has:

- one enrollment image;
- nine probe images with normalized targets of 95, 90, 85, 80, 75, 70, 65,
  60, and 55 percent; and
- genuine comparisons against its own enrollment plus impostor comparisons
  against every other evaluation identity.

A separate set of 10 synthetic identities is used only for calibration. No
calibration image or identity appears in evaluation. This separation prevents
the score conversion from being fitted to the answers it will later score.

Generated images, downloaded models, calibrators, and reports are intentionally
not committed. `spec.json` fixes the identities, seeds, target levels,
tolerance, generator revision, and reference-model configuration. A generated
`manifest.json` records every file hash and all transformation parameters.

## What the tests measure

| Measure | Meaning |
|---|---|
| Raw confidence | The unmodified score returned by the tested SDK. Its scale is vendor-specific. |
| Normalized match percentage | The SDK's raw score converted through a calibration fitted only on reserved calibration identities. |
| Absolute target error | Absolute difference, in percentage points, between normalized percentage and the predefined target. |
| Within-tolerance rate | Share of genuine evaluation probes within ±5 percentage points of their target. |
| Identity accuracy | Share of genuine and impostor pairs correctly classified at a normalized 50% operating point. |
| ROC AUC | Probability that a randomly chosen genuine pair ranks above a randomly chosen impostor pair. Higher is better; 0.5 is chance. |
| False accept rate (FAR) | Share of impostor pairs incorrectly accepted as matches at the 50% operating point. |
| False reject rate (FRR) | Share of genuine pairs incorrectly rejected at the 50% operating point. |
| TAR at FAR | True accept rate at a constrained false accept rate. The report includes FAR targets of 1% and 0.1%. |

The primary predefined-target pass rule is **100% of target-bearing probes
within ±5 percentage points**. The tool exits with code `1` when that rule is
missed, `2` for invalid input or execution failure, and `0` on pass.

The other measures are always reported because agreement with a reference score
is not, by itself, proof of recognition accuracy. Customers should compare the
full metric set and select operating thresholds appropriate to their risk.

## Why calibration is required

An SDK confidence of `0.8`, `80`, or `80%` does not have a universal meaning.
Vendors use different models, preprocessing, similarity measures, thresholds,
and score ranges. Comparing raw scores as if they were percentages produces
misleading results.

For each SDK and model version, this benchmark fits a monotonic isotonic mapping
from raw calibration scores to empirical same-identity probability. The mapping
is then frozen and applied to the held-out evaluation set. Raw scores remain in
the report for auditability. A calibration may never be reused for a different
SDK, model version, score mode, or materially different preprocessing pipeline.

## Prerequisites

- Windows, macOS, or Linux with Git.
- `uv` 0.12 or later: <https://docs.astral.sh/uv/>.
- A Python version supported by `pyproject.toml`; `uv` installs one if needed.
- For image generation, a CUDA-capable GPU and substantial RAM/VRAM are
  strongly recommended. FLUX.1-schnell downloads roughly 24 GB of weights.
- For Azure Face, an approved Face resource and an identity authorized to use
  detection and verification. Face verification may require Microsoft Limited
  Access approval.

From the repository root:

```powershell
$uv = "uv" # use the full uv.exe path if it is not yet on PATH
$tool = "src\Tools\FaceBenchmark"

# Core evaluator
& $uv sync --project $tool

# Add local image generation dependencies
& $uv sync --project $tool --extra generation

# Add Azure DefaultAzureCredential support when needed
& $uv sync --project $tool --extra azure
```

The pinned generator is Black Forest Labs FLUX.1-schnell, whose code and model
weights are Apache-2.0 licensed. The model is pinned to an immutable Hugging
Face repository revision; Hub downloads validate artifact ETags. Model weights
are downloaded into a local cache and are not redistributed by this repository.
The generator's training dataset has not been publicly disclosed, so customers
with biometric-data or training-provenance requirements should obtain legal and
privacy review before use.

## 1. Generate and validate the library

```powershell
& $uv run --project $tool face-benchmark generate `
  --spec benchmark\spec.json `
  --output benchmark\generated `
  --model-directory .cache\face-benchmark\models

& $uv run --project $tool face-benchmark validate-manifest `
  --spec benchmark\spec.json `
  --manifest benchmark\generated\manifest.json `
  --image-root benchmark\generated
```

The face detector is OpenCV's bundled Haar cascade, so generation does not
download a detector model. SFace remains the external reference recognizer and
is checksum-verified after download. Generation fails if that checksum is
wrong, an image does not contain exactly one detectable face, or a probe cannot
reach its target within the configured tolerance. Do not manually replace,
crop, recompress, or rename generated files; those changes invalidate the
manifest hash and the predefined expectations.

Generation is seeded and versions are pinned, but GPU libraries can introduce
platform-specific numerical variation. Archive the generated manifest with any
customer test so drift is detectable.

## 2. Run a customer SDK

Use the pairs in `benchmark\generated\manifest.json`. For each pair:

1. Detect exactly one face in the enrollment image and one in the probe image.
2. Run one-to-one verification using consistent settings for every pair.
3. Record the SDK's unmodified numeric score.
4. Do not apply the SDK's accept/reject threshold to the score before export.
5. Do not tune settings or thresholds using evaluation identities.

Write a UTF-8 CSV with this exact header:

```csv
pair_id,split,enrollment_image_id,probe_image_id,same_identity,target_percentage,raw_score
```

The IDs, split, identity label, and target must be copied unchanged from the
manifest. Only `raw_score` comes from the customer SDK. Leave
`target_percentage` empty for calibration and impostor pairs. See
`examples/sdk-scores.csv` for a compact shape example.

If lower scores indicate a stronger match, preserve those values and use
`--score-direction lower-is-match` during evaluation.

## 3. Evaluate customer results

```powershell
& $uv run --project $tool face-benchmark evaluate `
  --spec benchmark\spec.json `
  --scores C:\customer-results\sdk-scores.csv `
  --manifest benchmark\generated\manifest.json `
  --image-root benchmark\generated `
  --sdk "Customer SDK" `
  --model-version "2026.08" `
  --score-direction higher-is-match `
  --output benchmark\reports\customer-sdk-2026-08
```

The output directory contains:

- `report.json`: benchmark metadata, calibration mapping, aggregate metrics,
  and detailed pair results; and
- `results.csv`: per-pair raw score, normalized score, target error, and
  tolerance result.

Input validation rejects missing columns, unsupported columns, duplicate pair
IDs, malformed booleans, non-finite scores, incorrect targets, calibration and
evaluation leakage, or a split without both genuine and impostor pairs.

## Azure Face adapter

Sign in with a credential supported by `DefaultAzureCredential`, such as
Azure CLI, Visual Studio, workload identity, or managed identity:

```powershell
az login

& $uv run --project $tool face-benchmark run-azure `
  --spec benchmark\spec.json `
  --manifest benchmark\generated\manifest.json `
  --image-root benchmark\generated `
  --endpoint "https://YOUR-RESOURCE.cognitiveservices.azure.com" `
  --output benchmark\reports\azure-face-raw.csv

& $uv run --project $tool face-benchmark evaluate `
  --spec benchmark\spec.json `
  --scores benchmark\reports\azure-face-raw.csv `
  --manifest benchmark\generated\manifest.json `
  --image-root benchmark\generated `
  --sdk "Azure AI Face" `
  --model-version "recognition_04 / face-v1.2-preview.1" `
  --score-direction higher-is-match `
  --output benchmark\reports\azure-face
```

The adapter caches detected face IDs for the duration of the run, performs
one-to-one verification for every manifest pair, preserves Azure's raw
confidence, honors `Retry-After` on throttling, and surfaces authorization or
Limited Access failures.

## Customer result submission checklist

Submit these items together:

- the exact generated `manifest.json`;
- unmodified raw-score CSV;
- generated `report.json` and `results.csv`;
- SDK name, model/version, deployment region, and execution date;
- score direction and all detection, alignment, quality, and verification
  settings;
- hardware/runtime details if they can affect inference;
- any failed detections or excluded images, with reasons; and
- confirmation that calibration and evaluation identities were kept separate.

Do not omit failed detections from a favorable report. A complete benchmark
must either score every required pair or clearly report the run as incomplete.

## Interpreting and sharing results

Suggested customer summary:

> **[SDK and version]** was evaluated against FRS Synthetic Face Verification
> Benchmark **[benchmark version]** using manifest **[SHA-256]**. The SDK placed
> **[within-tolerance rate]** of target-bearing probes within ±5 percentage
> points, with mean absolute error **[MAPE]** points, ROC AUC **[AUC]**, FAR
> **[FAR]**, and FRR **[FRR]** at the normalized 50% operating point. Calibration
> used only reserved identities. Raw and normalized per-pair results are
> attached.

Compare SDKs only when they used the same benchmark version, generated
manifest, required pair list, and reporting rules. Recalibrate each SDK
independently; never compare raw confidences directly.

## Limitations and responsible use

- Targets are expectations from a pinned synthetic reference pipeline, not
  biological truth or a guarantee about a real person's identity.
- Synthetic images reduce privacy risk but can accidentally resemble a real
  person. No generated portrait should be represented as an actual individual.
- FLUX.1-schnell is permissively licensed, but its training-data composition is
  undisclosed. This benchmark does not assert that all generator training
  subjects consented to biometric or generative-model use.
- Pixel transforms model framing, illumination, blur, noise, compression, and
  partial occlusion. They do not fully model aging, expression changes,
  extreme three-dimensional pose, twins, masks, or real camera pipelines.
- The bundled Haar detector avoids a separate model download but is less robust
  than landmark-based detectors. The benchmark compensates by generating
  centered frontal portraits and using a deterministic padded crop; results
  remain specific to benchmark version 1.1 and its pinned OpenCV package.
- Synthetic demographic appearance is not reliable ground-truth demographic
  labeling. This benchmark does not establish demographic fairness.
- The benchmark does not replace testing on lawfully obtained, consented,
  representative data from the intended operating environment.
- Results should not be used as the sole basis for high-impact identity,
  employment, housing, credit, policing, or access decisions.
- Review all generator, model, SDK, privacy, biometric, and regional legal terms
  before customer use.

## Troubleshooting

| Symptom | Action |
|---|---|
| Recognizer checksum mismatch | Delete the cached SFace file and retry. Do not bypass checksum validation. |
| SFace download has an SSL error | Download `face_recognition_sface_2021dec.onnx` through an approved browser or artifact mirror, place it in `.cache\face-benchmark\models`, and rerun; its SHA-256 is still verified against `spec.json`. |
| Zero or multiple faces | Keep the failure; do not hand-edit the image. Confirm the pinned OpenCV package version. |
| Target cannot be reached | Confirm pinned generator/reference versions and supported runtime. The run is not valid if generation only partially completes. |
| Azure returns 401/403 | Confirm endpoint, RBAC, token tenant, and Face Limited Access approval. |
| Azure returns 429 | The adapter retries using `Retry-After`; reduce parallel external calls if repeated throttling persists. |
| CSV rejected | Compare its exact header and pair metadata with the manifest. Preserve blank target fields. |
| Metrics differ between runs | Compare manifest hashes, SDK/model versions, score direction, preprocessing, and calibrator metadata. |

## Tool tests

The automated tests cover contract validation, calibration/evaluation split
isolation, pair construction, score direction, isotonic normalization,
tolerance boundaries, ROC/TAR calculations, report writing, strict CSV parsing,
manifest checksums, Azure request shape, throttling, and Limited Access errors:

```powershell
& $uv run --project src\Tools\FaceBenchmark `
  python -m unittest discover `
  -s src\Tools\FaceBenchmark\tests `
  -t src\Tools\FaceBenchmark `
  -v
```
