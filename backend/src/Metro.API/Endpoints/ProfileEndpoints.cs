using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metro.Application.Abstractions.Storage;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Metro.API.Endpoints;

/// <summary>UC05/UC06 owner profile and administrator concession verification.</summary>
public static class ProfileEndpoints
{
    private const long MaxEvidenceBytes = 5 * 1024 * 1024;
    private const int MaxEvidenceFiles = 3;
    private const string EntityType = "profile_eligibility_requests";
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "application/pdf", "image/jpeg", "image/png" };
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal) { "STUDENT", "ELDERLY" };
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Profile").RequireAuthorization();
        group.MapGet("/me", GetProfileAsync).WithName("GetProfile").Produces<ProfileResponse>();
        group.MapPut("/me", UpdateProfileAsync).WithName("UpdateProfile").Produces<ProfileResponse>();
        group.MapGet("/me/eligibility", GetEligibilityAsync).WithName("GetEligibility");
        group.MapGet("/me/eligibility/{id:guid}", GetOwnEligibilityAsync).WithName("GetOwnEligibility");
        group.MapPost("/me/eligibility", SubmitEligibilityAsync).WithName("SubmitEligibility").Produces<EvidenceResponse>(201);
        group.MapPost("/me/eligibility/{id:guid}/evidence", UploadEvidenceAsync)
            .WithName("UploadEligibilityEvidence").DisableAntiforgery().Accepts<IFormFile>("multipart/form-data");
        group.MapGet("/me/eligibility/{id:guid}/evidence", DownloadOwnEvidenceAsync).WithName("DownloadEligibilityEvidence");

        var reviews = app.MapGroup("/api/v1/eligibility").WithTags("Eligibility").RequireAuthorization("Admin");
        reviews.MapGet("/", ListReviewQueueAsync).WithName("ListEligibilityReviews");
        reviews.MapGet("/{id:guid}", GetReviewAsync).WithName("GetEligibilityReview");
        reviews.MapGet("/{id:guid}/evidence", DownloadReviewEvidenceAsync).WithName("DownloadReviewEvidence");
        reviews.MapPost("/{id:guid}/decision", DecideEligibilityAsync).WithName("DecideEligibility");
        reviews.MapPut("/{id:guid}/hold", SetEvidenceHoldAsync).WithName("SetEligibilityEvidenceHold");
        return app;
    }

    private static async Task<IResult> GetProfileAsync(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var id = UserId(principal);
        var profile = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id, ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (profile is null || user is null) return Missing();
        var locked = await IdentityLockedAsync(db, profile, ct);
        return Results.Ok(ToProfile(profile, user.Email, user.PhoneNumber, locked));
    }

    private static Task<IResult> UpdateProfileAsync(ProfileUpdateRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
        => ForUserAsync(db, UserId(principal), async () =>
        {
            var name = request.FullName?.Trim();
            var locale = request.Locale?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 200 || string.IsNullOrEmpty(locale) || locale.Length > 20 ||
                request.BirthDate is { } dob && (dob > Today || dob < Today.AddYears(-130)) ||
                request.PhoneNumber is { Length: > 0 } phone && !Regex.IsMatch(phone, @"^\+?[0-9]{9,15}$", RegexOptions.CultureInvariant))
                return Invalid("INVALID_PROFILE", "Name, date of birth, locale or phone number is invalid.");
            var id = UserId(principal);
            var profile = await db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == id, ct);
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (profile is null || user is null) return Missing();
            if (request.Email is not null && !string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
                return Invalid("EMAIL_IMMUTABLE", "Email cannot be changed through the profile.");
            var locked = await IdentityLockedAsync(db, profile, ct);
            if (locked && (profile.FullName != name || request.BirthDate is { } date && date != profile.BirthDate))
                return Conflict("IDENTITY_FIELDS_LOCKED", "Name and birth date cannot be changed after verification.");
            var before = HashProfile(profile, user.PhoneNumber);
            var changed = new List<string>();
            if (profile.FullName != name) { profile.FullName = name; profile.NormalizedFullName = name.Normalize(NormalizationForm.FormKC).ToUpperInvariant(); changed.Add("full_name"); }
            if (request.BirthDate is { } birthDate && birthDate != profile.BirthDate) { profile.BirthDate = birthDate; changed.Add("birth_date"); }
            if (profile.Locale != locale) { profile.Locale = locale; changed.Add("locale"); }
            if (profile.EmailNotifications != request.EmailNotifications) { profile.EmailNotifications = request.EmailNotifications; changed.Add("email_notifications"); }
            var phoneValue = string.IsNullOrEmpty(request.PhoneNumber) ? null : request.PhoneNumber;
            if (request.PhoneNumber is not null && phoneValue != user.PhoneNumber)
            {
                user.PhoneNumber = phoneValue;
                user.PhoneNumberConfirmed = false;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                changed.Add("phone_number");
            }
            if (changed.Count > 0)
            {
                profile.UpdatedAt = DateTime.UtcNow;
                db.AuditLogs.Add(Audit(principal, "PROFILE_UPDATED", "user_profiles", id, changed.ToArray(), before, HashProfile(profile, user.PhoneNumber)));
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(ToProfile(profile, user.Email, user.PhoneNumber, locked));
        }, ct);

    private static Task<bool> IdentityLockedAsync(ApplicationDbContext db, UserProfile profile, CancellationToken ct)
        => profile.CitizenIdVerifiedAt is not null ? Task.FromResult(true) : db.EligibilityEvidence
            .AnyAsync(x => x.UserId == profile.UserId && (x.Status == "VERIFIED" || x.Status == "REVOKED" ||
                x.Decisions.Any(d => d.Status == "VERIFIED" || d.Status == "REVOKED")), ct);

    private static async Task<IResult> GetEligibilityAsync(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
        => Results.Ok((await Requests(db).Where(x => x.UserId == UserId(principal)).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(ToResponse));

    private static async Task<IResult> GetOwnEligibilityAsync(Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var item = await Requests(db).SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId(principal), ct);
        return item is null ? Missing() : Results.Ok(ToResponse(item));
    }

    private static Task<IResult> SubmitEligibilityAsync(EligibilityRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
        => ForUserAsync(db, UserId(principal), async () =>
        {
            if (request.Category is null || !Categories.Contains(request.Category) ||
                request.ValidFrom is not null && request.ValidTo < request.ValidFrom || request.ValidTo is { } end && end < Today)
                return Invalid("INVALID_ELIGIBILITY", "Category must be STUDENT or ELDERLY and document dates must be valid.");
            if (!await db.UserProfiles.AnyAsync(x => x.UserId == UserId(principal), ct)) return Missing();
            if (await db.EligibilityEvidence.AnyAsync(x => x.UserId == UserId(principal) && x.Category == request.Category &&
                (x.Status == "PENDING" || x.Status == "NEEDS_INFO"), ct))
                return Conflict("ELIGIBILITY_ALREADY_PENDING", "An application for this category is already being reviewed.");
            var item = new EligibilityEvidence { UserId = UserId(principal), Category = request.Category, ValidFrom = request.ValidFrom, ValidTo = request.ValidTo };
            db.EligibilityEvidence.Add(item);
            db.AuditLogs.Add(Audit(principal, "ELIGIBILITY_SUBMITTED", EntityType, item.Id.ToString(), ["category", "valid_from", "valid_to"], null, HashEvidence(item)));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/me/eligibility/{item.Id}", ToResponse(item));
        }, ct);

    private static async Task<IResult> UploadEvidenceAsync(Guid id, Guid? replaceEvidenceId, HttpRequest request, ClaimsPrincipal principal, ApplicationDbContext db, IObjectStorage storage, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!request.HasFormContentType) return Invalid("MULTIPART_REQUIRED", "Use multipart/form-data with file or files fields.");
        if (request.ContentLength > MaxEvidenceFiles * MaxEvidenceBytes + 65536) return TooLarge();
        var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = MaxEvidenceFiles * MaxEvidenceBytes + 65536;
        IFormCollection form;
        try { form = await request.ReadFormAsync(ct); }
        catch (InvalidDataException) { return Invalid("INVALID_EVIDENCE_FILE", "Invalid or oversized multipart upload."); }
        if (form.Files.Count is < 1 or > MaxEvidenceFiles) return Invalid("INVALID_EVIDENCE_FILE", "Upload one to three files.");
        var uploads = new List<(byte[] Bytes, string Type, string Extension, string Name)>();
        foreach (var file in form.Files)
        {
            if (file.Length > MaxEvidenceBytes) return TooLarge();
            if (file.Length <= 0 || !AllowedTypes.Contains(file.ContentType)) return Invalid("INVALID_EVIDENCE_FILE", "Only JPG, PNG and PDF files are supported.");
            var validated = await ReadAndValidateAsync(file, ct);
            if (validated is null) return Invalid("FILE_CONTENT_MISMATCH", "File content does not match the declared type.");
            var safeName = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 200 || safeName.Any(char.IsControl)) return Invalid("INVALID_FILE_NAME", "File name is invalid.");
            var extension = Path.GetExtension(safeName).ToLowerInvariant();
            if (extension != validated.Value.Extension && !(validated.Value.Extension == ".jpg" && extension == ".jpeg"))
                return Invalid("FILE_CONTENT_MISMATCH", "File extension does not match the document format.");
            uploads.Add((validated.Value.Bytes, validated.Value.Type, validated.Value.Extension, safeName));
        }
        var storedKeys = new List<string>();
        // Stable IDs make a database transient retry overwrite the same private objects.
        var attachmentIds = uploads.Select(_ => Guid.NewGuid()).ToArray();
        try
        {
            var result = await ForUserAsync(db, UserId(principal), async () =>
            {
                var item = await db.EligibilityEvidence.Include(x => x.Attachments).Include(x => x.Decisions)
                    .SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId(principal), ct);
                if (item is null) return Missing();
                if (attachmentIds.All(attachmentId => item.Attachments.Any(a => a.Id == attachmentId)))
                    return Results.Ok(ToResponse(item));
                if (item.Status is not ("PENDING" or "NEEDS_INFO")) return Conflict("EVIDENCE_REQUEST_CLOSED", "This request cannot accept evidence.");
                var active = item.Attachments.Where(a => a.ReplacedAt == null).ToList();
                var replaced = replaceEvidenceId is { } replacementId ? active.SingleOrDefault(a => a.Id == replacementId) : null;
                if (replaceEvidenceId is not null && (replaced is null || uploads.Count != 1))
                    return Invalid("INVALID_EVIDENCE_REPLACEMENT", "Replace one current attachment with one new file.");
                if (active.Count + uploads.Count - (replaced is null ? 0 : 1) > MaxEvidenceFiles)
                    return Conflict("TOO_MANY_EVIDENCE_FILES", "Each request accepts at most three current files.");
                var before = HashEvidence(item);
                if (replaced is not null)
                {
                    replaced.ReplacedAt = DateTime.UtcNow;
                    // Release the unique current slot before inserting its new version.
                    // Both operations remain in the same transaction.
                    await db.SaveChangesAsync(ct);
                    active.Remove(replaced);
                }
                for (var uploadIndex = 0; uploadIndex < uploads.Count; uploadIndex++)
                {
                    var upload = uploads[uploadIndex];
                    var attachment = new EligibilityAttachment
                    {
                        Id = attachmentIds[uploadIndex], RequestId = id, Slot = Enumerable.Range(1, 3).First(slot => active.All(a => a.Slot != slot)), FileName = upload.Name,
                        ContentType = upload.Type, Size = upload.Bytes.Length,
                        Sha256 = Convert.ToHexStringLower(SHA256.HashData(upload.Bytes)),
                    };
                    attachment.ObjectKey = $"eligibility/{id:N}/{attachment.Id:N}{upload.Extension}";
                    storedKeys.Add(attachment.ObjectKey);
                    using var content = new MemoryStream(upload.Bytes, writable: false);
                    await storage.PutAsync(attachment.ObjectKey, content, upload.Type, ct);
                    item.Attachments.Add(attachment);
                    active.Add(attachment);
                    db.Set<EligibilityAttachment>().Add(attachment);
                }
                if (item.Status == "NEEDS_INFO")
                {
                    item.Status = "PENDING"; item.DecidedAt = null; item.DecidedByUserId = null; item.DecisionReason = null;
                }
                item.UpdatedAt = DateTime.UtcNow;
                db.AuditLogs.Add(Audit(principal, "ELIGIBILITY_EVIDENCE_UPLOADED", EntityType, id.ToString(), ["attachments", "status"], before, HashEvidence(item)));
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(item));
            }, ct);
            // A retry can return a conflict after another review closed the request.
            // Compensate earlier rolled-back attempts on every outcome, not just exceptions.
            await CleanupUploadsAsync();
            return result;
        }
        catch
        {
            await CleanupUploadsAsync();
            throw;
        }

        async Task CleanupUploadsAsync()
        {
            foreach (var key in storedKeys.Distinct(StringComparer.Ordinal))
            {
                // A lost commit acknowledgement may have persisted the reference.
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    if (!await db.Set<EligibilityAttachment>().AsNoTracking().AnyAsync(x => x.ObjectKey == key, timeout.Token))
                        await storage.DeleteAsync(key, timeout.Token);
                }
                catch { loggers.CreateLogger("ProfileEvidence").LogWarning("Evidence cleanup requires a retry; no document data is logged."); }
            }
        }
    }

    private static Task<IResult> DownloadOwnEvidenceAsync(Guid id, Guid? evidenceId, ClaimsPrincipal principal, ApplicationDbContext db, IObjectStorage storage, CancellationToken ct)
        => DownloadAsync(id, evidenceId, true, principal, db, storage, ct);

    private static Task<IResult> DownloadReviewEvidenceAsync(Guid id, Guid? evidenceId, ClaimsPrincipal principal, ApplicationDbContext db, IObjectStorage storage, CancellationToken ct)
        => DownloadAsync(id, evidenceId, false, principal, db, storage, ct);

    private static async Task<IResult> DownloadAsync(Guid id, Guid? evidenceId, bool ownerOnly, ClaimsPrincipal principal, ApplicationDbContext db, IObjectStorage storage, CancellationToken ct)
    {
        var item = await Requests(db).SingleOrDefaultAsync(x => x.Id == id && (!ownerOnly || x.UserId == UserId(principal)), ct);
        if (item is null) return Missing();
        var evidence = item.Attachments.Where(x => evidenceId is null ? x.ReplacedAt == null : x.Id == evidenceId).OrderByDescending(x => x.UploadedAt).FirstOrDefault();
        if (evidence?.ObjectKey is null) return Missing();
        var stream = await storage.GetAsync(evidence.ObjectKey, ct);
        try
        {
            db.AuditLogs.Add(Audit(principal, "ELIGIBILITY_EVIDENCE_DOWNLOADED", EntityType, id.ToString(), [], null, HashEvidence(item)));
            await db.SaveChangesAsync(ct);
        }
        catch { await stream.DisposeAsync(); throw; }
        return Results.File(stream, evidence.ContentType, evidence.FileName);
    }

    private static async Task<IResult> ListReviewQueueAsync(string? status, int? page, int? pageSize, ApplicationDbContext db, CancellationToken ct)
    {
        var filter = status?.Trim().ToUpperInvariant();
        if (filter is not (null or "PENDING" or "NEEDS_INFO" or "VERIFIED" or "REJECTED" or "REVOKED" or "EXPIRED") || page is < 1 || pageSize is < 1 or > 100)
            return Invalid("INVALID_REVIEW_FILTER", "Invalid status or pagination.");
        var query = Requests(db).Where(x => filter == null ? x.Status == "PENDING" || x.Status == "NEEDS_INFO" : x.Status == filter);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(((page ?? 1) - 1) * (pageSize ?? 20)).Take(pageSize ?? 20).ToListAsync(ct);
        return Results.Ok(new { total, page = page ?? 1, pageSize = pageSize ?? 20, items = items.Select(ToResponse) });
    }

    private static async Task<IResult> GetReviewAsync(Guid id, ApplicationDbContext db, CancellationToken ct)
    {
        var item = await Requests(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        return item is null ? Missing() : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> DecideEligibilityAsync(Guid id, DecisionRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var userId = await db.EligibilityEvidence.Where(x => x.Id == id).Select(x => x.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) return Missing();
        return await ForUserAsync(db, userId, async () =>
        {
            var decision = request.Decision?.Trim().ToUpperInvariant();
            if (decision is not ("VERIFIED" or "REJECTED" or "REVOKED" or "NEEDS_INFO") || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
                return Invalid("INVALID_DECISION", "A supported decision and reason of at most 1000 characters are required.");
            var item = await db.EligibilityEvidence.Include(x => x.Attachments).Include(x => x.Decisions).SingleAsync(x => x.Id == id, ct);
            var validTransition = item.Status == "PENDING" && decision is ("VERIFIED" or "REJECTED" or "NEEDS_INFO") ||
                item.Status == "VERIFIED" && decision is ("REVOKED" or "REJECTED");
            if (!validTransition) return Conflict("INVALID_ELIGIBILITY_TRANSITION", "This decision is not allowed in the current state.");
            var start = request.ValidFrom ?? Today;
            var end = request.ValidTo ?? item.ValidTo;
            if (decision == "VERIFIED")
            {
                if (item.UserId == UserId(principal)) return Conflict("SELF_REVIEW_NOT_ALLOWED", "An administrator cannot approve their own concession request.");
                if (!item.Attachments.Any(x => x.ObjectKey != null && x.ReplacedAt == null)) return Conflict("EVIDENCE_REQUIRED", "Upload supporting evidence before approval.");
                if (end is null || start < Today || end < start || end > Today.AddMonths(12) ||
                    item.ValidFrom is { } docStart && start < docStart || item.ValidTo is { } docExpiry && end > docExpiry)
                    return Invalid("INVALID_ELIGIBILITY_VALIDITY", "Approval dates must be valid, within twelve months, and no later than document expiry.");
                if (!Categories.Contains(item.Category)) return Invalid("INVALID_ELIGIBILITY", "Unsupported concession category.");
                if (item.Category == "ELDERLY")
                {
                    var birthDate = await db.UserProfiles.Where(x => x.UserId == item.UserId).Select(x => x.BirthDate).SingleAsync(ct);
                    if (request.DocumentBirthDate is null || request.DocumentBirthDate != birthDate)
                        return Invalid("BIRTH_DATE_MISMATCH", "Confirm the document birth date matches the passenger profile. Age at travel/payment is checked separately.");
                }
            }
            var before = HashEvidence(item);
            // ST-008 represents revocation as REJECTED; retain the action in history.
            item.Status = decision == "REVOKED" ? "REJECTED" : decision;
            item.DecisionReason = request.Reason.Trim();
            item.DecidedByUserId = UserId(principal);
            item.DecidedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            if (decision == "VERIFIED") { item.ValidFrom = start; item.ValidTo = end; }
            var history = new EligibilityDecision { RequestId = id, Status = decision, Reason = item.DecisionReason,
                ActorUserId = item.DecidedByUserId, OccurredAt = item.DecidedAt.Value, ValidFrom = item.ValidFrom, ValidTo = item.ValidTo };
            item.Decisions.Add(history);
            db.Set<EligibilityDecision>().Add(history);
            db.AuditLogs.Add(Audit(principal, "ELIGIBILITY_" + decision, EntityType, id.ToString(), ["status", "decision_reason", "valid_from", "valid_to"], before, HashEvidence(item), item.DecisionReason));
            await db.SaveChangesAsync(ct);
            // Durable in-app and mock-email outbox rows share the decision transaction.
            var payload = JsonSerializer.Serialize(new { requestId = id, status = decision });
            foreach (var channel in new[] { "IN_APP", "EMAIL_MOCK" })
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO metro.notifications (recipient_user_id, notification_type, channel, template_code, content_reference, mandatory) VALUES ({userId}, 'ELIGIBILITY_DECISION', {channel}, 'ELIGIBILITY_DECISION', CAST({payload} AS jsonb), true)", ct);
            return Results.Ok(ToResponse(item));
        }, ct);
    }

    private static async Task<IResult> SetEvidenceHoldAsync(Guid id, EvidenceHoldRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000) return Invalid("REASON_REQUIRED", "A reason is required.");
        var userId = await db.EligibilityEvidence.Where(x => x.Id == id).Select(x => x.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) return Missing();
        return await ForUserAsync(db, userId, async () =>
        {
            var item = await db.EligibilityEvidence.SingleAsync(x => x.Id == id, ct);
            var before = HashEvidence(item);
            item.EvidenceHold = request.Hold; item.UpdatedAt = DateTime.UtcNow;
            db.AuditLogs.Add(Audit(principal, "ELIGIBILITY_EVIDENCE_HOLD", EntityType, id.ToString(), ["evidence_hold"], before, HashEvidence(item), request.Reason.Trim()));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { item.Id, item.EvidenceHold });
        }, ct);
    }

    private static IQueryable<EligibilityEvidence> Requests(ApplicationDbContext db) => db.EligibilityEvidence.AsNoTracking().Include(x => x.Attachments).Include(x => x.Decisions).AsSplitQuery();

    private static Task<IResult> ForUserAsync(ApplicationDbContext db, string userId, Func<Task<IResult>> action, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Shared by profile edits, reviews, uploads and the lifecycle worker.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId}, 4))", ct);
            var result = await action();
            await transaction.CommitAsync(ct);
            return result;
        });

    private static async Task<(byte[] Bytes, string Type, string Extension)?> ReadAndValidateAsync(IFormFile file, CancellationToken ct)
    {
        await using var input = file.OpenReadStream();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > MaxEvidenceBytes) return null;
            output.Write(buffer, 0, read);
        }
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
    private static EvidenceResponse ToResponse(EligibilityEvidence x)
    {
        var latest = x.Attachments.OrderByDescending(a => a.UploadedAt).FirstOrDefault();
        var status = x.Status == "VERIFIED" && x.ValidTo < Today ? "EXPIRED" : x.Status;
        return new(x.Id, x.Category, status, x.ValidFrom, x.ValidTo, latest?.FileName ?? x.EvidenceFileName,
            latest?.ContentType ?? x.EvidenceContentType, latest?.Size ?? x.EvidenceSize, x.DecisionReason, x.DecidedAt, x.CreatedAt, x.UpdatedAt,
            x.UserId, x.Attachments.OrderBy(a => a.Slot).ThenBy(a => a.UploadedAt).Select(a => new AttachmentResponse(a.Id, a.FileName, a.ContentType, a.Size, a.UploadedAt, a.PurgedAt, a.ReplacedAt)).ToArray(),
            x.Decisions.OrderBy(d => d.OccurredAt).Select(d => new DecisionResponse(d.Id, d.Status, d.Reason, d.ActorUserId, d.OccurredAt, d.ValidFrom, d.ValidTo)).ToArray());
    }
    private static ProfileResponse ToProfile(UserProfile x, string? email, string? phone, bool locked) => new(x.FullName, x.BirthDate, x.Locale, x.EmailNotifications,
        x.CitizenIdLast4, x.BankCode, x.BankAccountLast4, x.BankHolderName, x.Status, x.UpdatedAt, email, phone, locked);
    private static string HashProfile(UserProfile x, string? phone) => Digest(JsonSerializer.Serialize(new { x.FullName, x.BirthDate, x.Locale, x.EmailNotifications, PhoneNumber = phone }));
    private static string HashEvidence(EligibilityEvidence x) => Digest(JsonSerializer.Serialize(new { x.Category, x.Status, x.ValidFrom, x.ValidTo, x.DecisionReason,
        x.DecidedByUserId, x.DecidedAt, x.EvidenceHold, Attachments = x.Attachments.OrderBy(a => a.Id).Select(a => new { a.Id, a.Sha256, a.PurgedAt, a.ReplacedAt }) }));
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static AuditLog Audit(ClaimsPrincipal principal, string action, string type, string id, string[] fields, string? before, string after, string? reason = null) => new()
    {
        ActorUserId = UserId(principal), Action = action, EntityType = type, EntityId = id, ChangedFields = fields,
        BeforeHash = before, AfterHash = after, Reason = reason,
    };
    private static IResult Invalid(string code, string detail) => Results.Problem(statusCode: 400, title: "Invalid request", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
    private static IResult Conflict(string code, string detail) => Results.Problem(statusCode: 409, title: "Request conflict", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
    private static IResult Missing() => Results.Problem(statusCode: 404, title: "Profile or evidence not found", extensions: new Dictionary<string, object?> { ["code"] = "PROFILE_RESOURCE_NOT_FOUND" });
    private static IResult TooLarge() => Results.Problem(statusCode: 413, title: "Evidence exceeds 5 MiB", extensions: new Dictionary<string, object?> { ["code"] = "EVIDENCE_TOO_LARGE" });

    public sealed record ProfileResponse(string FullName, DateOnly BirthDate, string Locale, bool EmailNotifications, string? CitizenIdLast4, string? BankCode, string? BankAccountLast4, string? BankHolderName, string Status, DateTime UpdatedAt, string? Email, string? PhoneNumber, bool IdentityFieldsLocked);
    public sealed record ProfileUpdateRequest(string? FullName, string? Locale, bool EmailNotifications, DateOnly? BirthDate = null, string? PhoneNumber = null, string? Email = null);
    public sealed record EligibilityRequest(string? Category, DateOnly? ValidFrom, DateOnly? ValidTo);
    public sealed record DecisionRequest(string? Decision, string? Reason, DateOnly? ValidFrom = null, DateOnly? ValidTo = null, DateOnly? DocumentBirthDate = null);
    public sealed record EvidenceHoldRequest(bool Hold, string? Reason);
    public sealed record AttachmentResponse(Guid Id, string FileName, string ContentType, long Size, DateTime UploadedAt, DateTime? PurgedAt, DateTime? ReplacedAt);
    public sealed record DecisionResponse(Guid Id, string Status, string Reason, string? ActorUserId, DateTime OccurredAt, DateOnly? ValidFrom, DateOnly? ValidTo);
    public sealed record EvidenceResponse(Guid Id, string Category, string Status, DateOnly? ValidFrom, DateOnly? ValidTo, string? FileName, string? ContentType, long? Size, string? DecisionReason, DateTime? DecidedAt, DateTime CreatedAt, DateTime UpdatedAt, string UserId, AttachmentResponse[] Attachments, DecisionResponse[] Decisions);
}
