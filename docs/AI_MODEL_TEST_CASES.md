# Test model cảm xúc song ngữ 6 lớp

Cập nhật: 01/11/2026. Hai candidate mới được tích hợp vào service, nhưng kết quả test dưới đây là số ghi trong output Colab lúc train; lượt tích hợp này không chạy lại model trên tập test.

## Model và kết quả đã nhận

| Ngôn ngữ | Model | Tập đánh giá | Accuracy | Macro-F1 | Diễn giải |
|---|---|---|---:|---:|---|
| English | `roberta-base`, 6 lớp | `dair-ai/emotion` test, n=2.000 | 93,25% | 90,17% | Cùng nguồn/phân phối với train; chưa chứng minh domain Moodify. |
| Vietnamese | `vinai/phobert-base-v2`, 6 lớp | `test_mt`, n=2.000 | 85,70% | 81,08% | Dịch máy từ English; đo nhất quán của pipeline dịch, không chứng minh tiếng Việt tự nhiên. |
| Vietnamese | cùng model | UIT-VSMEC 5 lớp, n=432 | 79,40% | 64,18% | Test tiếng Việt thật, thấp hơn mục tiêu accuracy 85%. Không làm tròn hoặc gộp với `test_mt`. |

Chưa có `human` set độc lập. Ngưỡng confidence không nằm trong `label_config.json`; API vì vậy trả `accepted: null`. Client phải yêu cầu người dùng kiểm tra/xác nhận mood. Không dùng accuracy test làm confidence threshold.

## Luồng chạy

| Nơi dùng | Định tuyến | Model / endpoint |
|---|---|---|
| Web | Người dùng chọn language | `vi` → `POST /predict-vi` → PhoBERT; `en` → `POST /predict-va` → RoBERTa classifier 6 lớp |
| Mobile | Quy tắc nhẹ đoán ngôn ngữ | Gửi `language` tới `/api/MoodAnalysis/valence-arousal`; backend chọn Python route như bảng trên |
| Legacy classifier | API riêng | GoEmotions 28 nhãn qua `POST /predict`; không đổi trong tích hợp này |

Hai model mới đều trả cảm xúc, confidence, top emotion candidates, V/A và version. V/A = tổng xác suất lớp nhân với anchor đọc từ artifact `label_config.json`; đây không phải V/A regression. Backend chọn mood gần nhất theo vùng V/A và người dùng xác nhận trước recommendation.

## Ca kiểm tra kỹ thuật

Câu trong notebook (`SMOKE_EN`, `SMOKE_VI`) chỉ là smoke do tác giả tự viết, không phải gold set. Có thể dùng để phát hiện lỗi tải model, tokenizer, tiền xử lý, schema và API; không báo accuracy từ các câu này.

Khi đánh giá thật:

1. Ghi `modelVersion`, ngôn ngữ request, nhãn dự đoán, confidence, accepted, V/A và mood được chọn.
2. Giữ riêng kết quả EN, VI `test_mt`, VI `test_vsmec_5cls` và `human`.
3. Với nhãn cảm xúc, báo accuracy, macro-F1/per-class F1 và confusion matrix. Với selective prediction, báo coverage cùng selective accuracy sau khi hiệu chỉnh ngưỡng trên calibration set riêng.
4. Thêm test Moodify do người gán nhãn, ít nhất hai annotator, kèm inter-annotator agreement; không dùng tập test kín để chọn threshold.
5. Chạy cả câu tiếng Việt có dấu, không dấu, teencode, emoji, phủ định, câu trung tính và cảm xúc pha trộn. Sáu lớp không có lớp neutral/Other nên câu trung tính vẫn có top-1 class và cần được người dùng kiểm tra.

## Model artifact

- EN version: `roberta_base_moodify_6c_20261101_051915`; SHA-256 `b28f6902afe7522b42a6ed1c1608a3337a1d085c6905ace96b8d2bf8019070b8`.
- VI version: `phobert_moodify_6c_20261101_053022`; SHA-256 `9902ea9983e3df7939861454ddc9f27963eafbb45ec1582bd7d8b02be99b3790`.
- Metadata, split sizes, metrics, dataset notes and license review status are in each candidate's `manifest.json` under ignored `code-train/train-KLCN/output/`.
- Anchor `Love=(0.8, 0.3)` is a proposal from the notebook, not a validated psychological measurement.

## Mapping và lịch sử cũ

Classifier mới tính V/A từ sáu class anchors và backend tìm nearest mood. `aiEmotionMappings`/Emotion mapping hiện là hợp đồng cố định 28 nhãn của classifier GoEmotions cũ; không thay nó bằng 6 labels. Lịch sử cũ vẫn giữ `modelVersion`; không migrate hoặc xóa DB khi cài model mới. Rule-based mobile analyzer có nhãn cũ riêng, còn response parser của hai classifier mới chỉ chấp nhận taxonomy 6 lớp.
