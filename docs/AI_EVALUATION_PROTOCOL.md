# Quy trình đánh giá AI và Hybrid

Cập nhật 27/10/2026. Các công cụ đã có; nhãn độc lập và quyết định nghiệm thu phải được người phụ trách dữ liệu/GVHD rà soát. Smoke, dữ liệu tổng hợp và readiness không phải bằng chứng chất lượng người dùng.

## 1. Tập NLP song ngữ

Chuẩn bị tối thiểu 100 mô tả mới mỗi ngôn ngữ để có thể phân tích lỗi ban đầu; số lượng này là mức khởi đầu, không bảo đảm đủ sức mạnh thống kê. Lưu nguồn, quyền sử dụng, thời điểm và người gán nhãn. Không lấy trực tiếp dữ liệu train hoặc 20 câu demo làm test mới.

Bao phủ: vui/buồn/sợ/giận, bình yên/tập trung/hoài niệm/lãng mạn, phủ định, câu ngắn/không dấu, emoji/teencode, cảm xúc pha trộn và câu Việt/Anh tương đương. Các nhãn khác nhau phải được gán riêng:

- `emotion`: chỉ 7 nhãn VSMEC, dành cho VI; không gán Relaxed vào Other.
- `valence/arousal`: tọa độ [-1,1], có hướng dẫn và thang gốc nếu chuyển thang.
- `moodId`: chỉ ID mood catalog đã chốt; có lựa chọn bất đồng/không xác định trong quá trình rà soát, không ép mọi câu vào một mood.

Hai người gán nhãn độc lập, rà soát bất đồng; ghi tỷ lệ đồng thuận và số loại bỏ. Tách train/validation/test trước khi chọn candidate/threshold. Kiểm tra trùng văn bản sau NFKC/lowercase/gộp khoảng trắng và trùng ý nghĩa giữa các split. Các cặp song ngữ cùng tình huống phải ở cùng split.

Mẫu JSONL của một hàng **chưa được review**:

```json
{"id":"vi-001","text":"Hôm nay tôi thấy bình yên sau khi đi dạo.","language":"vi","split":"validation","reviewed":false,"emotion":null,"valence":null,"arousal":null,"moodId":null}
```

Không đổi `reviewed=true` tự động. Hoàn thiện nhãn rồi mới đưa vào evaluator hoặc typed dataset của Dashboard.

## 2. Đánh giá và chốt triển khai

Giữ artifact/config/threshold/hash, phiên bản policy, vùng mood và danh sách split cố định.

1. Đo baseline bằng preprocessing thực sự đang phục vụ; nếu thiếu bản train gốc phải ghi rõ.
2. Chọn mapping/threshold/candidate bằng validation. Không xem test để chỉnh tiếp cùng candidate.
3. Chốt tiêu chí trước khi chạy test: độ không suy giảm so với baseline, minimum coverage/accepted count, sai số VA theo vùng, latency và ngân sách tài nguyên. Ngưỡng cụ thể cần phù hợp bộ dữ liệu đã review; không suy ra từ vài câu đẹp.
4. Xuất dự đoán có `id/language/modelVersion/mappingVersion/accepted` và nhãn/VA đúng loại.
5. Chạy evaluator; báo cáo cả support, abstention và giới hạn. Macro-F1 dùng cả 7 lớp. Regression dùng MAE/RMSE/Pearson và sai số theo vùng, không tạo accuracy phân loại.

```powershell
.\ai-service\.venv\Scripts\python.exe ai-service\evaluate_bilingual.py `
  --data .local-build\gold-reviewed.jsonl `
  --predictions .local-build\candidate-predictions.jsonl `
  --split test --out .local-build\evaluation-test.json
```

Evaluator chống trùng trong các hàng được cung cấp; không tự chứng minh đã kiểm tra toàn bộ train cũ. Chỉ Activate khi người phụ trách chấp thuận bằng chứng; Validate loading trong dashboard chỉ là readiness. Đã có quality gate nối các API Activate/rollback VI/VA, nhưng chưa có bộ gold mới đã review để approve candidate thực tế. Server kiểm tra report do kỹ sư nộp, không tự chứng minh chất lượng nhãn hoặc tính lại toàn bộ dự đoán.

## 3. Matrix Factorization và Hybrid

MF mới dùng sao 1–5 mới nhất cho từng user–item trong mỗi khoảng thời gian; mở link là tín hiệu yếu của hồ sơ/bandit, không giả sao hoặc nhãn cảm xúc. ID phải ánh xạ vào tài khoản/catalog hiện tại trước khi Activate.

Có ba mức so sánh khác nhau:

| Mức | Công cụ | Bằng chứng |
| --- | --- | --- |
| MF explicit ratings | `matrix_factorization.py` | RMSE/MAE/mean baseline, Precision/Recall/NDCG@10, coverage; chọn epoch bằng validation |
| Hybrid component ranking | `evaluate_hybrid.py` | Chọn trọng số bằng NDCG validation, báo test riêng và baseline, diversity/cold context |
| Online pilot | Dashboard Feedback analytics | Nhóm account ổn định, arm/propensity/version, reward từ feedback thật; mặc định tắt |

Ví dụ chạy MF offline:

```powershell
.\ai-service\.venv\Scripts\python.exe ai-service\matrix_factorization.py `
  --data .local-build\ratings-reviewed.json `
  --train-before '2026-09-01T00:00:00Z' `
  --validation-before '2026-10-01T00:00:00Z' `
  --version mf_candidate_001 --out .local-build\mf_candidate_001.json
```

Không dùng MovieLens item ID trực tiếp làm ID phim Moodify. Không train từ request Mobile.

`evaluate_hybrid.py` nhận snapshot JSON `schemaVersion=1`, `sourceEvidence` và `contexts`. Mỗi context có `id/split/reviewed/profileThrough/requestedAt/items`. Mỗi item có `id/mood/content/collaborative/disliked/distanceSquared/genres/futureStars/labelAt`; thành phần không có phải là `null`. Dựng profile/factors chỉ bằng dữ liệu trước `requestedAt`; sao tương lai chỉ dùng làm relevance để đo, không làm feature. Validation outcomes phải có trước giai đoạn test.

```powershell
.\ai-service\.venv\Scripts\python.exe ai-service\evaluate_hybrid.py `
  --data .local-build\hybrid-temporal-snapshots.json `
  --k 10 --out .local-build\hybrid-comparison.json
```

Đây là đánh giá trước quota cân bằng phim/nhạc. Unjudged không phải negative đã xác nhận; snapshot score không tự chứng minh provenance thời gian. Trọng số được chọn chỉ là ứng viên báo cáo, không tự ghi vào serving.

DB chính mới có 7 rating tại snapshot. Không đủ để chốt MF/Hybrid tốt hơn hoặc mở A/B đáng tin cậy; gate import MF yêu cầu ít nhất 20 warm validation và 20 warm test pair, RMSE không tệ hơn mean baseline. Gate kỹ thuật này không thay thế đánh giá ranking/catalog mapping.

## 4. Phạm vi bandit và vận hành

Đã có contextual epsilon-greedy bandit: context=mood ID, action=bộ trọng số Hybrid, reward=sao mới nhất; nếu chưa có sao thì một tín hiệu mở link nhỏ. Lưu xác suất chọn arm và phân nhóm account. Đây không phải RL nhiều bước.

Bật pilot chỉ sau review baseline/offline, phạm vi với GVHD và kế hoạch dừng. Nhãn “generated” chỉ chứng minh hệ thống đã tạo gợi ý, không chứng minh người dùng đã thấy/nghe/xem hết. Báo feedback count riêng; người không phản hồi không được coi là dislike.

Lịch retrain do người vận hành chủ động tạo job mới từ snapshot mới; không có scheduler auto-retrain MF hoặc tự Promote model. Giữ version cũ để rollback; cache factors theo immutable version, không cache hồ sơ đánh giá giữa request.
## 5. Quy trình và công cụ hiện tại

- [Hướng dẫn gán nhãn cho người dùng](HUONG_DAN_GAN_NHAN_AI.md); công cụ offline `tools/ai-label-review.html`.
- `docs/ai-evaluation/review-starter.json` có 48 câu AI soạn **chưa review**, dùng tập quy trình; không coi là test độc lập đạt nghiệm thu.
- Khi có gold, tạo dataset `purpose=evaluation`, `independentEvaluation=true`, validation/test riêng; chốt immutable quality policy trước job. Tải bundle và dùng `train_experiment.py` trên artifact tồn tại; runner tự chuyển evaluation-only.
- `evaluate_candidate.py` xuất split predictions/metrics và fingerprint đầy đủ mà không huấn luyện/tune/đổi active. VI/EN có metric đúng task; report và nguồn nhãn do kỹ sư chịu trách nhiệm.
- [Workflow gate chi tiết](AI_ENGINEER_IMPLEMENTATION.md): review → approval khớp hash → Activate. Readiness hoặc baseline cũ không được ghi là quality validated.
- Hiện chưa có metric độc lập mới đủ để chốt cải thiện NLP/MF/Hybrid. RL/bandit không bắt buộc theo chỉ đạo hiện tại, pilot tắt.
