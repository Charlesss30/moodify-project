# Moodify — Gợi ý phim và âm nhạc theo tâm trạng

Hệ thống gồm Flutter, website Admin/AIEngineer, ASP.NET Core API và Python inference RoBERTa. Dữ liệu nghiệp vụ dùng **MongoDB**, phim tích hợp **TMDB**, nhạc tích hợp **Spotify**. Repo không chứa database cá nhân, file phim/nhạc hay trọng số model.

## Chức năng

- User: đăng ký/đăng nhập; quiz emoji; khám phá mô tả tiếng Anh hoặc chọn mood; thư viện; chi tiết phim/nhạc, trailer YouTube, Spotify player và liên kết gốc; lịch sử tương tác; đánh giá 1–5 sao, nhận xét trong chi tiết.
- Admin: nội dung, thể loại, người dùng, mood/vùng VA; Feedback; Users → Activity; Recommendations và quản lý vòng đời model.
- AIEngineer: AI overview, Models, Emotion mapping, Evaluation, Datasets, Training jobs, Feedback analytics, Inference lab, Recommendations.
- Recommender: Mood Matching + Content-based + Collaborative Filtering; phản hồi mới cập nhật hồ sơ và thứ hạng ở yêu cầu tiếp theo.

AI hiện hỗ trợ **tiếng Anh**, cần người dùng xác nhận mood và có fallback thủ công. Mapping còn thử nghiệm. Training jobs chuẩn bị notebook Colab/nhận báo cáo, **không tự train GPU trên web**. Feedback Loop không phải Reinforcement Learning.

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
Copy-Item backend/appsettings.Development.example.json backend/appsettings.Development.json
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

Hoặc chạy thủ công, mỗi lệnh ở một terminal, `Ctrl+C` để dừng:

```powershell
dotnet run --project backend/mood_recommendation.csproj --launch-profile http
npm run dev --prefix admin-web
```

Không chạy runner và dotnet run cùng cổng. Swagger Development: `http://localhost:5170/swagger`. Đăng nhập tại `http://localhost:5170`: Admin/AIEngineer vào dashboard đúng vai trò, nhận phiên một lần không đăng nhập lại. Giữ nhất quán localhost hoặc 127.0.0.1 trong luồng chuyển trang.

## 4. Chạy Flutter

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

Khôi phục artifact được cấp riêng tại `code-train/train-KLCN/output/roberta_large_asl_en_01/`, gồm `best_model`, `deployment`, `run_config.json`. Không chỉ chép model.safetensors. Model VA EmoBank dùng riêng file `code-train/train-KLCN/output/roberta_va_regression/roberta_va_regression.pt` và thư mục `tokenizer`; runner nhận diện model này và cung cấp endpoint riêng. Xem [artifact](code-train/README.md) và [Python environment](ai-service/README.md).

1. Cài `ai-service/.venv`, đặt artifact cần dùng đúng thư mục, chạy `./Start.ps1` không có WithoutAI. VA có thể chạy riêng khi chưa có artifact phân loại cũ.
2. Đợi runner báo `Valence/arousal inference ready` trên cổng 8001. Website người dùng → Home → nhập mô tả tiếng Anh → Analyze Mood → xem tọa độ VA/mood → Show recommendations. Registry model cảm xúc 28 nhãn không quản lý model VA.
3. Nếu dùng classifier cũ, đăng nhập Admin/AIEngineer → Models → Validate → Activate. Runtime ready và registry active là hai điều kiện riêng; runner không tự Activate. Emotion mapping chỉ áp dụng cho classifier 28 nhãn.
4. Datasets → Training jobs: tải bundle/notebook, chạy Colab bên ngoài rồi nhập báo cáo. Tạo job không tự triển khai model mới.

Nếu chạy từng terminal chỉ với VA, tạo `ai-service/.venv`, cài `ai-service/requirements.txt`, rồi trong thư mục `ai-service` đặt `VA_MODEL_ENABLED=true`, `VA_MODEL_DIR` trỏ tới `../code-train/train-KLCN/output/roberta_va_regression`, `VA_CHECKPOINT` trỏ tới file `.pt`, `VA_TOKENIZER_DIR` trỏ tới thư mục `tokenizer`, và chạy `python -m uvicorn main:app --host 127.0.0.1 --port 8001 --workers 1`. Backend cần `AI__Enabled=true` và `AI__ServiceUrl=http://127.0.0.1:8001/`. Nếu dùng runner `Start.ps1`, các biến này được đặt tự động. Khi AI không sẵn sàng, các luồng chọn mood thủ công vẫn hoạt động.

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
