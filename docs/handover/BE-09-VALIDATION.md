> Cách bàn giao hiện hành: một nhánh `feature/be09-08-integration-handover`, một PR #77 vào dev theo yêu cầu người dùng. Nội dung chia nhiều PR bên dưới là lịch sử đã được thay thế.

> KẾT QUẢ MỚI SAU SỬA: **113/113 pass**, 0 fail/skip; migration hiện hành là 007–009. Xem [bản hoàn thành](BE-09-FINAL-2026-11-22.md). Số liệu 106/106 bên dưới là mốc 20/11.

# Kết quả kiểm chứng BE-09

Ngày 20/11/2026. Nhánh `feature/be-09-ticket-issuance`, baseline `7afc4f25c85bff6156d16002fea33d6ebeea2a5c`. Code và tài liệu nằm trong checkout `C:\Users\ACER\Documents\Codex\Metro-BE09-review-20261119`.

## Kết quả thực thi cuối

| Kiểm tra | Kết quả |
| --- | --- |
| Release build, `-warnaserror`, SDK 10.0.401 / target net9.0 | Thành công, **0 warning, 0 error** |
| Unit project | **50 passed**, 0 failed, 0 skipped |
| Integration project | **56 passed**, 0 failed, 0 skipped |
| Tổng | **106 passed**, 0 failed, 0 skipped |
| PostgreSQL acceptance theo bộ đếm runner | **33 passed** |
| CI credential policy | Passed |
| `build_deploy.py --check` | deploy.sql khớp toàn bộ migration nguồn |
| Deploy 001–007 trên database test dựng mới | Thành công |
| Deploy lặp | 001–007 được nhận diện đã áp dụng; không đổi checksum migration cũ |
| `scripts/09_verify.sql` | `database verification passed`, đủ 7 migration |
| Postman | JSON hợp lệ, thêm đúng 6 request trong folder BE-09, giữ request cũ |
| Swagger | Có 6 route, Bearer, Idempotency-Key bắt buộc, PNG binary và trạng thái 202 |
| `git diff --check` | Passed |

TRX cuối ở ngoài repository, không chứa credential kết nối:

```text
C:\Users\ACER\Documents\Codex\Metro-BE09-localtest\results-20261120-160047\
  backend_net9.0_20261120160051.trx  — Unit
  backend_net9.0_20261120160100.trx  — Integration
```

Database kiểm thử: PostgreSQL 17 tại localhost, database riêng `metro_test_be09_tuyen`. Chạy bằng launcher nạp credential DPAPI ngoài Git. Không dùng Neon chung; không dùng InMemory hoặc Docker thay cho các test SQL/HTTP chạm DB. Các test thuần metadata/rule có sẵn vẫn được giữ trong toàn bộ regression; con số integration project không được hiểu là mọi test trong đó đều truy cập PostgreSQL.

## Các quy tắc đã kiểm chứng

| Nhóm | Bằng chứng |
| --- | --- |
| Thu tiền / cấp vé | Gọi SQL entry point, lặp cùng event, payload replay khác bị từ chối, signature chưa verify bị từ chối; chỉ một Ticket; Order PAID trước khi QR sẵn và ISSUED sau worker |
| Version | Payment gắn version; callback version cũ bị chặn; không sửa nội dung confirmed version; chi tiết vé đọc nhóm ưu đãi từ version bất biến |
| Race | Hai connection xác nhận hai đơn chồng hiệu lực chỉ một nguồn thành công; hai callback cùng nguồn và hai worker giữ một vé/current credential |
| Hiệu lực | UTC+7, `[start,end)`, ngày tương lai chưa có QR, pass 1/7/30 ngày lịch bắt đầu PaidAt; retry không kéo dài hạn; ngày liên tiếp được phép |
| Giữ quyền | Vé/đơn chờ cấp vẫn chặn overlap; Journey mở của vé hết hạn chặn cấp mới; QR hành trình đang đi vẫn lấy được để kết thúc lượt cũ |
| Trẻ | Dưới 6 tại ngày đi, đúng sinh nhật 6 và năm nhuận, không paid/periodic ticket; kiểm lại ngày sinh đã sửa trước cấp QR |
| Giám hộ | Quan hệ đúng trẻ và đã duyệt, guardian ticket đủ điều kiện; nhận tham chiếu guardian tương lai đã trả nhưng chưa có QR; không cấp sớm; thu hồi quan hệ chặn quyền QR mới |
| Miễn phí | Hai đơn riêng; issue-free gọi SQL; key gọi lặp, key khác nội dung/version conflict; không tạo Payment hoặc PaidAt |
| QR | Ký HMAC, SHA-256 hash, version, ổn định sau tạo codec lại và reload, PNG đúng; không PII/secret trong payload; hash sai, dùng rồi/hết hạn bị từ chối |
| Failure | Thiếu key retry, sau 15 phút NeedsSupport, enqueue thông báo một lần; giữ đơn đã thu và vé, phục hồi key không tạo thêm/gia hạn |
| API | 401 cả sáu route; 403 thao tác free thiếu quyền; 404 vé ngoài ownership/cross-operator; Buyer không xem QR người nhận; phân trang và lịch sử mặc định 30 ngày |

Tests chính: `backend/tests/Metro.IntegrationTests/Endpoints/TicketEndpointsTests.cs`, `backend/tests/Metro.UnitTests/Ticketing/`, và regression toàn solution. Đã cập nhật assertion cũ về Ticket ISSUED cho đúng contract “SQL tạo bản ghi → chờ QR”; vẫn kiểm đầy đủ round-trip hiệu lực và ACTIVE ở mốc đầu/cuối.

## Giới hạn bằng chứng và phần phải review

- **Chưa xác nhận end-to-end MoMo TEST hoặc frontend**: BE-06/07/08/10/14 cần ghép luồng thật. Fixture không chứng minh logic tạo đơn, consent, xét duyệt giám hộ, gateway, quét cặp hoặc email delivery của các thành viên khác.
- Các file SQL test lịch sử dưới `infra/database/tests` vẫn giữ kỳ vọng schema 005/006. Bộ đếm trên là harness .NET hiện hành, không phải tuyên bố các SQL script lịch sử đó chạy thành công trên 007; README database đã chỉ rõ thay đổi contract.
- **Chưa áp dụng 007 lên Neon chung**. Khi tích hợp cần cấu hình khóa QR ổn định và quyền runtime trong [hướng dẫn BE-09](BE-09-IMPLEMENTATION.md).
- **Chưa commit/push/tạo PR, chưa được reviewer duyệt hoặc merge**. Diff hiện lớn hơn 400 dòng logic, phải chia thay đổi thành các PR đáp ứng giới hạn trước khi xuất bản; grouping SQL, worker và API trong handover chỉ là nhóm trách nhiệm, không xác nhận ba PR sẽ tự đủ nhỏ.
- Vì còn bước review và tích hợp, **chưa đánh dấu issue Done toàn bộ DoD**. Phần code và kiểm thử trong phạm vi BE-09 đã triển khai và kiểm chứng như bảng trên.
