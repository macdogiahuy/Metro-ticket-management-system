# Ánh xạ test case với automation hiện có

123 test case UC trong [TEST-CASES](TEST-CASES.md) là các ca thiết kế QA, không phải 123 method hoặc class C#. Một method xUnit có thể chứa nhiều assertion/biến thể; một case QA có thể cần cả unit, API và UI. Chỉ đánh Pass case khi đủ bước, dữ liệu và evidence của case đó, không lấy việc tìm thấy class làm kết quả chạy.

Bảng này đối chiếu phần automation liên quan các thay đổi đã lấy từ `origin/dev` **e8243d4** ngày **24/11/2026**. Đây là ánh xạ **một phần** để QA-02 tái sử dụng, không phải tuyên bố toàn bộ 27 UC đã có automation.

## 1 Class và phạm vi assertion

| Case QA liên quan | Class và file | Method hoặc assertion đối chiếu | Giới hạn |
| --- | --- | --- | --- |
| TC-UC09-01/02/04 | [FareCalculatorTests](../../backend/tests/Metro.UnitTests/Fare/FareCalculatorTests.cs) | `SingleTrip_segment_formula_and_discounts_match_AC_X04_and_AC_X23`, `Periodic_passes_apply_20_percent_elderly_discount`, `HalfUp_rounding_applies_to_fractional_vnd_discount` | Tính giá thuần; không kiểm HTTP/Checkout/MoMo |
| TC-UC06-04/05; TC-UC09-02/03 | [FareCalculatorTests](../../backend/tests/Metro.UnitTests/Fare/FareCalculatorTests.cs) | `Elderly_under_60_or_unverified_adult_falls_back_to_ordinary_fare`, `Child_under_6_rules_allow_free_single_trip_and_never_fallback_to_paid`, `NullSafe_duplicate_key_not_sellable_and_explicit_od_override_behave_deterministically` | Có fixture tham khảo và fixture đã xác minh; không kiểm quy trình duyệt hồ sơ/UI |
| TC-UC06-04; TC-UC11-10; TC-UC25-02 | [FareRulesTests](../../backend/tests/Metro.UnitTests/Fare/FareRulesTests.cs) | `CalculateAgeYears_and_VietnamDate_respect_utc_plus_7_boundaries`, nhóm `ValidateProductSpec`, `ValidatePaidRange`, `RoundHalfUp` | Rule thuần; chưa chứng minh thời điểm PaymentStart hoặc giao dịch thật |
| TC-UC09-01/02/03 và biến thể reference/matrix | [FareQuoteEndpointsTests](../../backend/tests/Metro.IntegrationTests/Endpoints/FareQuoteEndpointsTests.cs) | `Public_fare_quote_computes_by_published_version_segment_formula_od_override_and_errors`, `Authenticated_quote_checks_verified_elderly_age_fallback_and_child_free_without_logging_pii` | Cần PostgreSQL test; vẫn cần kiểm UI, toàn bộ biên và chủ sở hữu theo run |
| TC-UC25-01/02/04 và biến thể product/idempotency | [AdminProductEndpointsTests](../../backend/tests/Metro.IntegrationTests/Endpoints/AdminProductEndpointsTests.cs) | `Admin_products_enforce_401_403_duration_validation_idempotency_and_audit` | Bao gồm auth, validation, replay/conflict và audit trong một method; không bao trùm mọi key length/race/multi-instance |
| TC-UC25-01/02/04 và biến thể fare lifecycle/guards | [AdminFareEndpointsTests](../../backend/tests/Metro.IntegrationTests/Endpoints/AdminFareEndpointsTests.cs) | `Admin_fares_enforce_auth_null_safe_key_maker_checker_overlap_idempotency_and_audit` | Cần PostgreSQL; đối chiếu riêng từng guard, không suy ra mọi route/role đã chạy |
| TC-UC25-01/02/04 và biến thể policy lifecycle/guards | [AdminEligibilityPolicyEndpointsTests](../../backend/tests/Metro.IntegrationTests/Endpoints/AdminEligibilityPolicyEndpointsTests.cs) | `Admin_eligibility_policies_enforce_auth_srs_groups_maker_checker_idempotency_and_audit` | Policy quản trị khác quy trình hồ sơ UC06; không thay test duyệt giấy tờ hoặc UI |
| TC-UC11-04/05/08; TC-UC12-04; TC-UC13-02/04 | [TicketEndpointsTests](../../backend/tests/Metro.IntegrationTests/Endpoints/TicketEndpointsTests.cs) | `Future_single_has_no_QR_before_service_day`, `All_pass_products_use_paid_time_and_calendar_EndExclusive`, `Two_callbacks_and_two_workers_keep_one_ticket_and_current_credential`, `Pass_QR_remains_available_when_app_clock_lags_behind_database_clock` | Chỉ các assertion trong method tương ứng; race payment/gate và toàn bộ flow UI vẫn cần evidence riêng |

Với các nhóm case khác, bắt đầu từ [backend/tests](../../backend/tests/README.md), đối chiếu assertion của Auth/Profile/Catalog/Administration/Payment/Ticket tests rồi ghi mapping vào run. Ca chưa có assertion hoặc chưa có tính năng phải được thực thi thủ công khi đủ điều kiện, hoặc ghi Blocked; không tự sinh test chỉ assert hằng số để đủ số lượng.

## 2 Contract và công cụ API

- UC09: [FareEndpoints](../../backend/src/Metro.API/Endpoints/FareEndpoints.cs), GET `/api/v1/fares/quote`, cho phép Guest; [FareQuoteContracts](../../backend/src/Metro.Application/Abstractions/Fare/FareQuoteContracts.cs) định nghĩa flags tham khảo/xác minh/fallback và nguồn giá.
- UC25: [AdminProductEndpoints](../../backend/src/Metro.API/Endpoints/AdminProductEndpoints.cs), [AdminFareEndpoints](../../backend/src/Metro.API/Endpoints/AdminFareEndpoints.cs), [AdminEligibilityPolicyEndpoints](../../backend/src/Metro.API/Endpoints/AdminEligibilityPolicyEndpoints.cs); các nhóm đều yêu cầu policy `Admin`.
- Replay UC25: [IdempotencyEndpointFilter](../../backend/src/Metro.API/Filters/IdempotencyEndpointFilter.cs); header là tùy chọn theo contract hiện tại. Kiểm có header và không header riêng, không tự giả định tất cả mutation bắt buộc header.
- [Postman collection hiện có](../postman/Metro.API.postman_collection.json) đã có request Fare/Admin Product/Fare/Eligibility Policy. QA-02 phải đối chiếu assertion và environment trước khi gọi là regression suite; chỉ có collection không chứng minh API đã pass.

## 3 Kết quả kiểm tra khi đồng bộ

Nhánh `feature/QA-01-test-plan-and-test-cases` đã merge `origin/dev` e8243d4 bằng merge commit **05b63d1**, không conflict. Các file ứng dụng backend/frontend/infra và Postman sau merge không có thay đổi riêng so với dev; thay đổi QA tiếp theo nằm trong tài liệu.

Đã chạy tại thư mục `backend` ngày 24/11/2026:

```powershell
dotnet test tests/Metro.UnitTests/Metro.UnitTests.csproj --no-restore --configuration Release --filter FullyQualifiedName~Metro.UnitTests.Fare --logger "console;verbosity=minimal"
```

Kết quả: **36 Passed, 0 Failed, 0 Skipped**, target `net9.0`; SDK thực tế **10.0.301**, được chọn theo `rollForward` trong [global.json](../../backend/global.json). Đây là unit test của `FareCalculatorTests` và `FareRulesTests`, không phải 36 case QA đã hoàn tất API/UI.

Đã build toàn solution backend bằng `dotnet build Metro.sln --configuration Release --no-restore --warnaserror`: **Build succeeded, 0 Warning, 0 Error**, gồm API và hai project test. Kiểm tài liệu: link nội bộ, cấu trúc bảng Markdown, ID/range truy vết 27 UC, 16 tham chiếu method và YAML issue form 14 field đều hợp lệ; `git diff --check` không có lỗi whitespace.

Integration PostgreSQL chưa chạy trong lượt này vì chưa đặt `METRO_TEST_POSTGRES_CONNECTION_STRING`, `METRO_TEST_POSTGRES_DATABASE`, `METRO_TEST_POSTGRES_REQUIRED`. Chạy đầy đủ theo [test harness](../../backend/tests/README.md) khi có DB test riêng đã apply/verify SQL; không dùng connection production hoặc chấp nhận skipped làm Pass. E2E/UAT và MoMo TEST thực tế tiếp tục thuộc QA-02/QA-03; trạng thái ma trận chưa tự chuyển sang Pass.
