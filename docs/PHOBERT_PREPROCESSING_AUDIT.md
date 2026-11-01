# Kiểm tra preprocessing PhoBERT — 27/10/2026

## Kết luận

Chưa tìm thấy `preprocessing.json` gốc trong thư mục model người dùng chỉ hoặc trong cây thư mục Drive đã cung cấp. Hai CSV mới đọc được và đã tạo được JSON tương thích về cấu trúc. **Chưa được dùng JSON mới để thay preprocessing của model active:** dữ liệu khác rõ rệt với dữ liệu được log ở lần train target80.

Đã tạo [JSON ứng viên](../ai-service/config/vietnamese-preprocessing-candidate.json) và [công cụ xuất lại](../ai-service/export_vietnamese_preprocessing.py). JSON là cấu hình ứng viên có nguồn/checksum, không phải bản khôi phục được chứng minh giống lần train cũ.

## Đã tìm ở đâu?

- Tìm đệ quy file trong `D:\hoc-tap\moodify-project-archive-2026-10-09\rar model`.
- Đọc danh sách entry trong 4 ZIP: hai ZIP tải từ Drive và hai ZIP model nhỏ bên trong các thư mục output. Không giải nén/chỉnh weights.
- Duyệt 18 thư mục trong [Drive train-KLCN đã cung cấp](https://drive.google.com/drive/folders/1EPZgTCFjEB2JE3gbvGhKm0vxw8clyqWT); danh sách chờ còn 0. Cây này có GoEmotions/output classifier cũ; không thấy export preprocessing PhoBERT, hai CSV Việt hoặc UIT-VSMEC.
- Đọc `code-train/train-KLCN/AI Vietnamese.ipynb` dưới dạng dữ liệu; không chạy cell cài package, mount Drive hoặc train.

Bằng chứng local: `.local-build/phase2-drive-tree.json`, `.local-build/phase2-find-preprocessing.py`. Kết luận chỉ áp dụng những đường dẫn/cây đã truy cập; không khẳng định file không tồn tại ở mọi Drive/thư mục khác.

## Đối chiếu với lần train gốc

Notebook cell target80 có `execution_count=12`, log bắt đầu `2026-10-17 07:18:47`, đúng tên output `output_phobert_target80_20261017_071847`. Cell xuất JSON sau cùng có `execution_count=null` và không có output lưu. Đây là dấu vết trong notebook, không chứng minh chưa từng export ở notebook khác.

| Nội dung | Log train target80 | Hai CSV mới |
| --- | --- | --- |
| Emoji | 3.323 ánh xạ đã nạp | 332 dòng, 332 khóa giữ nguyên phân biệt hoa/thường |
| Teencode | 215.709 cặp câu hợp lệ; 184.427 cặp cùng số token; trích 3.320 ánh xạ | 352 dòng ánh xạ trực tiếp; dùng được 335 khóa |
| Cột teencode | `correct_text,error_text` | `teencode,standard` |
| Quy tắc tạo từ điển | Căn chỉnh token theo vị trí; ≥5 phiếu và đồng thuận ≥90% | Đọc trực tiếp từ do người dùng cung cấp |
| Chuẩn hóa | Emoji dài trước → bỏ dấu câu → lowercase → thay token teencode → gộp khoảng trắng → PyVi → lowercase | Cùng thuật toán, nhưng từ điển khác |

17 dòng teencode có khóa chứa ký tự bị loại trước bước tra token hoặc rỗng nên không thể được tra theo thuật toán hiện hành; exporter bỏ và ghi số lượng trong provenance. Không sửa các CSV gốc.

Checksum nguồn:

- `vietnamese_emoji.csv`: `e0b3217bf4908af5f2c214bc3e4cf82edf0bec1cb7e036a4884fe11da4368e37`
- `vietnamese_teencode.csv`: `632e649b04aa675bf584b2db8e2659c85c541a12fd09a6186327dd29367f12cb`
- JSON ứng viên: `b025383c22e3afa63a57867028ddfa5227a202fcccdcd443f68ef6585cb8cdc6`

Bản serving `phobert_vsmec_v2/model` hiện thiếu export. Nó tiếp tục fallback 4 emoji và từ điển teencode rỗng như trước. Runtime mới báo rõ `preprocessing_status=legacy_fallback_missing_training_export` cùng SHA-256 của cấu hình thực sự dùng. Có `preprocessing.json` chỉ đổi trạng thái thành bundled; không tự được coi là đã chứng minh khớp train.

## Đã kiểm chứng gì?

- So sánh hàm `preprocess_vietnamese_text` đã đọc trong notebook với normalizer dùng chung train/serve: **670 ca khớp khi cung cấp cùng từ điển**, gồm các khóa emoji/teencode và câu trộn.
- Bằng chứng: `.local-build/phase2-preprocessing-parity.json`.
- Test exporter kiểm tra UTF-8/BOM, emoji dài trước, từ điển thống kê đủ phiếu/đồng thuận và xung đột ánh xạ trực tiếp.
- Loader từ chối JSON sai cấu trúc hoặc normalization version không hỗ trợ.
- Không đổi weights, threshold, điểm neo hoặc marker active. Không train lại model thật bằng hai CSV này.

**670 ca trên là kiểm tra thuật toán chuẩn hóa, không phải 670 nhãn cảm xúc độc lập và không phải accuracy của model.**

## Tạo lại ứng viên trên máy khác

Tại root project, dùng venv AI đã cài:

```powershell
.\ai-service\.venv\Scripts\python.exe ai-service\export_vietnamese_preprocessing.py `
  --emoji docs\vietnamese_emoji.csv `
  --teencode docs\vietnamese_teencode.csv `
  --out .local-build\preprocessing-candidate\preprocessing.json
```

Chọn output mới nếu file đã tồn tại; công cụ không ghi đè. Công cụ hỗ trợ cả hai định dạng teencode và giữ checksum nguồn.

## Cách lấy bản gốc đúng nhất

Nếu phiên Colab train cũ vẫn còn các biến `EMOJI_DICT`, `TEENCODE_DICT`, chạy riêng cell xuất:

```python
import json
from pathlib import Path

target = Path(OUTPUT_DIR) / "preprocessing.json"
target.write_text(
    json.dumps(
        {"emoji_dict": EMOJI_DICT, "teencode_dict": TEENCODE_DICT},
        ensure_ascii=False, indent=2
    ),
    encoding="utf-8"
)
print(target)
```

Nếu runtime đã mất, cần tìm đúng CSV train đầy đủ hoặc export đã sao lưu để dựng lại bằng thuật toán trong notebook. Hai CSV hiện tại không thay thế được bằng chứng đó. Không cần chạy lại toàn bộ huấn luyện chỉ để xuất từ điển nếu dữ liệu/biến gốc còn.

Khi đã khôi phục/đánh giá xong, `preprocessing.json` phải nằm **cạnh `config.json` và weights trong thư mục model** của một phiên bản mới; giữ bản active cũ để rollback. So sánh validation trước, chốt threshold, rồi đánh giá test riêng. Không nhét JSON ứng viên hiện tại vào thư mục active và coi là đã sửa xong parity.