using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    // Grid-cell contour layout adapted from the supplied Dynamo double-spiral algorithm.
    // A spanning tree over safe cells defines one non-crossing path around the whole zone.
    internal static class AreaPipeLayout
    {
        private const double MillimetersPerFoot = 304.8;
        private const double Epsilon = 1e-7;
        private const int MaximumCells = 50000;

        internal static IList<XYZ> CreateSpiralPath(IList<IList<XYZ>> boundaryLoops,
            double spacingMillimeters, double edgeOffsetMillimeters, double pipeDiameterMillimeters,
            double levelElevationFeet, double layingHeightMillimeters,
            XYZ startConnection, XYZ startDirection, XYZ endConnection, XYZ endDirection)
        {
            if (boundaryLoops == null || boundaryLoops.Count == 0 ||
                !double.IsFinite(spacingMillimeters) || !double.IsFinite(edgeOffsetMillimeters) ||
                !double.IsFinite(pipeDiameterMillimeters) || !double.IsFinite(levelElevationFeet) ||
                !double.IsFinite(layingHeightMillimeters) || spacingMillimeters <= pipeDiameterMillimeters ||
                edgeOffsetMillimeters < 0 || pipeDiameterMillimeters <= 0 ||
                !IsFinite(startConnection) || !IsFinite(endConnection) ||
                !IsFinite(startDirection) || !IsFinite(endDirection) ||
                Math.Sqrt(startDirection.X * startDirection.X + startDirection.Y * startDirection.Y) <= Epsilon ||
                Math.Sqrt(endDirection.X * endDirection.X + endDirection.Y * endDirection.Y) <= Epsilon ||
                boundaryLoops.Any(loop => loop == null || loop.Count < 3 || loop.Any(point => !IsFinite(point))))
                return new List<XYZ>();

            var layouts = new List<OrientationLayout>();
            foreach (OrientationCandidate orientation in GetOrientationCandidates(boundaryLoops, spacingMillimeters))
            {
                IList<XYZ> candidate = CreateForOrientation(boundaryLoops, spacingMillimeters,
                    edgeOffsetMillimeters, pipeDiameterMillimeters, levelElevationFeet,
                    layingHeightMillimeters, startConnection, startDirection, endConnection,
                    endDirection, orientation.Angle, out double coverage);
                if (candidate.Count > 0)
                    layouts.Add(new OrientationLayout(candidate, coverage, orientation.BoundarySupport));
            }

            if (layouts.Count == 0)
                return new List<XYZ>();

            OrientationLayout selected = layouts.OrderByDescending(layout => layout.Coverage)
                .ThenByDescending(layout => layout.BoundarySupport)
                .First();
            return selected.Path;
        }

        private static IList<XYZ> CreateForOrientation(IList<IList<XYZ>> boundaryLoops,
            double spacingMillimeters, double edgeOffsetMillimeters, double pipeDiameterMillimeters,
            double levelElevationFeet, double layingHeightMillimeters,
            XYZ startConnection, XYZ startDirection, XYZ endConnection, XYZ endDirection,
            double angle, out double overallCoverage)
        {
            overallCoverage = 0;
            var loops = boundaryLoops.Select(loop => CleanLoop(loop.Select(point => ToLocal(point, angle))))
                .Where(loop => loop.Count >= 3).ToList();
            if (loops.Count == 0)
                return new List<XYZ>();

            var start = ToLocal(startConnection, angle);
            var end = ToLocal(endConnection, angle);
            var startDir = Normalize(ToLocalVector(startDirection, angle));
            var endDir = Normalize(ToLocalVector(endDirection, angle));
            double nominalOffset = Math.Max(0, edgeOffsetMillimeters);
            double[] offsetFactors = nominalOffset > Epsilon
                ? new[] { 0.9, 1.0, 1.1 }
                : new[] { 1.0 };
            IList<XYZ> bestResult = new List<XYZ>();
            double bestCoverage = double.NegativeInfinity;
            double bestEdgeExcess = double.PositiveInfinity;
            double bestDeviation = double.PositiveInfinity;
            double maximumWallOffset = nominalOffset > Epsilon ? nominalOffset * 1.15 : 15.0;
            double edgeExcessTolerance = Math.Max(5.0, nominalOffset * 0.05);

            foreach (double offsetFactor in offsetFactors)
            {
                double edgeOffset = nominalOffset * offsetFactor;
                double clearance = edgeOffset + pipeDiameterMillimeters / 2.0;
                double allowedEdgeExcess = Math.Max(0, maximumWallOffset - edgeOffset);
                IList<XYZ> candidate = TryCreateCandidate(loops, spacingMillimeters, edgeOffset,
                    pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                    startConnection, endConnection, start, startDir, end, endDir, angle,
                    out double coverage, out double edgeExcess);
                if (candidate.Count == 0)
                    continue;

                double deviation = Math.Abs(edgeOffset - nominalOffset);
                bool candidateWithinOffset = edgeExcess <= allowedEdgeExcess;
                bool previousWithinOffset = bestEdgeExcess <= allowedEdgeExcess;
                bool better;
                if (bestResult.Count == 0)
                {
                    better = true;
                }
                else if (candidateWithinOffset != previousWithinOffset)
                {
                    better = bestResult.Count == 0 || candidateWithinOffset;
                }
                else if (!candidateWithinOffset &&
                    Math.Abs(edgeExcess - bestEdgeExcess) > edgeExcessTolerance)
                {
                    better = edgeExcess < bestEdgeExcess;
                }
                else
                {
                    double coverageTolerance = Math.Max(1e-8,
                        Math.Max(Math.Abs(coverage), Math.Abs(bestCoverage)) * 0.01);
                    better = coverage > bestCoverage + coverageTolerance ||
                        (Math.Abs(coverage - bestCoverage) <= coverageTolerance && deviation < bestDeviation);
                }
                if (better)
                {
                    bestResult = candidate;
                    bestCoverage = coverage;
                    bestEdgeExcess = edgeExcess;
                    bestDeviation = deviation;
                }
            }

            overallCoverage = bestCoverage;
            return bestResult;
        }

        private static List<OrientationCandidate> GetOrientationCandidates(IList<IList<XYZ>> loops,
            double spacingMillimeters)
        {
            const double binSizeRadians = Math.PI / 360.0;
            const double angleToleranceRadians = Math.PI / 90.0;
            double minimumEdgeLength = Math.Max(100.0, spacingMillimeters * 1.5);
            var bins = new Dictionary<int, AngleBin>();

            foreach (IList<XYZ> loop in loops)
            {
                if (loop == null || loop.Count < 2)
                    continue;
                for (int i = 0; i < loop.Count; i++)
                {
                    XYZ first = loop[i];
                    XYZ second = loop[(i + 1) % loop.Count];
                    double dx = (second.X - first.X) * MillimetersPerFoot;
                    double dy = (second.Y - first.Y) * MillimetersPerFoot;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length < minimumEdgeLength)
                        continue;

                    double angle = Math.Atan2(dy, dx) % (Math.PI / 2.0);
                    if (angle < 0) angle += Math.PI / 2.0;
                    int key = (int)Math.Round(angle / binSizeRadians);
                    int binCount = (int)Math.Round((Math.PI / 2.0) / binSizeRadians);
                    key %= binCount;
                    if (!bins.TryGetValue(key, out AngleBin bin))
                        bin = new AngleBin();
                    bin.Weight += length;
                    bin.Cosine += length * Math.Cos(4.0 * angle);
                    bin.Sine += length * Math.Sin(4.0 * angle);
                    bins[key] = bin;
                }
            }

            var candidates = new List<OrientationCandidate>
            {
                new OrientationCandidate(DominantAngle(loops), 0)
            };
            foreach (AngleBin bin in bins.Values.OrderByDescending(value => value.Weight))
            {
                double angle = Math.Atan2(bin.Sine, bin.Cosine) / 4.0;
                if (angle < 0) angle += Math.PI / 2.0;
                if (candidates.Any(existing => OrientationDistance(existing.Angle, angle) < angleToleranceRadians))
                    continue;
                double axisTolerance = Math.PI / 90.0;
                double distanceFromAxis = Math.Min(angle, Math.PI / 2.0 - angle);
                double obliqueSupport = distanceFromAxis > axisTolerance ? bin.Weight : 0;
                candidates.Add(new OrientationCandidate(angle, obliqueSupport));
                if (candidates.Count >= 4)
                    break;
            }

            return candidates;
        }

        private static double OrientationDistance(double first, double second)
        {
            double difference = Math.Abs(first - second) % (Math.PI / 2.0);
            return Math.Min(difference, Math.PI / 2.0 - difference);
        }

        private static List<AxisCellRange> BuildAxisCellRanges(IList<List<Point2>> loops,
            double nominalPitch, double pipeDiameter, double clearance, bool horizontal)
        {
            double min = horizontal
                ? loops.SelectMany(loop => loop).Min(point => point.X)
                : loops.SelectMany(loop => loop).Min(point => point.Y);
            double max = horizontal
                ? loops.SelectMany(loop => loop).Max(point => point.X)
                : loops.SelectMany(loop => loop).Max(point => point.Y);
            double safeMin = min + clearance;
            double safeMax = max - clearance;
            // Local span lengths around openings can differ from the room bounds by only a
            // few millimetres. Use the same 15% tuning envelope as the setback search so
            // exact wall-aligned pipe runs remain possible without leaving large gaps.
            double minPitch = Math.Max(nominalPitch * 0.85, pipeDiameter + 0.1);
            double maxPitch = nominalPitch * 1.15;
            if (safeMax - safeMin < minPitch || maxPitch < minPitch)
                return new List<AxisCellRange>();

            var anchors = new List<AxisAnchor>
            {
                new AxisAnchor(safeMin, 0),
                new AxisAnchor(safeMax, 1)
            };
            double minimumWallLength = Math.Max(300.0, nominalPitch * 1.5);
            foreach (List<Point2> loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                Point2 a = loop[i];
                Point2 b = loop[(i + 1) % loop.Count];
                Point2 edge = b - a;
                double length = edge.Length;
                if (length < minimumWallLength)
                    continue;
                if (horizontal ? Math.Abs(edge.X) > length * 0.02 : Math.Abs(edge.Y) > length * 0.02)
                    continue;

                Point2 midpoint = (a + b) / 2.0;
                Point2 normal = new Point2(-edge.Y / length, edge.X / length);
                double probe = Math.Min(10.0, Math.Max(1.0, clearance * 0.1));
                bool positiveSideInside = IsInside(loops, midpoint + normal * probe);
                bool negativeSideInside = IsInside(loops, midpoint - normal * probe);
                if (positiveSideInside == negativeSideInside)
                    continue;
                Point2 inward = positiveSideInside ? normal : normal * -1.0;
                double coordinate = horizontal
                    ? midpoint.X + inward.X * clearance
                    : midpoint.Y + inward.Y * clearance;
                if (coordinate > safeMin + 0.5 && coordinate < safeMax - 0.5)
                {
                    double inwardAxis = horizontal ? inward.X : inward.Y;
                    anchors.Add(new AxisAnchor(coordinate, inwardAxis > 0 ? 0 : 1));
                }
            }

            anchors.Sort((first, second) => first.Position.CompareTo(second.Position));
            var uniqueAnchors = new List<AxisAnchor>();
            foreach (AxisAnchor anchor in anchors)
            {
                if (uniqueAnchors.Count == 0 || anchor.Position - uniqueAnchors[uniqueAnchors.Count - 1].Position > 1.0)
                {
                    uniqueAnchors.Add(anchor);
                    continue;
                }

                if (anchor.Parity != uniqueAnchors[uniqueAnchors.Count - 1].Parity)
                    return new List<AxisCellRange>();
            }
            if (uniqueAnchors.Count < 2)
                return new List<AxisCellRange>();

            var spans = new List<AxisSpan>();
            for (int i = 0; i + 1 < uniqueAnchors.Count; i++)
            {
                AxisAnchor startAnchor = uniqueAnchors[i];
                AxisAnchor endAnchor = uniqueAnchors[i + 1];
                double length = endAnchor.Position - startAnchor.Position;
                int minimumCount = Math.Max(1, (int)Math.Ceiling(length / maxPitch - 1e-9));
                int maximumCount = (int)Math.Floor(length / minPitch + 1e-9);
                // A start-side anchor (parity 0) owns the first cell to its right; an
                // end-side anchor (parity 1) owns the final cell to its left. The other side
                // may start a new local pairing, which avoids forcing a whole extra interval
                // through every narrow opening merely to preserve a global grid phase.
                int requiredCountParity = endAnchor.Parity == 1
                    ? (startAnchor.Parity == 0 ? 1 : 0)
                    : -1;
                int count = minimumCount <= maximumCount
                    ? Enumerable.Range(minimumCount, maximumCount - minimumCount + 1)
                        .Where(candidate => requiredCountParity < 0 || candidate % 2 == requiredCountParity)
                        .OrderBy(candidate => Math.Abs(length / candidate - nominalPitch))
                        .FirstOrDefault()
                    : 0;

                // Parity can make an otherwise valid narrow span impossible. In that case,
                // keep the maximum spacing bounded to +15% and add tighter runs rather than
                // dropping an entire wall/opening offset row and leaving a cold strip.
                if (count == 0)
                {
                    double denseMinimumPitch = Math.Max(pipeDiameter + 0.1, nominalPitch * 0.85);
                    int denseMaximumCount = (int)Math.Floor(length / denseMinimumPitch + 1e-9);
                    if (minimumCount > denseMaximumCount)
                        return new List<AxisCellRange>();
                    count = Enumerable.Range(minimumCount, denseMaximumCount - minimumCount + 1)
                        .Where(candidate => requiredCountParity < 0 || candidate % 2 == requiredCountParity)
                        .OrderBy(candidate => Math.Abs(length / candidate - nominalPitch))
                        .FirstOrDefault();
                }
                if (count == 0)
                    return new List<AxisCellRange>();
                spans.Add(new AxisSpan(startAnchor.Position, endAnchor.Position, count));
            }

            var result = new List<AxisCellRange>();
            for (int spanIndex = 0; spanIndex < spans.Count; spanIndex++)
            {
                AxisSpan span = spans[spanIndex];
                int pairingPhase = uniqueAnchors[spanIndex].Parity;
                var coordinates = new double[span.Count + 1];
                coordinates[0] = span.Start;
                for (int i = 1; i <= span.Count; i++)
                    coordinates[i] = i == span.Count
                        ? span.End
                        : span.Start + span.Length * i / span.Count;

                for (int i = pairingPhase; i + 1 <= span.Count; i += 2)
                    result.Add(new AxisCellRange(coordinates[i], coordinates[i + 1]));
            }
            return result;
        }

        private static bool IsFinite(XYZ point) => point != null &&
            double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);

        private static IList<XYZ> TryCreateCandidate(IList<List<Point2>> loops,
            double spacingMillimeters, double edgeOffsetMillimeters, double pipeDiameterMillimeters,
            double levelElevationFeet, double layingHeightMillimeters,
            XYZ startConnection, XYZ endConnection, Point2 start, Point2 startDir,
            Point2 end, Point2 endDir, double angle,
            out double coverage, out double edgeExcess)
        {
            coverage = 0;
            edgeExcess = double.PositiveInfinity;
            double clearance = edgeOffsetMillimeters + pipeDiameterMillimeters / 2.0;
            double pitch = spacingMillimeters;

            List<GridCell> cells = BuildCells(loops, spacingMillimeters,
                pipeDiameterMillimeters, clearance, out edgeExcess);
            if (cells.Count == 0)
                return new List<XYZ>();

            GridCell entry = null;
            int entrySide = -1;
            double entryScore = double.PositiveInfinity;
            foreach (GridCell cell in cells)
            {
                for (int side = 0; side < 4; side++)
                {
                    Point2 normal = SideNormal(side);
                    Point2 midpoint = SideMidpoint(cell, side);
                    double edgeDepth = DistanceToBoundary(loops, midpoint);
                    double facing = Dot(start - midpoint, normal);
                    if (edgeDepth > clearance + spacingMillimeters * 1.1 || facing <= 0)
                        continue;

                    double score = midpoint.DistanceSquaredTo(start) - facing * 0.1;
                    if (score < entryScore)
                    {
                        entry = cell;
                        entrySide = side;
                        entryScore = score;
                    }
                }
            }
            if (entry == null)
                return new List<XYZ>();

            BuildAdjacency(cells);
            BuildSpanningTree(entry, loops, clearance, pitch);
            HashSet<GridCell> component = GetTreeComponent(entry);
            if (component.Count != cells.Count)
                return new List<XYZ>();

            List<Point2> loopPath = TraceCellContour(entry, entrySide, component);
            if (loopPath == null || loopPath.Count < 2)
                return new List<XYZ>();
            coverage = ComputeCoverageQuality(loops, loopPath, clearance, pitch);
            edgeExcess = Math.Max(edgeExcess, ComputeBoundaryOffsetExcess(loops, loopPath,
                edgeOffsetMillimeters, pipeDiameterMillimeters, pitch));

            List<Point2> bestPath = null;
            double bestScore = double.PositiveInfinity;
            List<List<Point2>> innerLoops = GetInnerLoops(loops);
            for (int orientation = 0; orientation < 2; orientation++)
            {
                List<Point2> orientedLoop = new List<Point2>(loopPath);
                if (orientation == 1)
                    orientedLoop.Reverse();

                bool loopIsSafe = true;
                for (int i = 0; i + 1 < orientedLoop.Count; i++)
                {
                    if (IsSafeSegment(loops, orientedLoop[i], orientedLoop[i + 1], clearance))
                        continue;
                    loopIsSafe = false;
                    break;
                }
                if (!loopIsSafe)
                    continue;

                Point2 loopStart = orientedLoop[0];
                Point2 loopEnd = orientedLoop[orientedLoop.Count - 1];
                List<List<Point2>> startRoutes = BuildConnectorRoutes(loopStart, start,
                    startDir, orientedLoop, atStart: true, innerLoops, clearance,
                    pipeDiameterMillimeters, pitch);
                List<List<Point2>> endRoutes = BuildConnectorRoutes(loopEnd, end,
                    endDir, orientedLoop, atStart: false, innerLoops, clearance,
                    pipeDiameterMillimeters, pitch);
                foreach (List<Point2> fromStart in startRoutes)
                foreach (List<Point2> toEnd in endRoutes)
                {
                    var candidate = new List<Point2>();
                    for (int i = fromStart.Count - 1; i >= 0; i--)
                        AddDistinct(candidate, fromStart[i]);
                    foreach (Point2 point in orientedLoop)
                        AddDistinct(candidate, point);
                    for (int i = 1; i < toEnd.Count; i++)
                        AddDistinct(candidate, toEnd[i]);

                    if (candidate.Count < 2 ||
                        candidate.Zip(candidate.Skip(1), (first, second) => first.DistanceTo(second))
                            .Any(length => length < 0.1 * 25.4))
                        continue;
                    int crossings = CountCrossings(candidate);
                    if (crossings > 0)
                        continue;

                    double score = PolylineLength(fromStart) + PolylineLength(toEnd) + candidate.Count;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPath = candidate;
                    }
                }
            }

            if (bestPath == null || CountCrossings(bestPath) > 0)
                return new List<XYZ>();

            double heightOffsetFeet = layingHeightMillimeters / MillimetersPerFoot;
            var result = new List<XYZ>(bestPath.Count);
            for (int i = 0; i < bestPath.Count; i++)
            {
                Point2 world = ToWorld(bestPath[i], angle);
                double z = i == 0
                    ? startConnection.Z - heightOffsetFeet
                    : i == bestPath.Count - 1
                        ? endConnection.Z - heightOffsetFeet
                        : levelElevationFeet;
                AddDistinct(result, new XYZ(world.X / MillimetersPerFoot,
                    world.Y / MillimetersPerFoot, z));
            }
            return result;
        }

        private static List<List<Point2>> BuildConnectorRoutes(Point2 pipePoint, Point2 connection,
            Point2 connectionDirection, IList<Point2> coilPath, bool atStart, IList<List<Point2>> innerLoops,
            double clearance, double pipeDiameter, double pitch)
        {
            double minX = coilPath.Min(point => point.X);
            double maxX = coilPath.Max(point => point.X);
            double minY = coilPath.Min(point => point.Y);
            double maxY = coilPath.Max(point => point.Y);
            double minimumGap = Math.Max(10.0, pipeDiameter + 2.0);
            double laneStep = Math.Max(30.0, Math.Min(pitch * 0.5, 100.0));
            Point2 approach = connection + Normalize(connectionDirection) * Math.Max(25.0, pipeDiameter * 2.0);
            var candidates = new List<List<Point2>>();

            AddRoute(new[] { pipePoint, approach, connection });
            AddRoute(new[] { pipePoint, new Point2(pipePoint.X, approach.Y), approach, connection });
            AddRoute(new[] { pipePoint, new Point2(approach.X, pipePoint.Y), approach, connection });
            for (int lane = 0; lane < 4; lane++)
            {
                double margin = minimumGap + lane * laneStep;
                double left = minX - margin;
                double right = maxX + margin;
                double bottom = minY - margin;
                double top = maxY + margin;
                AddRoute(new[] { pipePoint, new Point2(pipePoint.X, top),
                    new Point2(approach.X, top), approach, connection });
                AddRoute(new[] { pipePoint, new Point2(pipePoint.X, bottom),
                    new Point2(approach.X, bottom), approach, connection });
                AddRoute(new[] { pipePoint, new Point2(left, pipePoint.Y),
                    new Point2(left, approach.Y), approach, connection });
                AddRoute(new[] { pipePoint, new Point2(right, pipePoint.Y),
                    new Point2(right, approach.Y), approach, connection });
            }

            return candidates
                .OrderBy(PolylineLength)
                .Take(12)
                .ToList();

            void AddRoute(IEnumerable<Point2> points)
            {
                var route = new List<Point2>();
                foreach (Point2 point in points)
                    AddDistinct(route, point);
                if (route.Count < 2 || route.Zip(route.Skip(1),
                        (first, second) => first.DistanceTo(second)).Any(length => length < 0.1 * 25.4))
                    return;
                Point2 connectorSegment = route[route.Count - 1] - route[route.Count - 2];
                if (Math.Abs(Dot(Normalize(connectorSegment), Normalize(connectionDirection))) < 0.7)
                    return;
                bool innerSafe = IsSafeFromInnerBoundaries(route, innerLoops, clearance);
                bool coilSafe = IsConnectorClearOfCoil(route, coilPath, atStart, minimumGap);
                if (!innerSafe || !coilSafe)
                    return;
                if (candidates.Any(existing => existing.Count == route.Count &&
                    existing.Zip(route, (first, second) => first.DistanceTo(second)).All(distance => distance < 0.1)))
                    return;
                candidates.Add(route);
            }
        }

        private static bool IsConnectorClearOfCoil(IList<Point2> route, IList<Point2> coilPath,
            bool atStart, double minimumGap)
        {
            double minimumGapSquared = minimumGap * minimumGap;
            for (int i = 0; i + 1 < route.Count; i++)
            for (int j = 0; j + 1 < coilPath.Count; j++)
            {
                if (atStart && i == 0 && j == 0 || !atStart && i == 0 && j == coilPath.Count - 2)
                    continue;
                if (SegmentDistanceSquared(route[i], route[i + 1], coilPath[j], coilPath[j + 1]) <
                    minimumGapSquared - 1e-6)
                    return false;
            }
            return true;
        }

        private static List<GridCell> BuildCells(IList<List<Point2>> loops,
            double nominalPitch, double pipeDiameter, double clearance, out double edgeExcess)
        {
            edgeExcess = double.PositiveInfinity;
            double minX = loops.SelectMany(loop => loop).Min(point => point.X);
            double maxX = loops.SelectMany(loop => loop).Max(point => point.X);
            double minY = loops.SelectMany(loop => loop).Min(point => point.Y);
            double maxY = loops.SelectMany(loop => loop).Max(point => point.Y);
            List<AxisCellRange> columns = BuildAxisCellRanges(loops, nominalPitch,
                pipeDiameter, clearance, horizontal: true);
            List<AxisCellRange> rows = BuildAxisCellRanges(loops, nominalPitch,
                pipeDiameter, clearance, horizontal: false);
            if (columns.Count == 0 || rows.Count == 0)
                return new List<GridCell>();
            if ((long)columns.Count * rows.Count > MaximumCells)
                return new List<GridCell>();

            var result = new List<GridCell>();
            var byKey = new Dictionary<CellKey, GridCell>();

            for (int row = 0; row < rows.Count; row++)
            {
                for (int column = 0; column < columns.Count; column++)
                {
                    AxisCellRange xRange = columns[column];
                    AxisCellRange yRange = rows[row];
                    var cell = new GridCell(column, row, xRange.Min, yRange.Min, xRange.Max, yRange.Max);
                    if (!IsCellSafe(cell, loops, clearance))
                        continue;
                    cell.Id = result.Count;
                    result.Add(cell);
                    byKey.Add(new CellKey(column, row), cell);
                }
            }

            if (result.Count == 0)
                return result;

            foreach (GridCell cell in result)
            {
                byKey.TryGetValue(new CellKey(cell.Column + 1, cell.Row), out GridCell right);
                byKey.TryGetValue(new CellKey(cell.Column - 1, cell.Row), out GridCell left);
                byKey.TryGetValue(new CellKey(cell.Column, cell.Row + 1), out GridCell up);
                byKey.TryGetValue(new CellKey(cell.Column, cell.Row - 1), out GridCell down);
                if (right != null && IsConnectorSafe(cell, right, loops, clearance, horizontal: true))
                {
                    cell.Right = right;
                    right.Left = cell;
                }
                if (up != null && IsConnectorSafe(cell, up, loops, clearance, horizontal: false))
                {
                    cell.Up = up;
                    up.Down = cell;
                }
            }

            double firstCellX = result.Min(cell => cell.MinX);
            double lastCellX = result.Max(cell => cell.MaxX);
            double firstCellY = result.Min(cell => cell.MinY);
            double lastCellY = result.Max(cell => cell.MaxY);
            edgeExcess = Math.Max(
                Math.Max(firstCellX - (minX + clearance), maxX - clearance - lastCellX),
                Math.Max(firstCellY - (minY + clearance), maxY - clearance - lastCellY));
            return result;
        }

        private static bool IsConnectorSafe(GridCell first, GridCell second,
            IList<List<Point2>> loops, double clearance, bool horizontal)
        {
            double minX = horizontal ? first.MaxX : first.MinX;
            double maxX = horizontal ? second.MinX : first.MaxX;
            double minY = horizontal ? first.MinY : first.MaxY;
            double maxY = horizontal ? first.MaxY : second.MinY;
            Point2 a = new Point2(minX, minY);
            Point2 b = new Point2(maxX, minY);
            Point2 c = new Point2(maxX, maxY);
            Point2 d = new Point2(minX, maxY);
            return IsSafeSegment(loops, a, b, clearance) &&
                   IsSafeSegment(loops, b, c, clearance) &&
                   IsSafeSegment(loops, c, d, clearance) &&
                   IsSafeSegment(loops, d, a, clearance);
        }

        private static bool IsCellSafe(GridCell cell, IList<List<Point2>> loops, double clearance)
        {
            Point2 bl = new Point2(cell.MinX, cell.MinY);
            Point2 br = new Point2(cell.MaxX, cell.MinY);
            Point2 tr = new Point2(cell.MaxX, cell.MaxY);
            Point2 tl = new Point2(cell.MinX, cell.MaxY);
            return IsSafeSegment(loops, bl, br, clearance) &&
                   IsSafeSegment(loops, br, tr, clearance) &&
                   IsSafeSegment(loops, tr, tl, clearance) &&
                   IsSafeSegment(loops, tl, bl, clearance);
        }

        private static void BuildAdjacency(IEnumerable<GridCell> cells)
        {
            foreach (GridCell cell in cells)
            {
                if (cell.Right != null) cell.Neighbors.Add(cell.Right);
                if (cell.Up != null) cell.Neighbors.Add(cell.Up);
                if (cell.Left != null) cell.Neighbors.Add(cell.Left);
                if (cell.Down != null) cell.Neighbors.Add(cell.Down);
            }
        }

        private static void BuildSpanningTree(GridCell entry, IList<List<Point2>> loops,
            double clearance, double pitch)
        {
            var visited = new HashSet<GridCell> { entry };
            var stack = new Stack<GridCell>();
            stack.Push(entry);
            entry.Layer = (int)Math.Floor((DistanceToBoundary(loops, entry.Center) - clearance) / (2 * pitch));

            while (stack.Count > 0)
            {
                GridCell current = stack.Peek();
                GridCell parent = current.Parent;
                Point2 incoming = parent == null ? default : Normalize(current.Center - parent.Center);
                GridCell next = current.Neighbors
                    .Where(candidate => !visited.Contains(candidate))
                    .Select(candidate =>
                    {
                        candidate.Layer = (int)Math.Floor(
                            (DistanceToBoundary(loops, candidate.Center) - clearance) / (2 * pitch));
                        Point2 direction = Normalize(candidate.Center - current.Center);
                        int layerClass = candidate.Layer == current.Layer ? 0 :
                            candidate.Layer > current.Layer ? 1 : 2;
                        int straight = parent != null && Dot(incoming, direction) > 0.99 ? 0 : 1;
                        Point2 right = new Point2(direction.Y, -direction.X);
                        double probeDepth = DistanceToBoundary(loops, current.Center + right * pitch);
                        double currentDepth = DistanceToBoundary(loops, current.Center);
                        int rightRank = probeDepth < currentDepth - 40 ? 0 :
                            probeDepth < currentDepth + 40 ? 1 : 2;
                        return new { Cell = candidate, Layer = layerClass, Straight = straight, Right = rightRank };
                    })
                    .OrderBy(item => item.Layer)
                    .ThenBy(item => item.Straight)
                    .ThenBy(item => item.Right)
                    .ThenBy(item => item.Cell.Id)
                    .Select(item => item.Cell)
                    .FirstOrDefault();

                if (next == null)
                {
                    stack.Pop();
                    continue;
                }

                next.Parent = current;
                visited.Add(next);
                stack.Push(next);
            }
        }

        private static HashSet<GridCell> GetTreeComponent(GridCell entry)
        {
            var component = new HashSet<GridCell>();
            var stack = new Stack<GridCell>();
            stack.Push(entry);
            while (stack.Count > 0)
            {
                GridCell current = stack.Pop();
                if (!component.Add(current))
                    continue;
                foreach (GridCell neighbor in current.Neighbors)
                {
                    if (neighbor.Parent == current || current.Parent == neighbor)
                        stack.Push(neighbor);
                }
            }
            return component;
        }

        private static List<Point2> TraceCellContour(GridCell entry, int entrySide,
            HashSet<GridCell> component)
        {
            var next = new Dictionary<State, State>();
            foreach (GridCell cell in component)
            {
                if (!IsTreeEdge(cell, cell.Down))
                    next[new State(cell, 0)] = new State(cell, 1);

                if (cell.Right != null && IsTreeEdge(cell, cell.Right))
                {
                    next[new State(cell, 1)] = new State(cell.Right, 0);
                    next[new State(cell.Right, 3)] = new State(cell, 2);
                }
                else
                    next[new State(cell, 1)] = new State(cell, 2);

                if (cell.Up != null && IsTreeEdge(cell, cell.Up))
                {
                    next[new State(cell, 2)] = new State(cell.Up, 1);
                    next[new State(cell.Up, 0)] = new State(cell, 3);
                }
                else
                    next[new State(cell, 2)] = new State(cell, 3);

                if (!IsTreeEdge(cell, cell.Left))
                    next[new State(cell, 3)] = new State(cell, 0);
            }

            var start = new State(entry, (entrySide + 1) % 4);
            var finish = new State(entry, entrySide);
            var nodes = new List<State> { start };
            State current = start;
            int guard = 0;
            while (!current.Equals(finish))
            {
                if (!next.TryGetValue(current, out current))
                    return null;
                nodes.Add(current);
                if (++guard > 8 * component.Count + 10)
                    return null;
            }

            var path = new List<Point2>();
            foreach (State node in nodes)
                AddDistinct(path, Corner(node.Cell, node.Side));
            return path;
        }

        private static bool IsTreeEdge(GridCell first, GridCell second) =>
            first != null && second != null && (first.Parent == second || second.Parent == first);

        private static Point2 Corner(GridCell cell, int cornerIndex) => cornerIndex switch
        {
            0 => new Point2(cell.MinX, cell.MinY),
            1 => new Point2(cell.MaxX, cell.MinY),
            2 => new Point2(cell.MaxX, cell.MaxY),
            _ => new Point2(cell.MinX, cell.MaxY)
        };

        private static Point2 SideNormal(int side) => side switch
        {
            0 => new Point2(0, -1),
            1 => new Point2(1, 0),
            2 => new Point2(0, 1),
            _ => new Point2(-1, 0)
        };

        private static Point2 SideMidpoint(GridCell cell, int side) => side switch
        {
            0 => new Point2((cell.MinX + cell.MaxX) / 2, cell.MinY),
            1 => new Point2(cell.MaxX, (cell.MinY + cell.MaxY) / 2),
            2 => new Point2((cell.MinX + cell.MaxX) / 2, cell.MaxY),
            _ => new Point2(cell.MinX, (cell.MinY + cell.MaxY) / 2)
        };

        private static bool IsSafeSegment(IList<List<Point2>> loops, Point2 start, Point2 end,
            double clearance)
        {
            double minimumDistanceSquared = clearance * clearance;
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    Point2 boundaryStart = loop[i];
                    Point2 boundaryEnd = loop[(i + 1) % loop.Count];
                    if (SegmentDistanceSquared(start, end, boundaryStart, boundaryEnd) <
                        minimumDistanceSquared - 1e-6)
                        return false;
                }
            }

            int steps = Math.Max(1, (int)Math.Ceiling(start.DistanceTo(end) / 50.0));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                Point2 point = start + (end - start) * t;
                if (!IsSafePoint(loops, point, clearance))
                    return false;
            }
            return true;
        }

        private static List<List<Point2>> GetInnerLoops(IList<List<Point2>> loops)
        {
            var innerLoops = new List<List<Point2>>();
            for (int i = 0; i < loops.Count; i++)
            {
                Point2 representative = loops[i][0];
                for (int other = 0; other < loops.Count; other++)
                {
                    if (other != i && IsPointInsideLoop(loops[other], representative))
                    {
                        innerLoops.Add(loops[i]);
                        break;
                    }
                }
            }
            return innerLoops;
        }

        private static bool IsSafeFromInnerBoundaries(IList<Point2> path,
            IList<List<Point2>> innerLoops, double clearance)
        {
            double minimumDistanceSquared = clearance * clearance;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                foreach (List<Point2> loop in innerLoops)
                {
                    for (int j = 0; j < loop.Count; j++)
                    {
                        if (SegmentDistanceSquared(path[i], path[i + 1], loop[j],
                            loop[(j + 1) % loop.Count]) < minimumDistanceSquared - 1e-6)
                            return false;
                    }
                }
            }
            return true;
        }

        private static double ComputeBoundaryOffsetExcess(IList<List<Point2>> loops,
            IList<Point2> path, double edgeOffset, double pipeDiameter, double pitch)
        {
            double maximumExtra = 0;
            double minimumWallLength = Math.Max(300.0, pitch * 2.0);
            double parallelTolerance = Math.Sin(2.0 * Math.PI / 180.0);
            foreach (List<Point2> loop in loops)
            for (int edge = 0; edge < loop.Count; edge++)
            {
                Point2 wallStart = loop[edge];
                Point2 wallEnd = loop[(edge + 1) % loop.Count];
                Point2 wallVector = wallEnd - wallStart;
                double wallLength = wallVector.Length;
                if (wallLength < minimumWallLength)
                    continue;
                Point2 wallDirection = wallVector / wallLength;
                double nearestDistance = double.PositiveInfinity;

                for (int i = 0; i + 1 < path.Count; i++)
                {
                    Point2 pipeStart = path[i];
                    Point2 pipeEnd = path[i + 1];
                    Point2 pipeVector = pipeEnd - pipeStart;
                    double pipeLength = pipeVector.Length;
                    if (pipeLength < Math.Max(25.0, pitch * 0.5))
                        continue;
                    Point2 pipeDirection = pipeVector / pipeLength;
                    if (Math.Abs(Cross(wallDirection, pipeDirection)) > parallelTolerance)
                        continue;

                    double firstProjection = Dot(pipeStart - wallStart, wallDirection);
                    double secondProjection = Dot(pipeEnd - wallStart, wallDirection);
                    double overlap = Math.Min(wallLength, Math.Max(firstProjection, secondProjection)) -
                        Math.Max(0, Math.Min(firstProjection, secondProjection));
                    if (overlap < Math.Min(50.0, wallLength * 0.1))
                        continue;

                    double distance = Math.Abs(Cross(pipeStart - wallStart, wallDirection));
                    nearestDistance = Math.Min(nearestDistance, distance);
                }

                if (double.IsFinite(nearestDistance))
                {
                    double wallGap = nearestDistance - pipeDiameter / 2.0;
                    maximumExtra = Math.Max(maximumExtra, wallGap - edgeOffset);
                }
            }

            return maximumExtra;
        }

        private static double ComputeCoverageQuality(IList<List<Point2>> loops,
            IList<Point2> path, double clearance, double pitch)
        {
            if (path.Count < 2)
                return 0;

            double minX = loops.SelectMany(loop => loop).Min(point => point.X);
            double maxX = loops.SelectMany(loop => loop).Max(point => point.X);
            double minY = loops.SelectMany(loop => loop).Min(point => point.Y);
            double maxY = loops.SelectMany(loop => loop).Max(point => point.Y);
            double sampleStep = Math.Max(50.0, Math.Min(100.0, pitch * 0.5));
            double maximumGap = 0;

            for (double x = minX + clearance; x <= maxX - clearance; x += sampleStep)
            for (double y = minY + clearance; y <= maxY - clearance; y += sampleStep)
            {
                Point2 sample = new Point2(x, y);
                if (!IsInside(loops, sample) || DistanceToBoundary(loops, sample) < clearance)
                    continue;

                double nearestSquared = double.PositiveInfinity;
                for (int i = 0; i + 1 < path.Count; i++)
                    nearestSquared = Math.Min(nearestSquared,
                        DistanceToSegmentSquared(sample, path[i], path[i + 1]));
                maximumGap = Math.Max(maximumGap, Math.Sqrt(nearestSquared));
            }

            return 1.0 / (1.0 + maximumGap);
        }

        private static double SegmentDistanceSquared(Point2 firstStart, Point2 firstEnd,
            Point2 secondStart, Point2 secondEnd)
        {
            if (SegmentsCross(firstStart, firstEnd, secondStart, secondEnd))
                return 0;
            return Math.Min(
                Math.Min(DistanceToSegmentSquared(firstStart, secondStart, secondEnd),
                    DistanceToSegmentSquared(firstEnd, secondStart, secondEnd)),
                Math.Min(DistanceToSegmentSquared(secondStart, firstStart, firstEnd),
                    DistanceToSegmentSquared(secondEnd, firstStart, firstEnd)));
        }

        private static bool IsSafePoint(IList<List<Point2>> loops, Point2 point, double clearance)
        {
            bool inside = false;
            double minDistanceSquared = double.PositiveInfinity;
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    Point2 a = loop[i];
                    Point2 b = loop[(i + 1) % loop.Count];
                    if ((a.Y > point.Y) != (b.Y > point.Y) &&
                        point.X < a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
                        inside = !inside;
                    minDistanceSquared = Math.Min(minDistanceSquared, DistanceToSegmentSquared(point, a, b));
                }
            }
            return inside && minDistanceSquared >= clearance * clearance;
        }

        private static double DistanceToBoundary(IList<List<Point2>> loops, Point2 point)
        {
            double min = double.PositiveInfinity;
            foreach (List<Point2> loop in loops)
                for (int i = 0; i < loop.Count; i++)
                    min = Math.Min(min, Math.Sqrt(DistanceToSegmentSquared(point,
                        loop[i], loop[(i + 1) % loop.Count])));
            return IsInside(loops, point) ? min : -min;
        }

        private static bool IsInside(IList<List<Point2>> loops, Point2 point)
        {
            bool inside = false;
            foreach (List<Point2> loop in loops)
                if (IsPointInsideLoop(loop, point))
                    inside = !inside;
            return inside;
        }

        private static bool IsPointInsideLoop(IList<Point2> loop, Point2 point)
        {
            bool inside = false;
            for (int i = 0; i < loop.Count; i++)
            {
                Point2 a = loop[i];
                Point2 b = loop[(i + 1) % loop.Count];
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
                    inside = !inside;
            }
            return inside;
        }

        private static double DistanceToSegmentSquared(Point2 point, Point2 start, Point2 end)
        {
            Point2 segment = end - start;
            double lengthSquared = segment.LengthSquared;
            if (lengthSquared < Epsilon)
                return point.DistanceSquaredTo(start);
            double t = Dot(point - start, segment) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            return point.DistanceSquaredTo(start + segment * t);
        }

        private static double DominantAngle(IList<IList<XYZ>> loops)
        {
            var weights = new Dictionary<int, double>();
            foreach (IList<XYZ> loop in loops)
                for (int i = 0; i < loop.Count; i++)
                {
                    XYZ a = loop[i];
                    XYZ b = loop[(i + 1) % loop.Count];
                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length < 1e-8)
                        continue;
                    double angle = Math.Atan2(dy, dx) % (Math.PI / 2.0);
                    if (angle < 0) angle += Math.PI / 2.0;
                    int bin = (int)Math.Round(angle * 720.0 / Math.PI) % 360;
                    weights[bin] = weights.TryGetValue(bin, out double weight) ? weight + length : length;
                }
            if (weights.Count == 0)
                return 0;
            int best = weights.OrderByDescending(pair => pair.Value).First().Key;
            return best * Math.PI / 720.0;
        }

        private static List<Point2> CleanLoop(IEnumerable<Point2> source)
        {
            var result = new List<Point2>();
            foreach (Point2 point in source)
                if (result.Count == 0 || point.DistanceTo(result[result.Count - 1]) > 0.1)
                    result.Add(point);
            if (result.Count > 2 && result[0].DistanceTo(result[result.Count - 1]) <= 0.1)
                result.RemoveAt(result.Count - 1);
            return result;
        }

        private static Point2 ToLocal(XYZ point, double angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            double x = point.X * MillimetersPerFoot;
            double y = point.Y * MillimetersPerFoot;
            return new Point2(x * c + y * s, -x * s + y * c);
        }

        private static Point2 ToLocalVector(XYZ vector, double angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return new Point2(vector.X * c + vector.Y * s, -vector.X * s + vector.Y * c);
        }

        private static Point2 ToWorld(Point2 point, double angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return new Point2(point.X * c - point.Y * s, point.X * s + point.Y * c);
        }

        private static int CountCrossings(IList<Point2> path)
        {
            int count = 0;
            for (int i = 0; i + 1 < path.Count; i++)
                for (int j = i + 2; j + 1 < path.Count; j++)
                    if (SegmentsCross(path[i], path[i + 1], path[j], path[j + 1]))
                        count++;
            return count;
        }

        private static bool SegmentsCross(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            double abC = Cross(b - a, c - a);
            double abD = Cross(b - a, d - a);
            double cdA = Cross(d - c, a - c);
            double cdB = Cross(d - c, b - c);
            return abC * abD < -1e-6 && cdA * cdB < -1e-6;
        }

        private static double PolylineLength(IList<Point2> points)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++)
                length += points[i].DistanceTo(points[i - 1]);
            return length;
        }

        private static double Cross(Point2 first, Point2 second) => first.X * second.Y - first.Y * second.X;
        private static double Dot(Point2 first, Point2 second) => first.X * second.X + first.Y * second.Y;
        private static Point2 Normalize(Point2 point) => point.Length > Epsilon ? point / point.Length : default;

        private static void AddDistinct(IList<Point2> points, Point2 point)
        {
            if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > 0.1)
                points.Add(point);
        }

        private static void AddDistinct(IList<XYZ> points, XYZ point)
        {
            if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > 1e-8)
                points.Add(point);
        }

        private readonly struct CellKey : IEquatable<CellKey>
        {
            public CellKey(int column, int row) { Column = column; Row = row; }
            public int Column { get; }
            public int Row { get; }
            public bool Equals(CellKey other) => Column == other.Column && Row == other.Row;
            public override bool Equals(object obj) => obj is CellKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Column, Row);
        }

        private readonly struct AxisAnchor
        {
            public AxisAnchor(double position, int parity) { Position = position; Parity = parity; }
            public double Position { get; }
            public int Parity { get; }
        }

        private readonly struct AxisCellRange
        {
            public AxisCellRange(double min, double max) { Min = min; Max = max; }
            public double Min { get; }
            public double Max { get; }
        }

        private readonly struct AxisSpan
        {
            public AxisSpan(double start, double end, int count)
            {
                Start = start;
                End = end;
                Count = count;
            }
            public double Start { get; }
            public double End { get; }
            public double Length => End - Start;
            public int Count { get; }
        }

        private readonly struct OrientationCandidate
        {
            public OrientationCandidate(double angle, double boundarySupport)
            {
                Angle = angle;
                BoundarySupport = boundarySupport;
            }
            public double Angle { get; }
            public double BoundarySupport { get; }
        }

        private sealed class OrientationLayout
        {
            public OrientationLayout(IList<XYZ> path, double coverage, double boundarySupport)
            {
                Path = path;
                Coverage = coverage;
                BoundarySupport = boundarySupport;
            }
            public IList<XYZ> Path { get; }
            public double Coverage { get; }
            public double BoundarySupport { get; }
        }

        private sealed class AngleBin
        {
            public double Weight { get; set; }
            public double Cosine { get; set; }
            public double Sine { get; set; }
        }

        private sealed class GridCell
        {
            public GridCell(int column, int row, double minX, double minY, double maxX, double maxY)
            {
                Column = column; Row = row; MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            }
            public int Id { get; set; }
            public int Column { get; }
            public int Row { get; }
            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public Point2 Center => new Point2((MinX + MaxX) / 2, (MinY + MaxY) / 2);
            public int Layer { get; set; }
            public GridCell Parent { get; set; }
            public GridCell Right { get; set; }
            public GridCell Up { get; set; }
            public GridCell Left { get; set; }
            public GridCell Down { get; set; }
            public List<GridCell> Neighbors { get; } = new List<GridCell>();
        }

        private readonly struct State : IEquatable<State>
        {
            public State(GridCell cell, int side) { Cell = cell; Side = side; }
            public GridCell Cell { get; }
            public int Side { get; }
            public bool Equals(State other) => Cell.Id == other.Cell.Id && Side == other.Side;
            public override bool Equals(object obj) => obj is State other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Cell.Id, Side);
        }

        private readonly struct Point2
        {
            public Point2(double x, double y) { X = x; Y = y; }
            public double X { get; }
            public double Y { get; }
            public double Length => Math.Sqrt(LengthSquared);
            public double LengthSquared => X * X + Y * Y;
            public double DistanceTo(Point2 other) => Math.Sqrt(DistanceSquaredTo(other));
            public double DistanceSquaredTo(Point2 other)
            {
                double dx = X - other.X; double dy = Y - other.Y;
                return dx * dx + dy * dy;
            }
            public static Point2 operator +(Point2 a, Point2 b) => new Point2(a.X + b.X, a.Y + b.Y);
            public static Point2 operator -(Point2 a, Point2 b) => new Point2(a.X - b.X, a.Y - b.Y);
            public static Point2 operator *(Point2 a, double k) => new Point2(a.X * k, a.Y * k);
            public static Point2 operator /(Point2 a, double k) => new Point2(a.X / k, a.Y / k);
        }
    }
}
