# BE-10: Gate validation và hỗ trợ QR có xác minh

Closes [#30 — BE-10](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/30).

Backend theo SRS v1.2 UC16–UC18: scan trả Allow/Deny sau commit hoặc Pending khi chưa xác nhận kết quả; lưu Journey vào/ra đúng ga/chuyến/chiều, chặn entry thiếu/đầy tải. Hỗ trợ ngoại lệ và hiện/thay QR đúng quyền mà không mất lịch sử hay quyền exit hợp lệ.

## Thay đổi

- Receipt idempotent, chống quét trùng/đồng thời; payload khác cùng ID trả conflict. Cặp trẻ–giám hộ cùng thành công hoặc cùng thất bại.
- Entry kiểm current QR, hiệu lực và bối cảnh nhận khách thực tế; không lấy ETA làm actual arrival, không giữ sức chứa, không buộc chuyến gợi ý là chuyến thực đi.
- Exit kết thúc Journey hợp lệ kể cả vé hết hạn/holder bị khóa sau entry. Sửa quét nhầm, bổ sung lượt thiếu hoặc chuyển Admin theo bằng chứng; giữ AFC gốc và audit nguyên tử.
- Tra cứu tối thiểu; hiện/thay QR sau xác minh tên/ngày sinh cùng mã email hoặc giấy tờ minh họa khi không truy cập email. Thay QR thu hồi mã cũ ngay, giữ vé/hạn/Journey.
- OTP dùng một lần, có hạn/quota chung; outbox mã hóa, retry và xóa payload; không lộ OTP/raw QR trên API/log.
- Đã tích hợp dev `e66f058` gồm BE-15 và giữ cả OpenAPI gate lẫn reports. Không nhập runtime của các PR #87/#91 đang mở.

## Migration 014–017: đã phân chia version

| Version | Chủ sở hữu / nội dung |
| --- | --- |
| 014 | `014_service_operations.sql` — schema prerequisite giữ nguyên từ BE-14 PR #87, head `7cff576` |
| 015 | `015_gate_validation.sql` — BE-10, trước đây 014 |
| 016 | `016_gate_qr_support.sql` — BE-10, trước đây 015 |
| 017 | `017_gate_support_review.sql` — BE-10, trước đây 016 |

Bản BE-10 chứa đúng file 014 của BE-14 để deploy độc lập, không tạo khoảng trống version khi #87 chưa merge. Cùng nội dung/checksum 014 cho phép giữ nguyên lịch sử khi BE-14 vào trước hoặc sau. `09_verify.sql` kiểm đúng version/name và schema đến 017; `deploy.sql` được sinh lại. Không viết lại migration/checksum đã áp dụng trên DB chung; DB đã có chuỗi gate 014–016 cũ cần kế hoạch riêng, không áp dụng chuỗi mới trực tiếp.

## Hiệu năng và bằng chứng kiểm thử

Reviewer trên `5b30cdf` báo unit 112/112, integration 145/146; smoke 100 request/20 client có P95 khoảng 1495 ms qua Neon pooled, vượt ngưỡng 1000 ms. Kết quả đó được giữ nguyên; chưa đủ bằng chứng quy toàn bộ nguyên nhân cho mạng.

TestServer không loại bỏ DB execution, pool waits hoặc độ trễ API–DB. Bản sửa ghi rõ phạm vi đo và thêm probe `SELECT 1` riêng, không in endpoint/secret. Giữ nguyên 100 request/20 client và ngưỡng 1000 ms cho mọi môi trường; không skip, nâng ngưỡng hoặc trừ probe khỏi P95. Smoke fixture nhỏ không thay nghiệm thu NFR-002 bằng bộ dữ liệu/môi trường SRS đầy đủ.

Kiểm lại sau tích hợp dev/đổi số migration trên PostgreSQL 17.12 local:

- Build Release `--no-restore --warnaserror`: **0 warning / 0 error**.
- **283 pass = 130 unit + 153 integration; 0 fail / 0 skip** qua harness đầy đủ.
- Smoke trong suite: **P95 338,30 ms**, max 357,67 ms; DB probe median 0,14 ms. Chưa chạy lại Neon; không lấy số local thay kết quả Neon đã báo fail.
- Deploy sạch 001–017 + verifier + deploy lặp đạt; upgrade từ bộ BE-14 qua 014 đến 017 + verifier đạt, checksum 014 không đổi.
- DB legacy gate 014–016 cũ bị từ chối đúng bằng checksum, lịch sử không bị viết lại.
- `build_deploy.py --check`, Postman JSON và giữ nguyên contract Reports từ dev đạt.

Chi tiết/log kiểm thử tại [BE-10 handover](BE-10-GATE-VALIDATION.md). Ma trận yêu cầu/test: [SRS traceability](BE-10-SRS-TRACEABILITY.md). Kiểm tra tất cả nhánh và PR mở: [integration review](BE-10-INTEGRATION-REVIEW.md).

Chưa nghiệm thu camera/FE, SMTP inbox thực hoặc cổng vật lý. Không có offline validation, giữ ghế/sức chứa hoặc thanh toán tiền thật.
