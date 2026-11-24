using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Metro.Infrastructure.Booking;

public sealed class BookingSuggestionWorker(IServiceScopeFactory scopes, IConfiguration config,
    ILogger<BookingSuggestionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (config["METRO_TEST_MODE"] == "1" || config.GetValue("Booking:SuggestionsEnabled", true) == false) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var offset = 0;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<BookingSuggestionProcessor>();
                var count = await processor.ProcessAsync(offset, stoppingToken);
                offset = count < BookingSuggestionProcessor.BatchSize ? 0 : offset + count;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { log.LogWarning("Booking suggestion polling failed. It will be retried."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class BookingSuggestionProcessor(ApplicationDbContext db, IOperatorContext scope, BookingService bookings,
    TimeProvider time, ILogger<BookingSuggestionProcessor> log)
{
    public const int BatchSize = 100;

    public async Task<int> ProcessAsync(int offset, CancellationToken ct)
    {
        var today = TicketRules.LocalDate(time.GetUtcNow());
        var due = await db.Database.SqlQuery<DueOrder>($"""
            SELECT o.id AS "Id", o.operator_id AS "OperatorId" FROM metro.orders o
            JOIN metro.operators op ON op.id=o.operator_id AND op.status='ACTIVE'
            WHERE o.service_date>={today} AND o.status IN ('PAID','PENDING_ISSUE','ISSUED')
              AND (o.paid_at IS NOT NULL OR o.free_confirmed_at IS NOT NULL)
              AND o.fare_snapshot->>'BookingCheckout'='true'
              AND NOT EXISTS (SELECT 1 FROM metro.tickets t WHERE t.operator_id=o.operator_id AND t.order_id=o.id
                  AND (t.first_used_at IS NOT NULL OR t.status IN ('IN_USE','USED','REVOKED','REFUND_PENDING','REFUNDED','EXPIRED')))
            ORDER BY o.service_date,o.id LIMIT {BatchSize} OFFSET {offset}
            """).ToListAsync(ct);
        foreach (var entry in due)
        {
            try
            {
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
                {
                    db.ChangeTracker.Clear();
                    await RefreshAsync(entry, ct);
                }, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { log.LogWarning("A booking suggestion update failed. It will be retried."); }
            finally { db.ChangeTracker.Clear(); }
        }
        return due.Count;
    }

    private async Task RefreshAsync(DueOrder entry, CancellationToken ct)
    {
        scope.CurrentOperatorId = entry.OperatorId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Share the service-change lock so an old suggestion cannot commit after a published closure.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({entry.OperatorId.ToString()},14))", ct);
        var order = await bookings.LockAsync(entry.Id, ct);
        if (order.Status is not ("PAID" or "PENDING_ISSUE" or "ISSUED") ||
            (order.PaidAt ?? order.FreeConfirmedAt) is not { } confirmedAt ||
            order.ServiceDate is not { } day || order.BeneficiaryUserId is not { } beneficiary)
            return;
        var now = time.GetUtcNow();
        var today = TicketRules.LocalDate(now);
        if (day < today || await db.Tickets.AnyAsync(t => t.OrderId == order.Id &&
            (t.FirstUsedAt != null || t.Status == "IN_USE" || t.Status == "USED" || t.Status == "REVOKED" ||
             t.Status == "REFUND_PENDING" || t.Status == "REFUNDED" || t.Status == "EXPIRED" ||
             db.Journeys.Any(j => j.TicketId == t.Id)), ct)) return;

        var version = await db.OrderVersions.AsNoTracking().SingleOrDefaultAsync(v =>
            v.OrderId == order.Id && v.Version == order.OrderVersion && v.Status == "CONFIRMED", ct);
        if (version?.OriginStationId is not { } origin || version.DestinationStationId is not { } destination) return;
        var price = JsonSerializer.Deserialize<CheckoutPrice>(version.FareSnapshot);
        if (price?.FirstLineId is not { } line || price.FirstDirection is not { } direction || price.FirstLegEnd is not { } legEnd) return;
        var routeIsServing = await RouteIsServingAsync(price.Route, ct);

        var start = day > today ? TicketRules.Midnight(day) : now.UtcDateTime;
        if (confirmedAt > start) start = confirmedAt;
        var serviceEnd = TicketRules.Midnight(day.AddDays(1));
        var op = entry.OperatorId;
        var trip = await db.Database.SqlQuery<TripCandidate>($"""
            SELECT p.id AS "TripId", s.id AS "ScheduleId", s.code AS "ScheduleCode",
                   COALESCE(actual.actual_departure_at, eta.expected_departure_at, stop.planned_departure_at) AS "Departure",
                   (eta.expected_departure_at IS NOT NULL) AS "HasUpdate", boarding.current_boarding AS "IsBoarding"
            FROM metro.planned_trips p
            JOIN metro.schedule_versions s ON s.operator_id=p.operator_id AND s.id=p.schedule_version_id
            JOIN metro.service_calendars cal ON cal.operator_id=s.operator_id AND cal.id=s.service_calendar_id
            JOIN metro.lines l ON l.operator_id=p.operator_id AND l.id=p.line_id
            JOIN metro.stations origin ON origin.operator_id=p.operator_id AND origin.id={origin}
            JOIN metro.stations destination ON destination.operator_id=p.operator_id AND destination.id={destination}
            JOIN metro.planned_stop_times stop ON stop.operator_id=p.operator_id AND stop.planned_trip_id=p.id AND stop.station_id={origin}
            LEFT JOIN metro.actual_trips a ON a.operator_id=p.operator_id AND a.planned_trip_id=p.id
            LEFT JOIN metro.actual_stop_times actual ON actual.operator_id=a.operator_id AND actual.actual_trip_id=a.id AND actual.station_id=stop.station_id
            LEFT JOIN LATERAL (
                SELECT e.expected_departure_at FROM metro.trip_eta_updates e
                WHERE e.operator_id=p.operator_id AND e.planned_trip_id=p.id AND e.station_id=stop.station_id
                  AND e.recorded_at<={now}
                ORDER BY e.recorded_at DESC,e.id LIMIT 1
            ) eta ON true
            CROSS JOIN LATERAL (
                SELECT ({day}={today} AND actual.actual_arrival_at<={now} AND actual.actual_departure_at IS NULL
                    AND NOT EXISTS (SELECT 1 FROM metro.actual_stop_times later
                        WHERE later.operator_id=a.operator_id AND later.actual_trip_id=a.id
                          AND later.sequence_no>actual.sequence_no AND later.actual_arrival_at<={now})) IS TRUE AS current_boarding
            ) boarding
            WHERE {routeIsServing} AND p.operator_id={op} AND p.service_date={day} AND p.line_id={line} AND p.direction={direction}
              AND s.status='PUBLISHED' AND s.effective_from<{serviceEnd} AND (s.effective_to IS NULL OR s.effective_to>{start})
              AND cal.status='PUBLISHED' AND cal.effective_from<={day} AND (cal.effective_to IS NULL OR cal.effective_to>={day})
              AND l.status='ACTIVE' AND origin.status='ACTIVE' AND destination.status='ACTIVE'
              AND p.status IN ('PLANNED','READY','IN_SERVICE','DELAYED')
              AND (a.id IS NULL OR a.status IN ('READY','IN_SERVICE','DELAYED'))
              AND p.boarding_status='OPEN'
              AND actual.actual_departure_at IS NULL
              AND (p.status<>'DELAYED' AND a.status IS DISTINCT FROM 'DELAYED' OR
                   eta.expected_departure_at IS NOT NULL OR boarding.current_boarding)
              AND (NOT EXISTS(SELECT 1 FROM metro.service_disruptions d WHERE d.operator_id=p.operator_id
                   AND d.line_id=p.line_id AND d.status='PUBLISHED' AND d.effect='DELAYED')
                   OR eta.expected_departure_at IS NOT NULL OR boarding.current_boarding)
              AND (COALESCE(eta.expected_departure_at, stop.planned_departure_at)>={start} OR boarding.current_boarding)
              AND COALESCE(actual.actual_departure_at, eta.expected_departure_at, stop.planned_departure_at)<{serviceEnd}
              AND stop.planned_departure_at>=s.effective_from AND (s.effective_to IS NULL OR stop.planned_departure_at<s.effective_to)
              AND EXISTS (SELECT 1 FROM metro.planned_stop_times next_stop WHERE next_stop.operator_id=p.operator_id
                  AND next_stop.planned_trip_id=p.id AND next_stop.station_id={legEnd} AND next_stop.sequence_no>stop.sequence_no)
              AND ({day}>{today} OR NOT EXISTS (SELECT 1 FROM metro.v_trip_load_status load
                  WHERE load.operator_id=p.operator_id AND load.planned_trip_id=p.id AND load.load_status='FULL'))
            ORDER BY "Departure",p.id LIMIT 1
            """).SingleOrDefaultAsync(ct);
        var current = await db.TripSuggestions.AsNoTracking().SingleOrDefaultAsync(s =>
            s.OrderId == order.Id && s.OrderVersion == order.OrderVersion && s.SupersededAt == null, ct);
        var status = trip is null ? TripSuggestionStatus.NoTrip : TripSuggestionStatus.Suggested;
        var source = trip?.HasUpdate == true ? TripSuggestionSource.Eta :
            trip?.IsBoarding == true ? TripSuggestionSource.Manual : TripSuggestionSource.Schedule;
        if (current is not null && current.Status == status && current.SuggestedPlannedTripId == trip?.TripId &&
            current.SuggestedDepartureAt == trip?.Departure && current.Source == source && current.ScheduleVersionId == trip?.ScheduleId)
            return;

        // The existing trigger supersedes the previous row for this version within the transaction.
        db.TripSuggestions.Add(new TripSuggestion
        {
            Id = Guid.NewGuid(), OperatorId = op, OrderId = order.Id, OrderVersion = order.OrderVersion,
            SuggestionCode = $"SUG-{Guid.NewGuid():N}", BeneficiaryUserId = beneficiary, ServiceDate = day,
            OriginStationId = origin, DestinationStationId = destination, LineId = line, Direction = direction,
            Status = status, Source = source, SuggestedPlannedTripId = trip?.TripId, SuggestedDepartureAt = trip?.Departure,
            ScheduleVersionId = trip?.ScheduleId, SourceVersionLabel = trip?.ScheduleCode, NoTripReason = trip is null ? "NO_SERVING_TRIP" : null,
            UpdateReason = current is null ? "ORDER_CONFIRMED" : "SERVICE_CHANGED", ComputedAt = now, UpdatedAt = now,
            CorrelationId = Guid.NewGuid()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private sealed record DueOrder(Guid Id, Guid OperatorId);
    private sealed record TripCandidate(Guid TripId, Guid ScheduleId, string ScheduleCode, DateTimeOffset Departure, bool HasUpdate, bool IsBoarding);

    private async Task<bool> RouteIsServingAsync(string? route, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(route)) return false;
        var lines = new HashSet<Guid>();
        var stations = new HashSet<Guid>();
        foreach (var leg in route.Split('|'))
        {
            var parts = leg.Split(':');
            if (parts.Length != 3 || !Guid.TryParse(parts[0], out var line)) return false;
            lines.Add(line);
            foreach (var value in parts[2].Split(','))
            {
                if (!Guid.TryParse(value, out var station)) return false;
                stations.Add(station);
            }
        }
        return await db.Lines.CountAsync(l => lines.Contains(l.Id) && l.Status == "ACTIVE", ct) == lines.Count &&
               await db.Stations.CountAsync(s => stations.Contains(s.Id) && s.Status == "ACTIVE", ct) == stations.Count;
    }
}
