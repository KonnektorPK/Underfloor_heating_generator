using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Underfloor_heating_generator.Infrastructure.Geometry;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    // Prefer the tested perimeter-first counter-flow spiral for simple offset contours.
    // Keep the safe-cell contour method as a fallback for concave, split, and holed regions.
    internal static class AreaPipeLayout
    {
        private const double MillimetersPerFoot = 304.8;
        private const double Epsilon = 1e-7;
        private const int MaximumCells = 50000;
        private const double WallOffsetTolerance = 0.02;
        private const double WallMeasurementToleranceMillimeters = 0.01;
        private const double SegmentGeometryToleranceMillimeters = 0.25;

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

            List<OrientationCandidate> orientations = GetOrientationCandidates(boundaryLoops,
                spacingMillimeters);
            if (orientations.Count == 0)
                return new List<XYZ>();

            OrientationCandidate preferred = orientations
                .OrderBy(candidate => OrientationDistance(candidate.Angle,
                    DominantAngle(boundaryLoops)))
                .ThenByDescending(candidate => candidate.BoundarySupport)
                .First();
            IList<XYZ> preferredPath = CreateForOrientation(boundaryLoops, spacingMillimeters,
                edgeOffsetMillimeters, pipeDiameterMillimeters, levelElevationFeet,
                layingHeightMillimeters, startConnection, startDirection, endConnection,
                endDirection, preferred.Angle, out _, out _);
            if (preferredPath.Count > 0 && PathFollowsOrientation(preferredPath,
                    preferred.Angle, spacingMillimeters))
                return preferredPath;

            // Only try alternate axes when the geometry cannot be routed on its primary
            // wall-supported axis. Valid routes no longer pay for a full orientation sweep.
            IList<XYZ> firstFallback = preferredPath;
            foreach (OrientationCandidate orientation in orientations
                         .Where(candidate => OrientationDistance(candidate.Angle, preferred.Angle) >
                                             Math.PI / 360.0))
            {
                IList<XYZ> candidate = CreateForOrientation(boundaryLoops, spacingMillimeters,
                    edgeOffsetMillimeters, pipeDiameterMillimeters, levelElevationFeet,
                    layingHeightMillimeters, startConnection, startDirection, endConnection,
                    endDirection, orientation.Angle, out _, out _);
                if (candidate.Count > 0 && firstFallback.Count == 0)
                    firstFallback = candidate;
                if (candidate.Count > 0 && PathFollowsOrientation(candidate,
                        orientation.Angle, spacingMillimeters))
                    return candidate;
            }
            return firstFallback ?? new List<XYZ>();
        }

        private static bool PathFollowsOrientation(IList<XYZ> path, double orientation,
            double spacingMillimeters)
        {
            if (path == null || path.Count < 2)
                return false;
            double minimumLength = 10.0;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                double dx = path[i + 1].X - path[i].X;
                double dy = path[i + 1].Y - path[i].Y;
                double length = Math.Sqrt(dx * dx + dy * dy) * MillimetersPerFoot;
                if (length < minimumLength)
                    continue;
                double difference = OrientationDistance(Math.Atan2(dy, dx), orientation);
                if (difference <= Math.PI / 90.0)
                    return true;
            }
            return false;
        }

        private static IList<XYZ> CreateForOrientation(IList<IList<XYZ>> boundaryLoops,
            double spacingMillimeters, double edgeOffsetMillimeters, double pipeDiameterMillimeters,
            double levelElevationFeet, double layingHeightMillimeters,
            XYZ startConnection, XYZ startDirection, XYZ endConnection, XYZ endDirection,
            double angle, out double overallCoverage, out double overallEdgeExcess)
        {
            overallCoverage = 0;
            overallEdgeExcess = double.PositiveInfinity;
            var loops = boundaryLoops.Select(loop => CleanLoop(loop.Select(point => ToLocal(point, angle))))
                .Where(loop => loop.Count >= 3).ToList();
            if (loops.Count == 0)
                return new List<XYZ>();

            var start = ToLocal(startConnection, angle);
            var end = ToLocal(endConnection, angle);
            var startDir = Normalize(ToLocalVector(startDirection, angle));
            var endDir = Normalize(ToLocalVector(endDirection, angle));
            double nominalOffset = Math.Max(0, edgeOffsetMillimeters);
            double offsetTolerance = nominalOffset * WallOffsetTolerance;
            double minimumClearance = nominalOffset - offsetTolerance + pipeDiameterMillimeters / 2.0;
            double maximumWallOffset = nominalOffset + offsetTolerance;
            double[] offsetFactors = nominalOffset > Epsilon
                ? new[] { 1.0, 1.0 - WallOffsetTolerance, 1.0 + WallOffsetTolerance }
                : new[] { 1.0 };
            double maximumPitchFactor = loops.Count == 1 && IsAxisAlignedRectangle(loops[0])
                ? RectangularSpiral.MaximumPitchFactor
                : 1.1;
            bool orthogonalRecesses = loops.Count == 1 && loops[0].Count > 4 && IsAxisAligned(loops[0]);
            bool preferBoundaryLap = loops.Count == 1 && !orthogonalRecesses && HasConcaveCorner(loops[0]);
            double[] spacingFactors = loops.Count > 1 || orthogonalRecesses
                ? new[] { 1.0, 0.9, maximumPitchFactor, 0.94, 1.06, 0.97, 1.03, 0.88, 1.08, 0.92 }
                : new[] { 1.0, 0.9, maximumPitchFactor };
            IList<XYZ> bestResult = new List<XYZ>();
            double bestCoverage = double.NegativeInfinity;
            double bestEdgeExcess = double.PositiveInfinity;
            double bestDeviation = double.PositiveInfinity;
            double preferredCoverageGap = spacingMillimeters * (orthogonalRecesses ? 0.85 : 1.65);

            // The finer grid search is only needed when the usual pitches give no
            // route within the setback tolerance.
            for (int spacingIndex = 0; spacingIndex < spacingFactors.Length; spacingIndex++)
            {
                if (spacingIndex >= 3 && bestResult.Count > 0 && 1.0 / bestCoverage - 1.0 <= preferredCoverageGap)
                    break;
                double spacingFactor = spacingFactors[spacingIndex];
                foreach (double offsetFactor in offsetFactors)
                {
                    double edgeOffset = nominalOffset * offsetFactor;
                    double allowedEdgeExcess = maximumWallOffset - edgeOffset + WallMeasurementToleranceMillimeters;
                    double pitch = spacingMillimeters * spacingFactor;
                    // A deep oblique recess can split the offset rings. Keep
                    // the outer lap and fill the connected interior first.
                    bool tryBoundaryLapFirst = preferBoundaryLap && spacingIndex == 0 && offsetFactor == 1.0;
                    IList<XYZ> candidate = TryCreateCandidate(loops, pitch, edgeOffset,
                        pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                        startConnection, endConnection, start, startDir, end, endDir, angle,
                        out double coverage, out double edgeExcess, out bool usedRectangularSpiral,
                        allowPerimeterSpiral: !orthogonalRecesses && !tryBoundaryLapFirst,
                        forceBoundaryLap: tryBoundaryLapFirst,
                        allowRectangularPitchAdjustment: spacingFactor == 1.0, minimumClearance: minimumClearance,
                        referencePitch: spacingMillimeters);
                    if (orthogonalRecesses && spacingIndex == 0 && offsetFactor == 1.0 &&
                        (candidate.Count == 0 || edgeExcess > allowedEdgeExcess ||
                         1.0 / coverage - 1.0 > preferredCoverageGap))
                    {
                        for (int entryRank = 1; entryRank <= 4 &&
                            (candidate.Count == 0 || edgeExcess > allowedEdgeExcess ||
                             1.0 / coverage - 1.0 > preferredCoverageGap); entryRank++)
                        {
                            IList<XYZ> entryCandidate = TryCreateCandidate(loops, pitch, edgeOffset,
                                pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                                startConnection, endConnection, start, startDir, end, endDir, angle,
                                out double entryCoverage, out double entryEdgeExcess, out _,
                                allowPerimeterSpiral: false, minimumClearance: minimumClearance,
                                gridEntryRank: entryRank, referencePitch: spacingMillimeters);
                            if (entryCandidate.Count == 0 || entryEdgeExcess > allowedEdgeExcess ||
                                (candidate.Count > 0 && edgeExcess <= allowedEdgeExcess && coverage >= entryCoverage))
                                continue;
                            candidate = entryCandidate;
                            coverage = entryCoverage;
                            edgeExcess = entryEdgeExcess;
                        }
                    }
                    if ((tryBoundaryLapFirst || orthogonalRecesses && spacingIndex == 0 && offsetFactor == 1.0) &&
                        (candidate.Count == 0 || edgeExcess > allowedEdgeExcess))
                    {
                        candidate = TryCreateCandidate(loops, pitch, edgeOffset,
                            pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                            startConnection, endConnection, start, startDir, end, endDir, angle,
                            out coverage, out edgeExcess, out usedRectangularSpiral,
                            minimumClearance: minimumClearance, referencePitch: spacingMillimeters);
                    }
                    if (candidate.Count == 0 || edgeExcess > allowedEdgeExcess)
                        continue;

                    double deviation = Math.Abs(edgeOffset - nominalOffset) +
                        Math.Abs(pitch - spacingMillimeters);
                    double coverageTolerance = Math.Max(1e-8, Math.Abs(coverage) * 0.01);
                    bool better = bestResult.Count == 0 || coverage > bestCoverage + coverageTolerance ||
                        (Math.Abs(coverage - bestCoverage) <= coverageTolerance && deviation < bestDeviation);
                    if (better)
                    {
                        bestResult = candidate;
                        bestCoverage = coverage;
                        bestEdgeExcess = edgeExcess;
                        bestDeviation = deviation;
                    }

                    double largestGap = coverage > 0 ? (1.0 / coverage) - 1.0 :
                        double.PositiveInfinity;
                    if (spacingFactor == 1.0 && offsetFactor == 1.0 &&
                        (usedRectangularSpiral || largestGap <= preferredCoverageGap))
                    {
                        overallCoverage = coverage;
                        overallEdgeExcess = edgeExcess;
                        return candidate;
                    }
                }
            }

            overallCoverage = bestCoverage;
            overallEdgeExcess = bestEdgeExcess;
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
                // Exactly one span owns the cell next to each anchor. If both
                // spans end/start a cell at the same coordinate, the trace
                // retraces a pipe along the shared edge.
                int requiredCountParity = startAnchor.Parity ^ endAnchor.Parity;
                int count = minimumCount <= maximumCount
                    ? Enumerable.Range(minimumCount, maximumCount - minimumCount + 1)
                        .Where(candidate => candidate % 2 == requiredCountParity)
                        .OrderBy(candidate => Math.Abs(length / candidate - nominalPitch))
                        .FirstOrDefault()
                    : 0;

                // A short span between two recess walls may not have an integer
                // count of nominal intervals with the required pairing. Keep
                // the wall anchors and pick the closest physically safe spacing
                // for this span only.
                if (count == 0)
                {
                    double minimumLocalPitch = pipeDiameter + 2;
                    int localMinimumCount = Math.Max(1, (int)Math.Ceiling(length / (nominalPitch * 1.25) - 1e-9));
                    int localMaximumCount = (int)Math.Floor(length / minimumLocalPitch + 1e-9);
                    if (localMinimumCount > localMaximumCount)
                        return new List<AxisCellRange>();
                    count = Enumerable.Range(localMinimumCount, localMaximumCount - localMinimumCount + 1)
                        .Where(candidate => candidate % 2 == requiredCountParity)
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
            out double coverage, out double edgeExcess, out bool usedRectangularSpiral,
            bool allowPerimeterSpiral = true,
            int boundaryJoinOrder = 0, bool forceBoundaryLap = false,
            bool allowRectangularSpiral = true, bool allowRectangularPitchAdjustment = true,
            double minimumClearance = 0, int gridEntryRank = 0, double referencePitch = 0)
        {
            coverage = 0;
            edgeExcess = double.PositiveInfinity;
            usedRectangularSpiral = false;
            double clearance = edgeOffsetMillimeters + pipeDiameterMillimeters / 2.0;
            double pitch = spacingMillimeters;

            List<Point2> loopPath = null;
            bool usedPerimeterSpiral = false;
            bool usedBoundaryPrefix = false;
            int perimeterLaps = 0;
            List<Point2> outerBoundary = null;
            List<Point2> boundaryLap = null;
            Point2 leadOutward = default;
            usedRectangularSpiral = allowRectangularSpiral && allowPerimeterSpiral && loops.Count == 1 &&
                TryBuildRectangularSpiral(loops[0], start, end, startDir, endDir, clearance,
                    pipeDiameterMillimeters, allowRectangularPitchAdjustment, ref pitch,
                    out loopPath, out leadOutward);
            if (usedRectangularSpiral)
            {
                usedPerimeterSpiral = true;
                edgeExcess = 0;
            }
            else if (allowPerimeterSpiral && loops.Count == 1)
            {
                IReadOnlyList<PerimeterSpiralCore.Point> boundary = loops[0]
                    .Select(point => new PerimeterSpiralCore.Point(point.X, point.Y)).ToList();
                double openingLength = GetConnectionOpeningLength(start, end, pitch,
                    pipeDiameterMillimeters);
                if (PerimeterSpiralCore.TryBuild(new[] { boundary }, pitch, clearance,
                        new PerimeterSpiralCore.Point((start.X + end.X) / 2.0,
                            (start.Y + end.Y) / 2.0), openingLength,
                        out List<PerimeterSpiralCore.Point> perimeterRoute))
                {
                    loopPath = perimeterRoute.Select(point => new Point2(point.X, point.Y)).ToList();
                    usedPerimeterSpiral = true;
                    edgeExcess = 0;
                }
            }

            // Orthogonal recesses may split an inset while still allowing a connected
            // circuit. The wall-aligned grid can cover those branches.
            bool canUseOrthogonalFallback = loops.Count == 1 && IsAxisAligned(loops[0]);
            if (!usedPerimeterSpiral && (allowPerimeterSpiral || forceBoundaryLap))
            {
                outerBoundary = loops.Count == 1 ? loops[0] : FindSingleOuterBoundary(loops);
                if (outerBoundary != null)
                {
                    IReadOnlyList<PerimeterSpiralCore.Point> boundary = outerBoundary
                        .Select(point => new PerimeterSpiralCore.Point(point.X, point.Y)).ToList();
                    double openingLength = GetConnectionOpeningLength(start, end, pitch,
                        pipeDiameterMillimeters);
                    if (PerimeterSpiralCore.TryBuildBoundaryBand(boundary, pitch, clearance,
                            new PerimeterSpiralCore.Point((start.X + end.X) / 2.0,
                                (start.Y + end.Y) / 2.0),
                            openingLength, maximumLaps: 1,
                            out List<PerimeterSpiralCore.Point> perimeterLap, out perimeterLaps))
                    {
                        List<Point2> candidateLap = perimeterLap
                            .Select(point => new Point2(point.X, point.Y)).ToList();
                        bool candidateLapIsSafe = true;
                        for (int segmentIndex = 0; segmentIndex + 1 < candidateLap.Count; segmentIndex++)
                        {
                            Point2 first = candidateLap[segmentIndex], second = candidateLap[segmentIndex + 1];
                            if (IsSafeSegment(loops, first, second, clearance))
                                continue;
                            candidateLapIsSafe = false;
                            break;
                        }
                        if (candidateLapIsSafe)
                        {
                            boundaryLap = candidateLap;
                            usedBoundaryPrefix = true;
                        }
                    }
                }
            }
            if (loops.Count == 1 && !usedPerimeterSpiral && !canUseOrthogonalFallback && !usedBoundaryPrefix)
                return new List<XYZ>();

            if (!usedPerimeterSpiral)
            {
                List<GridCell> cells = BuildCells(loops, spacingMillimeters,
                    pipeDiameterMillimeters, clearance, out edgeExcess, outerBoundary,
                    usedBoundaryPrefix ? clearance + perimeterLaps * pitch : 0,
                    referencePitch * RectangularSpiral.MinimumPitchFactor - 0.5);
                if (cells.Count == 0)
                {
                    if (usedBoundaryPrefix)
                        return RetryWithoutPerimeterPrefix(loops, spacingMillimeters, edgeOffsetMillimeters,
                            pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                            startConnection, endConnection, start, startDir, end, endDir, angle,
                            out coverage, out edgeExcess, minimumClearance, referencePitch);
                    return new List<XYZ>();
                }

                GridCell entry = null;
                int entrySide = -1;
                var entries = new List<(GridCell Cell, int Side, double Score)>();
                Point2 entryPreference = usedBoundaryPrefix ? boundaryLap[boundaryLap.Count - 1] :
                    Dot(startDir, endDir) > 0.95 ? (start + end) / 2 : start;
                foreach (GridCell cell in cells)
                {
                    for (int side = 0; side < 4; side++)
                    {
                        GridCell acrossSide = side switch
                        {
                            0 => cell.Down, 1 => cell.Right, 2 => cell.Up, _ => cell.Left
                        };
                        if (acrossSide != null)
                            continue;
                        Point2 normal = SideNormal(side);
                        Point2 midpoint = SideMidpoint(cell, side);
                        double edgeDepth = DistanceToBoundary(loops, midpoint);
                        double facing = Dot(entryPreference - midpoint, normal);
                        double entryDepthLimit = clearance + spacingMillimeters *
                            (usedBoundaryPrefix ? perimeterLaps + 2.5 : 1.1);
                        if (edgeDepth > entryDepthLimit ||
                            !usedBoundaryPrefix && facing <= 0)
                            continue;

                        double score = midpoint.DistanceSquaredTo(entryPreference) -
                            (usedBoundaryPrefix ? 0 : facing * 0.1);
                        entries.Add((cell, side, score));
                    }
                }
                if (gridEntryRank < entries.Count)
                {
                    var choice = entries.OrderBy(item => item.Score).ElementAt(gridEntryRank);
                    entry = choice.Cell;
                    entrySide = choice.Side;
                }
                if (entry == null)
                {
                    if (usedBoundaryPrefix)
                        return RetryWithoutPerimeterPrefix(loops, spacingMillimeters, edgeOffsetMillimeters,
                            pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                            startConnection, endConnection, start, startDir, end, endDir, angle,
                            out coverage, out edgeExcess, minimumClearance, referencePitch);
                    return new List<XYZ>();
                }
                BuildAdjacency(cells);
                BuildSpanningTree(entry, loops, clearance, pitch);
                HashSet<GridCell> component = GetTreeComponent(entry);
                if (component.Count != cells.Count)
                {
                    if (usedBoundaryPrefix)
                        return RetryWithoutPerimeterPrefix(loops, spacingMillimeters, edgeOffsetMillimeters,
                            pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                            startConnection, endConnection, start, startDir, end, endDir, angle,
                            out coverage, out edgeExcess, minimumClearance, referencePitch);
                    return new List<XYZ>();
                }

                loopPath = SimplifyCollinear(TraceCellContour(entry, entrySide, component));
                if (usedBoundaryPrefix && loopPath != null)
                {
                    loopPath = JoinBoundaryLap(boundaryLap, loopPath, loops, clearance, pitch,
                        boundaryJoinOrder, pitch * (perimeterLaps + 1.0));
                    if (loopPath == null)
                    {
                        return RetryWithoutPerimeterPrefix(loops, spacingMillimeters, edgeOffsetMillimeters,
                            pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                            startConnection, endConnection, start, startDir, end, endDir, angle,
                            out coverage, out edgeExcess, minimumClearance, referencePitch);
                    }
                    edgeExcess = 0;
                }
            }

            if (loopPath == null || loopPath.Count < 2)
            {
                if (usedBoundaryPrefix)
                    return RetryWithoutPerimeterPrefix(loops, spacingMillimeters, edgeOffsetMillimeters,
                        pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                        startConnection, endConnection, start, startDir, end, endDir, angle,
                        out coverage, out edgeExcess, minimumClearance, referencePitch);
                return new List<XYZ>();
            }
            List<Point2> bestPath = null;
            double bestScore = double.PositiveInfinity;
            double bestBodyCoverage = 0;
            double bestPathEdgeExcess = double.PositiveInfinity;
            double requiredClearance = Math.Max(clearance,
                minimumClearance + SegmentGeometryToleranceMillimeters - WallMeasurementToleranceMillimeters);
            List<List<Point2>> innerLoops = GetInnerLoops(loops);
            for (int orientation = 0; orientation < 2; orientation++)
            {
                List<Point2> orientedLoop = new List<Point2>(loopPath);
                if (orientation == 1)
                    orientedLoop.Reverse();
                if (!usedRectangularSpiral)
                {
                    if (!TryRoundLoop(orientedLoop, loops, clearance, pipeDiameterMillimeters,
                            pitch, out List<Point2> roundedLoop))
                        continue;
                    orientedLoop = roundedLoop;
                }

                bool loopIsSafe = true;
                for (int i = 0; i + 1 < orientedLoop.Count; i++)
                {
                    if (IsSafeSegment(loops, orientedLoop[i], orientedLoop[i + 1], requiredClearance))
                        continue;
                    loopIsSafe = false;
                    break;
                }
                if (!loopIsSafe)
                {
                    continue;
                }
                if (!HasLongRunPitch(orientedLoop, referencePitch > 0 ? referencePitch : spacingMillimeters))
                    continue;

                Point2 loopStart = orientedLoop[0];
                Point2 loopEnd = orientedLoop[orientedLoop.Count - 1];
                double bodyCoverage = ComputeCoverageQuality(loops, orientedLoop, clearance, pitch);
                double bodyEdgeExcess = ComputeBoundaryOffsetExcess(loops, orientedLoop,
                    edgeOffsetMillimeters, pipeDiameterMillimeters, pitch);
                bool oppositeLeads = Dot(startDir, endDir) < -0.95;
                List<List<Point2>> startRoutes = BuildConnectorRoutes(loopStart, start,
                    startDir, orientedLoop, atStart: true, innerLoops, clearance,
                    pipeDiameterMillimeters, pitch, leadOutward, oppositeLeads);
                List<List<Point2>> endRoutes = BuildConnectorRoutes(loopEnd, end,
                    endDir, orientedLoop, atStart: false, innerLoops, clearance,
                    pipeDiameterMillimeters, pitch, leadOutward, oppositeLeads);
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
                    double leadClearance = pipeDiameterMillimeters + 2.0;
                    if (!LeadsHaveClearance(fromStart, toEnd, leadClearance))
                    {
                        continue;
                    }

                    double score = PolylineLength(fromStart) + PolylineLength(toEnd) + candidate.Count;
                    if (score < bestScore && PerimeterSpiralCore.HasRoomBetweenRuns(
                        candidate.Select(point => new PerimeterSpiralCore.Point(point.X, point.Y)).ToList(),
                        pitch, leadClearance))
                    {
                        bestScore = score;
                        bestPath = candidate;
                        bestBodyCoverage = bodyCoverage;
                        bestPathEdgeExcess = bodyEdgeExcess;
                    }
                }
            }

            if (bestPath == null && usedRectangularSpiral)
            {
                return TryCreateCandidate(loops, spacingMillimeters, edgeOffsetMillimeters, pipeDiameterMillimeters,
                    levelElevationFeet, layingHeightMillimeters, startConnection, endConnection,
                    start, startDir, end, endDir, angle, out coverage, out edgeExcess, out usedRectangularSpiral,
                    allowRectangularSpiral: Math.Abs(pitch - spacingMillimeters) > Epsilon,
                    allowRectangularPitchAdjustment: false, minimumClearance: minimumClearance,
                    referencePitch: referencePitch);
            }

            if ((bestPath == null || CountCrossings(bestPath) > 0) && usedBoundaryPrefix &&
                boundaryJoinOrder == 0)
            {
                return TryCreateCandidate(loops, spacingMillimeters, edgeOffsetMillimeters,
                    pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                    startConnection, endConnection, start, startDir, end, endDir, angle,
                            out coverage, out edgeExcess, out usedRectangularSpiral,
                            allowPerimeterSpiral, boundaryJoinOrder: 1,
                            forceBoundaryLap, minimumClearance: minimumClearance, referencePitch: referencePitch);
            }

            if ((bestPath == null || CountCrossings(bestPath) > 0) && usedBoundaryPrefix)
            {
                return RetryWithoutPerimeterPrefix(loops, spacingMillimeters, edgeOffsetMillimeters,
                    pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                    startConnection, endConnection, start, startDir, end, endDir, angle,
                    out coverage, out edgeExcess, minimumClearance, referencePitch);
            }

            if ((bestPath == null || CountCrossings(bestPath) > 0) && usedPerimeterSpiral)
            {
                if (canUseOrthogonalFallback)
                    return RetryWithoutPerimeterPrefix(loops, spacingMillimeters, edgeOffsetMillimeters,
                        pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                        startConnection, endConnection, start, startDir, end, endDir, angle,
                        out coverage, out edgeExcess, minimumClearance, referencePitch);
                return new List<XYZ>();
            }

            if (bestPath == null || CountCrossings(bestPath) > 0)
            {
                return new List<XYZ>();
            }

            coverage = bestBodyCoverage;
            edgeExcess = bestPathEdgeExcess;

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

        private static bool TryBuildRectangularSpiral(IList<Point2> loop, Point2 start, Point2 end,
            Point2 startDir, Point2 endDir, double clearance, double diameter, bool adjustPitch, ref double pitch,
            out List<Point2> path, out Point2 outward)
        {
            path = null;
            outward = default;
            if (!IsAxisAlignedRectangle(loop) || pitch <= diameter || pitch < 2 * 2.54)
                return false;

            Point2 center = new Point2(loop.Average(p => p.X), loop.Average(p => p.Y));
            Point2 portCenter = (start + end) / 2;
            var axes = new[] { new Point2(1, 0), new Point2(0, 1), new Point2(-1, 0), new Point2(0, -1) };
            foreach (Point2 normal in axes)
            {
                double wall = loop.Max(p => Dot(p, normal));
                double right = wall - clearance;
                if (Dot(start, normal) < right || Dot(end, normal) < right ||
                    Dot(startDir, normal) > -0.99999 || Dot(endDir, normal) > -0.99999)
                    continue;

                Point2 tangent = new Point2(-normal.Y, normal.X);
                if (Dot(portCenter - center, tangent) < 0)
                    tangent = tangent * -1;
                double left = loop.Min(p => Dot(p, normal)) + clearance;
                double bottom = loop.Min(p => Dot(p, tangent)) + clearance;
                double width = right - left;
                double height = loop.Max(p => Dot(p, tangent)) - clearance - bottom;
                double selectedPitch = adjustPitch
                    ? RectangularSpiral.SelectRoomPitch(width, height, pitch, diameter)
                    : pitch;
                int turns = RectangularSpiral.CountRoomTurns(width, height, selectedPitch);
                if (turns == 0)
                    return false;
                pitch = selectedPitch;

                double maximumEntryHeight = height > 5 * pitch + 5.08
                    ? height - 4 * pitch - 5.08
                    : height - pitch;
                double entryHeight = Math.Clamp(Dot(portCenter, tangent) - bottom - pitch / 2,
                    pitch, maximumEntryHeight);
                double innerWidth = width - 4 * pitch * turns - diameter;
                double innerHeight = height - (4 * turns - 2) * pitch - diameter;
                float traceWidth = (float)diameter;
                float gap = (float)(pitch - diameter);
                // The geometry uses PointF in local coordinates; allow a small rounding
                // margin at the minimum inner width.
                float clearWidth = Math.Max(gap, (float)innerWidth);
                path = RectangularSpiral.GenerateRoomSpiral(turns, traceWidth, gap,
                        clearWidth, (float)innerHeight, entryHeight)
                    .Select(p => normal * (left + p.X) + tangent * (bottom + p.Y)).ToList();
                outward = normal;
                return true;
            }
            return false;
        }

        private static bool IsAxisAlignedRectangle(IList<Point2> loop)
        {
            if (loop.Count != 4)
                return false;
            double minX = loop.Min(p => p.X), maxX = loop.Max(p => p.X);
            double minY = loop.Min(p => p.Y), maxY = loop.Max(p => p.Y);
            const double tolerance = 0.05;
            return loop.All(p => Math.Min(p.X - minX, maxX - p.X) <= tolerance &&
                                 Math.Min(p.Y - minY, maxY - p.Y) <= tolerance);
        }

        private static bool IsAxisAligned(IList<Point2> loop)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                Point2 edge = loop[(i + 1) % loop.Count] - loop[i];
                if (Math.Min(Math.Abs(edge.X), Math.Abs(edge.Y)) > 0.05)
                    return false;
            }
            return true;
        }

        private static bool HasConcaveCorner(IList<Point2> loop)
        {
            double winding = Math.Sign(SignedArea(loop));
            for (int i = 0; i < loop.Count; i++)
            {
                Point2 incoming = loop[i] - loop[(i + loop.Count - 1) % loop.Count];
                Point2 outgoing = loop[(i + 1) % loop.Count] - loop[i];
                if (winding * Cross(incoming, outgoing) < -Epsilon)
                    return true;
            }
            return false;
        }

        private static bool HasLongRunPitch(IList<Point2> path, double requestedPitch)
        {
            double overlapLimit = 3 * requestedPitch;
            double minimumPitch = requestedPitch * RectangularSpiral.MinimumPitchFactor - 0.5;
            double parallelTolerance = Math.Sin(Math.PI / 360);
            var runs = new List<(Point2 Start, Point2 End, Point2 Unit, double Length)>();
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Point2 start = path[i], end = path[i + 1];
                double length = start.DistanceTo(end);
                if (length >= overlapLimit)
                    runs.Add((start, end, (end - start) / length, length));
            }
            for (int i = 0; i < runs.Count; i++)
            {
                var first = runs[i];
                for (int j = i + 1; j < runs.Count; j++)
                {
                    var second = runs[j];
                    if (Math.Abs(Cross(first.Unit, second.Unit)) > parallelTolerance)
                        continue;
                    double a = Dot(second.Start - first.Start, first.Unit);
                    double b = Dot(second.End - first.Start, first.Unit);
                    double from = Math.Max(0, Math.Min(a, b));
                    double to = Math.Min(first.Length, Math.Max(a, b));
                    if (to - from < overlapLimit)
                        continue;
                    double slope = Cross(first.Unit, second.End - second.Start) / (b - a);
                    double intercept = Cross(first.Unit, second.Start - first.Start) - a * slope;
                    if (Math.Min(Math.Abs(intercept + from * slope), Math.Abs(intercept + to * slope)) < minimumPitch)
                        return false;
                }
            }
            return true;
        }

        private static bool TryRoundLoop(IList<Point2> source, IList<List<Point2>> loops,
            double clearance, double pipeDiameter, double pitch, out List<Point2> rounded)
        {
            rounded = null;
            double requestedRadius = Math.Min(pitch * 0.2, pipeDiameter * 2.0);
            double minimumRadius = Math.Max(6.0, pipeDiameter * 0.5);
            double[] radiusFactors = { 1.0, 0.75, 0.5 };
            foreach (double factor in radiusFactors)
            {
                double radius = requestedRadius * factor;
                if (radius < minimumRadius - 1e-6 ||
                    !PerimeterSpiralCore.TryRoundPolyline(source.Select(point =>
                            new PerimeterSpiralCore.Point(point.X, point.Y)).ToList(),
                        radius, 20.0, closed: false, out List<PerimeterSpiralCore.Point> candidate))
                    continue;

                List<Point2> localCandidate = candidate.Select(point =>
                    new Point2(point.X, point.Y)).ToList();
                if (localCandidate.Zip(localCandidate.Skip(1), (first, second) =>
                        IsSafeSegment(loops, first, second, clearance)).Any(isSafe => !isSafe) ||
                    CountCrossings(localCandidate) > 0)
                    continue;

                rounded = localCandidate;
                return true;
            }
            return false;
        }

        private static IList<XYZ> RetryWithoutPerimeterPrefix(IList<List<Point2>> loops,
            double spacingMillimeters, double edgeOffsetMillimeters, double pipeDiameterMillimeters,
            double levelElevationFeet, double layingHeightMillimeters,
            XYZ startConnection, XYZ endConnection, Point2 start, Point2 startDir,
            Point2 end, Point2 endDir, double angle, out double coverage, out double edgeExcess, double minimumClearance, double referencePitch)
        {
            return TryCreateCandidate(loops, spacingMillimeters, edgeOffsetMillimeters,
                pipeDiameterMillimeters, levelElevationFeet, layingHeightMillimeters,
                startConnection, endConnection, start, startDir, end, endDir, angle,
                out coverage, out edgeExcess, out _, allowPerimeterSpiral: false, minimumClearance: minimumClearance, referencePitch: referencePitch);
        }

        private static double GetConnectionOpeningLength(Point2 start, Point2 end,
            double pitch, double pipeDiameter)
        {
            double connectionSeparation = start.DistanceTo(end);
            // The wall opening only needs enough room for the perimeter-to-field bridge.
            // Port separation is handled by independent, collision-checked lead routes;
            // letting it widen this opening leaves a cold strip along the outer wall.
            double cornerAllowance = connectionSeparation < pitch * 2.0 ? pitch : 0;
            double required = connectionSeparation + pipeDiameter * 2.0 + cornerAllowance;
            return Math.Max(pitch * 1.5, Math.Min(required, pitch * 2.5));
        }

        private static List<Point2> FindSingleOuterBoundary(IList<List<Point2>> loops)
        {
            if (loops == null || loops.Count < 2)
                return null;
            List<Point2> outer = loops.OrderByDescending(loop => Math.Abs(SignedArea(loop))).FirstOrDefault();
            if (outer == null || loops.Any(loop => !ReferenceEquals(loop, outer) &&
                                                   !IsPointInsideLoop(outer, loop[0])))
                return null;
            return outer;
        }

        private static double SignedArea(IList<Point2> loop)
        {
            double twiceArea = 0;
            for (int i = 0; i < loop.Count; i++)
                twiceArea += Cross(loop[i], loop[(i + 1) % loop.Count]);
            return twiceArea * 0.5;
        }

        private static List<Point2> JoinBoundaryLap(IList<Point2> boundaryLap,
            IList<Point2> interiorPath, IList<List<Point2>> loops, double clearance,
            double pitch, int preferredReverse, double maximumJoinDistance)
        {
            if (boundaryLap == null || boundaryLap.Count < 3 || interiorPath == null || interiorPath.Count < 2)
                return null;

            foreach (int reverse in new[] { preferredReverse, 1 - preferredReverse })
            {
                var candidate = new List<Point2>(boundaryLap);
                Point2 joinStart = boundaryLap[boundaryLap.Count - 1];
                Point2 interiorStart = reverse == 0 ? interiorPath[0] : interiorPath[interiorPath.Count - 1];
                double joinDistance = joinStart.DistanceTo(interiorStart);
                bool joinIsSafe = IsSafeSegment(loops, joinStart, interiorStart, clearance);
                if (joinDistance > Math.Max(pitch * 2.5, maximumJoinDistance) || !joinIsSafe)
                    continue;

                AddDistinct(candidate, interiorStart);
                if (reverse == 0)
                {
                    for (int i = 1; i < interiorPath.Count; i++)
                        AddDistinct(candidate, interiorPath[i]);
                }
                else
                {
                    for (int i = interiorPath.Count - 2; i >= 0; i--)
                        AddDistinct(candidate, interiorPath[i]);
                }

                int crossings = CountCrossings(candidate);
                bool shortSegment = candidate.Zip(candidate.Skip(1), (first, second) => first.DistanceTo(second))
                    .Any(length => length < 0.1 * 25.4);
                if (candidate.Count < 4 || crossings > 0 || shortSegment)
                    continue;
                return candidate;
            }
            return null;
        }

        private static List<List<Point2>> BuildConnectorRoutes(Point2 pipePoint, Point2 connection,
            Point2 connectionDirection, IList<Point2> coilPath, bool atStart, IList<List<Point2>> innerLoops,
            double clearance, double pipeDiameter, double pitch, Point2 leadOutward = default,
            bool oppositeLeads = false)
        {
            double minX = coilPath.Min(point => point.X);
            double maxX = coilPath.Max(point => point.X);
            double minY = coilPath.Min(point => point.Y);
            double maxY = coilPath.Max(point => point.Y);
            double minimumGap = Math.Max(10.0, pipeDiameter + 2.0);
            double laneStep = Math.Max(30.0, Math.Min(pitch * 0.5, 100.0));
            Point2 approach = connection + Normalize(connectionDirection) * Math.Max(25.0, pipeDiameter * 2.0);
            var candidates = new List<List<Point2>>();

            if (leadOutward.Length > Epsilon)
            {
                Point2 leadNormal = new Point2(-leadOutward.Y, leadOutward.X);
                double across = Dot(approach - pipePoint, leadNormal);
                double along = Dot(approach - pipePoint, leadOutward);
                double chamfer = Math.Abs(across);
                if (chamfer > 0.1 && along > chamfer)
                {
                    // Use a 45-degree shift with straight stubs on both sides.
                    Point2 bend = pipePoint + leadOutward * ((along - chamfer) / 2);
                    AddRoute(new[] { pipePoint, bend, bend + leadOutward * chamfer + leadNormal * across,
                        approach, connection });
                }
            }

            AddRoute(new[] { pipePoint, approach, connection });
            AddRoute(new[] { pipePoint, new Point2(pipePoint.X, approach.Y), approach, connection });
            AddRoute(new[] { pipePoint, new Point2(approach.X, pipePoint.Y), approach, connection });
            if (leadOutward.Length <= Epsilon)
            {
                Point2 outward = Normalize(connectionDirection) * -1.0;
                Point2 lateral = new(-outward.Y, outward.X);
                double along = Dot(approach - pipePoint, outward);
                double across = Dot(approach - pipePoint, lateral);
                if (along > 2 * minimumGap && Math.Abs(across) > 2.54)
                {
                    // The two leads may have overlapping lateral intervals.
                    // Stagger their bends in the available normal distance.
                    foreach (double fraction in new[] { 0.25, 0.5, 0.75 })
                    {
                        Point2 bend = pipePoint + outward * (along * fraction);
                        AddRoute(new[] { pipePoint, bend, bend + lateral * across, approach, connection });
                    }
                }
            }
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

            List<List<Point2>> orderedCandidates = candidates
                .OrderBy(PolylineLength)
                .Take(12)
                .ToList();
            if (oppositeLeads)
            {
                // The body still has one opening. The far connection can run
                // around its outside using two corners, clear of the coil.
                int seen = candidates.Count;
                double margin = Math.Max(clearance, minimumGap);
                double[] sidesX = { minX - margin, maxX + margin };
                double[] sidesY = { minY - margin, maxY + margin };
                if (Math.Abs(connectionDirection.X) >= Math.Abs(connectionDirection.Y))
                {
                    foreach (double x in sidesX)
                    foreach (double y in sidesY)
                        AddRoute(new[] { pipePoint, new Point2(x, pipePoint.Y), new Point2(x, y),
                            new Point2(approach.X, y), approach, connection });
                }
                else
                {
                    foreach (double y in sidesY)
                    foreach (double x in sidesX)
                        AddRoute(new[] { pipePoint, new Point2(pipePoint.X, y), new Point2(x, y),
                            new Point2(x, approach.Y), approach, connection });
                }
                orderedCandidates.AddRange(candidates.Skip(seen));
            }

            void AddRoute(IEnumerable<Point2> points)
            {
                var route = new List<Point2>();
                foreach (Point2 point in points)
                    AddDistinct(route, point);
                if (route.Count < 2 || route.Zip(route.Skip(1),
                        (first, second) => first.DistanceTo(second)).Any(length => length < 0.1 * 25.4))
                    return;
                if (leadOutward.Length > Epsilon)
                {
                    if (route.Any(point => Dot(point - pipePoint, leadOutward) < -0.05))
                        return;
                    for (int i = 0; i + 1 < route.Count; i++)
                    {
                        Point2 delta = route[i + 1] - route[i];
                        double x = Math.Abs(delta.X), y = Math.Abs(delta.Y);
                        if (Math.Min(x, y) > 0.05 && Math.Abs(x - y) > 0.05)
                            return;
                    }
                }
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

            return orderedCandidates;
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
            double nominalPitch, double pipeDiameter, double clearance, out double edgeExcess,
            List<Point2> outerBandBoundary, double outerBandClearance, double minimumRunPitch)
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
            List<List<Point2>> outerBoundaryOnly = outerBandBoundary == null
                ? null
                : new List<List<Point2>> { outerBandBoundary };

            for (int row = 0; row < rows.Count; row++)
            {
                for (int column = 0; column < columns.Count; column++)
                {
                    AxisCellRange xRange = columns[column];
                    AxisCellRange yRange = rows[row];
                    var cell = new GridCell(column, row, xRange.Min, yRange.Min, xRange.Max, yRange.Max);
                    if (!IsCellSafe(cell, loops, clearance) ||
                        outerBoundaryOnly != null && !IsCellSafe(cell, outerBoundaryOnly, outerBandClearance))
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
                if (right != null && IsConnectorSafe(cell, right, loops, clearance, horizontal: true) &&
                    (outerBoundaryOnly == null || IsConnectorSafe(cell, right, outerBoundaryOnly,
                        outerBandClearance, horizontal: true)))
                {
                    cell.Right = right;
                    right.Left = cell;
                }
                if (up != null && IsConnectorSafe(cell, up, loops, clearance, horizontal: false) &&
                    (outerBoundaryOnly == null || IsConnectorSafe(cell, up, outerBoundaryOnly,
                        outerBandClearance, horizontal: false)))
                {
                    cell.Up = up;
                    up.Down = cell;
                }
            }
            // A narrow cell can be necessary to reach a nearby step. Connect
            // such cells across their narrow side so they do not form a long
            // double run with a gap smaller than the laying pitch.
            foreach (GridCell cell in result)
            {
                if (cell.MaxY - cell.MinY < minimumRunPitch && cell.Right is { } right &&
                    (cell.Up != null || cell.Down != null) && (right.Up != null || right.Down != null))
                {
                    cell.Right = null;
                    right.Left = null;
                }
                if (cell.MaxX - cell.MinX < minimumRunPitch && cell.Up is { } up &&
                    (cell.Right != null || cell.Left != null) && (up.Right != null || up.Left != null))
                {
                    cell.Up = null;
                    up.Down = null;
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
            // Cutting an interior tree link can produce a valid-looking loop
            // that drops a whole branch. Keep every cell in the traced circuit.
            if (nodes.Count != 4 * component.Count)
                return null;

            var path = new List<Point2>();
            foreach (State node in nodes)
                AddDistinct(path, Corner(node.Cell, node.Side));
            return path;
        }

        private static List<Point2> SimplifyCollinear(IList<Point2> source)
        {
            var result = source?.ToList() ?? new List<Point2>();
            double angularTolerance = Math.Sin(0.5 * Math.PI / 180.0);
            for (int index = 1; index + 1 < result.Count;)
            {
                Point2 incoming = result[index] - result[index - 1];
                Point2 outgoing = result[index + 1] - result[index];
                double product = incoming.Length * outgoing.Length;
                if (product > Epsilon && Dot(incoming, outgoing) > 0 &&
                    Math.Abs(Cross(incoming, outgoing)) <= angularTolerance * product)
                {
                    result.RemoveAt(index);
                    index = Math.Max(1, index - 1);
                }
                else
                {
                    index++;
                }
            }
            return result;
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
            // Clipper stores offsets at fixed 0.001 mm precision. Leave a small
            // numerical tolerance so an exactly-on-offset segment is not rejected.
            double validatedClearance = Math.Max(0, clearance - SegmentGeometryToleranceMillimeters);
            double minimumDistanceSquared = validatedClearance * validatedClearance;
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    Point2 boundaryStart = loop[i];
                    Point2 boundaryEnd = loop[(i + 1) % loop.Count];
                    if (SegmentDistanceSquared(start, end, boundaryStart, boundaryEnd) <
                        minimumDistanceSquared - 1e-6)
                    {
                        return false;
                    }
                }
            }

            int steps = Math.Max(1, (int)Math.Ceiling(start.DistanceTo(end) / 50.0));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                Point2 point = start + (end - start) * t;
                if (!IsSafePoint(loops, point, validatedClearance))
                {
                    return false;
                }
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
                    double overlapStart = Math.Max(0, Math.Min(firstProjection, secondProjection));
                    double overlapEnd = Math.Min(wallLength, Math.Max(firstProjection, secondProjection));
                    if (overlapEnd - overlapStart < Math.Min(50.0, wallLength * 0.1))
                        continue;

                    // Measure the whole projected portion of this run. Sampling only
                    // its first endpoint can accept a pipe that drifts away from the wall.
                    double distanceAtStart = Cross(pipeStart - wallStart, wallDirection);
                    double distanceSlope = Cross(pipeVector, wallDirection) /
                        (secondProjection - firstProjection);
                    double distance = Math.Max(
                        Math.Abs(distanceAtStart + (overlapStart - firstProjection) * distanceSlope),
                        Math.Abs(distanceAtStart + (overlapEnd - firstProjection) * distanceSlope));
                    nearestDistance = Math.Min(nearestDistance, distance);
                }

                if (!double.IsFinite(nearestDistance))
                    return double.PositiveInfinity;
                double wallGap = nearestDistance - pipeDiameter / 2.0;
                maximumExtra = Math.Max(maximumExtra, wallGap - edgeOffset);
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
            var segmentBuckets = new SegmentBuckets<Point2>(path.Zip(path.Skip(1), (a, b) => (a, b)),
                point => (point.X, point.Y), DistanceToSegmentSquared, pitch * 1.65);

            for (double x = minX + clearance; x <= maxX - clearance; x += sampleStep)
            for (double y = minY + clearance; y <= maxY - clearance; y += sampleStep)
            {
                Point2 sample = new Point2(x, y);
                if (!IsInside(loops, sample) || DistanceToBoundary(loops, sample) < clearance)
                    continue;

                maximumGap = Math.Max(maximumGap, Math.Sqrt(segmentBuckets.NearestSquared(sample)));
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
            return inside && minDistanceSquared >= clearance * clearance - 1e-6;
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

        private static bool LeadsHaveClearance(IList<Point2> first, IList<Point2> second,
            double minimumSeparation)
        {
            double minimumSquared = minimumSeparation * minimumSeparation;
            for (int i = 0; i + 1 < first.Count; i++)
            for (int j = 0; j + 1 < second.Count; j++)
                if (SegmentDistanceSquared(first[i], first[i + 1], second[j], second[j + 1]) <
                    minimumSquared)
                    return false;
            return true;
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
            public OrientationLayout(IList<XYZ> path, double coverage, double boundarySupport,
                double edgeExcess)
            {
                Path = path;
                Coverage = coverage;
                BoundarySupport = boundarySupport;
                EdgeExcess = edgeExcess;
            }
            public IList<XYZ> Path { get; }
            public double Coverage { get; }
            public double BoundarySupport { get; }
            public double EdgeExcess { get; }
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
