namespace Metro.API.Configuration;

/// <summary>BE-17 hardening knobs. Defaults are safe for production; tests raise rate limits.</summary>
public sealed class HardeningOptions
{
    public bool SecurityHeadersEnabled { get; set; } = true;

    public RateLimitOptions RateLimiting { get; set; } = new();

    public sealed class RateLimitOptions
    {
        public bool Enabled { get; set; } = true;
        /// <summary>Requests allowed per window per client (subject or remote IP).</summary>
        public int PermitLimit { get; set; } = 120;
        public int WindowSeconds { get; set; } = 60;
        /// <summary>Only these path prefixes are limited; empty means all API paths.</summary>
        public string[] Paths { get; set; } = ["/api/v1/auth/", "/api/v1/payments", "/api/v1/webhooks/"];
    }

    /// <summary>True => fail the request when an audited write has no AuditLog entry.</summary>
    public bool RequireAuditOnAdminWrites { get; set; } = true;
}
