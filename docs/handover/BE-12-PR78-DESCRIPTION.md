## Summary

BE-12 triển khai quản trị tài khoản/quyền và truy cập hồ sơ hành khách có audit
(UC22/UC23). Đã tích hợp `dev` tại `bc01004c5bce6d4e4caef9873a1d4f4286dc1e8a`
vào nhánh `feature/BE-12-account-permission-audit` và xử lý conflict tại local.

## Related Issue

Closes #21

## Changes

- [x] Feature
- [x] Bug fix
- [ ] Refactoring
- [x] Documentation
- [ ] Other

## Implementation Details

- API Admin: tìm/lọc/phân trang tài khoản đã xác minh email; quản lý role,
  khóa/mở khóa tài khoản; xem/sửa thông tin hành khách với reason/purpose hợp lệ.
- Bảo vệ Admin cuối cùng; staff giữ role Passenger theo BR-012; thay đổi role
  và khóa tài khoản thu hồi toàn bộ session, đổi SecurityStamp trong transaction.
  Bearer request kiểm tra stamp, session và trạng thái tài khoản để quyền cũ
  bị thu hồi ngay sau commit.
- Xem hồ sơ bắt buộc purpose thuộc danh sách cho phép, che PII bằng last4,
  lưu audit trước khi trả dữ liệu. Audit mutation có hash trước/sau; truy cập
  Admin bị từ chối cũng được audit.
- **Phạm vi authorization:** giữ nguyên semantics policy của `dev`:
  Passenger yêu cầu role Passenger, StationStaff yêu cầu StationStaff,
  Admin yêu cầu Admin. Đã bỏ thay đổi mở rộng policy trên toàn API.
  Admin không tự có quyền StationStaff; BE-12 giữ Passenger bằng role assignment
  tường minh. Có test hồi quy cho role đơn/kết hợp và Admin bị 403 ở staff probe.
- Giữ mapping TicketException, ticket endpoints, QR codec/worker/service và
  payment ownership từ BE-08/BE-09 trên `dev`, đồng thời giữ DI/audit BE-12.
  Swagger vẫn kiểm tra các contract profile, admin, payment và ticket.
- Cập nhật fixture JWT của payment tests để có auth session thực và các claim
  security_stamp/session_id, tương thích kiểm tra bearer của BE-12.

## Testing

Bằng chứng **local sau merge dev**, ngày 23/11/2026:

- [x] Unit tests: **51 passed**.
- [x] Integration tests: **97 passed**, gồm **15 test quản trị**; PostgreSQL
  17.12 riêng biệt, database mới `metro_test_pr78`, không skip.
- [x] Build successful: Release `--no-restore --warnaserror`, 0 warning/error.
- [x] SQL deployment package khớp migration nguồn; deploy 001–012 và verify pass.
- [x] Frontend: `npm ci`, typecheck, test (**1 passed**), production build pass.
- [ ] Manual testing: chưa thực hiện lượt Postman thủ công.
- [ ] `backend-build` xanh trên **head mới sau khi push**.
- [ ] `frontend-build` xanh trên **cùng head mới sau khi push**.

Backend chạy bằng `backend/tests/Run-PostgresTests.ps1 -NoBuild -NoRestore`
với `METRO_TEST_POSTGRES_REQUIRED=1`: **148 passed, 0 failed, 0 skipped**.
Local dùng .NET SDK 10.0.301 / target net9.0 và Node 26.13.1;
CI dùng .NET 9 / Node 24, cần xác nhận riêng trên GitHub.

TRX local: `%TEMP%/metro-pr78-20dfbff1487d4dbba595748a543e366f/results/`,
files `backend_net9.0_20261123153306.trx` và `backend_net9.0_20261123153415.trx`.
Chi tiết: [BE-12 verification](../../backend/docs/BE-12-ACCOUNT-ADMINISTRATION.md#verification).

Chưa push và chưa cập nhật PR trên GitHub theo yêu cầu chỉ làm local.
Kết quả CI của head cũ không được dùng làm bằng chứng cho bản sửa này.
Sau khi push, chỉ tick hai mục CI khi checks của đúng SHA mới đã xanh;
đính kèm link run tương ứng trước khi merge.

## Screenshots

Không có thay đổi UI.

## Checklist

- [x] Code follows project conventions.
- [x] No unnecessary changes.
- [x] No sensitive information committed.
- [x] No debug logs added.
- [x] Authorization scope and token upgrade requirements documented.
- [x] Local tests pass after integrating current dev.
- [x] Documentation updated.
- [ ] Hosted CI confirmed on the new head.
