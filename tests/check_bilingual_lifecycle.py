"""Isolated real-weight lifecycle and PhoBERT runner smoke. Synthetic data only."""
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    args = parser.parse_args()
    root = Path(args.root).resolve()
    folder = root / ".local-build" / ("lifecycle-real-" + uuid.uuid4().hex)
    folder.mkdir(parents=True)
    import torch
    from transformers import RobertaConfig, RobertaForSequenceClassification
    labels = ["Anger", "Disgust", "Enjoyment", "Fear", "Other", "Sadness", "Surprise"]
    base = root / "code-train/train-KLCN/output/vietnamese-emotion/phobert_vsmec_v2/model"
    config_data = json.loads((base / "config.json").read_text(encoding="utf-8"))
    digests = []
    for version, seed in (("fixture_a", 71), ("fixture_b", 72)):
        model_dir = folder / version / "model"
        model_dir.mkdir(parents=True)
        torch.manual_seed(seed)
        cfg = RobertaConfig(vocab_size=config_data["vocab_size"], hidden_size=16, num_hidden_layers=1,
                            num_attention_heads=2, intermediate_size=32, max_position_embeddings=130,
                            num_labels=7, id2label=dict(enumerate(labels)), label2id={label: i for i, label in enumerate(labels)})
        model = RobertaForSequenceClassification(cfg)
        model.save_pretrained(model_dir, safe_serialization=True)
        for name in ("tokenizer_config.json", "vocab.txt", "bpe.codes", "added_tokens.json"):
            if (base / name).is_file():
                shutil.copy2(base / name, model_dir / name)
        digests.append(hashlib.sha256((model_dir / "model.safetensors").read_bytes()).hexdigest())
    assert digests[0] != digests[1], "Fixtures must contain genuinely different weights."
    marker = folder / "active.txt"
    marker.write_text("fixture_a\n")
    socket_handle = socket.socket()
    socket_handle.bind(("127.0.0.1", 0))
    port = socket_handle.getsockname()[1]
    socket_handle.close()
    env = {**os.environ, "AI_MODEL_ENABLED": "false", "VA_MODEL_ENABLED": "false", "VI_MODEL_ENABLED": "true",
           "VI_MODELS_ROOT": str(folder), "VI_ACTIVE_MODEL_FILE": str(marker), "TOKENIZERS_PARALLELISM": "false"}
    process = None
    log = (folder / "service.log").open("w", encoding="utf-8")
    def start():
        return subprocess.Popen([sys.executable, "-m", "uvicorn", "main:app", "--host", "127.0.0.1", "--port", str(port)],
                                cwd=root / "ai-service", env=env, stdout=log, stderr=log, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    def stop():
        nonlocal process
        if process is not None:
            process.terminate()
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
    def call(path, body=None, expected=200):
        payload = None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(f"http://127.0.0.1:{port}/" + path, data=payload,
                                         headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request, timeout=3 if path == "health/live" else 60) as response:
                status, value = response.status, json.load(response)
        except urllib.error.HTTPError as error:
            status, value = error.code, json.load(error)
        assert status == expected, (path, status, value)
        return value
    def wait_live():
        for _ in range(180):
            assert process.poll() is None, "Fixture service exited: inspect local lifecycle service.log."
            try:
                call("health/live")
                return
            except (urllib.error.URLError, TimeoutError, ConnectionError):
                time.sleep(.5)
        raise RuntimeError("Fixture service did not start.")
    try:
        process = start()
        wait_live()
        assert call("health/vi/ready")["model_version"] == "fixture_a"
        assert call("models/vi/validate", {"version": "fixture_b"})["quality_validated"] is False
        fp = call("models/vi/fingerprint/fixture_b")["bundleSha256"]
        call("models/vi/activate", {"version":"fixture_b","expected_revision":0,"expected_fingerprint":"0"*64}, 409)
        assert call("models/vi/activate", {"version": "fixture_b", "expected_revision": 0, "expected_fingerprint": fp})["active_version"] == "fixture_b"
        call("models/vi/activate", {"version": "fixture_a", "expected_revision": 0}, 409)
        assert call("models/vi/rollback", {"expected_revision": 1})["active_version"] == "fixture_a"
        call("models/vi/deactivate", {"expected_revision": 2})
        call("health/vi/ready", expected=503)
        stop()
        process = start()
        wait_live()
        call("health/vi/ready", expected=503)
        assert call("models/vi/rollback", {"expected_revision": 3})["active_version"] == "fixture_a"
        assert call("predict-vi", {"text": "Tôi đang vui", "language": "vi"})["model_version"] == "fixture_a"
    finally:
        stop()
        log.close()
    rows = [{"id": split + str(i), "text": f"Tôi đang cảm thấy vui vào ngày {i} {split}",
             "emotion": "Enjoyment", "split": split, "reviewed": True}
            for split in ("train", "validation", "test") for i in range(3)]
    cfg = {"taskType": "vsmec7", "language": "vi", "epochs": 1, "batchSize": 2,
           "gradientAccumulation": 1, "seed": 42, "maxLength": 128}
    rows_text, cfg_text = json.dumps(rows, ensure_ascii=False), json.dumps(cfg)
    bundle = {"jobId": "synthetic_training_smoke", "modelVersion": "trained_fixture", "datasetJson": rows_text,
              "configJson": cfg_text, "datasetHash": hashlib.sha256(rows_text.encode()).hexdigest(),
              "configHash": hashlib.sha256(cfg_text.encode()).hexdigest()}
    bundle_path = folder / "bundle.json"
    bundle_path.write_text(json.dumps(bundle, ensure_ascii=False), encoding="utf-8")
    subprocess.run([sys.executable, str(root / "ai-service/train_experiment.py"), "--bundle", str(bundle_path),
                    "--base-dir", str(folder / "fixture_a/model"), "--out", str(folder / "trained")],
                   cwd=root / "ai-service", check=True, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    from safetensors.torch import load_file
    original = load_file(folder / "fixture_a/model/model.safetensors")
    trained = load_file(folder / "trained/model/model.safetensors")
    delta = float((original["classifier.out_proj.weight"] - trained["classifier.out_proj.weight"]).abs().max())
    assert delta > 0, "Training did not update classifier weights."
    evaluation_rows=[r for r in rows if r["split"]!="train"]
    evaluation_cfg={**cfg,"purpose":"evaluation","mappingVersion":"synthetic-evaluation-only"}
    data_text=json.dumps(evaluation_rows,ensure_ascii=False);config_text=json.dumps(evaluation_cfg)
    evaluation_bundle={"jobId":"synthetic_evaluation_smoke","modelVersion":"fixture_a","datasetJson":data_text,
        "configJson":config_text,"datasetHash":hashlib.sha256(data_text.encode()).hexdigest(),"configHash":hashlib.sha256(config_text.encode()).hexdigest()}
    evaluation_path=folder/"evaluation-bundle.json"
    evaluation_path.write_text(json.dumps(evaluation_bundle,ensure_ascii=False),encoding="utf-8")
    subprocess.run([sys.executable,str(root/"ai-service/train_experiment.py"),"--bundle",str(evaluation_path),
        "--base-dir",str(folder/"fixture_a/model"),"--out",str(folder/"evaluation"),"--device","cpu"],
        cwd=root/"ai-service",check=True,creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0))
    evaluation=json.loads((folder/"evaluation/report.json").read_text(encoding="utf-8"))
    assert evaluation["artifactHash"]==digests[0] and len(evaluation["bundleSha256"])==64
    assert evaluation["validationMetrics"] and evaluation["testSamples"]==3
    assert hashlib.sha256((folder/"fixture_a/model/model.safetensors").read_bytes()).hexdigest()==digests[0]
    (root/".local-build/phase3-vi-evaluation-runner.json").write_text(json.dumps({
        "scope":"Synthetic labels only; runner execution, not NLP quality.",
        "weightsUnchanged":True,"validationAndTestReported":True,"bundleSha256":evaluation["bundleSha256"]},indent=2),encoding="utf-8")
    report = {"scope": "Synthetic contracts, not independent NLP quality.", "differentWeightHashes": digests,
              "rollbackAndDeactivationPersisted": True, "classifierTrainingWeightDelta": delta,
              "productionMarkersUnchanged": True}
    (root / ".local-build/phase2-lifecycle-real.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("PASS: real distinct-weight lifecycle, inactive restart, rollback and actual classifier gradient update. Synthetic fixture only.")


if __name__ == "__main__":
    main()
