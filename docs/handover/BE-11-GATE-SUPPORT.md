# BE-11 — Hỗ trợ tại cổng: tra vé theo mã/SĐT, cấp lại QR có audit

## Phạm vi (issue #31, UC19, phụ thuộc BE-10)

BE-10 đã phát hành các endpoints `POST /api/v1/gate/lookup`, QR challenge/support
và image. BE-11 bổ sung **tra cứu bằng số điện thoại hành khách** cho lookup,
giữ nguyên hợp đồng cũ (`ticketCode` vẫn dùng được) và chuẩn hoá audit.

- `GateLookupRequest` có thêm `phone` (tuỳ chọn). **Bắt buộc đúng một định danh**:
  `ticketCode` hoặc `phone`; nếu cả hai hoặc không có → `400 INVALID_GATE_LOOKUP`.
- `GateLookupRules.NormalizePhone` đưa `0912...`, `+84 912...`, `84912...` về cùng
  khoá `912...`; SQL chuẩn hoá cả `AspNetUsers."PhoneNumber"` trước khi so sánh.
- Lookup vẫn trả payload tối thiểu (không QR, không SĐT, không birthDate/payment)
  và ghi `GATE_LOOKUP` audit với actor, reason và ticket id.
- Cấp lại QR (`qr-challenges` → `qr-support` → QR image) đã ở BE-10; BE-11 bổ sung
  kiểm chứng audit `REPLACE_TICKET_QR` và idempotency của request ghi.

## Bằng chứng (28/11/2026)

| Hạng mục | Kết quả |
| --- | --- |
| `dotnet build backend/Metro.sln -c Release --warnaserror` | 0 warning / 0 error |
| Unit test | **236 passed, 0 failed, 0 skipped** (thêm 7 test `GateLookupRulesTests`) |
| Integration test trên PostgreSQL riêng `metro_test_be11_*` (`deploy.sql` + `09_verify.sql`) | **176 passed, 0 failed, 0 skipped** (thêm 3 test) |
| 401/403 | `GateEndpointsTests.All_routes_require_staff...` phủ lookup/solutions cho anonymous, passenger và staff ngoài operator |
| Idempotency | `Verified_email_replacement_revokes_old_QR...` gửi cùng request hai lần, cùng receipt, một credential ACTIVE |
| PII | Test phone lookup khẳng định SĐT không xuất hiện trong response và `TestLogs.Messages` |

Không dùng Docker/InMemory hay DB dùng chung/production; không migration mới.
Swagger summary và Postman collection (`Audited lookup by passenger phone`)
đều đã cập nhật theo contract mới.
