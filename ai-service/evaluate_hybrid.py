"""Offline Hybrid weight selection on validation-only historical component snapshots.

No training, activation or production reads. Scores must be rebuilt using only
feedback available before each request; the tool cannot prove that provenance.
"""
import argparse
from datetime import datetime
import hashlib
import itertools
import json
import math
from pathlib import Path
import statistics

BASELINE = (.55, .30, .15)


def timestamp(value):
    t = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if t.tzinfo is None:
        raise ValueError("Timestamps need explicit timezone.")
    return t


def validate(data):
    if data.get("schemaVersion") != 1 or len(data.get("sourceEvidence", "")) < 20:
        raise ValueError("Declare snapshot schema and source/temporal reconstruction evidence.")
    rows = data.get("contexts", [])
    ids = set()
    for r in rows:
        if not r.get("id") or r["id"] in ids or r.get("split") not in ("validation", "test") or r.get("reviewed") is not True:
            raise ValueError("Every reviewed context needs a unique ID and explicit validation/test split.")
        ids.add(r["id"])
        if timestamp(r["profileThrough"]) >= timestamp(r["requestedAt"]):
            raise ValueError("Profile/model snapshot includes future feedback.")
        items, item_ids = r.get("items", []), set()
        if not items:
            raise ValueError("A context needs its complete candidate pool.")
        for i in items:
            if not isinstance(i.get("id"), str) or not i["id"] or i["id"] in item_ids:
                raise ValueError("Candidate IDs must be stable and unique within context.")
            item_ids.add(i["id"])
            if type(i.get("disliked")) is not bool or not isinstance(i.get("genres"), list) or any(not isinstance(g, str) for g in i["genres"]):
                raise ValueError("Declare prior dislike and genre features separately from future labels.")
            if any(key not in i for key in ("mood", "content", "collaborative")):
                raise ValueError("Declare all score components; unavailable components must be null.")
            for key in ("mood", "content", "collaborative"):
                value = i.get(key)
                if value is None and key != "mood":
                    continue
                if type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1:
                    raise ValueError("All supplied components must use the serving [0,1] scale.")
            d = i.get("distanceSquared")
            if type(d) not in (int, float) or not math.isfinite(d) or not 0 <= d <= 8:
                raise ValueError("Supply exact distanceSquared for serving tie order.")
            stars = i.get("futureStars")
            if stars is not None and (type(stars) is not int or not 1 <= stars <= 5 or timestamp(i["labelAt"]) < timestamp(r["requestedAt"])):
                raise ValueError("Explicit future stars and label timestamps are required for relevance.")
        if not any((i.get("futureStars") or 0) >= 4 for i in items):
            raise ValueError("No observed positive for this context; report it outside ranking evaluation.")
    val = [r for r in rows if r["split"] == "validation"]
    test = [r for r in rows if r["split"] == "test"]
    if not val or not test or max(timestamp(r["requestedAt"]) for r in val) >= min(timestamp(r["requestedAt"]) for r in test):
        raise ValueError("Validation contexts must precede all test requests.")
    if max(timestamp(i["labelAt"]) for r in val for i in r["items"] if i.get("futureStars") is not None) >= min(timestamp(r["requestedAt"]) for r in test):
        raise ValueError("Validation outcomes must be known before the test period starts.")
    return val, test


def rank(items, weights):
    def score(item):
        pairs = [(item[k], w) for k, w in zip(("mood", "content", "collaborative"), weights) if item[k] is not None and w > 0]
        # Content-only/CF-only baselines fall back to mood for cold candidates.
        value = sum(v * w for v, w in pairs) / sum(w for _, w in pairs) if pairs else item["mood"]
        return value * (.5 if item["disliked"] else 1)
    return sorted(items, key=lambda i: (-score(i), i["distanceSquared"], i["id"]))


def metrics(rows, weights, k):
    values, selected, catalog, diversity, cold = [], set(), set(), [], 0
    for r in rows:
        ranked = rank(r["items"], weights)[:k]
        positive = {i["id"] for i in r["items"] if (i.get("futureStars") or 0) >= 4}
        hits = [int(i["id"] in positive) for i in ranked]
        ideal = sum(1 / math.log2(n + 2) for n in range(min(k, len(positive))))
        dcg = sum(v / math.log2(n + 2) for n, v in enumerate(hits))
        values.append((sum(hits) / k, sum(hits) / len(positive), dcg / ideal))
        selected.update(i["id"] for i in ranked)
        catalog.update(i["id"] for i in r["items"])
        cold += not any(i["collaborative"] is not None for i in r["items"])
        for a, b in itertools.combinations(ranked, 2):
            ga, gb = set(a["genres"]), set(b["genres"])
            if ga or gb:
                diversity.append(1 - len(ga & gb) / len(ga | gb))
    return {"contexts": len(rows), "precisionAtK": statistics.mean(x[0] for x in values),
            "recallAtK": statistics.mean(x[1] for x in values), "ndcgAtK": statistics.mean(x[2] for x in values),
            "catalogCoverage": len(selected) / len(catalog), "genreDiversity": statistics.mean(diversity) if diversity else None,
            "collaborativeColdContexts": cold}


def evaluate(data, k=10):
    if type(k) is not int or not 1 <= k <= 100:
        raise ValueError("K must be 1–100.")
    val, test = validate(data)
    # The test labels are never read by the selection objective.
    candidates = [BASELINE] + [(m / 20, c / 20, (20-m-c) / 20)
                               for m in range(4, 18) for c in range(2, 20-m) if 20-m-c >= 1]
    chosen = max(candidates, key=lambda w: (metrics(val, w, k)["ndcgAtK"], -sum(abs(a-b) for a, b in zip(w, BASELINE))))
    baselines = {"currentHybrid": BASELINE, "moodOnlyWithDislike": (1, 0, 0),
                 "contentWithMoodFallback": (0, 1, 0), "collaborativeWithMoodFallback": (0, 0, 1),
                 "selectedOnValidation": chosen}
    return {"schemaVersion": 1, "k": k, "selectedWeights": dict(zip(("mood", "content", "collaborative"), chosen)),
            "validation": {name: metrics(val, w, k) for name, w in baselines.items()},
            "test": {name: metrics(test, w, k) for name, w in baselines.items()},
            "status": "offline_candidate_not_deployed",
            "limitations": ["Observed explicit positives only; unjudged items are not confirmed negatives.",
                           "Snapshot temporal provenance is declared by exporter, not provable from scores alone.",
                           "Evaluation is before the serving Movie/Song balancing quota.",
                           "Metrics do not prove causal benefit or replace a reviewed online pilot."]}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--data", required=True); p.add_argument("--out", required=True); p.add_argument("--k", type=int, default=10)
    args = p.parse_args()
    raw = Path(args.data).read_bytes()
    result = evaluate(json.loads(raw.decode("utf-8-sig")), args.k)
    result["datasetSha256"] = hashlib.sha256(raw).hexdigest()
    target = Path(args.out)
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("x", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, allow_nan=False, indent=2)
    print("Offline comparison written. Serving weights and experiment state unchanged.")


if __name__ == "__main__":
    main()