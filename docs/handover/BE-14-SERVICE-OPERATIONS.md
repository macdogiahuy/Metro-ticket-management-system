# BE14 Cập nhật ETA tải và gián đoạn dịch vụ

BE-14 bổ sung API quản trị ETA, tải kg, trạng thái chuyến và gián đoạn tuyến/ga/chuyến, nối với catalog và gợi ý chuyến BE-07. Giữ lịch sử; không giữ chỗ, suy tải từ vé bán, sửa Journey hoặc tự hủy/hoàn/gia hạn vé do gián đoạn.

Nguồn: [SRS v1.2](../requirements/Metro_SRS_v1.2.docx), UC30/36, BR-051–059/072, DATA-011/012/020, AC-X18/23/35–37; [TASK-ASSIGNMENT](../planning/TASK-ASSIGNMENT.md) mục 5.1 và [BE-07](BE-07-BOOKING.md). Nền triển khai: `origin/dev` **fe29f9b**, nhánh local `feature/BE-14-service-operations`.

## 1 API và phân quyền

Mọi route yêu cầu `operatorCode` cụ thể. `/admin` cần JWT role `Admin` và membership ACTIVE trong operator ACTIVE; service kiểm lại trong giao dịch. Guest trả 401, sai role/membership trả 403, ID thuộc operator khác trả 404. Không phân quyền bổ sung theo ga/ca.

Mutation yêu cầu `Idempotency-Key` 8–120 ký tự ASCII hiển thị không khoảng trắng và lý do không rỗng tối đa 1.000 ký tự. Key có phạm vi operator + actor + thao tác. Cùng key/body trả receipt đã lưu; khác body trả `409 IDEMPOTENCY_KEY_CONFLICT`. Timestamp cũng là nội dung body, cần giữ nguyên khi retry sau mất phản hồi.

| Method và route sau `/api/v1` | Body hoặc kết quả |
| --- | --- |
| POST `/admin/trips/{id}/eta` | `stationId`, `expectedArrivalAt` và/hoặc `expectedDepartureAt`, `reason`; RECORDED |
| POST `/admin/trips/{id}/load` | `observedLoadKg`, `observedAt`, `reason`; RECORDED |
| PUT `/admin/trips/{id}/status` | `status`, `reason`, tùy chọn `boardingStatus` |
| GET `/admin/trips/{id}/operations?page=1` | Lịch sử ETA/tải, mỗi collection 50 dòng/trang, có người/thời điểm ghi |
| POST `/admin/disruptions` | `title`, `description`, `effect`, `occurredAt`, đúng một `lineId`/`stationId`/`plannedTripId`; DRAFT |
| GET `/admin/disruptions?page=1` | Draft/lịch sử gián đoạn trong operator, 50 dòng/trang |
| POST `/admin/disruptions/{id}/publish` | `confirmed=true`, `reason`; PUBLISHED |
| POST `/admin/disruptions/{id}/resolve` | `confirmed=true`, `reason`; RESOLVED |
| GET `/trips/{id}/load` | Công khai: số kg mới nhất, thời điểm quan sát/ghi, giới hạn version đã gán, boardingStatus/loadStatus; không trả actor/ghi chú riêng |

Receipt gồm `id`, `status`, `recordedAt`, `suggestionsRefreshPending`. Với ETA/tải, ID là bản ghi mới; status là trip ID; gián đoạn là disruption ID. Mutation thành công dùng HTTP 200, kể cả tạo draft; client lấy ID từ body. Route admin đặt `Cache-Control: no-store`.

## 2 ETA tải và trạng thái chuyến

ETA chỉ ghi cho ga trong lịch chuyến, ít nhất một thời điểm, departure không trước arrival, cùng ngày UTC+7 với chuyến. Có thể lưu ETA đã qua; catalog hiện NO_ETA_UPDATE thay vì giả tàu đã đến. Append lịch sử; không nhập cho chuyến đã COMPLETED/CANCELLED.

Tải không âm, tối đa một số lẻ, đơn vị kg; từ chối nhiều số lẻ trước khi DB có thể làm tròn. `observedAt` không ở tương lai, cùng ngày phục vụ; trip cần load profile có giới hạn dương. Cho nhập vượt giới hạn để cảnh báo. Mới nhất xếp theo observedAt/recordedAt/ID; nhập muộn một số đo cũ không đè số mới. Tải không tự hết hạn hoặc thay đổi khi scan.

Giới hạn 18.000 kg: 17.999,9 là AVAILABLE; 18.000/18.000,1 là FULL. Thiếu số đo NO_DATA, thiếu giới hạn NO_LIMIT; cả hai không cho entry dựa trên tải. `entryLoadAllowed` chỉ là điều kiện tải/boarding, **không phải** Allow của gate: BE-10 còn kiểm vé/Journey/ga/chiều/chuyến/quyền. Exit hợp lệ không phụ thuộc tải.

Status hỗ trợ PLANNED/READY/IN_SERVICE/DELAYED/DISRUPTED/COMPLETED/CANCELLED. Không hồi sinh chuyến kết thúc/hủy hoặc đưa chuyến chạy về PLANNED/READY. CANCELLED/DISRUPTED/COMPLETED đóng nhận khách; không tạo arrival/departure hoặc đóng Journey. `boardingStatus=OPEN` cần bối cảnh đã có: đúng ga boarding, actual arrival, chưa departure/chưa đến ga sau. Tạo lịch, gán đoàn và ghi nhận đến/rời ga thuộc BE-13, không được giả từ ETA.

Trip disruption đã công bố phải resolve trước khi phục vụ lại; vẫn cho kết thúc/hủy có lý do. Resolve giữ terminal/manual state khác đã cập nhật sau đó và không tự mở nhận khách sau suspension.

## 3 Công bố và khôi phục gián đoạn

Draft lưu phạm vi/nội dung để xác minh, chưa đổi trạng thái hoặc xuất hiện công khai. Tuyến/chuyến nhận DELAYED/SUSPENDED; ga nhận CLOSED. Phải đúng một đối tượng trong operator. Không cho hai disruption PUBLISHED cùng đối tượng.

Publish cần `confirmed=true`: lưu snapshot trạng thái trước và effect, incident/timeline, PUBLIC notice và outbox IN_APP/EMAIL_MOCK cho người đặt/người đi bị ảnh hưởng trong cùng transaction. Payload thông báo chỉ chứa ID/sự kiện. Tuyến DELAYED giữ master ACTIVE và operational state DELAYED; SUSPENDED/ga CLOSED bị loại khỏi hành trình mới. Trip suspension chuyển DISRUPTED, đóng nhận khách; chậm không tự sinh ETA.

Resolve lưu recovery/timeline, expire notice cũ, queue thông báo hồi phục. Chỉ khôi phục state còn đúng effect, giữ thay đổi terminal khác mới hơn; không đổi vé/tiền/hạn/Journey. Đổi vé do gián đoạn thuộc BE-16. EMAIL_MOCK là outbox mô phỏng, không chứng minh email thật đã gửi; lỗi delivery không đảo ngược giao dịch.

## 4 Gợi ý dự phòng và cache

Tái sử dụng worker BE-07 mỗi 15 giây; `Booking__SuggestionsEnabled=false` tắt worker. Service state đã commit là nguồn để tính lại, lỗi tạm thời thử lại chu kỳ sau, không rollback payment/issuance. Receipt không cam kết gợi ý đã tính xong; đọc `/orders/{id}/suggestion` theo quyền BE-07.

Worker kiểm toàn bộ tuyến/ga snapshot, kể cả ga trung gian/chuyển tuyến. Không gợi ý route đã đóng; dùng ETA công bố để xếp chuyến. Trip/tuyến chậm thiếu ETA hoặc bằng chứng đang nhận khách thì không bịa thời gian. Loại trip hủy/gián đoạn/đầy hiện tại; tải hôm nay không thành quota tương lai. Không có chuyến trả NO_TRIP, không hủy/hoàn/gia hạn. Giữ history/một current mỗi order version; vé đã bắt đầu dùng/có Journey dừng cập nhật, không sửa entryTrip.

Mutation và worker cùng khóa advisory theo operator, tránh gợi ý cũ commit sau closure. Worker tiếp tục thứ tự khóa beneficiary/order BE-07; mutation dùng khóa quản trị BE-12 khi kiểm quyền. Catalog namespace cache gồm revision operator tăng cùng mutation transaction. Read sau commit không dùng cache cũ dù một read cũ đang cache lại; revision nằm ở DB, dùng được nhiều instance, key cũ tự hết TTL.

## 5 Migration và công cụ

[014_service_operations.sql](../../infra/database/migrations/014_service_operations.sql) thêm `operations_requests`, `operation_revisions`, `service_disruptions` và tie-breaker cho view tải. Không sửa migration 001–013. `deploy.sql` sinh lại, verifier yêu cầu migration/bảng mới.

Trên DB test riêng PostgreSQL 17+, chọn đúng connection trước khi chạy:

```powershell
python -X utf8 infra/database/build_deploy.py --check
psql -v ON_ERROR_STOP=1 -f infra/database/deploy.sql
psql -v ON_ERROR_STOP=1 -f infra/database/scripts/09_verify.sql
```

Không dùng EF migrations. Chưa deploy production/thay master data thật. Runtime cần quyền đọc/ghi operations/audit/outbox và bảng mới; không có secret/config mới.

[Postman](../postman/Metro.API.postman_collection.json) có folder **Service Operations (BE-14)** với 11 request. Điền operatorCode/adminAccessToken/operationsTripId/operationsStationId. `operationsLoadKg=17999.9`; chọn trip đúng ngày quan sát. ETA/time tự sinh nếu trống; phải cùng ngày phục vụ. Khi replay cố định operationsRequestKey và timestamp/body. Draft lưu operationsDisruptionId cho publish/resolve. Các request module khác giữ nguyên.

Swagger có Bearer, header chống lặp, DTO, ví dụ body và mã lỗi. Các code chính: INVALID_TRIP_ETA, ETA_STATION_NOT_ON_TRIP, ETA_OUTSIDE_SERVICE_DAY, INVALID_LOAD_KG, INVALID_LOAD_OBSERVED_AT, TRIP_LOAD_PROFILE_REQUIRED, TRIP_ALREADY_ENDED, OPERATIONS_FORBIDDEN, INVALID_DISRUPTION_TRANSITION, DISRUPTION_ALREADY_ACTIVE, ACTIVE_DISRUPTION_REQUIRES_RECOVERY.

## 6 Truy vết kiểm thử

| Phạm vi | Class hoặc method chính |
| --- | --- |
| Kg, precision/time, ETA, transition, scope, key | OperationsRulesTests — 19 lượt unit |
| 401/403/operator, public load, Swagger | OperationsEndpointsTests.Every_admin_route_requires_auth_and_operator_membership_while_load_is_public |
| Concurrent/replay, conflict, validation/history/audit | ETA_and_load_are_atomic_idempotent_histories_with_strict_validation |
| Draft/publish/resolve, cached routes/notices, outbox, tiền giữ nguyên | Publishing_and_recovery_update_cached_routes_notices_notifications_and_keep_paid_rights |
| ETA đổi chuyến, hủy đến NO_TRIP, history/một current | ETA_cancellation_and_missing_fallback_recompute_one_current_suggestion_without_touching_money |
| Full, số đo cũ, NO_DATA, vé đã dùng | Latest_kg_controls_fallback_but_missing_data_never_grants_gate_permission_and_used_tickets_freeze_suggestions |
| Chậm thiếu ETA, không hồi sinh chuyến hủy | Published_delay_requires_real_ETA_and_trip_recovery_never_resurrects_later_cancellation |
| Audit lỗi rollback ETA/idem và retry sạch | Audit_failure_rolls_back_ETA_and_replay_record_and_allows_a_clean_retry |

Chạy [backend test harness](../../backend/tests/README.md) trên DB `metro_test_*` đã apply/verify 014. Runner đã nhận diện Operations trong bộ đếm PostgreSQL. Gateway dùng stub; kết quả không thay nghiệm thu gate/email/MoMo thật hoặc phê duyệt reviewer.

## 7 Kết quả tự kiểm ngày 24 tháng 11 năm 2026

- `dotnet build backend/Metro.sln --configuration Release --no-restore --warnaserror`: **0 warning, 0 error**.
- `Run-PostgresTests.ps1 -NoBuild -NoRestore`: **240 passed, 0 failed, 0 skipped** trên PostgreSQL 17.12 riêng `metro_test_be14`: 116 unit và 124 integration. Harness đếm 83 test thuộc danh sách lớp PostgreSQL, gồm 7 Operations endpoint test; 19 lượt unit Operations nằm trong tổng 116.
- Kiểm cả publish/resolve chung một clock tick; rollback audit; replay/đồng thời; gián đoạn/ETA/tải thay gợi ý; fixture gate đã dùng không bị cập nhật hồi tố.
- `build_deploy.py --check`, deploy/redeploy 001–014 và schema verifier đạt; migration 001–013 không đổi.
- Postman JSON/body và JavaScript hợp lệ, thêm 11 request BE-14 và giữ nguyên 19 item/folder có trước. Link handover và `git diff --check` đạt.
- Chỉ chạy DB test riêng; kết quả này không là deploy production, nghiệm thu dịch vụ ngoài hoặc phê duyệt PR. Thay đổi được giữ local, không push.
