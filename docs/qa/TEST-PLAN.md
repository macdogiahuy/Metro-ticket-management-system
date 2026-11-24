# Kế hoạch kiểm thử Metro Ticket Management System

QA-01 thiết kế kiểm thử để nhóm xác nhận luồng tài khoản, mua vé MoMo TEST, phát hành QR, soát vé và quản trị hoạt động đúng SRS v1.2. Bộ ca dùng chung cho QA-02 và QA-03; việc nghiệm thu cần kết quả chạy và bằng chứng trên build cụ thể.

## 1 Thông tin và baseline

| Thuộc tính | Giá trị |
| --- | --- |
| Task và phiên bản | QA-01 · phiên bản 1.0 · 24/11/2026 |
| Owner và review | My M05; review chéo Tiên M06; Huy M01 duyệt theo quy trình repo |
| Yêu cầu chính | [SRS v1.2](../requirements/Metro_SRS_v1.2.docx), mục 4 BR, mục 5 FR, mục 9 NFR, mục 10 AC |
| Nguồn hỗ trợ | [UC v1.6](../requirements/Metro_Dac_ta_%20UC_v1.6.docx); [TASK-ASSIGNMENT](../planning/TASK-ASSIGNMENT.md) mục 8–10 |
| Quy tắc giải quyết khác biệt | SRS v1.2 thắng UC v1.6, demo script và mô tả ngắn trên board; thay đổi phạm vi phải được M01 ghi nhận |
| Deliverable | Kế hoạch này, TEST-DATA, TEST-CASES, mẫu bug và GitHub issue form |

Không dùng tiêu chí quota/hold theo đoạn của SRS v1.1. UC36 có **gợi ý chuyến, không giữ chỗ**; gate kiểm chuyến thực, ga, chiều và tải kg nhập tay. UC18 gồm tra cứu/cấp lại QR; UC19 là hỗ trợ **mua vé**, gồm Administrator thao tác tại quầy có xác nhận khách. Các mô tả rút gọn của BE-11/FE-06 hoặc demo script không thay nghĩa UC này. Notice là UC31; UC29 quản lý lịch và hoạt động tàu.

## 2 Phạm vi

### 2.1 Phải kiểm cho demo

**27 UC:** UC01–UC19, UC22–UC25, UC29, UC31, UC33, UC36. Ma trận chi tiết ở [TEST-CASES](TEST-CASES.md).

| Nhóm | Nội dung và rủi ro chính |
| --- | --- |
| UC01–UC06 | Email/mật khẩu/phiên; hồ sơ; duyệt HS/SV và người cao tuổi; truy cập giấy tờ |
| UC07–UC10 | Mạng tuyến, hành trình, công thức giá, thông tin dịch vụ và ETA thiếu |
| UC11–UC15 | Checkout, retry/Unknown, xác minh callback, thu trùng/muộn, PaidAt, chờ phát hành, thời hạn và lịch sử |
| UC16–UC19 | Entry/exit, Pending/Deny, quét lặp/đồng thời, tải kg, ngoại lệ, thay QR, mua tại quầy |
| UC22–UC25 | Quyền và thu hồi phiên, Administrator cuối, dữ liệu nhạy cảm, version mạng tuyến/giá |
| UC29/31/33/36 | Lịch/chuyến, notice, đối chiếu tiền/vé/lượt, gợi ý chuyến tách biệt entryTrip |
| NFR liên quan | Hiệu năng, bảo mật, responsive/tiếp cận, đồng thời, phiên, quan sát, phục hồi và retention |

Kiểm vai trò và sở hữu tại API, không chỉ ẩn nút UI. Tên nghiệp vụ **Ticket Inspector/Administrator** ánh xạ mã Identity hiện tại **StationStaff/Admin**. Nhân viên dùng chức năng Passenger cho bản thân; Administrator có nghiệp vụ soát vé theo SRS. Ghi lỗi nếu tập role/policy thực tế không đáp ứng; không sửa expected theo hành vi code đang lỗi.

### 2.2 Stretch và ngoài phạm vi

| Phân loại | UC hoặc khả năng | Cách xử lý |
| --- | --- | --- |
| Stretch | UC20–21, UC26–27, UC30, UC32, UC34–35, UC38, UC42–43 | Chỉ lập/chạy bộ chức năng đầy đủ sau khi nhóm nhận scope; không tính vào mẫu số 27 UC |
| Out | UC28, UC37, UC39–41, UC44–47 | Không nghiệm thu UI/API đầy đủ cho demo; có schema không đồng nghĩa đã có tính năng |
| Ngoài sản phẩm | Tiền thật, cổng vật lý, giữ ghế/quota, vé 3 ngày, OCR/biometrics, SMS/push, offline gate, hóa đơn thuế, hoàn tự nguyện/bồi thường | Không thiết kế luồng chấp nhận các chức năng này |

Các **bất biến tích hợp** của UC Must vẫn phải kiểm: khoản thu muộn/trùng không cấp vé sai, không khai Unknown là đã thu, không tạo payment giả cho đơn miễn phí nếu fixture hợp lệ đã có, không lộ QR người khác. Kiểm hợp đồng này không đưa toàn bộ màn hình hoàn tiền UC42 hoặc mua hộ/giám hộ UC39/40 vào Must. Nếu fixture trẻ/giám hộ chưa sẵn sàng, ghi rõ ca tích hợp điều kiện là Blocked; không tự tạo quan hệ sai hoặc suy ra luồng đã đạt. Xuất CSV thuộc UC34 Stretch; FE-08 có triển khai thì thêm regression riêng, không thay kiểm báo cáo UC33.

## 3 Chiến lược và thứ tự chạy

| Lớp | Cách kiểm | Thời điểm và người phụ trách |
| --- | --- | --- |
| Review thiết kế | Đối chiếu UC → FR/AC → test ID; kiểm tiền/thời gian theo SRS | QA-01, My; Tiên review |
| Unit/component | xUnit cho rule; Vitest/React Testing Library cho UI | Owner BE/FE trên PR liên quan |
| Integration/contract | WebApplicationFactory + PostgreSQL thật; HTTP auth/ownership, transaction, SQL invariants | Owner BE, QA-02 tái sử dụng bằng chứng có build/run |
| API regression | Postman/Newman với environment test, assertion response và persisted state | QA-02 Tuyên; collection là đầu ra QA-02, chưa phải đầu ra QA-01 |
| E2E/UAT | UI trình duyệt/camera/upload; có thể tự động hóa Playwright khi harness sẵn sàng | QA-03 My; owner module hỗ trợ, Hoàng review |
| NFR | Bài tải theo SRS 9.1, kiểm quyền/PII, responsive, phục hồi trên DB test | QA phối hợp BE-17/FE-09/DOC-01 |

Thứ tự: smoke (S01–S06) → P0 tiền/vé/quyền → P1 chức năng Must và biên → P2 khả dụng/hiển thị → retest và regression. Chạy lại smoke khi build hoặc cấu hình triển khai đổi. Sau sửa payment chạy UC11/12/13/15/33; sau sửa QR chạy UC13/16/17/18/36; sau sửa auth/role chạy UC01–06/19/22/23 và quyền các endpoint liên quan. Không suy ra coverage từ tên file test có sẵn; cần ánh xạ assertion và kết quả thực tế.

## 4 Môi trường và dữ liệu

1. Pin SDK theo `backend/global.json`; FE dùng lockfile với `npm ci`; ghi version runtime thực tế và commit SHA. PostgreSQL test 17+ theo harness, DB có tên `metro_test_*` riêng cho mỗi lượt/nhóm.
2. Apply SQL hiện hành bằng `infra/database/deploy.sql`, sau đó `infra/database/scripts/09_verify.sql`. Không dùng EF migrations. Chỉ chạy setup/fixture trên DB test riêng, không dùng Neon production hoặc DB chia sẻ cho regression phá dữ liệu.
3. Dùng dữ liệu tổng hợp trong [TEST-DATA](TEST-DATA.md). Ghi mapping alias → ID fixture trong run; xác nhận seed thực tế, không giả định demo seed đã có. Reset bằng cách tái tạo **đúng DB test của run** sau khi giữ evidence; không xóa/move thư mục repo hoặc dùng thao tác trên DB production.
4. Email dùng mock outbox/dev inbox; MoMo chỉ TEST hoặc stub kiểm soát kết quả. Stub pass không thay một lượt tích hợp MoMo TEST có xác minh backend và callback. Ghi riêng provider/mode trong evidence.
5. Thời gian nghiệp vụ UTC+7, server quyết định. Dùng clock kiểm soát trong harness hoặc dữ liệu mốc phù hợp; không đổi đồng hồ máy dùng chung. Case đồng thời dùng barrier/2 client, không click nối tiếp rồi gọi là race test.

Chạy acceptance backend theo [backend/tests/README](../../backend/tests/README.md). Sau khi tạo và apply schema cho DB test riêng, đặt:

```powershell
$env:METRO_TEST_POSTGRES_DATABASE = 'metro_test_qa01_local'
$env:METRO_TEST_POSTGRES_CONNECTION_STRING = '<Npgsql connection tới DB test trên>'
$env:METRO_TEST_POSTGRES_REQUIRED = '1'
pwsh backend/tests/Run-PostgresTests.ps1
```

Harness yêu cầu zero fail, zero skipped và có PostgreSQL test thực chạy. Offline unit run có skip **không phải acceptance**. Các SQL test theo chuỗi fixture cũ 005/006 được đánh dấu lịch sử trong [infra/database/README](../../infra/database/README.md); không chạy chúng như bộ nghiệm thu hợp đồng issuance hiện hành 010–012. Xác nhận version mới nhất của package khi chạy.

FE tại thư mục `frontend`: `npm ci`, `npm run lint`, `npm test`, `npm run build`. Kiểm UI khi FE của UC đã sẵn sàng; API pass không tự là UI pass. Runtime và thao tác chạy trên là hướng dẫn cho lượt thực thi, không phải kết quả đã chạy trong QA-01.

## 5 Điều kiện vào và ra

### 5.1 Bắt đầu một lượt kiểm thử

- Build được định danh SHA và không lỗi build; API/FE theo scope lượt chạy truy cập được, health và schema verify đạt.
- DB test, tài khoản/role, operator, fixture và clock đã xác nhận; mock/stub/sandbox mode được ghi rõ.
- Swagger/contract và màn hình thuộc ca đã có; worker cấp vé/đối soát có cách kích hoạt và quan sát.
- Có nơi lưu evidence đã che dữ liệu, người chạy và scope case. Thiếu điều kiện thì Blocked với owner phụ thuộc, không đánh Fail sản phẩm vì môi trường chưa sẵn sàng.

### 5.2 Hoàn tất QA-01

- Có test plan, dữ liệu, truy vết đủ 27 UC, ca chạy được với expected, mẫu bug dùng được.
- Có positive, negative, biên và kiểm quyền; ưu tiên riêng tính đúng tiền/vé/phiên/QR và đồng thời.
- Link/ID/công thức/mốc thời gian thống nhất; đã tự kiểm cấu trúc tài liệu. Review của Tiên và duyệt M01 được ghi riêng khi diễn ra; không tự đánh dấu đã duyệt.

### 5.3 Ra cổng nghiệm thu bản demo

- 27/27 UC Must có ca chức năng đã chạy; tất cả ca Must áp dụng trong ma trận và toàn bộ P0/P1 đạt; S01–S06 đạt trên build bàn giao. Blocked/Not Run của Must chưa đáp ứng điều kiện ra.
- Không còn bug P0/P1 mở; mọi sai lệch tiền, vé, quyền hoặc Journey là lỗi chặn nghiệm thu dù tỷ lệ thấp. P2/P3 còn lại cần owner, tác động/workaround và M01 ghi nhận trước bàn giao.
- Regression liên quan sau fix và các kiểm NFR áp dụng đã có evidence. Khác môi trường hiệu năng tham chiếu phải ghi rõ; không tuyên bố đạt mục tiêu SRS từ cấu hình khác mà chưa đánh giá.
- QA-03 lưu UAT, người nghiệm thu, ngày/build; DOC-01 có cách chạy máy sạch và phục hồi. Mọi giảm scope cần quyết định được ghi nhận, không tự xóa ca Fail khỏi thống kê.

Coverage thiết kế UC = số UC Must có ca / 27. Coverage thực thi UC = số UC Must có ít nhất một ca chức năng đã chạy / 27 (chỉ số này **không** chứng minh toàn bộ UC đạt). Tiến độ case = (Pass + Fail) / tổng ca áp dụng; pass rate = Pass / (Pass + Fail). Báo thêm Blocked/Not Run và mức phủ từng bộ dữ liệu tham số; mẫu số bằng 0 thì ghi N/A. Chỉ loại ca có điều kiện khỏi mẫu số khi điều kiện không áp dụng đã được ghi nhận, không loại vì chưa có chức năng.

## 6 Chỉ tiêu phi chức năng

| Mã SRS | Phương pháp và ngưỡng nghiệm thu |
| --- | --- |
| NFR-001/002/003/007, AC-N01 | 4 vCPU/8 GB/SSD cho app+DB; dữ liệu 10.000 account, 20.000 order, tối đa 20.000 ticket, 100.000 GateEvent; 30 phiên gồm 2 màn scan. Warm-up 1 phút, đo 10 phút, ≥100 mẫu hợp lệ/thao tác. P95 local ≤2s, QR đã lưu ≤1s, báo cáo ≤31 ngày ≤5s; lỗi server bất ngờ ≤1%. Tách mạng/camera/MoMo/email. Export ≤10.000 dòng ≤15s chỉ khi UC34 được nhận scope |
| NFR-004, AC-N02 | 10 callback cùng giao dịch; 2 client quét/đổi quyền đồng thời; một khoản thu/vé/Journey hợp lệ và Administrator cuối được giữ |
| NFR-005, AC-N03 | 401/403, truy cập chéo, file sai nội dung/đuôi/cỡ, OTP sai mục đích/hết hạn, QR cũ, injection/script, HTTPS, không có secret/QR/giấy tờ trong log/export |
| NFR-006, AC-N04 | 360×800, 768×1024, 1366×768; Chrome/Edge desktop hai bản ổn định gần nhất tại ngày thử; Chrome Android/Safari iOS cho hành khách. Ghi version thực tế; bàn phím, focus, nhãn, tương phản ≥4,5:1, không chỉ màu, không che QR/cuộn ngang |
| NFR-008, AC-N05 | Demo 2h không dừng ngoài kế hoạch; backup ngày giữ 7 bản; diễn tập restore DB test: RPO ≤24h, RTO ≤2h; đối chiếu số lượng/checksum và quyền thu hồi |
| NFR-009, AC-N06 | Audit có actor/action/object/time/reason/result/correlation; website ≤5s, queue email ≤1 phút. Email lỗi không đổi tiền/vé; mock outbox không được ghi là email thật đã nhận |
| NFR-010, AC-X03/11 | Idle 30 phút, absolute 8h; poll nền không gia hạn; HTTP MoMo timeout ≥30s, UX 5 phút và đối soát 15 phút tách biệt, Unknown không thành Failed/Paid tùy ý |
| NFR-011, AC-N07 | Kiểm retention với mốc theo bảng SRS 8.3 trên dữ liệu tổng hợp; hoãn hồ sơ mở, audit xóa/ẩn danh, restore không làm lộ lại PII đã xóa |

## 7 Lịch và trách nhiệm

Các mốc kế thừa [TASK-ASSIGNMENT](../planning/TASK-ASSIGNMENT.md); đây là lịch dự kiến, không phải trạng thái đã hoàn thành.

| Mốc năm 2026 | Công việc | Owner và phụ thuộc |
| --- | --- | --- |
| 23–25/11 | QA-01 thiết kế, review ma trận/mẫu bug | My; Tiên review; SRS/UC/BE-18 fixture |
| Đến 04/12 | API freeze; rà lại contract và smoke API | Owner BE, Tuyên QA-02; BE-03/07/08/09/10 và các API Must |
| 07–13/12 | Tích hợp FE, cập nhật dữ liệu UI/camera | Owner FE; API đã merge |
| 15–16/12 | QA-02 API regression Postman/Newman | Tuyên; Thành review; DB seed test |
| 17/12 | Code freeze, E2E luồng chính xanh; nhận fix P0/P1 | Cả nhóm; chốt scope build |
| 17–19/12 | QA-03 E2E/UAT/bug bash, lưu evidence | My; Hoàng review; owner sửa lỗi |
| 20/12; nộp 23/12 | Bàn giao nội bộ và gói nộp | Huy DOC-01; QA cung cấp báo cáo tồn lỗi/kết quả |

## 8 Rủi ro và báo cáo

| Rủi ro | Cách xử lý |
| --- | --- |
| Sandbox/callback hoặc mạng không sẵn sàng | Dùng stub để kiểm trạng thái/race; đánh dấu ca tích hợp MoMo TEST Blocked và chạy lại khi sẵn sàng |
| API/FE/worker chưa triển khai | Ghi Blocked kèm module/owner, chạy phần độc lập; không sửa expected theo màn hình tạm |
| Seed cũ, nhiễm dữ liệu hoặc lệch operator | DB test riêng và alias/run ID; xác nhận schema/fixture trước mỗi suite |
| Tài liệu demo còn quota hoặc nhầm UC | Dùng SRS; ghi khác biệt theo mục 1, tạo yêu cầu sửa tài liệu riêng nếu cần |
| Lỗi thời gian/race khó lặp lại | Clock xác định, barrier, lưu requestId/transId giả lập và correlation đã che |
| Deadline thiếu thời gian | Ưu tiên P0/P1, giảm Stretch qua M01; không bỏ test payment/ticket hoặc che Blocked |

Mỗi lượt gửi báo cáo nội bộ gồm build/env/provider mode, case áp dụng và Pass/Fail/Blocked/Not Run, coverage UC, bug theo priority/severity, quyết định scope và người chịu trách nhiệm blocker. Dùng bảng thực thi cuối TEST-CASES và mẫu bug để giữ liên kết giữa kết quả và bằng chứng.
