# Kế hoạch triển khai & phân công — Nhóm 6 (M01–M06)

| Thông tin | Giá trị |
| --- | --- |
| Dự án | Hệ thống quản lý vé và hỗ trợ hành khách Metro |
| Tài liệu | Kế hoạch Backend-first → API freeze → Frontend |
| Phiên bản | 5.5 (thay thế 5.4) — bám SRS v1.2 / UC v1.6 |
| Ngày lập | 13/11/2026 |
| Người lập | M01 — Mạc Đỗ Gia Huy (Tech Lead) |
| Khởi động | **16/11/2026** (thứ Hai) |
| API freeze | **04/12/2026** (thứ Sáu) — chốt contract các luồng Must-have |
| Frontend | **07/12 – 17/12/2026** |
| Bàn giao | **20/12/2026** (Chủ nhật) — nội bộ xong hết |
| Nộp bài | **23/12/2026** — hard deadline; 21–23/12 chỉ là đệm sửa P0 |
| Bảng theo dõi | `docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx` (mã task trùng với tài liệu này) |
| Bảng điều khiển | `docs/planning/BOARD.md` — link lọc theo sprint/người + toàn bộ 33 task |
| Kịch bản kickoff | `docs/planning/KICKOFF-S0.md` — agenda, 6 điểm cần chốt, task S0, tin nhắn gửi nhóm |

> Đây là đồ án môn học: **cả 6 người đều làm BE rồi làm FE**. M01 là tech lead, chủ yếu kiến trúc – review – tích hợp, chỉ code khi có việc chặn đường găng.

## 1. Mục tiêu và phạm vi

- Làm **27 UC Must-have** trong SRS 1.2 (47 UC); Stretch/Out xem mục 10.
- **Schema đã bám SRS v1.2**: `infra/database` có 6 migration (001–006); 006 bổ sung order version, gợi ý chuyến, tải kg nhập tay, giám hộ, mua hộ, đổi vé do gián đoạn, tiếp cận, thất lạc. Không còn quota/giữ chỗ trong luồng mới — bảng cũ chỉ là **legacy**.
- **BE trước**: 16/11 → 04/12 dựng toàn bộ API Must-have, có Swagger + test.
- **Chốt API 04/12**: sau mốc này chỉ thêm field/endpoint, không đổi tên, kiểu, tham số, mã lỗi.
- **FE sau**: 07/12 → 17/12 bám contract đã freeze; 18–20/12 UAT, demo và bàn giao; 21–23/12 đệm P0.

## 2. Nguyên tắc

1. **Hợp đồng trước, giao diện sau**: FE không dựng màn hình khi endpoint chưa có trong Swagger + Postman.
2. **Một task một owner**, reviewer ghi riêng; không ai tự merge PR của mình — trừ 5 task của M01 (tech lead) được tự merge sau khi CI xanh.
3. **Cả nhóm làm BE**: kể cả bạn mạnh FE, để hiểu domain và dữ liệu trước khi làm UI.
4. **Không sửa migration đã áp dụng**; thay đổi schema = migration mới + test.
5. **Giao task theo cổng** (mục 4.2), không giao hết một lần — tránh task chết vì chờ dependency.
6. **Definition of Done đo được** (mục 9), không đánh giá bằng cảm tính.

## 3. Đội hình

| Mã | Tên | Vai trò Scrum | Chuyên môn chính | Capacity/sprint | Ghi chú |
| --- | --- | --- | --- | --- | --- |
| **M01** | Mạc Đỗ Gia Huy (Huy) | Team Lead / Scrum Master | Kiến trúc, API contract, review, tích hợp | 10 SP | Tech lead; code các task nền/auth/payment/hardening; không nhận task FE chính |
| **M02** | Tuyên | Developer | Backend/API (ticketing), sau đó FE booking | 20 SP | Booking/payment/vé; giữ nhịp freeze 04/12 |
| **M03** | Hoàng | Developer | Frontend/UI; BE catalog/fare giai đoạn đầu | 20 SP | Chủ lực UI, nhận BE trước để hiểu dữ liệu · GitHub `@lehoangdn2005-png` |
| **M04** | Thành | Developer | Backend/Database (EF mapping, booking), sau đó FE vé/soát vé | 20 SP | Đường găng BE-07 (26–30/11); không dồn thêm việc trong khoảng này · GitHub `@22-LeDaiThanh` |
| **M05** | My | QA / Tester | Test case, manual/API test; BE admin; FE admin | 20 SP | Vẫn code BE+FE theo yêu cầu môn học · GitHub `@v-hamy165` |
| **M06** | Tiên | Developer / QA | Full-stack, CI/test harness, FE reports | 20 SP | Giữ CI xanh và Postman/Newman · GitHub `@TienNguyen0o0` |

- Tên đã chốt: M01 Huy · M02 Tuyên (`@hinouser29`) · M03 Hoàng · M04 Thành · M05 My · M06 Tiên. 33 issue GitHub **#12–#44** đã tạo, gán đúng assignee + milestone S0–S3. Deadline nộp **23/12**, nội bộ chốt xong **20/12**.
- Ai chưa quen ASP.NET/EF: nhận task nhỏ hơn + pairing với M02/M04 trong S0–S1; không tự nhận BE-07/BE-08.

## 4. Lịch và cổng giao task

### 4.1. Sprint

| Sprint | Thời gian | Mục tiêu | Mốc |
| --- | --- | --- | --- |
| **S0** | 16/11 – 22/11 | Nền API, EF mapping, catalog, seed, CI | **Nền chạy được** |
| **S1** | 23/11 – 04/12 | Auth, hồ sơ, fare, booking, payment, vé, admin, báo cáo | **API freeze 04/12** |
| **S2** | 07/12 – 17/12 | FE (5 người) + soát vé/hardening (BE) + tích hợp | **Code freeze 17/12** |
| **S3** | 18/12 – 20/12 | UAT, sửa P0–P1, demo, bàn giao | **Bàn giao 20/12** |
| *Đệm* | 21/12 – 23/12 | Chỉ sửa P0 phát sinh, đóng gói, nộp bài | **Nộp 23/12** |

Hai điểm tràn ranh giới sprint (đã tính sẵn trong lịch, không phải trượt): **BE-06 khởi động 22/11** (Chủ nhật, để BE-07 đóng 30/11) và **BE-16 (hủy/hoàn + đổi vé — Stretch) chốt 06/12**, sau freeze 2 ngày nhưng không đổi contract của FE-04/FE-07.

### 4.2. Cổng giao task (giao dần, không giao một lần)

| Cổng | Ngày | Giao gì | Điều kiện vào cổng |
| --- | --- | --- | --- |
| **G0 — Kickoff** | 16/11 | BE-01, BE-02A/B, BE-18A/B, BE-05 (bắt đầu) | Chốt tên, .NET version, DB dùng chung, kênh liên lạc |
| **G1 — S1 Planning** | 23/11 | BE-03, BE-04, BE-06, BE-12, QA-01, BE-18 (hoàn tất) | S0 demo: `/health`, `/swagger`, EF đọc được bảng lõi, seed chạy |
| **G2 — API Freeze** | 04/12 | FE-01…FE-05, FE-07, QA-02; BE-10/BE-11/BE-15 (nếu chưa xong) | Swagger + Postman đủ luồng mua vé→thanh toán→vé; không còn endpoint Must-have thiếu |
| **G3 — FE Wave 2** | 10/12 | FE-06 (soát vé), FE-08 (báo cáo) | BE-10 và BE-15 đã merge + Postman xanh |
| **G4 — Code Freeze** | 17/12 | Chỉ nhận fix P0/P1 | E2E luồng chính xanh trên môi trường chung |
| **G5 — Bàn giao** | 20/12 | DOC-01, QA-03 | UAT xong, demo chạy được, tài liệu đầy đủ; nộp bài trước 23/12 |

Quy tắc: task chỉ được kéo sang *In Progress* khi đã qua cổng tương ứng. Việc chưa tới cổng để trạng thái **To Do**, không nhận trước.

### 4.3. Bảng giao việc theo từng người

Dùng bảng này để gửi task ngay khi kickoff; thứ tự dòng chính là thứ tự làm.

| Mã | Người | Task theo thứ tự (ngày · pd) | Tổng pd |
| --- | --- | --- | --- |
| M01 | Mạc Đỗ Gia Huy (Huy) | BE-01 (16–18/11 · 3) → BE-03 (23–25/11 · 3) → BE-08 (01–04/12 · 4) → BE-17 (08–09/12 · 2) → DOC-01 (19–20/12 · 2) | 14 |
| M02 | Tuyên | BE-02A (16–18/11 · 3) → BE-09 (25–28/11 · 4) → BE-16 (05–06/12 · 2) → FE-04 (07–12/12 · 5) → QA-02 (15–16/12 · 2) | 16 |
| M03 | Hoàng | BE-05 (19–21/11 · 3) → BE-06 (22–25/11 · 4) → BE-13 (26–29/11 · 4) → FE-02 (07–09/12 · 3) → FE-03 (10–12/12 · 3) | 17 |
| M04 | Thành | BE-02B (19–20/11 · 2) → BE-07 (26–30/11 · 5) → BE-10 (01–04/12 · 4) → FE-05 (07–09/12 · 3) → FE-06 (10–12/12 · 3) | 17 |
| M05 | My | BE-18A (16–17/11 · 2) → QA-01 (23–25/11 · 1) → BE-04 (26–27/11 · 2) → BE-12 (28–29/11 · 2) → BE-14 (30/11–01/12 · 2) → FE-07 (07–13/12 · 5) → QA-03 (17–19/12 · 3) | 17 |
| M06 | Tiên | BE-18B (16–18/11 · 2) → BE-15 (30/11–02/12 · 3) → BE-11 (05/12 · 1) → FE-01 (07–09/12 · 3) → FE-08 (10–13/12 · 3) → FE-09 (16–17/12 · 2) | 14 |

- Tổng 95 pd, **mỗi người 14–17 pd**, xếp liên tiếp đúng 1 pd/ngày, không chồng lấn, không ai có việc sau 20/12.
- Mỗi người chỉ có **một** task đang mở tại một thời điểm; hết task sớm thì nhận task kế tiếp của mình, không nhảy sang task của người khác khi chưa qua cổng.
- **Task của M01 (BE-01, BE-03, BE-08, BE-17, DOC-01) không review chéo** — M01 tự merge sau khi CI xanh; đổi lại M01 review toàn bộ PR của 5 người còn lại.
- M06 trống 19–29/11: giữ CI/Postman xanh, viết test harness cho module đang mở, review PR.
- M01 không nhận task FE chính; thời gian trống dùng để review, gỡ blocker, chốt contract.

## 5. Backend (16/11 – 09/12)

Ước lượng theo **person-day (pd)**, đã gồm code + test + review. Mỗi task một branch `feature/<task>-<slug>` và PR vào `dev`.

| Mã | Nội dung | UC/BR | Owner | Thời gian | pd | Phụ thuộc |
| --- | --- | --- | --- | --- | --- | --- |
| BE-01 | Nền API: ProblemDetails, validation pipeline, logging, health, Swagger + example, CORS, `global.json` | NFR | M01 | 16–18/11 | 3 | — |
| BE-02A | EF Core mapping lõi: identity, network, order, order_versions, ticket (phần chính) | DATA | M02 | 16–18/11 | 3 | BE-01 (contract) |
| BE-02B | EF mapping ticketing/finance: payment, refund, inspection, gợi ý chuyến, consent mua hộ, giám hộ, đổi vé, thất lạc — **bỏ bảng capacity legacy** | DATA | M04 | 19–20/11 | 2 | BE-02A |
| BE-05 | Catalog đọc: tuyến, ga, hành trình, tình trạng dịch vụ (cache ngắn) | UC07, UC08, UC10 | M03 | 19–21/11 | 3 | BE-02A |
| BE-18A | Seed/fixture: 2 tuyến, 12 ga, giá mẫu, tài khoản demo | AC-* | M05 | 16–17/11 | 2 | — |
| BE-18B | Test harness + CI: xUnit, `WebApplicationFactory`, PostgreSQL thật (không bắt buộc Docker) | NFR | M06 | 16–18/11 | 2 | BE-01 (contract) |
| BE-03 | Identity + JWT: đăng ký, xác minh email, đăng nhập/xuất, refresh, policy role | UC01–UC04 | M01 | 23–25/11 | 3 | BE-02A |
| BE-06 | Fare engine + quản lý loại vé/giá/chính sách ưu đãi (version + audit) | UC09, UC25 | M03 | 22–25/11 | 4 | BE-05 |
| BE-09 | Phát hành vé + QR credential + vé miễn phí trẻ <6 (không tạo payment) + chặn trùng hiệu lực + API vé của tôi/lịch sử | UC13–UC15, BR-007 | M02 | 25–28/11 | 4 | BE-02B |
| BE-04 | Hồ sơ + xác minh ưu đãi, upload minh chứng, audit thay đổi | UC05, UC06 | M05 | 26–27/11 | 2 | BE-03 |
| BE-07 | Booking: tạo đơn, order version, **gợi ý chuyến** (không giữ chỗ), consent mua hộ, `Idempotency-Key` | UC11, UC36 | M04 | 26–30/11 | 5 | BE-06 |
| BE-13 | Tuyến/ga + lịch/chuyến + **hồ sơ tải kg** (`max_load_kg`, thời gian chạy, thời gian chuyển tuyến) + thông báo dịch vụ | UC24, UC29, UC31 | M03 | 26–29/11 | 4 | BE-05 |
| BE-12 | Quản trị tài khoản/quyền + xem dữ liệu hành khách có audit | UC22, UC23 | M05 | 28–29/11 | 2 | BE-03 |
| BE-14 | Cập nhật ETA + trạng thái tải/gián đoạn + gợi ý chuyến dự phòng | UC30, UC36 | M05 | 30/11–01/12 | 2 | BE-13 |
| BE-15 | Báo cáo vé/doanh thu + export CSV | UC33 | M06 | 30/11–02/12 | 3 | BE-09 |
| BE-08 | Payment MoMo TEST: tạo giao dịch, callback/IPN, chữ ký, retry idempotent | UC11, UC12 | M01 | 01–04/12 | 4 | BE-07 |
| BE-10 | Soát vé tại cổng: Allow/Deny/Pending, journey vào/ra (ghi chuyến/chiều), chặn khi thiếu/đầy tải, scan idempotent | UC16–UC18 | M04 | 01–04/12 | 4 | BE-09 |
| BE-16 | Hủy đơn chưa thu + hoàn khoản thu lỗi + **đổi vé do gián đoạn** (tối đa 1 lần/vé, thu hồi QR cũ) | UC42, UC43 | M02 | 05–06/12 | 2 | BE-08 |
| BE-11 | Hỗ trợ tại cổng: tra vé, cấp lại QR có audit | UC19 | M06 | 05/12 | 1 | BE-10 |
| BE-17 | Hardening: rate limit, security headers, PII masking, audit bắt buộc | NFR | M01 | 08–09/12 | 2 | BE-03…BE-16 |

Tổng BE: **57 pd** (chưa gồm QA-01 1 pd) — M01 12, M02 9, M03 11, M04 11, M05 8, M06 6. Phần còn lại của capacity mỗi người dành cho review, test và hỗ trợ tích hợp.

> Lịch trên đã tính đúng **1 pd/ngày/người** và chỉ nối tiếp sau khi task phụ thuộc xong. BE-06 khởi động **Chủ nhật 22/11** để BE-07 kịp đóng 30/11 và payment kịp freeze 04/12.

### 5.1. Ghi chú kỹ thuật bắt buộc

- Thanh toán/hủy/miễn phí/đổi vé **gọi hàm SQL** đã test: `confirm_payment_and_issue_tickets`, `issue_free_order`, `cancel_unpaid_order`, `revoke_unused_ticket`, `apply_ticket_change`. **Không** dùng `hold_trip_capacity`/`confirm_capacity_hold` (legacy SRS 1.1).
- **Không giữ chỗ**: đơn vé lượt có tối đa **một gợi ý hiện hành**/phiên bản đơn (`trip_suggestions`, view `v_current_trip_suggestion`). Gợi ý không phải booking, không bảo đảm chỗ và không chặn vào cổng.
- **Trùng hiệu lực**: DB chặn bằng exclusion constraint `ex_tickets_no_overlapping_validity`; service vẫn phải kiểm đơn đã trả tiền hoặc miễn phí đã xác nhận đang chờ cấp vé.
- **Tải kg**: `load_observations` nhập tay (1 số lẻ, đơn vị kg); thiếu số đo hoặc đã đầy thì cổng từ chối vào, không suy diễn từ số vé bán.
- **Một người/một loại vé mỗi đơn**: trigger `trg_order_items_single_passenger` chặn `passenger_count > 1` với sản phẩm không `capacity_controlled`.
- `Idempotency-Key` cho POST tạo đơn/thanh toán/scan; payment lưu `provider_event_id` + `payload_hash` và claim event nguyên tử.
- Thời gian UTC; `EndExclusive`; không dùng `DateTime.Now`.
- Bắt `PostgresException` và map SQLSTATE (`P0001`, `P0002`, `23505`, `22023`, `28000`) sang mã lỗi nội bộ; không trả raw message/stack trace.
- `RequireUniqueEmail = true` để email trùng trả 400 kèm `code` thay vì 500.
- Không log PII/token; token chỉ lưu hash.

## 6. API freeze — 04/12/2026

Điều kiện freeze:

- Swagger có đủ endpoint dưới đây, mỗi endpoint có example request/response và mã lỗi.
- Postman collection chạy được 6 kịch bản demo trên DB seed.
- Không còn `TODO`/`NotImplementedException` trong luồng Must-have.

| Module | Endpoint freeze | Task |
| --- | --- | --- |
| Auth | `POST /auth/register`, `/verify-email`, `/login`, `/refresh`, `/logout`, `GET /auth/me` | BE-03 |
| Hồ sơ | `GET/PUT /me`, `GET/POST /me/eligibility` | BE-04 |
| Catalog | `GET /lines`, `/stations`, `/journeys`, `/fares/quote`, `/service-status`, `/trips/{id}/load` | BE-05, BE-06, BE-14 |
| Booking | `POST /orders`, `GET /orders/{id}`, `GET /orders/{id}/suggestion`, `POST /orders/{id}/consent`, `DELETE /orders/{id}` | BE-07 |
| Payment | `POST /payments`, `POST /payments/{id}/retry`, `POST /webhooks/momo` | BE-08 |
| Vé | `GET /tickets`, `/tickets/{id}`, `/tickets/{id}/qr`, `GET /orders`, `GET /payments`, `POST /orders/{id}/issue-free` | BE-09 |
| Admin | `/admin/accounts`, `/admin/roles`, `/admin/lines`, `/admin/stations`, `/admin/products`, `/admin/fares`, `/admin/schedules`, `/admin/trips`, `/admin/trip-load`, `/admin/notices` | BE-12, BE-13 |
| Báo cáo | `GET /reports/revenue`, `/reports/tickets`, `/reports/revenue/export` | BE-15 |
| Hủy/hoàn | `POST /orders/{id}/cancel`, `POST /refunds`, `POST /refunds/{id}/approve` | BE-16 |
| Đổi vé | `POST /tickets/{id}/change-requests`, `POST /ticket-change-requests/{id}/approve` | BE-16 |

**Sau 04/12**: chỉ được **thêm** field optional hoặc endpoint mới; mọi thay đổi breaking phải có M01 duyệt + thông báo kênh FE. Soát vé (`BE-10/BE-11`) đóng muộn nhất **05/12** — FE-06 vẫn nằm ở wave 2.

## 7. Frontend (07/12 – 17/12)

Wave 1 bám module đã freeze; wave 2 mở sau khi BE tương ứng merge.

| Wave | Ngày | Nội dung |
| --- | --- | --- |
| W1 | 07–10/12 | App shell, design token, auth/hồ sơ, catalog/giá, booking, admin CRUD |
| W2 | 10–13/12 | Vé của tôi/QR, soát vé, báo cáo; hoàn thiện admin |
| W3 | 16–17/12 | Polish responsive/a11y, E2E, chuẩn bị code freeze |

| Mã | Nội dung | Owner | Thời gian | pd | Phụ thuộc |
| --- | --- | --- | --- | --- | --- |
| FE-01 | App shell, routing, design token, axios/react-query, layout theo role | M06 | 07–09/12 | 3 | API freeze |
| FE-02 | Auth + hồ sơ + trạng thái ưu đãi | M03 | 07–09/12 | 3 | BE-03, BE-04 |
| FE-03 | Catalog: tuyến/ga/hành trình/giá + tình trạng dịch vụ | M03 | 10–12/12 | 3 | BE-05, BE-06 |
| FE-04 | Booking → thanh toán → kết quả (kèm retry) | M02 | 07–12/12 | 5 | BE-07, BE-08 |
| FE-05 | Vé của tôi, QR, lịch sử đặt/thanh toán | M04 | 07–09/12 | 3 | BE-09 |
| FE-06 | Portal soát vé: scan, tra cứu, cấp lại QR | M04 | 10–12/12 | 3 | BE-10, BE-11 |
| FE-07 | Admin: tài khoản, tuyến/ga, loại vé/giá, lịch/chuyến, thông báo | M05 | 07–13/12 | 5 | BE-12, BE-13 |
| FE-08 | Báo cáo doanh thu/vé + export CSV | M06 | 10–13/12 | 3 | BE-15 |
| FE-09 | Responsive, accessibility, loading/empty/error toàn cục | M06 | 16–17/12 | 2 | FE-01…FE-08 |

Tổng FE: **30 pd** (M02 5, M03 6, M04 6, M05 5, M06 8). M01 review + hỗ trợ tích hợp, không nhận task FE chính.

Quy ước FE: không gọi API khác contract; mọi màn hình có 4 trạng thái loading/empty/error/success; không hard-code màu – dùng token; không đưa secret xuống browser.

## 8. QA / Test

| Mã | Nội dung | Owner | Thời gian | pd |
| --- | --- | --- | --- | --- |
| QA-01 | Test plan + ma trận test case theo UC + mẫu bug report | M05 | 23–25/11 | 1 |
| QA-02 | API regression Postman/Newman trên DB seed | M02 | 15–16/12 | 2 |
| QA-03 | E2E + UAT + bug bash, ghi bằng chứng | M05 | 17–19/12 | 3 |

Mức test:

| Loại | Công cụ | Phạm vi | Khi nào |
| --- | --- | --- | --- |
| Unit | xUnit + NSubstitute | Validator, handler không chạm DB | Mỗi PR |
| Integration | xUnit + `WebApplicationFactory` + PostgreSQL thật | Endpoint + EF + hàm SQL | Mỗi PR chạm DB |
| SQL regression | `infra/database/tests/*.sql` | Capacity, payment, constraint | Khi đổi schema/hàm |
| Contract/E2E | Postman/Newman, Playwright | 6 kịch bản demo + luồng chính | Trước mỗi demo, G4 |

Docker Desktop có thể không chạy — integration test dùng PostgreSQL local qua biến môi trường, không bắt buộc Testcontainers.

## 9. Definition of Done và Git

### 9.1. DoD mọi task

- [ ] `dotnet build` / `npm run build` xanh, không warning mới.
- [ ] Unit test cho rule; integration test cho endpoint chạm DB (BE) hoặc test component (FE).
- [ ] Swagger + Postman cập nhật (BE); không gọi endpoint ngoài contract (FE).
- [ ] Kiểm quyền server-side: có test 401/403.
- [ ] Idempotency cho endpoint ghi; không log PII/secret.
- [ ] PR ≤ 400 dòng logic, mô tả + link task/UC, review chéo theo bảng dưới; **bắt buộc có approval của M01** (CODEOWNERS), review chéo chỉ là tư vấn.
- [ ] Cập nhật dòng task trong Excel: Status, % Complete, Last Updated, ghi chú blocker.

### 9.2. Review

| Người | Review chính cho | Tải review |
| --- | --- | --- |
| M01 | Toàn bộ PR của 5 người còn lại; bắt buộc với auth, payment, vé, thay đổi contract | 28 |
| M02 | BE-02B, BE-04, BE-06, BE-07, FE-02 | 5 |
| M03 | BE-18A, BE-12, BE-14, BE-16, QA-03 | 5 |
| M04 | BE-02A, BE-18B, BE-09, BE-11, FE-04, QA-02 | 6 |
| M05 | BE-05, BE-13, BE-15, FE-01, FE-08, FE-09 | 6 |
| M06 | QA-01, BE-10, FE-03, FE-05, FE-06, FE-07 | 6 |

Cột *Assignee/Reviewer* trong sheet SPRINT_BOARD là nguồn chính thức; nếu đổi người review thì cập nhật cả hai nơi.

**Cơ chế chặn trên GitHub**: `main` và `dev` dùng ruleset `protect-main` / `protect-dev` bật `require_code_owner_review`; `.github/CODEOWNERS` gán `* @macdogiahuy` nên **chỉ approval của M01 mới mở khóa merge**. M01 nằm trong danh sách bypass của ruleset nên PR của M01 tự merge được sau khi CI xanh. Approval của thành viên khác vẫn có giá trị tham khảo nhưng không thay thế được approval của M01.

**Task của M01 (BE-01, BE-03, BE-08, BE-17, DOC-01) không cần review** — M01 tự merge sau khi CI xanh và tự kiểm bằng Postman/E2E. Bù lại M01 duyệt 28 task còn lại; 28 task kia vẫn review chéo 5–6 task/người để bắt lỗi sớm, nhưng không ai tự merge PR của mình. Review SLA 4 giờ làm việc; quá 24 giờ nhắc trong kênh nhóm, M01 duyệt trong tối đa 1 ngày làm việc.

### 9.3. Git

`feature/<task>-<slug> → PR → dev`; `dev → main` chỉ ở mốc và do M01 merge. Không merge khi CI đỏ, không force push. Title: `feat(be): ...`, `feat(fe): ...`, `fix: ...`, `docs: ...`.

## 10. Truy vết Use Case → task

| Nhóm UC | Nội dung | Mức | Task |
| --- | --- | --- | --- |
| UC01–UC04 | Đăng ký/đăng nhập/đăng xuất/khôi phục mật khẩu | Must | BE-03, FE-02 |
| UC05–UC06 | Hồ sơ, xác minh ưu đãi | Must | BE-04, FE-02 |
| UC07–UC10 | Tuyến/ga, hành trình, giá, tình trạng dịch vụ | Must | BE-05, BE-06, FE-03 |
| UC11–UC12 | Mua vé, thử lại thanh toán | Must | BE-07, BE-08, FE-04 |
| UC13–UC15 | Vé của tôi, lịch sử đặt/thanh toán | Must | BE-09, FE-05 |
| UC16–UC18 | Soát vé, vé không hợp lệ, tra cứu tại ga | Must | BE-10, FE-06 |
| UC19 | Hỗ trợ hành khách tại cổng | Must | BE-11, FE-06 |
| UC22–UC23 | Tài khoản/quyền, dữ liệu hành khách | Must | BE-12, FE-07 |
| UC24–UC25 | Tuyến/ga, loại vé/giá/chính sách | Must | BE-06, BE-13, FE-07 |
| UC29, UC31 | Lịch/chuyến, thông báo dịch vụ | Must | BE-13, FE-07 |
| UC33 | Báo cáo vé/doanh thu | Must | BE-15, FE-08 |
| UC42–UC43 | Hủy đơn chưa thu, hoàn khoản thu lỗi, đổi vé do gián đoạn | Stretch (làm nếu kịp) | BE-16, FE-07 |
| UC20–UC21, UC26–UC27, UC30, UC32, UC34–UC35, UC38 | Phản ánh, cấu hình, dự phòng, báo cáo vận hành, AFC | Stretch | BE-14, BE-15 |
| UC28, UC37, UC39–UC41, UC44–UC47 | Audit đầy đủ, giới hạn tải nâng cao, giám hộ/mua hộ, tiếp cận, thất lạc, dự báo | Out | — |

> Các UC Out **đã có schema + hàm SQL** trong migration 006 nhưng chưa làm UI/API trong đồ án. Nếu nhóm còn thời gian, thứ tự mở rộng: UC39–UC40 (giám hộ + vé trẻ miễn phí) → UC44 (tiếp cận) → UC46 (thất lạc) → UC47 (dự báo). Phải báo M01 trước khi nhận vì ảnh hưởng deadline.

## 11. Rủi ro

| Rủi ro | Ảnh hưởng | Giảm thiểu | Theo dõi |
| --- | --- | --- | --- |
| EF mapping 93 bảng kéo dài | Chặn toàn bộ BE | Chỉ map bảng Must-have trước; M04 hỗ trợ M02 từ 19/11; bảng legacy capacity không map | M02 |
| Hiểu nhầm mô hình quota cũ (SRS 1.1) | Code giữ chỗ sai, phí công | Chỉ dùng SRS v1.2; 006 đánh dấu legacy; đọc `infra/database/documentation/DATABASE_DESIGN.md` trước khi code booking | M01 |
| BE-07 trễ (26–30/11) | Chặn BE-08 → chặn freeze | M01 làm adapter payment song song từ 23/11; BE-10 của M04 lùi tối đa 1 ngày | M04 |
| Nhóm chưa quen ASP.NET/EF | Chậm, sai kiến trúc | Task nhỏ + pairing; review sớm trong S0 | M01 |
| MoMo sandbox thay đổi | Chặn BE-08 | Adapter + mock provider, chốt sandbox trong S0 | M01 |
| FE dồn 2 tuần cuối | Trượt mốc 20/12 | Freeze 04/12; cắt theo mục 12; wave 2 chỉ mở khi API xong | M01 |
| Thành viên nghỉ/giảm giờ | Trượt cổng | Báo trước 1 ngày; cắt task Stretch trước, không cắt test | Cả nhóm |
| PR lớn, conflict | Chậm tích hợp | PR ≤400 dòng; rebase hằng ngày; owner theo `Features/<Module>` | Cả nhóm |

## 12. Cắt scope khi trễ

Thứ tự cắt (dừng ở mức đủ demo):

1. UC42 hủy/hoàn → chỉ hủy đơn chưa thanh toán.
2. FE-08 báo cáo → chỉ bảng doanh thu theo ngày, bỏ export CSV.
3. FE-07 admin → chỉ tuyến/ga/giá/lịch, bỏ quản lý tài khoản nâng cao.
4. FE-06 → chỉ scan + tra cứu, bỏ cấp lại QR.
5. **Không cắt**: BE-07, BE-08, BE-09, BE-03, FE-04, FE-05 và test thanh toán/vé.

## 13. Bàn giao 20/12 (nộp bài 23/12)

- [ ] Release `v1.0-demo` build từ `main`, chạy được trên máy sạch theo README.
- [ ] Swagger + Postman collection + seed demo + 6 kịch bản demo.
- [ ] Báo cáo tiến độ, danh sách bug còn lại (chỉ P2/P3), hướng dẫn chạy.
- [ ] Excel cập nhật đủ Status/Done; Daily Scrum và Retro có ghi chép.
- [ ] Slide/demo 10 phút: đăng nhập → mua vé → thanh toán → vé QR → soát vé → báo cáo.
- [ ] Phân công người trực demo và phương án dự phòng (video quay sẵn).
- [ ] Đóng gói bản nộp (zip/PDF + link repo) **trước 23/12**; 21–23/12 chỉ sửa P0.

## 14. Việc cần chốt ngay (trước 16/11)

1. Số ngày làm việc/tuần thực tế của từng người (ảnh hưởng capacity 20 SP/sprint).
2. Chốt .NET 9 (scaffold) hay nâng .NET 10; pin `global.json`.
3. Chốt DB dùng chung: Neon (đã có) hay PostgreSQL local; phát connection string qua kênh kín.
4. Chốt tài khoản MoMo TEST + endpoint callback (ngrok/Cloudflare tunnel).
5. Chốt style API: ProblemDetails, mã lỗi, phân trang (`/api/v1`, camelCase, tiền minor unit).
6. Label `blocked` + template bug report trên GitHub (33 issue đã tạo sẵn).
7. Chốt lịch: Daily 15 phút (21:00), Review thứ Sáu, Retro cuối sprint.
8. Chốt danh sách **27 UC Must-have demo** và ghi rõ phần Out-of-scope trong `README.md` (schema đã hỗ trợ nhiều UC Out nhưng chưa làm UI).

---

**Ghi chú phiên bản**: 5.5 — bám **SRS v1.2 / UC v1.6** (SRS thắng khi lệch): bỏ giữ chỗ theo đoạn (BE-07 dùng gợi ý chuyến), tải kg nhập tay (BE-13/BE-14), vé trẻ miễn phí không tạo payment (BE-09), chặn trùng hiệu lực, đổi vé do gián đoạn (BE-16), EF bỏ bảng capacity legacy (BE-02B). Lịch, người, số pd **giữ nguyên v5.4**: mốc 16/11 → 20/12/2026 (nộp 23/12); BE-first, API freeze 04/12, code freeze 17/12; tổng 95 pd (BE 57, FE 30, QA 3 + DOC 2), mỗi người 14–17 pd.
