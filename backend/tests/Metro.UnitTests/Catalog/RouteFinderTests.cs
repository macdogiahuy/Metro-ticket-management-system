using Metro.Application.Abstractions.Catalog;
using Metro.Application.Catalog;

namespace Metro.UnitTests.Catalog;

public sealed class RouteFinderTests
{
    private readonly RouteFinder _sut = new();

    [Fact]
    public void SingleLine_ForwardAndSynthesizedReverse_CalculateSegmentsDurationAndDistance()
    {
        var l1 = Line("L1");
        var (a, b, c, d) = (Station("S1"), Station("S2"), Station("S3"), Station("S4"));
        var graph = new NetworkGraphData(
            [l1],
            [a, b, c, d],
            [
                Stop(l1.Id, a.Id, 0, 1, 180, 0),
                Stop(l1.Id, b.Id, 0, 2, 180, 1500),
                Stop(l1.Id, c.Id, 0, 3, 240, 3500),
                Stop(l1.Id, d.Id, 0, 4, 180, 5000),
            ]);

        var forward = Assert.Single(_sut.FindOptimalRoutes(graph, a.Id, d.Id));
        Assert.Equal(3, forward.SegmentCount);
        Assert.Equal(0, forward.TransferCount);
        Assert.Equal(600, forward.EstimatedDurationSeconds);
        Assert.Equal(10, forward.EstimatedDurationMinutes);
        Assert.Equal(5000, forward.TotalDistanceMeters);
        Assert.Equal(0, Assert.Single(forward.Legs).Direction);
        Assert.Equal(["S1", "S2", "S3", "S4"], forward.Legs[0].Stations.Select(s => s.Code));

        // BR-050: reverse direction works even when only direction 0 is seeded.
        var reverse = Assert.Single(_sut.FindOptimalRoutes(graph, d.Id, a.Id));
        Assert.Equal(3, reverse.SegmentCount);
        Assert.Equal(0, reverse.TransferCount);
        Assert.Equal(600, reverse.EstimatedDurationSeconds);
        Assert.Equal(5000, reverse.TotalDistanceMeters);
        Assert.Equal(1, Assert.Single(reverse.Legs).Direction);
        Assert.Equal(["S4", "S3", "S2", "S1"], reverse.Legs[0].Stations.Select(s => s.Code));
    }

    [Fact]
    public void ExplicitDualDirections_UsesConfiguredReverseTravelTime()
    {
        var l1 = Line("L1");
        var (a, b) = (Station("S1"), Station("S2"));
        var graph = new NetworkGraphData(
            [l1],
            [a, b],
            [
                Stop(l1.Id, a.Id, 0, 1, 180, 0),
                Stop(l1.Id, b.Id, 0, 2, 200, 1000),
                Stop(l1.Id, b.Id, 1, 1, 180, 0),
                Stop(l1.Id, a.Id, 1, 2, 260, 1100),
            ]);

        var reverse = Assert.Single(_sut.FindOptimalRoutes(graph, b.Id, a.Id));
        Assert.Equal(1, reverse.SegmentCount);
        Assert.Equal(260, reverse.EstimatedDurationSeconds);
        Assert.Equal(1100, reverse.TotalDistanceMeters);
        Assert.Equal(1, Assert.Single(reverse.Legs).Direction);
    }

    [Fact]
    public void MultiLineTransfer_DoesNotIncrementSegmentCount_AndAddsMinTransferSeconds()
    {
        var (l1, l2) = (Line("L1"), Line("L2"));
        var (a, b, interchange, y) = (Station("A"), Station("B"), Station("C", minTransferSeconds: 300), Station("Y"));
        var graph = new NetworkGraphData(
            [l1, l2],
            [a, b, interchange, y],
            [
                Stop(l1.Id, a.Id, 0, 1, 180, 0),
                Stop(l1.Id, b.Id, 0, 2, 180, 1000),
                Stop(l1.Id, interchange.Id, 0, 3, 180, 2000),
                Stop(l2.Id, interchange.Id, 0, 1, 180, 0),
                Stop(l2.Id, y.Id, 0, 2, 180, 1200),
            ]);

        var option = Assert.Single(_sut.FindOptimalRoutes(graph, a.Id, y.Id));
        Assert.Equal(3, option.SegmentCount);
        Assert.Equal(1, option.TransferCount);
        Assert.Equal(3 * 180 + 300, option.EstimatedDurationSeconds);
        Assert.Equal(14, option.EstimatedDurationMinutes);
        Assert.Equal(3200, option.TotalDistanceMeters);
        Assert.Equal(2, option.Legs.Count);
        var transfer = Assert.Single(option.Transfers);
        Assert.Equal("C", transfer.StationCode);
        Assert.Equal("L1", transfer.FromLineCode);
        Assert.Equal("L2", transfer.ToLineCode);
        Assert.Equal(300, transfer.MinTransferSeconds);
    }

    [Fact]
    public void LexicographicPriority_FewerSegmentsBeatsFewerTransfers()
    {
        // L1 direct path: A - B - C - D - E (4 segments, 0 transfers)
        // Shortcut via L2: A - B (L1) -> transfer -> E (L2) (2 segments, 1 transfer)
        var (l1, l2) = (Line("L1"), Line("L2"));
        var (a, b, c, d, e) = (Station("A"), Station("B"), Station("C"), Station("D"), Station("E"));
        var graph = new NetworkGraphData(
            [l1, l2],
            [a, b, c, d, e],
            [
                Stop(l1.Id, a.Id, 0, 1, 180, 0),
                Stop(l1.Id, b.Id, 0, 2, 180, 1000),
                Stop(l1.Id, c.Id, 0, 3, 180, 2000),
                Stop(l1.Id, d.Id, 0, 4, 180, 3000),
                Stop(l1.Id, e.Id, 0, 5, 180, 4000),
                Stop(l2.Id, b.Id, 0, 1, 180, 0),
                Stop(l2.Id, e.Id, 0, 2, 180, 1500),
            ]);

        var winner = Assert.Single(_sut.FindOptimalRoutes(graph, a.Id, e.Id));
        Assert.Equal(2, winner.SegmentCount);
        Assert.Equal(1, winner.TransferCount);
    }

    [Fact]
    public void LexicographicPriority_EqualSegmentsPrefersFewerTransfers()
    {
        // L1 direct: A - B - C - D (3 segments, 0 transfers)
        // L1+L2:     A - B (L1) -> X -> D (L2) (3 segments, 1 transfer)
        var (l1, l2) = (Line("L1"), Line("L2"));
        var (a, b, c, d, x) = (Station("A"), Station("B"), Station("C"), Station("D"), Station("X"));
        var graph = new NetworkGraphData(
            [l1, l2],
            [a, b, c, d, x],
            [
                Stop(l1.Id, a.Id, 0, 1, 180, 0),
                Stop(l1.Id, b.Id, 0, 2, 180, 1000),
                Stop(l1.Id, c.Id, 0, 3, 180, 2000),
                Stop(l1.Id, d.Id, 0, 4, 180, 3000),
                Stop(l2.Id, b.Id, 0, 1, 180, 0),
                Stop(l2.Id, x.Id, 0, 2, 180, 1000),
                Stop(l2.Id, d.Id, 0, 3, 180, 2000),
            ]);

        var winner = Assert.Single(_sut.FindOptimalRoutes(graph, a.Id, d.Id));
        Assert.Equal(3, winner.SegmentCount);
        Assert.Equal(0, winner.TransferCount);
        Assert.Equal("L1", Assert.Single(winner.Legs).LineCode);
    }

    [Fact]
    public void TiedOptimalRoutes_ReturnsAllAcyclicAlternativesDeterministically()
    {
        // Diamond: L1 (A - B - D) and L2 (A - C - D) both 2 segments, 0 transfers.
        var (l1, l2) = (Line("L1"), Line("L2"));
        var (a, b, c, d) = (Station("A"), Station("B"), Station("C"), Station("D"));
        var graph = new NetworkGraphData(
            [l1, l2],
            [a, b, c, d],
            [
                Stop(l1.Id, a.Id, 0, 1, 180, 0),
                Stop(l1.Id, b.Id, 0, 2, 200, 1000),
                Stop(l1.Id, d.Id, 0, 3, 200, 2000),
                Stop(l2.Id, a.Id, 0, 1, 180, 0),
                Stop(l2.Id, c.Id, 0, 2, 180, 1000),
                Stop(l2.Id, d.Id, 0, 3, 180, 2000),
            ]);

        var options = _sut.FindOptimalRoutes(graph, a.Id, d.Id);
        Assert.Equal(2, options.Count);
        Assert.All(options, o =>
        {
            Assert.Equal(2, o.SegmentCount);
            Assert.Equal(0, o.TransferCount);
        });
        // Faster option comes first (360s on L2 before 400s on L1).
        Assert.Equal("L2", options[0].Legs[0].LineCode);
        Assert.Equal(1, options[0].OptionIndex);
        Assert.Equal("L1", options[1].Legs[0].LineCode);
        Assert.Equal(2, options[1].OptionIndex);
    }

    [Fact]
    public void ClosedIntermediateStation_BreaksLineWithoutTeleportingAcrossIt()
    {
        var l1 = Line("L1");
        var (a, closedB, c) = (Station("A"), Station("B", status: "CLOSED"), Station("C"));
        var graph = new NetworkGraphData(
            [l1],
            [a, closedB, c],
            [
                Stop(l1.Id, a.Id, 0, 1, 180, 0),
                Stop(l1.Id, closedB.Id, 0, 2, 180, 1000),
                Stop(l1.Id, c.Id, 0, 3, 180, 2000),
            ]);

        Assert.Empty(_sut.FindOptimalRoutes(graph, a.Id, c.Id));
    }

    [Fact]
    public void SuspendedLineAndSameStationAndZeroDurations_HandledCorrectly()
    {
        var suspended = Line("L-SUSP", status: "SUSPENDED");
        var active = Line("L-ACT");
        var (a, b) = (Station("A", minTransferSeconds: 0), Station("B", minTransferSeconds: 0));

        var suspendedGraph = new NetworkGraphData(
            [suspended], [a, b],
            [Stop(suspended.Id, a.Id, 0, 1, 180, 0), Stop(suspended.Id, b.Id, 0, 2, 180, 1000)]);
        Assert.Empty(_sut.FindOptimalRoutes(suspendedGraph, a.Id, b.Id));

        var zeroDurationGraph = new NetworkGraphData(
            [active], [a, b],
            [Stop(active.Id, a.Id, 0, 1, 0, 0), Stop(active.Id, b.Id, 0, 2, 0, 0)]);
        Assert.Empty(_sut.FindOptimalRoutes(zeroDurationGraph, a.Id, a.Id));
        var option = Assert.Single(_sut.FindOptimalRoutes(zeroDurationGraph, a.Id, b.Id));
        Assert.Equal(0, option.EstimatedDurationSeconds);
        Assert.Equal(0, option.EstimatedDurationMinutes);
    }

    private static GraphLine Line(string code, string status = "ACTIVE") =>
        new(Guid.NewGuid(), code, $"Line {code}", status);

    private static GraphStation Station(string code, string status = "ACTIVE", int minTransferSeconds = 300) =>
        new(Guid.NewGuid(), code, $"Station {code}", status, minTransferSeconds);

    private static GraphLineStop Stop(Guid lineId, Guid stationId, short direction, short seq, int travelSeconds, int distanceM) =>
        new(lineId, stationId, direction, seq, travelSeconds, distanceM);
}
