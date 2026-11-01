# Khôi phục database Moodify trên máy người nhận

Cập nhật ngày **01/11/2026**: bản backup local mới nằm tại **`D:\hoc-tap\moodify-project\database_backup\mood_recommendation`**. Đã xuất bằng `mongodump` và thử khôi phục sang database tạm thành công: **25 file BSON + 25 file metadata JSON**, cùng file phụ của công cụ. Bản này chứa dữ liệu mẫu 35 ngày cho tài khoản được yêu cầu; xem [dữ liệu demo cá nhân](USER_DEMO_DATA.md).

Các lệnh bên dưới dùng thư mục cha `database_backup`. Nếu bản nhận có tên thư mục khác, thay đường dẫn cuối tương ứng. Không cần insert từng collection thủ công.

## 0. Xuất bản backup mới trên máy chủ project

Dừng các thao tác ghi từ app/backend trong lúc xuất để tránh dữ liệu giữa các collection lệch thời điểm. Giữ MongoDB Server đang chạy. Mở PowerShell:

```powershell
cd D:\hoc-tap\moodify-project

# Dùng thư mục mới, tránh lẫn file từ một lần dump cũ.
if (Test-Path -LiteralPath '.\database_backup') {
    throw 'database_backup đã tồn tại. Đổi tên/cất bản cũ trước khi xuất bản mới.'
}

mongodump --uri="mongodb://127.0.0.1:27017" --db="mood_recommendation" --out=".\database_backup"
if ($LASTEXITCODE -ne 0) { throw 'Backup thất bại; kiểm tra log mongodump.' }

# Xem các file .bson và .metadata.json vừa xuất.
Get-ChildItem -LiteralPath '.\database_backup\mood_recommendation'
```

JSON ở đây là metadata; document nằm trong BSON. Không đổi đuôi file hoặc dùng mongoimport cho BSON. Trong repository hiện tại, 33 file dump đã được Git theo dõi; 18 file mới đang bị quy tắc `database_backup/` trong `.gitignore` bỏ qua. Bản local đầy đủ có **51 file**. Muốn chuyển trọn bản mới nhất cho người nhận, gửi nguyên thư mục `database_backup/mood_recommendation`, hoặc chủ động kiểm tra đủ file khi đưa lên Git. Hướng dẫn này không tự commit/push.

## 1. Chuẩn bị phần mềm và backup

- Cài MongoDB Community Server và bật dịch vụ MongoDB, mặc định cổng 27017.
- Cài [MongoDB Database Tools](https://www.mongodb.com/docs/database-tools/installation/), thêm thư mục `bin` của tools vào PATH rồi mở lại terminal. Compass là giao diện quản lý, không thay thế Database Tools hoặc MongoDB Server.
- Clone source Moodify có kèm folder `database_backup`, hoặc nhận backup riêng và giải nén sao cho có cấu trúc:

```text
D:\hoc-tap\moodify-project\database_backup\
  mood_recommendation\
    accounts.bson
    accounts.metadata.json
    contents.bson
    contents.metadata.json
    movies.bson
    music.bson
    moods.bson
    ...
```

Giữ nguyên toàn bộ file BSON và metadata; metadata chứa cấu hình collection/index đi kèm. File phụ như `prelude.json`, nếu có, giữ nguyên cùng bản xuất. Không đổi đuôi BSON thành JSON.

Kiểm tra công cụ trong PowerShell:

```powershell
mongorestore --version
```

Nếu báo không nhận diện lệnh, kiểm tra PATH hoặc gọi đầy đủ đường dẫn tới `mongorestore.exe`. Nên dùng phiên bản MongoDB tương thích với máy xuất và Database Tools phù hợp; không giả định dump từ mọi phiên bản có thể khôi phục sang mọi phiên bản khác.

## 2. Khôi phục lần đầu

Thực hiện trước khi bật backend/app. Kết nối Compass đến `mongodb://127.0.0.1:27017` và kiểm tra database đích `mood_recommendation` chưa có dữ liệu. Nếu đã có dữ liệu, dùng cách đổi tên ở mục 3 thay vì chạy chồng lên.

Mở PowerShell tại thư mục project và chạy:

```powershell
mongorestore --uri="mongodb://127.0.0.1:27017" --nsInclude="mood_recommendation.*" --stopOnError ".\database_backup"
if ($LASTEXITCODE -ne 0) { throw "Restore thất bại; đọc lỗi trước khi chạy app." }
```

Nếu giải nén ở chỗ khác, ví dụ `C:\dump\mood_recommendation`, dùng **thư mục cha `C:\dump`**:

```powershell
mongorestore --uri="mongodb://127.0.0.1:27017" --nsInclude="mood_recommendation.*" --stopOnError "C:\dump"
```

Lệnh lọc đúng namespace database nguồn; không cần tạo sẵn collection. `mongorestore` khôi phục dữ liệu và metadata/index mặc định. Lệnh không có `--drop`, nhưng **không tự từ chối DB không rỗng** như script JSON của project: vẫn có thể thêm một phần dữ liệu vào DB cũ trước khi gặp lỗi. Vì vậy phải kiểm tra DB đích trước. Restore không phải một giao dịch toàn bộ; nếu lỗi giữa chừng, kiểm tra phần đã nhập và thử lại vào tên DB mới, không chạy lặp mù quáng.

## 3. Nếu máy đã có database cùng tên

Khôi phục sang tên mới chưa dùng, ví dụ `moodify_received`:

```powershell
mongorestore --uri="mongodb://127.0.0.1:27017" --nsInclude="mood_recommendation.*" --nsFrom="mood_recommendation.*" --nsTo="moodify_received.*" --stopOnError ".\database_backup"
```

Sau đó cấu hình backend dùng `moodify_received`. Không thêm `--drop` khi chưa chủ động quyết định thay thế dữ liệu; tùy chọn đó xóa các collection đích được khôi phục trước khi nạp.

## 4. Kiểm tra sau khôi phục

1. Đọc tổng kết: số document đã restore phải phù hợp với bản nhận và số failed bằng 0. Nếu `0 restored` toàn bộ, kiểm tra cấu trúc giải nén, đường dẫn và bộ lọc namespace.
2. Refresh Compass, mở đúng database đích và đối chiếu các collection với file BSON nhận được. Catalog, accounts, moodHistory, ratings và các collection AI/metadata có số lượng tùy thời điểm xuất; một collection có thể hợp lệ dù không có document.
3. Kiểm tra vài tên phim/bài hát, vùng mood và index; không chỉ dựa vào việc thấy tên database.

Số document phụ thuộc thời điểm xuất. Con số 369 trong hướng dẫn backup cũ là của bản JSON ngày 09/10/2026, không phải số đã xác minh cho bản BSON này.

## 5. Cấu hình backend và chạy ứng dụng

Nếu chưa có file local, chép `backend/appsettings.Development.example.json` thành `backend/appsettings.Development.json`. Nếu file đã tồn tại thì chỉnh mục Mongo, không ghi đè các cấu hình riêng khác:

```json
"Mongo": {
  "ConnectionString": "mongodb://127.0.0.1:27017",
  "Database": "mood_recommendation"
}
```

Nếu dùng mục 3, đổi Database thành `moodify_received`. Đoạn trên là một mục trong file JSON, không phải toàn bộ file. Kiểm tra biến môi trường `Mongo__Database`/`Mongo__ConnectionString` nếu đã thiết lập vì chúng có thể ghi đè cấu hình file. Các ví dụ giả định MongoDB local không bật authentication; server có authentication cần URI/thông tin đăng nhập riêng của người quản trị MongoDB.

Cài dependency theo [README](../README.md), sau đó từ root project:

```powershell
.\Start.ps1 -WithoutAI
```

Mở `http://localhost:5170` để đăng nhập; Admin/AIEngineer chuyển đến dashboard ở cổng 5174. Tài khoản ứng dụng trong `accounts` được khôi phục cùng password hash và role, nhưng hash không cho biết mật khẩu để đăng nhập. Người nhận cần tài khoản demo/mật khẩu được chủ project cấp riêng, hoặc đăng ký mới rồi nhờ người quản trị cấp vai trò. Không có mật khẩu mặc định được suy ra từ backup. Tài khoản ứng dụng khác với tài khoản xác thực MongoDB Server.

Chạy Flutter theo README: emulator dùng `http://10.0.2.2:5170`; web/Windows dùng `http://localhost:5170`; điện thoại thật dùng IP LAN của máy backend.

## 6. Những gì backup không cung cấp

- Model weights/tokenizer và môi trường Python: cần cấp riêng theo `code-train/README.md` và `ai-service/README.md`. Restore registry active không làm AI tự chạy. Sau khi runtime sẵn sàng, kiểm tra Models → Validate/Activate nếu cần.
- Khóa TMDB/Spotify: cấu hình riêng để tra cứu/nhập mới từ nhà cung cấp. Metadata đã lưu vẫn phụ thuộc URL ảnh/video và dịch vụ ngoài khi hiển thị/phát.
- Phim và file nhạc đầy đủ: hệ thống sử dụng metadata, ID/URL và player nhà cung cấp.

Folder backup có tài khoản/password hash, lịch sử và đánh giá của máy xuất; chỉ chia sẻ cho người được phép. Muốn phát hành demo công khai cần bản đã làm sạch. Quy tắc ignore không làm các file đã tracked ngừng được theo dõi; cần kiểm tra cả file mới nếu chia sẻ qua Git.

## Lỗi thường gặp

| Lỗi | Cách xử lý |
|---|---|
| `mongorestore` không nhận diện | Cài Database Tools, sửa PATH, mở terminal mới |
| Connection refused | Bật MongoDB Server, kiểm tra host/cổng |
| Duplicate key / E11000 | Có dữ liệu trùng hoặc đã restore một phần; chuyển sang DB mới, không tự xóa DB cũ |
| Restore thành công nhưng app trống | Kiểm tra Mongo:Database/URI và biến môi trường, restart backend sau khi đổi cấu hình |
| Đăng nhập không được | Cần mật khẩu thật tương ứng tài khoản; hash trong dump không dùng làm mật khẩu |
| AI unavailable | Kiểm tra artifact, môi trường Python, runtime, cấu hình AI và registry riêng |

## Phân biệt hai định dạng backup

| Bản backup | Công cụ khôi phục |
|---|---|
| `.bson` + `.metadata.json` trong thư mục tên DB | `mongorestore` theo hướng dẫn này |
| `manifest.json` + collection JSON do project xuất | `node scripts/mongo-backup.mjs import ...` theo README |

Tham khảo chính thức: [mongorestore](https://www.mongodb.com/docs/database-tools/mongorestore/) và [ví dụ restore, lọc/đổi namespace](https://www.mongodb.com/docs/database-tools/mongorestore/mongorestore-examples/).
