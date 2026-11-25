using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Ticketing;
using Metro.Application.Booking;
using Metro.Application.Fare;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Ticketing;

public sealed class TicketChangeService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    TicketQrCodec qrCodec) : ITicketChangeService
{
    public async Task<TicketChangeResponse> CreateAsync(
        Guid ticketId, CreateTicketChangeRequest request, string userId, string? operatorCode, string idempotencyKey, CancellationToken ct)
    {
        BookingRules.ValidateKey(idempotencyKey);
        var reasonType = TicketChangeRules.NormalizeReasonType(request.ReasonType);
        var today = FareRules.ToVietnamDate(DateTime.UtcNow);
        TicketChangeRules.ValidateRequestedServiceDate(request.RequestedServiceDate, today);
        TicketChangeRules.ValidateStations(request.NewOriginStationId, request.NewDestinationStationId);

        var opId = await ResolveOperatorAsync(operatorCode, ticketId, ct);

        var ticket = await db.Tickets
            .Include(t => t.TicketProduct)
            .Include(t => t.OrderItem)
            .SingleOrDefaultAsync(t => t.OperatorId == opId && t.Id == ticketId, ct)
            ?? throw new TicketChangeException("TICKET_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket not found.");

        var hasOpenJourney = await db.Journeys
            .AnyAsync(j => j.OperatorId == opId && j.TicketId == ticket.Id && j.Status == JourneyStatus.Open, ct);

        TicketChangeRules.ValidateTicketEligibility(ticket.Status, ticket.FirstUsedAt, hasOpenJourney, ticket.ChangeCount);

        // Check if an active change request already exists for this ticket
        var existingRequest = await db.TicketChangeRequests
            .SingleOrDefaultAsync(r => r.OperatorId == opId && r.TicketId == ticket.Id &&
                (r.Status == TicketChangeRequestStatus.Requested || r.Status == TicketChangeRequestStatus.Approved), ct);

        if (existingRequest is not null)
        {
            if (existingRequest.RequestedByUserId == userId && existingRequest.RequestedServiceDate == request.RequestedServiceDate)
                return ToResponse(existingRequest);

            throw new TicketChangeException("ACTIVE_CHANGE_REQUEST_EXISTS", StatusCodes.Status409Conflict,
                "An active change request already exists for this ticket.");
        }

        // Check fare equality if route changes for single trip (BR-058)
        var newOrigin = request.NewOriginStationId ?? ticket.OriginStationId;
        var newDest = request.NewDestinationStationId ?? ticket.DestinationStationId;

        if (ticket.TicketProduct.ProductType == "SINGLE_TRIP" && (request.NewOriginStationId.HasValue || request.NewDestinationStationId.HasValue))
        {
            if (newOrigin.HasValue && newDest.HasValue)
            {
                var entry = await db.FareMatrixEntries.AsNoTracking().FirstOrDefaultAsync(e =>
                    e.OperatorId == opId &&
                    e.FarePolicyVersionId == ticket.FarePolicyVersionId &&
                    e.TicketProductId == ticket.TicketProductId &&
                    e.OriginStationId == newOrigin.Value &&
                    e.DestinationStationId == newDest.Value, ct);

                if (entry is null || entry.AmountMinor != ticket.OrderItem.UnitPriceMinor)
                {
                    throw new TicketChangeException("ROUTE_FARE_MISMATCH", StatusCodes.Status409Conflict,
                        "Disruption ticket exchange is only allowed for journeys with identical fare.");
                }
            }
        }

        // Check overlapping validity for passenger on the new service date (BR-005 / BR-058)
        if (ticket.HolderUserId is not null)
        {
            var (newStart, newEnd) = TicketRules.Validity(ticket.TicketProduct.ProductType, request.RequestedServiceDate, DateTimeOffset.UtcNow);
            var overlapping = await db.Tickets.AsNoTracking().AnyAsync(t =>
                t.OperatorId == opId &&
                t.Id != ticket.Id &&
                t.HolderUserId == ticket.HolderUserId &&
                (t.Status == "ACTIVE" || t.Status == "ISSUED") &&
                t.EffectiveFrom < newEnd && newStart < t.ExpiresAt, ct);

            if (overlapping)
                throw new TicketChangeException("OVERLAPPING_VALIDITY_CONFLICT", StatusCodes.Status409Conflict,
                    "Ticket change creates overlapping validity with another active ticket.");
        }

        var now = DateTimeOffset.UtcNow;
        var changeRequest = new TicketChangeRequest
        {
            Id = Guid.NewGuid(),
            OperatorId = opId,
            RequestCode = $"CHG-{Guid.NewGuid():N}"[..24].ToUpperInvariant(),
            TicketId = ticket.Id,
            OrderId = ticket.OrderId ?? ticket.OrderItem.OrderId,
            RequestedByUserId = userId,
            ReasonType = reasonType,
            IncidentId = request.IncidentId,
            OriginalServiceDate = ticket.ServiceDate,
            RequestedServiceDate = request.RequestedServiceDate,
            OriginalOriginStationId = ticket.OriginStationId,
            OriginalDestinationStationId = ticket.DestinationStationId,
            NewOriginStationId = newOrigin,
            NewDestinationStationId = newDest,
            OriginalPlannedTripId = ticket.OrderItem.PlannedTripId,
            NewPlannedTripId = request.NewPlannedTripId,
            Status = TicketChangeRequestStatus.Requested,
            DecisionReason = request.DecisionReason,
            ChangeSequence = (short)(ticket.ChangeCount + 1),
            CorrelationId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.TicketChangeRequests.Add(changeRequest);
        await db.SaveChangesAsync(ct);

        return ToResponse(changeRequest);
    }

    public async Task<TicketChangeResponse> ApproveAsync(
        Guid changeRequestId, ApproveTicketChangeRequest request, string staffUserId, string? operatorCode, string idempotencyKey, CancellationToken ct)
    {
        BookingRules.ValidateKey(idempotencyKey);
        var decisionReason = request.DecisionReason ?? "Approved disruption change";
        TicketChangeRules.ValidateApproval("REQUESTED", decisionReason);

        var opId = await db.TicketChangeRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == changeRequestId)
            .Select(r => (Guid?)r.OperatorId).SingleOrDefaultAsync(ct)
            ?? throw new TicketChangeException("CHANGE_REQUEST_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket change request not found.");
        operatorContext.CurrentOperatorId = opId;

        var changeRequest = await db.TicketChangeRequests
            .SingleOrDefaultAsync(r => r.OperatorId == opId && r.Id == changeRequestId, ct)
            ?? throw new TicketChangeException("CHANGE_REQUEST_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket change request not found.");

        // Idempotency: if already applied or approved, return current state
        if (changeRequest.Status is TicketChangeRequestStatus.Applied or TicketChangeRequestStatus.Approved)
            return ToResponse(changeRequest);

        if (changeRequest.Status != TicketChangeRequestStatus.Requested)
            throw new TicketChangeException("CHANGE_REQUEST_NOT_APPROVABLE", StatusCodes.Status409Conflict,
                $"Change request is in state '{changeRequest.Status}' and cannot be approved.");

        var ticket = await db.Tickets
            .SingleOrDefaultAsync(t => t.OperatorId == opId && t.Id == changeRequest.TicketId, ct)
            ?? throw new TicketChangeException("TICKET_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket not found.");

        if (ticket.ChangeCount >= 1)
            throw new TicketChangeException("TICKET_CHANGE_LIMIT_REACHED", StatusCodes.Status409Conflict,
                "Ticket change limit reached (maximum 1 change per ticket).");

        var now = DateTimeOffset.UtcNow;
        changeRequest.Status = TicketChangeRequestStatus.Approved;
        changeRequest.DecidedByUserId = staffUserId;
        changeRequest.DecidedAt = now;
        changeRequest.DecisionReason = decisionReason;
        changeRequest.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        // Execute the atomic SQL function to apply disruption change and revoke old QR
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT metro.apply_ticket_change({opId}, {changeRequestId}, {staffUserId})", ct);

        // Reload the change request
        await db.Entry(changeRequest).ReloadAsync(ct);
        await db.Entry(ticket).ReloadAsync(ct);

        // If the service date is today (in Vietnam time), generate new QR credential for the ticket
        var today = FareRules.ToVietnamDate(DateTime.UtcNow);
        if (ticket.ServiceDate.HasValue && ticket.ServiceDate.Value == today)
        {
            await GenerateNewQrCredentialAsync(ticket, ct);
        }

        return ToResponse(changeRequest);
    }

    public async Task<TicketChangeResponse> GetAsync(Guid changeRequestId, string userId, string? operatorCode, CancellationToken ct)
    {
        var opId = await db.TicketChangeRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == changeRequestId)
            .Select(r => (Guid?)r.OperatorId).SingleOrDefaultAsync(ct)
            ?? throw new TicketChangeException("CHANGE_REQUEST_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket change request not found.");
        operatorContext.CurrentOperatorId = opId;

        var changeRequest = await db.TicketChangeRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OperatorId == opId && r.Id == changeRequestId, ct)
            ?? throw new TicketChangeException("CHANGE_REQUEST_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket change request not found.");

        return ToResponse(changeRequest);
    }

    private async Task GenerateNewQrCredentialAsync(Domain.Entities.Tickets.Ticket ticket, CancellationToken ct)
    {
        try
        {
            var maxVersion = await db.TicketCredentials
                .Where(c => c.OperatorId == ticket.OperatorId && c.TicketId == ticket.Id && c.CredentialType == "QR")
                .MaxAsync(c => (int?)c.Version, ct) ?? 0;

            var newVersion = maxVersion + 1;
            var credentialId = Guid.NewGuid();
            var keyId = qrCodec.ActiveKeyId;
            var token = qrCodec.Token(ticket.OperatorId, ticket.CorrelationId, credentialId, newVersion, keyId);

            var newCredential = new Domain.Entities.Tickets.TicketCredential
            {
                Id = credentialId,
                OperatorId = ticket.OperatorId,
                TicketId = ticket.Id,
                CredentialType = "QR",
                TokenHash = TicketQrCodec.Hash(token),
                Status = "ACTIVE",
                Version = newVersion,
                KeyId = keyId,
                IssuedAt = DateTime.UtcNow
            };

            db.TicketCredentials.Add(newCredential);
            ticket.Status = "ACTIVE";
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // If already created or issuance worker is handling it, continue
        }
    }

    private async Task<Guid> ResolveOperatorAsync(string? operatorCode, Guid? ticketId, CancellationToken ct)
    {
        if (operatorContext.CurrentOperatorId is { } existing && existing != Guid.Empty)
            return existing;

        if (!string.IsNullOrWhiteSpace(operatorCode))
        {
            var op = await db.Operators.AsNoTracking().Where(o => o.Code == operatorCode && o.Status == "ACTIVE")
                .Select(o => (Guid?)o.Id).SingleOrDefaultAsync(ct);
            if (op.HasValue)
            {
                operatorContext.CurrentOperatorId = op.Value;
                return op.Value;
            }
        }

        if (ticketId.HasValue)
        {
            var op = await db.Tickets.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.Id == ticketId.Value)
                .Select(t => (Guid?)t.OperatorId).SingleOrDefaultAsync(ct);
            if (op.HasValue)
            {
                operatorContext.CurrentOperatorId = op.Value;
                return op.Value;
            }
        }

        var defaultOp = await db.Operators.AsNoTracking().Where(o => o.Status == "ACTIVE")
            .Select(o => o.Id).FirstOrDefaultAsync(ct);
        if (defaultOp != Guid.Empty)
        {
            operatorContext.CurrentOperatorId = defaultOp;
            return defaultOp;
        }

        throw new TicketChangeException("OPERATOR_NOT_FOUND", 400, "Active operator not found.");
    }

    private static TicketChangeResponse ToResponse(TicketChangeRequest r) => new(
        r.Id,
        r.OperatorId,
        r.RequestCode,
        r.TicketId,
        r.OrderId,
        r.RequestedByUserId,
        r.ReasonType.ToString().ToUpperInvariant(),
        r.IncidentId,
        r.OriginalServiceDate,
        r.RequestedServiceDate,
        r.OriginalOriginStationId,
        r.OriginalDestinationStationId,
        r.NewOriginStationId,
        r.NewDestinationStationId,
        r.OriginalPlannedTripId,
        r.NewPlannedTripId,
        r.Status.ToString().ToUpperInvariant(),
        r.DecidedByUserId,
        r.DecidedAt,
        r.DecisionReason,
        r.AppliedAt,
        r.ChangeSequence,
        r.CreatedAt,
        r.UpdatedAt
    );
}
