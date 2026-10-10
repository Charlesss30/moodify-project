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
