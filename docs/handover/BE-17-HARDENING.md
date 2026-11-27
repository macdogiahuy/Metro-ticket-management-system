# BE-17 — Hardening NFR checklist & evidence

## Scope (PR link: issue #32)

Production logic: `HardeningOptions`, `SecurityHeadersMiddleware`, `FixedWindowRateLimiter`,
`ClientRateLimitMiddleware`, `PiiRedactor`, `AuditGuardMiddleware` + wiring in `Program.cs`,
`appsettings.json`, `RequestLoggingMiddleware`, `GlobalExceptionHandler` (~261 implementation lines).
Tests are kept separate and are not counted as implementation logic.

## Checklist NFR

| Control | Implementation | Evidence |
| --- | --- | --- |
| Security headers | `nosniff`, `DENY`, `no-referrer`, CSP, `Permissions-Policy` on every response; `no-store` for `/admin`, `/reports`, `/me`, `/tickets`, `/orders`, `/payments` | `HardeningEndpointsTests.Security_headers_are_present_on_public_and_sensitive_routes` |
| Rate limit | Fixed window per `sub` or remote IP on `/api/v1/auth/`, `/api/v1/payments`, `/api/v1/webhooks/`; configurable `PermitLimit`/`WindowSeconds`; 429 RFC 9457 `code=RATE_LIMITED` + `Retry-After` | `FixedWindowRateLimiterTests`, `HardeningEndpointsTests.Rate_limit_returns_429_problem_details_after_configured_burst` |
| PII masking | `PiiRedactor` strips email, phone, Bearer/JWT token and 6-digit OTP from request-path logs, handled-exception logs and ProblemDetails `Detail` | `PiiRedactorTests`; no request body or query string is ever logged |
| Audit bắt buộc | Every successful `/api/v1/admin` write gets an append-only `AuditLog` receipt carrying `X-Correlation-Id`, actor and path; response echoes the correlation id | `HardeningEndpointsTests.Successful_admin_write_always_produces_an_audit_record` |
| 401/403 | Anonymous admin request → 401, passenger admin request → 403; headers still present on both | `Security_headers_are_present_on_public_and_sensitive_routes` |
| Idempotency | BE-17 adds no new write endpoint; the audit receipt uses a caller-supplied correlation id when present, so retries reuse the same id | `AuditGuardMiddleware` |
| Light load | 60 requests at 10 concurrent clients against `/health/live`, all 200 with headers, P95 < 1000 ms | `Hardening_headers_survive_a_light_concurrent_load` |

## Verification (28/11/2026)

- `dotnet build backend/Metro.sln -c Release --warnaserror`: **0 warning, 0 error**.
- Unit tests: **229 passed, 0 failed, 0 skipped**.
- Integration tests on a dedicated local PostgreSQL database `metro_test_be17_*`
  (`infra/database/deploy.sql` + `scripts/09_verify.sql`, rc=0): **173 passed, 0 failed, 0 skipped**.
- No Docker / in-memory provider / production or shared database was used.
- No secrets or PII appear in logs or in this document. No production migration was applied.
