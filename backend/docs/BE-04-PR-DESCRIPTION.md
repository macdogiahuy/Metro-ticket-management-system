## Phạm vi

Issue: [#19 — BE-04 · Hồ sơ + xác minh ưu đãi, upload minh chứng, audit thay đổi](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/19) (UC05, UC06).

PR triển khai chỉnh sửa hồ sơ hành khách, khóa thông tin định danh sau xác minh và cho phép tải tối đa ba minh chứng (5 MiB mỗi tệp) để Admin duyệt. Lịch sử quyết định, audit và thông báo được lưu cùng transaction. Nhánh đã merge `origin/dev` tại `a70125b`, giữ các endpoint hồ sơ, catalog và payment BE-08, đồng thời bổ sung tài liệu API và Postman collection.

## Quyền truy cập và xử lý lặp

- Nhóm `/api/v1/me` dùng `RequireAuthorization()`. Nhóm `/api/v1/eligibility` dùng `RequireAuthorization("Admin")`. Request chưa xác thực nhận **401**, tài khoản đã xác thực thiếu quyền Admin nhận **403**, và hành khách truy cập hồ sơ ưu đãi của người khác nhận **404**.
- Submit, upload, quyết định và job lifecycle dùng chung `pg_advisory_xact_lock(hashtextextended(userId, 4))` để tuần tự hoá các thay đổi theo hành khách. Submit trùng hồ sơ đang mở và chuyển trạng thái không hợp lệ trả **409**. Cập nhật hồ sơ lặp lại với dữ liệu giống nhau không tạo thêm audit.
- Audit hồ sơ lưu tên trường đã đổi và hash trước/sau. Log retry của job purge chỉ ghi loại lỗi, không ghi PII, nội dung tệp, object key hoặc thông tin xác thực.

## Integrity của tác vụ purge

Bản sửa trong commit `41e65d0` lưu tác vụ purge vào outbox migration 009 trong cùng transaction với thao tác gỡ tham chiếu file. Khi commit thành công, worker mới xóa file. Job giữ lại tác vụ nếu storage lỗi và tiếp tục thử sau. Các ca rollback trước commit, retry khi xóa dở và nhiều worker chạy đồng thời có test hồi quy.

## Đồng bộ dev và kiểm thử local ngày 22/11/2026

- [x] Merge `origin/dev` (`a70125b`, đã gồm BE-08) vào nhánh BE-04 từ `cf003fd`. `Program.cs` giữ đăng ký payment gateway và cả profile/catalog/payment endpoints; `DependencyInjection.cs` giữ profile lifecycle cùng payment options/service; Swagger contract giữ cả kiểm tra profile và payment. Không còn file unmerged.
- [x] Restore dependency mới từ dev; build Release với `--no-restore --warnaserror` đạt **0 warning, 0 error**.
- [x] `Run-PostgresTests.ps1 -NoBuild -NoRestore` đạt **100/100**: **40 unit + 60 integration**, 0 fail, 0 skip, trên PostgreSQL **17.12** riêng. Bao gồm 3 integration test payment và 2 Swagger contract test; runner báo riêng 32 test PostgreSQL theo danh sách lớp sẵn có (chưa tính payment và Swagger).
- [x] `python infra/database/build_deploy.py --check` đạt; deploy sạch **001–009** và `09_verify.sql` đạt trên database local riêng. Giữ nguyên migrations 007–009 và checksum 008 đã deploy.
- [x] Frontend: `npm.cmd run typecheck`, `npm.cmd test` (**1/1**) và `npm.cmd run build` đều đạt.
- [ ] **CI của head mới chưa được xác minh**: chỉ thực hiện local, chưa push hoặc cập nhật PR trên GitHub. Cần `backend-build` và `frontend-build` chạy trên đúng head sau khi push; GitGuardian hoặc CI của head cũ không thay thế hai job này. Trạng thái DIRTY trên GitHub chỉ được đánh giá lại sau khi nhánh PR được cập nhật.

## Bằng chứng kiểm thử trước khi merge dev

- [x] Unit tests: **39/39** đạt.
- [x] Integration tests: **57/57** đạt, gồm **32** test trên PostgreSQL thật; 0 lỗi, 0 skip.
- [x] Build successful: `dotnet build backend/Metro.sln --configuration Release --no-restore --warnaserror` đạt, 0 warning, 0 error.

Chạy lại ngày **21/11/2026** trên PostgreSQL **17.12** riêng bằng `Run-PostgresTests.ps1 -NoBuild -NoRestore`: **96/96** test, 0 skip. Trên Windows sandbox, đặt `Logging__EventLog__LogLevel__Default=None` cho tiến trình test vì tài khoản không có quyền ghi Event Log. CI và HTTP smoke test có trạng thái riêng bên dưới; không dùng kết quả local để đánh dấu CI của head mới.

Package SQL khớp source; deploy sạch 001–009 và nâng cấp 008 → 009 đều qua script xác minh mới. Cả bốn ca thiếu/chưa validate từng constraint đều bị từ chối, kể cả khi có constraint cùng tên trên bảng khác; rollback giữ schema test nguyên vẹn.

## Bằng chứng kiểm thử và trạng thái phát hành

- [x] Liên kết issue và UC, mô tả phạm vi cùng quy tắc 401/403, idempotency và tránh log PII.
- [x] Build baseline `783a475` đạt 0 warning, 0 error. **93/93** test đạt gồm 39 unit và 54 integration, 0 skip. Kiểm tra SQL và nâng cấp baseline đã đạt trên database riêng. [Kết quả M01 xác minh trên baseline](https://github.com/macdogiahuy/Metro-ticket-management-system/pull/64#pullrequestreview-5989544838).
- [x] Các job CI của baseline `783a475` đã đạt. [Backend CI](https://github.com/macdogiahuy/Metro-ticket-management-system/actions/runs/44700751655/job/136168257945) · [Frontend CI](https://github.com/macdogiahuy/Metro-ticket-management-system/actions/runs/44700751889/job/136168258293).
- [x] Checkout local với bản sửa outbox đạt **96/96** gồm 39 unit và 57 integration, trong đó 32 test chạy trên PostgreSQL thật. 0 lỗi, 0 skip. Build Release đạt 0 warning, 0 error. Package SQL khớp source, deploy sạch đến 009 và nâng cấp 008 → 009 đều qua `09_verify.sql` trên database test riêng.
- [x] Neon production đã lên **001–008** lúc 22:54 ngày 20/11/2026 (UTC+7), sau khi được cấp quyền và xác minh backup riêng. Checksum 007/008 khớp source, ba bảng mới và kết nối backend đã được kiểm tra trực tiếp. Backup trước deploy vẫn ở 006. Đây là kết quả deploy DB production, tách biệt với kết quả CI.
- [x] Neon production đã lên **001–009** lúc 23:33 ngày 20/11/2026 (UTC+7), sau khi được cấp quyền, xác minh backup riêng ở 008 và chạy thử trên một branch sao chép. `09_verify.sql`, checksum, bảng purge và quyền của tài khoản backend đều đã xác minh. Backup 008 vẫn được giữ để phục hồi nếu cần.
- [x] Giữ nguyên checksum 008 đã deploy (`a6f4a8c072a6f9a6f0643755eb28ff583c961c6ed1591aaa2b62ac88d38be34e`). Matcher `pg_get_constraintdef LIKE '%status%'` là rủi ro bảo trì còn lại của migration lịch sử. `09_verify.sql` kiểm tra hai CHECK constraint đã validate theo tên `ck_profile_eligibility_status` và `ck_profile_eligibility_decision` trên đúng bảng. Migration mới chỉnh các check này phải nhắm trực tiếp theo tên.
- [ ] Cập nhật mô tả này lên PR và chạy lại CI trên head mới sau khi được phép push.
- [ ] Phát hành backend chứa worker mới và chạy smoke test HTTP sau khi bản sửa có trên nhánh PR và qua CI.

## Ngoại lệ quy mô PR — chờ M01 xác nhận

Diff tại commit `41e65d0` so với merge-base của `origin/dev`: **+2.121 / −5 dòng, 23 file** (`git diff --stat origin/dev...41e65d0`). Sau merge dev `a70125b` và cập nhật tài liệu lần này: **+2.177 / −5 dòng, 23 file**. Khi cập nhật PR, đối chiếu bằng `git diff --stat origin/dev...HEAD` và gắn với SHA của head thực tế.

Phân loại 2.121 dòng thêm để M01 đối chiếu:

| Nhóm | Dòng thêm |
| --- | ---: |
| C# trong `backend/src` (gồm cấu hình và entity) | 749 |
| Test và test runner | 756 |
| Migration 007–009 | 94 |
| `deploy.sql` sinh từ migration | 145 |
| Tài liệu, Postman và script xác minh SQL | 377 |

PR vượt giới hạn **400 dòng logic**; bảng trên là số dòng diff, chưa phải phép đo dòng logic. Đề nghị M01 xem xét ngoại lệ vì endpoint, transaction/audit, worker purge và migration cần được review cùng nhau để kiểm tra tính toàn vẹn. Test, migration và file sinh làm tăng tổng diff nhưng không tự động miễn trừ giới hạn DoD.

- [ ] **M01 chấp nhận ngoại lệ trước merge** và ghi rõ phạm vi/head được chấp nhận bằng review hoặc comment trên PR.
- Bằng chứng chấp thuận: **chưa có link xác nhận ngoại lệ**. Review baseline và CI xanh không thay thế quyết định này.
