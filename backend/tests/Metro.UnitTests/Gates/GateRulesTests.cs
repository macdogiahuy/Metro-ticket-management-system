using Metro.Application.Abstractions.Gates;
using Metro.Application.Gates;
using Metro.Infrastructure.Ticketing;

namespace Metro.UnitTests.Gates;

public sealed class GateRulesTests
{
    [Theory]
    [InlineData(null, 18000d, "LOAD_NO_DATA")]
    [InlineData(10d, null, "LOAD_NO_LIMIT")]
    [InlineData(10d, 0d, "LOAD_NO_LIMIT")]
    [InlineData(17999.9, 18000d, null)]
    [InlineData(18000d, 18000d, "LOAD_FULL")]
    [InlineData(18000.1, 18000d, "LOAD_FULL")]
    public void Entry_load_uses_latest_kg_and_a_strict_less_than_limit(double? observed, double? maximum, string? error)
        => Assert.Equal(error, GateRules.EntryLoad((decimal?)observed, (decimal?)maximum));

    [Fact]
    public void Exit_requires_no_trip_but_entry_requires_frozen_trip_and_direction()
    {
        var id = Guid.NewGuid();
        var r = new GateScanRequest(id, "EXIT", Guid.NewGuid(), null, null, ["opaque-qr"], "staff-browser");
        GateRules.Validate(r, id.ToString());
        Assert.Throws<GateException>(() => GateRules.Validate(r with { Action = "ENTRY" }, id.ToString()));
        Assert.Throws<GateException>(() => GateRules.Validate(r with { QrTokens = ["same", "same"] }, id.ToString()));
        Assert.Throws<GateException>(() => GateRules.Validate(r, Guid.NewGuid().ToString()));
    }

    [Fact]
    public void Manual_resolution_needs_evidence_and_undo_needs_explicit_unused_passage_confirmation()
    {
        var id = Guid.NewGuid();
        var r = new GateResolutionRequest(id, Guid.NewGuid(), "UNDO_SCAN", Guid.NewGuid(), "Mistaken scan", "staff-case-1", true);
        Assert.Throws<GateException>(() => GateRules.Validate(r, id.ToString()));
        GateRules.Validate(r with { PassengerHasNotTravelled = true }, id.ToString());
        Assert.Throws<GateException>(() => GateRules.Validate(r with { Action = "SUPPLY_ENTRY_AND_EXIT" }, id.ToString()));
        Assert.Throws<GateException>(() => GateRules.Validate(r with { Action = "CLOSE_JOURNEY", EvidenceReference = "" }, id.ToString()));
    }

    [Theory]
    [InlineData("booking-code")]
    [InlineData("M1.key.bad.bad.bad.1.ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("M1.key.00000000000000000000000000000001.00000000000000000000000000000002.00000000000000000000000000000003.0.ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    public void Reading_a_code_never_treats_an_order_code_or_malformed_payload_as_QR(string token) => Assert.Null(TicketQrCodec.Read(token));
}
