# Hiệu năng đăng nhập & mức log — bằng chứng

## Vấn đề
Benchmark trước đó (28/11/2026) ghi nhận `POST /api/v1/auth/login` chỉ ~16–18 req/s
và p50 ~588 ms ở 10 luồng, dù PBKDF2 chỉ ~60 ms/request. Log cũng ~2,5 KB/request.

## Nguyên nhân & sửa
- `AuthService.LoginAsync` giữ advisory lock `pg_advisory_xact_lock(userId)` **trong
  lúc verify mật khẩu PBKDF2**, nên mọi login cùng tài khoản bị serialize.
  Đã chuyển `FindByIdAsync` + `CheckPasswordAsync` + kiểm tra `EmailConfirmed`
  ra **trước** lock; trong lock chỉ còn `RequireActiveAsync` + phát hành session —
  vẫn bảo vệ đúng thao tác ghi/trạng thái.
- `appsettings.json` bổ sung `Microsoft.EntityFrameworkCore: Warning` để không ghi
  từng câu lệnh SQL ở mức Information.

**Không thay đổi số vòng PBKDF2 hay bất kỳ tham số bảo mật nào.**

## Đo lại (cùng máy, DB PostgreSQL riêng đã deploy + seed)

| Kịch bản | Trước | Sau |
| --- | --- | --- |
| 40 login, 1 luồng | ~14 req/s, p50 ~60 ms | ~14 req/s, p50 ~60 ms |
| 40 login, 10 luồng cùng user | ~18 req/s, p50 ~588 ms | **~88 req/s, p50 ~93 ms** |
| 40 login, 20 luồng | ~18 req/s | rate limit trả 429 đúng cấu hình (120/60s) |
| Log sau 80 login | ~2.625 B/request (có SQL EF) | **~248 B/request** (chỉ request log + 15 warning validation lúc startup) |

Đo với content root đúng (`backend/src/Metro.API`) và `ASPNETCORE_ENVIRONMENT=Production`.

## Kiểm chứng
- `dotnet build backend/Metro.sln -c Release --warnaserror`: **0 warning / 0 error**.
- Unit: **236 passed** · Integration: **177 passed, 0 failed, 0 skipped**.
- Test mới `Concurrent_logins_for_one_account_issue_distinct_valid_sessions`: 8 login
  đồng thời → 8 token/refresh/session khác nhau, đều dùng được `GET /auth/me`.
- Không migration, không secret/PII trong log; không đụng DB dùng chung/production.
