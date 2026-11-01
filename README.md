# Moodify — Gợi ý phim và âm nhạc theo tâm trạng

Hệ thống gồm Flutter, website Admin/AIEngineer, ASP.NET Core API và Python inference RoBERTa. Dữ liệu nghiệp vụ dùng **MongoDB**, phim tích hợp **TMDB**, nhạc tích hợp **Spotify**. Repo không chứa database cá nhân, file phim/nhạc hay trọng số model.

## Chức năng

- User: đăng ký/đăng nhập email hoặc Google; quên mật khẩu qua email; cài đặt thông báo đẩy Android; quiz emoji; khám phá mô tả tiếng Việt/tiếng Anh hoặc chọn mood; thư viện; nội dung được quan tâm/vừa cập nhật; thống kê theo ngày/năm; chi tiết phim/nhạc, trailer YouTube, Spotify player và liên kết gốc; lịch sử tương tác; đánh giá 1–5 sao, nhận xét trong chi tiết.
- Admin: nội dung, thể loại, người dùng, mood/vùng VA; Feedback; Users → Activity; Recommendations và quản lý vòng đời model.
- AIEngineer: AI overview, Models, Emotion mapping, Evaluation, Datasets, Training jobs, Feedback analytics, Inference lab, Recommendations.
- Recommender: Mood Matching + Content-based + Collaborative Filtering; phản hồi mới cập nhật hồ sơ và thứ hạng ở yêu cầu tiếp theo.

Phân tích mô tả dùng hai classifier cảm xúc 6 lớp: **PhoBERT tiếng Việt** và **RoBERTa-base tiếng Anh**. V/A được tính từ anchor trong `label_config.json`, không phải output VA regression. Model Việt cần được activate; model Anh candidate mới được local runner chọn mặc định nếu không có active marker. Mobile tự nhận diện ngôn ngữ và hiển thị model dưới ô nhập; Web chọn ngôn ngữ trên form. Người dùng xác nhận mood trước khi lấy gợi ý và có thể chọn mood thủ công. Mapping còn thử nghiệm. Training jobs chuẩn bị notebook Colab/nhận báo cáo, **không tự train GPU trên web**. Feedback Loop không phải Reinforcement Learning.

## Cấu trúc repository

| Thư mục | Vai trò |
|---|---|
| backend | .NET 10 API, schema MongoDB, gateway AI, recommender |
| admin-web | Dashboard Vite/JavaScript theo vai trò |
| mobile-app | Flutter: Trang chủ, Khám phá, Thư viện, Lịch sử, Cá nhân |
| ai-service | Python/FastAPI, inference RoBERTa local |
| code-train | Notebook; dataset và weights cấp riêng |
| scripts | Runner local, backup/restore, bảo trì |
| tests | Kiểm thử tích hợp .NET và notebook |
| docs | Hướng dẫn, báo cáo và bằng chứng theo ngày |

## 1. Chuẩn bị

Cần .NET SDK 10, MongoDB tại `127.0.0.1:27017`, Node.js 24 (đã dùng trên máy phát triển), npm, Flutter phù hợp `mobile-app/pubspec.yaml`. Android cần Android SDK/emulator. AI cần Python và artifact theo [hướng dẫn inference](ai-service/README.md).

Các lệnh PowerShell bắt đầu từ root repository:

```powershell
dotnet restore backend/mood_recommendation.csproj
dotnet build tests/Moodify.Integration.csproj
npm ci --prefix admin-web
npm ci --prefix scripts
cd mobile-app
flutter pub get
cd ..
if (-not (Test-Path backend/appsettings.Development.json)) {
    Copy-Item backend/appsettings.Development.example.json backend/appsettings.Development.json
}
```

Điền `Tmdb:AccessToken`, `Spotify:ClientId`, `Spotify:ClientSecret` trong file Development thật, đã gitignore. Có thể dùng biến môi trường `Tmdb__AccessToken`, `Spotify__ClientId`, `Spotify__ClientSecret`, `Mongo__ConnectionString`, `Mongo__Database`, `Jwt__Key`. Không đưa credential vào Flutter/JavaScript trình duyệt.

Development không có JWT key sẽ sinh key tạm; restart khiến phiên cũ hết hiệu lực. Ngoài Development cần JWT key tối thiểu 32 byte. Đây là hướng dẫn local, chưa phải cấu hình production.

## 2. Database và tài khoản

Backend tự tạo collection/index/validator khi database trống, **không tự tạo Admin hoặc thư viện demo**.

### Khôi phục bản `database_backup` bằng mongorestore

Bản backup được cấp riêng có cấu trúc `database_backup/mood_recommendation/*.bson` và `*.metadata.json`. Cài **MongoDB Community Server** và **MongoDB Database Tools**, bật MongoDB tại `127.0.0.1:27017`; khôi phục trước khi chạy backend. Database đích `mood_recommendation` phải chưa có dữ liệu. Từ root project, sau khi giải nén đúng thư mục:

```powershell
mongorestore --uri="mongodb://127.0.0.1:27017" --nsInclude="mood_recommendation.*" --stopOnError ".\database_backup"
```

Đường dẫn cuối là thư mục **cha chứa `mood_recommendation`**. Kiểm tra lệnh kết thúc thành công, có số bản ghi được restore và `0 document(s) failed to restore`; sau đó mở MongoDB Compass xem các collection. Đặt `Mongo:Database` thành `mood_recommendation` trong cấu hình backend. Không tự thêm `--drop` hoặc chạy lặp trên DB đã có dữ liệu.

Xem [hướng dẫn khôi phục đầy đủ](docs/MONGODB_RESTORE.md) cho cách cài tools, chọn đường dẫn khác, đổi tên DB đích, đăng nhập và chạy app. Backup không chứa model AI hoặc khóa TMDB/Spotify; vẫn cần cấu hình riêng theo các mục bên dưới. Cú pháp dùng theo [tài liệu mongorestore chính thức](https://www.mongodb.com/docs/database-tools/mongorestore/).

### Nếu nhận bản JSON do script của project xuất

Chỉ dùng các lệnh dưới khi bản backup có `manifest.json` và các file collection JSON do `mongo-backup.mjs` tạo. **Không dùng chúng cho folder `.bson` ở trên.** Import vào database mới, trống, rồi cấu hình `Mongo:Database` trỏ đến đó:

```powershell
node scripts/mongo-backup.mjs import --from 'D:/duong-dan/backup' --database moodify_restored
node scripts/mongo-backup.mjs import --from 'D:/duong-dan/backup' --database moodify_restored --apply
```

Lệnh đầu kiểm tra, lệnh sau áp dụng. Importer từ chối DB có collection. Backup chứa tài khoản/password hash/lịch sử, không đưa lên GitHub. Không chạy hàng loạt migration/seed như lệnh cài mới.

Nếu tạo dữ liệu mới:

1. Chạy backend, đăng ký ở `http://localhost:5170`.
2. Chủ database mở MongoDB Compass → đúng database → `accounts`, tìm đúng tài khoản vừa đăng ký và đổi `role` thành `Admin`. Không sửa password hash. Đăng nhập lại.
3. Admin vào Users cấp `AIEngineer` cho tài khoản kỹ sư đã đăng ký.
4. Admin tạo mood/vùng VA, thể loại và thêm phim/nhạc, rà soát nhãn trước khi gợi ý.
5. Kỹ sư thiết lập mapping phù hợp mood thực tế. Mapping 13/28 trong tài liệu là snapshot local 09/10/2026, không tự áp dụng khi clone repo.

Repo không phân phối mật khẩu thật hoặc Admin mặc định.

## 3. Chạy backend và dashboard

Runner Windows, sau khi MongoDB/dependency sẵn sàng:

```powershell
./Start.ps1 -WithoutAI
./scripts/moodify-local.ps1 -Action Status
./Stop.ps1
```

Backend: `http://127.0.0.1:5170`; dashboard: `http://127.0.0.1:5174`; log: `%LOCALAPPDATA%/Moodify/logs`. Runner kiểm tra DB chỉ đọc, không chiếm cổng tiến trình khác và không tự chạy cùng Windows. Stop không dừng MongoDB.

Hoặc chạy thủ công trong ba terminal PowerShell riêng. Các ví dụ dưới đây bắt đầu ở thư mục gốc project; `Ctrl+C` dừng dịch vụ trong terminal tương ứng.

Terminal API (bật kết nối tới AI local):

```powershell
$env:AI__Enabled = "true"
$env:AI__ServiceUrl = "http://127.0.0.1:8001/"
dotnet run --project backend/mood_recommendation.csproj --launch-profile http
```

Terminal Admin:

```powershell
npm run dev --prefix admin-web
```

Terminal AI — candidate tiếng Anh 6 lớp đã được cài trong output và virtualenv:

```powershell
cd ai-service
$env:VA_MODEL_ENABLED = "true"
$env:VA_MODELS_ROOT = Join-Path (Split-Path -Parent (Get-Location).Path) "code-train/train-KLCN/output"
$env:VA_MODEL_VERSION = "roberta_base_moodify_6c_20261101_051915"
$env:VA_ACTIVE_MODEL_FILE = Join-Path $env:LOCALAPPDATA "Moodify/va-active-model.txt"
$env:VI_MODELS_ROOT = Join-Path (Split-Path -Parent (Get-Location).Path) "code-train/train-KLCN/output/vietnamese-emotion"
$env:VI_ACTIVE_MODEL_FILE = Join-Path $env:LOCALAPPDATA "Moodify/vi-active-model.txt"
.\.venv\Scripts\python.exe -m uvicorn main:app --host 127.0.0.1 --port 8001 --workers 1
```

Model Anh mới là classifier sáu lớp; `/predict-va` được giữ để tương thích API, V/A tính từ anchors. PhoBERT candidate được chọn trong AIEngineer → Vietnamese PhoBERT và lưu marker `vi-active-model.txt`; chưa có confidence threshold nên `accepted` là null. `pyvi` và `sentencepiece` phải nằm trong đúng virtualenv.

Readiness English: `http://127.0.0.1:8001/health/va/ready`; Vietnamese: `/health/vi/ready` sau khi candidate được activate. Candidate và runtime-active là hai trạng thái riêng.

Sau khi pull/merge, runner build backend trước khi nạp AI để báo lỗi C# ngay. Thiếu RAM theo ngưỡng khuyến nghị được báo cảnh báo; runner vẫn kiểm tra hash, VRAM và readiness của model.

Không chạy runner và dotnet run cùng cổng. Swagger Development: `http://localhost:5170/swagger`. Đăng nhập tại `http://localhost:5170`: Admin/AIEngineer vào dashboard đúng vai trò, nhận phiên một lần không đăng nhập lại. Giữ nhất quán localhost hoặc 127.0.0.1 trong luồng chuyển trang.

## 4. Chạy Flutter

Trên Windows, đặt `PUB_CACHE` cố định nếu muốn cache nằm cùng thư mục cha của project. Ví dụ project ở `D:\hoc-tap\moodify-project` thì cache dùng `D:\hoc-tap\moodify-pub-cache`. Chạy một lần tại root project:

```powershell
$pubCacheDir = Join-Path (Split-Path -Parent (Get-Location).Path) "moodify-pub-cache"
[Environment]::SetEnvironmentVariable("PUB_CACHE", $pubCacheDir, "User")
$env:PUB_CACHE = $pubCacheDir
```

Đóng và mở lại Android Studio/VS Code sau khi đổi biến môi trường. Những lần `flutter pub get`, `flutter run` và build tiếp theo sẽ dùng cache này. Cache chỉ chứa dependency tải được lại, không đưa lên Git. Không dùng `pub upgrade` chỉ để chuyển thư mục cache.

```powershell
cd mobile-app
flutter devices
flutter run -d <device-id>
# Hoặc web:
flutter run -d chrome --web-port 8080
```

Thay `<device-id>` bằng ID từ flutter devices. Android emulator mặc định gọi `http://10.0.2.2:5170`; Web/Windows gọi `http://localhost:5170`.

Điện thoại thật: cùng mạng với máy backend, firewall cho phép cổng 5170; dừng stack trước khi thay ListenAddress:

```powershell
# Tại root:
./Start.ps1 -WithoutAI -ListenAddress 0.0.0.0
# Tại mobile-app, thay IP đúng của máy backend:
flutter run -d <device-id> --dart-define=API_BASE_URL=http://192.168.1.100:5170
```

## 5. Bật AI

Hai classifier mới được cung cấp trong gitignored `code-train/train-KLCN/output/`: `roberta_base_moodify_6c_20261101_051915` và `vietnamese-emotion/phobert_moodify_6c_20261101_053022`. Candidate Anh được local runner chọn mặc định; candidate Việt cần activate trong dashboard. Model 28 lớp GoEmotions và checkpoint VA cũ vẫn là các luồng legacy riêng. Xem [artifact](code-train/README.md), [Python environment](ai-service/README.md), [test cases](docs/AI_MODEL_TEST_CASES.md) và [luồng AI/dataset](moodify-ai-flow-and-dataset-guide.md).

1. Cài `ai-service/.venv`, rồi chạy `./Start.ps1` không có `-WithoutAI`. English candidate 6 lớp được nạp theo version mặc định, trừ khi marker active chọn model khác. Vietnamese candidate cần activate trong dashboard AIEngineer → Vietnamese PhoBERT.
2. Đợi model tương ứng ready trên cổng 8001. Web người dùng chọn English/tiếng Việt; Mobile tự đoán language bằng luật nhẹ. Hai model trả emotion/confidence/candidates và V/A theo anchors. Model chưa có threshold nên `accepted=null`; người dùng cần kiểm tra mood trước khi nhận gợi ý.
3. Classifier 28 nhãn và Emotion Mapping 28 nhãn vẫn là luồng legacy độc lập; không thay mapping DB sang 6 nhãn.
4. Nếu dùng classifier 28 nhãn cũ, đăng nhập Admin/AIEngineer → Models → Validate → Activate. Emotion mapping chỉ áp dụng cho classifier legacy.
5. Datasets → Training jobs tạo bundle/notebook để train bên ngoài rồi nhập báo cáo; không tự deploy model mới.

Nếu chạy service thủ công, đặt `VA_MODEL_ENABLED=true`, `VA_MODELS_ROOT` trỏ `../code-train/train-KLCN/output`, `VA_MODEL_VERSION=roberta_base_moodify_6c_20261101_051915`; đặt `VI_MODELS_ROOT` tới `../code-train/train-KLCN/output/vietnamese-emotion` và dùng marker version sau khi activate PhoBERT. Khởi động Uvicorn trên loopback port 8001. Backend cần `AI__Enabled=true` và `AI__ServiceUrl=http://127.0.0.1:8001/`. Nếu AI chưa sẵn sàng, mood thủ công vẫn hoạt động.

## Cách sử dụng

**User:** Trang chủ chọn emoji → gợi ý → chi tiết nghe/xem/mở dịch vụ gốc → đánh giá. Khám phá dùng mô tả hoặc chọn mood; Thư viện nằm ở tab giữa; Lịch sử xem lại hoạt động.

**Admin:** quản lý catalog/VA; Users → Activity tra cứu tương tác/gợi ý/đánh giá; Feedback xem tên/ảnh nội dung và nhận xét; Models giám sát model.

**AIEngineer:** model/mapping/dataset/job/báo cáo/phân tích phản hồi; không CRUD tài khoản hay catalog. Recommendations trên dashboard ghi lịch sử tài khoản đang đăng nhập, không phải sandbox.

Spotify/YouTube quyết định quyền nhúng và thời lượng phát; không đảm bảo nghe toàn bài trong embed. Mở player/link không chứng minh đã xem/nghe xong.

## Backup và kiểm thử

```powershell
node scripts/mongo-backup.mjs export --out .local-backups/my-backup
node scripts/check-backup-roundtrip.mjs .local-backups/my-backup
```

Roundtrip restore vào DB tạm rồi xóa DB tạm; kiểm tra BSON/schema/index và bảo vệ DB có dữ liệu. Export tuần tự không phải snapshot giao dịch toàn DB; nên ngừng thao tác ghi trong lúc backup.

```powershell
dotnet build tests/Moodify.Integration.csproj
dotnet run --project tests/Moodify.Integration.csproj --no-build
npm run lint --prefix admin-web
npm run build --prefix admin-web
cd mobile-app
flutter analyze
flutter test
```

MongoDB phải chạy; các test AI cần artifact/config riêng. Backend tests mặc định dùng DB tạm. Smoke/fixture tests không chứng minh độ chính xác AI.

## Lỗi thường gặp và giới hạn

- Cổng 5170/5174 bị chiếm: kiểm tra Status và Stop runner trước khi dotnet run.
- Runtime unavailable: kiểm tra Python, weights/hash, AI.Enabled, ServiceUrl; registry active không đồng nghĩa runtime đang chạy.
- Unknown/unmapped: nhãn chưa vượt ngưỡng hoặc chưa có mood; chọn tay.
- Danh mục trống: import bản backup riêng hoặc thêm nội dung bằng Admin.
- Emulator không kết nối: kiểm tra backend và API 10.0.2.2:5170.

Chưa hoàn tất đánh giá NLP/recommender độc lập, đo thời lượng nghe/xem thực, RL và chứng minh rollback sang artifact thứ hai. Kiểm tra quyền phân phối trước khi công bố dataset/artifact bên thứ ba; repo không tự cấp giấy phép cho chúng.

## Tài liệu

- [Báo cáo tuần 7–8](docs/BAO_CAO_TUAN_7_8.md).
- [Đối chiếu task và phân quyền](docs/WEEK_7_8_ROLE_REVIEW.md).
- [Emotion mapping](docs/EMOTION_MAPPING_RESEARCH.md).
- [AI Engineer](docs/AI_ENGINEER_IMPLEMENTATION.md).
- [Recommender](docs/RECOMMENDATION_FEATURES_2026-10-07.md).
- [File giữ lại và chuyển riêng](docs/REPOSITORY_CLEANUP.md).

README là điểm bắt đầu hiện tại; tài liệu theo ngày có thể phản ánh trạng thái cũ.

### Phân tích AI song ngữ trên Mobile

Mobile gọi `POST /api/MoodAnalysis/valence-arousal`: `language=vi` dùng PhoBERT 6 lớp đang active; `language=en` dùng RoBERTa-base 6 lớp. Hai model trả nhãn/candidates và V/A từ anchors; khi nhận gợi ý, app gửi V/A và `modelVersion` với `source=ai_va`, backend lưu version trong history. Chưa có confidence threshold nên `accepted=null`; app yêu cầu xem và xác nhận mood.

Nhận diện ngôn ngữ là quy tắc nhẹ chạy trên máy, không tải thêm model lên điện thoại. Mobile chỉ dùng tự nhận diện và hiện model dưới ô nhập. Với câu không dấu, quá ngắn hoặc pha ngôn ngữ, nhận diện có thể chưa chính xác; nên diễn đạt rõ bằng một ngôn ngữ. Xem [20 ca thử và cách xác định model](docs/AI_MODEL_TEST_CASES.md). Trọng số model vẫn chạy ở Python service, không đóng gói trong APK.


### Các phần Mobile bổ sung ngày 25/10/2026

- Thống kê: chọn **Theo năm**, biểu đồ theo tháng và múi giờ thiết bị.
- Phim: diễn viên, năm phát hành, thời lượng từ TMDB và popup Admin; phim cũ thiếu dữ liệu được bổ sung khi mở chi tiết nếu có ID nguồn.
- Khám phá: nút xu hướng → **Nội dung nổi bật** → Được quan tâm trong Moodify / Vừa cập nhật, lọc phim/nhạc.

Xem [đối chiếu đề cương, cách dùng và phạm vi kiểm thử](docs/MOBILE_FEATURE_COMPLETION_2026-10-25.md). Quên mật khẩu, đăng nhập Google và push Android đã được bổ sung ở lượt tiếp theo; cấu hình dịch vụ theo mục dưới.

### Quên mật khẩu, Google và thông báo Android — 26/10/2026

- Màn đăng nhập có **Quên mật khẩu**: nhận mã email và đặt mật khẩu mới. Gmail SMTP dùng app password.
- **Tiếp tục với Google** xác minh bằng Firebase Auth ở backend. Email đã có tài khoản cần mật khẩu Moodify để liên kết.
- **Cá nhân → Thông báo**: chủ động bật nhận push, nhắc tâm trạng hằng ngày/nhìn lại tuần, chọn giờ và gửi thử. Backend phải chạy để gửi theo lịch.

Cấp riêng `mobile-app/android/app/google-services.json` và `backend/firebase-adminsdk.json` cùng Firebase project, bật Google provider/thêm chứng chỉ ký Android, rồi tại root:

```powershell
# Đồng bộ Firebase và giữ các cấu hình local hiện có:
.\scripts\configure-mobile-services.ps1
# Máy mới: cấu hình Gmail, app password được nhập ẩn khi script hỏi:
.\scripts\configure-mobile-services.ps1 -GmailAddress 'your-address@gmail.com'
```

Script giữ các section TMDB/Spotify/AI và sao lưu cấu hình cũ trước khi ghi. Credential nằm trong file local đã gitignore; không đưa service account hoặc app password lên Git. Khởi động lại backend, Stop rồi Run app hoàn toàn trong Android Studio để nhận native plugin mới.

Xem [hướng dẫn cấu hình, cách thử và giới hạn](docs/AUTH_NOTIFICATIONS_SETUP.md). Android tự đọc `google-services.json`; Web Google cần cấu hình Firebase Web riêng, chưa có Web/iOS push. Kiểm thử hiện tại: 67 Flutter tests, 612 API/nghiệp vụ assertions; xác thực SMTP/Firebase thật thành công nhưng chưa xác minh email/push tới thiết bị hoặc chọn Google account thật. APK debug đã build thành công.
