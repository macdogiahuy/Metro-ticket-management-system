using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Storage;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Identity;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class ProfileEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Profile_requires_authentication_and_update_records_only_changed_fields_and_hashes()
    {
        using var app = CreateApp(new MemoryObjectStorage());
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/me")).StatusCode);
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;

        using (var profile = await ReadJsonAsync(await client.GetAsync("/api/v1/me"), HttpStatusCode.OK))
        {
            Assert.Equal("Profile Test Passenger", profile.RootElement.GetProperty("fullName").GetString());
            Assert.False(profile.RootElement.TryGetProperty("citizenIdCiphertext", out _));
        }
        var update = new { fullName = "  Updated Profile Passenger  ", locale = " en-US ", emailNotifications = false };
        using (var profile = await ReadJsonAsync(await client.PutAsJsonAsync("/api/v1/me", update), HttpStatusCode.OK))
        {
            Assert.Equal("Updated Profile Passenger", profile.RootElement.GetProperty("fullName").GetString());
            Assert.Equal("en-US", profile.RootElement.GetProperty("locale").GetString());
            Assert.False(profile.RootElement.GetProperty("emailNotifications").GetBoolean());
        }
        // Replaying normalized values is a no-op and must not create another audit record.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/me", update)).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == owner.UserId);
        Assert.Equal("UPDATED PROFILE PASSENGER", saved.NormalizedFullName);
        var audit = Assert.Single(await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(x => x.ActorUserId == owner.UserId && x.Action == "PROFILE_UPDATED").ToListAsync());
        Assert.Equal(new[] { "email_notifications", "full_name", "locale" }, audit.ChangedFields.Order().ToArray());
        Assert.Matches("^[0-9a-f]{64}$", audit.BeforeHash!);
        Assert.Matches("^[0-9a-f]{64}$", audit.AfterHash!);
        Assert.NotEqual(audit.BeforeHash, audit.AfterHash);
        Assert.Null(audit.Reason);
        Assert.DoesNotContain(saved.FullName, audit.BeforeHash + audit.AfterHash);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Invalid_profile_and_eligibility_inputs_return_400_without_persisting_changes()
    {
        using var app = CreateApp(new MemoryObjectStorage());
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/me", new { fullName = " ", locale = "vi-VN", emailNotifications = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/me/eligibility", new { category = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/me/eligibility", new { category = "UNKNOWN" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/me/eligibility", new { category = "DISABILITY" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/me/eligibility", new { category = "STUDENT", validFrom = "2027-12-31", validTo = "2027-01-01" })).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("Profile Test Passenger", (await db.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == owner.UserId)).FullName);
        Assert.Empty(await db.EligibilityEvidence.AsNoTracking().Where(x => x.UserId == owner.UserId).ToListAsync());
        _ = await SubmitAsync(client);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/me/eligibility", new { category = "STUDENT" })).StatusCode);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Evidence_is_private_multiple_attachments_are_preserved_and_storage_keys_are_not_exposed()
    {
        var storage = new MemoryObjectStorage();
        using var app = CreateApp(storage);
        var owner = await CreatePassengerAsync(app);
        var outsider = await CreatePassengerAsync(app);
        using var client = owner.Client;
        using var other = outsider.Client;
        var id = await SubmitAsync(client);
        var first = Pdf("first private evidence");
        Guid firstAttachmentId;
        using (var response = await UploadAsync(client, id, first))
        using (var payload = await ReadJsonAsync(response, HttpStatusCode.OK))
        {
            var attachment = Assert.Single(payload.RootElement.GetProperty("attachments").EnumerateArray());
            firstAttachmentId = attachment.GetProperty("id").GetGuid();
            Assert.Equal(first.Length, attachment.GetProperty("size").GetInt64());
            Assert.False(payload.RootElement.TryGetProperty("evidenceObjectKey", out _));
            Assert.False(payload.RootElement.TryGetProperty("evidenceSha256", out _));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/me/eligibility/{id}/evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(other, id, Pdf("foreign upload"))).StatusCode);
        using (var list = await ReadJsonAsync(await other.GetAsync("/api/v1/me/eligibility"), HttpStatusCode.OK))
            Assert.Empty(list.RootElement.EnumerateArray());
        using (var download = await client.GetAsync($"/api/v1/me/eligibility/{id}/evidence"))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(first, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        }
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("second document"))).StatusCode);
        Assert.Equal(2, storage.Objects.Count);
        Assert.Empty(storage.DeletedKeys);
        using (var oldAttachment = await client.GetAsync($"/api/v1/me/eligibility/{id}/evidence?evidenceId={firstAttachmentId}"))
        {
            Assert.Equal(HttpStatusCode.OK, oldAttachment.StatusCode);
            Assert.Equal(first, await oldAttachment.Content.ReadAsByteArrayAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("third document"))).StatusCode);
        using (var excess = await UploadAsync(client, id, Pdf("fourth document")))
            Assert.Contains(excess.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        Assert.Equal(3, storage.Objects.Count);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(x => x.EntityId == id.ToString() && x.Action == "ELIGIBILITY_EVIDENCE_UPLOADED").ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.All(audits, x => Assert.Matches("^[0-9a-f]{64}$", x.AfterHash!));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Invalid_or_oversized_uploads_do_not_create_objects_or_modify_evidence()
    {
        var storage = new MemoryObjectStorage();
        using var app = CreateApp(storage);
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        var id = await SubmitAsync(client);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsJsonAsync($"/api/v1/me/eligibility/{id}/evidence", new { file = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, id, Encoding.UTF8.GetBytes("not pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, id, Pdf("valid bytes wrong media type"), "text/plain")).StatusCode);
        using (var tooLarge = await UploadAsync(client, id, new byte[5 * 1024 * 1024 + 1]))
            Assert.Contains(tooLarge.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        using (var mixedBatch = new MultipartFormDataContent())
        {
            var good = new ByteArrayContent(Pdf("valid first part"));
            good.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            mixedBatch.Add(good, "files", "first.pdf");
            var bad = new ByteArrayContent(Encoding.UTF8.GetBytes("spoofed second PDF"));
            bad.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            mixedBatch.Add(bad, "files", "second.pdf");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/v1/me/eligibility/{id}/evidence", mixedBatch)).StatusCode);
        }
        Assert.Empty(storage.Objects);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null((await db.EligibilityEvidence.AsNoTracking().SingleAsync(x => x.Id == id)).EvidenceObjectKey);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Concurrent_upload_batches_cannot_exceed_three_attachments_or_leave_orphan_objects()
    {
        var storage = new MemoryObjectStorage();
        using var app = CreateApp(storage);
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        var id = await SubmitAsync(client);
        async Task<HttpResponseMessage> UploadPairAsync(string label)
        {
            using var body = new MultipartFormDataContent();
            for (var i = 0; i < 2; i++)
            {
                var file = new ByteArrayContent(Pdf($"{label} document {i}"));
                file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                body.Add(file, "files", $"{label}-{i}.pdf");
            }
            return await client.PostAsync($"/api/v1/me/eligibility/{id}/evidence", body);
        }
        var responses = await Task.WhenAll(UploadPairAsync("first"), UploadPairAsync("second"));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(2, storage.Objects.Count);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.Set<EligibilityAttachment>().AsNoTracking().CountAsync(x => x.RequestId == id));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_storage_batch_compensates_uploaded_objects_and_rolls_back_sql()
    {
        var storage = new MemoryObjectStorage { FailOnPut = 2 };
        using var app = CreateApp(storage);
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        var id = await SubmitAsync(client);
        using (var body = new MultipartFormDataContent())
        {
            for (var i = 0; i < 2; i++)
            {
                var file = new ByteArrayContent(Pdf($"private document {i}"));
                file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                body.Add(file, "files", $"document-{i}.pdf");
            }
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsync($"/api/v1/me/eligibility/{id}/evidence", body)).StatusCode);
        }
        Assert.Empty(storage.Objects);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.Set<EligibilityAttachment>().AsNoTracking().Where(x => x.RequestId == id).ToListAsync());
            Assert.Empty(await db.AuditLogs.IgnoreQueryFilters().Where(x => x.EntityId == id.ToString() && x.Action == "ELIGIBILITY_EVIDENCE_UPLOADED").ToListAsync());
        }
        // The failed batch must leave the request able to receive a later retry.
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("retry after failure"))).StatusCode);
        Assert.Single(storage.Objects);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Reviewer_requires_evidence_reason_and_valid_transition_and_decisions_are_audited()
    {
        using var app = CreateApp(new MemoryObjectStorage());
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        using var staff = await CreateReviewerAsync(app);
        using var stationStaff = await CreateReviewerAsync(app, "StationStaff");
        var id = await SubmitAsync(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(client, id, "VERIFIED", "Passenger cannot approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(stationStaff, id, "VERIFIED", "Station staff cannot approve")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(staff, id, "VERIFIED", " ")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(staff, id, "VERIFIED", "Missing evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("student document"))).StatusCode);
        using (var result = await ReadJsonAsync(await DecideAsync(staff, id, "VERIFIED", "Document reviewed"), HttpStatusCode.OK))
            Assert.Equal("VERIFIED", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(client, id, Pdf("closed request"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(staff, id, "VERIFIED", "Duplicate approval")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(staff, id, "REVOKED", "Document no longer valid")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(staff, id, "REVOKED", "Duplicate revocation")).StatusCode);

        var rejectedId = await SubmitAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(staff, rejectedId, "REJECTED", "Insufficient proof")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(staff, rejectedId, "REVOKED", "Invalid transition")).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(x => x.EntityId == id.ToString() && (x.Action == "ELIGIBILITY_VERIFIED" || x.Action == "ELIGIBILITY_REVOKED")).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, x => { Assert.NotNull(x.ActorUserId); Assert.False(string.IsNullOrWhiteSpace(x.Reason)); Assert.NotEqual(x.BeforeHash, x.AfterHash); });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Verification_validity_cannot_exceed_document_expiry_or_twelve_months()
    {
        using var app = CreateApp(new MemoryObjectStorage());
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        using var admin = await CreateReviewerAsync(app);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var id = await SubmitAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("student proof"))).StatusCode);
        using (var beyondDocument = await admin.PostAsJsonAsync($"/api/v1/eligibility/{id}/decision", new
        {
            decision = "VERIFIED", reason = "Cannot exceed document expiry", validFrom = today, validTo = today.AddMonths(7)
        }))
            Assert.Equal(HttpStatusCode.BadRequest, beyondDocument.StatusCode);
        using (var beyondYear = await admin.PostAsJsonAsync($"/api/v1/eligibility/{id}/decision", new
        {
            decision = "VERIFIED", reason = "Cannot exceed twelve months", validFrom = today, validTo = today.AddMonths(13)
        }))
            Assert.Equal(HttpStatusCode.BadRequest, beyondYear.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, id, "VERIFIED", "Valid dates")).StatusCode);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Elderly_verification_requires_matching_document_birth_date()
    {
        using var app = CreateApp(new MemoryObjectStorage());
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        using var admin = await CreateReviewerAsync(app);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/me", new { fullName = "Profile Test Passenger", locale = "vi-VN", birthDate = "1950-01-01", emailNotifications = true })).StatusCode);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        using var submitted = await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/me/eligibility", new { category = "ELDERLY", validFrom = today, validTo = today.AddMonths(6) }), HttpStatusCode.Created);
        var id = submitted.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("elderly identity proof"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(admin, id, "VERIFIED", "Document birth date is required")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/eligibility/{id}/decision", new { decision = "VERIFIED", reason = "Mismatched birth date", documentBirthDate = "1951-01-01" })).StatusCode);
        using var accepted = await ReadJsonAsync(await admin.PostAsJsonAsync($"/api/v1/eligibility/{id}/decision", new { decision = "VERIFIED", reason = "Birth date matches", documentBirthDate = "1950-01-01" }), HttpStatusCode.OK);
        Assert.Equal("VERIFIED", accepted.RootElement.GetProperty("status").GetString());
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Legacy_revoked_verification_keeps_identity_fields_locked_without_verified_history()
    {
        using var app = CreateApp(new MemoryObjectStorage());
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        await using (var seed = app.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.EligibilityEvidence.Add(new EligibilityEvidence
            {
                UserId = owner.UserId, Category = "STUDENT", Status = "REVOKED",
                DecidedByUserId = owner.UserId, DecidedAt = DateTime.UtcNow, DecisionReason = "Legacy revocation before decision history existed"
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/v1/me", new { fullName = "Changed legacy identity", locale = "vi-VN", emailNotifications = true })).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var persisted = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("Profile Test Passenger", (await persisted.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == owner.UserId)).FullName);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_can_review_private_evidence_request_more_information_and_profile_identity_stays_locked_after_revocation()
    {
        using var app = CreateApp(new MemoryObjectStorage());
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        using var admin = await CreateReviewerAsync(app);
        var id = await SubmitAsync(client);
        using (var queue = await admin.GetAsync("/api/v1/eligibility?pageSize=100"))
        {
            Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
            Assert.Contains(id.ToString(), await queue.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/eligibility/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, id, "NEEDS_INFO", "Please add student proof")).StatusCode);
        var bytes = Pdf("student proof");
        using (var upload = await ReadJsonAsync(await UploadAsync(client, id, bytes), HttpStatusCode.OK))
            Assert.Equal("PENDING", upload.RootElement.GetProperty("status").GetString());
        using (var evidence = await admin.GetAsync($"/api/v1/eligibility/{id}/evidence"))
        {
            Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
            Assert.Equal(bytes, await evidence.Content.ReadAsByteArrayAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/eligibility/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, id, "VERIFIED", "Proof complete")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, id, "REVOKED", "Revoked later")).StatusCode);
        using (var lockedName = await client.PutAsJsonAsync("/api/v1/me", new { fullName = "New identity", locale = "vi-VN", emailNotifications = true }))
            Assert.Contains(lockedName.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        using (var lockedBirthDate = await client.PutAsJsonAsync("/api/v1/me", new { fullName = "Profile Test Passenger", birthDate = "2001-01-01", locale = "vi-VN", emailNotifications = true }))
            Assert.Contains(lockedBirthDate.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        using (var updated = await ReadJsonAsync(await client.PutAsJsonAsync("/api/v1/me", new { fullName = "Profile Test Passenger", locale = "en-US", phoneNumber = "+84912345678", emailNotifications = false }), HttpStatusCode.OK))
            Assert.Equal("+84912345678", updated.RootElement.GetProperty("phoneNumber").GetString());
        using (var detail = await ReadJsonAsync(await admin.GetAsync($"/api/v1/eligibility/{id}"), HttpStatusCode.OK))
            Assert.Equal(new[] { "NEEDS_INFO", "VERIFIED", "REVOKED" }, detail.RootElement.GetProperty("decisions").EnumerateArray().Select(x => x.GetProperty("status").GetString()).ToArray());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var channels = await db.Database.SqlQuery<string>($"SELECT channel::text AS \"Value\" FROM metro.notifications WHERE recipient_user_id = {owner.UserId} AND content_reference ->> 'requestId' = {id.ToString()}").ToListAsync();
        Assert.Equal(3, channels.Count(x => x == "IN_APP"));
        Assert.Equal(3, channels.Count(x => x == "EMAIL_MOCK"));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Needs_information_at_attachment_limit_allows_replacement_and_keeps_original_history()
    {
        var storage = new MemoryObjectStorage();
        using var app = CreateApp(storage);
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        using var admin = await CreateReviewerAsync(app);
        var id = await SubmitAsync(client);
        Guid originalId;
        using (var first = await ReadJsonAsync(await UploadAsync(client, id, Pdf("original proof")), HttpStatusCode.OK))
            originalId = Assert.Single(first.RootElement.GetProperty("attachments").EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("second proof"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("third proof"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, id, "NEEDS_INFO", "Replace unreadable original proof")).StatusCode);
        var corrected = Pdf("corrected readable proof");
        using (var replaced = await ReadJsonAsync(await UploadAsync(client, id, corrected, replaceEvidenceId: originalId), HttpStatusCode.OK))
        {
            Assert.Equal("PENDING", replaced.RootElement.GetProperty("status").GetString());
            var attachments = replaced.RootElement.GetProperty("attachments").EnumerateArray().ToArray();
            Assert.Equal(4, attachments.Length);
            Assert.Equal(3, attachments.Count(x => x.GetProperty("replacedAt").ValueKind == JsonValueKind.Null));
            Assert.NotEqual(JsonValueKind.Null, Assert.Single(attachments, x => x.GetProperty("id").GetGuid() == originalId).GetProperty("replacedAt").ValueKind);
        }
        Assert.Equal(4, storage.Objects.Count);
        using (var downloaded = await admin.GetAsync($"/api/v1/eligibility/{id}/evidence"))
            Assert.Equal(corrected, await downloaded.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, id, "VERIFIED", "Corrected evidence reviewed")).StatusCode);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Lifecycle_expires_verification_purges_after_retention_and_preserves_evidence_on_hold()
    {
        var storage = new MemoryObjectStorage();
        using var app = CreateApp(storage);
        var owner = await CreatePassengerAsync(app);
        var heldOwner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        using var heldClient = heldOwner.Client;
        using var admin = await CreateReviewerAsync(app);
        var id = await SubmitAsync(client);
        var heldId = await SubmitAsync(heldClient);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf("purge after retention"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(heldClient, heldId, Pdf("retain for dispute"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, id, "VERIFIED", "Proof valid")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, heldId, "VERIFIED", "Proof valid")).StatusCode);
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var request in await db.EligibilityEvidence.Where(x => x.Id == id || x.Id == heldId).ToListAsync())
            {
                request.ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(-70);
                request.ValidTo = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(-40);
                request.EvidenceHold = request.Id == heldId;
            }
            await db.SaveChangesAsync();
        }
        await using (var worker = app.Services.CreateAsyncScope())
            await worker.ServiceProvider.GetRequiredService<ProfileEligibilityLifecycle>().RunOnceAsync(DateTime.UtcNow);
        await using (var replay = app.Services.CreateAsyncScope())
            await replay.ServiceProvider.GetRequiredService<ProfileEligibilityLifecycle>().RunOnceAsync(DateTime.UtcNow);
        await using var scope = app.Services.CreateAsyncScope();
        var persisted = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var purged = await persisted.EligibilityEvidence.Include(x => x.Attachments).Include(x => x.Decisions).AsNoTracking().SingleAsync(x => x.Id == id);
        var held = await persisted.EligibilityEvidence.Include(x => x.Attachments).AsNoTracking().SingleAsync(x => x.Id == heldId);
        Assert.Equal("EXPIRED", purged.Status);
        Assert.Null(purged.EvidenceObjectKey);
        Assert.All(purged.Attachments, x => { Assert.Null(x.ObjectKey); Assert.NotNull(x.PurgedAt); });
        Assert.Contains(purged.Decisions, x => x.Status == "VERIFIED");
        Assert.Single(purged.Decisions, x => x.Status == "EXPIRED");
        Assert.Equal("EXPIRED", held.Status);
        Assert.All(held.Attachments, x => Assert.NotNull(x.ObjectKey));
        Assert.Single(storage.Objects);
        var audits = await persisted.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(x => x.EntityId == id.ToString() && x.Action == "ELIGIBILITY_EVIDENCE_PURGED").ToListAsync();
        Assert.Single(audits);
    }

    private WebApplicationFactory<Program> CreateApp(MemoryObjectStorage storage) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IObjectStorage>();
        services.AddSingleton<IObjectStorage>(storage);
    }));

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Lifecycle_save_or_commit_failure_keeps_evidence_downloadable_and_does_not_delete_storage()
    {
        foreach (var failOnSave in new[] { true, false })
        {
            var storage = new MemoryObjectStorage();
            using var app = CreateApp(storage);
            var owner = await CreatePassengerAsync(app);
            using var client = owner.Client;
            var id = await PrepareRetainedEvidenceAsync(app, client);
            IInterceptor fault = failOnSave ? new FailLifecycleSave() : new FailLifecycleCommit("ELIGIBILITY_EVIDENCE_PURGE_QUEUED");
            await using (var db = LifecycleDb(fault))
            {
                var lifecycle = new ProfileEligibilityLifecycle(db, storage, NullLogger<ProfileEligibilityLifecycle>.Instance);
                await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.RunOnceAsync(DateTime.UtcNow));
            }

            Assert.Empty(storage.DeletedKeys);
            using (var download = await client.GetAsync($"/api/v1/me/eligibility/{id}/evidence"))
            {
                Assert.Equal(HttpStatusCode.OK, download.StatusCode);
                Assert.Equal(Pdf("retained private evidence 0"), await download.Content.ReadAsByteArrayAsync());
            }
            await using (var db = LifecycleDb())
            {
                Assert.NotNull((await db.EligibilityEvidence.AsNoTracking().SingleAsync(x => x.Id == id)).EvidenceObjectKey);
                Assert.Equal(0, await PurgeJobCountAsync(db, id));
            }
            await RunLifecycleAsync(app);
            Assert.Empty(storage.Objects);
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Lifecycle_storage_failure_retries_committed_queue_without_logging_private_details()
    {
        const string privateMessage = "passenger-secret@example.invalid";
        var storage = new MemoryObjectStorage { FailOnDelete = 2, DeleteFailureMessage = privateMessage };
        using var app = CreateApp(storage);
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        var id = await PrepareRetainedEvidenceAsync(app, client, 2);
        var keys = storage.Objects.Keys.ToArray();
        storage.BeforeDelete = async _ =>
        {
            // Read through another connection: detachment and the cleanup intent must
            // already be committed before even the first storage deletion starts.
            await using var db = LifecycleDb();
            var request = await db.EligibilityEvidence.Include(x => x.Attachments).AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Null(request.EvidenceObjectKey);
            Assert.All(request.Attachments, x => Assert.Null(x.ObjectKey));
            Assert.Equal(1, await PurgeJobCountAsync(db, id));
        };
        await RunLifecycleAsync(app);
        storage.BeforeDelete = null;
        Assert.Single(storage.Objects);
        await using (var db = LifecycleDb())
        {
            Assert.Equal(1, await PurgeJobCountAsync(db, id));
            Assert.Empty(await db.AuditLogs.IgnoreQueryFilters().Where(x => x.EntityId == id.ToString() && x.Action == "ELIGIBILITY_EVIDENCE_PURGED").ToListAsync());
        }
        var messages = string.Join('\n', factory.TestLogs.Messages);
        Assert.Contains("evidence purge will retry after IOException", messages);
        Assert.DoesNotContain(privateMessage, messages);
        Assert.All(keys, key => Assert.DoesNotContain(key, messages));

        // Independent worker scopes also exercise SKIP LOCKED and idempotent replay
        // of an object that was deleted before the previous batch failed.
        await Task.WhenAll(RunLifecycleAsync(app), RunLifecycleAsync(app));
        await RunLifecycleAsync(app);
        Assert.Empty(storage.Objects);
        await using var verified = LifecycleDb();
        Assert.Equal(0, await PurgeJobCountAsync(verified, id));
        Assert.Single(await verified.AuditLogs.IgnoreQueryFilters().Where(x => x.EntityId == id.ToString() && x.Action == "ELIGIBILITY_EVIDENCE_PURGED").ToListAsync());
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Lifecycle_cleanup_commit_failure_preserves_queue_for_idempotent_retry()
    {
        var storage = new MemoryObjectStorage();
        using var app = CreateApp(storage);
        var owner = await CreatePassengerAsync(app);
        using var client = owner.Client;
        var id = await PrepareRetainedEvidenceAsync(app, client);
        await using (var db = LifecycleDb(new FailLifecycleCommit("ELIGIBILITY_EVIDENCE_PURGED")))
        {
            var lifecycle = new ProfileEligibilityLifecycle(db, storage, NullLogger<ProfileEligibilityLifecycle>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.RunOnceAsync(DateTime.UtcNow));
        }
        Assert.Empty(storage.Objects);
        await using (var db = LifecycleDb())
        {
            Assert.Equal(1, await PurgeJobCountAsync(db, id));
            Assert.Null((await db.EligibilityEvidence.AsNoTracking().SingleAsync(x => x.Id == id)).EvidenceObjectKey);
        }
        await RunLifecycleAsync(app);
        await using var verified = LifecycleDb();
        Assert.Equal(0, await PurgeJobCountAsync(verified, id));
        Assert.Single(await verified.AuditLogs.IgnoreQueryFilters().Where(x => x.EntityId == id.ToString() && x.Action == "ELIGIBILITY_EVIDENCE_PURGED").ToListAsync());
    }

    private static ApplicationDbContext LifecycleDb(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgreSqlTestConfiguration.LoadRequired().ConnectionString)
            .AddInterceptors(interceptors).Options);

    private static Task<int> PurgeJobCountAsync(ApplicationDbContext db, Guid id) => db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM metro.profile_eligibility_purge_queue WHERE request_id = {id}
        """).SingleAsync();

    private static async Task RunLifecycleAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProfileEligibilityLifecycle>().RunOnceAsync(DateTime.UtcNow);
    }

    private static async Task<Guid> PrepareRetainedEvidenceAsync(WebApplicationFactory<Program> app, HttpClient client, int files = 1)
    {
        var id = await SubmitAsync(client);
        for (var i = 0; i < files; i++)
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, id, Pdf($"retained private evidence {i}"))).StatusCode);
        using var admin = await CreateReviewerAsync(app);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, id, "REJECTED", "Retention regression fixture")).StatusCode);
        await using var db = LifecycleDb();
        var request = await db.EligibilityEvidence.Include(x => x.Attachments).SingleAsync(x => x.Id == id);
        request.DecidedAt = DateTime.UtcNow.AddDays(-31);
        // Exercise legacy 007 references alongside the attachments backfilled by 008.
        var first = request.Attachments[0];
        request.EvidenceObjectKey = first.ObjectKey;
        request.EvidenceFileName = first.FileName;
        request.EvidenceContentType = first.ContentType;
        request.EvidenceSize = first.Size;
        request.EvidenceSha256 = first.Sha256;
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class FailLifecycleSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => HasAudit(eventData.Context, "ELIGIBILITY_EVIDENCE_PURGE_QUEUED")
                ? throw new InvalidOperationException("Simulated lifecycle save failure.")
                : ValueTask.FromResult(result);
    }

    private sealed class FailLifecycleCommit(string action) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => HasAudit(eventData.Context, action)
                ? throw new InvalidOperationException("Simulated lifecycle commit failure.")
                : ValueTask.FromResult(result);
    }

    private static bool HasAudit(DbContext? db, string action) =>
        db?.ChangeTracker.Entries<AuditLog>().Any(x => x.Entity.Action == action) == true;

    private static async Task<(HttpClient Client, string UserId)> CreatePassengerAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var email = $"profile-{Guid.NewGuid():N}@example.invalid";
        var password = $"Metro!{Guid.NewGuid():N}Aa1";
        using var registration = await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, fullName = "Profile Test Passenger", birthDate = "2000-01-01" }), HttpStatusCode.Created);
        var userId = registration.RootElement.GetProperty("userId").GetString()!;
        var code = registration.RootElement.GetProperty("devVerificationCode").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code })).StatusCode);
        await AuthenticateAsync(client, email, password);
        return (client, userId);
    }

    private static async Task<HttpClient> CreateReviewerAsync(WebApplicationFactory<Program> app, string role = "Admin")
    {
        var email = $"reviewer-{Guid.NewGuid():N}@example.invalid";
        var password = $"Metro!{Guid.NewGuid():N}Aa1";
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = new IdentityUser { Id = Guid.NewGuid().ToString("N"), UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var client = app.CreateClient();
        await AuthenticateAsync(client, email, password);
        return client;
    }

    private static async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        using var payload = await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }), HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.RootElement.GetProperty("accessToken").GetString());
    }

    private static async Task<Guid> SubmitAsync(HttpClient client)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        using var result = await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/me/eligibility", new { category = "STUDENT", validFrom = today, validTo = today.AddMonths(6) }), HttpStatusCode.Created);
        return result.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid id, string decision, string reason) => client.PostAsJsonAsync($"/api/v1/eligibility/{id}/decision", new { decision, reason });
    private static byte[] Pdf(string text) => Encoding.UTF8.GetBytes("%PDF-1.7\n" + text + "\n%%EOF");
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid id, byte[] data, string contentType = "application/pdf", Guid? replaceEvidenceId = null)
    {
        using var body = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        body.Add(file, "file", "proof.pdf");
        var suffix = replaceEvidenceId.HasValue ? $"?replaceEvidenceId={replaceEvidenceId}" : string.Empty;
        return await client.PostAsync($"/api/v1/me/eligibility/{id}/evidence{suffix}", body);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class MemoryObjectStorage : IObjectStorage
    {
        public string Bucket => "test-evidence";
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
        public ConcurrentBag<string> DeletedKeys { get; } = [];
        public int? FailOnPut { get; init; }
        public int? FailOnDelete { get; init; }
        public string DeleteFailureMessage { get; init; } = "Simulated storage delete failure.";
        public Func<string, Task>? BeforeDelete { get; set; }
        private int _putCount;
        private int _deleteCount;
        public Task<bool> IsReachableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _putCount) == FailOnPut) throw new IOException("Simulated object storage failure.");
            using var output = new MemoryStream();
            await content.CopyToAsync(output, cancellationToken);
            Objects[key] = output.ToArray();
        }
        public Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(Objects[key], writable: false));
        public Task<Uri> CreateDownloadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Private evidence must be streamed through an authorized endpoint.");
        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            if (BeforeDelete is { } beforeDelete) await beforeDelete(key);
            if (Interlocked.Increment(ref _deleteCount) == FailOnDelete) throw new IOException(DeleteFailureMessage);
            Objects.TryRemove(key, out _);
            DeletedKeys.Add(key);
        }
    }
}
