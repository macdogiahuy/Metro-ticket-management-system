# Neon setup (Postgres + Object Storage)

Hướng dẫn cấu hình môi trường cloud cho dự án. Mọi lệnh chạy ở thư mục gốc repo.

## 1. Cấu hình đã triển khai

| Hạng mục | Giá trị |
| --- | --- |
| Tổ chức | `org-broad-surf-89465441` |
| Project | `raspy-butterfly-94762786` |
| Branch | `production` |
| Postgres | database `neondb`, schema `metro` (77 bảng, 5 migration) |
| Object Storage | bucket `assets` (private) |

Cấu hình dịch vụ nằm trong `neon.ts` (Neon infrastructure-as-code). `neon deploy` tạo bucket và ghi biến môi trường vào `.env.local` (hoặc `.env` nếu file đó tồn tại).

## 2. Lệnh thường dùng

```powershell
npm i -g neon@latest
neon deploy            # áp dụng neon.ts + kéo biến môi trường về .env.local
neon config status     # xem dịch vụ hiện có trên branch
neon checkout <branch> --create   # tạo branch mới cho tính năng, tự pull env
```

`neon init` đã chạy và ghi `.neon` (git-ignored) để CLI biết project/branch hiện tại.

## 3. Biến môi trường

| Biến | Dùng cho | Ghi chú |
| --- | --- | --- |
| `DATABASE_URL` | Backend runtime | Chuỗi pooled; dùng cho EF Core/Npgsql |
| `DATABASE_URL_UNPOOLED` | Migration, `psql`, `pg_dump` | Kết nối trực tiếp, không qua pooler |
| `AWS_ENDPOINT_URL_S3` | Object Storage | Endpoint theo branch |
| `AWS_REGION` | Object Storage | Ví dụ `ap-southeast-1` |
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Object Storage | Credential theo branch |

Không commit `.env.local`. Trên môi trường deploy, đặt các biến trên trong phần environment settings của hosting provider.

## 4. Cách backend dùng dịch vụ

- `backend/src/Metro.Infrastructure/Configuration/NeonConnectionString.cs`: đổi URI `postgresql://...` của Neon sang connection string Npgsql (SSL bắt buộc, channel binding, pool tối đa 20).
- `backend/src/Metro.Infrastructure/DependencyInjection.cs`: đăng ký `ApplicationDbContext` với Npgsql và `IObjectStorage`. `DATABASE_URL` được ưu tiên; nếu không có thì fallback `ConnectionStrings:DefaultConnection` cho môi trường local.
- `backend/src/Metro.Infrastructure/Storage/NeonObjectStorage.cs`: AWS SDK S3 với `ForcePathStyle = true` (Neon yêu cầu path-style), bucket lấy từ `ObjectStorage:Bucket` (mặc định `assets`).
- `backend/src/Metro.API/Configuration/EnvFileConfiguration.cs`: đọc `.env.local`/`.env` khi chạy local; biến môi trường thật luôn thắng file.
- `frontend/src/features/neon-status/NeonStatus.tsx`: panel trạng thái gọi `/health` để xác nhận cả hai dịch vụ từ trình duyệt.

Hai endpoint để kiểm chứng:

- `GET /health` — kiểm tra Postgres (đếm migration trong `metro.schema_migrations`) và Object Storage (list bucket); trả `503` nếu một dịch vụ lỗi.
- `GET /api/v1/diagnostics/db` và `POST /api/v1/diagnostics/storage/roundtrip` — chỉ bật ở `Development`; roundtrip upload → download → so sánh → xoá một object tạm.

## 5. Triển khai schema lên Neon

Dùng kết nối trực tiếp để chạy migration, không dùng URL pooled:

```powershell
$url = (Get-Content .env.local | Where-Object { $_ -match '^DATABASE_URL_UNPOOLED=' }) -replace '^DATABASE_URL_UNPOOLED=', ''
psql $url -v ON_ERROR_STOP=1 -f infra/database/deploy.sql
psql $url -v ON_ERROR_STOP=1 -f infra/database/scripts/09_verify.sql
```

Schema hiện đã được deploy lên branch `production`; chạy lại lệnh trên là idempotent (checksum được kiểm tra trong `metro.schema_migrations`).

## 6. Kiểm chứng nhanh

```powershell
curl.exe -s http://localhost:8080/health
curl.exe -s http://localhost:8080/api/v1/diagnostics/db
curl.exe -s -X POST http://localhost:8080/api/v1/diagnostics/storage/roundtrip
```

Kỳ vọng: `status = healthy`, `tables = 77`, `migrations = 5`, roundtrip trả `uploaded/downloaded/matches/deleted = true`.

## 7. Lưu ý vận hành

- Branch Neon là copy-on-write: tạo branch cho từng tính năng/preview, dữ liệu và object storage đi kèm nhau.
- Object Storage chỉ có ở `aws-us-east-1`, `aws-us-east-2`, `aws-eu-central-1`, `aws-ap-southeast-1`; project hiện ở `ap-southeast-1`.
- Bucket `assets` là private: cấp quyền đọc qua presigned URL (`IObjectStorage.CreateDownloadUrlAsync`), không mở public.
- Neon có thể scale-to-zero; request đầu tiên sau khi suspend có thể chậm vài trăm ms — EF Core đã bật `EnableRetryOnFailure(3)`.
