# Khôi phục database Moodify trên máy người nhận

Hướng dẫn dành cho bản xuất bằng `mongodump --db mood_recommendation --out "D:\moodify-project\database_backup"`. Bản local kiểm kê ngày 10/10/2026 có 16 file BSON cùng metadata. Đây mới là kiểm kê file, chưa phải xác nhận restore thử bản BSON này. Không dùng kết quả kiểm thử bản JSON trước đó để kết luận bản này đã được kiểm thử.

## 1. Chuẩn bị phần mềm và backup

- Cài MongoDB Community Server và bật dịch vụ MongoDB, mặc định cổng 27017.
- Cài [MongoDB Database Tools](https://www.mongodb.com/docs/database-tools/installation/), thêm thư mục `bin` của tools vào PATH rồi mở lại terminal. Compass là giao diện quản lý, không thay thế Database Tools hoặc MongoDB Server.
- Clone source Moodify và nhận folder backup qua kênh chia sẻ riêng. Giải nén sao cho có cấu trúc:

```text
D:\moodify-project\database_backup\
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
2. Refresh Compass, mở đúng database đích. Đối với bản đang chia sẻ có 16 collection, gồm accounts, contents, movies, music, genres, moods, ratings, moodHistory, interactions và các collection AI/counters. Một collection có thể hợp lệ dù không có document. Backend mới bổ sung `savedContents` cho danh sách phim/nhạc đã lưu theo tài khoản. Bản backup cũ chưa có collection này sẽ được backend tạo rỗng khi khởi động; muốn chuyển cả danh sách đã lưu thì tạo bản dump mới sau khi sử dụng chức năng.
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

Folder backup có tài khoản/password hash, lịch sử và đánh giá của máy xuất; chỉ chia sẻ cho người được phép. Muốn phát hành demo công khai cần bản đã làm sạch. `database_backup/` được gitignore; hướng dẫn này được đưa lên GitHub, còn backup gửi riêng.

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
