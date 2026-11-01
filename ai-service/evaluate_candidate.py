"""Evaluate an immutable existing VI/EN artifact; no optimizer, threshold tuning or activation."""
import json
import os
from pathlib import Path

from evaluate_bilingual import score


def evaluate_existing(bundle, rows, cfg, base, out, device):
    if cfg.get("taskType") not in ("vsmec7","va_regression") or not base.is_dir():
        raise ValueError("Evaluation needs an explicit local bilingual artifact.")
    if not any(r["split"]=="validation" for r in rows) or not any(r["split"]=="test" for r in rows) or any(r["split"]=="train" for r in rows):
        raise ValueError("Independent evaluation requires validation/test only; no training rows.")
    if device=="cpu":
        os.environ["CUDA_VISIBLE_DEVICES"]=""
    language="vi" if cfg["taskType"]=="vsmec7" else "en"
    version=bundle["modelVersion"]
    if language=="vi":
        from vietnamese_predictor import VietnameseEmotionPredictor
        root=base/"model" if (base/"model/config.json").is_file() else base
        predictor=VietnameseEmotionPredictor(root,version)
    else:
        from va_predictor import ValenceArousalPredictor
        files=list(base.glob("*.pt"))
        if len(files)!=1:
            raise ValueError("VA evaluation needs one checkpoint and the tokenizer folder.")
        predictor=ValenceArousalPredictor(files[0],base/"tokenizer",version)
    if device=="cuda" and predictor.device!="cuda":
        raise ValueError("CUDA was explicitly requested but unavailable.")
    fingerprint=predictor.artifact_fingerprint
    gold=[{**r,"language":language} for r in rows]
    predictions=[]
    centers=cfg.get("moodCenters",[])
    for row in rows:
        raw=predictor.predict(row["text"])
        accepted=raw.get("accepted",True)
        if language=="vi":
            accepted=accepted and raw["confidence"]>=cfg.get("minimumConfidence",0)
        prediction={"id":row["id"],"language":language,"modelVersion":version,
            "mappingVersion":cfg.get("mappingVersion") or "unscoped-offline-evaluation","accepted":bool(accepted),
            "valence":raw["valence"],"arousal":raw["arousal"],"moodId":None}
        if language=="vi":
            prediction["emotion"]=raw["emotion"]
        if accepted and centers:
            prediction["moodId"]=min(centers,key=lambda m:((m["valence"]-raw["valence"])**2+(m["arousal"]-raw["arousal"])**2,m["id"]))["id"]
        predictions.append(prediction)
    metrics={}
    for split in ("validation","test"):
        report=score(gold,predictions,split)
        (out/(split+"-evaluation.json")).write_text(json.dumps(report,ensure_ascii=False,indent=2,allow_nan=False),encoding="utf-8")
        selected=[p for p,r in zip(predictions,rows) if r["split"]==split]
        (out/(split+"-predictions.jsonl")).write_text("\n".join(json.dumps(p,ensure_ascii=False,allow_nan=False) for p in selected),encoding="utf-8")
        group=report["groups"][0]
        if language=="vi":
            c=group["classification"]
            metrics[split]={"macroF1":c["macroF1AllSevenLabels"],"coverage":c["coverage"],
                "selectiveAccuracy":c["selectiveAccuracy"] if c["selectiveAccuracy"] is not None else 0.0}
        else:
            metrics[split]={axis+suffix:group["va"][axis][metric] for axis in ("valence","arousal")
                for suffix,metric in (("Mae","mae"),("Rmse","rmse"))}
    result={key:bundle[key] for key in ("jobId","datasetHash","configHash","modelVersion")}
    result.update({"artifactHash":fingerprint["weightSha256"],"bundleSha256":fingerprint["bundleSha256"],
        "artifactFiles":fingerprint["files"],"testSamples":sum(r["split"]=="test" for r in rows),
        "metrics":metrics["test"],"validationMetrics":metrics["validation"],
        "status":"evaluated_existing_candidate_not_deployed",
        "evidenceScope":"Offline execution on supplied reviewed labels; annotation independence requires accountable review.",
        "preprocessingStatus":getattr(predictor,"preprocessing_status",None)})
    return result