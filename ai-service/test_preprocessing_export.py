"""Dictionary export correctness and train/serve normalization parity."""
import csv
import json
from pathlib import Path
import tempfile
import unittest
from export_vietnamese_preprocessing import build
from vietnamese_preprocessing import normalize, validate


class ExportPreprocessingTests(unittest.TestCase):
    def csv(self, root, name, fields, rows):
        p = root / name
        with p.open("w", encoding="utf-8-sig", newline="") as f:
            writer = csv.writer(f); writer.writerow(fields); writer.writerows(rows)
        return p

    def test_direct_unicode_long_emoji_and_provenance(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            emoji = self.csv(root, "emoji.csv", ["emoji", "description"], [["❤", "yêu"], ["❤️", "yêu thương"]])
            teen = self.csv(root, "teen.csv", ["teencode", "standard"], [["KO", "không"], ["t.t", "khóc"]])
            p = build(emoji, teen)
            class Tokens:
                tokenize = staticmethod(lambda text: text)
            self.assertEqual(normalize("KO ❤️", p["emoji_dict"], p["teencode_dict"], Tokens), "không yêu thương")
            self.assertEqual(p["provenance"]["teencode_unreachable_or_empty_rows_skipped"], 1)
            self.assertIn("not_original", p["provenance"]["teencode_method"])
            self.assertEqual(p["provenance"]["sources"][0]["rows"], 2)
            self.assertEqual(len(p["provenance"]["sources"][0]["sha256"]), 64)

    def test_statistical_pairs_keep_only_sufficient_agreement(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            emoji = self.csv(root, "e.csv", ["emoji", "description"], [])
            teen = self.csv(root, "t.csv", ["correct_text", "error_text"],
                [["tôi không vui", "tôi ko vui"]] * 9 + [["tôi khác vui", "tôi ko vui"]]
                + [["bạn biết", "bạn bít"]] * 4 + [["một từ", "khác số token"]])
            self.assertEqual(build(emoji, teen)["teencode_dict"], {"ko": "không"})

    def test_invalid_model_bundle_preprocessing_fails_closed(self):
        for payload in ([], {"normalization_version": "unknown", "emoji_dict": {}, "teencode_dict": {}},
                        {"emoji_dict": {"❤️": 12}, "teencode_dict": {}},
                        {"emoji_dict": {}, "teencode_dict": []}):
            with self.assertRaises(ValueError):
                validate(payload)
    def test_direct_conflict_does_not_silently_choose(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            e = self.csv(root, "e.csv", ["emoji", "description"], [])
            t = self.csv(root, "t.csv", ["teencode", "standard"], [["ko", "không"], ["ko", "khác"]])
            with self.assertRaises(ValueError):
                build(e, t)


if __name__ == "__main__":
    unittest.main()