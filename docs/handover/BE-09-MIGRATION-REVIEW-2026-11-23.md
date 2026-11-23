# BE-09 — Sửa blocker migration PR #77

Ngày 23/11/2026 (Asia/Saigon). Issue #25; contract thanh toán liên quan #26.
PR duy nhất: [#77](https://github.com/macdogiahuy/Metro-ticket-management-system/pull/77), nhánh `feature/be09-08-integration-handover`, đích `dev`.

## Thay đổi

- Hợp nhất `origin/dev` tại `3c70bf4`, đã chứa PR #64 (BE-04 profile).
- Giữ nguyên source và checksum của **001–009** từ `dev`; không ghi đè ledger production.
- Đổi tên ba migration BE-09 thành **010_ticket_issuance.sql**, **011_paid_ticket_issuance.sql**, **012_free_child_ticket_issuance.sql**. So sánh SHA-256 raw file trước/sau: nội dung không đổi. Builder tính checksum theo nội dung SQL; đổi số/tên chỉ thay metadata của BE-09 trong deploy, không sửa checksum của BE-04.
- Sinh lại `deploy.sql` bằng `build_deploy.py`; `09_verify.sql` yêu cầu migration **012**, giữ các kiểm tra profile của BE-04 và kiểm tra bảng issuance BE-09.
- Swagger dùng chung security scheme `Bearer`, giữ cả profile authorization filter và ticket contract filter. Runner giữ các nhóm kiểm thử payment/ticket và thêm nhóm profile.
- Cập nhật README và hướng dẫn BE-09; số liệu 106/113 test trong báo cáo 20–22/11 là lịch sử, bảng dưới là kết quả sau hợp nhất.

## Kiểm chứng trên PostgreSQL 17 riêng

DB kiểm thử: localhost, database `metro_test_be09_tuyen`. Không dùng Neon chung, không dùng Docker/InMemory thay PostgreSQL.

| Kiểm tra | Kết quả |
| --- | --- |
| Source BE-09 trước/sau đổi tên | 3/3 không đổi nội dung |
| Source 001–009 so với `origin/dev` | 9/9 khớp |
| `python infra/database/build_deploy.py --check` | Khớp toàn bộ migration nguồn |
| Deploy sạch 001–012 + `scripts/09_verify.sql` | rc=0 |
| Nâng cấp từ gói `dev` 001–009 lên 012 | rc=0; toàn bộ 9 dòng ledger cũ (version/name/checksum) giữ nguyên |
| Nâng cấp có dữ liệu | Giữ nguyên profile request; backfill `payments.order_version=1` cho payment cũ; giữ trạng thái PENDING |
| Chạy lại gói 001–012 | rc=0; toàn bộ 12 dòng ledger giữ nguyên |
| `dotnet build Metro.sln -c Release --no-restore --warnaserror` | 0 warning, 0 error |
| Unit project | 51 passed, 0 failed, 0 skipped |
| Integration project | 80 passed, 0 failed, 0 skipped |
| Tổng test solution | **131 passed, 0 failed, 0 skipped** |
| Nhóm PostgreSQL theo runner | 55 passed (thuộc integration project, không cộng thêm vào tổng) |
| CI credential policy / `git diff --check` | Đạt |

Lệnh test: `backend/tests/Run-PostgresTests.ps1` với cấu hình database test riêng và `METRO_TEST_POSTGRES_REQUIRED=1`. Credential nằm ngoài repo, không đưa vào báo cáo.

Bằng chứng local ngoài Git:

- `C:\Users\ACER\Documents\Codex\Metro-BE09-localtest\results-20261123-132214` (TRX 51 + 80).
- `C:\Users\ACER\Documents\Codex\Metro-BE09-localtest\upgrade-evidence-20261123.json` (ledger trước/sau).
- Cùng thư mục: `upgrade-before-001-009.log`, `upgrade-001-012.log`, `upgrade-repeat-001-012.log`, `upgrade-verify.log`, `clean-verify-20261123.log`.

## Review và triển khai

GitHub CI được theo dõi theo head mới của PR #77; kết quả cập nhật trong PR description. Kiểm thử local không thay bằng chứng CI hoặc duyệt của reviewer.

Chưa áp dụng 010–012 lên Neon production. Chưa tự merge PR hoặc đánh dấu reviewer đã duyệt. Chỉ dùng một nhánh và một PR theo yêu cầu; PR tổng lớn hơn guideline 400 dòng, giữ lịch sử commit để review.
