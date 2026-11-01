"""Deterministic biased matrix factorization for explicit ratings; offline only."""
import argparse
import copy
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import random
import statistics


def stamp(value):
    value = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    if value.tzinfo is None:
        raise ValueError("Every timestamp and cutoff needs a timezone.")
    return value.astimezone(timezone.utc)


def latest(rows):
    selected = {}
    for row in sorted(rows, key=lambda r: (stamp(r["timestamp"]), str(r.get("id", "")))):
        selected[(row["userId"], row["contentId"])] = row
    return sorted(selected.values(), key=lambda r: (r["userId"], r["contentId"]))


def temporal_split(rows, train_before, validation_before):
    train_time, validation_time = stamp(train_before), stamp(validation_before)
    if train_time >= validation_time:
        raise ValueError("Train cutoff must precede validation cutoff.")
    clean = []
    for r in rows:
        if type(r.get("userId")) is not int or r["userId"] < 1 or not isinstance(r.get("contentId"), str) or not r["contentId"] or len(r["contentId"]) > 100:
            raise ValueError("Stable user and catalog IDs are required.")
        if type(r.get("stars")) is not int or not 1 <= r["stars"] <= 5:
            raise ValueError("Only explicit 1-5 star ratings train this model.")
        stamp(r["timestamp"])
        clean.append(dict(r))
    return (latest([r for r in clean if stamp(r["timestamp"]) < train_time]),
            latest([r for r in clean if train_time <= stamp(r["timestamp"]) < validation_time]),
            latest([r for r in clean if stamp(r["timestamp"]) >= validation_time]))


def predict(model, user_id, content_id):
    u = next((x for x in model["users"] if x["userId"] == user_id), None)
    i = next((x for x in model["items"] if x["contentId"] == content_id), None)
    if u is None or i is None:
        return model["globalMean"]
    return max(-1., min(1., model["globalMean"] + u["bias"] + i["bias"]
                       + sum(a * b for a, b in zip(u["factors"], i["factors"]))))


def rating_metrics(model, rows):
    if not rows:
        return {"count": 0, "warmCount": 0, "rmse": None, "mae": None, "meanBaselineRmse": None}
    known_u = {u["userId"] for u in model["users"]}
    known_i = {i["contentId"] for i in model["items"]}
    errors = [predict(model, r["userId"], r["contentId"]) - (r["stars"] - 3) / 2 for r in rows]
    return {"count": len(rows),
            "warmCount": sum(r["userId"] in known_u and r["contentId"] in known_i for r in rows),
            "rmse": math.sqrt(statistics.mean(e * e for e in errors)),
            "mae": statistics.mean(abs(e) for e in errors),
            "meanBaselineRmse": math.sqrt(statistics.mean((model["globalMean"] - (r["stars"] - 3) / 2) ** 2 for r in rows))}


def ranking_metrics(model, train, rows, k=10):
    seen, positives = {}, {}
    for r in train:
        seen.setdefault(r["userId"], set()).add(r["contentId"])
    for r in rows:
        if r["stars"] >= 4 and r["contentId"] not in seen.get(r["userId"], set()):
            positives.setdefault(r["userId"], set()).add(r["contentId"])
    scored, covered, recommended = [], set(), set()
    all_items = {i["contentId"] for i in model["items"]}
    for user, relevant in sorted(positives.items()):
        candidates = all_items - seen.get(user, set())
        ranked = sorted(candidates, key=lambda item: (-predict(model, user, item), item))[:k]
        hits = [int(item in relevant) for item in ranked]
        dcg = sum(hit / math.log2(index + 2) for index, hit in enumerate(hits))
        ideal = sum(1 / math.log2(index + 2) for index in range(min(k, len(relevant))))
        scored.append((sum(hits) / k, sum(hits) / len(relevant), dcg / ideal if ideal else 0))
        recommended.update(ranked)
        covered.update(relevant & all_items)
    return {"k": k, "users": len(scored), "precision": statistics.mean(x[0] for x in scored) if scored else None,
            "recall": statistics.mean(x[1] for x in scored) if scored else None,
            "ndcg": statistics.mean(x[2] for x in scored) if scored else None,
            "catalogCoverage": len(recommended) / len(all_items) if all_items else 0,
            "note": "Unseen held-out positives only; unavailable items count as misses. No mood/content ground truth is inferred."}


def train(rows, train_before, validation_before, version, factors=8, epochs=100, learning_rate=.02, regularization=.05, seed=42):
    if not 1 <= factors <= 64 or not 1 <= epochs <= 1000 or not 0 < learning_rate <= .1 or not 0 < regularization <= 10:
        raise ValueError("Invalid factorization configuration.")
    training, validation, test = temporal_split(rows, train_before, validation_before)
    if len(training) < 3:
        raise ValueError("At least three training ratings are required; this is only a technical minimum.")
    rng = random.Random(seed)
    users = [{"userId": u, "bias": 0., "factors": [rng.uniform(-.05, .05) for _ in range(factors)]}
             for u in sorted({r["userId"] for r in training})]
    items = [{"contentId": i, "bias": 0., "factors": [rng.uniform(-.05, .05) for _ in range(factors)]}
             for i in sorted({r["contentId"] for r in training})]
    model = {"schemaVersion": 1, "version": version, "factors": factors, "globalMean": statistics.mean((r["stars"] - 3) / 2 for r in training),
             "users": users, "items": items, "trainBefore": train_before, "validationBefore": validation_before,
             "datasetHash": hashlib.sha256(json.dumps(rows, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()).hexdigest(),
             "config": {"epochs": epochs, "learningRate": learning_rate, "regularization": regularization, "seed": seed}}
    u_index = {u["userId"]: u for u in users}
    i_index = {i["contentId"]: i for i in items}
    best, best_loss = None, math.inf
    order = list(training)
    for epoch in range(epochs):
        rng.shuffle(order)
        for row in order:
            u, i = u_index[row["userId"]], i_index[row["contentId"]]
            error = (row["stars"] - 3) / 2 - (model["globalMean"] + u["bias"] + i["bias"]
                    + sum(a * b for a, b in zip(u["factors"], i["factors"])))
            u["bias"] += learning_rate * (error - regularization * u["bias"])
            i["bias"] += learning_rate * (error - regularization * i["bias"])
            previous = u["factors"][:]
            for j in range(factors):
                u["factors"][j] += learning_rate * (error * i["factors"][j] - regularization * u["factors"][j])
                i["factors"][j] += learning_rate * (error * previous[j] - regularization * i["factors"][j])
        loss = rating_metrics(model, validation)["rmse"]
        if loss is not None and loss < best_loss:
            best_loss, best = loss, copy.deepcopy(model)
            best["selectedEpoch"] = epoch + 1
    model = best or model
    model["validationRows"] = validation
    model["testRows"] = test
    model["report"] = {"train": rating_metrics(model, training), "validation": rating_metrics(model, validation),
                       "test": rating_metrics(model, test), "ranking": ranking_metrics(model, training, test),
                       "qualityStatus": "candidate_requires_review_not_automatically_deployed"}
    return model


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--data", required=True)
    p.add_argument("--train-before", required=True)
    p.add_argument("--validation-before", required=True)
    p.add_argument("--version", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--epochs", type=int, default=100)
    p.add_argument("--factors", type=int, default=8)
    p.add_argument("--seed", type=int, default=42)
    args = p.parse_args()
    rows = json.loads(Path(args.data).read_text(encoding="utf-8-sig"))
    model = train(rows, args.train_before, args.validation_before, args.version, factors=args.factors, epochs=args.epochs, seed=args.seed)
    target = Path(args.out)
    if target.exists():
        raise ValueError("Choose an immutable new version/output.")
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(model, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps(model["report"], ensure_ascii=False))


if __name__ == "__main__":
    main()
