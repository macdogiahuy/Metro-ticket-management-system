## Phạm vi

Liên kết [issue #19](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/19), UC05 và UC06. PR triển khai chỉnh sửa hồ sơ hành khách, khóa thông tin định danh sau xác minh và cho phép tải tối đa ba minh chứng (5 MiB mỗi tệp) để Admin duyệt. Lịch sử quyết định, audit và thông báo được lưu cùng transaction. Nhánh đã đồng bộ với `dev`, giữ các endpoint hồ sơ và catalog, đồng thời bổ sung tài liệu API và Postman collection.

## Quyền truy cập và xử lý lặp

- Nhóm `/api/v1/me` dùng `RequireAuthorization()`. Nhóm `/api/v1/eligibility` dùng `RequireAuthorization("Admin")`. Request chưa xác thực nhận **401**, tài khoản đã xác thực thiếu quyền Admin nhận **403**, và hành khách truy cập hồ sơ ưu đãi của người khác nhận **404**.
- Submit, upload, quyết định và job lifecycle dùng chung `pg_advisory_xact_lock(hashtextextended(userId, 4))` để tuần tự hoá các thay đổi theo hành khách. Submit trùng hồ sơ đang mở và chuyển trạng thái không hợp lệ trả **409**. Cập nhật hồ sơ lặp lại với dữ liệu giống nhau không tạo thêm audit.
- Audit hồ sơ lưu tên trường đã đổi và hash trước/sau. Log retry của job purge chỉ ghi loại lỗi, không ghi PII, nội dung tệp, object key hoặc thông tin xác thực.

## Bản sửa integrity đang chờ đưa vào nhánh PR

Bản sửa local lưu tác vụ purge vào outbox migration 009 trong cùng transaction với thao tác gỡ tham chiếu file. Khi commit thành công, worker mới xóa file. Job giữ lại tác vụ nếu storage lỗi và tiếp tục thử sau. Các ca rollback trước commit, retry khi xóa dở và nhiều worker chạy đồng thời có test hồi quy.

## Bằng chứng kiểm thử và trạng thái phát hành

- [x] Liên kết issue và UC, mô tả phạm vi cùng quy tắc 401/403, idempotency và tránh log PII.
- [x] Build baseline `783a475` đạt 0 warning, 0 error. **93/93** test đạt gồm 39 unit và 54 integration, 0 skip. Kiểm tra SQL và nâng cấp baseline đã đạt trên database riêng. [Kết quả M01 xác minh trên baseline](https://github.com/macdogiahuy/Metro-ticket-management-system/pull/64#pullrequestreview-5989544838).
- [x] Các job CI của baseline `783a475` đã đạt. [Backend CI](https://github.com/macdogiahuy/Metro-ticket-management-system/actions/runs/44700751655/job/136168257945) · [Frontend CI](https://github.com/macdogiahuy/Metro-ticket-management-system/actions/runs/44700751889/job/136168258293).
- [x] Checkout local với bản sửa outbox đạt **96/96** gồm 39 unit và 57 integration, trong đó 32 test chạy trên PostgreSQL thật. 0 lỗi, 0 skip. Build Release đạt 0 warning, 0 error. Package SQL khớp source, deploy sạch đến 009 và nâng cấp 008 → 009 đều qua `09_verify.sql` trên database test riêng.
- [x] Neon production đã lên **001–008** lúc 22:54 ngày 20/11/2026 (UTC+7), sau khi được cấp quyền và xác minh backup riêng. Checksum 007/008 khớp source, ba bảng mới và kết nối backend đã được kiểm tra trực tiếp. Backup trước deploy vẫn ở 006. Đây là kết quả deploy DB production, tách biệt với kết quả CI.
- [x] Neon production đã lên **001–009** lúc 23:33 ngày 20/11/2026 (UTC+7), sau khi được cấp quyền, xác minh backup riêng ở 008 và chạy thử trên một branch sao chép. `09_verify.sql`, checksum, bảng purge và quyền của tài khoản backend đều đã xác minh. Backup 008 vẫn được giữ để phục hồi nếu cần.
- [x] Giữ nguyên checksum 008 đã deploy. Migration mới chỉnh check trạng thái hoặc quyết định phải nhắm trực tiếp tới `ck_profile_eligibility_status` và `ck_profile_eligibility_decision`.
- [ ] Đưa bản sửa integrity vào nhánh PR và chạy lại CI trên head mới.
- [ ] M01 xác nhận ngoại lệ giới hạn **400 dòng logic** trước merge. Baseline PR có **+1.701 dòng / 20 file**, review ước tính khoảng 750 dòng logic sau khi trừ test. Vui lòng ghi rõ quyết định ngoại lệ trên PR.
- [ ] Phát hành backend chứa worker mới và chạy smoke test HTTP sau khi bản sửa có trên nhánh PR và qua CI.
