# GameDistributionPlatform

Nền tảng phân phối game gồm ASP.NET Core Web API (.NET 10 LTS), SQL Server và Angular 21. Mock Payment phục vụ demo trong môi trường `Development` và `Testing`.

## Chạy dự án trên Windows bằng Git Bash

Chạy từ thư mục gốc. Backend sử dụng database Development đã được cấu hình và có schema tương ứng. Cấu hình JWT và tài khoản bootstrap nếu cần qua User Secrets hoặc biến môi trường của máy.

```bash
dotnet run --project backend/src/GameDistributionPlatform.WebApi/GameDistributionPlatform.WebApi.csproj --launch-profile https
```

Profile `https` lắng nghe ở `https://localhost:7255` và `http://localhost:5105`. Nếu Windows chưa tin cậy chứng chỉ phát triển của .NET, chạy `dotnet dev-certs https --trust`.

Mở terminal thứ hai tại thư mục gốc.

```bash
cd frontend
npm ci
npm start
```

Mở `http://localhost:4200`. Cấu hình `frontend/proxy.conf.json` chuyển tiếp `/api/**` tới `https://localhost:7255`. Khi triển khai, host phải phục vụ Angular và chuyển tiếp `/api/v1/**` trên cùng origin. Các route Angular như `/library` cần được trả về `index.html` khi refresh.

Khi browser khôi phục một trang đã đăng nhập từ lịch sử, Angular tải lại trang để xác minh phiên hiện tại trước khi hiển thị dữ liệu riêng tư.

## Kiểm tra runtime và phiên bản đang chạy

```bash
netstat.exe -ano -p TCP | grep -E ':(4200|5105|7255)[[:space:]]'
curl --fail --silent --show-error https://localhost:7255/openapi/v1.json -o /dev/null
curl --fail --silent --show-error 'http://localhost:4200/api/v1/games?pageSize=1'
git status --short --untracked-files=all
```

- Tìm dòng `LISTENING` và PID để xác định tiến trình giữ cổng. Nếu cần khởi động lại, dừng đúng terminal hoặc phiên Debug của dự án. Sau đó chạy lại Backend và Angular.
- Mở `https://localhost:7255/swagger` để xem Swagger UI và kiểm tra đường dẫn trong `/openapi/v1.json`. Swagger và OpenAPI được bật ở `Development`.
- Khi Admin API trả `404` nhưng Swagger của phiên bản hiện tại có route đó, kiểm tra PID, đường dẫn DLL mà phiên chạy đang dùng và proxy target. `401` trên endpoint được bảo vệ khi chưa gửi JWT xác nhận request đã tới một endpoint cần xác thực.
- Nếu DLL Debug đang bị khóa nhưng cần giữ tiến trình hiện tại chạy, build và test vào output riêng với `--artifacts-path frontend/.angular/local-dotnet` trên cả hai lệnh bên dưới. Phiên server đang chạy cần được khởi động lại riêng để tải DLL mới.

## Build và regression tests

```bash
dotnet build backend/GameDistributionPlatform.sln
dotnet test backend/GameDistributionPlatform.sln
cd frontend
npm run build
TZ=Asia/Ho_Chi_Minh npm test -- --watch=false
./node_modules/.bin/tsc --noEmit -p tsconfig.app.json
./node_modules/.bin/tsc --noEmit -p tsconfig.spec.json
```

WebApi Integration Tests dùng SQLite in-memory. SQL Server concurrency cần một test database riêng đã được phép sử dụng. Sau khi triển khai, kiểm tra TLS, proxy routing và cấu hình bảo vệ endpoint xác thực trên host thực tế.
