#----------------------------------------------------------------------------------
# THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
# EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
# OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
#
# This sample is not supported under any Microsoft standard support program or
# service. It is provided to you solely for the purpose of illustration and is
# intended to be modified, tested, and validated by the customer prior to any
# production use. The entire risk arising out of the use or performance of this
# code remains with the customer.
#
# Copyright (c) Microsoft Corporation. All rights reserved.
#----------------------------------------------------------------------------------

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .azure_face import AzureFaceClient, default_credential, run_manifest
from .calibration import fit_isotonic
from .contract import load_spec, validate_manifest
from .errors import BenchmarkError
from .generation import generate_library
from .metrics import evaluate
from .reporting import write_reports
from .scores import read_score_csv, validate_scores_against_manifest


def main(argv: list[str] | None = None) -> int:
    parser = _parser()
    args = parser.parse_args(argv)
    try:
        if args.command == "validate-spec":
            spec = load_spec(args.spec)
            print(
                f"Valid benchmark {spec.version}: "
                f"{len(spec.evaluation_identities)} evaluation identities, "
                f"{len(spec.calibration_identities)} calibration identities, "
                f"{len(spec.targets)} targets."
            )
            return 0
        if args.command == "validate-manifest":
            spec = load_spec(args.spec)
            manifest = validate_manifest(args.manifest, args.image_root, spec)
            print(f"Validated {len(manifest['artifacts'])} image artifacts.")
            return 0
        if args.command == "evaluate":
            return _evaluate(args)
        if args.command == "run-azure":
            spec = load_spec(args.spec)
            client = AzureFaceClient(args.endpoint, default_credential())
            count = run_manifest(
                args.manifest,
                args.image_root,
                args.output,
                spec,
                client,
            )
            print(f"Wrote {count} Azure Face verification scores to {args.output}.")
            return 0
        if args.command == "generate":
            spec = load_spec(args.spec)
            manifest = generate_library(
                spec,
                args.output,
                args.model_directory,
                args.recognizer_model,
            )
            print(f"Generated benchmark manifest at {manifest}.")
            return 0
        parser.error(f"Unknown command '{args.command}'.")
    except BenchmarkError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2
    return 2


def _evaluate(args: argparse.Namespace) -> int:
    spec = load_spec(args.spec)
    validate_manifest(args.manifest, args.image_root, spec)
    rows = read_score_csv(args.scores)
    validate_scores_against_manifest(rows, args.manifest, spec.version)
    calibration = fit_isotonic(rows, args.score_direction)
    evaluated, metrics = evaluate(rows, calibration, spec.tolerance)
    json_path, csv_path = write_reports(
        output_directory=args.output,
        benchmark_version=spec.version,
        sdk=args.sdk,
        model_version=args.model_version,
        tolerance=spec.tolerance,
        calibration=calibration,
        evaluated=evaluated,
        metrics=metrics,
    )
    print(json.dumps(metrics, indent=2))
    print(f"Wrote {json_path} and {csv_path}.")
    return 0 if metrics["withinToleranceRate"] == 1.0 else 1


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="face-benchmark")
    subcommands = parser.add_subparsers(dest="command", required=True)

    validate_spec = subcommands.add_parser("validate-spec")
    validate_spec.add_argument("--spec", type=Path, required=True)

    validate_manifest_parser = subcommands.add_parser("validate-manifest")
    validate_manifest_parser.add_argument("--spec", type=Path, required=True)
    validate_manifest_parser.add_argument("--manifest", type=Path, required=True)
    validate_manifest_parser.add_argument("--image-root", type=Path, required=True)

    evaluate_parser = subcommands.add_parser("evaluate")
    evaluate_parser.add_argument("--spec", type=Path, required=True)
    evaluate_parser.add_argument("--scores", type=Path, required=True)
    evaluate_parser.add_argument("--manifest", type=Path, required=True)
    evaluate_parser.add_argument("--image-root", type=Path, required=True)
    evaluate_parser.add_argument("--sdk", required=True)
    evaluate_parser.add_argument("--model-version", required=True)
    evaluate_parser.add_argument(
        "--score-direction",
        choices=["higher-is-match", "lower-is-match"],
        required=True,
    )
    evaluate_parser.add_argument("--output", type=Path, required=True)

    azure_parser = subcommands.add_parser("run-azure")
    azure_parser.add_argument("--spec", type=Path, required=True)
    azure_parser.add_argument("--manifest", type=Path, required=True)
    azure_parser.add_argument("--image-root", type=Path, required=True)
    azure_parser.add_argument("--endpoint", required=True)
    azure_parser.add_argument("--output", type=Path, required=True)

    generate_parser = subcommands.add_parser("generate")
    generate_parser.add_argument("--spec", type=Path, required=True)
    generate_parser.add_argument(
        "--output", type=Path, default=Path("benchmark/generated")
    )
    generate_parser.add_argument(
        "--model-directory", type=Path, default=Path(".cache/face-benchmark/models")
    )
    generate_parser.add_argument(
        "--recognizer-model",
        type=Path,
        help="Local SFace ONNX file; bypasses download and is SHA-256 verified.",
    )
    return parser


if __name__ == "__main__":
    raise SystemExit(main())
