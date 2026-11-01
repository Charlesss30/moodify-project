# Rà soát toàn luồng AI Việt/Anh — 30/10/2026

> Cập nhật mới nhất: [Tối ưu đầu vào và context-mapping-v4](AI_INPUT_CONTEXT_OPTIMIZATION_2026-10-30.md). Các số đo bên dưới là snapshot của lượt trước.

## Kết luận thực hiện

**Đã sửa lỗi blanket-block phủ định và triển khai `context-mapping-v3`.** Câu trong ảnh “Hôm nay tôi không được vui” hiện được gợi ý **Buồn**: PhoBERT nhận Sadness khoảng 86,8%, raw V/A khoảng −0,710/−0,442; backend chấp nhận bằng chứng phù hợp và vẫn yêu cầu người dùng xác nhận.

Lỗi ở lượt trước nằm trong **luồng xử lý**, không phải model không hiểu câu cụ thể này. Model đã nhận Sadness nhưng mapper v2 chặn mọi câu phủ định. Bản v3 không tự đảo “không vui” thành “buồn”: nó chỉ nhận mood thay thế khi kết quả model phù hợp, không trùng cảm xúc bị phủ định và vẫn đạt ngưỡng.

Đã rà **136 câu model thật**: 71 VI và 65 EN; 17 nhóm. Thời điểm kết quả: 30/10/2026 15:01 (UTC+7).

| Tiêu chí | Kết quả |
| --- | --- |
| Có mood kỳ vọng | 78 câu |
| Được nhận và khớp mood kỳ vọng | 71/78 |
| Được nhận nhưng sai mood kỳ vọng | 0 trong bộ này |
| Câu có mood kỳ vọng nhưng chưa được nhận | 7, đều VI dưới ngưỡng confidence |
| Câu xem xét từ chối/làm rõ, không gán một mood ngay | 58 |
| Ca bắt buộc abstain bị nhận nhầm | 0/52 |
| Kiểm chứng model thật/API/receipt/target/Hybrid/history | 1046 assertion pass |
| Ngữ cảnh/model evidence/taxonomy | 209 assertion pass |
| Backend regression trên MongoDB tạm | 1.000 assertion pass |
| Python | 34 tests pass |
| Flutter | 82 tests pass; analyzer sạch |
| Browser | Admin/AI Engineer roles, inference UI, dataset/job/policy/forms pass |
| GoEmotions legacy thật | CUDA → authenticated gateway → recommendation/history/manual fallback pass |

Đây là bộ kiểm tra kỹ thuật và ví dụ ngữ nghĩa tự soạn đã dùng trong quá trình sửa. **Không gọi các tỷ lệ là accuracy tổng quát, không phải test gold độc lập**, không bảo đảm mọi câu tự nhiên đều đúng. Với câu chưa chắc chắn, hệ thống từ chối và cho người dùng chọn mood; không ép ra một kết quả để làm đẹp tỷ lệ.

## Lỗi tìm thấy và đã sửa

1. **Chặn tất cả câu phủ định:** giờ dùng kết quả model phù hợp; giữ loại trừ mood bị phủ định. “Không còn buồn” không được gán Sad chỉ vì model vẫn dự đoán Sadness.
2. **Phạm vi phủ định quá rộng/hẹp:** thay cửa sổ từ tùy ý bằng cụm từ phủ định/modifier có giới hạn; xử lý “không được vui”, “không cảm thấy thật sự hạnh phúc”, “không buồn và đang vui”, neither/nor, viết tắt k/ko.
3. **Phủ định trong so sánh:** “chưa từng vui như hôm nay”, “never felt happier”, “not only happy but excited” không bị coi là phủ định cảm xúc chính.
4. **Lẫn mô tả phim/nhạc với cảm xúc:** “watching a happy movie” và “listening to a sad song” không tự thành Happy/Sad; “made me feel happy/sad” vẫn là phản ứng cá nhân hợp lệ.
5. **Mỉa mai có dấu hiệu:** khi có 🙄, “yeah right”, /s hoặc dấu hiệu đã hỗ trợ, yêu cầu nói rõ thay vì gán Happy từ một từ đơn.
6. **Giả định/tương lai:** câu chỉ nói tương lai hoặc chưa rõ trạng thái không được coi là cảm xúc hiện tại; “excited about tomorrow” vẫn là cảm xúc hiện tại.
7. **Việt–Anh trộn:** nhận diện cue/phủ định cả hai ngôn ngữ trong mapper; backend vẫn tự chọn một model và ghi đúng provenance.
8. **Ghi đè model bằng từ khóa:** khi model và mô tả mâu thuẫn mạnh về tích cực/tiêu cực, yêu cầu xác nhận thay vì âm thầm bỏ bằng chứng model.
9. **Ngôn ngữ bị ép sai:** API không đưa mô tả Việt vào model Anh hoặc ngược lại chỉ vì caller gửi override trái ngôn ngữ; không hỗ trợ script thì trả trạng thái tương ứng.
10. **Thông báo gây hiểu nhầm:** trường hợp model confidence cao nhưng ngữ cảnh chưa rõ hiển thị “Cần làm rõ cảm xúc hiện tại”, thay cho việc gom hết thành “Chưa đủ tin cậy”.

Không chỉnh weights, điểm neo, preprocessing, confidence threshold, model registry hoặc nhãn của người dùng trong lượt này. Phần diễn giải câu trực tiếp là **quy tắc ngữ cảnh kết hợp model**, được ghi rõ bằng `mappingMethod`, không giả là classifier đã được train thêm.

## Ma trận các nhóm câu đã chạy bằng model thật

| Nhóm | Số câu | Phạm vi |
| --- | ---: | --- |
| Bộ 40 câu nền | 40 | `baseline` |
| Phủ định và phạm vi phủ định | 20 | `negation` |
| Phủ định trong so sánh/cường điệu | 6 | `comparative` |
| Quá khứ, hiện tại, tương lai | 10 | `tense` |
| Cảm xúc hiện tại và mong muốn nội dung | 10 | `goal` |
| Cảm xúc người khác | 6 | `other` |
| Văn bản trích dẫn | 4 | `quote` |
| Nhiều cảm xúc | 6 | `mixed` |
| Giận/lo sợ ngoài taxonomy | 6 | `unsupported` |
| Giả định/chưa chắc | 4 | `hypothetical` |
| Dấu hiệu mỉa mai | 2 | `sarcasm` |
| Thuộc tính phim/bài hát | 4 | `content` |
| Phản ứng cá nhân sau trải nghiệm | 4 | `reaction` |
| Viết hoa, khoảng trắng, emoji | 6 | `format` |
| Từ nhiều nghĩa | 2 | `polysemy` |
| Viết tắt/tiếng Việt không dấu | 3 | `shorthand` |
| Câu trộn Việt–Anh | 3 | `code-switch` |

## Các lớp kiểm tra ngoài bộ câu

| Lớp | Ca đã kiểm tra | Loại bằng chứng |
| --- | --- | --- |
| Input | Rỗng/khoảng trắng, quá 4.000 ký tự, language không hợp lệ, ngôn ngữ ép sai, script Trung/Nhật/Nga/Ả Rập | HTTP/backend contract |
| Auto routing | Việt có/không dấu, English, viết hoa, emoji và câu trộn | Model thật + Flutter/backend |
| Model service | Tắt AI, mất kết nối, timeout, caller cancellation, busy 429/503, lỗi 500/422, JSON sai | Fixture lỗi/contract; health thật riêng |
| Output | Sai language/version/nhãn/confidence/V/A, thiếu tọa độ, out-of-range | Backend/Python contract |
| Quyết định | Cue trực tiếp, phủ định, conflict, mixed, taxonomy/policy không cho phép | Context tests + model thật |
| Receipt | Gắn user, hết hạn, đổi model/mood geometry/policy, replay/retry, lease đồng thời, restart | MongoDB tạm + backend HTTP |
| Gợi ý | Target trong vùng mood, raw V/A giữ nguyên, Hybrid dùng target server | Model thật → receipt → recommendation |
| Lịch sử | Model/language/mapping, raw/target, quyết định AI/manual | Model thật + backend |
| Mobile state | Sửa text/loại nội dung, request cũ, model unavailable, chọn lại thủ công, dữ liệu trả sai kiểu | Flutter unit/widget |
| Admin/AI Engineer | Role isolation, pipeline preview, policy approval/reset, dataset/job/quality controls | Browser + backend |
| Lifecycle/evaluation | Lưu/validate/activate/rollback contract, pin fingerprint, preprocessing/evaluation/Hybrid | Python/backend tests; không Activate model chính |

Fixture lỗi chứng minh xử lý lỗi; không gọi nó là đã làm sập model thật hoặc load test production. Lifecycle distinct-weight và Android real-model E2E từng được ghi ở các đợt trước; không tính lại các lượt đó là mới chạy trong phiên này.

## Hiệu năng local

- VI: 71 lần API, median **26.3 ms**, p95 **30.0 ms**.
- EN: 65 lần API, median **25.8 ms**, p95 **31.4 ms**.

Burst 4 request đồng thời: status [200, 200, 200, 200], cao nhất khoảng 106.3 ms trong lần đo. Đường busy/reject có test fixture riêng.

Đây là API local có model đã sẵn sàng, không là thời gian cold start, render UI hay SLA. Lượt test chạy cùng một số kiểm thử khác trên máy. Không nạp thêm model/SDK AI lên điện thoại; mapper là regex có giới hạn thời gian, không thêm lần inference cho request ứng dụng.

GPU quan sát RTX 3050 Laptop 4 GB, dùng khoảng 3.076 MiB ở một thời điểm sau test, gồm các tiến trình khác; đây không phải peak theo workload chuẩn. Không tuyên bố đạt 60 FPS từ số đo API.

## Bảy câu chưa đạt ngưỡng model

Không hạ threshold để ép các câu này được nhận. Người dùng vẫn có thể chọn mood thủ công; không có receipt AI đã chấp nhận.

| ID | Câu | Mood kỳ vọng | Lý do |
| --- | --- | --- | --- |
| `vi-10` | Hôm qua tôi buồn nhưng hôm nay tôi vui. | Vui vẻ | `low_confidence` |
| `vi-18` | Tôi đang tập trung cao độ để học và không muốn bị làm phiền. | Tập trung | `low_confidence` |
| `vi-19` | Tôi nhớ tuổi thơ, những ký ức cũ khiến tôi bồi hồi hoài niệm. | Hoài niệm | `low_confidence` |
| `vi-negation-09` | Tôi không buồn, tôi đang vui | Vui vẻ | `low_confidence` |
| `vi-negation-10` | Tôi không thấy buồn và đang vui | Vui vẻ | `low_confidence` |
| `vi-tense-02` | Hôm qua tôi buồn nhưng hiện tại vui | Vui vẻ | `low_confidence` |
| `vi-goal-02` | Tôi buồn và muốn xem phim vui | Buồn | `low_confidence` |

## Toàn bộ kết quả

“—” trong kỳ vọng nghĩa câu dùng kiểm tra guard/làm rõ, không phải nhãn gold bỏ sót. Đáp án kỳ vọng là ví dụ kiểm thử, không phải nhãn độc lập.

| ID | Nhóm | Câu nhập | Kỳ vọng | Kết quả | Nguồn/lý do |
| --- | --- | --- | --- | --- | --- |
| `vi-01` | baseline | hôm nay tao vui | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-01` | baseline | I am happy today. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-02` | baseline | Hôm nay tôi cảm thấy vui vẻ. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-02` | baseline | Today I feel happy. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-03` | baseline | Hôm nay tôi rất vui và hài lòng với cuộc sống. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-03` | baseline | Today I am happy and content with my life. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-04` | baseline | Tôi cực kỳ hào hứng, tràn đầy năng lượng và mong chờ buổi hòa nhạc tối nay! | Hào hứng | Hào hứng | `explicit_self_description` |
| `en-04` | baseline | I am extremely excited, full of energy and cannot wait for tonight's concert! | Hào hứng | Hào hứng | `explicit_self_description` |
| `vi-05` | baseline | Hôm nay tôi rất buồn, chỉ muốn khóc. | Buồn | Buồn | `explicit_self_description` |
| `en-05` | baseline | Today I feel very sad and just want to cry. | Buồn | Buồn | `explicit_self_description` |
| `vi-06` | baseline | Tôi thấy bình yên và thư giãn khi ngồi uống trà. | Thư giãn | Thư giãn | `explicit_self_description` |
| `en-06` | baseline | I feel peaceful and relaxed while sitting with a cup of tea. | Thư giãn | Thư giãn | `explicit_self_description` |
| `vi-07` | baseline | Tôi rất tức giận vì bị đối xử bất công. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `en-07` | baseline | I am very angry because I was treated unfairly. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-08` | baseline | Tôi lo lắng và sợ hãi về chuyện ngày mai. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `en-08` | baseline | I am anxious and scared about tomorrow. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `vi-09` | baseline | Hôm nay tôi không vui, tôi thấy rất buồn. | Buồn | Buồn | `explicit_self_description` |
| `en-09` | baseline | I am not happy today; I feel very sad. | Buồn | Buồn | `explicit_self_description` |
| `vi-10` | baseline | Hôm qua tôi buồn nhưng hôm nay tôi vui. | Vui vẻ | Cần chọn/làm rõ | `low_confidence` |
| `en-10` | baseline | I was sad yesterday but today I am happy. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-11` | baseline | Tôi đang buồn nhưng muốn tìm nhạc vui để cải thiện tâm trạng. | Buồn | Buồn | `explicit_self_description` |
| `en-11` | baseline | I feel sad but want happy music to improve my mood. | Buồn | Buồn | `explicit_self_description` |
| `vi-12` | baseline | Tôi vừa vui vừa lo lắng về công việc mới. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `en-12` | baseline | I feel both happy and anxious about my new job. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `vi-13` | baseline | hom nay tao vui | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-13` | baseline | I feel happy. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-14` | baseline | Hôm nay tôi vui quá 😊 | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-14` | baseline | I am so happy today 😊 | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-15` | baseline | Tôi vui lắm! | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-15` | baseline | I am happy! | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-16` | baseline | Tôi vui vì đã hoàn thành công việc hôm nay. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-16` | baseline | I am happy because I finished my work today. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-17` | baseline | Tôi đang yêu và thấy thật lãng mạn khi nghĩ về người ấy. | Lãng mạn | Lãng mạn | `explicit_self_description` |
| `en-17` | baseline | I am in love and feel romantic when I think about my partner. | Lãng mạn | Lãng mạn | `explicit_self_description` |
| `vi-18` | baseline | Tôi đang tập trung cao độ để học và không muốn bị làm phiền. | Tập trung | Cần chọn/làm rõ | `low_confidence` |
| `en-18` | baseline | I am focused on studying and do not want to be disturbed. | Tập trung | Tập trung | `explicit_self_description` |
| `vi-19` | baseline | Tôi nhớ tuổi thơ, những ký ức cũ khiến tôi bồi hồi hoài niệm. | Hoài niệm | Cần chọn/làm rõ | `low_confidence` |
| `en-19` | baseline | I miss my childhood and feel nostalgic about those old memories. | Hoài niệm | Hoài niệm | `explicit_self_description` |
| `vi-20` | baseline | Tôi rất phấn khích và háo hức chờ chuyến đi sắp tới! | Hào hứng | Hào hứng | `explicit_self_description` |
| `en-20` | baseline | I am thrilled and excited about my upcoming trip! | Hào hứng | Hào hứng | `explicit_self_description` |
| `vi-negation-01` | negation | Hôm nay tôi không được vui | Buồn | Buồn | `model_negation` |
| `en-negation-01` | negation | I am not happy today. | Buồn | Buồn | `model_negation` |
| `vi-negation-02` | negation | Hôm nay tôi không vui | Buồn | Buồn | `model_negation` |
| `en-negation-02` | negation | I don't feel happy today. | Buồn | Buồn | `model_negation` |
| `vi-negation-03` | negation | Tôi chẳng thấy vui chút nào | Buồn | Buồn | `model_negation` |
| `en-negation-03` | negation | I am not feeling very happy. | Buồn | Buồn | `model_negation` |
| `vi-negation-04` | negation | Tôi không cảm thấy thật sự hạnh phúc | Buồn | Buồn | `model_negation` |
| `en-negation-04` | negation | I do not feel particularly happy. | Buồn | Buồn | `model_negation` |
| `vi-negation-05` | negation | Tôi không buồn | — | Cần chọn/làm rõ | `negation_model_conflict` |
| `en-negation-05` | negation | I am not sad. | — | Cần chọn/làm rõ | `negation_model_conflict` |
| `vi-negation-06` | negation | Tôi không còn buồn nữa | — | Cần chọn/làm rõ | `negation_model_conflict` |
| `en-negation-06` | negation | I am no longer sad. | — | Cần chọn/làm rõ | `negation_model_conflict` |
| `vi-negation-07` | negation | Tôi không vui cũng không buồn | — | Cần chọn/làm rõ | `negation_model_conflict` |
| `en-negation-07` | negation | I am neither happy nor sad. | — | Cần chọn/làm rõ | `negation_model_conflict` |
| `vi-negation-08` | negation | Không phải là tôi không vui | — | Cần chọn/làm rõ | `ambiguous_negation` |
| `en-negation-08` | negation | I am not not happy. | — | Cần chọn/làm rõ | `ambiguous_negation` |
| `vi-negation-09` | negation | Tôi không buồn, tôi đang vui | Vui vẻ | Cần chọn/làm rõ | `low_confidence` |
| `en-negation-09` | negation | I am not sad, I feel happy. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-negation-10` | negation | Tôi không thấy buồn và đang vui | Vui vẻ | Cần chọn/làm rõ | `low_confidence` |
| `en-negation-10` | negation | I am not sad and I feel happy. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-comparative-01` | comparative | Tôi chưa từng cảm thấy vui như hôm nay | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-comparative-01` | comparative | I have never felt happier than today. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-comparative-02` | comparative | Tôi chưa bao giờ thấy buồn đến thế này | Buồn | Buồn | `explicit_self_description` |
| `en-comparative-02` | comparative | I've never been so sad. | Buồn | Buồn | `explicit_self_description` |
| `vi-comparative-03` | comparative | Tôi không chỉ vui mà còn rất hào hứng | Hào hứng | Hào hứng | `explicit_self_description` |
| `en-comparative-03` | comparative | I am not only happy but also excited. | Hào hứng | Hào hứng | `explicit_self_description` |
| `vi-tense-01` | tense | Hôm qua tôi vui nhưng hôm nay buồn | Buồn | Buồn | `explicit_self_description` |
| `en-tense-01` | tense | I was happy yesterday but now I am sad. | Buồn | Buồn | `explicit_self_description` |
| `vi-tense-02` | tense | Hôm qua tôi buồn nhưng hiện tại vui | Vui vẻ | Cần chọn/làm rõ | `low_confidence` |
| `en-tense-02` | tense | I was sad yesterday but currently I am happy. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-tense-03` | tense | Hôm qua tôi vui | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-tense-03` | tense | I was happy yesterday. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-tense-04` | tense | Ngày mai tôi sẽ vui | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-tense-04` | tense | Tomorrow I will be happy. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-tense-05` | tense | Hôm nay tôi hào hứng về chuyến đi ngày mai | Hào hứng | Hào hứng | `explicit_self_description` |
| `en-tense-05` | tense | I am excited about tomorrow's trip. | Hào hứng | Hào hứng | `explicit_self_description` |
| `vi-goal-01` | goal | Tôi muốn nghe nhạc vui nhưng hiện tại rất buồn | Buồn | Buồn | `explicit_self_description` |
| `en-goal-01` | goal | I want happy music but right now I feel sad. | Buồn | Buồn | `explicit_self_description` |
| `vi-goal-02` | goal | Tôi buồn và muốn xem phim vui | Buồn | Cần chọn/làm rõ | `low_confidence` |
| `en-goal-02` | goal | I feel sad and want a happy movie. | Buồn | Buồn | `explicit_self_description` |
| `vi-goal-03` | goal | Tôi muốn cảm thấy hạnh phúc | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-goal-03` | goal | I want to feel happy. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-goal-04` | goal | Tôi đang tìm nhạc vui | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-goal-04` | goal | I am looking for happy music. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-goal-05` | goal | Tôi hy vọng ngày mai sẽ vui | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-goal-05` | goal | I hope to be happy tomorrow. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-other-01` | other | Bạn tôi vui | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-other-01` | other | My friend is happy. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-other-02` | other | Anh ấy rất buồn | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-other-02` | other | He is very sad. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-other-03` | other | Mẹ tôi đang vui | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-other-03` | other | My mother is happy. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-quote-01` | quote | Tôi đọc câu “hôm nay vui” | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-quote-01` | quote | I read: "happy today". | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-quote-02` | quote | Tôi nghe nói rằng “tôi vui” | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-quote-02` | quote | I heard "I am happy". | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-mixed-01` | mixed | Tôi vừa vui vừa buồn | — | Cần chọn/làm rõ | `mixed_emotions` |
| `en-mixed-01` | mixed | I feel happy and sad. | — | Cần chọn/làm rõ | `mixed_emotions` |
| `vi-mixed-02` | mixed | Tôi vui nhưng lo lắng | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `en-mixed-02` | mixed | I am happy but anxious. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `vi-mixed-03` | mixed | Tôi buồn nhưng cũng hào hứng | — | Cần chọn/làm rõ | `mixed_emotions` |
| `en-mixed-03` | mixed | I feel sad and excited. | — | Cần chọn/làm rõ | `mixed_emotions` |
| `vi-unsupported-01` | unsupported | Tôi rất tức giận | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `en-unsupported-01` | unsupported | I am furious. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `vi-unsupported-02` | unsupported | Tôi đang cảm thấy rất lo lắng | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `en-unsupported-02` | unsupported | I am worried and stressed. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `vi-unsupported-03` | unsupported | Tôi đang sợ hãi | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `en-unsupported-03` | unsupported | I am scared. | — | Cần chọn/làm rõ | `unsupported_emotion` |
| `vi-hypothetical-01` | hypothetical | Nếu tôi vui thì tôi sẽ nghe nhạc | — | Cần chọn/làm rõ | `uncertain_statement` |
| `en-hypothetical-01` | hypothetical | If I am happy I will listen to music. | — | Cần chọn/làm rõ | `uncertain_statement` |
| `vi-hypothetical-02` | hypothetical | Có lẽ tôi vui, tôi không chắc | — | Cần chọn/làm rõ | `uncertain_statement` |
| `en-hypothetical-02` | hypothetical | Maybe I am happy, I am not sure. | — | Cần chọn/làm rõ | `uncertain_statement` |
| `vi-sarcasm-01` | sarcasm | Tôi vui quá 🙄 | — | Cần chọn/làm rõ | `ambiguous_statement` |
| `en-sarcasm-01` | sarcasm | Yeah right, I am so happy 🙄 | — | Cần chọn/làm rõ | `ambiguous_statement` |
| `vi-content-01` | content | Tôi xem một bộ phim vui | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-content-01` | content | I am watching a happy movie. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-content-02` | content | Tôi nghe một bài hát buồn | — | Cần chọn/làm rõ | `not_current_self_description` |
| `en-content-02` | content | I am listening to a sad song. | — | Cần chọn/làm rõ | `not_current_self_description` |
| `vi-reaction-01` | reaction | Bộ phim này làm tôi cảm thấy vui | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-reaction-01` | reaction | This movie made me feel happy. | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-reaction-02` | reaction | Nghe bài hát đó làm tôi buồn | Buồn | Buồn | `explicit_self_description` |
| `en-reaction-02` | reaction | Listening to that song made me feel sad. | Buồn | Buồn | `explicit_self_description` |
| `vi-format-01` | format | HÔM NAY TÔI RẤT VUI!!! | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-format-01` | format | I AM VERY HAPPY TODAY!!! | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-format-02` | format |   Hôm nay  tôi thấy vui   | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `en-format-02` | format |   I feel  happy today   | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-format-03` | format | Hôm nay tôi cảm thấy buồn 😢 | Buồn | Buồn | `explicit_self_description` |
| `en-format-03` | format | I feel sad today 😢 | Buồn | Buồn | `explicit_self_description` |
| `vi-polysemy-01` | polysemy | Tôi đang yêu cầu một bộ phim | — | Cần chọn/làm rõ | `low_confidence` |
| `en-polysemy-01` | polysemy | I am creating content. | — | Cần chọn/làm rõ | `unresolved_mood` |
| `vi-shorthand-01` | shorthand | hom nay toi khong vui | Buồn | Buồn | `model_negation` |
| `vi-shorthand-02` | shorthand | hom nay t k vui | Buồn | Buồn | `model_negation` |
| `vi-shorthand-03` | shorthand | Hôm nay mình không được vui | Buồn | Buồn | `model_negation` |
| `vi-code-switch-01` | code-switch | Hôm nay I feel happy | Vui vẻ | Vui vẻ | `explicit_self_description` |
| `vi-code-switch-02` | code-switch | Tôi feel sad today | Buồn | Buồn | `explicit_self_description` |
| `vi-code-switch-03` | code-switch | Hôm nay mình không happy | Buồn | Buồn | `model_negation` |

## Chạy lại

Từ root project, bật stack AI trước. Các suite ghi vào DB/marker tạm; không ghi đánh giá/histories thử vào account chính. Smoke cuối trên API chính được ghi riêng bên dưới.

```powershell
dotnet build tests\Moodify.Integration.csproj --artifacts-path .local-build\ai-flow-audit
$env:MOODIFY_TEST_ROOT = (Get-Location).Path
$env:MOODIFY_BACKEND_DLL = Join-Path (Get-Location) '.local-build\ai-flow-audit\bin\mood_recommendation\debug\mood_recommendation.dll'

dotnet .local-build\ai-flow-audit\bin\Moodify.Integration\debug\Moodify.Integration.dll --mood-context-contract
dotnet .local-build\ai-flow-audit\bin\Moodify.Integration\debug\Moodify.Integration.dll
dotnet .local-build\ai-flow-audit\bin\Moodify.Integration\debug\Moodify.Integration.dll --bilingual-full-audit
```

Diagnostic trả mã **2** khi còn câu có kỳ vọng chưa được nhận hoặc có lỗi guard; xem JSON để phân biệt từ chối vì threshold với accepted wrong. Lượt này còn 7 ca dưới threshold, không báo toàn bộ 136 câu matched/pass ngữ nghĩa.

Dữ liệu ca bổ sung: `tests/ai-cases/bilingual-flow-cases.json`; 40 ca nền trong `tests/BilingualMoodDiagnostics.cs`.

Bằng chứng model thật: `.local-build/bilingual-mood-diagnostic-20261030T080055Z/results.json`.

Log liên quan dưới `.local-build/`: `ai-flow-real-final.log`, `ai-flow-backend-final.log`, `ai-flow-python-final.log`, `ai-flow-flutter-final.log`, `ai-flow-browser-final.log`, `ai-flow-legacy-real.log`, `ai-flow-analyzer-final.log`.

## Smoke cuối trên API chính

Sau Stop/Start đã đăng nhập bằng account test được người dùng cho phép và gửi đúng câu trong ảnh tới API cổng 5170:

- `status=matched`, `mood=Sad`, `modelVersion=phobert_vsmec_v2`.
- `mappingMethod=model_negation`, `mappingVersion=vsmec-anchors-v1|context-mapping-v3|model_negation`.
- Confidence model `0,8675547` (khoảng 86,8% điểm phân loại, không phải accuracy).
- Chỉ một analysis smoke trên DB chính: có receipt sống ngắn và metric tổng hợp; không gọi Recommendation, không gửi rating, không thêm history hoặc account.
- API/Genres, Admin web và readiness VI/VA đều HTTP 200. Release DLL mới hơn source mapper cuối.

## Trạng thái vận hành và phần chưa chứng minh

- Backend nạp mapper v3 sau khi Stop/Start; hai model VI/EN và GoEmotions legacy giữ version active.
- Khởi động lại app/debug Hot Restart để nạp phần thông báo Flutter mới; việc phân tích lại lấy logic server mới.
- `adb devices` không có thiết bị và không thấy tiến trình emulator lúc kiểm tra. Lượt này chưa kiểm thử trực tiếp màn hình Android hoặc cài APK; unit/widget và HTTP model thật đã kiểm tra riêng.
- Không có bộ gold độc lập mới; không thể kết luận mọi cách diễn đạt, mỉa mai ngầm hoặc tiếng lóng đều được hiểu đúng. Giữ confidence/clarification/manual để giảm gán sai.
- Preprocessing VI gốc vẫn thiếu export đã xác minh; không tự thay từ điển candidate trước hạn nộp.
- Chưa có đánh giá hiệu năng profile/release trên điện thoại thật. Không lấy kết quả emulator/HTTP thay cho nghiệm thu thiết bị.

[Chẩn đoán 29/10](AI_MOOD_ROUTING_DIAGNOSTIC_2026-10-29.md) · [Bản sửa mapper v2](AI_MOOD_MAPPING_FIX_2026-10-30.md)
