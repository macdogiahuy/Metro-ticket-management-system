# Tài liệu bàn giao — Metro Ticket Management System

> **DOC-01** (issue #44) · Cập nhật **28/11/2026** · Bàn giao nội bộ 20/12/2026 · Nộp bài 23/12/2026.
> Người tiếp nhận đọc §3 để chạy máy sạch, §4 để biết biến môi trường, §8 để biết rủi ro còn lại.

## 1. Hiện trạng hệ thống

**Đã hoàn tất (merge vào `dev`)**

- **Database** (`infra/database`): migration SQL thuần **001–017**; `deploy.sql` là artifact được build từ `migrations/` bằng `build_deploy.py`, verify bằng `scripts/09_verify.sql`. Nguồn chuẩn **SRS v1.2** (UC v1.6 là đầu vào hỗ trợ).
- **Backend** BE-01→BE-10 và BE-12→BE-17 (BE-11 còn To Do): nền API ProblemDetails RFC 9457 + validation + logging + `/health` + Swagger; EF mapping; identity/JWT; profile & ưu đãi; catalog & cache; fare engine; booking; payment MoMo TEST; ticket/QR; gate validation; network admin; ETA/load/gián đoạn; báo cáo + CSV; hủy/hoàn/đổi vé; seed demo (2 tuyến, 12 ga, giá mẫu, tài khoản demo).
- **Hardening BE-17** (PR #96): security headers, fixed-window rate limit, PII masking, audit guard; 229 unit + 173 integration test xanh.
- **QA-01**: test plan + ma trận test case + mẫu bug report.
- **Frontend**: scaffold React 19 + TypeScript + Vite; `npm ci && npm run build` xanh (FE-01→FE-09 đang là việc của M02–M06).
- **DOC-01**: `README.md` chạy máy sạch, `DEMO_SCRIPT.md` 6 kịch bản, slide 10 phút + video dự phòng (`slides/`, `video/`), bằng chứng `DOC-01-EVIDENCE.md`.

**Chưa hoàn tất** — xem `docs/planning/BOARD.md`: FE-01→FE-09, QA-02 (Newman), QA-03 (E2E/UAT). BE-11 (hỗ trợ tại cổng) cũng còn To Do.

## 2. Cấu trúc repo

```
backend/            .NET 9 (Metro.API / Application / Domain / Infrastructure) + tests
frontend/           React 19 + TypeScript + Vite + Vitest
infra/database/     migrations SQL (nguồn thật), build_deploy.py, deploy.sql, 09_verify.sql
infra/docker/       docker-compose.yml (Postgres 17 + Redis 7 local)
docs/               requirements, planning, api, database, postman, handover
docs/handover/      HANDOVER.md · DEMO_SCRIPT.md · BE-17-HARDENING.md · DOC-01-EVIDENCE.md
                    slides/ (deck + ảnh) · video/ (MP4 dự phòng)
.github/workflows/  backend-ci.yml, frontend-ci.yml
```

## 3. Chạy máy sạch

Làm theo **README §12**. Tóm tắt 5 bước:

1. Cài .NET 9 SDK (`backend/global.json`), Node LTS, PostgreSQL/Redis (Docker hoặc local).
2. Tạo `backend/.env.local` từ `backend/.env.example`; dùng DB **riêng** (`metro_*`) hoặc branch Neon riêng.
3. Apply schema: `psql -v ON_ERROR_STOP=1 -f infra/database/deploy.sql` rồi verify `09_verify.sql`.
4. `dotnet run --project backend/src/Metro.API` → `GET /health` = healthy, `/swagger` mở được trong Development.
5. `cd frontend && npm ci && npm run dev` → `http://localhost:5173`.

**Không chạy `dotnet ef database update`** — EF Migrations không được dùng.
Kết quả chạy lại trên checkout sạch: `DOC-01-EVIDENCE.md`.

## 4. Biến môi trường

| Biến | Dùng | Ghi chú |
| --- | --- | --- |
| `DATABASE_URL` | API đọc trực tiếp (URI Neon) | `NeonConnectionString.Resolve` đổi sang Npgsql; fallback `ConnectionStrings__DefaultConnection` |
| `AWS_ENDPOINT_URL_S3`, `AWS_REGION`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | Neon Object Storage (bucket `assets`) | Chỉ trên branch Neon đã cấu hình |
| `Frontend__AllowedOrigins` | CORS, phân tách phẩy | Mặc định `http://localhost:5173` |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` | Auth | Production bắt buộc key ≥ 32 ký tự; Development tự tạo key tạm |
| `Payment__*` | MoMo **TEST** | Không dùng tài khoản thật |
| `Hardening__RateLimiting__*` | BE-17 | `PermitLimit` (mặc định 120), `WindowSeconds` (60), `Paths` |
| `METRO_TEST_POSTGRES_*` | Integration test | DB tên `metro_test_*`, `REQUIRED=1` khi nghiệm thu |

Secret chỉ nằm ở `.env.local` (git-ignored). Mẫu: `.env.example`, `backend/.env.example`, `frontend/.env.example`. **Không commit secret.**

## 5. Database & migration

- Schema quản lý bằng SQL thuần; mọi đổi schema phải sửa `infra/database/migrations/NNN_*.sql` rồi build lại `deploy.sql` (`python -X utf8 infra/database/build_deploy.py --check`).
- Quy trình: tạo branch backup → apply `deploy.sql` → chạy `09_verify.sql` → chạy test liên quan → commit qua PR.
- **Production (Neon `production`) hiện chỉ có 001–009 được apply có authorization.** Migration 010–017 nằm trong `deploy.sql`/`dev` nhưng **chưa** được apply production trong workspace này — cần backup + authorization riêng. Không tự apply.

## 6. Quy trình Git & review

- `main` (bảo vệ) ← `dev` (tích hợp) ← `feature/*`, `chore/*`, `docs/*`.
- PR vào `dev`; CI `backend-build` + `frontend-build` phải xanh; Huy (@macdogiahuy) là người duyệt duy nhất, **1 approval + CODEOWNERS** cho cả hai nhánh. PR do Huy tạo tự merge sau CI xanh (ngoại lệ ruleset review); ruleset CI không bypass.
- `main` hiện giữ ở `c8d1988`, `dev` dẫn trước — **không fast-forward `main`** khi chưa có yêu cầu.
- Board: Excel `docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx` → `python -X utf8 tools/kickoff.py` → `BOARD.md`/`KICKOFF-S0.md`. GitHub Project "Metro Ticket - Kế hoạch & tiến độ" đồng bộ Status theo issue.

## 7. Demo & bàn giao

- Kịch bản 6 phần (đăng ký → catalog → thanh toán → QR/cổng → staff → admin): `DEMO_SCRIPT.md`.
- Slide 10 phút: `slides/index.html` (phím `→`, `←`, `N` để hiện ghi chú). Ảnh chụp và video dự phòng: `slides/captures/`, `video/metro-demo-fallback.mp4`. Cách tái tạo: `slides/README.md`.
- Bằng chứng build/test/ảnh: `DOC-01-EVIDENCE.md`.

## 8. Rủi ro & việc còn tồn

| Rủi ro | Chi tiết | Giảm thiểu |
| --- | --- | --- |
| FE/QA chưa xong | FE-01→FE-09, QA-02/QA-03 còn To Do nên demo trực tiếp chưa đầy đủ | Dùng slide + video dự phòng; cập nhật `DEMO_SCRIPT.md` khi xong FE |
| Migration production | 010–017 chưa apply; DB dùng chung dễ lệch schema | Dùng DB riêng; backup branch + authorization riêng trước khi apply |
| Secret `.env.local` | Chứa DATABASE_URL/AWS key thật | Git-ignored; nếu lộ thì rotate key Neon/AWS ngay |
| Lint FE | 1 warning có sẵn trong `NeonStatus.tsx` | Không phải warning mới; xử lý trong FE task liên quan |
| Swagger chỉ Development | Production không lộ docs | Cân nhắc bật staging nếu cần đối soát contract |

## 9. Checklist nhận bàn giao

- [x] Clone repo, chạy README §12 trên máy sạch, `GET /health` healthy.
- [x] Build BE/FE xanh, unit + integration test PostgreSQL thật xanh.
- [x] Đọc `TASK-ASSIGNMENT.md` §4.4 (deadline) + §9 (DoD, review).
- [x] Đọc `DEMO_SCRIPT.md`, mở slide và video dự phòng.
- [ ] Nhận credential Neon riêng (hoặc tự tạo branch từ `production`) khi cần apply migration.
- [ ] Chọn task tiếp theo trong `BOARD.md` (FE/QA/BE-11).
