# BE-02A — EF Core mapping lõi và cô lập dữ liệu theo nhà vận hành

Ngày cập nhật: **17/11/2026**. Người thực hiện: **Tuyên (@hinouser29)**. Reviewer kỹ thuật theo phân công: **Thành (@22-LeDaiThanh)**; approval của M01 theo quy định nhóm.

## 1. Mục tiêu và trạng thái bàn giao

BE-02A cung cấp lớp ánh xạ dữ liệu cho backend: từ các entity C# sang bảng PostgreSQL hiện hành, kèm quy tắc thời gian EndExclusive và giới hạn dữ liệu theo `operator_id`. Đây là nền tảng để các task API sau sử dụng; không phải chức năng mua vé hoặc soát vé hoàn chỉnh.

- Phạm vi: identity, network lõi, orders, order_versions, tickets và các bảng phụ cần thiết.
- Nền kỹ thuật: .NET 9, EF Core 9, Npgsql và PostgreSQL 17; giữ nguyên phiên bản package/framework của repo.
- Schema nghiệp vụ thuộc `metro`; ASP.NET Identity giữ bảng `public."AspNet*"` với khóa người dùng kiểu chuỗi.
- Schema SQL chính thức được quản lý bởi migrations **001–006**. Không tạo EF migration hoặc dùng `EnsureCreated()` để thay schema đó.
- Code đã push trước review: commit `093c567d8c3fe62d9551b4e55758b09b35027493`, nhánh `feature/be-02a-ef-core-mapping`.
- Hai nhận xét review sau commit trên đã được sửa và kiểm thử tại worktree `Metro-BE02A-publish`, commit **`3a458a9818c6b74c1872ba3b715624932c04ce3f`**. Tài liệu bàn giao này cùng hai file SRS/DDS bổ sung được đưa vào một commit tài liệu riêng; PR do người thực hiện tạo sau khi push.
- Không dùng tài liệu này để xác nhận PR đã được approve/merge hoặc task đã chuyển Done.

## 2. Chức năng đã triển khai

### 2.1. Ánh xạ entity với PostgreSQL

Model đăng ký **27 entity nghiệp vụ** và kế thừa model ASP.NET Identity. Các configurations xác định tên schema/bảng/cột, khóa chính, composite key/FK, kiểu dữ liệu, nullability, default, index liên quan và delete behavior.

| Nhóm | Bảng được map | Mục đích |
| --- | --- | --- |
| Identity/access — 11 bảng | `operators`, `banks`, `user_profiles`, `auth_sessions`, `email_verification_challenges`, `operator_accounts`, `business_roles`, `permissions`, `role_permissions`, `account_role_assignments`, `audit_logs` | Nhà vận hành, hồ sơ, phiên/xác minh và dữ liệu phân quyền/audit |
| Network lõi — 9 bảng | `lines`, `stations`, `line_stations`, `service_calendars`, `schedule_versions`, `planned_trips`, `planned_stop_times`, `actual_trips`, `actual_stop_times` | Tuyến, ga, lịch và mốc thời gian chuyến dự kiến/thực tế |
| Order/pricing — 5 bảng | `ticket_products`, `fare_policy_versions`, `orders`, `order_items`, `order_versions` | Tham chiếu sản phẩm/giá, nội dung đơn và phiên bản đơn |
| Ticket — 2 bảng | `tickets`, `ticket_credentials` | Dữ liệu vé, hiệu lực và credential đã hash |

Các bảng phụ như bank, quyền, stop time, product/fare version, order item và credential cần thiết để các quan hệ lõi hoạt động. Mapping các bảng này không đồng nghĩa triển khai API hoặc nghiệp vụ của những task khác.

Các điểm cần lưu ý:

- JSON snapshot map sang `jsonb`; ngày nghiệp vụ dùng `DateOnly`; thời điểm dùng UTC; mảng và các trường số giữ theo schema SQL.
- Composite FK có `operator_id` ngăn liên kết giữa bản ghi thuộc hai nhà vận hành khác nhau.
- Các cột bổ sung ở migration 006 đã được map: thông tin ngày/version/ưu đãi của đơn, thời gian chuyển tuyến/chạy và trạng thái boarding.
- `ActualStopTime.Source` tương ứng cột `source` bắt buộc, với tập giá trị DB cho phép: `MANUAL`, `OCC_IMPORT`, `SIMULATION`. Không map `dwell_seconds` hoặc `irregularity` vào bảng này vì schema không có hai cột đó.
- `planned_trips.load_profile_id` chỉ là scalar nullable; không tạo entity/navigation tới capacity legacy. FK của tham chiếu này vẫn do SQL chính thức quản lý.
- Các constraint/trigger SQL tiếp tục do bộ migration quản lý; không khai báo lại toàn bộ bằng Fluent API.

### 2.2. Quy tắc EndExclusive và UTC

Khoảng hiệu lực dùng quy ước **[start, end)**: tính tại đúng start, không tính tại đúng end. Hai khoảng tiếp giáp không chồng lấn.

| Trường hợp | Hành vi |
| --- | --- |
| `TimeWindow` có đủ hai cận | End phải lớn hơn start; bằng nhau hoặc đảo thứ tự bị từ chối |
| Ticket thiếu start | Không giới hạn cận dưới, nhưng vẫn kiểm tra end |
| Ticket thiếu end | Không giới hạn cận trên, nhưng vẫn kiểm tra start |
| Ticket thiếu cả hai cận | Khoảng không giới hạn; `IsValidAt` vẫn giữ điều kiện trạng thái vé hiện hành |
| `DateWindow` có start bằng end | Khoảng rỗng hợp lệ, không chứa ngày nào và không overlap |
| `DateWindow` đảo thứ tự | Ném `ArgumentException` |
| Timestamp `Utc` | Giữ nguyên thời điểm |
| Timestamp `Local` | Chuyển bằng `ToUniversalTime()` theo Kind đã được khai báo |
| Timestamp `Unspecified` | Từ chối; không gắn nhãn UTC thay cho chuyển timezone |

Ngày dịch vụ không được suy diễn từ timezone của máy chạy. Với vé lượt, trigger SQL tính đầu/cuối ngày trong `Asia/Ho_Chi_Minh`. Ví dụ ngày 16/11/2026 tương ứng khoảng UTC từ **15/11 17:00Z** đến **16/11 17:00Z**, với cận cuối exclusive.

Helper thời gian hỗ trợ kiểm tra hiệu lực; không triển khai lifecycle phát hành, kích hoạt hoặc thu hồi vé.

### 2.3. Cô lập dữ liệu theo operator

`IOperatorContext` và `ApplicationDbContext` được đăng ký scoped. Query filter đọc operator của context hiện tại, không chốt operator của request đầu tiên trong model cache.

- Context có operator A chỉ truy vấn được dữ liệu scoped của A.
- Thiếu operator hoặc `Guid.Empty`: truy vấn scoped trả rỗng; ghi scoped bị từ chối.
- Không mặc định operator NULL là admin/system; chưa có chức năng bypass đọc xuyên operator.
- `SaveChanges`/`SaveChangesAsync` kiểm tra operator trên entity thêm/sửa/xóa; không cho context A ghi graph hoàn toàn thuộc B hoặc đổi operator của bản ghi cũ.
- `OperatorId` là concurrency token để UPDATE/DELETE chứa điều kiện operator. Điều này chặn trường hợp attach ID của bản ghi B nhưng khai operator A trên entity local.
- Các bảng Identity/reference dùng chung không bị áp filter tùy tiện. Audit có operator nullable vẫn chịu chính sách fail closed của context thông thường.

**Giới hạn tích hợp:** context phải được gán bởi phía gọi đáng tin cậy sau khi kiểm quyền. BE-02A không triển khai nguồn claim/JWT hoặc lấy operator từ raw header/query. Query filter không thay thế authorization và không tự bảo vệ các lệnh SQL ngoài đường truy cập EF này.

### 2.4. Nhận lại giá trị do trigger sinh khi INSERT vé

Migration 006 dùng trigger `fill_ticket_travel_context` để điền dữ liệu còn thiếu. Bảy property được cấu hình `ValueGeneratedOnAdd()`:

- `EffectiveFrom`, `ExpiresAt`.
- `OrderId`, `OrderVersion`, `ServiceDate`.
- `OriginStationId`, `DestinationStationId`.

Với giá trị chưa được phía gọi cung cấp, EF đọc kết quả PostgreSQL trả về sau INSERT vào chính entity đang tracking. Không cần `ChangeTracker.Clear()` để vé có bounds đúng. Những bounds được cung cấp tường minh cho vé khác vẫn được giữ.

Regression kiểm tra ngay sau `SaveChangesAsync`, xác nhận truy vấn tracking trả cùng instance và `IsValidAt` trả false đúng end cũng như bốn ngày sau end.

## 3. Danh sách file và trách nhiệm

Các đường dẫn bên dưới tính từ gốc repo. Danh sách bao gồm code ban đầu của BE-02A và phần sửa review, không chỉ ba file sửa cuối.

### 3.1. Domain/common

Thư mục `backend/src/Metro.Domain/Common/`:

| File | Trách nhiệm |
| --- | --- |
| `TimeWindow.cs` | Khoảng thời gian với cận nullable, contains/overlap, chuẩn hóa Kind và từ chối Unspecified |
| `DateWindow.cs` | Khoảng ngày exclusive, hỗ trợ khoảng rỗng |
| `IOperatorScoped.cs` | Đánh dấu entity có operator bắt buộc |
| `IOptionalOperatorScoped.cs` | Đánh dấu entity có operator nullable |

### 3.2. Entity nghiệp vụ

Mỗi tên dưới đây tương ứng một file `.cs` trong thư mục đã chỉ định.

| Thư mục | File/entity | Trách nhiệm |
| --- | --- | --- |
| `backend/src/Metro.Domain/Entities/Identity/` | `Operator.cs`, `Bank.cs`, `UserProfile.cs`, `AuthSession.cs`, `EmailVerificationChallenge.cs`, `OperatorAccount.cs`, `BusinessRole.cs`, `Permission.cs`, `RolePermission.cs`, `AccountRoleAssignment.cs`, `AuditLog.cs` | Dữ liệu identity/access; helper hiệu lực phiên, challenge và phân quyền |
| `backend/src/Metro.Domain/Entities/Network/` | `Line.cs`, `Station.cs`, `LineStation.cs`, `ServiceCalendar.cs`, `ScheduleVersion.cs`, `PlannedTrip.cs`, `PlannedStopTime.cs`, `ActualTrip.cs`, `ActualStopTime.cs` | Dữ liệu tuyến/ga/lịch/chuyến và helper hiệu lực |
| `backend/src/Metro.Domain/Entities/Orders/` | `TicketProduct.cs`, `FarePolicyVersion.cs`, `Order.cs`, `OrderItem.cs`, `OrderVersion.cs` | Sản phẩm, giá tham chiếu và dữ liệu đơn/phiên bản |
| `backend/src/Metro.Domain/Entities/Tickets/` | `Ticket.cs`, `TicketCredential.cs` | Dữ liệu vé/credential và helper validity |

### 3.3. Application và Infrastructure

| File | Trách nhiệm |
| --- | --- |
| `backend/src/Metro.Application/Abstractions/MultiTenancy/IOperatorContext.cs` | Hợp đồng operator context cho lớp ứng dụng |
| `backend/src/Metro.Infrastructure/Persistence/MultiTenancy/OperatorContext.cs` | Lưu operator hiện tại trong scope |
| `backend/src/Metro.Infrastructure/DependencyInjection.cs` | Đăng ký operator context scoped cùng DbContext; các cấu hình storage/Identity có sẵn được giữ nguyên |
| `backend/src/Metro.Infrastructure/Persistence/Context/ApplicationDbContext.cs` | DbSet, đăng ký configurations, query filter, write guard và concurrency token |
| `backend/src/Metro.Infrastructure/Persistence/Configurations/IdentityConfigurations.cs` | Mapping 11 entity identity/access |
| `backend/src/Metro.Infrastructure/Persistence/Configurations/NetworkConfigurations.cs` | Mapping 9 entity network và composite FK |
| `backend/src/Metro.Infrastructure/Persistence/Configurations/OrderConfigurations.cs` | Mapping 5 entity order/pricing, JSONB và cột migration 006 |
| `backend/src/Metro.Infrastructure/Persistence/Configurations/TicketConfigurations.cs` | Mapping ticket/credential và giá trị trigger sinh khi INSERT |

### 3.4. Kiểm thử và hướng dẫn chạy

| File | Nội dung |
| --- | --- |
| `backend/tests/Metro.UnitTests/EndExclusiveTests.cs` | Mốc start/end, khoảng tiếp giáp/rỗng, NULL bounds, trạng thái và UTC/Unspecified |
| `backend/tests/Metro.IntegrationTests/ModelMappingTests.cs` | Metadata Npgsql, bảng/schema, khóa Identity, cột/default, không shadow FK/capacity legacy, ValueGenerated.OnAdd |
| `backend/tests/Metro.IntegrationTests/OperatorQueryFilterTests.cs` | Hai context độc lập, thiếu/unknown operator, write guard và đổi operator |
| `backend/tests/Metro.IntegrationTests/PostgresLiveIntegrationTests.cs` | Round-trip DB thật, constraint, operator isolation, forged UPDATE/DELETE, range PostgreSQL và trigger với entity đang tracking |
| `backend/tests/Metro.IntegrationTests/Metro.IntegrationTests.csproj` | Thêm provider InMemory phục vụ test nhanh; không dùng thay PostgreSQL acceptance |
| `backend/tests/Metro.IntegrationTests/BE-02A-TESTING.md` | Thiết lập database test riêng, biến môi trường, cách chạy và ranh giới tích hợp |
| `docs/handover/BE-02A-EF-CORE-MAPPING.md` | Tài liệu chức năng và bàn giao này |

Đã bỏ các file scaffold rỗng `backend/src/Metro.Domain/Class1.cs`, `backend/src/Metro.Infrastructure/Class1.cs` và hai `UnitTest1.cs` trong các project test để thay bằng code/test thực. `backend/Directory.Build.props` từng được task thêm chỉ để suppress warning đã bị loại bỏ; không thay chính sách build của nhóm để làm kết quả xanh.

## 4. Khó khăn và cách xử lý

| Khó khăn thực tế | Ảnh hưởng | Cách xử lý |
| --- | --- | --- |
| Chỉ đối chiếu migration tạo bảng ban đầu | Thiếu các cột bổ sung của migration 006; ActualStopTime có cột không tồn tại | Đối chiếu schema cuối 001–006, bổ sung scalar mapping và bỏ mapping sai |
| Default không bằng 0 trên cột cho phép giá trị 0 | Có nguy cơ EF dùng default DB thay giá trị 0 khi INSERT | Cấu hình sentinel riêng cho min_transfer_seconds/travel_time_seconds; test cả INSERT và UPDATE giá trị 0 |
| NULL bounds và cận ngày bằng nhau | Vé có thể được coi còn hiệu lực sai; calendar hợp lệ theo SQL bị ném lỗi | Xét độc lập từng cận của ticket, hỗ trợ DateWindow rỗng, đối chiếu PostgreSQL range |
| Thiếu operator bị hiểu nhầm thành admin | Rủi ro đọc toàn bộ hoặc ghi dữ liệu operator khác | Fail closed, write guard và concurrency token; không thêm bypass ngầm |
| EF tracking không tự nhận giá trị trigger | Sau INSERT, vé vẫn giữ bounds NULL; query tracking tiếp tục trả instance sai | ValueGeneratedOnAdd cho bảy trường; regression trước Clear và trên cùng tracked instance |
| Fixture có hai stop cùng một tuyến | SingleAsync theo LineId ném lỗi, ngăn các assertion phía sau chạy | Lọc thêm StationId cho đúng stop ở bước UPDATE và assertion |
| Test live từng return khi thiếu/lỗi DB | Có thể báo pass mà không kiểm tra PostgreSQL | Skip tường minh ở lượt tùy chọn; chế độ bắt buộc hoặc DB đã cấu hình lỗi phải fail |
| Runtime/cấu hình DB khác nhau giữa các phiên terminal | Lượt test trước có 23 pass nhưng 2 PostgreSQL skip | Kiểm thử lại bằng .NET 9 trên PostgreSQL 17 disposable có schema chính thức; không coi skip là đạt |
| Lịch sử Git local/remote phân kỳ | Push non-fast-forward; pull/rebase máy móc có thể kéo lịch sử không mong muốn | Tạo worktree từ remote, cherry-pick đúng hai commit BE-02A rồi push fast-forward; giữ nguyên công việc thư mục cũ |
| Cảnh báo System.IO.Hashing 11.0.0 với net9.0 | Build thành công nhưng còn ba warning dependency | Báo trung thực, không nâng framework/package hoặc che warning ngoài phạm vi task |

## 5. Kết quả kiểm chứng gần nhất

Kết quả thực chạy sau khi sửa hai nhận xét review, ngày **17/11/2026**:

| Kiểm tra | Kết quả |
| --- | --- |
| Build backend | Thành công; 0 error; 3 warning dependency nêu trên |
| Unit tests | **17 pass, 0 fail, 0 skip** |
| Integration project, gồm PostgreSQL thật | **8 pass, 0 fail, 0 skip** |
| Tổng suite | **25 pass, 0 fail, 0 skip** |
| PostgreSQL acceptance | Hai test live đều chạy và pass trên PostgreSQL 17 với migrations 001–006 |
| Diff whitespace | `git diff --check` đạt cho bản sửa review |

Instance PostgreSQL được tạo riêng trong thư mục tạm, chỉ nghe localhost ở port riêng. Database test hiện dùng tiền tố `metro_test_`. Dữ liệu test là dữ liệu tổng hợp; transaction được rollback. Instance đã dừng sau kiểm thử. Không sử dụng hoặc reset database dùng chung/production.

Các lệnh kiểm thử chính, chạy từ gốc worktree:

```powershell
dotnet build backend/Metro.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test backend/Metro.sln --no-build --no-restore -m:1
```

Để hai test PostgreSQL thực sự chạy, cần cấu hình trong cùng phiên gọi test:

- `METRO_TEST_POSTGRES_CONNECTION_STRING`: connection tới DB test riêng; giữ kín giá trị.
- `METRO_TEST_POSTGRES_DATABASE`: tên chính xác, bắt đầu bằng `metro_test_`.
- `METRO_TEST_POSTGRES_REQUIRED=1`: thiếu cấu hình phải fail thay vì skip.

Tham khảo hướng dẫn chi tiết tại `backend/tests/Metro.IntegrationTests/BE-02A-TESTING.md`. Không lưu secret/connection string vào tài liệu hoặc commit.

## 6. Ranh giới chức năng và việc bàn giao còn lại

Không triển khai endpoint mới, frontend, payment/refund/inspection, consent/guardian, phát hành/soát vé hoàn chỉnh hoặc quản lý tải. Không map capacity legacy; không thêm RLS hay kiến trúc multi-tenancy mới. Swagger/Postman, test 401/403 và idempotency endpoint không áp dụng cho thay đổi chỉ gồm mapping/helper này.

Bản sửa review chỉ chạm ba file: `TicketConfigurations.cs`, `ModelMappingTests.cs`, `PostgresLiveIntegrationTests.cs`; tài liệu này là file bổ sung riêng. Những tài liệu cá nhân/untracked và conflict ở worktree cũ không thuộc phần bàn giao này.

Trước khi đóng task:

1. Xác minh commit sửa review và commit tài liệu có trên nhánh `feature/be-02a-ef-core-mapping`, rồi tạo PR vào `dev`; không đẩy trực tiếp dev/main.
2. Reviewer kiểm tra lại hai nhận xét bằng bằng chứng PostgreSQL thực; lấy approval theo quy định nhóm.
3. Rà giới hạn 400 dòng logic trên PR thực tế, không dùng số dòng toàn repo thay số dòng PR.
4. Cập nhật trạng thái task trong công cụ theo dõi sau khi đủ điều kiện; không tự xác nhận Done/merge.

## 7. Tài liệu SRS/DDS bổ sung trong lượt bàn giao

- `docs/requirements/Metro_SRS_v1.1.docx`: tài liệu SRS do người thực hiện bổ sung.
- `docs/requirements/Metro_DDS_v1.0_Complete_Review.docx`: tài liệu DDS do người thực hiện bổ sung.

Hai file Word được đưa lên để reviewer tham khảo. Lượt bàn giao này không sửa schema/migration, không đổi tiêu chí BE-02A theo nội dung tài liệu mới, và không xóa/thay thế bản `Metro_SRS_v1.2.docx` đã có trong lịch sử nhánh. Bản local tên `Metro_SRS_v1.0.docx` trùng byte với bản SRS v1.2 đang được Git quản lý nên không thêm bản trùng hoặc stage thao tác xóa/đổi tên đó.

## 8. Tài liệu và schema đối chiếu

- `docs/planning/TASK-ASSIGNMENT.md` — phạm vi/phân công/DoD hiện hành.
- `docs/git-workflow/BRANCH-POLICY.md` — quy tắc nhánh và PR.
- `infra/database/README.md` — cách triển khai bộ SQL.
- `infra/database/documentation/DATABASE_DESIGN.md` — thiết kế DB và ranh giới domain.
- `infra/database/migrations/001_identity_and_access.sql` — identity/access.
- `infra/database/migrations/002_network_operations.sql` — network.
- `infra/database/migrations/003_ticketing_finance_capacity.sql` — order/ticket/pricing nền.
- `infra/database/migrations/004_support_analytics_automation.sql` và `005_transactional_capacity_retention.sql` — các thay đổi SQL trong thứ tự triển khai chính thức, không đồng nghĩa phải map toàn bộ domain của chúng trong BE-02A.
- `infra/database/migrations/006_srs_v1_2_alignment.sql` — cột bổ sung, order versions, trigger travel context và ràng buộc hiệu lực vé.
