# Dữ liệu kiểm thử Metro Ticket Management System

Các alias dưới đây là **hợp đồng dữ liệu QA cần chuẩn bị**, không phải khẳng định seed đang có sẵn. Dùng dữ liệu tổng hợp trên DB test `metro_test_*`; người chạy ghi ID thực, operator, thời điểm và fixture version vào run. Không lấy PII thật hoặc credential production.

## 1 Tài khoản và quyền

| Alias | Dữ liệu và trạng thái cần có |
| --- | --- |
| G | Guest, không bearer/session |
| P1/P2 | Hai Passenger ACTIVE, email đã xác minh, hồ sơ sinh 01/01/1990; chưa có vé/đơn đang xử lý khi bắt đầu ca độc lập |
| PU/PL | Passenger chưa xác minh email / bị Administrator khóa; dùng bản sao riêng cho ca đăng nhập và khóa |
| TI | Ticket Inspector; mã role `StationStaff` cùng Passenger để dùng chức năng cho bản thân |
| A1/A2 | Hai Administrator ACTIVE, email đã xác minh; mã role `Admin`, tập quyền nghiệp vụ phải đáp ứng BR-012; có fixture riêng chỉ còn một admin cho ca bảo toàn admin cuối |
| ST | Passenger có hồ sơ HS/SV VERIFIED, còn hiệu lực tại PaymentStart; bản sao PENDING/REJECTED/EXPIRED |
| SR59/SR60 | Passenger có ngày sinh quanh sinh nhật 60 theo ngày tham chiếu; giấy tờ đã duyệt. Với D=24/11/2026: 25/11/1966 chưa đủ, 24/11/1966 đủ; bản sao chưa duyệt/hết hạn |
| FREE | Chỉ cho ca tích hợp điều kiện: trẻ dưới 6, tài khoản xác minh và quan hệ giám hộ đã duyệt còn hiệu lực, vé giám hộ đáp ứng SRS. Nếu chưa có fixture đúng thì ghi Blocked, không mở rộng UC39/40 UI |
| OP1/OP2 | Hai operator fixture; P1 có membership hợp lệ OP1. Dùng P2/OP2 để kiểm truy cập chéo khi endpoint có operator context |

Email dạng `qa.<run>.<alias>@example.invalid`, mật khẩu hợp lệ 15–64 ký tự sinh riêng cho lượt; không hardcode password/token thật vào evidence. Email `.invalid` chỉ nhận qua mock/dev inbox. Tạo hai phiên/device cho ca revoke; tạo user riêng cho case đổi quyền/lock để tránh làm hỏng case khác.

## 2 Mạng tuyến và giá

| Alias | Dữ liệu cần chuẩn bị |
| --- | --- |
| NET | T01: A–B–C–D–E; T02: C–F–G; C là điểm chuyển tuyến, tổng 7 ga; 3 phút mỗi đoạn, chuyển tuyến 5 phút, hai chiều phục vụ. Đây là fixture theo ví dụ SRS, không thay seed BE-18A 12 ga nếu seed dùng tên khác |
| ALT | Đồ thị thử riêng có ≥2 đường cùng số đoạn, khác số chuyển tuyến; thêm hai đường bằng cả hai tiêu chí để kiểm lựa chọn đồng hạng |
| OFF | Bản sao NET có ga ẩn/ngừng hoặc đoạn ngắt; có vé/lịch sử tham chiếu trước lúc ngừng |
| F1 | Giá VND theo BR-017: lượt 5.000 đoạn đầu +1.000 mỗi đoạn thêm; ngày 30.000; tuần 150.000; tháng 500.000; round 0,5 lên đồng |
| F2 | Bản giá mới có thời điểm hiệu lực khác, dùng để đổi giá giữa Checkout và lần PaymentStart tiếp theo; không sửa snapshot giao dịch đã trả |

Oracle: A→B 1 đoạn = **5.000**; A→B→C→F 3 đoạn = **7.000**, HS/SV **6.300**, cao tuổi đủ điều kiện **5.600**. A→G 4 đoạn = **8.000**, 12 phút chạy +5 phút chuyển = **17 phút**. Ngày/tuần/tháng cao tuổi = **24.000/120.000/400.000**. Cùng đủ hai nhóm chỉ chọn một, không cộng dồn; test round bằng cấu hình giá hợp lệ tạo phần lẻ, ví dụ 5.005 ×90% =4.504,5 → **4.505**.

## 3 Thời gian và vé

| Alias | Dữ liệu và oracle |
| --- | --- |
| D | Ngày nghiệp vụ từ server UTC+7 tại lượt thử. Test mua trước D−1, D, D+7, D+8; D và D+7 hợp lệ nếu điều kiện khác đủ |
| CLOCK | Clock kiểm soát ở harness hoặc fixture mốc tương ứng; lưu cả UTC và UTC+7, dùng độ chính xác timestamp của hệ thống cho ca sát biên |
| BOUND | PaidAt=12/11/2026 23:59:50+07:00: ngày hết tại 13/11 00:00; tuần tại 19/11 00:00; tháng 30 ngày tại 12/12 00:00; đúng mốc là ngoài hiệu lực |
| T1 | Vé lượt cùng ngày A→G chưa dùng, current credential còn hiệu lực, người đi P1 |
| TP | Vé định kỳ còn hiệu lực, không giới hạn số lượt; không có suggestedTrip lúc mua |
| TF | Đơn vé lượt D+1 đã xác minh thu thành công: chờ phát hành, chưa có QR trước 00:00 ngày đi |
| TX/TU | Vé hết hạn chưa có Journey / vé lượt đã kết thúc Journey, USED |
| TJ | Vé có Journey đang mở; bản sao hết hạn trong Journey để kiểm exit và chặn mua/entry mới |
| TR | Vé được thay credential; giữ cả mã cũ đã revoke và mã mới hiện hành; không ghi raw QR vào log/bug |

Mỗi biến thể sản phẩm/thời điểm trong case phải chạy trên bản sao mới. Không đổi thẳng PaidAt hoặc cấp vé bằng SQL sai trạng thái rồi coi là bằng chứng luồng phát hành. Harness có thể seed trạng thái biên hợp lệ và phải ghi rõ đó là component/integration fixture.

## 4 Thanh toán và vận hành

| Alias | Fixture hoặc cách điều khiển |
| --- | --- |
| PAY | Đơn P1, một người/một loại, version đã xác nhận; mã order/attempt/provider transaction là ba tham chiếu riêng |
| PAY-S/PAY-F/PAY-U | Provider stub: xác minh đã thu / kết thúc chắc chắn không thu / kết quả Unknown. Sandbox run riêng dùng MoMo TEST |
| PAY-L/PAY-D | Callback muộn cho version cũ/ngày qua / hai giao dịch khác transId thu 5.000 mỗi giao dịch cho một đơn 5.000 |
| PAY-BAD | Callback sửa chữ ký/order/amount; tạo qua harness tích hợp có kiểm soát, không ghi khóa ký vào tài liệu |
| ISSUE-ERR | Fault injection sau verified payment, trước commit cấp vé; worker có thể chạy lần nữa với clock xác định |
| TRAIN | DT01/DT02 cho T01, DT03/DT04 cho T02; mỗi đoàn 3 toa, 6.000 kg/toa; version giới hạn đoàn 18.000 kg |
| LOAD | Tải chuyến mới nhất: thiếu, 0, 17.999,9, 18.000, 18.000,1 kg; bản sao âm hoặc >1 số lẻ cho validation |
| TRIP-A/TRIP-B | Hai chuyến cùng ngày/tuyến/chiều phù hợp; tại thời điểm scan, B đang nhận khách đúng ga. SuggestedTrip có thể là A; entryTrip phải là B |
| NOTICE | Notice nháp/đang hiệu lực/hết hạn, công khai và có đối tượng; kênh website + mock outbox |
| REPORT | Hai khoản thực thu 5.000 cho một đơn 5.000, một vé; một khoản thừa 5.000 cần hoàn. Thêm biến thể Unknown, hoàn Pending/Confirmed và phát hành Pending; giữ các nhóm tách biệt |

Gate fixture LOAD không thay quota. Mỗi ca scan giữ `requestId` và bối cảnh ga/chiều/trip cố định; ca duplicate dùng lại cùng request, ca double-entry dùng request mới. Khởi chạy đồng thời bằng barrier, chờ cả response rồi đối chiếu dữ liệu đã lưu. Với callback 10 lần, cùng provider transId phải được giữ nguyên.

## 5 Tệp và dữ liệu nhập

- Giấy tờ minh họa JPG/PNG/PDF: 1 tệp, 3 tệp, 4 tệp; đúng giới hạn 5 MB và vượt 1 byte theo cách server định nghĩa MB. Ghi số byte fixture và định nghĩa thực tế; nếu contract chưa chốt MB thập phân hay MiB, ghi câu hỏi yêu cầu thay vì tự chọn expected ở đúng biên.
- Tệp có đuôi hợp lệ nhưng nội dung/MIME khác, tệp thực thi, tệp hỏng và ảnh không đọc được; không dùng mã độc thật.
- Chuỗi thử script/injection vô hại trong tên/tìm kiếm/notice; xác nhận dữ liệu được xử lý như dữ liệu và không chạy/chèn lệnh.
- QR hợp lệ, chuỗi ngẫu nhiên, ảnh mờ, mã đơn giả làm QR, credential đã thu hồi; kiểm camera và upload ảnh.

## 6 Cô lập và bằng chứng

Trước mỗi ca ghi số đơn/thu/vé/credential/Journey/audit liên quan; sau ca so sánh theo ID run và invariant. Không dựa duy nhất vào toast UI hoặc status HTTP 2xx. Cleanup chỉ ảnh hưởng DB/object test của run sau khi lưu evidence đã che; tránh ghi token, raw QR, email code hoặc URL giấy tờ riêng vào issue công khai.
