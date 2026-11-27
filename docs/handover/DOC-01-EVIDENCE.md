# DOC-01 — Bằng chứng kiểm chứng (28/11/2026)

Issue: #44 · PR implementation: xem liên kết trong PR · Worktree sạch từ `origin/dev` (`e412dc9`).

## 1. README chạy máy sạch

Quy trình trong `README.md` §12 được chạy lại trên checkout sạch (không dùng
`node_modules` có sẵn):

| Lệnh | Kết quả |
| --- | --- |
| `dotnet build backend/Metro.sln -c Release --warnaserror` | **Build succeeded · 0 Warning(s) · 0 Error(s)** |
| `cd frontend && npm ci` | **added 410 packages** (lockfile, 43s) |
| `npm run build` | `tsc -b && vite build` · **✓ built in 213ms**, 80 modules |
| `npm run lint` | 1 warning có sẵn `react(set-state-in-effect)` trong `NeonStatus.tsx`; **không có warning mới** |

Lưu ý đã sửa trong README: `npm install` → `npm ci` cho máy sạch; làm rõ migration
010–017 **chưa** apply production và cần authorization riêng.

## 2. Backend test

| Lệnh | Kết quả |
| --- | --- |
| `dotnet test backend/tests/Metro.UnitTests/Metro.UnitTests.csproj -c Release --no-build` | **229 passed, 0 failed, 0 skipped** |
| Integration trên PostgreSQL riêng `metro_test_be17_*` (`deploy.sql` + `09_verify.sql`, rc=0) | **173 passed, 0 failed, 0 skipped** |

Không dùng Docker, InMemory hay DB dùng chung/production trong các lượt trên.

## 3. Ảnh & video

- `slides/captures/slide-01.png` … `slide-10.png`: ảnh chụp deck 1280×720.
- `slides/captures/slide-01.png` (tiêu đề), `slide-08.png` (biểu đồ bằng chứng),
  `slide-09.png` (trạng thái bàn giao) là các ảnh đại diện đính kèm issue.
- `video/metro-demo-fallback.mp4`: 594 giây, 610.135 bytes, 1280×720, không tiếng —
  dự phòng slide-based cho buổi demo.

## 4. Phạm vi & giới hạn cần nói rõ khi bàn giao

- Backend BE-01→BE-17, QA-01, seed demo và migration 001–017 đã merge vào `dev`.
- Frontend hiện là scaffold (`npm run build` xanh); FE-01→FE-09, QA-02 (Newman)
  và QA-03 (E2E/UAT) vẫn là việc của các thành viên theo `BOARD.md`.
- Vì vậy `DEMO_SCRIPT.md` mô tả kịch bản đích trên FE; video dự phòng dùng slide
  thay cho bản ghi màn hình thật cho đến khi QA-03 xong.
- Demo dùng MoMo **TEST**, không dùng tài khoản thật; secret nằm ở `.env.local`
  git-ignored, không xuất hiện trong ảnh/log/tài liệu này.
