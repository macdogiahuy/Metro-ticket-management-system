# BE-10 — Kiểm tra nhánh và PR trước cập nhật #90

Ngày 26/11/2026 (UTC+7). Dữ liệu từ `git fetch origin --prune`, toàn bộ 12 remote heads và GitHub API `/pulls?state=open`; không dựa vào trang danh sách PR bị cache. Dev tại thời điểm kiểm tra: `e66f058` (đã merge BE-15 #88).

| Nhánh remote | Head | So với dev | Migration sau 013 |
| --- | --- | --- | --- |
| main | c8d1988 | ancestor | Không |
| dev | e66f058 | baseline | Không |
| chore/board-progress-s1 | b683017 | ancestor | Không |
| chore/board-sync-be05 | 54b4f19 | ancestor | Không |
| chore/board-sync-be08 | 15a6a1b | ancestor | Không |
| chore/board-sync-be09 | 697c66a | ancestor | Không |
| chore/board-sync-be18b | 9c65120 | ancestor | Không |
| chore/board-sync-s0-done | af0c595 | ancestor | Không |
| feature/BE-06-1-fare-ef-mapping | 4761145 | ancestor | Không |
| feature/BE-10-gate-validation | 5b30cdf | ahead 2 / behind 4 | Gate 014–016 cũ, được sửa trong bản này |
| feature/BE-14-eta-load-disruption | 7cff576 | ahead 5 / behind 4 | 014_service_operations.sql |
| feature/BE16-ticket-cancellation-and-refund | 5162ec1 | ahead 2 / behind 4 | Không |

Các nhánh ancestor đã nằm trong dev; không lấy lại các bản code cũ. Nhánh BE-15 đã bị xóa sau merge #88, code đã có trong dev. Không có nhánh FE-05 được push; frontend không thay đổi trong bản sửa BE-10.

## PR chưa merge tại lần kiểm tra đầu

| PR | Head đã kiểm tra | Trạng thái / ảnh hưởng BE-10 |
| --- | --- | --- |
| [#87 BE-14](https://github.com/macdogiahuy/Metro-ticket-management-system/pull/87) | 7cff576 | Open; 014 schema operations. Chưa nhập runtime/API BE-14. Giữ file SQL 014 nguyên văn làm dependency của chuỗi BE-10 015–017. |
| [#90 BE-10](https://github.com/macdogiahuy/Metro-ticket-management-system/pull/90) | 5b30cdf trước sửa | Open; cập nhật dev, giải quyết OpenAPI conflict bằng cách giữ cả GateOpenApiFilter và ReportOpenApiFilter; sửa migration, log hiệu năng, mô tả PR. |
| [#91 BE-16](https://github.com/macdogiahuy/Metro-ticket-management-system/pull/91) | 5162ec1 | Open; không thêm migration; có sửa các vùng booking/payment/ticket dùng chung. Chưa tích hợp runtime vào BE-10; cần review concurrency và chạy test khi PR này được cập nhật/merge. |

GitHub API báo backend/frontend checks success trên các head nêu trên; đó là bằng chứng của từng nhánh riêng. Cả ba PR vẫn open và API báo mergeable=false tại thời điểm khảo sát; không suy ra ghép cả ba nhánh sẽ tự pass. Không approve/merge PR hoặc push vào nhánh của thành viên khác.

## Quyết định migration

1. BE-14 sở hữu 014; file 014 trong BE-10 phải khớp nội dung/checksum với `7cff576`.
2. BE-10 dùng 015 gate, 016 QR support, 017 admin support review. Không sửa nội dung SQL ba gate migrations khi đổi tên.
3. Wrapper deployment, checksum/name/version và verifier được sinh/đồng bộ đến 017. Không có hai file dùng cùng version hoặc khoảng trống 014.
4. Kiểm deploy sạch, re-deploy, upgrade từ chuỗi BE-14 qua 014. DB test có gate 014–016 cũ không được dùng để giả kiểm upgrade chuẩn bằng cách sửa schema_migrations.
5. Cùng file 014 cho phép hợp nhất hai PR theo cả hai thứ tự; người tích hợp vẫn phải giữ deployment/verifier mới nhất và kiểm thử sau merge. Nếu schema 014 của BE-14 thay đổi trước merge thì phải so lại checksum và kiểm lại dependency, không ghi đè lịch sử đã triển khai.

## Cập nhật trong phiên kiểm tra

Trong lúc chạy suite lần đầu, maintainer đã merge #87 vào dev `eba772f` và xóa nhánh BE-14. Đã fetch lại toàn bộ remote: còn 11 heads, PR mở còn #90 và #91. Bản cuối cập nhật dev này, giải quyết conflict OpenAPI/test runner/README/Postman/deploy và giữ đủ Gate + Operations + Reports. Migration 014 khớp dev chính thức, không còn là dependency chưa merge. Không nhập runtime #91 và không tự merge PR nào trên GitHub. Kết quả 283 test bên dưới là bước trước BE-14; bằng chứng suite cuối sau BE-14 được ghi riêng trong bàn giao.

## Hiệu năng

Giữ fail Neon pooled P95 khoảng 1495 ms trong mô tả. Giữ ngưỡng smoke 1000 ms, 100 request/20 client. Log mới phân biệt loopback/non-loopback và ghi probe DB riêng; không che fail, không trừ độ trễ DB khỏi P95, không suy đoán chắc chắn nguyên nhân chỉ là mạng. Kết quả trên fixture local không thay NFR-002 đầy đủ.

Trước khi BE-14 vào dev: 283 test pass (130 unit + 153 integration), P95 local 338,30 ms. Sau khi cập nhật dev `eba772f` và kiểm trên DB mới hoàn toàn: **309 pass (149 unit + 160 integration), 0 fail/skip; P95 local 288,06 ms, build 0 warning/error**. Deploy sạch/re-deploy/upgrade từ BE-14 014 đến 017 đạt; chuỗi gate 014–016 cũ bị chặn đúng bằng checksum. Chi tiết trong [bàn giao BE-10](BE-10-GATE-VALIDATION.md).
