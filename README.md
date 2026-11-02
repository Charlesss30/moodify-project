# Moodify — Gợi ý phim và âm nhạc theo tâm trạng

Hệ thống gồm Flutter, website Admin/AIEngineer, ASP.NET Core API và Python inference RoBERTa. Dữ liệu nghiệp vụ dùng **MongoDB**, phim tích hợp **TMDB**, nhạc tích hợp **Spotify**. Repo không chứa database cá nhân hoặc file phim/nhạc. Hai bộ weights 6 lớp được phân phối bằng Git LFS; model cũ và dữ liệu huấn luyện cấp riêng.

## Chức năng

- User: đăng ký/đăng nhập email hoặc Google; quên mật khẩu qua email; cài đặt thông báo đẩy Android; quiz emoji; khám phá mô tả tiếng Việt/tiếng Anh hoặc chọn mood; thư viện; nội dung được quan tâm/vừa cập nhật; thống kê theo ngày/năm; chi tiết phim/nhạc, trailer YouTube, Spotify player và liên kết gốc; lịch sử tương tác; đánh giá 1–5 sao, nhận xét trong chi tiết.
- Admin: nội dung, thể loại, người dùng, mood/vùng VA; Feedback; Users → Activity; Recommendations và quản lý vòng đời model.
- AIEngineer: AI overview, Models, Emotion mapping, Evaluation, Datasets, Training jobs, Feedback analytics, Inference lab, Recommendations.
- Recommender: Mood Matching + Content-based + Collaborative Filtering; phản hồi mới cập nhật hồ sơ và thứ hạng ở yêu cầu tiếp theo.

Phân tích mô tả hỗ trợ **tiếng Việt qua PhoBERT** và **tiếng Anh qua RoBERTa (hồi quy VA hoặc classifier 6 lớp)** khi artifact tương ứng đã được cài, kích hoạt và runtime sẵn sàng. Mobile tự nhận diện ngôn ngữ và hiển thị model dưới ô nhập; Web chọn ngôn ngữ trên form. Người dùng xác nhận mood trước khi lấy gợi ý và có thể chọn mood thủ công. Mapping còn thử nghiệm. Training jobs chuẩn bị notebook Colab/nhận báo cáo, **không tự train GPU trên web**. Feedback Loop không phải Reinforcement Learning.

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

Bản backup nhận kèm project hoặc được cấp riêng có cấu trúc `database_backup/mood_recommendation/*.bson` và `*.metadata.json`. Cài **MongoDB Community Server** và **MongoDB Database Tools**, bật MongoDB tại `127.0.0.1:27017`; khôi phục trước khi chạy backend. Database đích `mood_recommendation` phải chưa có dữ liệu. Từ root project, sau khi giải nén đúng thư mục:

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

Terminal AI — ví dụ model tiếng Anh `roberta_large_asl_en_01` đã có artifact và virtualenv:

```powershell
cd ai-service
$env:AI_MODEL_ENABLED = "true"
$env:MODEL_SHA256 = "EA865317D9EB7CF1B39008062276BAE441801A7D719D33177A3DDD1851AF7B09"
$env:VI_MODELS_ROOT = Join-Path (Split-Path -Parent (Get-Location).Path) "code-train/train-KLCN/output/vietnamese-emotion"
$env:VI_ACTIVE_MODEL_FILE = Join-Path $env:LOCALAPPDATA "Moodify/vi-active-model.txt"
.\.venv\Scripts\python.exe -m uvicorn main:app --host 127.0.0.1 --port 8001 --workers 1
```

Nếu đã cài checkpoint VA chuẩn ở `output/roberta_va_regression`, thêm `$env:VA_MODEL_ENABLED = "true"` trước lệnh Uvicorn để nạp cả VA khi khởi động. Model PhoBERT được chọn trong dashboard được lưu tại marker `vi-active-model.txt`; các package `pyvi` và `sentencepiece` phải được cài trong đúng virtualenv qua `pip install -r requirements.txt`.

Readiness của model tiếng Anh: `http://127.0.0.1:8001/health/ready`. Model VA cũ và PhoBERT 7 lớp cần artifact riêng; hai classifier 6 lớp cần tải weights bằng Git LFS. Các lệnh VA nằm ở mục AI bên dưới. Model registry đã activate và runtime sẵn sàng là hai trạng thái riêng.

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

Sau khi pull bản tích hợp Phuong/Khang, chạy `git lfs install` và `git lfs pull` để tải hai weights 6 lớp. Metadata/tokenizer đã nằm trong Git thường. Runner giữ nguyên model active; candidate mới cần đánh giá độc lập và duyệt trước khi kích hoạt. Xem [kết quả tích hợp và hướng dẫn](docs/KHANG_PHUONG_INTEGRATION_2026-11-03.md). Có thể dùng `./Start.ps1 -WithoutLegacyClassifier` để không nạp thêm classifier GoEmotions 28 nhãn, giảm bộ nhớ.


Khôi phục artifact được cấp riêng tại `code-train/train-KLCN/output/roberta_large_asl_en_01/`, gồm `best_model`, `deployment`, `run_config.json`. Không chỉ chép model.safetensors. Model VA EmoBank dùng riêng file `code-train/train-KLCN/output/roberta_va_regression/roberta_va_regression.pt` và thư mục `tokenizer`; runner nhận diện model này và cung cấp endpoint riêng. Xem [artifact](code-train/README.md) và [Python environment](ai-service/README.md).

1. Cài `ai-service/.venv`, đặt artifact cần dùng đúng thư mục, chạy `./Start.ps1` không có WithoutAI. VA có thể chạy riêng khi chưa có artifact phân loại cũ.
2. Đợi runner báo model tương ứng đã sẵn sàng trên cổng 8001. Website người dùng → Home → AI Mood Analysis → nhập mô tả Việt/Anh (backend tự nhận diện) → phân tích → xem mood → nhận gợi ý. Trên Mobile: Khám phá → nhập mô tả (mặc định tự nhận diện Việt/Anh) → Phân tích tâm trạng → Xác nhận và nhận gợi ý. Kết quả Mobile hiển thị model và phiên bản đã xử lý; không còn gọi classifier 28 nhãn cũ. Registry classifier 28 nhãn vẫn là luồng riêng.
3. Nếu dùng classifier cũ, đăng nhập Admin/AIEngineer → Models → Validate → Activate. Runtime ready và registry active là hai điều kiện riêng; runner không tự Activate. Emotion mapping chỉ áp dụng cho classifier 28 nhãn.
4. Datasets → Training jobs: tải bundle/notebook, chạy Colab bên ngoài rồi nhập báo cáo. Tạo job không tự triển khai model mới.

Nếu chạy từng terminal chỉ với VA, tạo `ai-service/.venv`, cài `ai-service/requirements.txt`, rồi trong thư mục `ai-service` đặt `VA_MODEL_ENABLED=true`, `VA_MODEL_DIR` trỏ tới `../code-train/train-KLCN/output/roberta_va_regression`, `VA_CHECKPOINT` trỏ tới file `.pt`, `VA_TOKENIZER_DIR` trỏ tới thư mục `tokenizer`, và chạy `python -m uvicorn main:app --host 127.0.0.1 --port 8001 --workers 1`. Backend cần `AI__Enabled=true` và `AI__ServiceUrl=http://127.0.0.1:8001/`. Nếu dùng runner `Start.ps1`, các biến này được đặt tự động. Khi AI không sẵn sàng, các luồng chọn mood thủ công vẫn hoạt động.

### Hợp đồng AI song ngữ hiện tại — 27/10/2026

Mobile và Web gửi `language=auto` tới `POST /api/MoodAnalysis/valence-arousal`. Backend chọn PhoBERT hoặc RoBERTa VA; response ghi `language`, `modelType`, `modelVersion`, `mappingVersion` và trạng thái. Kết quả đủ điều kiện có `analysisId` gắn tài khoản, hiệu lực 5 phút. Khi xác nhận, client gửi mã này với mood và bộ lọc; V/A và phiên bản model được lấy từ kết quả đã lưu ở server. Đổi model/vùng mood hoặc mã hết hạn thì cần phân tích lại. Kết quả dưới ngưỡng không tạo mã xác nhận AI; vẫn chọn mood thủ công được.

AI overview và Inference lab của kỹ sư đã phản ánh hai pipeline này. **GoEmotions mapping** chỉ tác động classifier 28 nhãn cũ. Datasets/Training jobs có schema riêng cho GoEmotions 28 nhãn, VSMEC 7 lớp, hồi quy VA, classifier 6 lớp EN/VI và MF. Job chuẩn bị bundle để train/evaluate bên ngoài; không tự chạy huấn luyện hoặc triển khai khi tạo job.

Xem [kết quả thực hiện và bằng chứng](docs/AI_SYSTEM_UNIFICATION_RESULTS.md), [checklist còn lại](docs/AI_SYSTEM_UNIFICATION_PLAN.md). Cả backend và Mobile cần được cập nhật cùng phiên bản hợp đồng này; client cũ gửi V/A với `source=ai_va` sẽ nhận HTTP 400 yêu cầu dùng `analysisId`.

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
- Sau khi Windows khởi động lại, runner bỏ bản ghi tiến trình đã hết hạn và kiểm tra lệnh/thời điểm tạo tiến trình để nhận diện PID được dùng lại. Có thể chạy Start lại bình thường; kiểm thử runner riêng: `powershell -NoProfile -File tests/LocalRunner.Tests.ps1`.
- Runtime unavailable: kiểm tra Python, weights/hash, AI.Enabled, ServiceUrl; registry active không đồng nghĩa runtime đang chạy.
- Unknown/unmapped: nhãn chưa vượt ngưỡng hoặc chưa có mood; chọn tay.
- Danh mục trống: import bản backup riêng hoặc thêm nội dung bằng Admin.
- Emulator không kết nối: kiểm tra backend và API 10.0.2.2:5170.

Chưa hoàn tất đánh giá chất lượng NLP/recommender độc lập hoặc đo thời lượng nghe/xem thực. Rollback giữa hai artifact khác weights đã kiểm chứng trên môi trường tạm. RL/bandit không bắt buộc trong phạm vi hiện tại, pilot tiếp tục tắt. Kiểm tra quyền phân phối trước khi công bố dataset/artifact bên thứ ba; repo không tự cấp giấy phép cho chúng.

## Tài liệu

- [Báo cáo tuần 09](docs/BAO_CAO_TUAN_09.md).
- [AI Engineer](docs/AI_ENGINEER_IMPLEMENTATION.md).
- [Recommender, Hybrid và kiểm chứng AI](docs/AI_SYSTEM_UNIFICATION_RESULTS.md).

README là điểm bắt đầu hiện tại; tài liệu theo ngày có thể phản ánh trạng thái cũ.

### Phân tích AI song ngữ trên Mobile

Mobile/Web gọi `POST /api/MoodAnalysis/valence-arousal` với `language=auto`: backend chọn PhoBERT VI hoặc RoBERTa VA EN đang active. Khi xác nhận gợi ý, app gửi `analysisId` gắn tài khoản/hạn dùng với `source=ai_va`; backend lấy V/A và model/policy từ receipt đã lưu. Client không gửi V/A/modelVersion để giả kết quả. PhoBERT dưới ngưỡng không tự chọn mood; người dùng có thể nhập rõ hơn hoặc chọn thủ công.

Định tuyến chính là quy tắc có phiên bản ở backend; dòng gợi ý trước khi phân tích trên Mobile chỉ là dự kiến. Không tải weights lên điện thoại. Mobile chỉ dùng tự nhận diện và hiện model dưới ô nhập. Với câu không dấu, quá ngắn hoặc pha ngôn ngữ, nhận diện có thể chưa chính xác; nên diễn đạt rõ bằng một ngôn ngữ. Xem [20 ca thử và cách xác định model](docs/AI_MODEL_TEST_CASES.md). Trọng số model vẫn chạy ở Python service, không đóng gói trong APK.


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


### AI Engineer và Recommender — cập nhật 27/10/2026

- **AI overview/Inference lab:** hai pipeline thật, policy và metrics theo phiên bản; GoEmotions vẫn riêng.
- **GoEmotions mapping:** phần Bilingual VA policies quản lý các mood ID được dùng và gate VI bổ sung, không sửa neo để ép kết quả.
- **Datasets / Training jobs / Evaluation:** schema riêng VSMEC7, VA regression, explicit-rating MF; job tải bundle, chạy runner offline/local/Colab rồi nhập report. Chuẩn bị job không tự train hoặc Activate.
- **VI/EN Models:** upload chỉ lưu candidate; quality policy → evaluation-only → review approval → activate. Lifecycle có revision/fingerprint, rollback và kiểm tra identity sau restart; readiness không là quality.
- **Feedback analytics:** MF registry, baseline fallback và contextual bandit thử nghiệm mặc định tắt. Đây không phải RL nhiều bước.

MF/pilot chưa được bật trên database chính. Chưa có gold NLP mới đã review hoặc bằng chứng MF vượt baseline trên dữ liệu ứng dụng hiện có. Hai CSV preprocessing mới tạo được JSON ứng viên; chưa khôi phục từ điển gốc của lần train target80.

Xem [kết quả/checklist](docs/AI_SYSTEM_UNIFICATION_RESULTS.md), [cách vận hành AI Engineer](docs/AI_ENGINEER_IMPLEMENTATION.md), [audit preprocessing](docs/PHOBERT_PREPROCESSING_AUDIT.md) và [quy trình đánh giá](docs/AI_EVALUATION_PROTOCOL.md).

Android emulator đã pass E2E với cả PhoBERT VI/RoBERTa VA EN thật trên DB tạm, gồm nhận gợi ý/chi tiết/đánh giá/lịch sử. Các giới hạn hiệu năng debug và số đo được ghi trong báo cáo kết quả; chưa kết luận chất lượng NLP hoặc độ mượt trên điện thoại thật.

Để bắt đầu phần nhãn còn thiếu: mở `tools/ai-label-review.html`, nhập `docs/ai-evaluation/review-starter.json`, làm theo [hướng dẫn gán nhãn offline](docs/HUONG_DAN_GAN_NHAN_AI.md). Mẫu 48 câu chưa có gold; nhãn hoàn tất lưu riêng vào `docs/ai-evaluation/completed/` đã gitignore.


### Thông báo hệ thống Android — 28/10/2026

Foreground FCM hiện được hiển thị trong ngăn thông báo điện thoại, có channel ưu tiên cao và bấm mở đúng màn hình. Cần build/cài APK mới; hot reload không cập nhật Kotlin/Manifest. Lịch nhắc và lựa chọn nhận thông báo giữ nguyên. Xem [hướng dẫn và phạm vi kiểm thử](docs/ANDROID_SYSTEM_NOTIFICATIONS.md).
