# Dữ liệu demo cá nhân và backup — 01/11/2026

Đã thêm dữ liệu mẫu cho đúng tài khoản được yêu cầu (account ID 4), từ **28/09/2026 đến 01/11/2026** theo giờ Việt Nam: 35 ngày, trong đó 32 ngày có hoạt động; không tạo mốc thời gian tương lai.

| Nhóm | Thêm mới | Tổng của tài khoản sau khi thêm |
| --- | ---: | ---: |
| Lịch sử tâm trạng/gợi ý | 89 | 93 |
| Hoạt động mở chi tiết/player/trailer/liên kết | 511 | 526 |
| Đánh giá | 53 | 53 |

- Đủ 7 mood, V/A thay đổi trong vùng mood hiện có.
- 115 phim/bài hát lấy từ catalog đang có; snapshot giữ tên, poster/ảnh bìa và loại nội dung.
- 234 lượt mở chi tiết, 120 tải player, 105 mở trailer, 52 mở liên kết; giờ sáng/trưa/tối, ngày thường/cuối tuần.
- Đủ 1–5 sao: 4 lượt 1 sao, 4 lượt 2 sao, 9 lượt 3 sao, 18 lượt 4 sao, 18 lượt 5 sao. Phân bố theo ngày.
- Đánh giá gắn đúng recommendation/content và có lượt mở chi tiết trước đó.
- Giữ nguyên tài khoản, mật khẩu/quyền, catalog, thư viện đã lưu và các document cũ.

Đây là dữ liệu **tổng hợp để demo lịch sử/thống kê**: `dataOrigin=synthetic_demo`, `isSynthetic=true`, `trainingEligible=false`, `seedBatchId=user-demo-4-20261101-v1`. Mô tả/nhận xét có tiền tố `[Dữ liệu mẫu]`; nguồn mood là manual, algorithm là `demo_manual_v1`. Không tạo analysis ID/model version hoặc giả nhận đã chạy AI trong quá khứ.

Recommender hiện tại đọc các collection này, nên dữ liệu mẫu có thể ảnh hưởng cá nhân hóa. `trainingEligible=false` là nhãn nguồn dữ liệu; khi trích xuất tập huấn luyện/đánh giá cần chủ động loại các bản ghi này, không xem là phản hồi thật hay gold độc lập.

## Kiểm chứng

Trên database tạm đã kiểm tra: validator/index, tham chiếu và ngày giờ, chạy lặp không trùng, rollback giữ số lượng gốc, controller Statistics 7/30/90 ngày và năm 2026, C# đọc history/ratings và các thẻ hoạt động. Sau khi ghi DB chính đã kiểm tra lại bằng controller, xác nhận các document cũ không bị sửa.

## Công cụ

Từ root project, dùng email của một User đang có. Lượt đầu chỉ xem trước:

```powershell
node scripts/seed-user-demo-data.mjs --email "<email>" --days 35 --through-date 2026-11-01
node scripts/seed-user-demo-data.mjs --email "<email>" --days 35 --through-date 2026-11-01 --apply
```

Giữ nguyên ngày/đợt khi chạy lại để không tạo đợt mới. Apply backup BSON trước khi ghi, cấp ID qua counter, giữ audit và kiểm chứng document gốc. Nếu lỗi khi insert/verify, chỉ các document của đợt mới được gỡ. Counter không lùi để tránh tái sử dụng ID.

Kiểm tra đợt này:

```powershell
node scripts/seed-user-demo-data.mjs --email "<email>" --days 35 --through-date 2026-11-01 --batch-id user-demo-4-20261101-v1 --verify
```

Nếu chủ động muốn gỡ dữ liệu mẫu:

```powershell
node scripts/seed-user-demo-data.mjs --email "<email>" --days 35 --through-date 2026-11-01 --batch-id user-demo-4-20261101-v1 --rollback
```

Rollback không gỡ dữ liệu thật/tài khoản khác. Muốn backup phản ánh trạng thái sau rollback thì cần xuất backup lại.

## Backup mới nhất

**`D:\hoc-tap\moodify-project\database_backup\mood_recommendation`**

Đã xuất toàn database sau seed: 25 `.bson`, 25 `.metadata.json` và file phụ của `mongodump`. Đã thử restore sang DB tạm, kiểm tra đợt seed và số lượng các collection nghiệp vụ; DB thử đã được dọn. Bản backup cũ được cất riêng trước khi thay bản mới.

Khôi phục trên máy nhận vào DB trống:

```powershell
mongorestore --uri="mongodb://127.0.0.1:27017" --nsInclude="mood_recommendation.*" --stopOnError ".\database_backup"
```

Chi tiết: [MONGODB_RESTORE.md](MONGODB_RESTORE.md). Bản local có 51 file; 33 file đã tracked, 18 file mới bị `.gitignore` bỏ qua. Nếu chia sẻ, cần gửi nguyên thư mục hoặc kiểm tra đủ file khi chủ động đưa lên Git.

## Chứng cứ local

- `.local-build/user-demo-last-result.json`: số lượng, phân bố mood/action/star, backup trước seed.
- `.local-build/user-demo-seed-test.json`: kiểm thử chạy lặp/rollback trên DB tạm.
- `.local-build/user-demo-stats-main.json`: controller thống kê/lịch sử trên DB chính.
- `.local-build/user-demo-backup-result.json`: hash file, vị trí bản backup cũ và restore-test.
- `database_backup/backup-info-20261101.json`: thông tin backup.

Sau khi thêm trực tiếp trong MongoDB, mở lại màn hình Thống kê hoặc tải lại Lịch sử để lấy dữ liệu mới.
