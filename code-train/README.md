# Notebook và artifact nghiên cứu

Git giữ `train-KLCN/RoBERTa_large.ipynb`. Template ở `backend/Training/` phục vụ tải notebook từ dashboard và cần giữ. Notebook chạy Colab, không tự chạy từ runner; chỉnh đường dẫn Drive/dataset theo môi trường người chạy.

Dataset GoEmotions, MovieLens, Spotify, log và output model được cấp riêng. Không coi tên dataset là giấy phép phân phối. Máy phát triển giữ output đang dùng tại chỗ nhưng gitignore; dữ liệu train gốc đã chuyển sang archive cạnh project.

Khôi phục đầy đủ artifact để chạy AI và các test liên quan:

```text
code-train/train-KLCN/output/roberta_large_asl_en_01/
  best_model/      weights, tokenizer, config
  deployment/      deployment/config đi kèm
  run_config.json
```

SHA-256 dự kiến của `best_model/model.safetensors`:

```text
EA865317D9EB7CF1B39008062276BAE441801A7D719D33177A3DDD1851AF7B09
```

Đây là RoBERTa-large đa nhãn tiếng Anh; không tự thay bằng DistilBERT hay tải weights từ mạng. Runner kiểm tra hash/version và cần Python environment riêng. Thiếu artifact vẫn chạy backend/dashboard/mobile với AI tắt, chọn mood thủ công, nhưng không chạy đầy đủ test AI. Xem `docs/REPOSITORY_CLEANUP.md` và `ai-service/README.md`.


## Model cảm xúc song ngữ 6 lớp — 01/11/2026

Candidate mới được cài local (thư mục `output/` nằm trong `.gitignore`; không commit weights):

```text
output/roberta_base_moodify_6c_20261101_051915/
  best_model/ model.safetensors + tokenizer/config
  label_config.json, manifest.json, test_report.json, training_metrics.json, training_log.txt
output/vietnamese-emotion/phobert_moodify_6c_20261101_053022/
  model/ model.safetensors + tokenizer/config
  label_config.json, preprocessing.json, manifest.json, test_report.json, training_metrics.json, training_log.txt
```

English `roberta-base`: `dair-ai/emotion` test n=2,000, accuracy 93.25%, macro-F1 90.17% (in-distribution). Vietnamese `vinai/phobert-base-v2`: translated test n=2,000, 85.70%/81.08%; UIT-VSMEC 5-class test n=432, 79.40%/64.18%. Vietnamese real-text result is below the requested 85% accuracy target. Metrics are reported separately and were not rerun during integration.

Both candidates return six-way emotion classification; V/A is calculated from `label_config.json` anchors. The outputs do not contain calibrated confidence thresholds, so `accepted` remains null until separately calibrated. Dataset/base-model/translation licenses require review; see candidate manifests and `docs/AI_MODEL_TEST_CASES.md`. The English candidate is the runner's default unless an active marker selects another model. Vietnamese requires explicit activation.

The original GoEmotions 28-label artifact above remains a separate legacy classifier and is not replaced by these artifacts.
