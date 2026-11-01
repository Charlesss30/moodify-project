"""Structural checks only: no model loading and no claimed prediction quality."""
import ast
from pathlib import Path
import random
import unittest


class VietnameseVaPolicyTests(unittest.TestCase):
    def test_anchor_hull_cannot_reach_positive_low_energy(self):
        module = ast.parse(Path(__file__).with_name("vietnamese_predictor.py").read_text(encoding="utf-8"))
        definition = next(n for n in module.body if isinstance(n, ast.ClassDef) and n.name == "VietnameseEmotionPredictor")
        anchors = next(ast.literal_eval(n.value) for n in definition.body
                       if isinstance(n, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "VA_ANCHORS" for t in n.targets))
        self.assertTrue(all(a >= 0.75 * v - 1e-12 for v, a in anchors))
        generator = random.Random(20261027)
        for _ in range(100):
            weights = [generator.random() for _ in anchors]
            total = sum(weights)
            v = sum(w / total * anchor[0] for w, anchor in zip(weights, anchors))
            a = sum(w / total * anchor[1] for w, anchor in zip(weights, anchors))
            self.assertGreaterEqual(a, 0.75 * v - 1e-12)
            self.assertFalse(v > 0 and a < 0)


if __name__ == "__main__":
    unittest.main()
