# Mẫu báo lỗi và quy trình xử lý

Dùng mẫu này khi actual khác expected của SRS trên môi trường đã đủ điều kiện chạy. Liên kết lỗi với UC, test case, run và build để owner tái hiện và QA retest. Thiếu API/fixture/môi trường thì ghi **Blocked** trong run; chỉ tạo bug sản phẩm khi có bằng chứng sai hành vi hoặc lỗi môi trường thuộc scope cần sửa.

Form tương ứng tại [bug_report.yml](../../.github/ISSUE_TEMPLATE/bug_report.yml). Form dùng được trên GitHub sau khi nhánh chứa template được tích hợp; QA-01 không tạo issue hoặc gửi thông báo tự động.

## 1 Severity và priority

Severity mô tả **tác động**; priority mô tả **thứ tự sửa**. QA đề xuất cả hai, owner và M01 triage theo tác động/phạm vi demo. Không tự hạ severity vì có workaround.

| Severity | Định nghĩa | Ví dụ |
| --- | --- | --- |
| S1 Critical | Sai tiền/vé/quyền/Journey, mất hoặc lộ dữ liệu nhạy cảm, phá bất biến | Callback trùng cấp hai vé; Passenger lấy QR người khác; mất admin cuối |
| S2 Major | Luồng Must chính không hoàn thành, chưa thấy phá bất biến S1 | Thanh toán hợp lệ nhưng luôn kẹt phát hành; scan hợp lệ luôn lỗi |
| S3 Moderate | Chức năng bị ảnh hưởng có workaround, không sai bất biến | Bộ lọc lịch sử sai một điều kiện, vẫn tra được chi tiết đúng |
| S4 Minor | Lỗi trình bày/nội dung nhỏ không cản thao tác | Lỗi chính tả hoặc căn lề ngoài vùng QR/CTA |

| Priority | Khi dùng |
| --- | --- |
| P0 | Chặn kiểm thử/bàn giao ngay: S1 hoặc sự cố khiến toàn bộ hệ thống không dùng được |
| P1 | Phải sửa trước nghiệm thu demo: lỗi chức năng Must, bảo mật, thời hạn hoặc điều kiện quan trọng |
| P2 | Sửa theo kế hoạch; tồn tại khi bàn giao cần owner/tác động/workaround và M01 ghi nhận |
| P3 | Cải thiện nhỏ, xếp sau P0–P2; vẫn cần ghi tác động nếu còn tồn |

Chỉ tiêu hiệu năng không đạt cần ghi môi trường, số mẫu và phép đo; không gán S4 vì chưa quan sát lỗi tiền. Tất cả sai lệch tiền/vé/quyền/Journey chặn nghiệm thu theo test plan dù chỉ tái hiện một lần.

## 2 Nội dung sao chép khi báo lỗi

```markdown
### Tiêu đề
[<Module>][<UC>] <hành động hoặc điều kiện> gây <kết quả sai cụ thể>

### Truy vết
- UC / FR / AC / BR: <ví dụ UC12, FR-12, AC-X13, BR-033>
- Case ID và biến thể: <TC-UC12-04/v1>
- Run ID: <QA-YYYYMMDD-build-sequence>
- Issue/task liên quan: <ID/link>

### Phân loại đề xuất
- Severity: <S1/S2/S3/S4 và tác động>
- Priority: <P0/P1/P2/P3 và lý do>
- Module / owner đề xuất: <module/người, có thể chưa triage>
- Tần suất: <số lần gặp / số lần thử, đồng thời hay nối tiếp>

### Môi trường
- Commit SHA / build / schema package / fixture version:
- API URL / FE URL / DB test name / operator alias:
- OS / runtime / browser + version / viewport / camera:
- Provider: <stub hoặc MoMo TEST>; email: <mock/dev inbox>
- Thời điểm UTC+7 / server clock nếu kiểm biên:

### Tiền điều kiện và dữ liệu
<Role, alias fixture, trạng thái trước lỗi, order version/attempt/requestId giả lập>
<Không đưa access/refresh token, secret, OTP, raw QR hoặc giấy tờ cá nhân vào issue>

### Bước tái hiện
1. ...
2. ...
3. ...
<Nếu race: ghi cách đồng bộ/barrier và số client, không chỉ nói click nhanh>

### Kết quả mong đợi
<Giá trị, trạng thái và invariant cụ thể; dẫn mục SRS/AC>

### Kết quả thực tế
<HTTP/error code đã che, giá trị/count thực, trạng thái DB và UI; phần khác expected>

### Bằng chứng
<Link ảnh/video, request/response đã che, correlation ID, counts trước/sau, log hoặc TRX>
<Không công khai file đính kèm nhạy cảm; chỉ chia sẻ bản tổng hợp/đã che>

### Ảnh hưởng và workaround
<Ai/luồng nào bị ảnh hưởng, điều kiện; workaround an toàn hoặc chưa có>

### Retest và regression
- Fix commit / build / PR: <owner bổ sung>
- Người retest / thời điểm / môi trường:
- Case/biến thể chạy lại và actual/evidence:
- Regression liên quan và kết quả:
- Kết luận: <Verified/Closed hoặc Reopened; người quyết định>
```

## 3 Ví dụ minh họa cách điền

**Ví dụ giả định để hướng dẫn, không phải lỗi đã quan sát trong repo.**

| Trường | Nội dung minh họa |
| --- | --- |
| Tiêu đề | [Payment][UC12] Callback cùng giao dịch gửi đồng thời tạo hai vé |
| Truy vết | TC-UC12-04/v1; AC-X13; BR-033; run QA-20261124-example-01 |
| Phân loại | S1 / P0: phá bất biến một khoản thu hợp lệ không cấp trùng vé |
| Môi trường | Build `<sha>`; DB `metro_test_example`; provider stub; clock 24/11/2026 10:00+07:00 |
| Tiền điều kiện | P1 có đơn 5.000 VND, một người/một loại; chưa có vé; stub có kết quả verified thành công |
| Bước | 1. Chuẩn bị cùng transId giả lập. 2. Dùng barrier gửi 10 callback. 3. Đợi tất cả response và worker kết thúc. 4. Đếm thu/vé/credential theo order |
| Expected | Một khoản thu, một vé, một current credential; PaidAt không đổi khi gửi lặp |
| Actual giả định | Một khoản thu nhưng hai vé/current credential; giữ counts và correlation để tái hiện |
| Evidence cần có | Response đã che, timestamp 10 request, query counts trước/sau; không đính QR hoặc khóa ký |
| Tần suất | `<số lỗi>/<số lần chạy thực tế>`; không tự điền số chưa đo |
| Regression sau sửa | TC-UC11-01/04/06/08; TC-UC12-03/04/06/07; TC-UC13-01; TC-UC33-01 |

## 4 Vòng đời và retest

`New → Triaged → In Progress → Ready for Retest → Verified → Closed`. Retest còn lỗi chuyển `Reopened → In Progress`. Duplicate liên kết lỗi gốc; Cannot Reproduce ghi build/dữ liệu/cách đã thử và trao đổi lại QA trước khi đóng. Deferred cần lý do, owner và mốc xem lại; lỗi P0/P1 chưa đạt điều kiện bàn giao không được coi là đã giải quyết vì chuyển Deferred.

Owner sửa ghi nguyên nhân, fix build và vùng ảnh hưởng. QA chạy lại đúng biến thể gây lỗi trên build sửa, thêm case lân cận/race và smoke liên quan; giữ cả evidence fail cũ và pass mới. Chỉ ghi Verified khi actual khớp expected và regression cần thiết đạt; đóng theo quy trình review nhóm. Status trên là quy ước xử lý, không khẳng định GitHub Project đã có các cột/automation tương ứng.
