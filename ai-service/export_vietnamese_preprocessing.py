"""Export reproducible Vietnamese preprocessing candidates, never modify serving artifacts."""
import argparse
from collections import Counter, defaultdict
import csv
import hashlib
import json
from pathlib import Path
import re
from vietnamese_preprocessing import VERSION

TOKEN = re.compile(r"^\w+$", flags=re.UNICODE)


def table(path):
    with Path(path).open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        rows = list(reader)
        return set(reader.fieldnames or []), rows


def build(emoji_path, teencode_path):
    fields, rows = table(emoji_path)
    if not {"emoji", "description"} <= fields:
        raise ValueError("Emoji CSV requires emoji,description.")
    emojis, emoji_conflicts = {}, 0
    for row in rows:
        key, value = (row.get("emoji") or "").strip(), (row.get("description") or "").strip()
        if key and value:
            emoji_conflicts += key in emojis and emojis[key] != value
            emojis[key] = value  # Same last-row behavior as the notebook's loader.
    emojis = dict(sorted(emojis.items(), key=lambda x: len(x[0]), reverse=True))
    emoji_rows = len(rows)
    fields, rows = table(teencode_path)
    teencodes, skipped = {}, 0
    if {"correct_text", "error_text"} <= fields:
        mode = "notebook_sentence_pair_votes_min5_confidence090"
        votes = defaultdict(Counter)
        for row in rows:
            good, bad = row.get("correct_text"), row.get("error_text")
            if not good or not bad:
                continue
            good, bad = good.lower().strip().split(), bad.lower().strip().split()
            if not good or len(good) != len(bad):
                continue
            for correct, error in zip(good, bad):
                if correct != error and TOKEN.fullmatch(correct) and TOKEN.fullmatch(error):
                    votes[error][correct] += 1
        for error, choices in votes.items():
            correct, count = choices.most_common(1)[0]
            if count >= 5 and count / sum(choices.values()) >= .9:
                teencodes[error] = correct
    elif {"teencode", "standard"} <= fields:
        mode = "user_supplied_direct_dictionary_not_original_training_export"
        for row in rows:
            key, value = (row.get("teencode") or "").strip().lower(), (row.get("standard") or "").strip().lower()
            if not key or not value or not TOKEN.fullmatch(key):
                skipped += 1
                continue
            if key in teencodes and teencodes[key] != value:
                raise ValueError("Conflicting direct teencode key: " + key)
            teencodes[key] = value
    else:
        raise ValueError("Teencode CSV needs correct_text,error_text or teencode,standard.")
    def source(path, count):
        path = Path(path)
        return {"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "rows": count}
    return {
        "normalization_version": VERSION, "emoji_dict": emojis, "teencode_dict": teencodes,
        "provenance": {
            "status": "candidate_not_verified_against_original_training",
            "teencode_method": mode,
            "sources": [source(emoji_path, emoji_rows), source(teencode_path, len(rows))],
            "emoji_conflicting_rows_last_wins": emoji_conflicts,
            "teencode_unreachable_or_empty_rows_skipped": skipped,
            "original_notebook_logged_counts": {"emoji": 3323, "teencode": 3320},
            "note": "Identical normalization code does not prove identical training dictionaries. Evaluate a separate candidate before deploying.",
        },
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--emoji", required=True)
    parser.add_argument("--teencode", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    payload = build(args.emoji, args.teencode)
    target = Path(args.out)
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(payload, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write("\n")
    print(json.dumps({"output": str(target), "emoji": len(payload["emoji_dict"]), "teencode": len(payload["teencode_dict"]),
                      "status": payload["provenance"]["status"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()