# QA cho Metro Ticket Management System

Bộ tài liệu **QA-01** dùng để thiết kế, chạy và ghi nhận kiểm thử cho 27 UC Must-have của bản demo. Baseline là **SRS v1.2**; UC v1.6 hỗ trợ mô tả luồng. Khi khác nhau, dùng SRS v1.2.

Ma trận gồm **123 ca theo UC**, **9 ca phi chức năng** và **6 luồng smoke/E2E**; tổng **138 ca thiết kế**. Các biến thể dữ liệu trong từng ca cần kết quả thực thi riêng.

| Tài liệu | Cách sử dụng |
| --- | --- |
| [Test plan](TEST-PLAN.md) | Phạm vi, chiến lược, môi trường, lịch, điều kiện vào và ra |
| [Dữ liệu kiểm thử](TEST-DATA.md) | Tài khoản, mạng tuyến, giá, thời gian và trạng thái cần chuẩn bị |
| [Ma trận và test case](TEST-CASES.md) | Truy vết từng UC tới ca kiểm thử có bước chạy và kết quả mong đợi |
| [Ánh xạ automation](AUTOMATION-MAPPING.md) | Đối chiếu case với class/method hiện có, contract API mới từ dev và kết quả kiểm tra đồng bộ |
| [Mẫu bug report](BUG-REPORT-TEMPLATE.md) | Ghi lỗi, phân loại severity/priority, retest và đóng lỗi |
| [GitHub issue form](../../.github/ISSUE_TEMPLATE/bug_report.yml) | Form tạo bug trên GitHub sau khi thay đổi được tích hợp |

Task: [QA-01 issue 22](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/22). Owner: **My (M05)**; reviewer chéo: **Tiên (M06)**; duyệt theo quy trình nhóm: **Huy (M01)**. Ngày soạn: **24/11/2026**, múi giờ nghiệp vụ UTC+7.

QA-01 bàn giao **thiết kế kiểm thử**. Các ca bắt đầu ở `Not Run`; tài liệu này không chứng minh phần mềm đã pass. QA-02 triển khai API regression, QA-03 chạy E2E/UAT và thu bằng chứng. Trạng thái task trên board chỉ cập nhật qua Excel nguồn và quy trình review của nhóm, không suy ra Done từ việc có tài liệu.

Đã đồng bộ nhánh QA-01 với `origin/dev` tại **e8243d4** ngày 24/11/2026. Các biến thể API UC09/UC25 được bổ sung theo contract vừa tích hợp; giữ nguyên 123 case cha để kết quả cũ tiếp tục truy vết được.

## Bắt đầu một lượt chạy

1. Đọc phạm vi và điều kiện vào trong test plan; ghi commit/build, URL và DB thử riêng.
2. Chuẩn bị fixture theo TEST-DATA; mỗi bộ dữ liệu tham số là một lượt con độc lập.
3. Chạy ca theo thứ tự smoke, P0, P1 rồi P2/P3. Thiếu API/fixture thì ghi `Blocked` kèm nguyên nhân.
4. Ghi actual và evidence theo mẫu cuối TEST-CASES; ca Fail tạo bug theo form, liên kết hai chiều với case/run.
5. Retest bản sửa, chạy regression liên quan, tính coverage và kiểm điều kiện ra. Không tính ca chưa chạy hoặc bị skip là Pass.

## Nguồn cần đọc cùng

- [SRS v1.2](../requirements/Metro_SRS_v1.2.docx), mục 4–10, đặc biệt BR-071/072 và AC-X33–37.
- [UC v1.6](../requirements/Metro_Dac_ta_%20UC_v1.6.docx).
- [Phân công và phạm vi demo](../planning/TASK-ASSIGNMENT.md), mục 8–10; [README dự án](../../README.md), mục 1.1.
- [Hướng dẫn test backend](../../backend/tests/README.md), [ghi chú database hiện hành](../../infra/database/README.md).
