# Ma trận và test case theo use case

Baseline: SRS v1.2, ngày soạn 24/11/2026. Mỗi UC dưới đây tham chiếu nhóm **FR-xx** và **AC-UCxx** cùng số trong SRS; các AC-X/N/A bổ sung được ghi ở từng nhóm. Dữ liệu alias và oracle nằm trong [TEST-DATA](TEST-DATA.md), quy tắc nghiệm thu trong [TEST-PLAN](TEST-PLAN.md).

## 1 Quy ước chạy

- Mọi case trong tài liệu có trạng thái ban đầu **Not Run**, chưa có Actual/Evidence. Khi chạy, thêm kết quả vào bảng execution ở mục 7; không đổi expected để hợp thức hóa lỗi.
- `+` là luồng đúng, `−` là từ chối/ngoại lệ, `B` là biên, `R` là quyền/sở hữu, `C` là đồng thời/idempotency. `API` yêu cầu gọi trực tiếp và đối chiếu persisted state; `UI` yêu cầu kiểm trình duyệt. Các lớp `API/UI` phải được ghi riêng trong kết quả.
- Priority của case: P0 chặn tiền/vé/quyền/bất biến; P1 chức năng Must và biên quan trọng; P2 hiển thị/khả dụng. Severity bug đánh giá khi thấy actual, không lấy priority case làm severity mặc định.
- Mỗi hàng có nhiều biến thể (liệt kê bằng dấu chấm phẩy hoặc giá trị) phải chạy **từng biến thể** trên dữ liệu độc lập, lưu hậu tố `/v1`, `/v2`… và actual riêng. Chỉ Pass case cha khi mọi biến thể áp dụng Pass.
- Trước ca cần backend/worker và fixture của nhóm sẵn sàng, phiên đúng role, P1 không có đơn/vé xung đột trừ khi case nói khác. Bước liệt kê theo thứ tự; giữ bằng chứng response và trạng thái DB liên quan. Không tự bịa endpoint chưa có: lấy route từ Swagger của build, ghi route vào run.
- Quyền: G gọi API bảo vệ → **401**; phiên hợp lệ sai role → **403**; sai chủ sở hữu → **403/404 theo contract**, không trả dữ liệu hoặc thay đổi đối tượng. API công khai được truy cập bởi G. Test cross-operator nếu endpoint có operator context. Các lỗi validation/nghiệp vụ kiểm mã theo contract, không buộc mọi lỗi đều 400.

## 2 Ma trận truy vết 27 UC Must

| UC | Chức năng SRS v1.2 | Case | Task phụ thuộc | Lớp |
| --- | --- | --- | --- | --- |
| UC01 | Đăng ký và xác minh email | TC-UC01-01 đến 05 | BE-03, FE-02 | API/UI |
| UC02 | Đăng nhập và phiên | TC-UC02-01 đến 05 | BE-03, FE-02 | API/UI |
| UC03 | Đăng xuất | TC-UC03-01 đến 02 | BE-03, FE-02 | API/UI |
| UC04 | Khôi phục mật khẩu | TC-UC04-01 đến 03 | BE-03, FE-02 | API/UI |
| UC05 | Hồ sơ cá nhân | TC-UC05-01 đến 03 | BE-04, FE-02 | API/UI |
| UC06 | Xác minh ưu đãi | TC-UC06-01 đến 05 | BE-04, BE-06, FE-02 | API/UI |
| UC07 | Tuyến và ga | TC-UC07-01 đến 02 | BE-05, FE-03 | API/UI |
| UC08 | Hành trình | TC-UC08-01 đến 03 | BE-05, FE-03 | API/UI |
| UC09 | Giá vé | TC-UC09-01 đến 04 | BE-06, FE-03 | API/UI |
| UC10 | Tình trạng Metro | TC-UC10-01 đến 03 | BE-05, FE-03 | API/UI |
| UC11 | Mua và phát hành vé | TC-UC11-01 đến 11 | BE-07/08/09, FE-04 | API/UI |
| UC12 | Retry và đối soát | TC-UC12-01 đến 10 | BE-08, FE-04 | API/UI |
| UC13 | Vé của tôi và QR | TC-UC13-01 đến 06 | BE-09, FE-05 | API/UI |
| UC14 | Lịch sử đặt vé | TC-UC14-01 đến 03 | BE-09, FE-05 | API/UI |
| UC15 | Lịch sử thanh toán | TC-UC15-01 đến 03 | BE-08/09, FE-05 | API/UI |
| UC16 | Quét vào và ra | TC-UC16-01 đến 11 | BE-10, FE-06 | API/UI |
| UC17 | Xử lý ngoại lệ | TC-UC17-01 đến 04 | BE-10, FE-06 | API/UI |
| UC18 | Tra cứu tại ga và thay QR | TC-UC18-01 đến 04 | BE-10/11, FE-06 | API/UI |
| UC19 | Hỗ trợ mua vé và tại quầy | TC-UC19-01 đến 03 | BE-07/08/11, FE-06/07 | API/UI |
| UC22 | Tài khoản và quyền | TC-UC22-01 đến 05 | BE-12, FE-07 | API/UI |
| UC23 | Quản lý dữ liệu hành khách | TC-UC23-01 đến 04 | BE-12, FE-07 | API/UI |
| UC24 | Quản lý tuyến và ga | TC-UC24-01 đến 03 | BE-13, FE-07 | API/UI |
| UC25 | Loại vé và phiên bản giá | TC-UC25-01 đến 04 | BE-06, FE-07 | API/UI |
| UC29 | Lịch và hoạt động tàu | TC-UC29-01 đến 04 | BE-13, FE-07 | API/UI |
| UC31 | Thông báo dịch vụ | TC-UC31-01 đến 03 | BE-13, FE-07 | API/UI |
| UC33 | Báo cáo vé và doanh thu | TC-UC33-01 đến 04 | BE-15, FE-08 | API/UI |
| UC36 | Gợi ý chuyến và kiểm chuyến thực | TC-UC36-01 đến 06 | BE-07/10/13, FE-03/04/05/06 | API/UI |

Task phụ thuộc là nơi phối hợp chuẩn bị/chạy, không xác nhận mọi API đã triển khai. Mô tả ngắn BE-11 trên board không thay định nghĩa UC18/19 của SRS. UC không có trong bảng theo phân loại Stretch/Out tại TEST-PLAN, không bị coi là bỏ sót Must.

## 3 Test case chức năng

### UC01 Đăng ký

FR-01, AC-UC01; BR-006–010; AC-X01/02, AC-A01/03.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC01-01 | P1 / + | Email mới; tên/ngày sinh hợp lệ; password 15 ký tự | 1. Đăng ký. 2. Thử login trước xác minh. 3. Nhập OTP đúng còn hạn. 4. Login | Trước xác minh không login; sau xác minh có Passenger ACTIVE; không tự cấp staff/admin |
| TC-UC01-02 | P1 / − B | Email trùng; email sai; tên rỗng; DOB ngày mai; password dài 14/15/64/65 có dấu cách | Gửi từng biến thể đăng ký độc lập | Email trùng/sai, tên rỗng, DOB tương lai, password 14/65 bị từ chối; 15/64 hợp lệ, không bắt buộc pha nhóm ký tự |
| TC-UC01-03 | P0 / B | OTP 6 số; clock tại trước/đúng 5 phút; OTP đã dùng; OTP reset password | Gửi từng OTP xác minh, bao gồm OTP sai mục đích | Chỉ OTP đúng mục đích, chưa dùng và chưa hết hạn hoạt động; đúng mốc 5 phút bị từ chối; không kích hoạt nhầm tài khoản |
| TC-UC01-04 | P1 / B | Clock kiểm soát; email mới | 1. Sai OTP 5 lần. 2. Xin lại trước 60s rồi đúng 60s. 3. Xin mã thứ 4 trong 15 phút. 4. Dùng mã cũ | Lần sai thứ 5 hủy mã; trước 60s/chạm giới hạn 3 mã bị chặn; mã mới vô hiệu mã cũ cùng mục đích; gửi lại email không kéo dài OTP |
| TC-UC01-05 | P0 / R C | Hai client cùng email mới; body thử chèn role Admin | 1. Đăng ký đồng thời. 2. Thử chọn role trái phép | Chỉ một account/email; không có vai trò nâng quyền; request lạ bị từ chối hoặc bỏ qua an toàn theo contract |

### UC02 Đăng nhập

FR-02, AC-UC02; BR-008/011–013; AC-X02/03, NFR-010.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC02-01 | P1 / + R | P1, TI, A1; mật khẩu đúng | Login từng role; gọi chức năng Passenger cho bản thân; A1 gọi nghiệp vụ inspector | Có phiên đúng quyền server; nhân viên dùng chức năng cá nhân; Administrator có nghiệp vụ inspector, không phụ thuộc nút UI |
| TC-UC02-02 | P0 / − | PU, PL; sai password; email không tồn tại | Login từng biến thể rồi gọi API bảo vệ | Không có phiên dùng được; khóa admin/email chưa xác minh không bị bypass; không trả password/hash |
| TC-UC02-03 | P1 / B | Account mới, clock; 4 rồi 5 lần sai | Thử password đúng khi đang chặn; thử trước/đúng 5 phút sau lần sai thứ 5; login đúng rồi sai lại | Sai 5 chặn 5 phút; không bypass bằng password đúng khi còn chặn; hết hạn hoặc login thành công reset bộ đếm |
| TC-UC02-04 | P0 / B | Phiên có t0; kiểm soát clock | 1. Chỉ poll nền đến phút 30. 2. Phiên khác tương tác ở phút 29. 3. Tiếp tục hoạt động đến đủ 8h | Poll không gia hạn idle; idle 30 phút bị 401; tương tác hợp lệ gia hạn idle nhưng đủ 8h luôn hết phiên; không tự replay payment/scan |
| TC-UC02-05 | P0 / R | Bearer thiếu/sai chữ ký/hết hạn; Passenger gọi admin/staff | Gọi trực tiếp các API bảo vệ đại diện | Thiếu/sai phiên →401; sai role →403; không đọc/ghi dữ liệu; không tin role do client gửi |

### UC03 Đăng xuất

FR-03, AC-UC03; BR-011.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC03-01 | P0 / + R | P1 có phiên hiện tại và vé/Journey hợp lệ | Logout; dùng lại access/refresh token phiên vừa logout | Phiên hiện tại không dùng lại được; UI về trạng thái an toàn; vé/Journey không đổi |
| TC-UC03-02 | P1 / − C | Phiên đã logout/hết hạn | Logout lặp; back/reload màn hình bảo vệ | Kết quả an toàn theo contract; phiên không hồi sinh; không lộ nội dung từ cache |

### UC04 Khôi phục mật khẩu

FR-04, AC-UC04; BR-009–011; AC-X01–03.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC04-01 | P0 / + | P1 có 2 phiên; bị chặn sai password tạm thời | Xin OTP reset đúng; đặt password mới; thử token/password cũ và login mới | Password cũ/2 phiên cũ vô hiệu; gỡ chặn tạm; không tự login; password mới login được |
| TC-UC04-02 | P0 / − B | OTP sai/hết 5 phút/đã dùng/sai mục đích; password 14/65; 5 OTP sai; resend 59s và lần thứ 4/15 phút | Gửi từng biến thể độc lập | Không đổi password nếu đầu vào/mã sai; dùng quy tắc OTP cùng UC01; không lộ account/OTP trong response/log |
| TC-UC04-03 | P0 / R | PL bị khóa admin, có OTP hợp lệ | Reset đúng rồi login; dùng token cũ | Đổi password không gỡ khóa admin; login/token vẫn bị chặn |

### UC05 Hồ sơ cá nhân

FR-05, AC-UC05; BR-006/007; NFR-005.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC05-01 | P1 / + | P1 chưa duyệt danh tính/ưu đãi | Xem hồ sơ; sửa tên/DOB hợp lệ và phone; reload | Lưu đúng trường được phép; phone tùy chọn/không bắt buộc duy nhất; không đổi vé đã mua |
| TC-UC05-02 | P1 / − B | Hồ sơ trước/sau duyệt; tên rỗng, DOB tương lai, đổi email | Gửi từng biến thể qua API | Dữ liệu sai/email bị từ chối; sau duyệt chính chủ không tự sửa tên/DOB; không lưu một phần |
| TC-UC05-03 | P0 / R | G; P2; ID hồ sơ P1 | Đọc/sửa trực tiếp hồ sơ P1 | G→401; P2 bị từ chối theo ownership; hồ sơ/giấy tờ P1 không bị lộ hay thay đổi |

### UC06 Xác minh ưu đãi

FR-06, AC-UC06; BR-019/020/071; AC-X33/34, AC-N03/07.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC06-01 | P1 / + | ST/SR60 chưa có hồ sơ duyệt; giấy tờ tổng hợp đúng | Chính chủ nộp; A1 yêu cầu bổ sung, nhận bổ sung rồi duyệt | Lưu lịch sử và lý do; chỉ VERIFIED còn hiệu lực áp giảm; xác minh email độc lập hồ sơ ưu đãi |
| TC-UC06-02 | P1 / B − | 1/3/4 tệp; JPG/PNG/PDF; đúng 5 MB/vượt 1 byte; MIME giả, tệp hỏng | Upload từng bộ; A1 thử hạn >12 tháng hoặc quá hạn giấy tờ | ≤3 tệp đúng định dạng/cỡ được nhận; vượt giới hạn/nội dung giả bị chặn; thời hạn không quá 12 tháng và hạn giấy tờ |
| TC-UC06-03 | P0 / R | G, P2, TI với ID hồ sơ/tệp P1 | Đọc tệp riêng, quyết định duyệt, sửa trạng thái trực tiếp | G→401; sai chủ/sai role bị từ chối; TI không duyệt; không lộ object key/URL riêng hoặc nội dung qua log |
| TC-UC06-04 | P1 / B | SR59/SR60, hồ sơ VERIFIED; vé lượt và ngày/tuần/tháng | Báo giá/PaymentStart tại trước/đúng sinh nhật 60 theo mốc từng loại | Vé lượt tính tuổi ngày đi; định kỳ tính ngày PaymentStart UTC+7; chỉ đủ 60 và hồ sơ hợp lệ mới giảm 20% |
| TC-UC06-05 | P0 / B | Hồ sơ PENDING/REJECTED/EXPIRED; hết hạn giữa Checkout và PaymentStart; hết sau PaymentStart hợp lệ | Chạy từng biến thể và xác nhận giá lại khi cần | Chưa đủ điều kiện dùng giá thường và cần xác nhận; không tự thu giá khác; hồ sơ hết sau PaymentStart hợp lệ/đã trả không hồi tố tiền vé |

### UC07 Tuyến và ga

FR-07, AC-UC07; BR-050/051.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC07-01 | P1 / + | NET công bố; G/P1 | Mở danh sách tuyến, chi tiết ga, điểm C chuyển tuyến | Đúng thứ tự/liên kết/tiện ích công bố, có thời điểm cập nhật; không tạo order |
| TC-UC07-02 | P2 / − | OFF; ID ga không tồn tại; dữ liệu rỗng | Tra từng biến thể | Thông báo không có/không phục vụ đúng; không crash; ga ngừng không thành lựa chọn mua mới; lịch sử vẫn đọc được |

### UC08 Hành trình

FR-08, AC-UC08; BR-042/050/051; AC-X23.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC08-01 | P1 / + | NET; G và P1 | Tra A→G; xem ga/tuyến/chuyển tuyến và tiếp tục mua | 4 đoạn, 1 chuyển tại C, 17 phút; chưa tạo booking/hold khi chỉ tra |
| TC-UC08-02 | P1 / − | Thiếu ga, A→A, ID lạ, OFF mất kết nối | Gửi từng truy vấn | Yêu cầu chọn lại hoặc báo không có đường; không tạo booking/hành trình giả |
| TC-UC08-03 | P1 / B | ALT có nhiều đường | Tra và so các phương án | Ưu tiên ít đoạn rồi ít chuyển; bằng cả hai thì cho chọn, không tự áp một đường chưa chọn |

### UC09 Giá vé

FR-09, AC-UC09; BR-002/017/019/071; AC-X04/33.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC09-01 | P1 / + | F1; P1/G | Báo giá 1/3/4 đoạn và ngày/tuần/tháng | 5.000/7.000/8.000 và 30.000/150.000/500.000 VND; giá có nguồn/version; chưa tạo payment/vé |
| TC-UC09-02 | P1 / B | ST/SR60 VERIFIED; 3 đoạn; từng vé định kỳ | Chọn từng nhóm ưu đãi rồi thử cộng dồn | 6.300 HS/SV hoặc 5.600 cao tuổi; định kỳ 20% là 24.000/120.000/400.000; không cộng dồn |
| TC-UC09-03 | P1 / − | Thiếu giá/giá hết hiệu lực; loại 3 ngày; hồ sơ hết hạn | Tra và thử sửa giá/discount từ client | Thiếu cấu hình báo rõ; không có vé 3 ngày; backend tính giá, không tin tiền/nhóm giảm trái phép |
| TC-UC09-04 | P1 / B | F1 bản sao giá 5.005, ST giảm 10% | Báo giá rồi Checkout | 4.504,5 làm lên 4.505 đồng; báo giá và tổng backend thống nhất, không phí/thuế tự phát |

### UC10 Tình trạng dịch vụ

FR-10, AC-UC10; BR-051/052; AC-X23.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC10-01 | P1 / + | Notice/ETA đã công bố theo ga | G/P1 xem tình trạng theo tuyến/ga | Đúng phạm vi, nguồn/thời điểm, thời gian còn lại và notice ảnh hưởng |
| TC-UC10-02 | P1 / − B | ETA thiếu hoặc đã qua; lịch còn; không còn chuyến phục vụ | Tra từng biến thể | Hiện Chưa có ETA cập nhật/Theo lịch đúng nghĩa; không khẳng định tàu đã đến hoặc giả có chuyến |
| TC-UC10-03 | P2 / − | Notice nháp/hết hạn; API chậm/lỗi rồi hồi phục | Xem, reload và thử lại | Không lộ nháp; lỗi/loading có thể nhận biết; không hiện dữ liệu lỗi như trạng thái phục vụ bình thường |

### UC11 Mua vé và phát hành

FR-11, AC-UC11; BR-003–005/014/018/029–040/071/072; AC-X05–07/10–17/33–35.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC11-01 | P0 / + | P1; F1; T1 chưa tồn tại; PAY-S | Chọn vé lượt hôm nay; xác nhận Checkout; trả MoMo TEST; chờ backend xác minh | Một đơn/người/loại, thu đúng tiền, đúng một vé/current QR; PaidAt là lần backend xác minh đầu; chỉ ghi đã cấp khi vé/QR sẵn sàng |
| TC-UC11-02 | P0 / − R | G, PL; body nhiều người/nhiều loại; sửa amount/người đi từ client | Tạo/confirm/start pay từng biến thể trái phép | Không bắt đầu khoản thu sai quyền/nội dung; backend kiểm người, giá và version; không cấp vé |
| TC-UC11-03 | P1 / B | Clock D; ngày D−1/D/D+7/D+8 | Chọn từng ngày vé lượt và thanh toán D+1 | D, D+7 hợp lệ; quá khứ/D+8 bị chặn; tương lai đã trả chờ cấp, không QR trước 00:00 |
| TC-UC11-04 | P0 / B | TF; clock trước/đúng 00:00 D+1 | Kiểm QR trước mốc; chạy worker ở mốc hai lần, không mở website | Trước mốc không QR; cấp đúng một vé/credential theo lịch; chạy lại không tạo trùng |
| TC-UC11-05 | P0 / B | BOUND, 3 account độc lập cho ngày/tuần/tháng; xác minh thành công qua nửa đêm | Trả từng loại, kiểm start/end; thử entry ngay trước/đúng end | Hiệu lực từ PaidAt, hết đúng mốc TEST-DATA; qua nửa đêm dùng ngày PaidAt mới, không dùng PaymentStart hay responseTime |
| TC-UC11-06 | P0 / − C | P1 có vé định kỳ còn hạn/TF đã trả/TJ hết lịch nhưng chưa đóng; bản sao TU | Thử mua vé chồng hiệu lực; hai yêu cầu đồng thời; thử lại với TU | Chặn xung đột trước thu và khi cấp; không hai vé giữ quyền trùng; TJ còn chặn; TU giải phóng giới hạn |
| TC-UC11-07 | P0 / B | Checkout F1; F2 hoặc hồ sơ ưu đãi đổi trước PaymentStart | Bắt đầu lần thanh toán mới; từ chối rồi xác nhận Checkout mới | Tính lại giá/điều kiện; cần khách xác nhận trước thu; vé đã trả giữ snapshot, không tự đổi số tiền |
| TC-UC11-08 | P0 / − C | PAY đã verified; ISSUE-ERR; clock kiểm soát | Gây lỗi cấp; kích worker mỗi phút đến 15 phút rồi phục hồi | Không yêu cầu trả lần nữa; tối đa một vé; retry không đổi PaidAt; sau 15 phút Cần hỗ trợ, không tự gia hạn/đóng Journey cũ |
| TC-UC11-09 | P1 / B | Order age 24h; vé lượt cuối ngày; provider đang xử lý từ trước hạn | Thử start ngay trước/đúng hết hạn; nhận kết quả cũ sau hạn | Chỉ start khi còn hạn 24h và chưa quá ngày đi; đã bắt đầu vẫn theo dõi; không tự Failed vì đồng hồ |
| TC-UC11-10 | P0 / B − | Giá cấu hình 999/1.000/50.000.000/50.000.001 VND | Start payment trên bản sao độc lập | Chỉ khoảng 1.000–50.000.000 hợp lệ; ngoài khoảng báo cấu hình, không tự nâng tiền, không gọi thu sai |
| TC-UC11-11 | P0 / + C điều kiện | FREE hợp lệ đã chuẩn bị; không kiểm UI UC39/40 | Xác nhận miễn phí; chạy lại tác vụ; nếu tương lai kiểm mốc phát hành | Không gọi MoMo, không tạo payment/PaidAt giả; không cấp trùng; phụ thuộc vé giám hộ và thời điểm đúng. Thiếu fixture ghi Blocked |

### UC12 Retry và đối soát thanh toán

FR-12, AC-UC12; BR-029–037; AC-X11–16, NFR-004/010, AC-A03.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC12-01 | P0 / + B | PAY-F, lần trước chắc chắn không thu | P1 bấm retry đến lần 3, thử lần 4 | Mỗi lần mã riêng, tối đa 3; lần 4 bị chặn; không tự tạo attempt khi chưa bấm |
| TC-UC12-02 | P0 / − B | PAY-U; clock 5 phút rồi 15 phút từ lúc bắt đầu đối soát | Đóng browser; thử retry/sửa/hủy; kích đối soát mỗi phút | 5 phút chuyển đối soát; Unknown không thu/cấp/cho retry; sau 15 phút chuyển admin và tiếp tục theo dõi; không tự Failed |
| TC-UC12-03 | P0 / C | Hai đơn khác ngày cùng người đi; 2 client barrier | Start cả hai đồng thời; đối chiếu attempts | Chỉ một thanh toán đang xử lý cho người đi; không giữ sức chứa và không hai khoản start song song trái phép |
| TC-UC12-04 | P0 / C | PAY-S; cùng transId; 10 callback cùng lúc | Gửi 10 callback; gửi lại sau đó | Một khoản thu và một vé; PaidAt không đổi; mọi response lặp an toàn, không outbox/credential trùng do replay |
| TC-UC12-05 | P0 / − R | PAY-BAD; return URL success; create-payment resultCode=0 chưa xác minh thu | Gửi từng biến thể rồi xem đơn/vé | Không ghi Paid/cấp từ redirect hoặc kết quả tạo link; sai chữ ký/order/amount không cấp; ghi sự kiện và đối soát khi cần |
| TC-UC12-06 | P0 / B | Callback muộn còn đủ điều kiện; PAY-L phiên bản cũ/ngày đi đã qua | Xác minh từng khoản thu, chạy cấp | Còn hợp lệ cấp một lần; không hợp lệ vẫn lưu khoản thực thu và nghĩa vụ xử lý/hoàn lỗi, không cấp theo version mới hoặc ngày đã qua |
| TC-UC12-07 | P0 / C | PAY-D: hai transId thực thu khác nhau cho đơn 5.000 | Nhận cả hai kết quả; xem lịch sử/báo cáo | Tổng thu 10.000, một đơn hợp lệ 5.000, một vé, 5.000 thừa cần hoàn; không làm mất khoản thứ hai |
| TC-UC12-08 | P0 / − R | P2, TI, G; ID payment/attempt P1 | Đọc, retry, giả confirm/duyệt hoàn bằng direct API | G→401, sai role/sở hữu bị từ chối; không giả thu/hoàn; không lộ giao dịch hoặc provider secret |
| TC-UC12-09 | P0 / B | Attempt đang Unknown rồi chắc chắn không thu; order còn hạn | Sửa cặp ga lúc Unknown; sau kết quả cuối sửa và xác nhận lại; callback cũ đến | Unknown cấm sửa/hủy; sau no-charge cho version mới nếu hợp lệ; callback cũ không cấp theo nội dung mới |
| TC-UC12-10 | P0 / + C điều kiện | Khoản thừa cần hoàn; A1; adapter hoàn lỗi sẵn sàng | Duyệt có lý do/bằng chứng; gửi lặp; giữ Pending rồi xác nhận provider | Chỉ admin, toàn bộ về giao dịch gốc; không hoàn trùng; Pending chưa là đã hoàn; quá 15 phút cần hỗ trợ. Kiểm hợp đồng UC12, không nghiệm thu toàn UI UC42 |

### UC13 Vé của tôi

FR-13, AC-UC13; BR-038–046; AC-X05/06/17/22, NFR-005.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC13-01 | P1 / + | P1 có T1/TP/TU/TX/TF | Mở danh sách, lọc, xem từng chi tiết | Đúng trạng thái/loại/hạn/ngày đi và gợi ý; chờ cấp phân biệt đã cấp/đã sử dụng, không tạo vé khi xem |
| TC-UC13-02 | P0 / + C | T1 current QR; browser print/download | Mở lại, tải PNG 2 lần, in; so payload trong harness | Cùng credential, tải/in đọc được, không PII trong QR; mã đơn/mã vé khác QR; raw QR không bị log |
| TC-UC13-03 | P0 / R | G/P2; ID T1, PNG endpoint, OP2 nếu có | Truy cập trực tiếp danh sách/chi tiết/download T1 | Không lấy được QR/metadata ngoài quyền; đúng 401/403/404 theo quy ước; URL/response không lộ giấy tờ |
| TC-UC13-04 | P0 / B | TF trước 00:00; TX; đồng hồ client chỉnh sai | Xem/download QR; chờ worker đúng giờ server | TF không lộ QR sớm; trạng thái/hạn theo UTC+7 server; client clock không mở quyền sử dụng |
| TC-UC13-05 | P0 / − | Paid chờ cấp ISSUE-ERR; retry worker | Mở ví liên tục, bấm refresh, worker chạy lại | Hiện chờ/cần hỗ trợ, không đòi thu lại; refresh không phát hành trùng hoặc nói đã cấp khi thiếu QR |
| TC-UC13-06 | P0 / B | TR thay mã, T1 đang có Journey | Reload/download sau reissue; scan mã cũ/mới | Chỉ mã mới hiện hành; mã cũ bị từ chối; không đổi hạn/Journey hoặc khôi phục TU/TX |

### UC14 Lịch sử đặt vé

FR-14, AC-UC14; BR-016.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC14-01 | P1 / + | P1 có đơn pending/paid/version mới/hủy hợp lệ | Xem danh sách/chi tiết và lịch sử điều chỉnh | Đúng chủ, version và trạng thái; không ghi đè lịch sử; không cấp vé khi xem |
| TC-UC14-02 | P1 / B − | P1 có nhiều trang; user mới không đơn; page/filter sai | Lọc/ngắt trang và gửi tham số sai | Không thiếu/trùng trong dữ liệu cố định; rỗng có thông báo; tham số sai không gây 500 hoặc trả dữ liệu toàn hệ thống |
| TC-UC14-03 | P0 / R | G/P2/OP2; orderId P1 | Gọi list/detail trực tiếp và đổi ID | Chỉ trả lịch sử trong quyền; không QR/PII ngoài quyền; không đổi order |

### UC15 Lịch sử thanh toán

FR-15, AC-UC15; BR-016/029/033/036/037.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC15-01 | P0 / + | REPORT/PAY-D và payment Unknown | Xem từng giao dịch/biên nhận website, đối chiếu attempts | Hiển thị mọi khoản thực thu và thời điểm, không chỉ một khoản theo order; Unknown không là đã thu; không hóa đơn thuế/PDF tự phát |
| TC-UC15-02 | P0 / B | Hoàn Pending rồi provider Confirmed | Xem trước/sau xác nhận và refresh lặp | Pending không ghi đã hoàn; sau xác nhận hiện đúng khoản hoàn, không trừ thu gốc hoặc mất lịch sử |
| TC-UC15-03 | P0 / R | G/P2; paymentId P1; trường provider trả về | Truy cập list/detail/biên nhận trực tiếp | Không lộ lịch sử người khác, chữ ký/secret/token hoặc raw payload nhạy cảm; không cho client đổi status |

### UC16 Quét vào và ra

FR-16, AC-UC16; BR-041–049/055/072; AC-X18–22/37, AC-A03/04.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC16-01 | P0 / + | TI; T1 A→G; B nhận khách A đúng chiều; LOAD=17.999,9 | Camera scan vào A; scan ra G sau hành trình | Allow sau commit; một Journey ghi entryTrip B; chưa USED ngay entry, chỉ USED sau exit; kg không tự đổi |
| TC-UC16-02 | P0 / + B | TP còn hạn; đủ tải | Vào/ra 2 lượt riêng; thử entry mới khi Journey đầu chưa đóng | TP còn dùng sau exit; không giới hạn số lượt hợp lệ; chặn entry thứ hai khi lượt cũ mở |
| TC-UC16-03 | P0 / − B | TX/TU/QR giả/QR revoke/mã đơn; T1 sai ga/chiều; TF trước ngày | Scan mỗi biến thể qua camera/upload | Deny đúng lý do, không tiêu vé/mở Journey hoặc đổi tải; mã đơn không thay QR |
| TC-UC16-04 | P0 / B | T1 mới cho từng ca; LOAD thiếu/17.999,9/18.000/18.000,1 | Scan entry; với Journey có sẵn scan exit khi đầy | Thiếu/≥18.000 Deny entry; dưới ngưỡng tiếp tục kiểm vé; exit hợp lệ không bị chặn do tải |
| TC-UC16-05 | P0 / C | T1, cùng requestId và ga/chiều/trip | Gửi request, gửi lặp sau response; đổi lựa chọn UI trong lúc request cũ đang xử lý | Trả kết quả đã lưu, một event/Journey hợp lệ; bối cảnh request cũ không đổi theo UI |
| TC-UC16-06 | P0 / C | Hai client barrier; T1; requestId khác nhau | Scan entry đồng thời; sau đó exit lặp | Chỉ một entry hợp lệ/Journey mở; exit lặp không thêm lượt; sự kiện gốc không sửa/xóa |
| TC-UC16-07 | P0 / − | Backend commit nhưng mất response; mất mạng trước gửi | Scan trong hai tình huống, khôi phục rồi tra/retry cùng requestId | UI Pending/chưa cho qua khi chưa rõ; tìm kết quả đã lưu trước request mới; không offline Allow hoặc tạo lượt trùng |
| TC-UC16-08 | P0 / B | TJ vào trước hạn, hết hạn lúc đang đi; khóa account sau entry | Scan exit đúng; thử entry mới | Exit lượt vào hợp lệ được hỗ trợ/kết thúc, không tự đóng qua ngày; hết hạn/khóa chặn bắt đầu mới; không gia hạn vé |
| TC-UC16-09 | P0 / − B | Không có trip nhận khách; trip sai ga/chiều; thiếu ETA nhưng trip/tải hợp lệ | Scan entry từng biến thể | Không nhận khách/sai bối cảnh Deny; thiếu ETA riêng không chặn nếu đủ điều kiện; không giả trip đang phục vụ |
| TC-UC16-10 | P0 / R | G/P1; payload scan hợp lệ; TI/A1 cho đối chứng | Gọi API scan trực tiếp với từng role | Chỉ nhân viên có quyền tạo/đóng Journey; scanner không tự có role; G→401, Passenger→403; A1 đáp ứng nghiệp vụ inspector |
| TC-UC16-11 | P1 / + − | Camera cho phép/từ chối; ảnh QR rõ/mờ/hỏng | Dùng camera, từ chối quyền rồi upload ảnh thay thế | Camera/upload cùng kiểm server; ảnh lỗi báo đọc không được; UI không tự Allow; kết quả có chữ/lý do, không chỉ màu |

### UC17 Ngoại lệ soát vé

FR-17, AC-UC17; BR-044/047–049; AC-X21/22.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC17-01 | P1 / + | T1 có entry hợp lệ; ra cùng ga/quá ga/sai ga đã mua | TI xác minh, ghi ga ra thực và lý do, đóng Journey | USED sau exit vé lượt; giữ ga thực/audit; không phụ thu/phạt; TP còn hạn tiếp tục dùng |
| TC-UC17-02 | P0 / − | Thiếu entry hoặc exit; bằng chứng đủ/thiếu; TJ qua ngày | Đề nghị hỗ trợ rồi xem Journey | Chỉ bổ sung khi có căn cứ; thiếu bằng chứng Cần hỗ trợ; không giả timestamp/tự đóng qua ngày hoặc đóng người còn đi |
| TC-UC17-03 | P0 / C B | Scan nhầm chưa thực hiện/chưa lượt kế; đối chứng có lượt hợp lệ kế tiếp | TI hoàn tác có lý do rồi gửi lặp; thử hoàn tác đối chứng | Chỉ ca đủ điều kiện được điều chỉnh bổ sung; sự kiện gốc giữ nguyên; không hồi sinh quyền vé đã dùng thật |
| TC-UC17-04 | P0 / R | G/P1/TI/A1; request thiếu lý do | Gọi API xử lý ngoại lệ mỗi vai trò | G/P1 bị từ chối; nhân viên đúng quyền và đủ căn cứ; thiếu lý do/bằng chứng không cho tùy ý mở/đóng lượt |

### UC18 Tra cứu và cấp lại QR

FR-18, AC-UC18; BR-045/046/049; AC-X01/22.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC18-01 | P1 / + | TI; T1; mục đích hỗ trợ hợp lệ | Tra mã vé/tiêu chí cho phép | Thấy dữ liệu hỗ trợ tối thiểu và trạng thái; mã không tồn tại trả rõ; không trả toàn giấy tờ/secret |
| TC-UC18-02 | P0 / + C | Đối chiếu mã vé/tên/DOB + email OTP; hoặc giấy tờ khi không truy cập email | TI cấp lại, lặp cùng yêu cầu; scan mã cũ/mới; xem Journey/hạn | QR cũ mất hiệu lực ngay; mã mới dùng theo vé gốc; audit/bằng chứng; không đổi Journey/hạn hoặc cấp vé mới |
| TC-UC18-03 | P0 / − B | Chỉ biết mã vé; sai tên/DOB; OTP sai mục đích/hết hạn; TU/TX | Thử thay QR từng trường hợp | Thiếu xác minh bị chặn; thay QR không cho khôi phục USED/entry bằng vé hết hạn; không lộ OTP/QR qua log |
| TC-UC18-04 | P0 / R | G/P1/P2; TI và A1 đối chứng | Gọi staff lookup/reissue trực tiếp | G→401, Passenger→403; TI/A1 đúng nghiệp vụ; không có endpoint cho client tự revoke/mint credential trái phép |

### UC19 Hỗ trợ mua vé

FR-19, AC-UC19; BR-014/020/029/071/072; AC-A03.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC19-01 | P0 / + | A1 phiên riêng, khách P1 xác minh đủ điều kiện | Mua tại quầy, checkbox/ghi chú khách xác nhận; Checkout và MoMo TEST | Ghi riêng actor/người đặt/người đi, không mạo danh login khách; cùng giá/thu/phát hành như UC11, QR trong quyền người đi |
| TC-UC19-02 | P0 / − | Khách chưa xác nhận, chưa đủ điều kiện, hồ sơ hết hạn | A1 thử bỏ xác nhận, bỏ kiểm ưu đãi hoặc tự mark Paid | Dừng trước thu sai; không bỏ qua Checkout/giấy tờ/xác minh provider hoặc dùng tiền mặt/POS |
| TC-UC19-03 | P0 / R | TI hướng dẫn; G/P2 với orderId khách | TI thử mua thay/duyệt ưu đãi/ghi Paid; gọi API trái quyền | TI chỉ hướng dẫn trong quyền; không có quyền admin tài chính/duyệt; G/P2 không lấy order/QR khách |

### UC22 Quản lý tài khoản và quyền

FR-22, AC-UC22; BR-008/012/013; AC-X03, NFR-004/005.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC22-01 | P0 / + | A1; P1 verified có 2 phiên; A2 còn hoạt động | Tìm account, đổi role có lý do; dùng token cũ; login lại | Role thay đúng, mọi phiên cũ bị revoke; login mới đúng quyền, có audit; vẫn dùng Passenger bản thân theo BR-012 |
| TC-UC22-02 | P0 / − B | PU; role không hỗ trợ/trùng; thiếu lý do; chỉ còn A1 active | Gán staff cho PU; gửi role sai; bỏ lý do; khóa/hạ admin cuối | Tất cả bị chặn đúng contract; không mất Administrator cuối hoặc tạo role tùy ý |
| TC-UC22-03 | P0 / C | A1/A2 là hai admin active, hai client barrier | Đồng thời khóa/hạ quyền hai admin; đối chiếu sau commit | Luôn ≥1 admin active; không race làm mất cả hai; không commit dở audit/role |
| TC-UC22-04 | P0 / + B C | P1 có vé/Journey/2 phiên; login/refresh đồng thời lúc khóa | A1 lock; thử access/refresh; unlock rồi login mới | Tất cả phiên cũ vô hiệu kể cả race; vé/Journey giữ; exit đã vào hợp lệ được hỗ trợ; unlock không hồi sinh token/gia hạn |
| TC-UC22-05 | P0 / R | G/P1/TI; A1 replay request cũ sau khi đã login phiên mới | Gọi list/role/status trực tiếp; replay mutation giống hệt | G→401, P1/TI→403; không đọc/ghi admin; replay không tạo tác dụng thành công lặp hoặc revoke phiên mới không cần thiết theo contract |

### UC23 Dữ liệu hành khách

FR-23, AC-UC23; BR-007/011/013; AC-N03.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC23-01 | P0 / + R | A1; P1; purpose code hợp lệ theo contract | Xem hồ sơ với mục đích; đối chiếu response/audit | Chỉ dữ liệu cần thiết/đã che; audit actor/target/purpose/time trước trả dữ liệu; không giấy tờ/URL riêng không thuộc quyền |
| TC-UC23-02 | P0 / + | Hồ sơ P1 đã duyệt, có vé trả tiền và eligibility VERIFIED | A1 sửa tên/DOB có lý do; xem eligibility/lịch sử/vé | Kiểm tra lại hồ sơ liên quan, giữ quyết định cũ; không đổi email, giá/hạn vé, Journey; audit thay đổi không lưu PII thô |
| TC-UC23-03 | P0 / − B | Thiếu/sai purpose; DOB tương lai; đổi email; không lưu được audit | Gọi đọc/sửa từng biến thể; fault injection audit | Không lộ hồ sơ khi thiếu căn cứ/audit bắt buộc thất bại; dữ liệu sai không lưu; không commit một phần thay đổi |
| TC-UC23-04 | P0 / R | G/P1/TI/OP2; endpoint admin P1 | Đọc/sửa/đòi export giấy tờ qua direct API | Chỉ A1 có quyền theo mục đích; G→401, role sai→403; không vượt operator/PII scope |

### UC24 Tuyến và ga

FR-24, AC-UC24; BR-018/050/051.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC24-01 | P1 / + | A1; NET | Thêm/sửa ga/tuyến, liên kết và thứ tự; công bố; G tra lại | Lưu đúng cấu hình/version; catalog/cache phản ánh thông tin công bố; không sai kết nối |
| TC-UC24-02 | P1 / − B | ID/mã trùng; thứ tự/liên kết sai; OFF có giao dịch lịch sử | Gửi cấu hình sai; thử xóa ga tham chiếu, rồi ẩn/ngừng | Không lưu mạng sai; không xóa mất lịch sử; ẩn/ngừng chặn mua mới, lịch sử giữ tham chiếu |
| TC-UC24-03 | P0 / R | G/P1/TI với payload hợp lệ | Gọi API thay đổi mạng tuyến | G→401, role sai→403; mạng/version/cache không bị thay đổi |

### UC25 Loại vé và giá

FR-25, AC-UC25; BR-002/017–019/071; AC-X04/33/34.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC25-01 | P1 / + | A1; F1/F2 | Tạo version giá/hiệu lực hợp lệ; tra tại trước/đúng mốc | Checkout mới dùng version đúng thời gian, vé đã trả giữ snapshot; có lịch sử cấu hình |
| TC-UC25-02 | P1 / − B | Giá âm/thiếu; hiệu lực xung đột; vé 3 ngày; nhóm giảm lạ/cộng dồn | Thử kích hoạt từng cấu hình | Không kích hoạt sai; chỉ bốn sản phẩm/nhóm giảm SRS; không tự sinh thuế/phí |
| TC-UC25-03 | P0 / B | Đơn chưa thu F1, F2 có hiệu lực; đơn khác đã trả F1 | Retry PaymentStart đơn chưa thu; đọc đơn trả F1 | Đơn chưa thu tính lại và xác nhận Checkout; đơn đã trả giữ tiền/hạn/ưu đãi cũ |
| TC-UC25-04 | P0 / R | G/P1/TI | Gọi API quản trị giá/ưu đãi | G→401, sai role→403; không đổi bảng giá/version bằng request trái phép |

### UC29 Lịch và hoạt động tàu

FR-29, AC-UC29; BR-050/052–056/072; AC-X18/23/24/36.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC29-01 | P1 / + | A1; TRAIN, NET, version tải | Tạo trip/chiều/giờ ga; chuyển nhận khách/chạy/ga kế/kết thúc; cập nhật ETA | Có version, actor/time và lịch sử; đúng ga nhận khách/ETA; không diễn giải như điều khiển tàu thật |
| TC-UC29-02 | P1 / − B | Cùng đoàn hai chuyến trùng thời gian; đoàn bảo trì; lịch/cấu hình sai | Tạo từng chuyến không hợp lệ | Từ chối lưu; không gán lịch sai hoặc sửa hồi tố version chuyến đã chạy |
| TC-UC29-03 | P0 / B | Trip đang nhận khách; LOAD 0/17.999,9/18.000/18.000,1/âm/>1 số lẻ | A1 nhập tải từng biến thể; scan entry/exit; chờ không cập nhật | Nonnegative ≤1 số lẻ được lưu kể cả vượt ngưỡng để cảnh báo; âm/sai precision bị chặn; tải gần nhất không tự hết hạn; QR không tự đổi kg |
| TC-UC29-04 | P0 / R B | G/P1/TI; trip/ETA liên quan suggestedTrip | Gọi mutation trái quyền; A1 cập nhật ETA/trạng thái rồi xem gợi ý/Journey | Trái quyền không lưu; thay đổi hợp lệ kích tính lại gợi ý, giữ entryTrip/Journey đã ghi; không quota/hủy vé tự động |

### UC31 Thông báo dịch vụ

FR-31, AC-UC31; BR-057; AC-X31, AC-N06.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC31-01 | P1 / + | A1; NOTICE công khai/nhóm ảnh hưởng | Soạn, xem trước, xác nhận công bố; kiểm G/P1/P2 | Đúng nội dung/phạm vi/hiệu lực; lưu người/version/time; người ngoài scope không thấy notice riêng |
| TC-UC31-02 | P1 / − B | Thiếu nội dung/phạm vi; hết hiệu lực; script vô hại | Thử công bố; xem trước/đúng mốc hết hạn | Không công bố sai; hết hạn không hiển thị như đang hiệu lực; input không thực thi script |
| TC-UC31-03 | P0 / R C | G/P1/TI; A1 hợp lệ; email worker lỗi | Thử mutation trái quyền; A1 công bố/gửi lại lúc email lỗi | Trái quyền bị chặn; công bố hợp lệ giữ nguyên khi email lỗi; đúng người/kênh, không lặp nghiệp vụ/đổi vé hoặc payment |

### UC33 Báo cáo vé và doanh thu

FR-33, AC-UC33; BR-003/033/036/037/061; AC-X14/30/37, AC-N01.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC33-01 | P0 / + | A1; REPORT | Lọc theo ngày/loại/ưu đãi; đối chiếu order/payment/ticket | Hai thu 5.000 =10.000 tổng thu, đơn hợp lệ 5.000, một vé, thừa cần hoàn 5.000; không coi toàn thu thừa là doanh thu đơn |
| TC-UC33-02 | P0 / B | Unknown; refund Pending/Confirmed; paid-chờ cấp; đơn miễn phí nếu có fixture | Chạy từng trạng thái và đối chiếu chỉ số | Unknown không là thu; pending refund không là hoàn; tách đã cấp/chờ và thu thực/hoàn; miễn phí không payment giả |
| TC-UC33-03 | P1 / B | Giao dịch 23:59:59 và 00:00 UTC+7; nhiều entry của TP; gợi ý không entry | Lọc khoảng ngày, loại/nhóm; kiểm lượt và unique account | Biên cuối không gồm 00:00 ngày kế; đếm entry hợp lệ riêng unique account, không cộng booking/gợi ý/scan Deny; rỗng thể hiện đúng nghĩa |
| TC-UC33-04 | P0 / R − | G/P1/TI; bộ lọc sai/ngoài giới hạn contract; OP2 | Gọi API báo cáo trực tiếp | Chỉ admin trong scope; invalid filter không 500 hoặc leak rộng; không PII/QR/secret. Export riêng khi UC34 nhận scope |

### UC36 Gợi ý chuyến và chuyến thực tại gate

FR-36.01–08, AC-UC36; BR-055/072; AC-X35–37, AC-A03/04.

| ID | Ưu tiên / loại | Tiền điều kiện và dữ liệu | Bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-UC36-01 | P1 / + B | Vé lượt hôm nay/tương lai; lịch có chuyến nhận khách/chưa rời ga và chuyến sau | Xác minh thu; xem đơn/vé và nhãn gợi ý | Hôm nay chọn chuyến sớm nhất còn phục vụ từ xác nhận, tương lai chuyến đầu hợp lệ ngày đi; đúng ga/chiều/tuyến đầu; nhãn không giữ chỗ |
| TC-UC36-02 | P0 / C | Hai khách độc lập, cùng trip; worker gợi ý chạy lặp | Xác nhận đơn đồng thời rồi chạy lại tác vụ | Không tranh quota/trừ chỗ; mỗi version đơn tối đa một gợi ý hiện hành; lỗi gợi ý không lặp thu hoặc cấp vé |
| TC-UC36-03 | P1 / − B | TP mua mới; không có trip phù hợp cho vé lượt | Xác minh thu rồi xem kết quả | TP không gán gợi ý; vé lượt hiện Chưa có chuyến phù hợp + thời điểm, không giả trip/hủy/hoàn/gia hạn; TF vẫn chờ QR đúng lịch |
| TC-UC36-04 | P1 / B | SuggestedTrip A đầy/chậm/hủy hoặc ETA đổi; B hợp lệ | Cập nhật dữ liệu, tính lại hai lần, xem lịch sử | Gợi ý mới theo dữ liệu hiện hành, có lịch sử/time; có thể giữ A với giờ mới nếu vẫn phù hợp; không khẳng định giữ chỗ |
| TC-UC36-05 | P0 / + R | T1 gợi ý A; B nhận khách đúng ga/chiều/ngày, tải hợp lệ | TI scan vào B; đổi gợi ý sau entry | Không Deny chỉ vì khác gợi ý; ghi entryTrip B, giữ Journey; quá tải/thiếu tải vẫn Deny entry, exit không bị tải chặn |
| TC-UC36-06 | P0 / B | Đơn chưa thu đổi version hợp lệ; bản sao hủy trước thu; vé đã có Journey | Chạy tác vụ gợi ý trước thu/sau hủy; trả đơn version mới; cập nhật lịch khi vé đã vào | Không gán cho đơn chưa xác nhận/đã hủy; sau thu chỉ version hiện hành có gợi ý; không hồi tố entryTrip/Journey hoặc cho sửa/hủy Unknown |

## 4 Ca kiểm thử phi chức năng

Các ngưỡng và môi trường chi tiết nằm ở TEST-PLAN mục 6 và SRS 9.1. Đây là thiết kế kiểm chứng, chưa phải số đo. Các ca NFR chỉ áp dụng phần chức năng trong scope; không yêu cầu xây mới UI cho UC Out.

| ID | Ưu tiên | Truy vết | Dữ liệu và bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| TC-NFR-01 | P1 | NFR-001/002/003/007, AC-N01 | Dữ liệu tải chuẩn; 30 phiên, 2 màn scan; warm-up 1 phút, đo 10 phút ≥100 mẫu/thao tác; lưu P50/P95/max/lỗi | P95 local ≤2s, QR persisted ≤1s, báo cáo ≤5s; lỗi bất ngờ ≤1%; tách camera/mạng/provider; mọi sai tiền/quyền vẫn Fail |
| TC-NFR-02 | P0 | NFR-004, AC-N02 | Chạy TC-UC12-03/04, TC-UC16-06, TC-UC22-03 bằng barrier; kiểm trước/sau commit | Không trùng thu/vé/Journey, vẫn có admin active; có evidence hai request thực sự chồng thời gian |
| TC-NFR-03 | P0 | NFR-005, AC-N03/A03 | API sở hữu/role từng nhóm; OTP sai mục đích, raw QR trong log, HTTPS, input script/injection, file MIME giả | Không vượt quyền/lộ secret/QR/giấy tờ; không thực thi input; response/error không chứa stacktrace nhạy cảm |
| TC-NFR-04 | P1 | NFR-006, AC-N04 | Chạy S01–S06 tại 360×800/768×1024/1366×768; browser theo plan, desktop camera và upload | Tiếng Việt; không cuộn ngang/che CTA/QR; thời gian UTC+7/tiền VND; ghi browser/version và ảnh |
| TC-NFR-05 | P1 | NFR-006, AC-N04 | Dùng bàn phím cho login/Checkout/ví/scan hỗ trợ; đo contrast, nhãn và focus | Thao tác chính dùng được, lỗi/decision có chữ; contrast văn bản ≥4,5:1; không tuyên bố chứng nhận WCAG |
| TC-NFR-06 | P1 | NFR-008, AC-N05 | Demo 2h, diễn tập backup/restore DB test với 7 bản giữ; đối chiếu số liệu, revoked token/QR | Không dừng ngoài kế hoạch; RPO ≤24h, RTO ≤2h; restore giữ toàn vẹn tiền/vé/quyền và không hồi sinh token/QR |
| TC-NFR-07 | P1 | NFR-009, AC-N06 | Tạo sự kiện mua/duyệt/notice; giả lỗi email, gửi lặp; đọc audit/outbox và website | Website ≤5s, queue email ≤1 phút; đúng người/kênh; audit đủ trường; email lỗi không đổi tiền/vé; mock không chứng minh giao email thật |
| TC-NFR-08 | P0 | NFR-010, AC-X03/11 | Clock idle/absolute; provider chậm/HTTP timeout; đóng và mở lại UI | Idle 30m/absolute 8h enforced server; poll không gia hạn; HTTP MoMo ≥30s; timeout không đổi Unknown thành Paid/Failed hoặc tự POST thu lại |
| TC-NFR-09 | P1 | NFR-011, AC-N07 | Dữ liệu tổng hợp đến hạn theo SRS 8.3; hồ sơ còn mở; chạy purge rồi restore DB test | Chỉ xóa/ẩn danh đúng loại/mốc, hoãn hồ sơ mở, audit; dữ liệu PII đã xóa không lộ lại sau restore |

## 5 Smoke và E2E cho QA03

Chạy bằng UI trên build bàn giao; xác nhận API/DB tương ứng khi liên quan tiền/vé/quyền. Sáu luồng này tổ chức theo demo nhưng dùng expected SRS đã sửa các mô tả cũ về hold/USED ngay khi vào. Case chi tiết vẫn phải chạy, smoke không thay regression.

| ID | Ưu tiên | UC | Dữ liệu và bước chạy | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| S01 | P1 | UC01–06 | Email mới → đăng ký/xác minh → login → sửa hồ sơ → nộp và duyệt ưu đãi → logout/reset | Xác minh/role đúng; hồ sơ/ưu đãi có quyết định; phiên cũ vô hiệu; không đổi giá vé đã trả |
| S02 | P1 | UC07–10/36 | G tra NET A→G, giá F1, tình trạng/ETA; P1 tiếp tục mua theo lựa chọn | 4 đoạn/17 phút/8.000; thông tin cập nhật đúng, tra cứu không giữ chỗ/tạo đơn; không giả ETA |
| S03 | P0 | UC11/12/13/15/36 | P1 Checkout → MoMo TEST no-charge rõ → retry success → ví vé; phát lại callback | Thu một lần hợp lệ/đúng một vé; PaidAt giữ nguyên; QR/gợi ý đúng; không quota/hold |
| S04 | P0 | UC13/16/17/36 | TI scan T1 bằng camera/upload vào A trên B → thử entry mới → ra G → scan lại | Allow sau commit; entry không USED; entry mới bị chặn; exit chuyển USED; không lượt trùng |
| S05 | P0 | UC17/18/19 | TI hỗ trợ ngoại lệ/tra vé, đối chiếu để thay QR; A1 mua tại quầy cho khách có xác nhận | QR cũ revoke, vé/Journey/hạn giữ; TI không mark Paid; mua quầy cùng xác minh thu UC11, actor tách khách |
| S06 | P0 | UC22–25/29/31/33 | A1 quản trị role/mạng/giá/lịch, công bố notice, đối chiếu báo cáo; P1/TI thử API admin | Cấu hình/version/audit đúng; phiên bị revoke khi đổi quyền; notice đúng scope; báo cáo khớp thu/vé/lượt; 401/403 đúng |

## 6 Kiểm quyền theo nhóm

Chạy direct API ít nhất cho **mỗi endpoint đọc/ghi trong scope của build**, không lấy một endpoint đại diện để tuyên bố cả nhóm đạt. Ghi thêm case execution con `/route-...` khi nhóm có nhiều route.

| Nhóm route nghiệp vụ | G | Passenger chính chủ | Passenger khác | Ticket Inspector | Administrator |
| --- | --- | --- | --- | --- | --- |
| Catalog công khai UC07–10 | Cho phép | Cho phép | Cho phép | Theo quyền công khai | Theo quyền công khai |
| Hồ sơ/ví/đơn/payment cá nhân | 401 | Cho phép trong sở hữu/điều kiện | 403/404 | Chức năng Passenger của bản thân | Chức năng Passenger của bản thân; hỗ trợ phải qua nghiệp vụ riêng |
| Duyệt hồ sơ UC06 | 401 | 403 | 403 | 403 | Có điều kiện và audit |
| Gate/tra cứu/thay QR/ngoại lệ UC16–18 | 401 | 403 | 403 | Có điều kiện và audit | Có điều kiện và audit |
| Mua thay tại quầy UC19 | 401 | Mua bản thân qua UC11 | Không có quyền mua thay từ UC19 | Chỉ hướng dẫn trong quyền | Đúng khách/xác nhận/Checkout/thu |
| Admin UC22–25/29/31/33 | 401 | 403 | 403 | 403 | Theo purpose/scope, kiểm admin cuối và audit |
| Callback provider | Không quyết định bằng role người dùng; kiểm chữ ký và tham chiếu provider theo contract | Không tự xác nhận đã thu | Không tự xác nhận đã thu | Không tự xác nhận đã thu | Không thay bằng chứng provider |

## 7 Nhật ký thực thi và bàn giao

Một run có thể lưu bằng file riêng hoặc công cụ QA của nhóm. Không commit secret/QR/evidence thô. Dùng mẫu:

```text
Run ID: QA-YYYYMMDD-<build>-<sequence>
Người chạy / thời gian UTC+7:
Commit SHA / build / schema package / fixture version:
API URL / FE URL / DB test name / operator alias:
Runtime / browser + version / viewport / camera:
Provider mode: stub | MoMo TEST; email: mock | dev inbox
Scope case / biến thể / route; mapping alias → test ID:
Thay đổi scope được ghi nhận / người quyết định (nếu có):
```

| Run ID | Case ID và biến thể | Lớp | Trạng thái | Actual | Evidence và correlation đã che | Bug ID | Người chạy / thời gian |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Chưa tạo run | Chưa thực thi | — | Not Run | — | — | — | — |

Trạng thái: **Not Run** chưa chạy; **Pass** expected khớp và có evidence; **Fail** actual sai khi điều kiện vào đủ; **Blocked** thiếu điều kiện/fixture/phụ thuộc, ghi owner và lý do. Nếu công cụ có Skipped thì không tính Pass, giải thích scope trước khi tính mẫu số. Với ca có điều kiện, ghi điều kiện áp dụng hay quyết định loại khỏi run; thiếu implementation của Must vẫn Blocked.

Evidence tối thiểu: request/response đã che, expected/actual, ID fixture/correlation và persisted state liên quan; UI có ảnh/video theo ca; race có thời điểm cả hai request và counts trước/sau; NFR có môi trường, phương pháp, số mẫu và số đo. Lỗi liên kết [mẫu bug](BUG-REPORT-TEMPLATE.md), retest ghi build mới và regression liên quan.
