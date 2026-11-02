import copy
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from emotion_bundle import inspect_bundle, fingerprint_bundle, LABELS
from evaluate_bilingual import score


def package(root, language="en", version="six_fixture"):
    model = root / ("best_model" if language == "en" else "model")
    model.mkdir(parents=True)
    labels = list(LABELS)
    config = {"id2label": {str(i): label for i, label in enumerate(labels)}, "num_labels": 6,
              "problem_type": "single_label_classification", "max_position_embeddings": 34}
    (model / "config.json").write_text(json.dumps(config), encoding="utf-8")
    (model / "model.safetensors").write_bytes(b"fixture-only-metadata-no-model-loaded")
    for name in ("tokenizer.json", "tokenizer_config.json", "vocab.txt", "bpe.codes"):
        (model / name).write_text("{}", encoding="utf-8")
    payload = {"labels": labels, "id2label": config["id2label"], "max_len": 16,
               "use_word_segmentation": False, "va_anchors": {label: [.5, .2] for label in labels}}
    (root / "label_config.json").write_text(json.dumps(payload), encoding="utf-8")
    (root / "preprocessing.json").write_text(json.dumps({"emoji_dict": {}, "teencode_dict": {}}), encoding="utf-8")
    manifest = {"language": language, "model_version": version,
                "weights_file": (model / "model.safetensors").relative_to(root).as_posix(),
                "weights_sha256": hashlib.sha256((model / "model.safetensors").read_bytes()).hexdigest()}
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    return model


class SixBundleTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_anchors_and_preprocessing_are_part_of_identity(self):
        package(self.root, "vi")
        original = fingerprint_bundle(self.root, "vi", "six_fixture")
        payload = json.loads((self.root / "label_config.json").read_text())
        payload["va_anchors"]["Love"] = [.8, .3]
        (self.root / "label_config.json").write_text(json.dumps(payload))
        changed = fingerprint_bundle(self.root, "vi", "six_fixture")
        self.assertNotEqual(original["bundleSha256"], changed["bundleSha256"])
        self.assertEqual(original["weightSha256"], changed["weightSha256"])
        paths = {f["path"] for f in changed["files"]}
        self.assertTrue({"label_config.json", "preprocessing.json", "manifest.json", "model/config.json"}.issubset(paths))

    def test_rejects_wrong_weight_and_label_order(self):
        model = package(self.root)
        payload = json.loads((model / "config.json").read_text())
        payload["id2label"]["0"], payload["id2label"]["1"] = payload["id2label"]["1"], payload["id2label"]["0"]
        (model / "config.json").write_text(json.dumps(payload))
        with self.assertRaises(ValueError): inspect_bundle(self.root, "en", "six_fixture")
        payload["id2label"] = {str(i): label for i, label in enumerate(LABELS)}
        (model / "config.json").write_text(json.dumps(payload))
        (model / "model.safetensors").write_bytes(b"tampered")
        with self.assertRaises(ValueError): inspect_bundle(self.root, "en", "six_fixture")

    def test_unknown_threshold_is_not_fabricated(self):
        package(self.root)
        with patch.dict(os.environ, {"EN_CONFIDENCE_THRESHOLD": ""}):
            self.assertIsNone(inspect_bundle(self.root, "en", "six_fixture").threshold)
        for invalid in ("NaN", "Infinity", "1.01", "-0.01"):
            with patch.dict(os.environ, {"EN_CONFIDENCE_THRESHOLD": invalid}):
                with self.assertRaises(ValueError): inspect_bundle(self.root, "en", "six_fixture")
        (self.root / "confidence_threshold.json").write_text('{"confidence_threshold":0.8}')
        with patch.dict(os.environ, {"EN_CONFIDENCE_THRESHOLD": "0.5"}):
            self.assertEqual(inspect_bundle(self.root, "en", "six_fixture").threshold, .8)

    def test_model_discovery_and_fingerprint_preserve_both_formats(self):
        from fastapi.testclient import TestClient
        import main
        root = self.root / "models"
        six = root / "six_fixture"
        package(six)
        legacy = root / "legacy_fixture"
        (legacy / "tokenizer").mkdir(parents=True)
        (legacy / "legacy_fixture.pt").write_bytes(b"legacy-fixture")
        (legacy / "tokenizer/tokenizer.json").write_text('{}')
        with patch.dict(os.environ, {"VA_MODELS_ROOT": str(root), "VI_MODELS_ROOT": str(self.root / "vi"),
             "AI_MODEL_ENABLED": "false", "VA_MODEL_ENABLED": "false", "VI_MODEL_ENABLED": "false"}):
            with TestClient(main.app) as client:
                catalog = client.get('/models/va').json()['items']
                self.assertEqual({c['model_type'] for c in catalog}, {'valence_arousal_regression', 'emotion_classification_va_adapter'})
                response = client.get('/models/va/fingerprint/six_fixture')
                self.assertEqual(response.status_code, 200)
                self.assertEqual(response.json()['taxonomy'], 'moodify-six-v1')
                self.assertEqual(client.get('/models/va/fingerprint/legacy_fixture').status_code, 200)
                self.assertEqual(client.get('/health/va/ready').status_code, 503)

    def test_evaluation_keeps_six_and_seven_class_denominators_separate(self):
        for language in ('en', 'vi'):
            gold = [{'id': str(i), 'text': f'unique fixture {i}', 'language': language, 'split': 'test', 'reviewed': True, 'emotion': label} for i, label in enumerate(LABELS)]
            predictions = [{'id': str(i), 'language': language, 'modelVersion': 'six_fixture', 'mappingVersion': 'fixture', 'accepted': False, 'emotion': label} for i, label in enumerate(LABELS)]
            report = score(gold, predictions, taxonomy='moodify-six-v1')['groups'][0]['classification']
            self.assertEqual(report['macroF1AllSixLabels'], 1)
            self.assertEqual(report['coverage'], 0)
            self.assertIsNone(report['selectiveAccuracy'])
            self.assertNotIn('macroF1AllSevenLabels', report)
            gold[0]['emotion'] = 'Other'
            with self.assertRaises(ValueError): score(gold, predictions, taxonomy='moodify-six-v1')

    def test_tiny_training_exports_loadable_uncalibrated_bundle(self):
        # A tiny random model exercises software export/parity only. It is never
        # used as a real candidate or reported as model quality evidence.
        import torch
        from tokenizers import Tokenizer
        from tokenizers.models import WordLevel
        from tokenizers.pre_tokenizers import Whitespace
        from transformers import PreTrainedTokenizerFast, RobertaConfig, RobertaForSequenceClassification
        from train_six_emotion import train
        from emotion_predictor import EnglishEmotionPredictor, VietnameseSixEmotionPredictor
        torch.set_num_threads(2)
        for language in ('en', 'vi'):
            base = self.root / language
            model_dir = package(base, language)
            vocab = {'<unk>':0, '<s>':1, '</s>':2, '<pad>':3, 'fixture':4, 'text':5}
            tokenizer = Tokenizer(WordLevel(vocab, unk_token='<unk>'));tokenizer.pre_tokenizer = Whitespace()
            saved = PreTrainedTokenizerFast(tokenizer_object=tokenizer, unk_token='<unk>', bos_token='<s>', eos_token='</s>', pad_token='<pad>')
            saved.save_pretrained(model_dir)
            config = RobertaConfig(vocab_size=len(vocab), hidden_size=8, num_hidden_layers=1, num_attention_heads=2,
                intermediate_size=16, max_position_embeddings=34, type_vocab_size=1, num_labels=6,
                bos_token_id=1, eos_token_id=2, pad_token_id=3, id2label={i:l for i,l in enumerate(LABELS)}, label2id={l:i for i,l in enumerate(LABELS)})
            RobertaForSequenceClassification(config).save_pretrained(model_dir, safe_serialization=True)
            manifest = json.loads((base/'manifest.json').read_text());manifest['weights_sha256']=hashlib.sha256((model_dir/'model.safetensors').read_bytes()).hexdigest()
            (base/'manifest.json').write_text(json.dumps(manifest))
            rows = [{'id':str(i), 'text':f'fixture text {i}', 'split':'train' if i<6 else 'validation' if i<8 else 'test', 'emotion':LABELS[i%6], 'reviewed':True} for i in range(10)]
            cfg = {'taskType':f'emotion6_{language}', 'seed':42, 'epochs':1, 'batchSize':2, 'gradientAccumulation':1}
            out = self.root / (language+'_out');out.mkdir()
            weights, metrics = train(rows, cfg, base, out, 'six_retrained_fixture', 'cpu')
            self.assertTrue(weights.is_file());self.assertEqual(metrics['coverage'], 0)
            predictor = (EnglishEmotionPredictor if language=='en' else VietnameseSixEmotionPredictor)(out, 'six_retrained_fixture')
            result = predictor.predict('fixture text')
            self.assertIsNone(result['accepted']);self.assertEqual(result['taxonomy'], 'moodify-six-v1')
            self.assertEqual(result['language'], language)


if __name__ == '__main__': unittest.main()
