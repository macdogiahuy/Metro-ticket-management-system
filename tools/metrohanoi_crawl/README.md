# Hanoi Metro public domain extraction

Python 3.11+, chỉ dùng thư viện chuẩn. Các module tách riêng HTTP, crawl, chuẩn hóa, schema, quy tắc, export, validation và seed. Chạy từ thư mục gốc repository.

```powershell
python -m tools.metrohanoi_crawl crawl --max-pages 500 --service-dates 2026-11-06 2026-11-07 2026-11-08
python -m tools.metrohanoi_crawl export
python -m tools.metrohanoi_crawl validate
python -m unittest discover -s tools/metrohanoi_crawl/tests
```

Để lấy ảnh chụp mới, dùng `--output output/metrohanoi-YYYY-MM-DD` cho **cả ba lệnh**. Không dùng lại thư mục cache cũ khi muốn làm mới toàn bộ nội dung. Ngày lịch mặc định là ngày hiện tại và hai ngày tiếp theo theo Asia/Saigon; có thể chỉ định `--service-dates`. Snapshot kèm theo được thu thập ngày 06/11/2026 cho ngày dịch vụ 6–8/11/2026.

HTTP chỉ cho phép HTTPS trên metrohanoi.vn, tối thiểu 1,5 giây giữa yêu cầu mạng. Kiểm tra robots trước; HTML tại robots/sitemap được ghi nhận là fallback SPA. URL script, endpoint, payload và khóa nội dung đều lấy từ tài nguyên website công bố. Khóa client trong config công khai chỉ dùng trong bộ nhớ; evidence loại mọi client/map key. Không dùng cookie/session/người dùng. Chỉ gọi API đọc danh sách, chi tiết và tra cứu; một số dùng POST theo giao thức của website. Không gọi API đăng nhập, đơn hàng, phản ánh, thất lạc, hồ sơ ứng viên hoặc tracking. Dừng khi nhận 401/403/429; lưu trạng thái dừng và lý do.

`output/evidence/` lưu phản hồi nguồn và metadata. JSON pointer trong `source_refs` trỏ vào phản hồi tương ứng trong `raw_data.json`. ID độc lập dùng UUIDv5 từ tên bảng và khóa nguồn. Khóa tổng hợp của lịch gồm ID nguồn và ngày dịch vụ. Chuẩn hóa NFC/khoảng trắng; không bịa giá trị thiếu. Các mô tả khác nhau giữa lần thu thập được giữ trong `data_quality_report.json`.

Export tạo 12 JSON theo yêu cầu, `validation_report.json`, `schema.sql` và `seed_reference.sql`. DDL dùng schema `metro_reference`; các bảng giao dịch/tài khoản/audit là đề xuất và không có bản ghi nguồn. Seed chỉ gồm ảnh chụp dữ liệu tham chiếu; vẫn giữ các bất thường đã báo cáo. Dùng database nghiên cứu rỗng để nạp:

```powershell
psql -v ON_ERROR_STOP=1 -d metro_research -f output/schema.sql
psql -v ON_ERROR_STOP=1 -d metro_research -f output/seed_reference.sql
```

Không dùng seed làm dữ liệu vận hành trước khi xác minh hạn tháng lịch, ngưỡng miễn phí theo tuổi, biểu giá/phương thức thanh toán, giá ưu tiên và bất thường lịch. URL 128 tệp doanh nghiệp được ghi lại; nội dung PDF/DOCX không được tải hay phân tích.

Để kiểm tra SQL có thể lặp lại, chọn database nghiên cứu rỗng và cấu hình xác thực PostgreSQL qua PGPASSFILE hoặc biến môi trường PGPASSWORD. Lệnh sau **nạp schema và seed**, từ chối nếu schema `metro_reference` đã tồn tại, đối chiếu số dòng của mọi bảng, PK/FK/UNIQUE/CHECK và index:

```powershell
python -m tools.metrohanoi_crawl.postgres_verify --database metro_research --user metro_researcher
python -m tools.metrohanoi_crawl validate
```

Có thể chỉ định `--host`, `--port`, `--psql` và `--output`. `postgresql_verification.json` ghi phiên bản engine, kết quả kiểm tra và SHA-256 của các tệp đã thử. Validation chỉ công nhận bằng chứng khi hash và số dòng còn khớp; sau khi thay dữ liệu, cần kiểm tra lại trong database rỗng khác. Snapshot kèm theo đã nạp và đối chiếu thành công trên PostgreSQL 17.6. Máy chủ kiểm tra tạm đã được dừng sau khi kiểm tra.

Đóng gói snapshot tại `output/` bằng `python -m tools.metrohanoi_crawl.package`. Gói gồm dữ liệu, bằng chứng đã loại các trường cá nhân, báo cáo và mã crawler; có `delivery_manifest.json` ghi SHA-256 từng tệp và tệp `.zip.sha256` bên ngoài. Không đưa database tạm, mật khẩu, bytecode hay mã ứng dụng có sẵn vào ZIP.
