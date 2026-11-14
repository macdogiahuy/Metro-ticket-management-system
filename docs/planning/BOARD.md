# Bảng điều khiển dự án (snapshot 14/11/2026 — kế hoạch v5.5)

Nguồn sự thật: `docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx` (sheet `SPRINT_BOARD`).
Kế hoạch chi tiết: `docs/planning/TASK-ASSIGNMENT.md` **v5.5** (bám SRS v1.2 / UC v1.6).
Task trên GitHub: 33 issue **#12–#44**, gán sẵn assignee + milestone.

## Liên kết nhanh

| Lọc | Link |
| --- | --- |
| Tất cả task | [issues](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue) |
| S0 · Nền tảng | [milestone S0](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+milestone%3A%22S0%22) |
| S1 · API + freeze | [milestone S1](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+milestone%3A%22S1%22) |
| S2 · FE + soát vé | [milestone S2](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+milestone%3A%22S2%22) |
| S3 · UAT + bàn giao | [milestone S3](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+milestone%3A%22S3%22) |
| Backend | [label be](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+label%3Abe) |
| Frontend | [label fe](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+label%3Afe) |
| QA | [label qa](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+label%3Aqa) |

Theo người (mở issue đang active):

| Người | Issue đang mở | Đã xong |
| --- | --- | --- |
| Huy (M01) | [open](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aopen+assignee%3Amacdogiahuy) | [closed](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aclosed+assignee%3Amacdogiahuy) |
| Tuyên (M02) | [open](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aopen+assignee%3Ahinouser29) | [closed](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aclosed+assignee%3Ahinouser29) |
| Hoàng (M03) | [open](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aopen+assignee%3Alehoangdn2005-png) | [closed](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aclosed+assignee%3Alehoangdn2005-png) |
| Thành (M04) | [open](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aopen+assignee%3A22-LeDaiThanh) | [closed](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aclosed+assignee%3A22-LeDaiThanh) |
| My (M05) | [open](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aopen+assignee%3Av-hamy165) | [closed](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aclosed+assignee%3Av-hamy165) |
| Tiên (M06) | [open](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aopen+assignee%3ATienNguyen0o0) | [closed](https://github.com/macdogiahuy/Metro-ticket-management-system/issues?q=is%3Aissue+is%3Aclosed+assignee%3ATienNguyen0o0) |

## Trạng thái theo sprint

| Sprint | Thời gian | Task | SP | Người | Mốc thoát |
| --- | --- | --- | --- | --- | --- |
| S0 | 16–22/11 | 5 | 14 | Huy, My, Thành, Tiên, Tuyên | nền chạy được |
| S0–S1 | 19–21/11 | 1 | 3 | Hoàng | catalog có dữ liệu |
| S1 | 23/11–04/12 | 12 | 45 | Hoàng, Huy, My, Thành, Tiên, Tuyên | API freeze 04/12 |
| S2 | 07–17/12 | 13 | 41 | Hoàng, Huy, My, Thành, Tiên, Tuyên | code freeze 17/12 |
| S3 | 17–20/12 | 2 | 5 | Huy, My | bàn giao 20/12 |

## Toàn bộ task

| Issue | Mã | Task | Owner | Reviewer | Sprint | Bắt đầu | Hạn | SP | Trạng thái |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [#12](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/12) | `BE-01` | Nền API | Huy | — | S0 | 16/11 | 18/11 | 5 | To Do |
| [#13](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/13) | `BE-02A` | EF mapping lõi | Tuyên | Thành | S0 | 16/11 | 18/11 | 3 | To Do |
| [#16](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/16) | `BE-18A` | Seed/fixture | My | Hoàng | S0 | 16/11 | 17/11 | 2 | To Do |
| [#17](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/17) | `BE-18B` | Test harness + CI | Tiên | Thành | S0 | 16/11 | 18/11 | 2 | To Do |
| [#14](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/14) | `BE-02B` | EF mapping ticketing/finance | Thành | Tuyên | S0 | 19/11 | 20/11 | 2 | To Do |
| [#15](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/15) | `BE-05` | Catalog đọc | Hoàng | My | S0–S1 | 19/11 | 21/11 | 3 | To Do |
| [#20](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/20) | `BE-06` | Fare engine + quản lý loại vé/giá/chính sách ưu đãi… | Hoàng | Tuyên | S1 | 22/11 | 25/11 | 5 | To Do |
| [#18](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/18) | `BE-03` | Identity + JWT | Huy | — | S1 | 23/11 | 25/11 | 5 | To Do |
| [#22](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/22) | `QA-01` | Test plan + ma trận test case theo UC + mẫu bug report | My | Tiên | S1 | 23/11 | 25/11 | 1 | To Do |
| [#25](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/25) | `BE-09` | Phát hành vé + QR credential + vé miễn phí trẻ <6 + chặn… | Tuyên | Thành | S1 | 25/11 | 28/11 | 5 | To Do |
| [#19](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/19) | `BE-04` | Hồ sơ + xác minh ưu đãi, upload minh chứng, audit thay đổi | My | Tuyên | S1 | 26/11 | 27/11 | 2 | To Do |
| [#23](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/23) | `BE-07` | Booking | Thành | Tuyên | S1 | 26/11 | 30/11 | 8 | To Do |
| [#24](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/24) | `BE-13` | Tuyến/ga + lịch/chuyến + hồ sơ tải kg + thời gian… | Hoàng | My | S1 | 26/11 | 29/11 | 5 | To Do |
| [#21](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/21) | `BE-12` | Quản trị tài khoản/quyền + xem dữ liệu hành khách có audit | My | Hoàng | S1 | 28/11 | 29/11 | 2 | To Do |
| [#27](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/27) | `BE-14` | Cập nhật ETA + trạng thái tải/gián đoạn + gợi ý chuyến dự… | My | Hoàng | S1 | 30/11 | 01/12 | 2 | To Do |
| [#29](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/29) | `BE-15` | Báo cáo vé/doanh thu + export CSV | Tiên | My | S1 | 30/11 | 02/12 | 3 | To Do |
| [#26](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/26) | `BE-08` | Payment MoMo TEST | Huy | — | S1 | 01/12 | 04/12 | 5 | To Do |
| [#30](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/30) | `BE-10` | Soát vé tại cổng | Thành | Tiên | S2 | 01/12 | 04/12 | 5 | To Do |
| [#28](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/28) | `BE-16` | Hủy đơn chưa thu + hoàn khoản thu lỗi + đổi vé do gián đoạn… | Tuyên | Hoàng | S1 | 05/12 | 06/12 | 2 | To Do |
| [#31](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/31) | `BE-11` | Hỗ trợ tại cổng | Tiên | Thành | S2 | 05/12 | 05/12 | 1 | To Do |
| [#33](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/33) | `FE-01` | App shell, routing, design token, axios/react-query, layout… | Tiên | My | S2 | 07/12 | 09/12 | 3 | To Do |
| [#34](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/34) | `FE-02` | Auth + hồ sơ + trạng thái ưu đãi | Hoàng | Tuyên | S2 | 07/12 | 09/12 | 3 | To Do |
| [#36](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/36) | `FE-04` | Booking → thanh toán → kết quả (kèm retry) | Tuyên | Thành | S2 | 07/12 | 12/12 | 5 | To Do |
| [#37](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/37) | `FE-05` | Vé của tôi, QR, lịch sử đặt/thanh toán | Thành | Tiên | S2 | 07/12 | 09/12 | 3 | To Do |
| [#39](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/39) | `FE-07` | Admin | My | Tiên | S2 | 07/12 | 13/12 | 5 | To Do |
| [#32](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/32) | `BE-17` | Hardening | Huy | — | S2 | 08/12 | 09/12 | 3 | To Do |
| [#35](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/35) | `FE-03` | Catalog | Hoàng | Tiên | S2 | 10/12 | 12/12 | 3 | To Do |
| [#38](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/38) | `FE-06` | Portal soát vé | Thành | Tiên | S2 | 10/12 | 12/12 | 3 | To Do |
| [#40](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/40) | `FE-08` | Báo cáo doanh thu/vé + export CSV | Tiên | My | S2 | 10/12 | 13/12 | 3 | To Do |
| [#42](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/42) | `QA-02` | API regression Postman/Newman trên DB seed | Tuyên | Thành | S2 | 15/12 | 16/12 | 2 | To Do |
| [#41](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/41) | `FE-09` | Responsive, accessibility, loading/empty/error toàn cục | Tiên | My | S2 | 16/12 | 17/12 | 2 | To Do |
| [#43](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/43) | `QA-03` | E2E + UAT + bug bash, ghi bằng chứng | My | Hoàng | S3 | 17/12 | 19/12 | 3 | To Do |
| [#44](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/44) | `DOC-01` | Demo script + README chạy máy sạch + tài liệu bàn giao | Huy | — | S3 | 19/12 | 20/12 | 2 | To Do |

> Cập nhật cột *Trạng thái* trong Excel (To Do → In Progress → Code Review / QA → Done), sau đó chạy lại `python tools/kickoff.py` để làm mới bảng này.
