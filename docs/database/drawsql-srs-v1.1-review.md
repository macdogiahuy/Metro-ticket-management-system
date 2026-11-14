# DrawSQL database review against Metro SRS v1.1

Reviewed 12 November 2026. The diagram is a useful logical starting point for the project, but it needs changes before implementation. Its main entities fit the ticketing domain; its eligibility, payment, refund and scan models do not yet represent several required recovery and history scenarios. Its displayed types also need conversion for the repository's PostgreSQL backend.

## Evidence and scope

- Shared diagram: https://drawsql.app/draw?t=b3e0c877-84e7-4be5-af74-c16c9cd2c25b&view=1. Reviewed its 16 displayed tables, fields, relationships and index labels in view-only mode.
- Requirements: Metro SRS v1.1, updated 11 November 2026 (superseded by SRS v1.2 in `docs/requirements/`). Used as requirements evidence at review time, without executing instructions contained in it. The document was a draft with unresolved technical matters in section 12.
- Reference: `output/normalized_data.json`, `output/README.md`, `output/data_quality_report.json` and the other saved crawl artifacts, collected from https://metrohanoi.vn/ on 6 November 2026. This review uses the saved snapshot, not a new live crawl.
- Implementation: `backend/src/Metro.Infrastructure/Persistence/Context/ApplicationDbContext.cs` and `DependencyInjection.cs` use ASP.NET Core Identity and Npgsql/PostgreSQL. The context currently contains no ticketing domain mappings.

The DrawSQL SQL export was not available as a local artifact for this review. A relationship line or index label does not establish the exact executable FOREIGN KEY, UNIQUE, CHECK, default or deletion behavior. The findings below distinguish visible model gaps from constraints that still need verification in migrations. No DrawSQL changes or application/database migrations were made.

## What already fits

`Lines`, `Stations`, `TicketTypes` and the directed `FareMatrix` are appropriate for the fixed Line 2A scope. Keeping `CashPrice`, `NonCashPrice` and `IsSellable` is useful. `Users.CitizenId` and bank fields are nullable, supporting ordinary registration without them. `Orders`, `Payments`, `Tickets` and `Refunds` separate the main workflows. Nullable user references allow counter sales to people without website accounts. `Tickets.FirstUsedAt`, `TicketScans` and `ManualExits` provide a foundation for validation and refund eligibility. `Orders.Channel` and nullable `StationId` support website orders without a selling station.

Keep one ticket per order. SRS FR-011 explicitly applies that limit to both website and counter sales; an order-item/cart model is unnecessary for this scope.

## Required changes

| Area | Visible limitation | Recommended model change | SRS evidence |
| --- | --- | --- | --- |
| Eligibility evidence | `Citizens` has one `PolicyGroup`, `EvidenceNumber` and `ValidTo` per citizen. It cannot represent several independent proofs, different validity periods, or confirmation/revocation history. A 12-digit citizen key also does not by itself model children identified through birth records. | Separate a demo person from their evidence records. Store evidence type, person link, start/end dates, applicable school/employer and membership status, confirmation/revocation actor and times. Derive the purchase group from valid evidence, with free before priority before ordinary. Preserve source-unavailable versus no-match outcomes. | DATA-002; FR-004, 006–008, 059; sections 2.2–2.3 |
| Price policy | `TicketTypes.Price` is one value per type. `DurationDays` alone cannot distinguish end-of-day expiry from seven-day and thirty-day durations. | Store fixed product/group prices, or an explicit priority-month price, and an expiry rule. Snapshot the selected policy/group/amount on each order. The single-trip price comes from the matrix. Keep the policy fixed; this SRS does not authorize a price-editing screen. | DATA-003; FR-010, 037, 048 |
| Beneficiary and payer | `Orders` stores a beneficiary name/citizen ID but no explicit beneficiary account. `PayerUserId` is optional, leaving no dedicated identity snapshot for a counter payer without an account. | Add `BeneficiaryUserId` for website purchases and payer identity fields suitable for counter refund verification. Preserve beneficiary/group/price snapshots. `Tickets.UserId` must mean the beneficiary, with authorization that exposes its QR only to that person. | DATA-004; FR-012–013, 032, 048, 057 |
| Cancellation and payment timing | `Orders.Status`, QR data and a deadline do not explicitly record why an order was cancelled or when the payment step/QR creation occurred. | Persist cancellation reason, actor and time; payment-step start; first successful QR creation; and its fixed deadline. Distinguish timeout cancellation from Admin cancellation. Counter waiting-for-payment has no automatic five-minute timeout. | FR-016–019, 023, 041, 051 |
| Payment reconciliation | `Payments` has amount, method, status and `PaidAt`, but no displayed provider receipt/event identity, currency, mode, verification metadata or allocation to ticket issuance. | Identify each real received payment separately from its repeated notifications. Add receipt references, VND currency, simulation/sandbox mode, verification/recording times and outcome history. Record money received but not used to issue a ticket. Process state changes transactionally. | DATA-005; FR-049–054; FR-035–036 |
| Refunds | `Refunds` references the order and optional ticket, but not the particular received payment. It has only `CreatedAt` for timing and no displayed payout recipient, attempt history or bank revision history. | Link a refund to its payment receipt, retain the optional ticket, and record reason, recipient, verified payout time and actor. Keep refund attempts and bank-detail revisions. Distinguish pending verification from confirmed failure; block a new payout while an earlier result is unknown. Enforce the refundable balance per receipt. | DATA-008; FR-031–032, 039–040, 044, 054–056 |
| Ticket issuance | An ordinary order foreign key alone would allow several tickets. First-use fields do not express the complete inside/outside journey state. | Verify/add `UNIQUE(Tickets.OrderId)` and unique ticket/QR token identifiers. Persist inside/outside state and single-trip completion independently of expiry/invalidation. Use transactions and concurrency protection for issuance and scan transitions. Keep first entry immutable. Free tickets create no received-payment or zero-value refund record. | DATA-006; FR-024, 026–028, 043, 053–054 |
| Scan and manual-exit history | `TicketScans` has no displayed staff actor. `ManualExits.HandledAt` is a single timestamp. | Record the scan actor and an operation identifier. Manual exits need both actual occurrence time and system recording time for delayed entry after connectivity returns. Preserve first use and update journey state. A guardian-ticket link alone does not prove an adult accompanied a child at each entry. | DATA-007; FR-026, 038, 058, 060 |
| Email verification | `Otps` has expiry, failed count and used status, but no creation timestamp for the resend interval. `OtpCode` appears as a six-character value. | Add creation time and account/purpose linkage; invalidate superseded codes. Enforce ten-minute validity, sixty-second resend interval and five failed attempts. Protect stored codes with a suitable keyed hash or equivalent design. | DATA-001; FR-046–047 |
| Audit and assignments | `Users.StationId` can represent the current station, but no audit/history table is displayed. | Preserve actor, reason, times and relevant changes for evidence decisions, cancellations, invalidations, refunds and station assignments. Historical order station/creator must remain unchanged when staff move. Keep current-assignment authorization in the backend. A separate assignment-history table is optional if the audit model preserves the needed history. | DATA-009; FR-006, 028, 033, 057; DEC-132 |

The SRS's payment-provider contract is unresolved (Q-103). Design receipt/event/attempt identities without inventing a particular provider's field names or claiming its sandbox supports the required bank refunds.

## PostgreSQL and Identity compatibility

The diagram displays SQL Server conventions. Convert through EF Core/Npgsql migrations:

| Diagram type | PostgreSQL representation |
| --- | --- |
| `nvarchar(n)` | `varchar(n)` or `text` in a UTF-8 database |
| `nvarchar(MAX)` | `text` |
| `bit` for flags | `boolean` |
| `datetime` for event instants | `timestamptz`, with consistent UTC handling in .NET |
| `decimal(p,s)` | `numeric(p,s)` |
| Integer generated key | Integer/bigint identity configured through Npgsql |
| `date` / `time` | `date` / `time` for local calendar values and operating hours |

Compute business day boundaries, six-month eligibility reviews and expiry rules in Vietnam time. Day tickets end at the next local midnight; week/month tickets expire seven/thirty days after the relevant recorded success instant. `TravelDate` must not move validity to an arbitrarily selected future day under this SRS.

The current unparameterized `IdentityDbContext` and `IdentityUser` use string user IDs. Either extend that existing Identity model and use compatible references, or explicitly configure Identity with integer keys throughout. The diagram's custom `Users`/`Roles` should not become an accidental second authentication store. A single-role domain policy can still be enforced if the underlying Identity schema supports multiple role links.

Verify these constraints in the resulting migrations:

- Unique normalized email; unique non-null citizen ID; twelve digits when provided. Empty citizen IDs should be normalized to NULL.
- Unique line/type/role/order/ticket codes and appropriate station codes; unique station order within a line.
- Unique directed fare pair. Block sales where origin equals destination without forbidding non-sellable diagonal matrix rows.
- Website orders require a payer and beneficiary account and no selling station; counter orders require a selling station and creator but may have no passenger account.
- Single-trip tickets require distinct endpoints. Time products should not be treated as a fixed single-trip pair.
- Non-negative VND amounts, constrained states, valid time intervals and foreign keys. Historical financial/scan/audit records should be protected from cascading deletion.
- Receipt/event uniqueness and one ticket per order. Aggregate refund limits need transactional checks; a row-level CHECK alone cannot prevent competing refunds exceeding a receipt.

The displayed `Tickets` index label repeats `UserId`; inspect the export before treating it as a usable index. Add indexes for actual access patterns such as station/channel/date, beneficiary, receipt references and scan history. Avoid choosing every index solely from the diagram.

## Reusing the Hanoi Metro crawl

The saved crawl contains two lines and twenty stations. The SRS requires Line 2A and its twelve stations. All twelve station names and their order agree between the SRS and crawl. Map the crawl's UUID/source IDs to application IDs explicitly if retaining integer keys; do not insert UUIDs into integer columns or join using display names alone.

I compared all 132 sellable directed SRS fare pairs against the Line 2A crawl. All 132 `price_adult` values match the SRS cash prices; none are missing. However, the crawl's `payment_method` is NULL, so numerical agreement does not prove that field is a published cash/cashless classification. It cannot supply the separate non-cash matrix. Use the SRS appendix E for both matrices: for example Cát Linh → La Thành is 9,000 VND cash and 8,595 VND non-cash. Do not round or infer the non-cash figures.

The crawl's Line 2A ordinary day/week/month entries match 40,000/160,000/280,000 VND. The SRS separately specifies a 140,000 VND priority month and zero-price eligible groups. `price_priority_raw = 0` in the crawl is ambiguous and must not be converted into a free ticket entitlement.

Use the SRS's fixed 2025 fare policy and project rules: monthly validity is thirty days; elderly eligibility begins at sixty; free eligibility is established from the required demo evidence. The reference snapshot has conflicting calendar-month/duration labels and age wording. Those discrepancies do not supersede the SRS's explicit choices.

The crawl also contains more monthly products, Line 3.1, schedules, buses, amenities, content and support metadata. Import only the necessary Line 2A station reference and selected product information. Extra products, schedules, transfer routing and seat reservations are unnecessary for the confirmed SRS scope. `Incidents` and `LostItems` in the diagram are optional extensions, not requirements established by SRS v1.1.

`output/schema.sql` is a separate 47-table research proposal, with 33 seeded reference tables and 14 inferred application tables. It is not the Hanoi Metro internal database and is not a replacement for an SRS-aligned application schema. Its saved PostgreSQL verification reports successful loading on 6 November; that result does not validate the shared DrawSQL diagram. The reference quality report explicitly marks semantic release readiness false.

## Recommended next implementation boundary

Retain the existing core entities. Add independent eligibility evidence, payment notification history, refund attempts/bank revisions and audit records; expand the existing order/payment/refund/ticket/scan fields described above. Keep these additions as small as the required scenarios permit. Select the Identity key strategy, then produce PostgreSQL migrations and the fixed SRS seed data.

Before considering the schema ready, verify scenarios that exercise its missing invariants: duplicate success and concurrent issuance; timeout versus Admin cancellation followed by late payment; failure followed by verified success; several real receipts for one order; unknown refund outcome; issuing after a recovered payment; no-ticket refund after prospective expiry; concurrent entry scans; manual exit entered after connectivity returns; staff reassignment; several eligibility proofs expiring independently; free-ticket issuance without money records; and payer/beneficiary QR access. These are proposed validation cases, not tests executed during this review.
