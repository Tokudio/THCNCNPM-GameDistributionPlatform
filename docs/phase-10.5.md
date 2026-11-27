# Phase 10.5 — Cấu hình kiểm thử và chạy demo

## Giới hạn Login và Register

Web API áp dụng fixed window theo IP mà server ghi nhận trên kết nối. Mặc định Login có 5 requests trong 60 giây và Register có 3 requests trong 300 giây. Hai endpoint dùng hai bộ đếm riêng. Có thể cấu hình lại qua AuthRateLimiting__Login__PermitLimit, AuthRateLimiting__Login__WindowSeconds, AuthRateLimiting__Register__PermitLimit và AuthRateLimiting__Register__WindowSeconds. Thay đổi có hiệu lực khi khởi động lại API.

Giới hạn chạy theo từng instance. Người dùng chung NAT hoặc cùng bucket có thể chia sẻ quota. App không đọc trực tiếp X-Forwarded-For. Khi triển khai sau reverse proxy, xác minh cấu hình forwarded headers chỉ tin proxy đã biết, TLS/HSTS và IP thực tế server nhận được trước khi dùng quota theo client.

## Gói tải xuống demo

Gói demo-signal-path.zip chứa dữ liệu bản đồ demo được viết cho dự án và một hướng dẫn. Đây là gói dữ liệu mẫu. User cần mua hoặc Free Claim một game demo rồi mở game đó trong My Library.

Web API chỉ đọc cấu hình GameDownloads:Packages trong Development và Testing. Gán GUID của game demo do bạn quản lý với basename của tệp ZIP. Trong Git Bash, chạy từ repo root và thay GUID mẫu bằng GUID của game demo.

Quyền tải dựa trên UserLibrary. Storage mở tệp từ mapping riêng của server. Trường Game.DemoDownloadUrl hiện có và các URL ảnh/trailer không tham gia luồng này.

~~~bash
env 'GameDownloads__Packages__12345678-1234-4234-8234-123456789abc=demo-signal-path.zip' \
  dotnet run --project backend/src/GameDistributionPlatform.WebApi/GameDistributionPlatform.WebApi.csproj --launch-profile https
~~~

Gói đặt tại thư mục DemoPackages dưới Content Root của Web API. Giữ thư mục ngoài wwwroot, chỉ cấp quyền đọc cho tiến trình chạy API và chỉ cấp quyền ghi cho người quản lý gói. Bộ tải demo nhận ZIP tối đa 8 MiB, từ chối symlink/junction trên thư mục và tệp, và dùng JWT của phiên hiện tại cho từng request. Nếu cấu hình trống hoặc file mất, Library báo game chưa có tệp tải xuống. Staging và Production không dùng mapping demo. Việc phân phối thật cần storage riêng và quy trình quản lý quyền phân phối package.

## Chuẩn bị SQL Server Testing

Các test gắn trait `Database=SqlServer` mặc định được skip. Để chạy, dùng Windows Authentication trên máy `LAPTOP-UI1V2NBG` với database `GameDistributionPlatform_Test` đã có hai migrations hiện tại và 15 bảng dữ liệu trống. Fixture dùng Shared Memory có mã hóa. `TrustServerCertificate=True` chỉ áp dụng cho kết nối test local này vì chứng chỉ SQL Server local không có chuỗi tin cậy. Các test quan sát lock wait cần quyền đọc DMV của các phiên SQL Server do fixture mở.

Chạy từ repo root trong PowerShell.

~~~powershell
$env:GDP_SQLSERVER_LIVE_TESTS = '1'
try {
    dotnet test backend/tests/GameDistributionPlatform.WebApi.Tests/GameDistributionPlatform.WebApi.Tests.csproj --filter 'Database=SqlServer'
} finally {
    Remove-Item Env:GDP_SQLSERVER_LIVE_TESTS
}
~~~

Test host thay AppDbContext trước khi startup seeder chạy. Fixture xác minh server và database qua từng kết nối, kiểm tra migration history, lấy application lock trong suốt suite và dọn bản ghi theo các ID do fixture tạo. Các kịch bản race chạy đồng thời trên các kết nối riêng. Các kịch bản SQL Server chạy tuần tự với nhau. Để chạy nhóm WebApi thông thường, dùng filter `Database!=SqlServer`.

## Chuẩn bị nâng cấp .NET 10 LTS

Dùng SDK .NET 10 đã được cài và phê duyệt. Nâng target của cả solution và các package liên quan đến ASP.NET Core, EF Core SqlServer/Sqlite/Design, Identity và Mvc.Testing cùng một lượt. Kiểm tra lại OpenAPI security scheme transformer, phiên bản JWT dependency và SQL của LINQ Contains với danh sách tham số. So sánh EF Core model với snapshot hiện có trước khi quyết định có cần thay đổi schema. Chạy lại build, toàn bộ test và Chrome E2E với output riêng.
