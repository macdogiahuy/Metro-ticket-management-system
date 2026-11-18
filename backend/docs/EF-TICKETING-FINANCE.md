# EF mapping ticketing và finance BE-02B

Phạm vi này triển khai persistence mapping theo SRS v1.2 và BE-02B trong
[`TASK-ASSIGNMENT.md`](../../docs/planning/TASK-ASSIGNMENT.md). Schema hiện hành
là `infra/database/migrations/001` đến `006`; các migration SQL này vẫn quản lý
DDL, check constraint, exclusion constraint, trigger, hàm nghiệp vụ và retention.

## Các bảng đã map

| Nhóm nghiệp vụ | Entity và bảng |
| --- | --- |
| Thanh toán và callback | `Payment` → `payments`, `PaymentEvent` → `payment_events` |
| Hoàn khoản thu lỗi và lịch sử | `RefundRequest`, `RefundTransaction`, `RefundAttempt`, `RefundPayoutRevision` → bốn bảng `refund_*` tương ứng |
| Soát vé | `TicketCredential` → `ticket_credentials`, `Journey` → `journeys`, `AfcEvent` → `afc_events` |
| Gợi ý chuyến | `TripSuggestion` → `trip_suggestions` |
| Đồng ý mua hộ | `PurchaseConsent` → `purchase_consents` |
| Quan hệ giám hộ | `GuardianLink` → `guardian_links` |
| Đổi vé do gián đoạn | `TicketChangeRequest` → `ticket_change_requests` |
| Thất lạc | `LostReport`, `FoundItem`, `LostItemClaim`, `LostItemHandover` → `lost_reports`, `found_items`, `lost_item_claims`, `lost_item_handovers` |

Không đăng ký entity, DbSet hoặc tham chiếu EF cho `capacity_profiles`,
`trip_capacity_segments`, `capacity_holds`, `capacity_hold_segments` hay mô hình
quota cũ. Các bảng lịch sử trong SQL được giữ nguyên; BE-02B không xóa dữ liệu
hoặc sửa migration đã áp dụng.

## Ranh giới Clean Architecture

- `Metro.Domain/Entities/{Finance,Ticketing,Passengers,LostProperty}` và `Tickets/TicketCredential` chứa POCO,
  enum và liên kết giữa các entity thuộc phạm vi này. Domain không tham chiếu EF,
  Npgsql, Identity hay Infrastructure.
- `Metro.Infrastructure/Persistence/Configurations` chứa từng
  `IEntityTypeConfiguration<T>`, với schema `metro`, tên cột snake_case, kiểu dữ
  liệu, độ dài, nullability, default SQL, khóa, quan hệ và index.
- `ApplicationDbContext` bổ sung 16 DbSet và dùng lại `TicketCredentials` của BE-02A; tự nạp configuration trong assembly.
  Cấu hình DI hiện có tiếp tục sử dụng DbContext này.
- Chưa bổ sung handler, repository, API hoặc chính sách phân quyền trong task
  mapping. Các use case tiếp theo triển khai trong Application qua abstraction
  cần thiết; Infrastructure thực hiện truy cập dữ liệu.

BE-02B đã đồng bộ `dev` tại commit `c8161cd`, bao gồm PR #54 (BE-02A).
Nền BE-02A ban đầu là `c5e7259`, đã có bản sửa trigger-generated ticket `3a458a9`.
Các FK tới identity, network, orders, order_versions và tickets dùng trực tiếp
entity/configuration BE-02A. `Persistence/Mapping/ExternalReferences.cs` chỉ còn
hai shared type chứa khóa cho `incidents` và `cars`, thuộc phạm vi khác.
Không đăng ký thêm shared type cho các bảng lõi đã có entity BE-02A.

`TicketCredential` dùng duy nhất entity `Metro.Domain.Entities.Tickets.TicketCredential`
và configuration BE-02A, giữ các thuộc tính text và thời gian UTC của API hiện có.
BE-02B bổ sung kiểu `char(64)` của token hash và giữ `DeleteBehavior.NoAction`.
Identity tiếp tục dùng `IdentityUser` với khóa `string` tại `public.AspNetUsers`.

Tất cả entity BE-02B triển khai `IOperatorScoped`, dùng chung query filter và
kiểm tra ghi của BE-02A. Caller phải cung cấp `IOperatorContext` hợp lệ cho
`ApplicationDbContext`; thiếu operator thì truy vấn trả rỗng và ghi bị từ chối.
Các FK composite giữ thứ tự `(OperatorId, Id)` và
`(OperatorId, OrderId, Version)` của phiên bản đơn.

## Quy tắc persistence cần giữ

- FK composite giữ phạm vi operator; consent và gợi ý tham chiếu đúng phiên bản
  đơn. Toàn bộ quan hệ nghiệp vụ dùng `DeleteBehavior.NoAction`, phù hợp FK SQL.
- Enum lưu dạng text đúng hợp đồng SQL, ví dụ `SandboxGateway` thành
  `SANDBOX_GATEWAY`, `ReviewRequired` thành `REVIEW_REQUIRED`. Một số trạng thái
  schema còn phục vụ lịch sử; có mapping không có nghĩa là cho phép use case mới
  sử dụng trạng thái ngoài SRS.
- Tiền minor-unit dùng `long`/`bigint`, ngày dịch vụ dùng `DateOnly`/`date`,
  thời điểm dùng `DateTimeOffset`/`timestamptz` và truyền UTC. JSON snapshot dùng
  `string`/`jsonb`, ciphertext dùng `byte[]`/`bytea`; token chỉ lưu hash.
- UUID, correlation ID và timestamp tạo mới dùng default của PostgreSQL. Các
  `updated_at` chỉ có default khi INSERT; caller/hàm SQL phải cập nhật khi sửa.
- Giữ unique index cho idempotency và partial unique index cho thanh toán thành
  công, hoàn tiền chưa rõ kết quả, hành trình mở, consent chờ, giám hộ đang hiệu
  lực, gợi ý hiện hành, đổi vé đã áp dụng, yêu cầu nhận và bàn giao đồ.
- Trigger SQL tự supersede gợi ý cũ cùng đơn/phiên bản khi INSERT gợi ý mới.
  Reload hoặc truy vấn không tracking khi cần đọc trạng thái bản ghi cũ đã bị
  trigger cập nhật. Gợi ý không giữ chỗ và không bắt buộc chuyến vào cổng.
- Thanh toán/phát hành/hủy/đổi vé dùng các hàm SQL hiện có theo mục 5.1 của
  `TASK-ASSIGNMENT.md`, trong transaction phù hợp. Không dùng `SaveChanges` để tự
  dựng lại chuỗi chuyển trạng thái nhiều bảng. Mapping không thay thế kiểm tra
  quyền, chủ sở hữu, bằng chứng, trùng hiệu lực, quy tắc cặp trẻ–giám hộ hoặc
  điều kiện đổi/hoàn ở Application.
- Tất cả bảng `metro` được map đều `ExcludeFromMigrations()`. Triển khai database
  bằng `infra/database/deploy.sql`; không dùng `EnsureCreated` hoặc migration EF
  để dựng lại schema này.

## Kiểm tra


Từ thư mục gốc, với một PostgreSQL test riêng đã được triển khai schema:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./backend/tests/Run-PostgresTests.ps1
```

Script kiểm tra cấu hình test, chạy toàn bộ test backend ở Release và từ chối
kết quả có test fail hoặc skip. CI khởi động PostgreSQL trực tiếp trên hosted
runner, không dùng Docker/Testcontainers. Không dùng connection string của ứng
dụng. Mỗi test mapping tự rollback transaction, phù hợp quy tắc không DELETE
lịch sử.

Nếu đã có database test riêng đã triển khai SQL, tên database phải bắt đầu bằng
`metro_test_`. Thiết lập riêng tư `METRO_TEST_POSTGRES_CONNECTION_STRING`
và `METRO_TEST_POSTGRES_DATABASE` theo
[`BE-02A-TESTING.md`](../tests/Metro.IntegrationTests/BE-02A-TESTING.md), rồi chạy
từ `backend`. Không ghi credential vào source, command history hoặc test output.

```powershell
$env:METRO_TEST_POSTGRES_REQUIRED = '1'
dotnet test Metro.sln --configuration Release
```

Không có `METRO_TEST_POSTGRES_CONNECTION_STRING` thì các test PostgreSQL được
đánh dấu skip rõ lý do; các test model vẫn chạy. CI thiết lập đầy đủ các biến
`METRO_TEST_POSTGRES_*`; acceptance không chấp nhận skip.

Các test kiểm tra toàn bộ cột và FK của 17 bảng với catalog PostgreSQL, kiểm
tra model/enum/index, ghi–đọc cả 17 entity, default sinh từ DB, JSON/ciphertext,
tham chiếu phiên bản đơn, ranh giới operator, chống callback trùng, trạng thái
hoàn tiền chưa rõ, hành trình cặp, QR thay thế, supersede gợi ý, giới hạn đổi vé
và bàn giao đồ một lần.

## Kiểm tra trước khi gửi review

- Chạy `Run-PostgresTests.ps1`: toàn bộ test phải pass, không có test skip.
  Script đọc TRX và
  từ chối kết quả có test fail/skip hoặc không thực thi test PostgreSQL.
- CI dùng chính script này với `-NoBuild`, không duy trì bộ cấu hình database
  thứ hai. Kết quả TRX được lưu thành artifact `backend-test-results`.
- `Test-CiCredentialPolicy.ps1` kiểm tra workflow và test runner, chặn password
  ghi trực tiếp trong YAML hoặc connection string. Đây là guard
  cho cấu hình test, không thay thế secret scanner. Không tắt GitGuardian hay
  thêm ngoại lệ bỏ quét file.
- Mật khẩu container là giá trị ngẫu nhiên cho từng lần chạy, được mask trong
  GitHub Actions, truyền qua môi trường cho Docker và không lưu vào source.
  Container chỉ bind localhost, không có volume, được xóa trong `finally`.
- Chỉ deploy SQL sau khi server PostgreSQL cuối cùng nhận kết nối TCP và truy
  vấn được database test. Không dùng readiness qua Unix socket của server tạm
  trong entrypoint vì database có thể chưa được tạo.
- Regression BE-02A kiểm tra ngay entity vé đang tracking sau INSERT: các
  trường do trigger sinh phải được đọc lại và `IsValidAt` phải trả false tại
  hoặc sau thời điểm hết hạn. Không gọi `ChangeTracker.Clear()` để che lỗi này.
- PR phải có title/commit theo Conventional Commits, `Closes #14`, phạm vi,
  bằng chứng test và quan hệ phụ thuộc PR #54. Merge BE-02A trước rồi đồng bộ
  với `dev`; không force-push nhánh đã chia sẻ.

Phạm vi review BE-02B là mapping/configuration finance, ticket inspection,
consent/guardian, trip suggestions, ticket changes, lost property và test tương
ứng. Các thay đổi BE-02A có PR #54 riêng. Tổng dòng boilerplate mapping/test lớn;
review theo từng nhóm trên và trao đổi với maintainer về giới hạn 400 dòng logic,
không coi số dòng thêm tự động là bằng chứng đã được miễn quy định.
