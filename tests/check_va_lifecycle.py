"""Real VA architecture lifecycle test using isolated, distinct checkpoint weights."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    root = Path(parser.parse_args().root).resolve()
    base = root / "code-train/train-KLCN/output/roberta_va_regression_v2"
    original = base / "roberta_va_regression_v2.pt"
    original_hash = digest(original)
    folder = root / ".local-build" / ("va-lifecycle-real-" + uuid.uuid4().hex)
    folder.mkdir(parents=True)
    checkpoints = []
    for version in ("fixture_va_a", "fixture_va_b"):
        target = folder / version
        target.mkdir()
        shutil.copytree(base / "tokenizer", target / "tokenizer")
        checkpoints.append(target / (version + ".pt"))
    # A is a read-only hardlink; B is a separately serialized dict. No tensor
    # or file backing A/original is modified by the test.
    os.link(original, checkpoints[0])
    import torch
    weights = torch.load(original, map_location="cpu", weights_only=True)
    weights["regressor.bias"] = weights["regressor.bias"].clone() + .01
    torch.save(weights, checkpoints[1])
    del weights
    hashes = [digest(path) for path in checkpoints]
    assert hashes[0] != hashes[1]
    marker = folder / "active.txt"
    marker.write_text("fixture_va_a")
    sock = socket.socket(); sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]; sock.close()
    env = {**os.environ, "AI_MODEL_ENABLED": "false", "VI_MODEL_ENABLED": "false", "VA_MODEL_ENABLED": "true",
           "VA_MODELS_ROOT": str(folder), "VA_MODEL_DIR": str(folder / "fixture_va_a"),
           "VA_ACTIVE_MODEL_FILE": str(marker), "TOKENIZERS_PARALLELISM": "false"}
    process = None
    log = (folder / "service.log").open("w", encoding="utf-8")
    def start():
        return subprocess.Popen([sys.executable, "-m", "uvicorn", "main:app", "--host", "127.0.0.1", "--port", str(port)],
            cwd=root / "ai-service", env=env, stdout=log, stderr=log, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    def stop():
        if process is not None and process.poll() is None:
            process.terminate()
            try: process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait()
    def call(path, body=None, expected=200):
        request = urllib.request.Request(f"http://127.0.0.1:{port}/"+path,
            data=None if body is None else json.dumps(body).encode(), headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request, timeout=3 if path=="health/live" else 120) as response:
                status, value = response.status, json.load(response)
        except urllib.error.HTTPError as error:
            status, value = error.code, json.load(error)
        assert status == expected, (path, status, value)
        return value
    def live():
        for _ in range(180):
            assert process.poll() is None, "Fixture VA service exited; inspect isolated service.log."
            try:
                call("health/live"); return
            except (urllib.error.URLError, TimeoutError, ConnectionError):
                time.sleep(.5)
        raise RuntimeError("Fixture VA did not start.")
    try:
        process = start(); live()
        prediction_body = {"text": "I feel calm and hopeful today.", "language": "en"}
        a = call("predict-va", prediction_body)
        assert call("models/va/validate", {"version":"fixture_va_b"})["quality_validated"] is False
        fp = call("models/va/fingerprint/fixture_va_b")["bundleSha256"]
        call("models/va/activate", {"version":"fixture_va_b","expected_revision":0,"expected_fingerprint":"0"*64}, 409)
        assert call("models/va/activate", {"version":"fixture_va_b","expected_revision":0,"expected_fingerprint":fp})["active_version"]=="fixture_va_b"
        b = call("predict-va", prediction_body)
        assert abs(a["valence"]-b["valence"]) > .001
        call("models/va/activate", {"version":"fixture_va_a","expected_revision":0}, 409)
        call("models/va/rollback", {"expected_revision":1})
        restored = call("predict-va", prediction_body)
        assert abs(a["valence"]-restored["valence"]) < 1e-6
        call("models/va/deactivate", {"expected_revision":2})
        stop(); process = start(); live()
        call("health/va/ready", expected=503)
        assert call("models/va/rollback", {"expected_revision":3})["active_version"]=="fixture_va_a"
    finally:
        stop(); log.close()
        # Delete only the two explicitly created checkpoint entries, after
        # checking resolved targets. No recursive directory deletion.
        for path in checkpoints:
            assert path.resolve().is_relative_to(folder)
            path.unlink(missing_ok=True)
    assert digest(original)==original_hash
    rows=[{"id":split+str(n),"text":f"Contract fixture {split} number {n}: I am reading a book quietly.",
        "language":"en","split":split,"reviewed":True,"valence":0.0,"arousal":0.0}
        for split in ("validation","test") for n in range(3)]
    cfg={"taskType":"va_regression","language":"en","purpose":"evaluation","mappingVersion":"synthetic-evaluation-only"}
    data_text=json.dumps(rows);config_text=json.dumps(cfg)
    bundle={"jobId":"synthetic_va_evaluation","modelVersion":"roberta_va_regression_v2","datasetJson":data_text,
        "configJson":config_text,"datasetHash":hashlib.sha256(data_text.encode()).hexdigest(),"configHash":hashlib.sha256(config_text.encode()).hexdigest()}
    bundle_path=folder/"evaluation-bundle.json";bundle_path.write_text(json.dumps(bundle),encoding="utf-8")
    subprocess.run([sys.executable,str(root/"ai-service/train_experiment.py"),"--bundle",str(bundle_path),
        "--base-dir",str(base),"--out",str(folder/"evaluation"),"--device","cpu"],cwd=root/"ai-service",check=True,
        creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0))
    evaluation=json.loads((folder/"evaluation/report.json").read_text(encoding="utf-8"))
    assert evaluation["artifactHash"]==original_hash and digest(original)==original_hash
    assert evaluation["validationMetrics"] and evaluation["testSamples"]==3
    (root/".local-build/phase3-va-evaluation-runner.json").write_text(json.dumps({
        "scope":"Synthetic target coordinates, not NLP quality.","weightsUnchanged":True,
        "validationAndTestReported":True,"bundleSha256":evaluation["bundleSha256"]},indent=2),encoding="utf-8")
    report={"scope":"VA lifecycle, distinct real-architecture weights; synthetic perturbation, not trained quality improvement.",
            "differentWeightHashes":hashes,"predictionDelta":b["valence"]-a["valence"],
            "restoredPredictionDelta":restored["valence"]-a["valence"],"deactivationSurvivesRestart":True,
            "productionCheckpointUnchanged":True,"temporaryLargeCheckpointsRemoved":True}
    (root / ".local-build/phase2-va-lifecycle-real.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print("PASS: VA validate/activate/distinct-weight rollback, inactive restart; production weights unchanged.")


if __name__ == "__main__":
    main()