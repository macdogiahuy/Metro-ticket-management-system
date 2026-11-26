# BE-10 — Soát vé tại cổng

Nền ban đầu `origin/dev` `fe29f9b`, đã tích hợp dev `e66f058` (BE-15), nhánh local `feature/BE-10-gate-validation`. Bản phản hồi review dành cho PR #90; chỉ push nhánh BE-10, không merge PR. Nguồn: SRS v1.2 FR-16/17/18, BR-027/041–049/052/055/072; DATA-009; IF-002/008; chính sách mã email mục 8.2; AC-X19–22/37 và AC-A03/04. [Ma trận truy vết](BE-10-SRS-TRACEABILITY.md) ghi phạm vi backend và giới hạn nghiệm thu.

## Contract và quyền

Mọi route cần JWT StationStaff hoặc Admin, tài khoản còn hoạt động và membership STAFF/ADMIN ACTIVE trong operator ACTIVE. Bắt buộc `operatorCode` cụ thể; service kiểm lại quyền trong transaction. Dùng role `StationStaff` hiện có cho Ticket Inspector; không thêm role hoặc quyền thiết bị. Không phân quyền bổ sung theo ga/ca. Response không cache.

| Method và route sau `/api/v1` | Nội dung |
| --- | --- |
| POST `/gate/scans` | `requestId`, `action=ENTRY/EXIT`, `stationId`, `plannedTripId`, `direction=0/1`, `qrTokens` một hoặc hai mã, `deviceCode`, `pairPresentConfirmed` |
| GET `/gate/scans/{requestId}` | Khôi phục kết quả trước khi tạo scan mới. Chỉ người gửi đọc receipt của mình trong operator |
| POST `/gate/resolutions` | `requestId`, `sourceRequestId`, action, ga thực tế, reason, evidenceReference, identityVerified; thêm xác nhận cặp/khách chưa đi hoặc chứng cứ lượt vào tùy action |
| POST `/gate/lookup` | `ticketCode`, `reason`; dữ liệu vé, tên người đi và hành trình tối thiểu; audit trước khi trả |
| GET `/gate/escalations?page=1&status=OPEN` | Admin: danh sách ca chuyển hỗ trợ, 20 dòng/trang; OPEN/CLOSED/ALL |
| POST `/gate/tickets/{ticketId}/qr-challenges` | Tạo mã email sau đối chiếu tên/ngày sinh; ràng buộc operationId và action DISPLAY/REPLACE |
| POST `/gate/tickets/{ticketId}/qr-support` | Xác minh mã email hoặc giấy tờ minh họa, hiện QR hoặc thay credential nguyên tử |
| GET `/gate/qr-requests/{requestId}` | Nhân viên trong cùng session khôi phục receipt và xem trạng thái gửi email |
| GET `/gate/qr-requests/{requestId}/image` | PNG riêng tư sau xác minh, kiểm lại session/hồ sơ/current credential và audit |

POST scan/resolution cần đúng một header `Idempotency-Key` UUID dạng D, bằng `body.requestId`. Một ID định danh một thao tác trong operator; receipt gắn người thao tác. Cùng ID/nội dung trả kết quả cũ; khác nội dung trả 409 `IDEMPOTENCY_KEY_CONFLICT`. Nhân viên khác không đọc/chiếm request đã lưu. Khi retry giữ nguyên thứ tự QR và toàn bộ body. Đổi ga/chiều/chuyến/action phải tạo request mới sau khi biết kết quả cũ.

200 có `decision=Allow/Deny`, `reasonCode`, `processedAt`, `passages[{ticketId,journeyId,eventId}]`. Deny không tiêu vé. 202 `Pending/GATE_RESULT_UNCONFIRMED` nghĩa là chưa xác nhận commit, không cho qua; GET/replay cùng yêu cầu. Request chưa lưu cũng trả Pending khi tra kết quả, không giả Deny hoặc kết luận thao tác chưa từng xảy ra. Input/quyền/xung đột trả ProblemDetails với code ổn định. `REVIEW` trong AFC sửa quét không đồng nghĩa Pending.

## Kiểm vé và hành trình

- Dùng payload M1 và credential/hash BE-09; không nhận mã đơn/mã vé thay QR. Kiểm format, operator, tham chiếu vé, credential/version/key, hash và trạng thái ACTIVE. Không lưu/log raw QR. QR không nhận diện được có receipt Deny, không tạo vé/AFC giả để thỏa FK.
- Entry cần vé ACTIVE trong `[effective_from,expires_at)`, đúng ngày/ga và tuyến/chiều chặng đầu từ snapshot BE-07. Vé định kỳ dùng ở ga đang phục vụ. Chặn tài khoản người đi khóa và Journey OPEN/MANUAL_REVIEW của người đi. Không tự đóng hành trình qua ngày.
- Trip đang nhận khách đúng ga/chiều, lịch/calendar công bố, cùng ngày UTC+7, tuyến/ga ACTIVE và boarding OPEN. Kiểm actual arrival đã xảy ra, chưa actual departure, chưa đến ga sau. Không suy diễn tàu đã đến từ ETA/lịch; thiếu ETA riêng lẻ không chặn nếu đã có bằng chứng nhận khách.
- Tải lấy view mới nhất theo `(observed_at,recorded_at,id)`. Thiếu tải/giới hạn hoặc bằng/vượt max đều Deny. Số đo không tự hết hạn; scan không tăng/giảm kg hoặc giữ chỗ. EntryTrip không phải trùng chuyến gợi ý.
- Exit cần Journey OPEN đã vào hợp lệ. Exit thường đúng ga đến vé lượt, không ra cùng ga; không buộc trip/cùng trip lúc vào. Tải, hết hạn giữa hành trình hoặc khóa tài khoản người đi sau entry không chặn exit. Vé lượt USED; vé định kỳ ACTIVE, giữ hạn cũ và kiểm lại hạn khi entry mới.
- Trẻ miễn phí cần đúng vé/người giám hộ liên kết, hai QR trong cùng phiên có xác nhận cùng hiện diện. Entry kiểm quan hệ và tuổi dưới 6, tạo hai Journey liên kết trong cùng transaction. Exit đúng cặp/cùng ga và cho kết thúc sau khi quan hệ hết hiệu lực. Một vé không đạt thì không thay đổi cả cặp; không cho giám hộ exit riêng khi Journey ghép cặp.

## Ngoại lệ và sửa nhầm

`CLOSE_JOURNEY`: sau xác minh danh tính/bằng chứng, đóng lượt vào đã có tại ga thực tế, gồm sai ga/cùng ga/thiếu ra. Giữ ga đã mua, thêm MANUAL_EXIT, vé lượt USED, không phụ thu.

`SUPPLY_ENTRY_AND_EXIT`: bắt buộc `provenEnteredAt`, `entryStationId`, `entryPlannedTripId`, `entryDirection` và chứng cứ. Giờ không ở tương lai, nằm trong hiệu lực vé; kiểm ngày/ga/chuyến, không có Journey mở. Bổ sung MANUAL_ENTRY và MANUAL_EXIT cùng transaction. Giữ giờ vào có chứng cứ riêng với thời điểm xử lý, không tự bịa giờ thiếu. Cặp xử lý cả hai.

`UNDO_SCAN`: chỉ scan thường đã Allow, đã xác minh và `passengerHasNotTravelled=true`, chưa có thay đổi sử dụng tiếp theo và state khớp snapshot. Cặp hoàn tác cả hai. Không sửa AFC gốc; thêm REVIEW, authorization cùng transaction, audit và tham chiếu trước/sau. Chỉ undo một lần. Entry nhầm thành Journey CANCELLED; exit nhầm khôi phục OPEN. Trả `Deny/SCAN_CORRECTED_NO_PASSAGE`, không cho qua. Không đổi hạn/giá hoặc phục hồi lượt đã thực đi. Trường hợp phức tạp giữ nguyên quyền và chuyển hỗ trợ.

`ESCALATE`: lưu reason/evidence/source trong `gate_resolutions`, trả `Deny/NEEDS_ADMIN_SUPPORT`, không tạo/đóng Journey. Admin ACTIVE trong operator nhận thông báo IN_APP và đọc danh sách `/gate/escalations`; giữ hỗ trợ cho QR không nhận diện được mà không tạo ticket giả. Admin có thể dùng các luồng nghiệp vụ có bằng chứng hoặc `CLOSE_SUPPORT` để ghi kết quả xem xét. Đóng ca hỗ trợ trả `Deny/SUPPORT_CLOSED_NO_PASSAGE`, không thay vé/Journey; nhân viên thường không có quyền đóng thay Admin. OPEN/CLOSED được suy từ lịch sử bất biến, không xóa ca gốc.

## UC18 — Hiện và thay QR sau xác minh

Tra cứu không trả QR hoặc ngày sinh. Để hiện/thay QR, nhân viên cung cấp tên/ngày sinh đã đối chiếu với người đi, reason và hai UUID riêng: `operationId` định danh hành động đang chờ, `requestId` định danh một lần gửi. Header Idempotency-Key bằng requestId. Giữ operationId/action/reason và toàn bộ body khi retry mất phản hồi; một lần nhập mã khác dùng requestId mới. Không dùng requestId bằng operationId.

**EMAIL:** POST qr-challenges kiểm tên/ngày sinh hiện tại trên máy chủ rồi tạo mã TicketQrSupport, gắn tài khoản người đi, vé, hành động, current credential, nhân viên/session và snapshot hồ sơ. Gửi về email tài khoản người đi, không email của người mua hộ thường. Response chỉ có challengeId/expiry/queue state; không trả mã, kể cả Development/Test mode. Sau khi khách nhận email, POST qr-support với verificationMethod EMAIL, challengeId và emailCode.

Mã 6 số, 5 phút, một lần; 5 sai hủy mã. Trigger DB dùng chung bảng email_verification_challenges và khóa user, áp dụng khoảng cách 60 giây và tối đa 3 mã/15 phút trên tổng Signup/PasswordReset/TicketQrSupport, qua mọi operator/thiết bị. Mã mới chỉ supersede mã cũ cùng purpose. Vì baseline đăng ký cũ dùng 10 phút, migration 016 và AuthService được đồng bộ về 5 phút theo mục 8.2; purpose cũ không bị dùng thay cho QR support. Lần cấp được tính dù email lỗi. Mã QR và fingerprint request chứa OTP dùng HMAC; OTP trong outbox được Data Protection mã hóa, không nằm trong thông báo website/log.

**DOCUMENT:** khi không truy cập email, dùng verificationMethod DOCUMENT, noEmailAccess=true, documentMatchesProfile=true, evidenceReference riêng tư và tên/ngày sinh khớp. Đây là xác nhận kiểm giấy tờ minh họa tại ga của nhân viên; không yêu cầu tải giấy tờ thật. Không trộn challenge/emailCode vào request DOCUMENT. Lưu reference, actor, reason và kết quả; không sao chép nội dung giấy tờ vào audit. Hoàn tất bằng chứng giấy tờ hủy mã email còn chờ của thao tác đó.

Kết quả READY chỉ cho phép nhân viên đã xác minh đọc PNG ở `/qr-requests/{requestId}/image`; không phải Allow tại cổng. Quyền xem gắn session nhân viên hiện tại và snapshot hồ sơ; mã email cho quyền đọc đến hết cửa sổ xác minh, giấy tờ theo session còn hiệu lực. PNG luôn kiểm current credential; hồ sơ hoặc credential thay đổi thì phải xác minh lại. Không lưu raw QR trong receipt/outbox và không có link PNG công khai.

DISPLAY giữ nguyên credential/QR. REPLACE dùng key hiện hành của BE-09, đánh dấu credential cũ REPLACED, revoked_at và replaced_by_id, tạo đúng một credential ACTIVE phiên bản kế tiếp. Tất cả cùng transaction với tiêu thụ mã/bằng chứng, before/after credential references, receipt, audit và notification queue. Retry cùng request không thay mã lần nữa. Mã cũ bị scan từ chối; mã mới tiếp tục Journey cũ. Không thay ticket status, effective_from/expires_at, first_used_at hoặc bất kỳ Journey nào. Vé USED/REVOKED/REFUNDED, vé tương lai chưa có QR, hoặc hết hạn không có Journey hợp lệ không được dùng luồng này để hồi phục quyền đi. Vé đang đi hết hạn/khóa tài khoản người đi vẫn có thể được hỗ trợ để kết thúc lượt hợp lệ.

QR bị thay thông báo IN_APP + email cho người đi và giám hộ hiện hành được liên kết với vé trẻ; không phát QR hoặc giao dịch riêng cho người đặt hộ thường. Notification/outbox ghi event ID để chống lặp. Email vận hành là hậu giao dịch, lỗi gửi không đảo lại credential/vé/Journey đã commit.

Phân công dự án có chồng chéo tên công việc cấp lại QR ở BE-11 nhưng gắn UC18 vào BE-10. Bản này thực hiện FR-18.02/03 trong BE-10 theo SRS; BE-11/FE-06 có thể tái sử dụng contract, tránh viết một cơ chế cấp QR bỏ qua xác minh khác.

## Gửi email và cấu hình

`GateEmailProcessor` dùng outbox riêng được mã hóa, default transport SMTP. OTP thử lúc đầu và một lần sau 1 phút nếu còn hạn, không sinh mã mới hoặc kéo dài hạn khi retry. Notice QR_CHANGED dùng tối đa 3 lần ở 0/+1/+5 phút. Lưu lịch sử attempt chỉ bằng ID/trạng thái/mã lỗi, không ghi message/OTP. ACCEPTED nghĩa là SMTP chấp nhận, không khẳng định đến inbox. Payload được xóa khi đã chấp nhận, hết lượt thử, consumed/superseded hoặc hết hạn; worker dọn mã hết hạn mỗi vòng và khi khởi động lại. Khi ứng dụng dừng, worker không chạy; khi khởi động phải dọn payload hết hạn trước gửi, không gửi mã hết hạn.

Điền GateEmail__Host/Port/EnableSsl/From/UserName/Password ngoài Git theo [backend/.env.example](../../backend/.env.example); không có SMTP mặc định hoặc địa chỉ gửi bịa. GateEmail__KeyRingPath là thư mục key Data Protection bền vững, riêng tư ngoài web root, dùng chung nếu nhiều instance. Giữ key ring cùng cấu hình JWT/QR theo môi trường. Không cấu hình SMTP thì delivery báo lỗi/đợi retry, không giả Sent/Accepted. Có thể tắt worker bằng GateEmail__WorkerEnabled=false để kiểm thử có kiểm soát.

Test dùng TestGateEmailSender được đăng ký riêng trong WebApplicationFactory, chỉ tồn tại trong project tests. Không có endpoint xem hộp thư test hoặc đọc OTP trên ứng dụng thật. Kết quả gửi qua fake transport chứng minh workflow nội bộ, không là bằng chứng gửi email thật.

## Transaction và migration

SQL tham số hóa qua Npgsql, không dùng EF migrations. Thứ tự khóa: khóa đọc chung quản trị BE-12 → khóa đọc chung tài khoản nhân viên → khóa đọc chung operations theo operator (cùng key BE-14) → request độc quyền → khóa đọc chung hồ sơ người đi và `(operator,holder)` độc quyền theo thứ tự cố định (cùng khóa BE-07/09) → ticket/credential. Admin/account/network writers dùng khóa độc quyền tương ứng, nên không thay quyền/bối cảnh khi quyết định đang xử lý; các người đi khác nhau có thể quét song song. Kiểm lại state dưới khóa; Journey/ticket/AFC/receipt/snapshot/audit commit cùng nhau. Allow chỉ trả sau commit. Trang support và total dùng cùng một SQL snapshot.

`015_gate_validation.sql` thêm receipt/change/resolution/undo, bảo vệ history và trì hoãn FK cặp Journey đến commit. Giữ workflow tiền/vé hiện có; chỉ thêm chuyển trạng thái vé định kỳ sau exit và đảo trạng thái có authorization/snapshot trong transaction sửa nhầm. Migrations 001–013 giữ nguyên.

`016_gate_qr_support.sql` thêm intent/challenge/receipt/verification, encrypted email outbox/attempts và chính sách mã email chung. `017_gate_support_review.sql` bổ sung CLOSE_SUPPORT vào lịch sử xử lý. Migrations 001–013 và nội dung SQL ba migration gate giữ nguyên; không ghi đè lịch sử DB đã triển khai.

**Tích hợp BE-14:** giữ `014_service_operations.sql` nguyên văn từ PR #87, head `7cff576`, làm schema phụ thuộc để gói BE-10 deploy độc lập ngay khi BE-14 chưa vào dev. Không nhập API/runtime của PR #87. Nội dung và checksum 014 phải khớp nhánh BE-14; BE-14 vẫn sở hữu migration này. Gate chuyển thành `015_gate_validation.sql`, `016_gate_qr_support.sql`, `017_gate_support_review.sql`; nội dung ba migration gate giữ nguyên, chỉ đổi version/name của wrapper trong deploy. Verifier kiểm đúng tên/version 014–017. Vì cùng file 014 được giữ nguyên, BE-14 có thể vào trước hoặc sau mà không cần cấp thêm version 014. Khi nhánh BE-14 cập nhật dev sau đó, vẫn phải hợp nhất deploy/verifier và runtime bằng review của PR đó; không tự merge PR nào. Không sửa checksum trên DB chung đã áp dụng chuỗi gate 014–016 cũ: dùng DB test mới để kiểm chuỗi chuẩn, lập kế hoạch riêng cho DB chung nếu đã có chuỗi cũ.

Runtime cần SELECT/INSERT các bảng gate/QR history, SELECT/INSERT AFC, SELECT/UPDATE Journey/ticket/credential/email challenges/outbox và INSERT notification/attempt/audit. Chưa thay quyền production hoặc deploy DB thật. BE-13 cung cấp lịch/actual boarding; BE-14 cung cấp cập nhật tải/vận hành. Test dùng fixture riêng, không giả các API đó đã merge.

## Kiểm thử và công cụ

Dùng database riêng `metro_test_*` PostgreSQL 17+, deploy `infra/database/deploy.sql`, chạy `scripts/09_verify.sql`, rồi `backend/tests/Run-PostgresTests.ps1`. Không dùng DB chia sẻ/production. Harness nhận diện GateEndpointsTests; QR/payment đi qua BE-09 với gateway mô phỏng, không gọi MoMo thật.

| Phạm vi SRS | Test chính |
| --- | --- |
| IF-008; quyền, operator, privacy | All_routes_require_staff…; Unknown_QR_is_durably_denied… |
| BR-043; AC-X19 | Entry_exit_replay_and_concurrent_scans…; Lock_timeout_returns_Pending… |
| BR-055; thiếu/bằng/vượt tải, không TTL | Missing_full_equal_limit_and_over_limit_load…; GateRulesTests |
| BR-041/042/049; exit/hạn/khóa/vé định kỳ | Exit_ignores_expiry_holder_lock… |
| BR-027; AC-X20 | Guardian_child_entry_and_exit_are_atomic…; Guardian_pair_correction_is_atomic… |
| BR-047/048; AC-X21 | Wrong_exit_requires_verified_evidence…; Missing_entry_needs_proven_time… |
| BR-044; AC-X22 | Mistaken_entry_or_exit_can_be_corrected_once… |
| AC-X37; BR-052; chuyển tuyến | Suggested_trip_is_not_a_gate_restriction…; Frozen_first_leg_controls_transfer_entry… |
| FR-16.04/05; lỗi audit | Audit_failure_rolls_back_journey_ticket_AFC_and_receipt… |

Postman: folder `Gate validation (BE-10)`, điền `gateAccessToken` nhân viên/Admin, operator, raw QR và ga/chuyến từ DB demo. Để trống gateRequestId sinh UUID; giữ nguyên để replay, xóa trước thao tác khác. Copy source ID vào gateSourceRequestId khi hỗ trợ. Không lưu token/QR thật vào collection.

Subfolder `QR support (UC18)` có quy trình challenge → verify EMAIL/DOCUMENT → receipt → PNG. Nhập mã khách thực nhận vào gateQrEmailCode; collection không lấy mã từ HTTP. Giữ gateQrOperationId, xóa gateQrAccessRequestId cho lần nhập mã khác; muốn một hành động hỗ trợ khác thì xóa cả operation và hai request ID. GateQrAction=DISPLAY giữ QR, REPLACE thay QR. Có ví dụ Admin đọc/đóng ca hỗ trợ, thao tác này không cho qua cổng.

## Kết quả tự kiểm cuối ngày 25/11/2026

- Build Release `--no-restore --warnaserror`: 0 warning, 0 error. Target/runtime .NET 9; SDK 10.0.401 được chọn theo `rollForward=latestMajor` hiện có trong global.json.
- Harness đầy đủ: **258 passed, 0 failed, 0 skipped** — 112 unit và 146 integration. Trong đó BE-10 thêm **15 unit + 29 integration** so với baseline; harness nhận diện 105 test thuộc các lớp PostgreSQL được cấu hình, không đồng nhất số này với tổng integration.
- PostgreSQL 17.12 riêng `metro_test_be10_complete`: deploy mới 001–016 và verifier đạt. Deploy lại schema đã có và upgrade DB test checkpoint 014 cũng được kiểm tra; `build_deploy.py --check` khớp migrations. Không áp dụng migration lên Neon/DB chung.
- Smoke NFR-002: 100 quyết định QR, 20 request đồng thời, sau warmup; bản cuối **P95 316,84 ms, max 337,33 ms**, gồm cả TestServer HTTP/auth overhead. Mẫu đầu với khóa độc quyền toàn cục có P95 1.490,89 ms; đã sửa thành khóa đọc chung ở phần quyền/vận hành và giữ khóa độc quyền request/holder/ticket. Đây là fixture nhỏ local, chưa phải benchmark bộ dữ liệu/môi trường tham chiếu đầy đủ.
- Có ca mã OTP hết hạn trong lúc chờ khóa holder: đọc lại đồng hồ DB và điều kiện expires_at ngay tại UPDATE consume; từ chối, không tiêu mã hoặc thay credential. Các ca replay, sai purpose/ticket/action, 5 sai, global quota, SMTP retry/expiry, audit rollback, cặp trẻ và Admin review đều qua.
- Kiểm tra migrations 001–013 và 19 item/folder Postman có trước giữ nguyên nội dung; 17 request BE-10 và script JavaScript hợp lệ. PNG response không bị Postman test cố đọc như JSON. `git diff --check` đạt.
- Kết quả TRX/log local cuối ở `%TEMP%/metro-be10-local/srs-final`; PostgreSQL kiểm thử ở `%TEMP%/metro-be10-local/data`, đã dừng sau kiểm thử. Không để service test chạy nền.
- Tại thời điểm tự kiểm ngày 25/11, kết quả này chưa commit/push. Sau đó đã có commit BE-10 `a10b8a8`, merge dev `5b30cdf` và PR #90; đây không phải phê duyệt PR hoặc nghiệm thu FE/camera/cổng vật lý. Tích hợp môi trường chung cần dữ liệu nhận khách BE-13 và hợp nhất migration BE-14 như ghi ở trên.

## Phản hồi review ngày 26/11/2026

- Reviewer báo trên head `5b30cdf`: unit 112/112, integration 145/146; smoke P95 khoảng 1495 ms với DB Neon pooled. Đây là kết quả reviewer cung cấp, không phải lần tự chạy lại trên Neon. Không gọi kết quả này là “toàn bộ test xanh” và chưa đủ bằng chứng để khẳng định nguyên nhân chỉ là mạng.
- Smoke dùng TestServer cho HTTP nhưng vẫn truy cập PostgreSQL thật; stopwatch bao gồm DB execution, chờ pool và độ trễ đường API–DB. Dòng log cũ “excludes external network” không chính xác khi DB ở xa. Đã sửa mô tả, thêm phân loại loopback/non-loopback và probe `SELECT 1` ngoài mẫu scan. Không in endpoint/secret; không trừ probe khỏi P95, không nâng ngưỡng 1000 ms, không skip theo môi trường.
- Kết quả local ngày 25/11 và kết quả Neon là hai môi trường khác nhau. Cần chạy lại cùng môi trường/dataset SRS để kết luận NFR-002; test chức năng không thay cho nghiệm thu hiệu năng.
- Đã giải quyết trùng số 014 trong gói BE-10 bằng schema prerequisite 014 nguyên văn của BE-14 và gate 015–017. Xem mục Transaction và migration; không nhập API/runtime của PR #87.
- Kiểm lại bản sửa local ngày 26/11: Release `--warnaserror` đạt 0 warning/0 error; `build_deploy.py --check` đạt. Chạy đúng smoke bị review báo fail trên PostgreSQL 17 local (`metro_test_be10_complete`): **1 pass, 0 fail, 0 skip**, 100 request/20 client, **P95 320,94 ms, max 358,79 ms**. Probe DB 5 mẫu: min 0,10 / median 0,16 / max 2,67 ms. TRX ở `%TEMP%/metro-be10-local/review-20261126/gate-performance-review.trx`. Không chạy lại toàn bộ suite và chưa chạy lại Neon; số 258 ở trên là bằng chứng ngày 25/11. Các sửa phản hồi review chưa commit/push hoặc cập nhật PR trên GitHub.

## Kết quả tích hợp PR #90 ngày 26/11/2026

- Đã cập nhật dev `e66f058` gồm BE-15; giữ OpenAPI gate và reports, không nhập runtime BE-14/BE-16. [Khảo sát tất cả nhánh và PR mở](BE-10-INTEGRATION-REVIEW.md) ghi snapshot trước cập nhật.
- Schema chuẩn 001–017: 014 BE-14 nguyên văn từ `7cff576`, SHA256 `737729ad30cd60376aef07427cd212eeb170c3a8b986ed86c891e365e0335a9b`; 015–017 gate/QR/support. So sánh tự động xác nhận migrations 001–013 giống dev và nội dung/checksum ba gate migrations không đổi khi đổi tên.
- PostgreSQL 17.12: DB riêng `metro_test_be10_pr90_fresh_20261126` deploy sạch + `09_verify` + deploy lặp đạt; DB `metro_test_be10_pr90_upgrade_20261126` áp bộ BE-14 đến 014, verify bằng verifier của BE-14 rồi upgrade đến 017 + verify đạt. Checksum 014 giữ nguyên sau upgrade.
- DB legacy riêng `metro_test_be10_pr90_legacy_20261126` áp bộ gate 014–016 cũ; gói chuẩn mới bị chặn đúng bởi `Migration 014 checksum mismatch`, transaction rollback và lịch sử vẫn là gate 014 đến 016. Không sửa bảng schema_migrations để giả nâng cấp; không áp trên Neon/DB chung.
- Build Release `--no-restore --warnaserror`: **0 warning, 0 error**. Harness đầy đủ trên DB mới: **283 pass = 130 unit + 153 integration, 0 fail, 0 skip**; runner nhận diện 105 test trong các lớp PostgreSQL được cấu hình. Số 283 bao gồm BE-15 đã merge, không phải thêm 25 test gate.
- Smoke 100 request/20 client trong suite đầy đủ: **P95 338,30 ms, max 357,67 ms**. Probe DB 5 mẫu min 0,08 / median 0,14 / max 0,54 ms. Ngưỡng giữ 1000 ms; kết quả Neon 1495 ms trong review vẫn được ghi nhận, chưa chạy lại Neon và chưa nghiệm thu NFR-002 đầy đủ.
- `build_deploy.py --check`, Postman JSON và liên kết tài liệu đạt; toàn bộ folder/request/variable Postman có sẵn trên dev (gồm Reports BE-15) được giữ nguyên, folder gate vẫn có. Frontend không thay đổi.
- Log deploy/upgrade/TRX: `%TEMP%/metro-be10-local/pr90-review-20261126`. Các kết quả ở phần tự kiểm 25/11 và lần smoke riêng đầu 26/11 là lịch sử trước lần tích hợp này.
