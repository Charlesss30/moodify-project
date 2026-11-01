# Tổng hợp cập nhật kể từ commit 0e42360

Ngày tổng hợp: **27/10/2026**. Workspace: `D:\hoc-tap\moodify-project`. Nhánh hiện tại: **Phuong**.

Mốc đối chiếu là commit **`0e42360`**, nội dung **“Cap nhat giao dien mobile và add model AI vao mobile-app”**, lúc **22:55 ngày 26/10/2026, UTC+7**. Bản này tổng hợp diff so với `HEAD` và các file mới chưa theo dõi; không phải so riêng với `main` hoặc lần push gần nhất trên remote.

**Chưa commit hoặc push các thay đổi trong lượt này.** Có 40 file đã theo dõi thay đổi nội dung và 49 file mới trước khi tạo bản tổng hợp này. Ngoài ra Git báo 2 file generated Windows thay đổi nhưng không có diff nội dung. Danh sách đầy đủ nằm cuối tài liệu.

## 1. Mobile và Web người dùng

- Thống nhất API phân tích Việt/Anh: client gửi `language=auto`; backend quyết định model, trả chung cấu trúc kết quả.
- Mobile phân biệt các trạng thái matched, unknown và ngôn ngữ không hỗ trợ; lỗi runtime/AI tắt/bận có hướng chọn mood thủ công.
- Dòng dưới ô nhập thể hiện model dự kiến trước khi phân tích và ngôn ngữ/model thực tế sau phản hồi; không đưa lại bộ chọn ngôn ngữ cho người dùng.
- Kết quả thấp confidence không còn được hiểu là một mood AI đã chấp nhận.
- Khi nhận gợi ý AI, Mobile/Web gửi **`analysisId`**; bỏ gửi V/A/modelVersion do client tự khai để xác nhận kết quả.
- Chọn lại mood thủ công được ghi đúng nguồn quyết định, vẫn giữ thông tin phân tích gốc để truy nguyên.
- Siết kiểm tra response: ngôn ngữ, loại model/score, version, hạn dùng, ID, V/A hữu hạn và nhãn ứng viên VI.
- Web người dùng của backend dùng cùng hợp đồng và luồng tự nhận ngôn ngữ với Mobile.
- Thêm integration test Android với package `.smoke` riêng; cấu hình này không ghi đè application ID/Firebase của app chính.

Giao diện tối/tím, Trang chủ/Khám phá/Thư viện, hồ sơ/đổi mật khẩu, Google/quên mật khẩu, push, thống kê, Spotify/YouTube, rating, splash/icon đã có trong commit gốc. Đợt này giữ các chức năng đó, kiểm tra hồi quy và sửa phần tích hợp AI liên quan; không nhận là đã xây mới toàn bộ UI.

## 2. Backend: hợp đồng AI và xác nhận kết quả

- Thêm `BilingualMoodAnalysisService` và `MoodLanguageRouter` dùng chung.
- Routing có version: dấu Việt, từ không dấu, emoji, câu Latin và hệ chữ không hỗ trợ. Đây là heuristic, không cam kết nhận đúng mọi câu pha ngôn ngữ.
- VI dùng PhoBERT/VSMEC; EN dùng RoBERTa VA regression. Không âm thầm chuyển model ngôn ngữ khác khi runtime cần dùng lỗi.
- Áp dụng `AI:Enabled` cho cả hai nhánh; đồng bộ URL inference và timeout phân tích 15 giây.
- Receipt MongoDB `aiVaAnalyses` gắn user, TTL 5 phút, V/A/model/policy/ngôn ngữ/vùng mood do server giữ.
- Khi xác nhận, kiểm tra account sở hữu, hết hạn, acceptance, model active, policy và cấu hình mood. Dữ liệu V/A/version giả từ client bị chặn.
- Lease/idempotency bảo vệ gửi trùng, replay và restart; AI/manual override được phân biệt.
- Lịch sử bổ sung nguồn phân tích, quyết định, ngôn ngữ, loại model, routing, V/A gốc và phiên bản collaborative/experiment khi có.
- Bổ sung schema/index và đăng ký service cho receipt, policy, typed experiments, MF, quality approval và experiment.

Các thay đổi schema phục vụ metadata mới; không thay hàng loạt V/A phim/nhạc hoặc tạo lịch sử/rating giả trên database chính.

## 3. Dashboard AI Engineer và mapping

Các công cụ mới nằm trong tab phù hợp, giữ phân quyền hiện có.

| Khu vực | Cập nhật |
| --- | --- |
| AI overview | Hiện đúng hai pipeline Mobile/Web, model/policy, runtime và vùng mood DB |
| Inference lab | Gọi cùng endpoint song ngữ của ứng dụng; preview không tự tạo recommendation/history |
| GoEmotions mapping | Ghi rõ bảng 28 nhãn chỉ áp dụng classifier cũ |
| Bilingual VA policies | Candidate riêng VI/EN, chọn mood ID ổn định, siết confidence VI, reason/revision/Approve/reset/audit |
| Inference observations | Aggregate theo ngày/ngôn ngữ/model/policy/status: số lượt, accepted, mean/max latency |
| Datasets | Schema riêng GoEmotions cũ, VSMEC7, VA regression và rating MF; tách training/evaluation |
| Training jobs | Chuẩn bị bundle và cấu hình thực thi; không coi việc đổi trạng thái job là đang train |
| Evaluation | Nhập report gắn job/dataset/config/model/hash, validation/test metrics và manifest |
| VI/EN Models | Lifecycle riêng và công cụ quality policy/review approval |
| Feedback analytics | MF registry và experiment/bandit thử nghiệm, mặc định off |

Policy mapping mới có version, revision chống ghi đè khi người khác vừa cập nhật, kiểm tra mood ID tồn tại và invalidation receipt cũ. Nó điều khiển tập mood/ngưỡng; không là trình sửa điểm neo hoặc model tự học mapping.

Phân quyền API mới dành AIEngineer; User/Admin không tự dùng được chức năng đặc thù kỹ sư. Admin giữ quản lý người dùng/catalog và phần registry chung theo quyền hiện có.

## 4. Vòng đời model và quality gate

- VI/VA có validate loading, activate, deactivate, rollback riêng; lưu state atomic, revision, previous version và marker tương thích.
- Deactivate tồn tại qua restart; không tự nạp lại default đã tắt. Candidate load lỗi giữ model cũ.
- Đã thử rollback giữa **hai artifact khác weights**, không chỉ hai tên version trỏ cùng weights.
- **Upload giờ chỉ lưu candidate**. Route tên cũ vẫn tương thích nhưng không tự kích hoạt.
- Quality policy bất biến được chốt trước evaluation job; kiểm tra số mẫu và metric theo đúng task.
- Review approval phải khớp report/job/policy, fingerprint weights, tokenizer, config, preprocessing, threshold và code adapter.
- Các API Activate/rollback VI/VA qua quality gate; fingerprint được gửi tới Python và kiểm lại sau load.
- Sidecar pin identity được kiểm tra khi restart để phát hiện artifact thay đổi.
- Baseline đã load trước đó có trạng thái riêng phục vụ rollback vận hành; không được gắn nhãn “đạt đánh giá độc lập”.
- Server kiểm tra báo cáo do kỹ sư nộp; không tự chứng minh nguồn nhãn độc lập hoặc tính lại toàn bộ dự đoán.

Thay policy mapping vẫn có workflow review riêng; chưa có gate metric tự động tương đương gate model cho mọi lần đổi policy.

## 5. Training/evaluation thực thi offline

- Thêm `train_experiment.py` cho VSMEC7, VA regression và MF.
- Bundle có dataset/config/hash/split; kiểm tra ID, review, trùng văn bản/cross-split, loại nhãn và hyperparameter.
- NLP chạy optimizer thật, chọn checkpoint theo validation, xuất artifact/manifest/report. MF chạy huấn luyện riêng theo dữ liệu sao.
- Dataset evaluation-only chỉ validation/test; `evaluate_candidate.py` chạy candidate nguyên trạng, không optimizer hoặc đổi threshold.
- Báo metrics đúng task: macro-F1/coverage/selective accuracy cho VI, MAE/RMSE cho VA; evaluator bổ sung confusion matrix, tương quan, sai số theo vùng và mood match khi có nhãn.
- Có thể chạy explicit local/Colab với source và artifact riêng; dashboard không biến backend thành máy tự train nền.

Đã chạy gradient classifier VI trên fixture nhỏ và MF training thật từ bundle test. Chưa fine-tune production bằng bộ gold mới; không lấy fixture làm bằng chứng model production đã cải thiện.

## 6. Recommender: MF, Hybrid và feedback

- Bổ sung **biased Matrix Factorization** offline: user/item factors, bias, regularization, seed, split thời gian, chọn epoch bằng validation.
- Báo RMSE/MAE/mean baseline, ranking metrics và coverage theo dữ liệu có đủ điều kiện.
- MF registry có import/validate, stable account/catalog ID, gate heldout, activate/rollback/deactivate và cache theo version.
- Hybrid sử dụng score MF thật khi có; cold start/model chưa active quay về fallback hiện có.
- Giữ baseline Mood Matching + Content-based + user-neighbor CF; trọng số mặc định vẫn 55%/30%/15%, chuẩn hóa khi thiếu thành phần.
- Thêm `evaluate_hybrid.py`: chọn trọng số bằng validation, đo test/baseline/coverage/diversity/cold context; không tự ghi weights được chọn vào serving.
- Kiểm chứng feedback loop hiện hữu: sao/tương tác thay đổi hồ sơ/thứ hạng request sau, dislike mạnh hơn một lần mở link.
- Thêm contextual epsilon-greedy bandit thử nghiệm: mood context, arm trọng số, phân nhóm account ổn định, propensity/exposure/reward aggregate và tắt để về baseline.

**MF trên DB chính chưa active; bandit/pilot vẫn off.** DB snapshot chỉ có 7 rating, chưa đủ cơ sở chứng minh MF/Hybrid vượt baseline. Bandit không phải RL nhiều bước; theo chỉ đạo người dùng, RL không bắt buộc trong lượt hiện tại. Chưa có scheduler auto-retrain/auto-promote hoặc online A/B người dùng thật.

## 7. PhoBERT preprocessing, mapping và dữ liệu nhãn

- Đọc notebook, đối chiếu log train, thư mục model/archive và Drive được cung cấp; chưa tìm lại đúng `preprocessing.json` gốc.
- Tách normalizer dùng chung, thêm exporter và kiểm tra cấu trúc/version của cấu hình.
- Hai CSV anh cung cấp tạo được candidate với 332 emoji/335 khóa teencode dùng được, có provenance/checksum; không ghi đè CSV gốc.
- Kiểm tra 670 ca cho thấy normalizer khớp thuật toán notebook khi dùng cùng dictionary; không phải 670 nhãn cảm xúc đúng.
- Serving tiếp tục fallback đã công bố; candidate CSV chưa được deploy. Health/dashboard báo trạng thái preprocessing/hash.
- Ghi rõ mapping VI: xác suất 7 cảm xúc → trung bình điểm neo V/A → mood có tâm gần nhất; EN regression → mapper chung.
- Chứng minh giới hạn adapter VI với vùng V dương/A âm; không sửa tên Other thành Relaxed để che hạn chế.
- Tạo công cụ gán nhãn HTML offline, lưu nháp/xuất JSON; tách emotion, VA và mood.
- Tạo mẫu 48 câu chưa review/24 cặp song ngữ giữ cùng split; không gọi là gold.
- Tiếp nhận bản nhãn v2 anh ghi đè và chấp nhận; giữ nguồn AI, snapshot/checksum và file review riêng.

### Baseline bộ nhãn Phương

Chạy 48 câu qua đúng backend mapper + hai model đang phục vụ, mood/policy sao chép read-only và receipt DB tạm; không sửa DB nghiệp vụ để chạy bộ này.

- Routing đúng **48/48**.
- Test mỗi ngôn ngữ chỉ 12 câu: VI emotion khớp **6/12**, accepted **7/12**.
- Mood test khớp **VI 2/8**, **EN 1/8** trong các câu có mood tham chiếu, tính cả abstention.
- Có **10/34** hàng mà chính V/A tham chiếu đưa vào nearest-center cũng ra mood khác nhãn; cần rà soát định nghĩa mood/geometry ngoài việc xem weights.
- Đây là mức đồng thuận với nhãn AI được người dùng chấp nhận, chưa phải nghiệm thu gold độc lập; không tự train hoặc Activate từ 48 câu.

## 8. Kiểm thử, dữ liệu và vận hành

| Phạm vi | Bằng chứng mới nhất đã chạy |
| --- | --- |
| Build .NET | 0 warning/error |
| Backend HTTP/nghiệp vụ | 906 assertion, MongoDB tạm |
| Python | 34 tests pass |
| Flutter | Analyzer sạch, 75 unit/widget tests pass |
| Admin | Lint/build, browser smoke và role/quality workflow pass |
| Hai NLP thật | 196 assertion/26 mô tả trong harness, gồm Android E2E |
| Mobile/Web E2E | VI/EN → xác nhận → Hybrid → chi tiết → rating/comment → history |
| Lifecycle | VI/VA distinct-weight rollback và inactive restart |
| Công cụ nhãn | Browser import/validation/export/khôi phục pass |
| Bộ nhãn mới | 48 câu diagnostic, tách khỏi các assertion/test phần mềm |

Các số trên thuộc các loại kiểm tra khác nhau, không cộng thành số mẫu chất lượng NLP. AI E2E không chứng minh đã nghe Spotify/xem YouTube hết hoặc email/FCM đã tới thiết bị.

- Có backup MongoDB restore round-trip, 21 collection/456 bản ghi lúc đầu; kiểm chứng 35 artifact/config checksums giữ nguyên.
- Trước bật stack, dữ liệu 21 collection snapshot không đổi. Sau bật, 19/21 giữ nguyên; pushDevices/pushReceipts đổi theo scheduler hiện hữu do token FCM cũ unregistered và receipt ngày mới. Tài khoản/catalog/mood/lịch sử/rating giữ nguyên.
- Model/state chính giữ nguyên; các phép train/activate thử dùng fixture/marker/DB riêng. Nhãn hoàn tất và kết quả riêng lưu trong thư mục gitignored.
- Đã đo cold/warm inference, burst nhỏ, GPU/RAM server và debug Android. Frame debug còn giật; chưa kết luận 60 FPS, capacity production hoặc độ mượt trên điện thoại thật.
- Sửa runner: VRAM khuyến nghị 3500 MiB không còn chặn cứng trước load; vẫn yêu cầu model readiness/version/hash và cleanup khi lỗi.
- Sửa thông báo Start để phân biệt active state, runtime ready, quality review VI/VA và registry GoEmotions.
- Lượt khôi phục cuối: đủ ba model CUDA ready, backend 5170, Admin 5174; đã cài/mở lại APK chính API 10.0.2.2:5170 và kiểm tra login API chính thành công.

## 9. Tài liệu đã cập nhật/thêm

| Tài liệu | Nội dung |
| --- | --- |
| [README](../README.md) | Hướng dẫn theo hợp đồng/routing và workflow triển khai hiện hành |
| [Plan thống nhất AI](AI_SYSTEM_UNIFICATION_PLAN.md) | Checklist có bằng chứng, phần phụ thuộc dữ liệu còn mở |
| [Kết quả thống nhất](AI_SYSTEM_UNIFICATION_RESULTS.md) | Triển khai, test, số đo, vận hành và giới hạn theo từng đợt |
| [AI Engineer](AI_ENGINEER_IMPLEMENTATION.md) | Policy, typed dataset/job/evaluation, MF và approval |
| [AI implementation](AI_IMPLEMENTATION.md) | Đồng bộ ghi chú vận hành |
| [Test cases](AI_MODEL_TEST_CASES.md) | Đúng model/hợp đồng và cách phân biệt kết quả |
| [Protocol đánh giá](AI_EVALUATION_PROTOCOL.md) | Nhãn, split, metric và tiêu chí triển khai |
| [Audit preprocessing](PHOBERT_PREPROCESSING_AUDIT.md) | Đối chiếu CSV/notebook và candidate chưa deploy |
| [Hướng dẫn nhãn](HUONG_DAN_GAN_NHAN_AI.md) | Cách gán/review/export và thu rating |
| [Mapping hiện tại](MAPPING_GAN_NHAN_AI_HIEN_TAI.md) | Neo, V/A, mood, dashboard và các loại nhãn |
| [Baseline Phương](AI_BASELINE_PHUONG_2026-10-27.md) | 48 câu được chấp nhận, số đo/ca lệch/provenance |

Hai CSV và `labels-phuong-draft.json` là dữ liệu anh cung cấp. Công việc của lượt này là đọc/kiểm tra, tạo candidate/snapshot và báo cáo; không nhận là đã tự thu thập/gán độc lập các nhãn đó.

## 10. Phần còn thiếu sau đợt này

1. Preprocessing đúng bản train hoặc candidate được đánh giá đủ bằng dữ liệu độc lập.
2. Gold NLP tự nhiên, đủ support/split và review độc lập; cải thiện có kiểm chứng vùng bình yên/thư giãn và VA → mood.
3. Rating/snapshot thời gian thật đủ để calibrate/evaluate MF/Hybrid, thay vì fixture hoặc 7 rating hiện có.
4. Đo profile/release và điện thoại thật; giám sát tải/retention/retrain scheduler nếu triển khai lâu dài.
5. Giải trình model/dataset/phương pháp và phạm vi RL với GVHD. Chưa có kết luận đã nghiệm thu toàn bộ P0–P8.

## 11. Danh sách file so với Git

Danh sách dưới là snapshot ngay trước khi tạo tài liệu này. File runtime, backup, venv, build, model weights và nhãn trong `completed/` đang gitignore không nằm trong danh sách Git, dù đã được dùng khi vận hành/kiểm thử.

### Nội dung thay đổi trong file đã được Git theo dõi

- `.gitignore`
- `README.md`
- `admin-web/src/ai-workspace.js`
- `admin-web/src/management.js`
- `admin-web/tests/browser-smoke.mjs`
- `ai-service/main.py`
- `ai-service/test_main.py`
- `ai-service/va_predictor.py`
- `ai-service/vietnamese_predictor.py`
- `backend/Controllers/AiWorkspaceController.cs`
- `backend/Controllers/HistoryController.cs`
- `backend/Controllers/ModelsController.cs`
- `backend/Controllers/MoodAnalysisController.cs`
- `backend/Controllers/RecommendationController.cs`
- `backend/Data/MongoStore.cs`
- `backend/Data/mongo-schema.json`
- `backend/Models/LichSuTamTrang.cs`
- `backend/Program.cs`
- `backend/Services/HybridRecommender.cs`
- `backend/Services/ValenceArousalAnalysisService.cs`
- `backend/Services/VietnameseEmotionService.cs`
- `backend/wwwroot/app.js`
- `backend/wwwroot/index.html`
- `docs/AI_ENGINEER_IMPLEMENTATION.md`
- `docs/AI_IMPLEMENTATION.md`
- `docs/AI_MODEL_TEST_CASES.md`
- `docs/AI_SYSTEM_UNIFICATION_PLAN.md`
- `mobile-app/android/app/build.gradle.kts`
- `mobile-app/lib/core/api_service.dart`
- `mobile-app/lib/screens/mood_discovery_screen.dart`
- `mobile-app/lib/services/mood_analysis/mood_analyzer.dart`
- `mobile-app/pubspec.lock`
- `mobile-app/pubspec.yaml`
- `mobile-app/test/ai_filter_regression_test.dart`
- `mobile-app/test/bilingual_mood_flow_test.dart`
- `scripts/moodify-local.ps1`
- `tests/AiWorkspaceTests.cs`
- `tests/AuthNotificationTests.cs`
- `tests/FakeInferenceServer.cs`
- `tests/Program.cs`

### File mới chưa được Git theo dõi

- `admin-web/src/ai-tools.js`
- `admin-web/tests/annotation-review-smoke.mjs`
- `admin-web/tests/bilingual-flutter-real.mjs`
- `ai-service/artifact_fingerprint.py`
- `ai-service/config/vietnamese-preprocessing-candidate.json`
- `ai-service/evaluate_bilingual.py`
- `ai-service/evaluate_candidate.py`
- `ai-service/evaluate_hybrid.py`
- `ai-service/export_vietnamese_preprocessing.py`
- `ai-service/matrix_factorization.py`
- `ai-service/test_ai_completion.py`
- `ai-service/test_artifact_fingerprint.py`
- `ai-service/test_evaluate_bilingual.py`
- `ai-service/test_hybrid_evaluation.py`
- `ai-service/test_preprocessing_export.py`
- `ai-service/test_va_policy.py`
- `ai-service/train_experiment.py`
- `ai-service/vietnamese_preprocessing.py`
- `backend/Controllers/AiExperimentsController.cs`
- `backend/Controllers/AiPoliciesController.cs`
- `backend/Controllers/ModelLifecycleController.cs`
- `backend/Controllers/ModelQualityController.cs`
- `backend/Controllers/RecommendationExperimentsController.cs`
- `backend/Controllers/RecommenderModelsController.cs`
- `backend/Services/BilingualMoodAnalysisService.cs`
- `backend/Services/BilingualPolicyService.cs`
- `backend/Services/MatrixFactorizationService.cs`
- `backend/Services/ModelQualityService.cs`
- `backend/Services/MoodLanguageRouter.cs`
- `backend/Services/RecommendationStrategyService.cs`
- `backend/Services/VaAnalysisReceipt.cs`
- `docs/AI_BASELINE_PHUONG_2026-10-27.md`
- `docs/AI_EVALUATION_PROTOCOL.md`
- `docs/AI_SYSTEM_UNIFICATION_RESULTS.md`
- `docs/HUONG_DAN_GAN_NHAN_AI.md`
- `docs/MAPPING_GAN_NHAN_AI_HIEN_TAI.md`
- `docs/PHOBERT_PREPROCESSING_AUDIT.md`
- `docs/ai-evaluation/labels-phuong-draft.json`
- `docs/ai-evaluation/review-starter.json`
- `docs/vietnamese_emoji.csv`
- `docs/vietnamese_teencode.csv`
- `mobile-app/integration_test/bilingual_android_test.dart`
- `tests/AiCompletionTests.cs`
- `tests/BilingualAnalysisTests.cs`
- `tests/BilingualRealInferenceSmoke.cs`
- `tests/ModelQualityTests.cs`
- `tests/check_bilingual_lifecycle.py`
- `tests/check_va_lifecycle.py`
- `tools/ai-label-review.html`

### File generated Git báo sửa nhưng không có diff nội dung

- `mobile-app/windows/flutter/generated_plugin_registrant.cc`
- `mobile-app/windows/flutter/generated_plugins.cmake`

Tài liệu tổng hợp này cũng là file mới, được tạo sau snapshot danh sách trên. Không có thao tác commit/push trong lúc tổng hợp.
