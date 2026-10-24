# Thống kê cá nhân và training local — 16/10/2026

## Mobile: Cá nhân → Thống kê cá nhân

API `/api/Statistics?days=7|30|90&offsetMinutes=420` dùng tài khoản từ JWT; không nhận ID người dùng tùy ý. MongoDB tổng hợp mọi bản ghi trong khoảng ngày, không giới hạn theo trang lịch sử. Đầu ra có:

- Phân bố mood, số lượt ghi nhận, đánh giá và điểm trung bình.
- Trung bình Valence/Arousal mỗi ngày, ngày thiếu dữ liệu để trống.
- Tương tác mỗi ngày, số lượt phim–nhạc, top thể loại và khung giờ tương tác.
- Số liệu kỳ trước cùng số ngày. Kỳ hiện tại bao gồm hôm nay chưa kết thúc, kỳ trước là các ngày đầy đủ; UI ghi rõ để không hiểu nhầm mức tăng/giảm.

Genre dùng metadata catalog hiện tại; nội dung nhiều thể loại có thể được tính nhiều nhóm. Event mở chi tiết/player/link không tương đương nghe/xem hoàn thành. Không suy ra thời lượng hoặc sức khỏe tâm thần. Múi giờ dùng offset hiện tại của thiết bị; chưa xử lý lịch sử thay đổi DST theo IANA timezone.

## Dashboard: AIEngineer → Training jobs

Có hai đường thực thi:

1. **Colab:** giữ notebook/bundle gốc và import report; trạng thái external vẫn được đánh dấu thủ công, cancel record không dừng Colab.
2. **Local:** tạo job → Start local training → Progress / logs. Nút Stop training process gửi yêu cầu hủy; trạng thái cancelling cho đến khi tiến trình thật dừng. Refresh bảng để cập nhật trạng thái/nút sau khi hoàn tất.

Local worker Python fine-tune tiếp artifact hiện có bằng ASL, theo tham số epoch/batch/LR/gradient accumulation/seed của job. Không tự tải model hoặc tự chạy notebook người dùng cung cấp. Model đang serving và output job tách biệt. Validation ít nhất 400 mẫu, tách selection/calibration; test chỉ dùng sau chọn checkpoint và ngưỡng. Không tuyên bố test độc lập vì base model có thể đã từng thấy dữ liệu đó. Selective policy chưa được xác thực lại nên xuất ở trạng thái disabled, cần review trước serving.

Đây không phải chạy nguyên bản notebook Colab: local worker là pipeline fine-tune riêng. Notebook gốc vẫn dùng cho thực nghiệm từ pretrained model theo quy trình trước đây. Hash worker, base artifact, dataset/config được lưu để truy nguyên.

## Cấu hình và tài nguyên

`Training` trong appsettings:

```json
{
  "Enabled": true,
  "Device": "cpu",
  "AllowSharedGpu": false,
  "MaxHours": 24
}
```

Mặc định dùng `ai-service/.venv/Scripts/python.exe` và model `code-train/train-KLCN/output/roberta_large_asl_en_01/best_model`. Có thể cấu hình đường dẫn tuyệt đối `Training:Python` và `Training:BaseModelDirectory` cho máy riêng. Không thay allowlist/model serving khi cấu hình training.

CPU mặc định tránh tranh VRAM với inference. Full fine-tune RoBERTa-large vẫn cần nhiều RAM và có thể rất chậm. Worker ước lượng tối thiểu RAM/VRAM trống bằng 10 lần kích thước weights cộng 512 MiB trước khi nạp model; không đủ thì job failed kèm log, không ép chạy. Ước lượng này không bảo đảm mọi batch không OOM. CUDA bị chặn khi backend bật AI trừ khi operator chủ động cho phép chia sẻ; với GPU nhỏ nên dùng Colab/máy train riêng.

Chỉ một local job queued/running/cancelling cho mỗi database. File lock ngăn hai backend cùng thư mục dùng chung worker. Chưa phải hàng đợi phân tán nhiều máy. Job đang chạy khi backend restart được đánh dấu failed để review, không tự train lại; Python theo dõi PID cha để dừng khi backend chết. Graceful shutdown dừng process tree. Lỗi storage được log/retry thay vì cố khởi chạy nhiều job.

## Kết quả và model candidate

Output ở `.local-training/<database>/<jobId>/` (gitignored): bundle, progress.json, log giới hạn dung lượng, report và thư mục artifact. Giữ riêng để không đưa raw training text lên GitHub.

Sau process exit thành công, backend kiểm tra report provenance, số mẫu test, metric, cấu hình nhãn và SHA-256 weights, cập nhật job completed rồi đăng ký `aiTrainingArtifacts` với trạng thái `verified_local_candidate_not_deployed`. Models registry đang serving không bị thay. Nếu storage lỗi giữa cập nhật job/candidate, cần đối chiếu artifact/job; đây không phải giao dịch nhiều collection.

Nút Artifact manifest trả đường dẫn server, tên/kích thước file và provenance cho kỹ sư. Muốn triển khai, review chất lượng/calibration, copy artifact vào vị trí serving riêng, cập nhật allowlist/hash/config rồi Validate/Activate bằng quy trình hiện có. Không tự đổi model production từ một metric tốt hoặc tự rollback sang artifact mới chưa kiểm chứng.

## Kiểm thử

Kiểm thử backend dùng MongoDB tạm. Có thể chạy thêm train/cancel thật bằng model RoBERTa rất nhỏ, ngẫu nhiên, không mang chất lượng ứng dụng:

```powershell
./ai-service/.venv/Scripts/python.exe tests/create_training_fixture.py
$env:MOODIFY_TEST_ROOT = (Get-Location).Path
$env:MOODIFY_TRAINING_TEST_MODEL = Join-Path (Get-Location) '.local-build/training-tiny-fixture'
dotnet build tests/Moodify.Integration.csproj
dotnet run --project tests/Moodify.Integration.csproj --no-build
```

Không đặt biến fixture khi chạy stack người dùng. Khi không đặt fixture, tests backend vô hiệu hóa local worker và kiểm tra unavailable, không train nhầm model thật. Test có output riêng dưới `.local-training/moodify_test_*`; DB tạm tự dọn.

Flutter widget tests kiểm tra số liệu >50, đổi khoảng ngày và lỗi API/retry. Backend kiểm tra auth/range/user isolation/genre/previous period; training kiểm tra permission, revision, actual completion/artifact và cancel tiến trình. Kết quả ngày 16/10/2026: 508 assertion backend pass trên MongoDB tạm, gồm train thật bằng tiny RoBERTa, kiểm tra weights thay đổi và PID kết thúc khi hủy; 30 Flutter tests pass; Flutter analyze không có lỗi, build web release thành công; admin lint/build và browser smoke pass. Chưa chạy huấn luyện đầy đủ RoBERTa-large hoặc đo chất lượng model mới; chưa kiểm tra giao diện mới trên thiết bị Android thật.


