# Demo script — Metro Ticket Management System

> **DOC-01** (issue #44) · Nhóm 6 · Bàn giao 20/12/2026 · Nộp bài 23/12/2026
> Demo chính thức ở cổng G5 (S3, 18–20/12). Video dự phòng quay trước demo ≥ 1 ngày.

## 0. Chuẩn bị trước demo (checklist)

| # | Việc | Người | Trạng thái |
|---|------|-------|------------|
| 1 | Seed dữ liệu demo (BE-18A): 2 tuyến, 12 ga, giá mẫu, tài khoản demo | M05 | ☐ |
| 2 | Backend chạy local hoặc Neon production; `GET /health` = healthy | M01 | ☐ |
| 3 | Frontend chạy `npm run dev` (localhost:5173) | M03 | ☐ |
| 4 | Tài khoản demo: 1 passenger, 1 station staff, 1 admin (mật khẩu đã biết) | M05 | ☐ |
| 5 | MoMo TEST credentials nạp trong `.env` (không dùng tài khoản thật) | M02 | ☐ |
| 6 | Quay video dự phòng toàn bộ 6 kịch bản | M01 | ☐ |
| 7 | Kiểm tra màn hình chiếu, timer 20 phút, trình duyệt sạch cache | M06 | ☐ |

**Thời lượng mục tiêu: 18–20 phút** (6 kịch bản, ~3 phút/kịch bản).

---

## Kịch bản 1 — Đăng ký, đăng nhập, hồ sơ (UC01–UC06) · ~3 phút

**Người demo:** M01 (hoặc M03) · **Nói:** "Hành khách bắt đầu bằng tài khoản."

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|------------------|
| 1.1 | Mở `http://localhost:5173` → **Đăng ký** | Form email/họ tên/mật khẩu |
| 1.2 | Nhập email hợp lệ, mật khẩu yếu thử 1 lần | Lỗi validation (ProblemDetails 400 map ra thông báo) |
| 1.3 | Nhập mật khẩu hợp lệ → submit | Tài khoản tạo, email verification được gửi |
| 1.4 | Xác minh email (link trong log/dev inbox) | Tài khoản active |
| 1.5 | Đăng nhập → xem hồ sơ, đổi thông tin | Session/JWT hợp lệ; hồ sơ lưu |

**Fallback:** nếu email verification chưa cắm SMTP — hiện token từ log API và giải thích.

---

## Kịch bản 2 — Tìm tuyến, ga, giá vé (UC07–UC10) · ~3 phút

**Người demo:** M03 · **Nói:** "Hành khách tra cứu trước khi mua."

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|------------------|
| 2.1 | Xem danh sách tuyến (2 tuyến seed) | Ga theo thứ tự, sơ đồ tuyến |
| 2.2 | Chọn ga đi/ga đến, ngày đi | Hành trình + giá theo fare matrix |
| 2.3 | Xem trạng thái dịch vụ (service notices) | Notice hiển thị nếu có |
| 2.4 | (SRS v1.2) Gợi ý chuyến tự động cho vé lượt (UC36) | Suggestion hiển thị, không giữ chỗ |

---

## Kịch bản 3 — Mua vé + thanh toán MoMo TEST (UC11–UC12) · ~3 phút

**Người demo:** M02 · **Nói:** "Điểm cốt lõi: đặt vé và trả tiền sandbox."

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|------------------|
| 3.1 | Chọn sản phẩm vé lượt 1 chặng → **Đặt vé** | Order PENDING, TTL đếm ngược |
| 3.2 | Thanh toán MoMo TEST → thất bại thử 1 lần | Thông báo lỗi, order vẫn PENDING, cho retry |
| 3.3 | Retry thanh toán → thành công | Payment SUCCESS, tickets phát hành |
| 3.4 | Để 1 order khác quá hạn không trả | Order tự huỷ (`cancel_unpaid_order`), vé không phát hành |

**Nhấn mạnh kỹ thuật:** idempotent payment event, retry an toàn, capacity hold + release.

---

## Kịch bản 4 — Ví vé và gate QR (UC13–UC16) · ~3 phút

**Người demo:** M03 · **Nói:** "Vé điện tử và trải nghiệm cổng ga."

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|------------------|
| 4.1 | Vào **Vé của tôi** | QR cho vé đã mua, trạng thái VALID |
| 4.2 | Scan QR (mô phỏng gate) — lần 1 | Ra/into hợp lệ, vé chuyển USED, event ghi xuống afc_events |
| 4.3 | Scan lại cùng QR | Từ chối: vé đã dùng |
| 4.4 | Scan vé hết hạn / không hợp lệ | Từ chối với mã lỗi rõ ràng |

---

## Kịch bản 5 — Nhân viên ga (UC17–UC19) · ~2 phút

**Người demo:** M06 · **Nói:** "Nhân viên xử lý khi hành khách bị kẹt."

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|------------------|
| 5.1 | Đăng nhập tài khoản station staff | Chỉ thấy chức năng của staff (phân quyền) |
| 5.2 | Tra cứu vé theo mã/email hành khách | Thấy vé + trạng thái |
| 5.3 | Cấp lại QR cho vé hợp lệ | QR mới, QR cũ vô hiệu |

---

## Kịch bản 6 — Admin: quản trị & báo cáo (UC22–UC25, UC29, UC33) · ~3 phút

**Người demo:** M04 · **Nói:** "Quản trị và con số cuối cùng."

| Bước | Hành động | Kết quả mong đợi |
|------|-----------|------------------|
| 6.1 | Đăng nhập admin → quản lý ga/tuyến | Sửa thông tin ga, lưu thành công |
| 6.2 | Quản lý sản phẩm vé + giá | Cập nhật giá vào fare matrix (version) |
| 6.3 | Xuất báo cáo vé/doanh thu (UC33) | Báo cáo sinh từ dữ liệu demo |
| 6.4 | Thêm service notice (UC29) | Notice hiện ở khối tra cứu (nhớ lại 2.3) |

**Kết demo:** tổng kết 27 Must-have UC, chỉ lại điểm mạnh (schema 93 bảng, audit, capacity), mời hỏi đáp.

---

## Quản trị rủi ro demo

| Rủi ro | Biện pháp |
|--------|-----------|
| API/DB chết trước demo | Backup branch Neon; chạy local Postgres qua `infra/docker/docker-compose.yml` |
| MoMo sandbox lỗi | Bỏ qua bước 3.2, demo thành công ngay; video dự phòng có đủ luồng |
| Máy demo lỗi mạng | Video dự phòng + ảnh chụp màn hình từng bước |
| Hết giờ | Bỏ kịch bản 5, nhảy thẳng 6.3 để có con số kết thúc |
