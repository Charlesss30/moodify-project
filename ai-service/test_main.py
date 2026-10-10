import asyncio
import os
import unittest
from types import SimpleNamespace
from unittest.mock import patch

from fastapi import HTTPException

from main import PredictionRequest, app, live, predict, ready


class InferenceApiTests(unittest.TestCase):
    def test_readiness_reports_selected_inference_device(self):
        app.state.predictor = SimpleNamespace(
            config={"run_id": "test-model"},
            artifact_hash="verified-hash",
            config_hash="verified-config-hash",
            device="cuda",
            device_name="NVIDIA Test GPU",
        )
        try:
            response = ready()
        finally:
            app.state.predictor = None

        self.assertEqual(response["inference_device"], "cuda")
        self.assertEqual(response["device_name"], "NVIDIA Test GPU")
        self.assertEqual(response["config_hash"], "verified-config-hash")

    def test_live_and_not_ready_when_loading_is_disabled(self):
        async def check():
            with patch.dict(os.environ, {"AI_MODEL_ENABLED": "false"}):
                async with app.router.lifespan_context(app):
                    self.assertEqual(live(), {"status": "live"})
                    with self.assertRaises(HTTPException) as raised:
                        ready()
                    self.assertEqual(raised.exception.status_code, 503)
                    with self.assertRaises(HTTPException) as raised:
                        predict(PredictionRequest(text="A test sentence", language="en"))
                    self.assertEqual(raised.exception.status_code, 503)

        asyncio.run(check())


if __name__ == "__main__":
    unittest.main()
