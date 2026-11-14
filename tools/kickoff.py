# -*- coding: utf-8 -*-
"""Sinh docs/planning/KICKOFF-S0.md và docs/planning/BOARD.md từ Excel + GitHub issues."""
import json
import subprocess

import openpyxl

XL = r"D:\CODE\metro-ticket-management-system\docs\task-management\Agile_Scrum_Task_Template_Team_6.xlsx"
REPO = "macdogiahuy/Metro-ticket-management-system"
OUT_KICKOFF = r"D:\CODE\metro-ticket-management-system\docs\planning\KICKOFF-S0.md"
OUT_BOARD = r"D:\CODE\metro-ticket-management-system\docs\planning\BOARD.md"

SHORT = {"M01": "Huy", "M02": "Tuyên", "M03": "Hoàng", "M04": "Thành", "M05": "My", "M06": "Tiên"}
HANDLE = {"M01": "macdogiahuy", "M02": "hinouser29", "M03": "lehoangdn2005-png",
          "M04": "22-LeDaiThanh", "M05": "v-hamy165", "M06": "TienNguyen0o0"}

# ---- issue number theo mã task ----
p = subprocess.run(["gh", "issue", "list", "--repo", REPO, "--state", "all",
                    "--limit", "200", "--json", "number,title"], capture_output=True)
issues = {}
for it in json.loads(p.stdout.decode("utf-8")):
    issues[it["title"].split(" ")[0]] = it["number"]

# ---- đọc Excel ----
wb = openpyxl.load_workbook(XL)
ws = wb["SPRINT_BOARD"]
tasks = []
for r in range(2, 202):
    if not ws.cell(r, 5).value:
        continue
    tasks.append({
        "id": ws.cell(r, 1).value,
        "sprint": ws.cell(r, 3).value,
        "epic": ws.cell(r, 4).value,
        "title": ws.cell(r, 5).value,
        "type": ws.cell(r, 6).value,
        "owner": ws.cell(r, 7).value,
        "reviewer": ws.cell(r, 8).value,
        "priority": ws.cell(r, 9).value,
        "status": ws.cell(r, 10).value,
        "sp": ws.cell(r, 11).value,
        "start": ws.cell(r, 13).value.date(),
        "due": ws.cell(r, 14).value.date(),
        "dep": ws.cell(r, 16).value,
    })

s0 = [t for t in tasks if t["sprint"] in ("S0", "S0–S1")]
s0.sort(key=lambda t: (t["start"], t["id"]))


def d(x):
    return x.strftime("%d/%m")


def short(desc, limit=70):
    """Rút gọn tại dấu ':' hoặc ranh giới từ, không cắt giữa từ."""
    head = desc.split(":")[0].strip()
    if 4 <= len(head) <= limit:
        return head
    if len(desc) <= limit:
        return desc
    cut = desc[:limit].rsplit(" ", 1)[0]
    return cut + "…"


# ======================= KICKOFF-S0.md =======================
lines = []
lines += [
    "# Kickoff S0 — 16/11/2026",
    "",
    "| Thông tin | Giá trị |",
    "| --- | --- |",
    "| Thời lượng | 45 phút (online, có ghi màn hình để ai vắng xem lại) |",
    "| Chủ trì | Huy (M01) — Tech Lead |",
    "| Tham dự | Cả 6 người: Huy, Tuyên, Hoàng, Thành, My, Tiên |",
    "| Đầu ra | Chốt 6 điểm kỹ thuật + mỗi người nhận task S0 + repo/issue chạy được |",
    "| Bảng task | `docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx` |",
    "| Kế hoạch | `docs/planning/TASK-ASSIGNMENT.md` (v5.6, bám SRS v1.2) |",
    "| Issue | 33 issue GitHub #12–#44 |",
    "",
    "## 1. Agenda",
    "",
    "| Phút | Nội dung | Người |",
    "| --- | --- | --- |",
    "| 0–5 | Mục tiêu dự án, mốc 16/11 → API freeze 04/12 → code freeze 17/12 → bàn giao 20/12 → nộp 23/12 | Huy |",
    "| 5–15 | Chốt 6 điểm kỹ thuật (mục 3) — mỗi điểm lấy quyết định, ghi vào biên bản | Huy |",
    "| 15–25 | Demo repo: cấu trúc BE/FE, Swagger, DB Neon, cách chạy local | Huy |",
    "| 25–35 | Giao task S0 theo bảng mục 4 + cách cập nhật Excel/Issue | Huy |",
    "| 35–45 | Quy ước Git/PR/review (mục 5), chốt lịch daily, Q&A | Cả nhóm |",
    "",
    "## 2. Mốc phải nhớ",
    "",
    "```",
    "16/11  kickoff, bắt đầu S0",
    "22/11  S0 review: /health + /swagger + EF đọc bảng lõi + seed chạy",
    "04/12  API FREEZE (contract /api/v1), mở FE wave 1",
    "17/12  CODE FREEZE (release candidate v1.0-rc)",
    "20/12  BÀN GIAO nội bộ: demo + tài liệu + UAT sign-off",
    "23/12  NỘP BÀI (21–23/12 chỉ sửa P0, không nhận task mới)",
    "```",
    "",
    "## 3. Sáu điểm phải chốt trong buổi kickoff",
    "",
    "| # | Điểm cần chốt | Phương án đề xuất | Ai quyết |",
    "| --- | --- | --- | --- |",
    "| 1 | .NET version | Giữ `net9.0` (scaffold hiện tại) + pin `global.json`; không nâng .NET 10 giữa kỳ | Huy |",
    "| 2 | DB dùng chung | Neon (schema `metro` + Identity, migration 001–006) cho cả nhóm; ai cần local thì dùng PostgreSQL 17 + `infra/database/deploy.sql` | Huy |",
    "| 3 | Kênh nhận connection string | Nhắn riêng, không commit; `.env.local` đã git-ignore | Huy |",
    "| 4 | MoMo TEST | Dùng sandbox + adapter có mock fallback; callback qua Cloudflare tunnel/ngrok | Huy + Tuyên |",
    "| 5 | Style API | `/api/v1`, ProblemDetails + `code`, camelCase, tiền theo minor unit, phân trang `page/pageSize` | Huy |",
    "| 6 | Lịch làm việc | Daily 21:00 (15'), cập nhật Excel trước 20:45, review thứ Sáu, retro cuối sprint | Cả nhóm |",
    "",
    "> Ghi biên bản ngay vào issue #12 (BE-01) dưới dạng comment để có bằng chứng quyết định.",
    "",
    "## 4. Task S0 giao ngay (16–22/11)",
    "",
    "| Người | Task | Nội dung | Thời gian | Issue |",
    "| --- | --- | --- | --- | --- |",
]
for t in s0:
    desc = t["title"].split("·", 1)[1].strip()
    lines.append("| %s | `%s` | %s | %s–%s | #%s |" % (
        SHORT[t["owner"]], t["id"], short(desc), d(t["start"]), d(t["due"]), issues.get(t["id"], "?")))

lines += [
    "",
    "**Điều kiện thoát S0 (kiểm tra 22/11):**",
    "",
    "- [ ] `dotnet run` lên được, `/health` trả 200, `/swagger` mở được.",
    "- [ ] EF Core đọc/ghi được bảng lõi, có 1 integration test xanh trên PostgreSQL thật.",
    "- [ ] Seed idempotent: chạy 2 lần không nhân đôi dữ liệu.",
    "- [ ] `GET /api/v1/lines`, `/stations` trả dữ liệu seed.",
    "- [ ] CI (build + test) xanh trên PR đầu tiên.",
    "",
    "## 5. Quy ước làm việc (chốt luôn trong buổi)",
    "",
    "| Việc | Quy ước |",
    "| --- | --- |",
    "| Branch | `feature/<mã-task>-<slug>`, ví dụ `feature/be-02a-ef-core-mapping` |",
    "| PR | Vào `dev`, ≤ 400 dòng logic, title `feat(be): ...`, body ghi `Closes #<issue>` |",
    "| Review | Review chéo theo bảng §9.2 `TASK-ASSIGNMENT.md`, 5–6 task/người; SLA 4 giờ; **trừ 5 task của Huy: không review chéo, Huy tự merge sau khi CI xanh** |",
    "| Merge | Chỉ merge khi CI xanh + 1 approval (`dev`) / 2 approval (`main`) |",
    "| Cập nhật tiến độ | Sửa dòng task trong Excel: Status, % Complete, Last Updated, ghi blocker |",
    "| Blocker | Nhắn trong kênh nhóm + tag người gỡ; quá 4 giờ không ai trả lời thì tag Huy |",
    "| Commit | `feat|fix|docs|test|chore(scope): mô tả` — không commit `.env`, không commit `bin/obj/node_modules` |",
    "",
    "## 6. Tin nhắn gửi nhóm (copy nguyên khối)",
    "",
    "```text",
    "📌 KICKOFF S0 — 20:00 hôm nay (16/11), online 45 phút",
    "",
    "Mốc: API freeze 04/12 · code freeze 17/12 · bàn giao 20/12 · NỘP BÀI 23/12.",
    "Board: docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx",
    "Issue: https://github.com/macdogiahuy/Metro-ticket-management-system/issues",
    "Kế hoạch: docs/planning/TASK-ASSIGNMENT.md",
    "",
    "Task S0 (16–22/11) — ai xong thì báo để mình review ngay:",
]
for t in s0:
    desc = t["title"].split("·", 1)[1].strip()
    lines.append("• %s: %s (%s → %s) #%s" % (
        SHORT[t["owner"]], t["id"] + " — " + short(desc, 45), d(t["start"]), d(t["due"]), issues.get(t["id"], "?")))

lines += [
    "",
    "Luật chơi:",
    "1. Mỗi task một người, PR vào dev, không tự merge PR của mình.",
    "2. Daily 21:00, 15 phút: hôm qua làm gì / hôm nay làm gì / đang vướng gì.",
    "3. Cập nhật Excel trước 20:45 (Status + % + blocker).",
    "4. Chưa tới cổng thì để To Do, đừng nhận trước.",
    "",
    "Cần chốt trong buổi: .NET 9 hay 10, DB dùng chung (Neon), MoMo sandbox, style API, lịch daily.",
    "```",
    "",
    "---",
    "",
    "*Sinh tự động từ `SPRINT_BOARD` — chạy lại `python tools/kickoff.py` sau khi đổi lịch.*",
    "",
]
open(OUT_KICKOFF, "w", encoding="utf-8").write("\n".join(lines))

# ======================= BOARD.md =======================
base = "https://github.com/%s" % REPO
lines = []
lines += [
    "# Bảng điều khiển dự án (snapshot 14/11/2026 — kế hoạch v5.6)",
    "",
    "Nguồn sự thật: `docs/task-management/Agile_Scrum_Task_Template_Team_6.xlsx` (sheet `SPRINT_BOARD`).",
    "Kế hoạch chi tiết: `docs/planning/TASK-ASSIGNMENT.md` **v5.6** (bám SRS v1.2 / UC v1.6).",
    "Task trên GitHub: 33 issue **#12–#44**, gán sẵn assignee + milestone.",
    "",
    "## Liên kết nhanh",
    "",
    "| Lọc | Link |",
    "| --- | --- |",
    "| Tất cả task | [issues](%s/issues?q=is%%3Aissue) |" % base,
    "| S0 · Nền tảng | [milestone S0](%s/issues?q=is%%3Aissue+milestone%%3A%%22S0%%22) |" % base,
    "| S1 · API + freeze | [milestone S1](%s/issues?q=is%%3Aissue+milestone%%3A%%22S1%%22) |" % base,
    "| S2 · FE + soát vé | [milestone S2](%s/issues?q=is%%3Aissue+milestone%%3A%%22S2%%22) |" % base,
    "| S3 · UAT + bàn giao | [milestone S3](%s/issues?q=is%%3Aissue+milestone%%3A%%22S3%%22) |" % base,
    "| Backend | [label be](%s/issues?q=is%%3Aissue+label%%3Abe) |" % base,
    "| Frontend | [label fe](%s/issues?q=is%%3Aissue+label%%3Afe) |" % base,
    "| QA | [label qa](%s/issues?q=is%%3Aissue+label%%3Aqa) |" % base,
    "",
    "Theo người (mở issue đang active):",
    "",
    "| Người | Issue đang mở | Đã xong |",
    "| --- | --- | --- |",
]
for m in ["M01", "M02", "M03", "M04", "M05", "M06"]:
    h = HANDLE[m]
    lines.append("| %s (%s) | [open](%s/issues?q=is%%3Aissue+is%%3Aopen+assignee%%3A%s) | "
                 "[closed](%s/issues?q=is%%3Aissue+is%%3Aclosed+assignee%%3A%s) |"
                 % (SHORT[m], m, base, h, base, h))

lines += [
    "",
    "## Trạng thái theo sprint",
    "",
    "| Sprint | Thời gian | Task | SP | Người | Mốc thoát |",
    "| --- | --- | --- | --- | --- | --- |",
]
span = {"S0": "16–22/11", "S0–S1": "19–21/11", "S1": "23/11–04/12",
        "S2": "07–17/12", "S3": "17–20/12"}
goal = {"S0": "nền chạy được", "S0–S1": "catalog có dữ liệu", "S1": "API freeze 04/12",
        "S2": "code freeze 17/12", "S3": "bàn giao 20/12"}
for sp in ["S0", "S0–S1", "S1", "S2", "S3"]:
    grp = [t for t in tasks if t["sprint"] == sp]
    owners = sorted({SHORT[t["owner"]] for t in grp})
    lines.append("| %s | %s | %d | %d | %s | %s |" % (
        sp, span[sp], len(grp), sum(t["sp"] for t in grp), ", ".join(owners), goal[sp]))

lines += [
    "",
    "## Toàn bộ task",
    "",
    "| Issue | Mã | Task | Owner | Reviewer | Sprint | Bắt đầu | Hạn | SP | Trạng thái |",
    "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
]
for t in sorted(tasks, key=lambda x: x["start"]):
    desc = t["title"].split("·", 1)[1].strip()
    lines.append("| [#%s](%s/issues/%s) | `%s` | %s | %s | %s | %s | %s | %s | %d | %s |" % (
        issues.get(t["id"], "?"), base, issues.get(t["id"], ""), t["id"], short(desc, 60),
        SHORT[t["owner"]], SHORT.get(t["reviewer"], "—"), t["sprint"],
        d(t["start"]), d(t["due"]), t["sp"], t["status"]))

lines += [
    "",
    "> Cập nhật cột *Trạng thái* trong Excel (To Do → In Progress → Code Review / QA → Done), "
    "sau đó chạy lại `python tools/kickoff.py` để làm mới bảng này.",
    "",
]
open(OUT_BOARD, "w", encoding="utf-8").write("\n".join(lines))

print("issues:", len(issues), "| s0:", len(s0), "| tasks:", len(tasks))
print("wrote:", OUT_KICKOFF)
print("wrote:", OUT_BOARD)
