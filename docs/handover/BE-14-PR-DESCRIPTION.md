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

Bản cập nhật tài liệu này ghi lại bằng chứng review trên `e9ffbbf`; không đại
diện cho một lượt chạy lại suite hoặc xác nhận hosted CI trên head khác.

## Production release gate

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

## Screenshots

Không có thay đổi UI.

## Checklist

- [x] Bằng chứng 240/240 test được gắn với head `e9ffbbf`.
- [x] Ghi nhận lần fail trên DB dùng chung và yêu cầu DB mới cho mỗi run.
- [x] Ghi rõ trạng thái production và yêu cầu backup/authorization riêng cho 014.

Đây là bản description chuẩn bị trong repo; chưa cập nhật PR trên GitHub.
Lần sửa tài liệu này không push, approve, merge hoặc deploy production.
