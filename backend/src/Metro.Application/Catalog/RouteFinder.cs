using Metro.Application.Abstractions.Catalog;

namespace Metro.Application.Catalog;

/// <summary>
/// BR-050 &amp; BR-051 active-network route finder: minimizes segments first, then transfers,
/// returning all tied optimal acyclic paths in deterministic order.
/// </summary>
public sealed class RouteFinder : IRouteFinder
{
    private const int MaxReturnedOptions = 20;

    public IReadOnlyList<JourneyOption> FindOptimalRoutes(NetworkGraphData graph, Guid originStationId, Guid destinationStationId)
    {
        if (originStationId == Guid.Empty || destinationStationId == Guid.Empty || originStationId == destinationStationId)
            return [];

        var activeLines = graph.Lines.Where(l => l.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)).ToDictionary(l => l.Id);
        var activeStations = graph.Stations.Where(s => s.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)).ToDictionary(s => s.Id);
        if (!activeStations.ContainsKey(originStationId) || !activeStations.ContainsKey(destinationStationId))
            return [];

        var (rideEdges, stationLines) = BuildActiveGraph(graph.LineStops, activeLines, activeStations);
        if (!stationLines.TryGetValue(originStationId, out var originLines) || !stationLines.TryGetValue(destinationStationId, out var destLines))
            return [];

        var bestCost = new Dictionary<Node, Cost>();
        var predecessors = new Dictionary<Node, List<IncomingStep>>();
        var queue = new PriorityQueue<Node, Cost>();
        foreach (var lineId in originLines)
        {
            var start = new Node(lineId, originStationId);
            bestCost[start] = new Cost(0, 0);
            queue.Enqueue(start, new Cost(0, 0));
        }

        while (queue.TryDequeue(out var current, out var cost))
        {
            if (bestCost.TryGetValue(current, out var recorded) && cost.CompareTo(recorded) > 0) continue;

            if (rideEdges.TryGetValue(current, out var rides))
            {
                foreach (var edge in rides)
                    Relax(new Node(current.LineId, edge.ToStationId), new Cost(cost.Segments + 1, cost.Transfers),
                        new IncomingStep(current, false, edge.Direction, edge.TravelTimeSeconds, edge.DistanceMeters), bestCost, predecessors, queue);
            }

            if (current.StationId != originStationId && current.StationId != destinationStationId &&
                stationLines.TryGetValue(current.StationId, out var linesAtStation))
            {
                var transferSeconds = activeStations[current.StationId].MinTransferSeconds;
                foreach (var targetLineId in linesAtStation.Where(id => id != current.LineId))
                    Relax(new Node(targetLineId, current.StationId), new Cost(cost.Segments, cost.Transfers + 1),
                        new IncomingStep(current, true, 0, transferSeconds, 0), bestCost, predecessors, queue);
            }
        }

        Cost? globalBest = null;
        var winningTargets = new List<Node>();
        foreach (var lineId in destLines)
        {
            var target = new Node(lineId, destinationStationId);
            if (!bestCost.TryGetValue(target, out var cost)) continue;
            if (globalBest is null || cost.CompareTo(globalBest.Value) < 0)
            {
                globalBest = cost;
                winningTargets.Clear();
                winningTargets.Add(target);
            }
            else if (cost.CompareTo(globalBest.Value) == 0)
            {
                winningTargets.Add(target);
            }
        }
        if (globalBest is null) return [];

        var rawPaths = new List<List<PathStep>>();
        foreach (var target in winningTargets.OrderBy(t => activeLines[t.LineId].Code, StringComparer.Ordinal))
        {
            ReconstructPaths(target, originStationId, predecessors, [target.StationId], [], rawPaths);
            if (rawPaths.Count >= MaxReturnedOptions) break;
        }

        return rawPaths
            .Select(steps => BuildOption(steps, activeLines, activeStations))
            .OrderBy(o => o.EstimatedDurationSeconds)
            .ThenBy(o => string.Join('|', o.Legs.Select(l => l.LineCode)), StringComparer.Ordinal)
            .ThenBy(o => string.Join('|', o.Legs.SelectMany(l => l.Stations).Select(s => s.Code)), StringComparer.Ordinal)
            .Select((option, index) => option with { OptionIndex = index + 1 })
            .ToArray();
    }

    private static (Dictionary<Node, List<RideEdge>>, Dictionary<Guid, HashSet<Guid>>) BuildActiveGraph(
        IReadOnlyList<GraphLineStop> lineStops, Dictionary<Guid, GraphLine> activeLines, Dictionary<Guid, GraphStation> activeStations)
    {
        var rideEdges = new Dictionary<Node, List<RideEdge>>();
        var stationLines = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var lineGroup in lineStops.Where(ls => activeLines.ContainsKey(ls.LineId)).GroupBy(ls => ls.LineId))
        {
            var byDirection = lineGroup.GroupBy(ls => ls.Direction).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SequenceNo).ToList());
            var synthesizeReverse = byDirection.Count == 1;

            foreach (var (direction, stops) in byDirection)
            {
                foreach (var stop in stops.Where(s => activeStations.ContainsKey(s.StationId)))
                    (stationLines.TryGetValue(stop.StationId, out var set) ? set : stationLines[stop.StationId] = []).Add(lineGroup.Key);

                for (var i = 0; i < stops.Count - 1; i++)
                {
                    var (a, b) = (stops[i], stops[i + 1]);
                    if (!activeStations.ContainsKey(a.StationId) || !activeStations.ContainsKey(b.StationId)) continue;

                    var distance = Math.Max(0, b.DistanceFromOriginM - a.DistanceFromOriginM);
                    AddEdge(rideEdges, new Node(lineGroup.Key, a.StationId), new RideEdge(b.StationId, direction, b.TravelTimeSeconds, distance));
                    if (synthesizeReverse)
                        AddEdge(rideEdges, new Node(lineGroup.Key, b.StationId),
                            new RideEdge(a.StationId, direction == 0 ? (short)1 : (short)0, b.TravelTimeSeconds, distance));
                }
            }
        }
        return (rideEdges, stationLines);
    }

    private static void AddEdge(Dictionary<Node, List<RideEdge>> map, Node from, RideEdge edge)
    {
        var list = map.TryGetValue(from, out var existing) ? existing : map[from] = [];
        if (!list.Any(e => e.ToStationId == edge.ToStationId && e.Direction == edge.Direction)) list.Add(edge);
    }

    private static void Relax(Node next, Cost candidate, IncomingStep step, Dictionary<Node, Cost> bestCost,
        Dictionary<Node, List<IncomingStep>> predecessors, PriorityQueue<Node, Cost> queue)
    {
        if (!bestCost.TryGetValue(next, out var existing) || candidate.CompareTo(existing) < 0)
        {
            bestCost[next] = candidate;
            predecessors[next] = [step];
            queue.Enqueue(next, candidate);
        }
        else if (candidate.CompareTo(existing) == 0 && !predecessors[next].Contains(step))
        {
            predecessors[next].Add(step);
        }
    }

    private static void ReconstructPaths(Node current, Guid originStationId, Dictionary<Node, List<IncomingStep>> predecessors,
        HashSet<Guid> visitedStations, List<PathStep> reversedSteps, List<List<PathStep>> results)
    {
        if (results.Count >= MaxReturnedOptions) return;
        if (current.StationId == originStationId)
        {
            var forward = new List<PathStep>(reversedSteps);
            forward.Reverse();
            results.Add(forward);
            return;
        }
        if (!predecessors.TryGetValue(current, out var incoming)) return;

        foreach (var step in incoming)
        {
            var addedStation = !step.IsTransfer && visitedStations.Add(step.From.StationId);
            if (!step.IsTransfer && !addedStation) continue;
            reversedSteps.Add(new PathStep(step.From, current, step.IsTransfer, step.Direction, step.DurationSeconds, step.DistanceMeters));
            ReconstructPaths(step.From, originStationId, predecessors, visitedStations, reversedSteps, results);
            reversedSteps.RemoveAt(reversedSteps.Count - 1);
            if (addedStation) visitedStations.Remove(step.From.StationId);
        }
    }

    private static JourneyOption BuildOption(List<PathStep> steps, Dictionary<Guid, GraphLine> lines, Dictionary<Guid, GraphStation> stations)
    {
        var legs = new List<JourneyLeg>();
        var transfers = new List<JourneyTransfer>();
        var currentRides = new List<PathStep>();

        void FlushLeg()
        {
            if (currentRides.Count == 0) return;
            var (first, last) = (currentRides[0], currentRides[^1]);
            var (line, fromSt, toSt) = (lines[first.From.LineId], stations[first.From.StationId], stations[last.To.StationId]);
            var stops = new List<JourneyStop>(currentRides.Count + 1) { new(fromSt.Id, fromSt.Code, fromSt.Name) };
            stops.AddRange(currentRides.Select(r => stations[r.To.StationId]).Select(s => new JourneyStop(s.Id, s.Code, s.Name)));
            legs.Add(new JourneyLeg(line.Id, line.Code, line.Name, first.Direction, fromSt.Id, fromSt.Code, fromSt.Name,
                toSt.Id, toSt.Code, toSt.Name, currentRides.Count, currentRides.Sum(s => s.DurationSeconds), currentRides.Sum(s => s.DistanceMeters), stops));
            currentRides.Clear();
        }

        foreach (var step in steps)
        {
            if (step.IsTransfer)
            {
                FlushLeg();
                var (st, fromLine, toLine) = (stations[step.From.StationId], lines[step.From.LineId], lines[step.To.LineId]);
                transfers.Add(new JourneyTransfer(st.Id, st.Code, st.Name, fromLine.Id, fromLine.Code, toLine.Id, toLine.Code, step.DurationSeconds));
            }
            else
            {
                if (currentRides.Count > 0 && currentRides[^1].From.LineId != step.From.LineId) FlushLeg();
                currentRides.Add(step);
            }
        }
        FlushLeg();

        var totalSeconds = legs.Sum(l => l.TravelTimeSeconds) + transfers.Sum(t => t.MinTransferSeconds);
        return new JourneyOption(0, legs.Sum(l => l.SegmentCount), transfers.Count, totalSeconds,
            (int)Math.Ceiling(totalSeconds / 60.0), legs.Sum(l => l.DistanceMeters), legs, transfers);
    }

    private readonly record struct Node(Guid LineId, Guid StationId);
    private readonly record struct Cost(int Segments, int Transfers) : IComparable<Cost>
    {
        public int CompareTo(Cost other) => Segments != other.Segments ? Segments.CompareTo(other.Segments) : Transfers.CompareTo(other.Transfers);
    }
    private readonly record struct RideEdge(Guid ToStationId, short Direction, int TravelTimeSeconds, int DistanceMeters);
    private readonly record struct IncomingStep(Node From, bool IsTransfer, short Direction, int DurationSeconds, int DistanceMeters);
    private readonly record struct PathStep(Node From, Node To, bool IsTransfer, short Direction, int DurationSeconds, int DistanceMeters);
}
