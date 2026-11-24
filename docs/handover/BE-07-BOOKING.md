# BE-07 Booking

Triển khai local cho [issue #23](https://github.com/macdogiahuy/Metro-ticket-management-system/issues/23), trên nền `origin/dev` tại `e8243d4`. Phạm vi: tạo/sửa/xác nhận/hủy đơn chưa thu, phiên bản đơn, consent mua hộ, Checkout miễn phí trẻ em và gợi ý chuyến. Sử dụng fare engine BE-06, hồ sơ BE-04 và SQL phát hành BE-08/09; không giữ chỗ.

## Nguồn nghiệp vụ

- [SRS v1.2](../requirements/Metro_SRS_v1.2.docx): §1.4, §4, UC11/12/36/39/42, DATA-004/005/006/020, ST-002/003/004/014, AC-X04–16 và AC-X33–37, DEC-SRS-203–206.
- [UC v1.6](<../requirements/Metro_Dac_ta_ UC_v1.6.docx>) dùng đối chiếu. Những đoạn quota 5 phút, tự tạo mã thanh toán mới hoặc cấm hủy mọi đơn không thay quyết định hiện hành của SRS §11.6.
- [Kế hoạch](../planning/TASK-ASSIGNMENT.md) §5.1/§6, [thiết kế database](../../infra/database/documentation/DATABASE_DESIGN.md), [handover BE-09](BE-09-IMPLEMENTATION.md) xác định phạm vi, schema, SQL và quy ước khóa.
- Không sửa yêu cầu gốc hoặc tự đánh dấu issue/review hoàn thành. Cổng quét, thay QR, hoàn khoản thu lỗi và đổi/hủy vé đã phát hành thuộc các task tương ứng.

## API

Các endpoint cần JWT. Thao tác ghi `/orders` yêu cầu `Idempotency-Key` gồm 8–120 ký tự ASCII hiển thị, không khoảng trắng. Cùng key/nội dung trả lại phản hồi đã lưu cùng giao dịch; khác nội dung trả 409. Phạm vi key là operator + actor + thao tác. Không trả QR hoặc giấy tờ người nhận.

| API | Contract |
| --- | --- |
| `POST /api/v1/orders?operatorCode=...` | Một người đi/một sản phẩm; `beneficiaryUserId` hoặc `beneficiaryEmail`, bỏ cả hai để mua cho mình; gửi cả hai phải cùng tài khoản. Nhóm: `ORDINARY`, `STUDENT`, `ELDERLY`, `CHILD_UNDER_6`. |
| `GET /api/v1/orders/{id}` | Người đặt/người đi đọc trạng thái, phiên bản, giá, tên snapshot, deadline, consent. |
| `PUT /api/v1/orders/{id}` | Người đặt gửi `orderVersion`, `selection`; giữ lịch sử, tăng phiên bản và thay consent khi mua hộ. Chỉ sửa khi chưa thu chắc chắn và còn hạn. |
| `GET /api/v1/orders/consents?page=1` | Người nhận xem mã đơn còn lời mời hiện hành chờ trả lời, 20 dòng/trang. |
| `POST /api/v1/orders/{id}/consent` | `orderVersion`, `consentId`, `decision`. Người nhận `ACCEPT`/`DECLINE`; người đặt hoặc người nhận `WITHDRAW` trước thanh toán. |
| `POST /api/v1/orders/{id}/confirm` | Người đặt xác nhận `orderVersion`, `expectedTotalMinor`; kiểm lại giá/route/hồ sơ/consent/trùng hiệu lực. Trẻ hợp lệ gọi `metro.issue_free_order` trong cùng giao dịch, trả `PENDING_ISSUE`, không tạo Payment. |
| `DELETE /api/v1/orders/{id}?orderVersion=...` | Hủy đơn chưa thu bằng `metro.cancel_unpaid_order`; chặn payment đang xử lý/chưa rõ. Không hủy vé đã trả hoặc vé miễn phí đã xác nhận qua endpoint này. |
| `GET /api/v1/orders/{id}/suggestion` | Pass: `NOT_APPLICABLE`; trước xác nhận tiền/miễn phí: `NOT_AVAILABLE`; chờ worker: `PENDING`; kết quả: `SUGGESTED`/`NO_TRIP`. `holdsCapacity=false`, nhãn “Gợi ý chuyến — không giữ chỗ”. |
| `POST /api/v1/payments` | Body `orderId`, `orderVersion`, `idempotencyKey`; kiểm lại Checkout trước gọi cổng. Trả `attemptNumber`, `waitExpiresAt`, Checkout URL nếu có. Cùng key/nội dung dùng lại cùng giao dịch. |
| `POST /api/v1/payments/{id}/retry` | Lần cũ kết thúc chắc chắn không thu mới tạo bản ghi mới/mã riêng/phiên bản hiện hành, tối đa 3 lần/đơn kể cả sau sửa. Gửi lặp retry của một lần cũ trả cùng lần kế tiếp. |

Vé lượt bắt buộc ngày đi và hai ga khác nhau, từ hôm nay đến ngày tạo đơn +7 theo UTC+7. `journeyOptionIndex` là hành trình BE-05, không là chuyến/toa. Pass không nhận ngày/route. Sửa đơn không kéo dài hạn 24 giờ hoặc cửa sổ mua trước của đơn gốc.

## Checkout và điều kiện mở thanh toán

BookingPricing dùng bảng giá VND đã công bố còn hiệu lực và FareCalculator BE-06. Snapshot phiên bản giữ giá gốc/giảm/tổng, nhóm yêu cầu/áp dụng, mốc tuổi và căn cứ hồ sơ. HS/SV giảm 10%, cao tuổi giảm 20%, không cộng dồn. Hồ sơ người lớn thiếu/hết hạn báo giá thường với `fallbackToOrdinary=true`. Tuổi cao tuổi tính tại ngày đi cho vé lượt, tại PaymentStart cho pass. Giá/route/hồ sơ thay đổi trước PaymentStart trả `409 CHECKOUT_PRICE_OR_ROUTE_CHANGED`; sửa phiên bản, lấy consent mới khi cần và xác nhận lại.

Consent thông thường hết sau 24 giờ từ lời mời hoặc cuối ngày đi nếu sớm hơn. Người nhận chấp nhận đúng phiên bản trước PaymentStart. Không sửa/hủy/rút consent khi đang thanh toán hoặc chưa rõ kết quả. Consent hết hạn sau PaymentStart hợp lệ không đảo ngược khoản thu. Tạo/chấp nhận/từ chối/rút consent đưa thông báo cho người đặt và người nhận vào queue trong website + email theo SRS §8.2; payload chỉ chứa tham chiếu/sự kiện/hạn. Replay không tạo thông báo trùng; delivery thuộc luồng thông báo liên quan và phải kiểm lại hạn/quyền.

Mỗi người đi chỉ có một payment đang xử lý trong operator. Kiểm trùng tính cả Ticket `ISSUED/ACTIVE/IN_USE`, Journey `OPEN/MANUAL_REVIEW`, và Order `PAID/PENDING_ISSUE` đã trả/xác nhận miễn phí chờ cấp. Khóa beneficiary lấy trước order theo BE-09; PaymentStart khóa eligibility theo BE-04 rồi kiểm lại. Guard quan hệ giám hộ khóa bản ghi trước kiểm xác nhận miễn phí.

Kết quả cuối không thu do BE-08 cung cấp phải có `FAILED`/`EXPIRED`/`CANCELLED`, `reconciliation_status=MATCHED`, `verified_at`, không `succeeded_at`. Chỉ Failed, hết 5 phút, đóng trình duyệt hoặc lỗi HTTP không đủ bằng chứng. Lỗi mạng/timeout giữ lần thử bền vững chưa rõ, trả `PAYMENT_OUTCOME_UNKNOWN`, không tự mở sửa/hủy/retry hoặc gọi cổng lại khi replay. Sau kết quả chắc chắn không thu và còn hạn mới sửa/xác nhận lại hoặc retry; giữ nguyên mọi lần cũ.

Không có API cho client tự khai “chưa thu”. Poll/truy vấn cổng mỗi phút, chuyển đối soát quản trị sau 15 phút, nhận khoản thu muộn/trùng và hoàn lỗi thuộc BE-08/16. Fixture cung cấp kết quả cuối để kiểm contract BE-07, không thay nghiệm thu gateway TEST thật.

## Trẻ dưới 6 tuổi

Chọn rõ `CHILD_UNDER_6`, vé lượt, ngày đi; kiểm tuổi tại ngày đi, hai tài khoản/email đã xác minh và quan hệ đúng trẻ đã duyệt/còn hiệu lực tại Checkout và ngày đi. Không hợp lệ thì dừng, không tự chuyển sang trả phí. Người giám hộ cần pass phủ ngày hoặc vé lượt đã trả cùng ngày/cặp ga; chấp nhận vé tương lai chờ QR theo BE-09. Hai người có đơn riêng.

Giám hộ xác nhận Checkout thay trẻ với tổng 0, không yêu cầu consent thông thường. SQL BE-09 kiểm lại, tạo vé liên kết đúng giám hộ, không Payment/PaidAt/PaymentStart giả. QR tương lai đợi 00:00 ngày đi và vé giám hộ sẵn sàng. Quét cặp/giới hạn một trẻ mỗi hành trình thuộc BE-10; hủy vé trẻ đã xác nhận thuộc BE-16.

## Gợi ý chuyến và triển khai

Worker đọc lịch đã công bố, chuyến/giờ ga, ETA/tải từ schema BE-13/14, chạy mỗi 15 giây, tách khỏi giao dịch thu/cấp. Vé tương lai chọn chuyến đầu ngày; hôm nay chọn chuyến sớm nhất còn phục vụ, chưa rời ga đi. Dùng tuyến/chiều đầu hành trình, không đặt chỗ hoặc suy kg từ vé. ETA/chậm/hủy/đầy làm tính lại và giữ lịch sử; tối đa một gợi ý hiện hành/phiên bản. Không có chuyến không đổi tiền/vé; vé đã vào/dùng/hủy/hết ngày dừng cập nhật, không sửa Journey/entryTrip.

Tắt worker bằng `Booking__SuggestionsEnabled=false`. Test chủ động gọi processor với lịch/ETA đã công bố trong PostgreSQL; không giả định BE-13/14 đã hoàn thiện giao diện quản trị/delivery.

Migration `013_booking_requests.sql` thêm phản hồi chống lặp, số lần thử/Checkout URL, index gợi ý và cho phép `NO_TRIP` khi chưa có lịch; giữ nguyên 001–012. Chạy `python infra/database/build_deploy.py --check`, áp dụng `deploy.sql`, rồi `scripts/09_verify.sql`. Runtime cần quyền đọc/ghi Booking, payment metadata, audit/notification queue và gọi SQL workflow hiện có; không cần DELETE/TRUNCATE.

Postman có folder **Booking (BE-07)** gồm 13 request, tự lấy order/version/total/consent/payment từ phản hồi. Điền operator, JWT, sản phẩm, ga/ngày và dữ liệu đã công bố. Key trống sinh key mới cho từng request; đặt key cụ thể khi kiểm replay. Swagger có schema, Bearer/key, request/response và mã lỗi Booking.

## Truy vết kiểm thử

Các tên test dưới đây thuộc `BookingEndpointsTests` (hai file BookingEndpointsTests.cs và BookingAcceptanceTests.cs), trừ nhóm unit/Swagger ghi riêng.

| SRS/DoD | Thành phần | Test chính |
| --- | --- | --- |
| BR-006/014–016, DATA-004/005 | BookingService, quyền, email người nhận, version/consent | `Concurrent_create_replays_and_consent_is_bound_to_the_recipient_and_order_version`; `Every_booking_route_requires_authentication_and_unrelated_users_cannot_write` |
| BR-017–020/071, AC-X04/33/34 | BookingPricing + BE-06 | `Discount_uses_the_recipient_and_revocation_requires_a_new_confirmed_price`; `Elderly_age_is_checked_on_travel_date_for_single_trips_and_payment_day_for_passes` |
| BR-003/031/034/038, AC-X06/16 | BookingRules, deadline gốc | BookingRulesTests; `Expired_consent_or_original_order_deadline_cannot_be_extended_by_a_revision` |
| BR-031/032/034, DATA-006, AC-X11/12/16 | PaymentService/BookingPaymentGuard | `Payment_replays_and_only_verified_no_charge_can_reopen_checkout_with_three_immutable_attempts`; `Two_orders_for_one_beneficiary_cannot_start_payments_concurrently_even_on_different_dates`; `Provider_transport_failure_keeps_the_durable_attempt_unresolved_and_blocks_another_charge` |
| BR-005, AC-X07 | Vé/Journey/đơn chờ cấp | `Confirmed_paid_right_without_a_ticket_blocks_overlap_before_another_charge`; bộ test BE-09 |
| BR-021–026, AC-X09/10/35 | Checkout miễn phí + SQL BE-09 | `Free_child_checkout_uses_verified_guardian_and_paid_ticket_without_payment_or_early_QR`; bộ test BE-09 |
| BR-072, DATA-020, ST-014, AC-X35/36 | BookingSuggestionProcessor | `Verified_payment_gets_one_future_trip_and_refresh_preserves_history_without_holds`; `Suggestion_uses_published_ETA_preserves_history_and_stops_after_entry`; `A_train_still_boarding_at_the_origin_is_considered_without_inventing_a_new_departure_time` |
| Idempotency/authorization DoD #23 | Phản hồi cùng transaction, khóa request/beneficiary/order | `Each_booking_write_replays_without_extra_versions_consents_or_audit_events`; SwaggerContractTests |

Nghiệm thu theo [backend tests](../../backend/tests/README.md) trên database PostgreSQL 17+ riêng `metro_test_*`, không dùng DB chung. Gateway stub/lịch fixture kiểm chứng tích hợp nội bộ; MoMo TEST, cổng quét và email thực tế cần nghiệm thu riêng cùng task sở hữu. Review, commit/PR và phê duyệt vẫn theo quy trình nhóm.

## Kết quả thực chạy ngày 24 tháng 11 năm 2026

- `dotnet build backend/Metro.sln --configuration Release --no-restore --warnaserror`: 0 lỗi, 0 warning.
- Harness `Run-PostgresTests.ps1 -NoBuild -NoRestore`: **214 passed, 0 failed, 0 skipped**; 97 unit + 117 integration, gồm 14 test Booking chạy PostgreSQL 17.12 thật. Bộ đếm PostgreSQL của harness ghi nhận 76 test theo danh sách lớp được cấu hình.
- `build_deploy.py --check` khớp nguồn; deploy và deploy lại 001–013 thành công; `09_verify.sql` qua. Migrations 001–012 không đổi.
- Postman JSON và JavaScript hợp lệ; giữ nguyên các request có trước BE-07. Không có conflict marker hoặc lỗi `git diff --check`.
- Kết quả này là tự kiểm local, không thay phê duyệt reviewer hoặc nghiệm thu các dịch vụ ngoài.
