# Tài liệu bàn giao — Metro Ticket Management System

> **DOC-01** (issue #44) · Cập nhật 17/11/2026 (S0) · Bàn giao nội bộ 20/12/2026 (G5), nộp 23/12.
> Tài liệu này cho người tiếp nhận (hoặc thành viên mới) nắm được: hệ thống đang ở đâu, chạy thế nào, vận hành ra sao.

## 1. Hiện trạng hệ thống (tại 17/11 — hết S0)

**Đã hoàn tất**
- **Database** (`infra/database`): 6 migration SQL thuần (001–006), schema `metro` **93 bảng** + 7 bảng Identity, 14 hàm nghiệp vụ, exclusion constraint, trigger. Đã deploy **Neon production** 16/11, `09_verify.sql` pass. Nguồn chuẩn: **SRS v1.2** (thắng khi lệch UC v1.6).
- **Nền API BE-01** (PR #50): ProblemDetails RFC 9457, validation pipeline FluentValidation, request logging, `/health` + `/health/live`, Swagger + example, CORS từ config, `global.json` pin .NET 9.0.320.
- **CI**: `backend-build`, `frontend-build` (GitHub Actions, main + dev), ruleset review 2 lớp.
- **Quản lý task**: Excel board + `tools/kickoff.py` regenerate `BOARD.md`/`KICKOFF-S0.md`; GitHub Project public.

**Đang làm / kế tiếp** — xem `docs/planning/BOARD.md` (theo deadline từng người). M01: BE-03 (25/11) → BE-08 (04/12) → BE-17 (09/12) → DOC-01 hoàn tất (20/12).

## 2. Cấu trúc repo

```
backend/            .NET 9 (Metro.API / Application / Domain / Infrastructure) + tests
frontend/           React + TypeScript + Vite
infra/
  database/         migrations SQL (nguồn sự thật), build_deploy.py, deploy.sql (artifact),
                    tests/, scripts/09_verify.sql, documentation/ (ERD, DEPLOYMENT_GUIDE…)
  docker/           docker-compose.yml (Postgres 17 + Redis 7 local)
docs/               requirements (SRS/UC .docx), planning, handover, database, api…
tools/kickoff.py    Regenerate BOARD.md/KICKOFF-S0.md từ Excel
.github/workflows/  backend-ci.yml, frontend-ci.yml
```

## 3. Chạy máy sạch

Đầy đủ ở **README §12**. Tóm tắt 4 bước:

1. Cài .NET 9 SDK + Node LTS + (tuỳ chọn) Docker Desktop; clone repo.
2. DB — chọn một:
   - **Local Docker**: `docker compose -f infra/docker/docker-compose.yml up -d` → đặt `ConnectionStrings__DefaultConnection` trong `backend/.env.local` → tạo schema: `psql -v ON_ERROR_STOP=1 -d metro_ticket -f infra/database/deploy.sql`.
   - **Neon**: copy `DATABASE_URL` (+ `AWS_ENDPOINT_URL_S3`, `AWS_REGION`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`) vào `backend/.env.local` (git-ignored).
3. `dotnet run --project backend/src/Metro.API` → kiểm tra `GET /health` = `healthy`, `/swagger` mở được.
4. `cd frontend && npm install && npm run dev` → `http://localhost:5173`.

⚠️ **Không dùng `dotnet ef database update`** — EF Migrations không dùng. Schema quản lý bằng SQL thuần (mục 5).

## 4. Environment variables

| Biến | Dùng | Ghi chú |
|------|------|---------|
| `DATABASE_URL` | Backend đọc trước (Neon URI) | `NeonConnectionString.Resolve` dịch sang Npgsql; fallback `ConnectionStrings__DefaultConnection` |
| `AWS_ENDPOINT_URL_S3`, `AWS_REGION`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | Neon Object Storage (bucket `assets`) | Neon deploy ghi sẵn vào `.env.local` |
| `Frontend__AllowedOrigins` | CORS, phân tách phẩy | mặc định `http://localhost:5173` |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` | Auth (S1) | để trống khi chưa cắm JWT |
| `Payment__*` | MoMo TEST (S1–S2) | không dùng tài khoản thật |
| `ConnectionStrings__DefaultConnection` | Local Postgres | chỉ khi không có `DATABASE_URL` |

Secret thật nằm trong `.env.local` (git-ignored, **không bao giờ commit**). Mẫu: `.env.example`, `backend/.env.example`.

## 5. Database production (Neon) & quy trình migration

- Project Neon: `raspy-butterfly-94762786` (org `org-broad-surf-89465441`), PG 18.7, region ap-southeast-1.
- Branch **`production`** (`br-hidden-grass-b3wdynvr`) là nhánh chính dùng chung. Branch backup: `backup-before-006-2026-11-16` (`br-broad-smoke-b3de4rm9`, no-compute) — giữ làm rollback, có thể xoá sau khi ổn định.
- Bảng theo dõi: `metro.schema_migrations` (version, checksum, applied_at/by).

**Quy trình khi đổi schema (BẮT BUỘC theo thứ tự):**
1. Sửa/tạo file `infra/database/migrations/NNN_*.sql` (không sửa `deploy.sql` tay).
2. Tạo **branch backup** trên Neon trước khi apply.
3. `python -X utf8 infra/database/build_deploy.py` (+ `--check`).
4. Apply: `psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f infra/database/deploy.sql`.
5. Verify: `psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f infra/database/scripts/09_verify.sql`.
6. Chạy `infra/database/tests/*` nếu liên quan business rule.
7. Commit migration + `deploy.sql` (artifact) qua PR.

Deploy production cần user authorization (đã từng làm: "Deploy thẳng lên production" ngày 16/11).

## 6. Quy trình làm việc (Git + review + CI)

- Nhánh: `main` (bảo vệ, luôn xanh) ← `dev` (tích hợp) ← `feature/*`, `chore/*`.
- PR → `dev`; CI `backend-build` + `frontend-build` phải xanh; sau merge chờ CI trên commit mới rồi fast-forward `dev` → `main`.
- Ruleset 2 lớp: `protect-main`/`protect-dev` (status checks, non-fast-forward, deletion — **không bypass**) và `review-gate-*` (1 approval + CODEOWNERS — M01 bypass được).
- M01 tự merge PR của mình; PR thành viên khác bắt buộc qua M01 duyệt.
- CI backend: restore → build Release → test với PostgreSQL 17 thật (service container).

## 7. Task management

- Board chính thức: `docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx` (sheet `SPRINT_BOARD`).
- Sau khi sửa Excel: `python -X utf8 tools/kickoff.py` → regenerate `docs/planning/BOARD.md` + `KICKOFF-S0.md`. Commit cả ba file.
- GitHub Project "Metro Ticket — Kế hoạch & tiến độ" (`PVT_kwHOBTjKsM4Bqjkc`): Status/Start/Target date đồng bộ với Excel; issue #12–#44 ánh xạ task BE/FE/QA/DOC.
- Sprint mốc: S0 16–22/11 · S1 23/11–04/12 · S2 07–17/12 · S3 18–20/12 · nộp 23/12. API freeze 04/12, code freeze 17/12.

## 8. Rủi ro & việc còn tồn (đọc kỹ)

| Rủi ro | Chi tiết | Giảm thiểu |
|--------|----------|------------|
| BE phụ thuộc DB production dùng chung | Endpoint mới đụng bảng thật; ai chết chữ ký ai sửa | Test trên branch Neon trước; backup branch trước mọi thay đổi |
| JWT/MoMo chưa cắm | `Jwt__*`, `Payment__*` trống — auth thật ở S1 | Endpoint nền không cần auth đã chạy |
| `System.IO.Hashing` 11.0 warning | NuGet warns "not tested with net9.0" | Chỉ warning build-transitive, vô hại; đừng thêm dependency mới |
| Swagger chỉ ở Development | Production không lộ docs | Cố ý — đừng bật ở staging công khai |
| `.env.local` chứa secret thật | DATABASE_URL + AWS key | Git-ignored; nếu lộ: rotate key Neon/AWS ngay |

## 9. Checklist nhận bàn giao

- [ ] Clone repo, làm đúng mục 3, `GET /health` = healthy trên máy của bạn.
- [ ] Đọc `docs/planning/TASK-ASSIGNMENT.md` §4.4 (deadline) + §9 (DoD, review).
- [ ] Nhận 1 credential set Neon (hoặc tự tạo branch riêng từ `production`).
- [ ] Chọn task kế tiếp trong `docs/planning/BOARD.md` khối "việc theo người".
- [ ] Đọc `docs/handover/DEMO_SCRIPT.md` nếu tham gia demo.
