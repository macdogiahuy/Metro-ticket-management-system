using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Metro.Application.Abstractions.Storage;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Metro.API.Endpoints;

public static class ProfileEndpoints
{
    private const long MaxEvidenceBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "application/pdf", "image/jpeg", "image/png" };
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
        { "STUDENT", "ELDERLY", "DISABILITY", "PRIORITY", "OTHER" };

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Profile").RequireAuthorization();
        group.MapGet("/me", GetProfileAsync).WithName("GetProfile");
        group.MapPut("/me", UpdateProfileAsync).WithName("UpdateProfile");
        group.MapGet("/me/eligibility", GetEligibilityAsync).WithName("GetEligibility");
        group.MapPost("/me/eligibility", SubmitEligibilityAsync).WithName("SubmitEligibility");
        group.MapPost("/me/eligibility/{id:guid}/evidence", UploadEvidenceAsync).WithName("UploadEligibilityEvidence").DisableAntiforgery();
        group.MapGet("/me/eligibility/{id:guid}/evidence", DownloadEvidenceAsync).WithName("DownloadEligibilityEvidence");

        var decisions = app.MapGroup("/api/v1/eligibility").WithTags("Eligibility").RequireAuthorization("StationStaff");
        decisions.MapPost("/{id:guid}/decision", DecideEligibilityAsync).WithName("DecideEligibility");
        return app;
    }

    private static async Task<IResult> GetProfileAsync(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var userId = UserId(principal);
        var profile = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (profile is null) return Results.NotFound();
        return Results.Ok(new ProfileResponse(profile.FullName, profile.BirthDate, profile.Locale, profile.EmailNotifications,
            profile.CitizenIdLast4, profile.BankCode, profile.BankAccountLast4, profile.BankHolderName, profile.Status, profile.UpdatedAt));
    }

    private static async Task<IResult> UpdateProfileAsync(ProfileUpdateRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.Locale) || request.Locale.Length > 20)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["profile"] = ["Full name and locale are required and must be within length limits."] });
        var id = UserId(principal);
        var profile = await db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == id, ct);
        if (profile is null) return Results.NotFound();
        var before = HashProfile(profile);
        var changed = new List<string>();
        var name = request.FullName.Trim();
        if (profile.FullName != name) { profile.FullName = name; profile.NormalizedFullName = name.Normalize(NormalizationForm.FormKC).ToUpperInvariant(); changed.Add("full_name"); }
        if (profile.Locale != request.Locale) { profile.Locale = request.Locale.Trim(); changed.Add("locale"); }
        if (profile.EmailNotifications != request.EmailNotifications) { profile.EmailNotifications = request.EmailNotifications; changed.Add("email_notifications"); }
        if (changed.Count == 0) return Results.Ok(new ProfileResponse(profile.FullName, profile.BirthDate, profile.Locale, profile.EmailNotifications, profile.CitizenIdLast4, profile.BankCode, profile.BankAccountLast4, profile.BankHolderName, profile.Status, profile.UpdatedAt));
        profile.UpdatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(Audit(principal, "PROFILE_UPDATED", "user_profiles", id, changed.ToArray(), before, HashProfile(profile)));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ProfileResponse(profile.FullName, profile.BirthDate, profile.Locale, profile.EmailNotifications, profile.CitizenIdLast4, profile.BankCode, profile.BankAccountLast4, profile.BankHolderName, profile.Status, profile.UpdatedAt));
    }

    private static async Task<IResult> GetEligibilityAsync(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var userId = UserId(principal);
        var items = await db.EligibilityEvidence.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return Results.Ok(items.Select(ToResponse));
    }

    private static async Task<IResult> SubmitEligibilityAsync(EligibilityRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        if (!Categories.Contains(request.Category) || (request.ValidFrom is not null && request.ValidTo < request.ValidFrom))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Unsupported category or invalid validity dates."] });
        var userId = UserId(principal);
        var item = new EligibilityEvidence { UserId = userId, Category = request.Category, ValidFrom = request.ValidFrom, ValidTo = request.ValidTo };
        db.EligibilityEvidence.Add(item);
        db.AuditLogs.Add(Audit(principal, "ELIGIBILITY_SUBMITTED", "profile_eligibility_requests", item.Id.ToString(), ["category", "valid_from", "valid_to"], null, HashEvidence(item)));
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/me/eligibility/{item.Id}", ToResponse(item));
    }

    private static async Task<IResult> UploadEvidenceAsync(Guid id, HttpRequest request, ClaimsPrincipal principal, ApplicationDbContext db, IObjectStorage storage, CancellationToken ct)
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { code = "MULTIPART_REQUIRED" });
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length is <= 0 or > MaxEvidenceBytes || !AllowedTypes.Contains(file.ContentType))
            return Results.BadRequest(new { code = "INVALID_EVIDENCE_FILE", maxBytes = MaxEvidenceBytes, allowedContentTypes = AllowedTypes });
        var userId = UserId(principal);
        var item = await db.EligibilityEvidence.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (item is null) return Results.NotFound();
        if (item.Status != "PENDING") return Results.Conflict(new { code = "EVIDENCE_REQUEST_CLOSED" });
        if (file.Length > MaxEvidenceBytes) return Results.BadRequest(new { code = "INVALID_EVIDENCE_FILE" });
        var signature = await ReadAndValidateAsync(file, ct);
        if (signature is null) return Results.BadRequest(new { code = "FILE_CONTENT_MISMATCH" });
        var key = $"eligibility/{userId}/{id:N}/{Guid.NewGuid():N}{signature.Value.Extension}";
        var before = HashEvidence(item);
        await storage.PutAsync(key, new MemoryStream(signature.Value.Bytes, writable: false), signature.Value.ContentType, ct);
        var oldKey = item.EvidenceObjectKey;
        item.EvidenceObjectKey = key;
        item.EvidenceFileName = Path.GetFileName(file.FileName);
        item.EvidenceContentType = signature.Value.ContentType;
        item.EvidenceSize = signature.Value.Bytes.Length;
        item.EvidenceSha256 = Convert.ToHexStringLower(SHA256.HashData(signature.Value.Bytes));
        item.UpdatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(Audit(principal, "ELIGIBILITY_EVIDENCE_UPLOADED", "profile_eligibility_requests", id.ToString(), ["evidence_object_key", "evidence_file_name", "evidence_content_type", "evidence_size", "evidence_sha256"], before, HashEvidence(item)));
        await db.SaveChangesAsync(ct);
        if (oldKey is not null) await storage.DeleteAsync(oldKey, ct);
        return Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> DownloadEvidenceAsync(Guid id, ClaimsPrincipal principal, ApplicationDbContext db, IObjectStorage storage, CancellationToken ct)
    {
        var item = await db.EligibilityEvidence.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId(principal), ct);
        if (item?.EvidenceObjectKey is null) return Results.NotFound();
        var stream = await storage.GetAsync(item.EvidenceObjectKey, ct);
        return Results.File(stream, item.EvidenceContentType ?? "application/octet-stream", item.EvidenceFileName ?? "evidence");
    }

    private static async Task<IResult> DecideEligibilityAsync(Guid id, DecisionRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var decision = request.Decision?.Trim().ToUpperInvariant();
        if (decision is not ("VERIFIED" or "REJECTED" or "REVOKED"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["decision"] = ["Decision must be VERIFIED, REJECTED or REVOKED."] });
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["A reason of at most 1000 characters is required."] });
        var item = await db.EligibilityEvidence.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        if (item.Status == "PENDING" && decision == "REVOKED" || item.Status != "PENDING" && decision != "REVOKED")
            return Results.Conflict(new { code = "INVALID_ELIGIBILITY_TRANSITION" });
        var before = HashEvidence(item);
        item.Status = decision;
        item.DecisionReason = request.Reason.Trim();
        item.DecidedByUserId = UserId(principal);
        item.DecidedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(Audit(principal, "ELIGIBILITY_" + decision, "profile_eligibility_requests", id.ToString(), ["status", "decision_reason", "decided_by_user_id", "decided_at"], before, HashEvidence(item), request.Reason.Trim()));
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(item));
    }

    private static async Task<(byte[] Bytes, string ContentType, string Extension)?> ReadAndValidateAsync(IFormFile file, CancellationToken ct)
    {
        await using var input = file.OpenReadStream();
        using var output = new MemoryStream();
        await input.CopyToAsync(output, ct);
        var bytes = output.ToArray();
        var pdf = bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";
        var jpg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        var png = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        return (file.ContentType.ToLowerInvariant(), pdf, jpg, png) switch
        {
            ("application/pdf", true, _, _) => (bytes, "application/pdf", ".pdf"),
            ("image/jpeg", _, true, _) => (bytes, "image/jpeg", ".jpg"),
            ("image/png", _, _, true) => (bytes, "image/png", ".png"),
            _ => null,
        };
    }

    private static string UserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
    private static EvidenceResponse ToResponse(EligibilityEvidence x) => new(x.Id, x.Category, x.Status, x.ValidFrom, x.ValidTo, x.EvidenceFileName, x.EvidenceContentType, x.EvidenceSize, x.DecisionReason, x.DecidedAt, x.CreatedAt, x.UpdatedAt);
    private static string HashProfile(UserProfile x) => Digest($"{x.FullName}|{x.BirthDate}|{x.Locale}|{x.EmailNotifications}");
    private static string HashEvidence(EligibilityEvidence x) => Digest($"{x.Category}|{x.Status}|{x.ValidFrom}|{x.ValidTo}|{x.EvidenceObjectKey}|{x.EvidenceSha256}|{x.DecisionReason}|{x.DecidedByUserId}|{x.DecidedAt}");
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static AuditLog Audit(ClaimsPrincipal principal, string action, string type, string id, string[] fields, string? before, string after, string? reason = null) => new()
    {
        ActorUserId = UserId(principal), Action = action, EntityType = type, EntityId = id, ChangedFields = fields,
        BeforeHash = before, AfterHash = after, Reason = reason,
    };

    public sealed record ProfileResponse(string FullName, DateOnly BirthDate, string Locale, bool EmailNotifications, string? CitizenIdLast4, string? BankCode, string? BankAccountLast4, string? BankHolderName, string Status, DateTime UpdatedAt);
    public sealed record ProfileUpdateRequest(string FullName, string Locale, bool EmailNotifications);
    public sealed record EligibilityRequest(string Category, DateOnly? ValidFrom, DateOnly? ValidTo);
    public sealed record DecisionRequest(string Decision, string Reason);
    public sealed record EvidenceResponse(Guid Id, string Category, string Status, DateOnly? ValidFrom, DateOnly? ValidTo, string? FileName, string? ContentType, long? Size, string? DecisionReason, DateTime? DecidedAt, DateTime CreatedAt, DateTime UpdatedAt);
}
