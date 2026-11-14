# Kickoff S0 — 16/11/2026

| Thông tin | Giá trị |
| --- | --- |
| Thời lượng | 45 phút (online, có ghi màn hình để ai vắng xem lại) |
| Chủ trì | Huy (M01) — Tech Lead |
| Tham dự | Cả 6 người: Huy, Tuyên, Hoàng, Thành, My, Tiên |
| Đầu ra | Chốt 6 điểm kỹ thuật + mỗi người nhận task S0 + repo/issue chạy được |
| Bảng task | `docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx` |
| Kế hoạch | `docs/planning/TASK-ASSIGNMENT.md` (v5.6, bám SRS v1.2) |
| Issue | 33 issue GitHub #12–#44 |

## 1. Agenda

| Phút | Nội dung | Người |
| --- | --- | --- |
| 0–5 | Mục tiêu dự án, mốc 16/11 → API freeze 04/12 → code freeze 17/12 → bàn giao 20/12 → nộp 23/12 | Huy |
| 5–15 | Chốt 6 điểm kỹ thuật (mục 3) — mỗi điểm lấy quyết định, ghi vào biên bản | Huy |
| 15–25 | Demo repo: cấu trúc BE/FE, Swagger, DB Neon, cách chạy local | Huy |
| 25–35 | Giao task S0 theo bảng mục 4 + cách cập nhật Excel/Issue | Huy |
| 35–45 | Quy ước Git/PR/review (mục 5), chốt lịch daily, Q&A | Cả nhóm |

## 2. Mốc phải nhớ

```
16/11  kickoff, bắt đầu S0
22/11  S0 review: /health + /swagger + EF đọc bảng lõi + seed chạy
04/12  API FREEZE (contract /api/v1), mở FE wave 1
17/12  CODE FREEZE (release candidate v1.0-rc)
20/12  BÀN GIAO nội bộ: demo + tài liệu + UAT sign-off
23/12  NỘP BÀI (21–23/12 chỉ sửa P0, không nhận task mới)
```

## 3. Sáu điểm phải chốt trong buổi kickoff

| # | Điểm cần chốt | Phương án đề xuất | Ai quyết |
| --- | --- | --- | --- |
| 1 | .NET version | Giữ `net9.0` (scaffold hiện tại) + pin `global.json`; không nâng .NET 10 giữa kỳ | Huy |
| 2 | DB dùng chung | Neon (schema `metro` + Identity, migration 001–006) cho cả nhóm; ai cần local thì dùng PostgreSQL 17 + `infra/database/deploy.sql` | Huy |
| 3 | Kênh nhận connection string | Nhắn riêng, không commit; `.env.local` đã git-ignore | Huy |
| 4 | MoMo TEST | Dùng sandbox + adapter có mock fallback; callback qua Cloudflare tunnel/ngrok | Huy + Tuyên |
| 5 | Style API | `/api/v1`, ProblemDetails + `code`, camelCase, tiền theo minor unit, phân trang `page/pageSize` | Huy |
| 6 | Lịch làm việc | Daily 21:00 (15'), cập nhật Excel trước 20:45, review thứ Sáu, retro cuối sprint | Cả nhóm |

> Ghi biên bản ngay vào issue #12 (BE-01) dưới dạng comment để có bằng chứng quyết định.

## 4. Task S0 giao ngay (16–22/11)

| Người | Task | Nội dung | Thời gian | Issue |
| --- | --- | --- | --- | --- |
| Huy | `BE-01` | Nền API | 16/11–18/11 | #12 |
| Tuyên | `BE-02A` | EF mapping lõi | 16/11–18/11 | #13 |
| My | `BE-18A` | Seed/fixture | 16/11–17/11 | #16 |
| Tiên | `BE-18B` | Test harness + CI | 16/11–18/11 | #17 |
| Thành | `BE-02B` | EF mapping ticketing/finance | 19/11–20/11 | #14 |
| Hoàng | `BE-05` | Catalog đọc | 19/11–21/11 | #15 |

**Điều kiện thoát S0 (kiểm tra 22/11):**

- [ ] `dotnet run` lên được, `/health` trả 200, `/swagger` mở được.
- [ ] EF Core đọc/ghi được bảng lõi, có 1 integration test xanh trên PostgreSQL thật.
- [ ] Seed idempotent: chạy 2 lần không nhân đôi dữ liệu.
- [ ] `GET /api/v1/lines`, `/stations` trả dữ liệu seed.
- [ ] CI (build + test) xanh trên PR đầu tiên.

## 5. Quy ước làm việc (chốt luôn trong buổi)

| Việc | Quy ước |
| --- | --- |
| Branch | `feature/<mã-task>-<slug>`, ví dụ `feature/be-02a-ef-core-mapping` |
| PR | Vào `dev`, ≤ 400 dòng logic, title `feat(be): ...`, body ghi `Closes #<issue>` |
| Review | Review chéo theo bảng §9.2 `TASK-ASSIGNMENT.md`, 5–6 task/người; SLA 4 giờ; **trừ 5 task của Huy: không review chéo, Huy tự merge sau khi CI xanh** |
| Merge | Chỉ merge khi CI xanh + 1 approval (`dev`) / 2 approval (`main`) |
| Cập nhật tiến độ | Sửa dòng task trong Excel: Status, % Complete, Last Updated, ghi blocker |
| Blocker | Nhắn trong kênh nhóm + tag người gỡ; quá 4 giờ không ai trả lời thì tag Huy |
| Commit | `feat|fix|docs|test|chore(scope): mô tả` — không commit `.env`, không commit `bin/obj/node_modules` |

## 6. Tin nhắn gửi nhóm (copy nguyên khối)

```text
📌 KICKOFF S0 — 20:00 hôm nay (16/11), online 45 phút

Mốc: API freeze 04/12 · code freeze 17/12 · bàn giao 20/12 · NỘP BÀI 23/12.
Board: docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx
Issue: https://github.com/macdogiahuy/Metro-ticket-management-system/issues
Kế hoạch: docs/planning/TASK-ASSIGNMENT.md

Task S0 (16–22/11) — ai xong thì báo để mình review ngay:
• Huy: BE-01 — Nền API (16/11 → 18/11) #12
• Tuyên: BE-02A — EF mapping lõi (16/11 → 18/11) #13
• My: BE-18A — Seed/fixture (16/11 → 17/11) #16
• Tiên: BE-18B — Test harness + CI (16/11 → 18/11) #17
• Thành: BE-02B — EF mapping ticketing/finance (19/11 → 20/11) #14
• Hoàng: BE-05 — Catalog đọc (19/11 → 21/11) #15

Luật chơi:
1. Mỗi task một người, PR vào dev, không tự merge PR của mình.
2. Daily 21:00, 15 phút: hôm qua làm gì / hôm nay làm gì / đang vướng gì.
3. Cập nhật Excel trước 20:45 (Status + % + blocker).
4. Chưa tới cổng thì để To Do, đừng nhận trước.

Cần chốt trong buổi: .NET 9 hay 10, DB dùng chung (Neon), MoMo sandbox, style API, lịch daily.
```

---

*Sinh tự động từ `SPRINT_BOARD` — chạy lại `python tools/kickoff.py` sau khi đổi lịch.*
