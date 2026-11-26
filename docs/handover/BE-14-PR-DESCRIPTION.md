## Summary

BE-14 bổ sung quản trị ETA, tải kg, trạng thái chuyến và vòng đời gián đoạn
tuyến/ga/chuyến (UC30/UC36). Catalog và gợi ý chuyến đọc ETA đã công bố, giữ
lịch sử và dừng cập nhật gợi ý sau entry.

**Migration 014 chưa apply Neon production.** Deploy production cần backup DB
riêng đã xác minh khả năng phục hồi và authorization riêng, giống 010–013.
Kết quả kiểm thử và review/comment không cấp quyền deploy, approve hay merge.

## Related Issue

BE-14, UC30/UC36, BR-051–059/072, DATA-011/012/020, AC-X18/23/35–37.
Chi tiết: [handover BE-14](BE-14-SERVICE-OPERATIONS.md).

## Changes

- [x] Feature
- [x] Documentation

## Implementation Details

- Migration `014_service_operations.sql` thêm `operations_requests` để lưu
  response replay cùng transaction; `operation_revisions` đánh số namespace
  cache theo operator để read cũ không đưa dữ liệu cũ vào cache của revision mới.
- `service_disruptions` kiểm tra status/effect: ga chỉ `CLOSED`, tuyến/chuyến
  `DELAYED` hoặc `SUSPENDED`. View `v_trip_load_status` và API reader cùng chọn
  số đo mới nhất theo `observed_at, recorded_at, id` để tie-break ổn định.
- `OperationsService` dùng advisory lock theo operator và global, replay
  response khi trùng `Idempotency-Key`, trả 409 nếu payload đổi; kiểm tra active
  disruption trước publish/resolve. API quản trị yêu cầu Admin và `no-store`.
- Không tạo capacity hold (BR-054/055). `CatalogService` và
  `BookingSuggestionWorker` dùng ETA đã publish, giữ history và dừng sau entry
  theo UC36. Không tự hủy/hoàn/gia hạn vé hoặc sửa Journey.
- Giữ nguyên migration 001–013; `deploy.sql` được sinh từ migration nguồn.

## Testing

Đã đồng bộ `origin/dev` mới nhất tại thời điểm fetch ngày **25/11/2026**:
`d19b980` (PR #89) vào nhánh `feature/BE-14-eta-load-disruption`. Merge commit
local là **`5afd380`**, không có conflict; `origin/dev` là ancestor của HEAD.
Hai commit mới từ dev chỉ cập nhật board và file Excel kế hoạch, không đổi
backend hoặc migrations. Migration 001–013 giữ nguyên.

Bằng chứng do review cung cấp trên **head `e9ffbbf`**:

| Kiểm tra | Kết quả |
| --- | --- |
| `python -X utf8 infra/database/build_deploy.py --check` | `deploy.sql` khớp source |
| Deploy sạch đến migration 014 + `09_verify.sql` | Đạt, verifier rc=0 |
| Release build với `--warnaserror` | 0 warning, 0 error |
| Unit | **116/116 đạt** |
| Integration trên database riêng hoàn toàn mới | **124/124 đạt, 0 skip** |
| Tổng | **240/240 đạt, 0 failed, 0 skipped** |

Lần đầu chạy full suite trên DB đã dùng chung, test
`Suggestion_uses_published_ETA_preserves_history_and_stops_after_entry` fail.
Chạy riêng test đạt; chạy lại full suite trên DB sạch đạt. Review không xác định
đây là lỗi code, nhưng ghi nhận rủi ro suite chia sẻ state. **Mỗi acceptance run
phải tạo một DB `metro_test_*` riêng hoàn toàn mới**, deploy/verify 014 trước
khi chạy; không tái sử dụng DB từ run trước hoặc chạy chung DB với run khác.
Xem [hướng dẫn test](../../backend/tests/README.md).

Bằng chứng local chạy lại ngày **25/11/2026** sau merge dev, source tại
**`5afd380`**:

- `build_deploy.py --check` đạt; deploy sạch 001–014 và `09_verify.sql` **rc=0**.
- Release build `--no-restore --warnaserror`: **0 warning, 0 error**.
- `Run-PostgresTests.ps1 -NoBuild -NoRestore`: **240/240 đạt** gồm **116 unit +
  124 integration, 0 failed, 0 skipped**, trên PostgreSQL **17.12**, DB mới riêng
  `metro_test_be14_review_36235b7c542d4d479a14de31d2953385`.
- Bao gồm 19 lượt unit Operations, 7 integration Operations và test
  `Suggestion_uses_published_ETA_preserves_history_and_stops_after_entry` đạt.
  Harness đếm 83 test thuộc danh sách lớp PostgreSQL trong tổng 124 integration.
- TRX local tại `%TEMP%/metro-be14-review-35a8936e443e48a780099b3f31273369/results/`:
  `backend_net9.0_20261125151759.trx` và `backend_net9.0_20261125152020.trx`.

### Review lại ngày 26/11/2026

- HEAD local hiện tại: **`e841f75`**. Đối chiếu `e9ffbbf..e841f75` xác nhận
  backend source, unit/integration tests, migrations và `deploy.sql` không đổi;
  các thay đổi chỉ thuộc tài liệu và board/file Excel kế hoạch từ merge `dev`.
- Kết luận review được cung cấp giữ nguyên: **0 warning, 240/240 test đạt,
  CI xanh, không blocker kỹ thuật trong phạm vi BE-14**. Đã mở PR trên GitHub:
  **All checks have passed — 3 successful, 1 skipped**. Check CI bị bỏ qua khác
  với bằng chứng acceptance **0 test skipped**; PR vẫn chờ approving review
  của code owner.
- Kiểm tra lại local: `build_deploy.py --check`, `git diff --check` đạt;
  `dotnet build backend/Metro.sln --configuration Release --no-restore --warnaserror`
  đạt **0 warning, 0 error**. Không chạy lại full suite trong lượt chỉ sửa docs
  này; bằng chứng 240/240 vẫn gắn với các head ở trên.
- Các mục cần bổ sung trong description đã được ghi ở bản này: **014 chưa apply
  production**, backup + authorization riêng trước deploy và DB mới riêng cho
  từng acceptance run. Đã cập nhật description trên GitHub ngày 26/11/2026 và
  kiểm tra lại nội dung sau khi lưu.

## Production release gate

Quy trình bắt buộc: [release gate 014](../../infra/database/documentation/DEPLOYMENT_GUIDE.md#be-14-migration-014-production-release-gate).
Ghi authorization, backup mới, kiểm chứng phục hồi và rehearsal vào
[hồ sơ phát hành production](BE-14-PRODUCTION-RELEASE.md), hiện còn
**BLOCKED / NOT DEPLOYED**. Đây là điều kiện vận hành trước deploy, chưa phải
backup/authorization đã thực hiện. Package apply tất cả migration còn thiếu
đến 014; authorization phải bao gồm 010–013 nếu còn thiếu trên đích.

- [ ] Có authorization riêng, tường minh cho đúng Neon project/production
  branch và cửa sổ triển khai, giống yêu cầu của migrations 010–013.
- [ ] Tạo backup DB riêng trước deploy, ghi nhận branch ID/thời điểm/nguồn và
  xác minh khả năng phục hồi. Git branch không thay thế backup DB.
- [ ] Rehearsal trên branch riêng từ trạng thái production, kiểm tra checksum,
  apply package và chạy `09_verify.sql`; acceptance tests chỉ dùng DB test riêng.
- [ ] Chỉ apply production trong phạm vi được cấp quyền; xác minh schema và
  smoke test sau deploy rồi ghi bằng chứng phát hành.

Các mục trên còn chờ thực hiện; **014 chưa apply production**. Review hiện tại
không có phản đối về code/test, ở mức **review/comment**, chưa approve/merge.

## Merge coordination

Review mới nhất trên PR #87 còn yêu cầu phối hợp với
[PR #90](https://github.com/macdogiahuy/Metro-ticket-management-system/pull/90):
`014_gate_validation.sql` trùng số với `014_service_operations.sql` của PR này.
**Trước khi merge một trong hai PR, cần đổi số migration cho một bên.** Lần
cập nhật description này chỉ ghi nhận yêu cầu phối hợp, chưa đổi code/migration.

## Screenshots

Không có thay đổi UI.

## Checklist

- [x] Bằng chứng 240/240 test được gắn với head `e9ffbbf`.
- [x] Đồng bộ `origin/dev@d19b980` và chạy lại 240/240 trên source `5afd380`.
- [x] Ghi nhận lần fail trên DB dùng chung và yêu cầu DB mới cho mỗi run.
- [x] Ghi rõ trạng thái production và yêu cầu backup/authorization riêng cho 014.
- [ ] Phối hợp xử lý số migration trùng với PR #90 trước merge.
- [ ] Có approving review của code owner.

Nội dung review bổ sung đã được cập nhật vào description
[PR #87 trên GitHub](https://github.com/macdogiahuy/Metro-ticket-management-system/pull/87)
ngày 26/11/2026, gồm bằng chứng kiểm thử, yêu cầu DB riêng, production release
gate và lưu ý phối hợp số migration với PR #90; đã kiểm tra lại sau khi lưu.
Lần sửa tài liệu này không push, approve, merge hoặc deploy production.
