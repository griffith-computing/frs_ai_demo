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

import json
import unittest
import urllib.request
from dataclasses import dataclass

from face_benchmark.azure_face import AzureFaceClient, HttpResponse
from face_benchmark.errors import BenchmarkError


@dataclass(frozen=True)
class _Token:
    token: str


class _Credential:
    def get_token(self, *scopes: str) -> _Token:
        self.scopes = scopes
        return _Token("test-token")


class _Transport:
    def __init__(self, *responses: HttpResponse) -> None:
        self.responses = list(responses)
        self.requests: list[urllib.request.Request] = []

    def __call__(
        self, request: urllib.request.Request, timeout: float
    ) -> HttpResponse:
        self.requests.append(request)
        return self.responses.pop(0)


def _response(status: int, payload: object, **headers: str) -> HttpResponse:
    return HttpResponse(status, headers, json.dumps(payload).encode("utf-8"))


class AzureFaceTests(unittest.TestCase):
    def test_detect_requires_exactly_one_face(self) -> None:
        transport = _Transport(_response(200, []))
        client = AzureFaceClient("https://face.test", _Credential(), transport)

        with self.assertRaisesRegex(BenchmarkError, "exactly one face"):
            client.detect(b"photo")

    def test_verify_preserves_raw_confidence_and_authenticates(self) -> None:
        credential = _Credential()
        transport = _Transport(_response(200, {"isIdentical": True, "confidence": 0.82}))
        client = AzureFaceClient("https://face.test/", credential, transport)

        result = client.verify("face-1", "face-2")

        self.assertEqual(0.82, result)
        request = transport.requests[0]
        self.assertEqual("Bearer test-token", request.headers["Authorization"])
        self.assertTrue(request.full_url.endswith("/face/v1.2-preview.1/verify"))
        self.assertEqual(
            {"faceId1": "face-1", "faceId2": "face-2"},
            json.loads(request.data or b""),
        )

    def test_retries_rate_limit_response(self) -> None:
        transport = _Transport(
            _response(429, {"error": "rate limited"}, **{"retry-after": "0"}),
            _response(200, {"confidence": 0.5}),
        )
        client = AzureFaceClient("https://face.test", _Credential(), transport)

        self.assertEqual(0.5, client.verify("face-1", "face-2"))
        self.assertEqual(2, len(transport.requests))

    def test_surfaces_limited_access_failure(self) -> None:
        transport = _Transport(_response(403, {"error": "Forbidden"}))
        client = AzureFaceClient("https://face.test", _Credential(), transport)

        with self.assertRaisesRegex(BenchmarkError, "Limited Access"):
            client.verify("face-1", "face-2")


if __name__ == "__main__":
    unittest.main()
