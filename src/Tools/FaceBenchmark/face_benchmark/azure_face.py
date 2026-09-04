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

import csv
import json
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Protocol

from .contract import BenchmarkSpec, validate_manifest
from .errors import BenchmarkError


FACE_SCOPE = "https://cognitiveservices.azure.com/.default"


@dataclass(frozen=True)
class HttpResponse:
    status_code: int
    headers: dict[str, str]
    body: bytes


class TokenCredential(Protocol):
    def get_token(self, *scopes: str) -> Any: ...


Transport = Callable[[urllib.request.Request, float], HttpResponse]


class AzureFaceClient:
    def __init__(
        self,
        endpoint: str,
        credential: TokenCredential,
        transport: Transport | None = None,
        api_version: str = "face/v1.2-preview.1",
        timeout_seconds: float = 30.0,
        maximum_retries: int = 4,
    ) -> None:
        if not endpoint.strip():
            raise BenchmarkError("Azure Face endpoint must be non-empty.")
        self._endpoint = endpoint.rstrip("/")
        self._credential = credential
        self._transport = transport or _urlopen_transport
        self._api_version = api_version.strip("/")
        self._timeout = timeout_seconds
        self._maximum_retries = maximum_retries

    def detect(self, image: bytes) -> str:
        if not image:
            raise BenchmarkError("Cannot detect a face in an empty image.")
        response = self._request(
            "POST",
            "detect?returnFaceId=true&recognitionModel=recognition_04&detectionModel=detection_03",
            image,
            "application/octet-stream",
        )
        payload = _json(response)
        if not isinstance(payload, list):
            raise BenchmarkError("Azure Face detect returned an unexpected response shape.")
        if len(payload) != 1:
            raise BenchmarkError(
                f"Azure Face detect expected exactly one face but found {len(payload)}."
            )
        face_id = payload[0].get("faceId")
        if not isinstance(face_id, str) or not face_id:
            raise BenchmarkError("Azure Face detect did not return a faceId.")
        return face_id

    def verify(self, first_face_id: str, second_face_id: str) -> float:
        body = json.dumps(
            {"faceId1": first_face_id, "faceId2": second_face_id}
        ).encode("utf-8")
        payload = _json(
            self._request("POST", "verify", body, "application/json")
        )
        confidence = payload.get("confidence") if isinstance(payload, dict) else None
        if not isinstance(confidence, (int, float)) or isinstance(confidence, bool):
            raise BenchmarkError("Azure Face verify did not return a numeric confidence.")
        value = float(confidence)
        if not 0 <= value <= 1:
            raise BenchmarkError(
                f"Azure Face verify returned out-of-range confidence {value}."
            )
        return value

    def _request(
        self,
        method: str,
        operation: str,
        body: bytes,
        content_type: str,
    ) -> HttpResponse:
        token = self._credential.get_token(FACE_SCOPE).token
        request = urllib.request.Request(
            f"{self._endpoint}/{self._api_version}/{operation}",
            data=body,
            method=method,
            headers={
                "Authorization": f"Bearer {token}",
                "Content-Type": content_type,
                "Accept": "application/json",
            },
        )
        for attempt in range(self._maximum_retries + 1):
            response = self._transport(request, self._timeout)
            if 200 <= response.status_code < 300:
                return response
            if response.status_code == 429 and attempt < self._maximum_retries:
                retry_after = _retry_after(response.headers)
                time.sleep(retry_after)
                continue
            detail = response.body.decode("utf-8", errors="replace")
            if response.status_code in {401, 403}:
                raise BenchmarkError(
                    "Azure Face rejected the request. Verify RBAC and Limited Access approval: "
                    f"HTTP {response.status_code}: {detail}"
                )
            raise BenchmarkError(
                f"Azure Face request failed with HTTP {response.status_code}: {detail}"
            )
        raise BenchmarkError("Azure Face retry limit was exhausted.")


def run_manifest(
    manifest_path: Path,
    image_root: Path,
    output_path: Path,
    spec: BenchmarkSpec,
    client: AzureFaceClient,
) -> int:
    manifest = validate_manifest(manifest_path, image_root, spec)
    artifacts = {item["imageId"]: item for item in manifest["artifacts"]}
    pairs = manifest.get("pairs")
    if not isinstance(pairs, list) or not pairs:
        raise BenchmarkError("Manifest has no verification pairs.")

    required_image_ids = {
        image_id
        for pair in pairs
        for image_id in (pair["enrollmentImageId"], pair["probeImageId"])
    }
    missing = sorted(required_image_ids - artifacts.keys())
    if missing:
        raise BenchmarkError(
            "Manifest pairs reference missing image IDs: " + ", ".join(missing)
        )

    face_ids: dict[str, str] = {}
    for image_id in sorted(required_image_ids):
        image_path = image_root / artifacts[image_id]["path"]
        face_ids[image_id] = client.detect(image_path.read_bytes())

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(
            [
                "pair_id",
                "split",
                "enrollment_image_id",
                "probe_image_id",
                "same_identity",
                "target_percentage",
                "raw_score",
            ]
        )
        for pair in pairs:
            enrollment_id = pair["enrollmentImageId"]
            probe_id = pair["probeImageId"]
            raw_score = client.verify(face_ids[enrollment_id], face_ids[probe_id])
            writer.writerow(
                [
                    pair["pairId"],
                    pair["split"],
                    enrollment_id,
                    probe_id,
                    str(pair["sameIdentity"]).lower(),
                    (
                        ""
                        if pair.get("targetPercentage") is None
                        else pair["targetPercentage"]
                    ),
                    raw_score,
                ]
            )
    return len(pairs)


def default_credential() -> TokenCredential:
    try:
        from azure.identity import DefaultAzureCredential
    except ImportError as error:
        raise BenchmarkError(
            "Azure support is not installed. Run 'uv sync --extra azure'."
        ) from error
    return DefaultAzureCredential()


def _urlopen_transport(
    request: urllib.request.Request, timeout_seconds: float
) -> HttpResponse:
    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            return HttpResponse(
                status_code=response.status,
                headers={key.lower(): value for key, value in response.headers.items()},
                body=response.read(),
            )
    except urllib.error.HTTPError as error:
        return HttpResponse(
            status_code=error.code,
            headers={key.lower(): value for key, value in error.headers.items()},
            body=error.read(),
        )
    except urllib.error.URLError as error:
        raise BenchmarkError(f"Azure Face request failed: {error.reason}") from error


def _json(response: HttpResponse) -> Any:
    try:
        return json.loads(response.body)
    except json.JSONDecodeError as error:
        raise BenchmarkError("Azure Face returned invalid JSON.") from error


def _retry_after(headers: dict[str, str]) -> float:
    value = next((v for k, v in headers.items() if k.lower() == "retry-after"), "1")
    try:
        return max(0.0, min(float(value), 60.0))
    except ValueError:
        return 1.0
