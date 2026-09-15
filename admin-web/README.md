# Moodify Admin

Ứng dụng Web Admin thuần HTML5, CSS3 và JavaScript ES Modules cho thư viện Phim, Nhạc và Thể loại của Moodify. Vite chỉ được dùng làm dev server/build tool, không dùng React.

## Chạy local

```bash
npm install
npm run dev
```

Mở `http://localhost:5173`.

Có thể mở trực tiếp `index.html` bằng Live Server, nhưng dùng Vite được khuyến nghị để module JavaScript hoạt động ổn định.

## Build production

```bash
npm run build
npm run preview
```

## Backend API

Cấu hình trong `src/api.js`:

```text
http://localhost:5170/api
```

Endpoint backend hiện có:

- `GET/POST /Genres`, `GET /Genres/{id}`, `PUT/DELETE /Genres/{id}`.
- `GET/POST /Movies`, `GET /Movies/{id}`, `PUT/DELETE /Movies/{id}`.
- `GET/POST /Music`, `GET /Music/{id}`, `PUT/DELETE /Music/{id}`.
- `GET /Test/contents`: endpoint cũ, chỉ dùng dự phòng.
- `GET /Test/accounts`: đọc tài khoản.
- `GET /Test/moods`: đọc tâm trạng.
- `POST /Auth/login` và `POST /Auth/register`.

Frontend hiện gọi trực tiếp các route CRUD trên. Payload đã được map theo DTO C#:

- Genre: `TenTheLoai`.
- Movie: `NoiDungID`, `TieuDe`, `HinhAnh`, `MoTa`, `TheLoai`, `DiemDanhGiaTB`.
- Music: `NoiDungID`, `TieuDe`, `TenNgheSi`, `HinhAnh`, `Genre`, `Duration`.

Nếu API không thể kết nối, UI dùng mock data trong localStorage. Nếu API trả về 400/404/500, UI hiển thị thông báo lỗi server và không cập nhật giả vào bảng:

- `moodify_genres`
- `moodify_movies`
- `moodify_music`

## Cấu trúc chính

- `index.html`: HTML entrypoint
- `src/index.css`: giao diện dark mode responsive
- `src/main.js`: render DOM, routing tab, modal, search và CRUD events
- `src/api.js`: fetch service và fallback localStorage
