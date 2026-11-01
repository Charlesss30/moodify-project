# Baseline mapping Việt/Anh với bộ nhãn Phương chấp nhận — 27/10/2026

## 1. Kết luận và phạm vi

**Đã tiếp nhận đúng bản file mới anh ghi đè, chạy đủ 48 câu bằng hai model đang phục vụ và mapper thật của backend.** Định tuyến đúng ngôn ngữ 48/48; độ khớp mood với nhãn tham chiếu còn thấp trong bộ nhỏ này.

Đây là **đối chiếu chẩn đoán với nhãn do AI soạn, được người dùng kiểm tra/chấp nhận nguyên trạng**, theo xác nhận trong hội thoại. Không gọi bộ này là nhãn độc lập do con người gán mù; không dùng kết quả để tự approve/Activate hoặc tuyên bố chất lượng tổng quát.

- File nguồn: `docs/ai-evaluation/labels-phuong-draft.json`.
- Bản mới ghi `annotatorId=claude-draft-v2`, 48 câu `reviewed=true`, có `reviewedAt` và `labelProvenance`. Giữ nguyên các trường này; không đổi thành một người gán độc lập giả.
- SHA-256 nguồn: `bfc2122c738fee02238b0b8104732f4c40a4c069ec7c16c0c0b65db8364da975`.
- Đóng băng bản nguồn/nhãn/manifest tại `docs/ai-evaluation/completed/phuong-accepted-20261027T163351Z-bfc2122c`.
- 24 cặp Việt/Anh; mỗi ngôn ngữ có 12 validation + 12 test. Không đổi ID, văn bản hoặc split.
- Nhãn VA có đủ 48 câu; 34 câu có mood, 14 câu để mood null hợp lệ.
- Người dùng chấp nhận nhãn không tự làm mất nguồn gốc AI hoặc thay thế review mù thứ hai.

## 2. Phương pháp chạy

Dùng trực tiếp `BilingualMoodAnalysisService` của code backend hiện tại, `language=auto`, gọi Python inference đang chạy. Sao chép read-only `moods` và `aiBilingualPolicyState` sang database tạm để mapper/receipt chạy đúng logic.

Không chép lại thuật toán mapper bằng một công thức khác để tạo điểm tốt hơn. Đây là kiểm tra đường service; UI Mobile và transport controller/auth đã có E2E riêng trước đó, không được tính lại là kết quả của lượt đo này.

| Pipeline | Version | Mapping |
| --- | --- | --- |
| Việt | `phobert_vsmec_v2` | `vsmec-anchors-v1-nearest-center-v1` |
| Anh | `roberta_va_regression_v2` | `regression-nearest-center-v1` |

Đã đọc fingerprint toàn bộ artifact trước/sau, đối chiếu active identity không đổi. Snapshot policy, mood geometry, hash và dự đoán theo ID được lưu cùng thư mục private. Database tạm đã được xóa sau chạy; không tạo receipt/lịch sử/metrics đánh giá vào DB chính.

## 3. Kết quả theo split

Các chỉ số bên dưới là **mức đồng thuận/sai số so với nhãn tham chiếu được chấp nhận**. Mỗi ô chỉ có 12 câu theo ngôn ngữ/split; không suy rộng thành độ chính xác của ứng dụng.

| Ngôn ngữ | Split | Số câu | Được chấp nhận | Mood khớp / câu có nhãn mood | MAE Valence | MAE Arousal |
| --- | --- | ---: | --- | --- | ---: | ---: |
| VI | validation | 12 | 4/12 (33.3%) | 3/9 (33.3%) | 0.270 | 0.285 |
| EN | validation | 12 | 12/12 (100.0%) | 1/9 (11.1%) | 0.267 | 0.373 |
| VI | test | 12 | 7/12 (58.3%) | 2/8 (25.0%) | 0.381 | 0.442 |
| EN | test | 12 | 12/12 (100.0%) | 1/8 (12.5%) | 0.407 | 0.391 |

Cách đọc:

- VI có thể từ chối vì confidence dưới ngưỡng; từ chối được tính là chưa khớp trong cột mood khi câu có mood tham chiếu.
- EN được chấp nhận 12/12 nghĩa kết quả hợp lệ và mapper trả mood; **không có nghĩa 12/12 mood đúng**.
- Mood null ở nhãn tham chiếu được loại khỏi mẫu số mood. Không tự điền mood để tăng/giảm chỉ số.
- MAE càng nhỏ thì VA càng gần giá trị tham chiếu, trên thang mỗi trục từ −1 đến +1.
- Macro-F1 phân loại chỉ áp dụng VI; không so trực tiếp nó với MAE của EN.

Riêng emotion tiếng Việt:

| Split | Emotion khớp trên tất cả câu | Macro-F1 cả 7 lớp | Emotion khớp trong các câu được nhận |
| --- | --- | ---: | --- |
| validation | 6/12 (50.0%) | 0.272 | 4/4 (100.0%) |
| test | 6/12 (50.0%) | 0.352 | 3/7 (42.9%) |

Validation thiếu lớp Anger/Surprise; test thiếu Disgust. Evaluator vẫn tính trung bình cả 7 lớp, lớp không có support nhận F1=0 theo quy ước code. Bộ nhỏ và lệch support chưa đủ để kết luận theo từng lớp hoặc đạt selective accuracy mục tiêu.

Không chỉnh threshold từ các số test này. Test đã được xem trong lượt chẩn đoán; nếu dùng kết quả để thiết kế cải tiến sau đó, cần một bộ test mới chưa dùng để lựa chọn candidate.

## 4. Những chỗ lệch cần xử lý đúng nguyên nhân

### A. Adapter tiếng Việt không biểu diễn được vùng dễ chịu/ít kích hoạt

Trong nhãn tham chiếu có 4 câu VI với V dương/A âm: 1 validation + 3 test. Model/adapter trả 0/4 điểm trong vùng này. Đây phù hợp với giới hạn hình học của bảng neo đã phân tích trước đó; không phải bằng chứng rằng chỉ chỉnh tên mood là sửa được.

Một vài ca minh họa, không phải toàn bộ tập:

| ID | VA tham chiếu | VA dự đoán | Mood tham chiếu | Mood trả về | Trạng thái |
| --- | --- | --- | --- | --- | --- |
| `vi-draft-001` | 0.50 / -0.40 | 0.17 / 0.26 | 3 · Relaxed | Không nhận mood | unknown |
| `vi-draft-019` | 0.70 / 0.40 | 0.24 / 0.34 | 6 · Romantic | Không nhận mood | unknown |
| `vi-draft-024` | 0.60 / -0.30 | 0.62 / 0.61 | 3 · Relaxed | 1 · Happy | matched |
| `en-draft-024` | 0.60 / -0.30 | 0.14 / 0.07 | 3 · Relaxed | 7 · Nostalgic | matched |

Chi tiết toàn bộ 48 dự đoán và các ca lệch nằm trong file JSONL/JSON private. Không thay neo hoặc preprocessing của serving trong lượt này.

### B. Có cả độ lệch giữa nhãn VA và nhãn mood của chính bộ tham chiếu

Ngay cả khi cấp **VA tham chiếu chính xác** cho mapper nearest-center hiện tại, có **10/34 hàng có mood** sẽ ra mood khác nhãn tham chiếu (5 cặp Việt/Anh):

| Cặp câu | Mood tham chiếu | Mood gần nhất từ chính VA tham chiếu |
| --- | --- | --- |
| draft-002 (cặp VI/EN) | 3 · Relaxed | 2 · Sad |
| draft-003 (cặp VI/EN) | 4 · Excited | 5 · Focused |
| draft-006 (cặp VI/EN) | 4 · Excited | 1 · Happy |
| draft-009 (cặp VI/EN) | 5 · Focused | 7 · Nostalgic |
| draft-019 (cặp VI/EN) | 6 · Romantic | 1 · Happy |

Đây không tự chứng minh người gán nhãn sai: nhận định mood mang ngữ nghĩa rộng hơn hai tọa độ, vùng mood có thể chồng lấn, và mục tiêu nội dung mong muốn có thể khác trạng thái hiện tại. Nhưng nó chứng minh không thể quy toàn bộ lỗi mood cho weights NLP.

Cần chốt định nghĩa nhãn là **tâm trạng đang được mô tả**, phân biệt với **muốn thư giãn/tập trung sau đó**. Review các cặp trên rồi quyết định giữ nearest-center hoặc đánh giá một phương pháp mapping khác bằng validation. Không tự đổi nhãn người dùng để ép khớp thuật toán.

### C. Runtime đúng không đồng nghĩa chất lượng đã đạt

Hai model chạy được, định tuyến đúng và fingerprint ổn định. Chỉ số đồng thuận mood còn thấp trong bộ tham chiếu này; chưa có căn cứ nói mapping chính xác cao hoặc candidate đủ điều kiện triển khai.

## 5. Trạng thái sử dụng bộ nhãn

**Đã làm:** dùng đúng bản mới theo xác nhận người dùng; snapshot bất biến; chạy model/mapper thật; báo validation/test riêng và phân tích mismatch.

**Chưa thay đổi:** weights, threshold, preprocessing, điểm neo, policy active hoặc trạng thái quality approval. Không train/Activate model từ 48 câu này.

Bộ này phù hợp để:

1. Ghi baseline chẩn đoán và tập quy trình gán nhãn/evaluation.
2. Tìm các ca bình yên, cảm xúc pha trộn và khác biệt VA → mood cần rà soát.
3. Chuẩn bị review độc lập và mở rộng dữ liệu tự nhiên hợp lệ.

Chưa đủ cho promotion: chỉ 12 mẫu validation/test mỗi ngôn ngữ, thấp hơn cả floor 20 mẫu/split của quality gate hiện tại; chưa có review mù thứ hai và nguồn mẫu/nhãn vẫn do AI soạn. Không tạo `independentEvaluation=true` hoặc approval chính từ bộ này.

## 6. Bằng chứng và bước tiếp theo

Trong thư mục private đã nêu có:

- `labels-source.json`: bytes nguyên bản nguồn mới.
- `accepted-labels.jsonl`, `review-manifest.json`: nhãn đã chấp nhận và provenance.
- `predictions.jsonl`, `responses.json`: kết quả mapper thật theo ID.
- `pipeline-snapshot.json`: version, fingerprint, policy và mood geometry.
- `validation-diagnostic.json`, `test-diagnostic.json`, `diagnostic-summary.json`: chỉ số và các ca cần rà soát.
- `reference-geometry-check.json`: kiểm tra VA tham chiếu → mood.

Thư mục `docs/ai-evaluation/completed/` đã gitignore. File nguồn người dùng đặt ở ngoài thư mục này vẫn được giữ nguyên tại vị trí đã cung cấp.

Bước tiếp theo là rà soát định nghĩa trạng thái/nhu cầu và 5 cặp lệch geometry, bổ sung người gán mù cùng dữ liệu đủ lớn, rồi đánh giá candidate trên validation và một test mới. Chưa tự đổi thuật toán sau khi đã nhìn kết quả test.

Xem [mapping hiện tại](MAPPING_GAN_NHAN_AI_HIEN_TAI.md), [hướng dẫn gán nhãn](HUONG_DAN_GAN_NHAN_AI.md) và [protocol](AI_EVALUATION_PROTOCOL.md).
