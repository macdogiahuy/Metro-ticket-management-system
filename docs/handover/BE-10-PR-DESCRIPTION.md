# BE-10: Gate validation và hỗ trợ QR có xác minh

Closes [#30 — BE-10](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/30).

Backend theo SRS v1.2 UC16–UC18: scan trả Allow/Deny sau commit hoặc Pending khi chưa xác nhận kết quả; lưu Journey vào/ra đúng ga/chuyến/chiều, chặn entry thiếu/đầy tải. Hỗ trợ ngoại lệ và hiện/thay QR đúng quyền mà không mất lịch sử hay quyền exit hợp lệ.

## Thay đổi

- Receipt idempotent, chống quét trùng/đồng thời; payload khác cùng ID trả conflict. Cặp trẻ–giám hộ cùng thành công hoặc cùng thất bại.
- Entry kiểm current QR, hiệu lực và bối cảnh nhận khách thực tế; không lấy ETA làm actual arrival, không giữ sức chứa, không buộc chuyến gợi ý là chuyến thực đi.
- Exit kết thúc Journey hợp lệ kể cả vé hết hạn/holder bị khóa sau entry. Sửa quét nhầm, bổ sung lượt thiếu hoặc chuyển Admin theo bằng chứng; giữ AFC gốc và audit nguyên tử.
- Tra cứu tối thiểu; hiện/thay QR sau xác minh tên/ngày sinh cùng mã email hoặc giấy tờ minh họa khi không truy cập email. Thay QR thu hồi mã cũ ngay, giữ vé/hạn/Journey.
- OTP dùng một lần, có hạn/quota chung; outbox mã hóa, retry và xóa payload; không lộ OTP/raw QR trên API/log.
- Đã cập nhật dev `eba772f` gồm BE-14 (#87) và BE-15 (#88), giữ đủ OpenAPI gate, operations và reports. PR #87 được maintainer merge trong lúc kiểm tra; không nhập runtime PR #91 đang mở, không tự merge PR trên GitHub.

## Migration 014–017: đã phân chia version

| Version | Chủ sở hữu / nội dung |
| --- | --- |
| 014 | `014_service_operations.sql` — BE-14 đã có trên dev `eba772f`, giữ nguyên nội dung/checksum |
| 015 | `015_gate_validation.sql` — BE-10, trước đây 014 |
| 016 | `016_gate_qr_support.sql` — BE-10, trước đây 015 |
| 017 | `017_gate_support_review.sql` — BE-10, trước đây 016 |

BE-14 đã merge vào dev; bản cuối giữ nguyên 014 của dev và gate chỉ dùng 015–017. Không còn trùng version hoặc khoảng trống 014. `09_verify.sql` kiểm đúng version/name và schema đến 017; `deploy.sql` được sinh lại. Không viết lại migration/checksum đã áp dụng trên DB chung; DB đã có chuỗi gate 014–016 cũ cần kế hoạch riêng, không áp dụng chuỗi mới trực tiếp.

## Hiệu năng và bằng chứng kiểm thử

Reviewer trên `5b30cdf` báo unit 112/112, integration 145/146; smoke 100 request/20 client có P95 khoảng 1495 ms qua Neon pooled, vượt ngưỡng 1000 ms. Kết quả đó được giữ nguyên; chưa đủ bằng chứng quy toàn bộ nguyên nhân cho mạng.

TestServer không loại bỏ DB execution, pool waits hoặc độ trễ API–DB. Bản sửa ghi rõ phạm vi đo và thêm probe `SELECT 1` riêng, không in endpoint/secret. Giữ nguyên 100 request/20 client và ngưỡng 1000 ms cho mọi môi trường; không skip, nâng ngưỡng hoặc trừ probe khỏi P95. Smoke fixture nhỏ không thay nghiệm thu NFR-002 bằng bộ dữ liệu/môi trường SRS đầy đủ.

Kiểm lại sau tích hợp dev/đổi số migration trên PostgreSQL 17.12 local:

- Build Release `--no-restore --warnaserror`: **0 warning / 0 error**.
- **309 pass = 149 unit + 160 integration; 0 fail / 0 skip** qua harness đầy đủ trên DB mới sau khi BE-14 đã vào dev.
- Smoke trong suite: **P95 288,06 ms**, max 316,91 ms; DB probe median 0,12 ms. Chưa chạy lại Neon; không lấy số local thay kết quả Neon đã báo fail.
- Deploy sạch 001–017 + verifier + deploy lặp đạt; upgrade từ bộ BE-14 qua 014 đến 017 + verifier đạt, checksum 014 không đổi.
- DB legacy gate 014–016 cũ bị từ chối đúng bằng checksum, lịch sử không bị viết lại.
- `build_deploy.py --check`, Postman JSON và giữ nguyên contract Operations/Reports từ dev đạt; giữ đủ 3 OpenAPI filters, không còn conflict với dev `eba772f`.

Chi tiết/log kiểm thử tại [BE-10 handover](BE-10-GATE-VALIDATION.md). Ma trận yêu cầu/test: [SRS traceability](BE-10-SRS-TRACEABILITY.md). Kiểm tra tất cả nhánh và PR mở: [integration review](BE-10-INTEGRATION-REVIEW.md).

Chưa nghiệm thu camera/FE, SMTP inbox thực hoặc cổng vật lý. Không có offline validation, giữ ghế/sức chứa hoặc thanh toán tiền thật.
