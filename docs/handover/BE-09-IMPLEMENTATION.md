# BE-09 — Vé, QR và lịch sử

Cập nhật 22/11/2026 (Asia/Saigon). Task [#25](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/25), PB-009. Nhánh `feature/be-09-ticket-issuance`, nền `origin/dev` tại `a70125bf609bba473618c3efd58e9e68aed4e246`. Nguồn nghiệp vụ: SRS v1.2, UC v1.6, kế hoạch BE-09 và contract SQL BE-08/BE-09.

Code nằm tại `C:\Users\ACER\Documents\Codex\Metro-BE09-review-20261119`. Checkout cũ có cherry-pick conflict được giữ nguyên. Tài liệu này thay các đề xuất tích hợp trong `docs/planning/BE-09/CONTRACT-DRAFT.md`: BE-08 **không gọi service C# BE-09**.

## 1. Luồng phát hành

1. BE-07 chuẩn bị đơn đã xác nhận, đúng một người, một item, `passenger_count=1`, phiên bản `CONFIRMED`, snapshot nhất quán. Vé lượt chọn ngày và hai ga, không giữ chỗ hoặc gắn bắt buộc với một planned trip. Ngày mua đến tối đa ngày mua +7, theo UTC+7.
2. BE-08 kiểm chứng thông báo cổng và gọi đúng `metro.confirm_payment_and_issue_tickets` như trước. Migrations 007–009 giữ nguyên tám tham số và kiểu trả về `integer`; không bỏ kiểm chữ ký đã verify, amount/currency, quyền sở hữu event và idempotency. Cờ boolean không thay việc xác minh chữ ký ở BE-08.
3. SQL lưu Payment `SUCCEEDED`, Order `PAID`, tạo đúng một Ticket `ISSUED` và một job `PENDING` trong cùng transaction. Giá/discount lấy snapshot đã chốt, không tra lại bảng giá hiện tại. C# không insert Ticket trả phí lần nữa.
4. Worker BE-09 hoàn thiện QR, rồi commit credential `ACTIVE`, Ticket `ACTIVE`, job `READY`, Order `ISSUED` cùng `issued_at` trong một transaction. `ISSUED` trên bản ghi Ticket trước bước này **không phải vé đã sẵn sàng sử dụng**; `Ticket.IsValidAt` chỉ nhận `ACTIVE/IN_USE`. Cổng phải kiểm cả current credential và điều kiện Journey, không chỉ gọi helper kiểm thời gian.
5. Vé lượt tương lai có bản ghi chờ nhưng không có QR trước 00:00 ngày đi. Job bền vững được poll mỗi 15 giây; mỗi lần lỗi đặt `next_attempt_at` cách một phút. Sau 15 phút từ `first_failed_at`, job chuyển `NEEDS_SUPPORT`, không tự yêu cầu trả lại tiền hoặc kéo dài hạn.

Khóa `(operator, beneficiary)` lấy trước khóa payment/order/item; BE-08 giữ thứ tự payment → order → item bên trong. Worker lấy cùng khóa beneficiary trước order/job/ticket. Candidate được đọc trước, rồi kiểm lại trạng thái dưới khóa; hai worker không tạo hai credential. Các luồng đổi quyền của BE-07/10/11 phải dùng cùng quy ước khóa và kiểm overlap khi cần, không cập nhật chồng bằng SQL rời rạc.

Giữ quyền của Ticket `ISSUED/ACTIVE/IN_USE`, đơn đã trả hoặc xác nhận miễn phí chờ cấp, và Journey `OPEN/MANUAL_REVIEW`. Khoảng thời gian dùng `[start,end)`: hai ngày liên tiếp không xung đột; vé hết hạn còn Journey mở vẫn chặn quyền mới.

## 2. Miễn phí dưới 6

- POST chỉ nhận `orderVersion`; máy chủ không tin beneficiary/price/isPaid từ client. Người thao tác phải có quan hệ giám hộ đúng trẻ, được duyệt và còn hiệu lực. Không có role Guardian riêng và không cấp quyền mặc định cho mọi nhân viên.
- Ngày sinh và email của trẻ đã xác minh; tuổi tính theo ngày đi UTC+7. Đúng sinh nhật 6 tuổi bị từ chối. Không tự chuyển sang vé trả phí; trẻ dưới 6 cũng không được phát hành vé trả phí/định kỳ qua SQL paid workflow.
- Đơn có đúng vé lượt, giá gốc >0, giảm toàn bộ, tổng =0. Có Payment bất kỳ thì từ chối nhánh miễn phí. Gọi `metro.issue_free_order`; không gọi cổng, không tạo Payment, `paid_at`, `payment_started_at` hoặc dấu đã thu giả.
- Trẻ/giám hộ có hai đơn riêng. Giám hộ có vé lượt đã trả cùng ngày/cặp ga, kể cả đang chờ QR tương lai; hoặc pass còn hiệu lực vào ngày sử dụng. Vé trẻ lưu `related_ticket_id`, `guardian_link_id`, `guardian_user_id`; liên kết ngược truy vấn qua `related_ticket_id`, không ép một vé tháng chỉ liên kết với một đơn trẻ suốt tháng.
- Trước cấp QR trẻ, worker kiểm lại đúng quan hệ/vé giám hộ đã liên kết và QR giám hộ sẵn sàng. Quét cặp, cùng có mặt và giới hạn một trẻ mỗi hành trình thuộc BE-10; BE-09 không tự ghi Journey.
- `Idempotency-Key` scope operator + actor + operation, 8–120 ký tự ASCII hiển thị. Cùng key/nội dung trả cùng nguồn vé; khác nội dung trả 409. Khác key vẫn không tạo thêm Ticket từ cùng đơn.

## 3. API cho frontend

Tất cả endpoint cần Bearer JWT. Query `operatorCode` tùy chọn, cùng operator với catalog/checkout. Bỏ query thì dùng active operator mặc định như catalog. Mã operator chỉ chọn phạm vi, không cấp quyền. Không cần tạo `operator_accounts` cho mọi Passenger.

| Method / `/api/v1` | Contract |
| --- | --- |
| `GET /tickets` | Vé chủ sở hữu và trẻ trong quyền giám hộ; `page=1`, `pageSize=20`, tối đa 100. `total`, `items`, trạng thái sử dụng tách `issuanceStatus`, `qrAvailable`. |
| `GET /tickets/{id}` | `ticket` và `tripSuggestions`; loại vé, ga, ngày đi, `effectiveFrom`, `endExclusive`, nhóm ưu đãi snapshot. Chuyến gợi ý không phải reservation. BE-06/07 tạo gợi ý; BE-09 chỉ đọc. |
| `GET /tickets/{id}/qr` | Binary `image/png`, `Cache-Control: no-store, private`; GET không ghi credential. Chưa sẵn/hết quyền vào trả 409. Vé `IN_USE` có Journey mở vẫn lấy được QR để kết thúc lượt cũ, không cho phép vào lượt mới. |
| `GET /orders` | Chỉ Buyer; gồm giá, người đi snapshot, loại vé, trạng thái, mốc thu/cấp, các phiên bản nội dung. Không trả QR người nhận. |
| `GET /payments` | Chỉ Buyer; lần thử/thu, đối soát, refunds trong quyền. Không suy ra đã thu từ trạng thái chưa rõ, không trả payload cổng hoặc payout snapshot. |
| `POST /orders/{id}/issue-free` | Body `{"orderVersion":1}`, header `Idempotency-Key`. 202 = xác nhận miễn phí/chờ worker; 200 = đã phát hành trước đó. Không nhận client chỉ định actor hoặc giá. |

Lịch sử đơn/thanh toán mặc định 30 ngày lịch đến hôm nay, 20 dòng/trang. Cho lọc `fromDate`, `toDate` dạng `YYYY-MM-DD`, bao gồm ngày kết thúc theo UTC+7; DB query chuyển sang upper bound exclusive. Sắp theo thời gian giảm dần rồi ID để phân trang ổn định.

Lỗi có `code` ổn định trong ProblemDetails: 400 validation/header, 401 JWT, 403 thiếu quan hệ, 404 tài nguyên không có/ngoài ownership, 409 overlap/version/idempotency/QR chưa dùng được, 503 tạm thời/key hoặc integrity. Không trả raw exception SQL. BE-08 cũng resolve operator từ đơn/payment thuộc Buyer, không đòi membership nội bộ; create/get/retry của người khác trả 404. Các API “của tôi” không mở quyền xem toàn bộ dữ liệu chỉ vì có role Admin; giao diện quản trị dùng policy/endpoint quản trị tương ứng.

Swagger có sáu route, Bearer, header bắt buộc và PNG binary. Postman có folder **BE-09 Tickets and history**; điền `operatorCode`, `accessToken`, `orderId`, `orderVersion`, `ticketId`. Khi thử lại cùng đơn giữ `ticketIssueKey`; khi đổi đơn xóa biến này để sinh key mới.

## 4. QR và cấu hình

QR dùng QRCoder 1.8.1 để kết xuất PNG. Token:

```text
M1.<keyId>.<operatorId:N>.<ticketCorrelationId:N>.<credentialId:N>.<version>.<HMAC-SHA256 lowercase hex>
```

Không có tên, email, ngày sinh trong token. DB chỉ lưu SHA-256 hex của token, version và key ID. Cùng credential/key tái tạo cùng token/PNG qua restart. GET kiểm lại hash bằng so sánh constant-time. BE-10 đối chiếu operator, ticket reference, current credential, version/hash, trạng thái và Journey; không xác thực offline chỉ bằng việc đọc được QR.

Các biến môi trường, không commit giá trị thật:

```dotenv
TicketQr__ActiveKeyId=v1
TicketQr__Keys__v1=<base64 của ít nhất 32 byte ngẫu nhiên>
TicketQr__WorkerEnabled=true
```

Giữ secret ổn định trong secret store/cấu hình môi trường. Có thể tạo giá trị cục bộ bằng `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`, rồi lưu bằng cơ chế secret của nhóm. Không sinh lại mỗi lần boot. Đổi active key cần giữ key cũ trong key ring để tái tạo QR cũ. Ví dụ thêm `TicketQr__Keys__v2`, đặt ActiveKeyId=v2, vẫn giữ Keys__v1. Không có key hợp lệ thì job retry/NeedsSupport; không phát hành QR không ký hoặc tự dùng khóa ngẫu nhiên.

Chỉ một signed QR ACTIVE/vé và version duy nhất. Credential legacy không có `key_id` không thể tái tạo từ hash: worker chuyển hỗ trợ, không đoán token hoặc tự thay mã. Quy trình thay QR thuộc BE-11; phải thu hồi mã cũ, tăng version, gắn `old.replaced_by_id=new.id`, giữ Journey. Thu hồi quan hệ chặn quyền QR mới; cặp đã vào hợp lệ giữ quyền tối thiểu để ra.

## 5. Database và quyền runtime

- Thêm **007–009**, giữ nguyên file/checksum 001–006. Bổ sung `payments.order_version` đóng băng khi tạo payment và FK vào order_versions; nội dung phiên bản đã xác nhận không được sửa tại chỗ.
- Thêm credential version/key ID và partial unique indexes; hai bảng `ticket_issuance_jobs`, `ticket_issue_requests`; trạng thái Order `PENDING_ISSUE` cho miễn phí chờ cấp. Vé trả phí chờ cấp vẫn `PAID`.
- Gỡ yêu cầu legacy “có ga thì bắt buộc planned_trip”; giữ cặp ga đầy đủ, khác nhau và FK cùng operator. Không map hoặc dùng quota/capacity legacy trong BE-09.
- Vé Day/Week/Month bắt đầu PaidAt bất biến, kết thúc 00:00 sau ngày lịch thứ 1/7/30. Worker chậm không dời hiệu lực.
- Worker ghi audit và enqueue notification `TICKET_ISSUED`/`TICKET_NEEDS_SUPPORT` một lần mỗi ticket/người nhận/kênh. Payload chỉ có order/ticket ID; không nhúng QR hoặc thông tin người nhận. BE-14 xử lý template và delivery; lỗi gửi không rollback vé/thu tiền.

Triển khai theo quy trình chung:

```powershell
python infra/database/build_deploy.py --check
psql -X -v ON_ERROR_STOP=1 -f infra/database/deploy.sql
psql -X -v ON_ERROR_STOP=1 -f infra/database/scripts/09_verify.sql
```

Chọn DB và credential qua cấu hình của môi trường, không chép URL chứa mật khẩu vào lệnh hoặc source. Chưa chạy 007–009 trên Neon chung trong lượt triển khai code này. Backup/branch và review migration trước khi áp dụng môi trường dùng chung theo quy trình nhóm.

Nếu runtime khác migration owner, DBA cấp `USAGE ON SCHEMA metro`, SELECT cho các bảng đọc theo service; EXECUTE đúng chữ ký paid/free và helpers cho service tương ứng. Worker cần INSERT/UPDATE trên jobs, credentials, tickets, orders, audit_logs, notifications; adapter free cần INSERT/SELECT ticket_issue_requests. Không cấp DELETE/TRUNCATE hoặc quyền quản lý schema cho runtime. Các quyền cũ chỉ gọi SQL workflow chưa đủ cho worker mới; không chạy app bằng migration owner để né cấu hình quyền.

## 6. Kiểm chứng và trạng thái bàn giao

Test thực chạy trên PostgreSQL 17 riêng `metro_test_be09_tuyen`, localhost; không dùng Neon chung, EF InMemory hoặc Docker thay PostgreSQL. Dùng harness `backend/tests/Run-PostgresTests.ps1` và biến môi trường test hiện có. Test không cần secret thật; key ngẫu nhiên chỉ dùng trong test host, worker được gọi chủ động để kiểm soát lịch.

Bao phủ: SQL paid/free, một vé/người/đơn, replay và race hai connection/worker, hai đơn overlap và ngày liên tiếp, Journey hết hạn chưa đóng, tuổi tại ngày đi, single-only, guardian/QR tương lai, hash/key/version, PNG ổn định, retry/NeedsSupport không gia hạn, 401/403/404/cross-operator, lịch sử và Swagger. Kết quả thực thi cuối được ghi trong `BE-09-VALIDATION.md` cùng thư mục.

Theo yêu cầu người dùng, toàn bộ thay đổi dùng một nhánh `feature/be09-08-integration-handover` và PR #77 vào dev. Giữ tám commit làm lịch sử review; không dùng các nhánh/PR trung gian nữa. PR tổng không được coi là dưới 400 dòng. Chưa tự merge hoặc ghi thay reviewer đã duyệt. Xem BE-09-FINAL-2026-11-22.md.

Luồng end-to-end mua đơn → MoMo TEST → webhook → phát hành → frontend còn phải ghép code BE-06/07/08/10/14 của các thành viên. Fixture xác nhận được phần BE-09, không thay bằng chứng tích hợp gateway hoặc scan thật. BE-08 phải hiểu integer trả về là số Ticket SQL tạo/đã có, **không đồng nghĩa QR đã sẵn sàng**; đọc Order/job thay vì tự ghi Order ISSUED. Các lỗi thu tiền đã xảy ra nhưng SQL từ chối cấp cần đi vào đối soát/ngoại lệ tài chính của BE-08, không tự đánh dấu đã hoàn.
