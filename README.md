# Moodify

Moodify là hệ thống đề xuất nội dung theo tâm trạng, gồm ba thành phần:

- `backend/`: ASP.NET Core Web API + PostgreSQL.
- `admin-web/`: website quản trị HTML5/CSS3/JavaScript, chạy bằng Vite.
- `mobile-app/`: ứng dụng Flutter cho Android/Web/Windows.

## Sơ đồ hoạt động

```text
Flutter App / Moodify User Web
            |
            v
    Backend API :5170
            |
            v
       PostgreSQL

Admin Web :5174 ---> Backend API :5170
```

## Yêu cầu môi trường

Cài đặt các công cụ sau:

- .NET SDK 10
- Node.js và npm
- Flutter SDK, Dart SDK tương thích với `mobile-app/pubspec.yaml`
- PostgreSQL
- Android Studio và Android Emulator nếu chạy Android

## Cấu hình PostgreSQL

Backend đang dùng connection string trong `backend/appsettings.json`:

```text
Host=localhost;Port=5432;Database=mood_recommendation;Username=postgres;Password=123
```

Hãy tạo database `mood_recommendation`, sau đó cập nhật username/password trong file cấu hình nếu máy của bạn khác thiết lập trên.

Không commit mật khẩu thật lên repository công khai. Với môi trường production, nên dùng User Secrets hoặc biến môi trường.

## 1. Chạy Backend API

Mở terminal thứ nhất:

```powershell
cd backend
dotnet restore
dotnet run --launch-profile http
```

Backend chạy tại:

```text
http://localhost:5170
```

Swagger:

```text
http://localhost:5170/swagger
```

Các API chính:

```text
POST   /api/Auth/login
POST   /api/Auth/register

GET    /api/Genres
POST   /api/Genres
GET    /api/Genres/{id}
PUT    /api/Genres/{id}
DELETE /api/Genres/{id}

GET    /api/Movies
POST   /api/Movies
GET    /api/Movies/{id}
PUT    /api/Movies/{id}
DELETE /api/Movies/{id}

GET    /api/Music
POST   /api/Music
GET    /api/Music/{id}
PUT    /api/Music/{id}
DELETE /api/Music/{id}
```

Backend cũng phục vụ Moodify User Web tại `http://localhost:5170`.

## 2. Chạy Admin Website

Mở terminal thứ hai:

```powershell
cd admin-web
npm install
npm run dev
```

Admin Web chạy tại:

```text
http://localhost:5174
```

Admin Web đã cấu hình proxy:

```text
/api -> http://localhost:5170/api
```

Vì vậy frontend gọi API qua `/api`, còn Vite chuyển tiếp request tới backend port `5170`.

### Đăng nhập Admin

Có thể đăng nhập theo một trong hai cách:

1. Mở `http://localhost:5170`.
2. Dùng tài khoản:
   - Email: `admin@gmail.com`
   - Mật khẩu: `123`
3. Nếu tài khoản có role `QuanTriVien`, hệ thống tự chuyển sang `http://localhost:5174`.
4. Admin Web nhận session và mở Dashboard, không cần đăng nhập lại.

Hoặc mở trực tiếp `http://localhost:5174` và đăng nhập bằng cùng tài khoản.

### Lệnh kiểm tra Admin Web

```powershell
npm run build
npm run lint
npm run preview
```

## 3. Chạy Flutter Mobile App

Mở terminal thứ ba:

```powershell
cd mobile-app
flutter pub get
flutter run
```

Xem thiết bị khả dụng:

```powershell
flutter devices
```

Chạy Android emulator:

```powershell
flutter run -d android
```

Chạy Flutter Web:

```powershell
flutter run -d chrome
```

### Địa chỉ API theo nền tảng

Cấu hình hiện tại nằm tại `mobile-app/lib/core/api_service.dart`:

- Flutter Web: `http://localhost:5170`
- Android Emulator: `http://10.0.2.2:5170`
- Windows/Desktop: `http://localhost:5170`

Nếu chạy trên điện thoại thật, `localhost` là chính điện thoại. Hãy đổi base URL thành địa chỉ IP LAN của máy chạy backend, ví dụ:

```text
http://192.168.1.100:5170
```

Điện thoại và máy tính phải cùng mạng, đồng thời firewall phải cho phép port `5170`.

## Tài khoản demo

```text
Email: admin@gmail.com
Mật khẩu: 123
Role: QuanTriVien
```

Không dùng tài khoản/mật khẩu demo này trên production.

## Body API mẫu

### Login

```json
{
  "identifier": "admin@gmail.com",
  "matKhau": "123"
}
```

### Tạo thể loại

```json
{
  "TenTheLoai": "Kinh dị"
}
```

### Tạo phim

```json
{
  "NoiDungID": "MOV004",
  "TieuDe": "Tên phim",
  "HinhAnh": "https://example.com/poster.jpg",
  "MoTa": "Mô tả phim",
  "TheLoai": "Drama",
  "DiemDanhGiaTB": 8.5
}
```

### Tạo bài hát

```json
{
  "NoiDungID": "SONG004",
  "TieuDe": "Tên bài hát",
  "TenNgheSi": "Tên nghệ sĩ",
  "HinhAnh": "https://example.com/cover.jpg",
  "Genre": "Pop",
  "Duration": 210
}
```

## Thứ tự chạy đề xuất

1. Khởi động PostgreSQL.
2. Chạy Backend API tại port `5170`.
3. Chạy Admin Web tại port `5174`.
4. Chạy Flutter App nếu cần.

## Xử lý lỗi thường gặp

### Port 5170 đã được sử dụng

Kiểm tra process:

```powershell
Get-NetTCPConnection -LocalPort 5170 -State Listen
```

### Port 5174 đã được sử dụng

Kiểm tra process:

```powershell
Get-NetTCPConnection -LocalPort 5174 -State Listen
```

Đóng process không cần thiết hoặc dùng đúng server đang chạy.

### Admin Web không lấy được dữ liệu

Kiểm tra:

1. Backend có chạy tại `http://localhost:5170` không.
2. Mở Swagger để kiểm tra API.
3. Admin Web có chạy tại `http://localhost:5174` không.
4. PostgreSQL có chạy không.
5. Connection string có đúng không.
6. Mở Developer Tools của trình duyệt để xem lỗi Network/Console.

### Flutter không kết nối được API

- Android Emulator phải dùng `10.0.2.2:5170`.
- Điện thoại thật phải dùng IP LAN của máy tính.
- Backend phải lắng nghe địa chỉ có thể truy cập từ thiết bị.
- Kiểm tra firewall và hai thiết bị có cùng mạng hay không.

## Cấu trúc repository

```text
moodify-project/
├── backend/
│   ├── Controllers/
│   ├── Data/
│   ├── DTOs/
│   ├── Models/
│   ├── Program.cs
│   └── mood_recommendation.csproj
├── admin-web/
│   ├── src/
│   ├── index.html
│   ├── package.json
│   └── vite.config.js
├── mobile-app/
│   ├── lib/
│   ├── android/
│   ├── pubspec.yaml
│   └── test/
└── README.md
```
