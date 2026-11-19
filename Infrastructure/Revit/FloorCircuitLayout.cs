using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Underfloor_heating_generator.Application.Models;
using XYZ = Autodesk.Revit.DB.XYZ;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    internal static class FloorCircuitLayout
    {
        private const double FeetToMeters = 0.3048;
        private const double GridPerFoot = 3048000; // 0.0001 mm per clipping unit.
        private const int MaximumCircuits = 64;
        private const double OutletSpacingFeet = 150 / 304.8;

        internal sealed record Circuit(IList<XYZ> Points, double AreaSquareMeters,
            double LengthMeters, bool ConnectsToSelectedPipe);

        internal static IReadOnlyList<Circuit> Build(IList<IList<XYZ>> boundaries, XYZ selectedEnd,
            XYZ selectedDirection, double pitchMillimeters, double wallOffsetMillimeters,
            double diameterMillimeters, double levelElevation, double heightMillimeters,
            FloorCircuitLimits limits)
        {
            if (boundaries.Count == 0 || limits == null || limits.GetValidationError() != null ||
                !double.IsFinite(pitchMillimeters) || !double.IsFinite(wallOffsetMillimeters) ||
                !double.IsFinite(diameterMillimeters) || pitchMillimeters <= diameterMillimeters ||
                diameterMillimeters <= 0 || wallOffsetMillimeters < 0)
                return Array.Empty<Circuit>();

            double directionLength = Math.Sqrt(selectedDirection.X * selectedDirection.X +
                selectedDirection.Y * selectedDirection.Y);
            if (directionLength < 1e-9)
                return Array.Empty<Circuit>();
            var frame = new Frame(selectedEnd, -selectedDirection.X / directionLength,
                -selectedDirection.Y / directionLength);
            var source = new Paths64(boundaries.Select(loop => new Path64(loop.Select(point =>
                new Point64(frame.Along(point) * GridPerFoot, frame.Across(point) * GridPerFoot)))));
            Paths64 region = Clipper.Union(source, FillRule.EvenOdd);
            if (region.Count == 0)
                return Array.Empty<Circuit>();

            Rect64 bounds = Clipper.GetBounds(region);
            double area = Area(region);
            double areaLimit = limits.MaximumAreaSquareMeters ?? double.PositiveInfinity;
            double lengthAllowance = limits.MaximumLengthMeters.GetValueOrDefault() -
                2 * limits.CollectorDistanceMeters.GetValueOrDefault();
            if (!limits.MaximumLengthMeters.HasValue)
                lengthAllowance = double.PositiveInfinity;
            double firstCount = Math.Max(1, Math.Ceiling(area / areaLimit - 1e-9));
            if (area <= 0 || firstCount > MaximumCircuits)
                return Array.Empty<Circuit>();

            double pitchFeet = pitchMillimeters / 304.8;
            double diameterFeet = diameterMillimeters / 304.8;
            double dividerClearance = Math.Max(pitchFeet / 2, diameterFeet);
            if (diameterFeet >= OutletSpacingFeet)
                return Array.Empty<Circuit>();
            double levelOfPipe = levelElevation + heightMillimeters / 304.8;
            XYZ inward = frame.Inward;
            // Virtual boundaries separate circuits by a pitch without applying the
            // full wall setback twice to the shared seam between adjacent strips.
            double virtualBoundaryShift = (wallOffsetMillimeters * 0.98 + diameterMillimeters / 2) / 304.8 -
                dividerClearance;
            long[] recessCuts = FindRecessLevels(region, bounds, pitchFeet);
            var openings = region.Where(loop => Clipper.Area(loop) < 0).Select(loop =>
            {
                var boundary = loop.Select(p => frame.ToWorld(p.X / GridPerFoot, p.Y / GridPerFoot, 0))
                    .ToList();
                boundary.Add(boundary[0]);
                return boundary;
            }).ToList();
            double openingClearance = Math.Max(0, wallOffsetMillimeters * 0.98 +
                diameterMillimeters / 2 - 0.01) / 304.8;
            double[] outletShifts = openings.Count > 0
                ? new[] { 0.0, -0.5, 0.5 }
                : new[] { 0.0, -0.5, 0.5, -1.0, 1.0 };
            var routes = new Dictionary<(long Low, long High, double Start, double End, bool IsAnchor),
                IList<XYZ>>();
            var checkedPartitions = new HashSet<string>();
            // With several changes of wall level, the first plan that fits can
            // still leave a gap where the division crosses a recess.
            bool compareCoverage = openings.Count == 0 && recessCuts.Length > 4;
            IReadOnlyList<XYZ> coverageSamples = compareCoverage
                ? CoverageSamples(region, bounds, frame, pitchFeet, openingClearance) : Array.Empty<XYZ>();
            IReadOnlyList<Circuit> bestPlan = null;
            double bestGapSquared = double.PositiveInfinity;
            double preferredGapSquared = pitchFeet * pitchFeet * 0.64;
            int firstCompleteCount = 0;

            for (int count = (int)firstCount; count <= MaximumCircuits; count++)
            {
                if ((double)(bounds.bottom - bounds.top) / GridPerFoot / count <
                    2 * dividerClearance + Math.Min(OutletSpacingFeet, pitchFeet * 0.88))
                    break;

                long[] equalCuts = DivideByArea(region, bounds, area, count);
                IReadOnlyList<Circuit> circuits = TryApproaches(equalCuts);
                if (circuits != null)
                    return circuits;

                if (recessCuts.Length > 2 && count > 1)
                {
                    // A divider across a recess can leave an unrouteably thin
                    // ledge. Align useful dividers with the real wall levels
                    // without forcing a separate circuit at every change of wall.
                    IEnumerable<long> levels = recessCuts.Skip(1).SkipLast(1)
                        .OrderBy(level => equalCuts.Skip(1).SkipLast(1).Min(cut => Math.Abs(cut - level)));
                    foreach (long level in levels)
                    {
                        long[] aligned = DivideAtRecesses(region, bounds,
                            new[] { bounds.top, level, bounds.bottom }, areaLimit, count);
                        if (aligned != null && TryApproaches(aligned) is { } alignedCircuits)
                            return alignedCircuits;
                    }
                    long[] allAligned = DivideAtRecesses(region, bounds, recessCuts, areaLimit, count);
                    if (allAligned != null && TryApproaches(allAligned) is { } allCircuits)
                        return allCircuits;
                }

                if ((openings.Count > 0 || recessCuts.Length > 2) && count > 1)
                {
                    // Move the dividers together to give the passes on one side
                    // of an opening space without creating an extra narrow circuit.
                    foreach (double phase in new[] { -0.5, 0.5, -1.0, 1.0, -0.125, 0.125, -0.25, 0.25 })
                    {
                        long shift = (long)Math.Round(phase * pitchFeet * GridPerFoot);
                        long[] shifted = equalCuts.Select((cut, index) =>
                            index == 0 || index == count ? cut : cut + shift).ToArray();
                        if (shifted[1] <= bounds.top || shifted[^2] >= bounds.bottom)
                            continue;
                        circuits = TryApproaches(shifted);
                        if (circuits != null)
                            return circuits;
                    }
                }
                // Consider one extra circuit if it improves a complete plan.
                if (bestPlan != null && count > firstCompleteCount)
                    break;
            }
            return bestPlan ?? Array.Empty<Circuit>();

            IReadOnlyList<Circuit> TryApproaches(long[] cuts)
            {
                if (!checkedPartitions.Add(string.Join(",", cuts)))
                    return null;
                double[] stripAreas = cuts.Zip(cuts.Skip(1), (low, high) =>
                    Area(Clip(region, bounds, low, high))).ToArray();
                if (stripAreas.Any(stripArea => stripArea <= 0 || stripArea > areaLimit + 1e-6))
                    return null;
                foreach (double shift in outletShifts)
                {
                    IReadOnlyList<Circuit> result = TryBuildForCuts(cuts, stripAreas, shift);
                    if (result == null)
                        continue;
                    if (!compareCoverage)
                        return result;
                    if (firstCompleteCount == 0)
                        firstCompleteCount = result.Count;
                    double gapSquared = GetLargestCoverageGap(result, coverageSamples, bestGapSquared);
                    if (gapSquared < bestGapSquared)
                    {
                        bestGapSquared = gapSquared;
                        bestPlan = result;
                    }
                    if (gapSquared <= preferredGapSquared)
                        return result;
                }
                return null;
            }

            IReadOnlyList<Circuit> TryBuildForCuts(long[] cuts, double[] stripAreas,
                double outletShift)
            {
                int count = cuts.Length - 1;
                int anchorStrip = Math.Clamp(Array.FindLastIndex(cuts, cut => cut <= 0), 0, count - 1);
                var circuits = new List<Circuit>(count);
                for (int i = 0; i < count; i++)
                {
                    long low = cuts[i], high = cuts[i + 1];

                    long shift = (long)Math.Round(virtualBoundaryShift * GridPerFoot);
                    // Expanding across an actual change of outline would add a
                    // sliver of the adjacent recess that cannot house a pipe.
                    long layoutLow = i == 0 || IsRecess(low) ? low : Math.Max(bounds.top, low - shift);
                    long layoutHigh = i == count - 1 || IsRecess(high) ? high : Math.Min(bounds.bottom, high + shift);
                    // Use the full width of every strip up to the real wall.
                    Paths64 strip = Clip(region, bounds, layoutLow, layoutHigh);
                    IList<IList<XYZ>> layout = strip
                        .Select(loop => (IList<XYZ>)loop.Select(point => frame.ToWorld(
                            (double)point.X / GridPerFoot, (double)point.Y / GridPerFoot, levelElevation)).ToList())
                        .ToList();
                    bool isAnchor = i == anchorStrip;
                    double minAcross = low / GridPerFoot;
                    double maxAcross = high / GridPerFoot;
                    if (layout.Count == 0 || maxAcross - minAcross <= OutletSpacingFeet + diameterFeet)
                        return null;

                    double center = (minAcross + maxAcross) / 2;
                    double wallAlong = Clipper.GetBounds(strip).right / GridPerFoot;
                    double exitAlong = isAnchor && wallAlong <= 1e-6 ? 0 :
                        Math.Max(0, wallAlong + Math.Max(2 * diameterFeet, 25 / 304.8));
                    double lowestOutlet = minAcross + diameterFeet / 2;
                    double highestOutlet = maxAcross - diameterFeet / 2 - OutletSpacingFeet;
                    double preferredOutlet = center + outletShift * pitchFeet - OutletSpacingFeet / 2;
                    IEnumerable<(double First, double Second)> pairs = isAnchor
                        ? new[] { OutletSpacingFeet, -OutletSpacingFeet }
                            .OrderBy(across => Math.Abs(across - center)).Select(across => (0.0, across))
                        : GetOutletPositions(preferredOutlet, lowestOutlet, highestOutlet)
                            .Select(across => (across, across + OutletSpacingFeet));
                    Circuit accepted = null;
                    foreach (var (firstAcross, secondAcross) in pairs)
                    {
                        if (secondAcross < minAcross || secondAcross > maxAcross)
                            continue;
                        XYZ start = isAnchor ? selectedEnd : frame.ToWorld(exitAlong, firstAcross, levelOfPipe);
                        XYZ end = frame.ToWorld(exitAlong, secondAcross, isAnchor ? selectedEnd.Z : levelOfPipe);
                        var key = (layoutLow, layoutHigh, firstAcross, secondAcross, isAnchor);
                        if (!routes.TryGetValue(key, out IList<XYZ> points))
                        {
                            points = AreaPipeLayout.CreateSpiralPath(layout, pitchMillimeters,
                                wallOffsetMillimeters, diameterMillimeters, levelElevation, heightMillimeters,
                                start, isAnchor ? selectedDirection : inward, end, inward);
                            if (points.Count == 0 && isAnchor && exitAlong < pitchFeet)
                            {
                                // A pipe ending at the wall fixes the inlet
                                // position. Give the free return room to turn
                                // beyond it, while keeping its parallel axis.
                                XYZ extendedReturn = frame.ToWorld(exitAlong + 2 * pitchFeet,
                                    secondAcross, selectedEnd.Z);
                                points = AreaPipeLayout.CreateSpiralPath(layout, pitchMillimeters,
                                    wallOffsetMillimeters, diameterMillimeters, levelElevation, heightMillimeters,
                                    start, selectedDirection, extendedReturn, inward);
                            }
                            double shortExit = wallAlong + Math.Max(diameterFeet, 25 / 304.8);
                            if (points.Count == 0 && !isAnchor && strip.Count > 1 &&
                                (exitAlong - shortExit) * 304.8 > 2.55)
                            {
                                IList<XYZ> closePath = AreaPipeLayout.CreateSpiralPath(layout, pitchMillimeters,
                                    wallOffsetMillimeters, diameterMillimeters, levelElevation, heightMillimeters,
                                    frame.ToWorld(shortExit, firstAcross, levelOfPipe), inward,
                                    frame.ToWorld(shortExit, secondAcross, levelOfPipe), inward);
                                if (closePath.Count > 0)
                                {
                                    double drawingZ = levelOfPipe - heightMillimeters / 304.8;
                                    points = closePath.Prepend(frame.ToWorld(exitAlong, firstAcross, drawingZ))
                                        .Append(frame.ToWorld(exitAlong, secondAcross, drawingZ)).ToList();
                                }
                            }
                            routes.Add(key, points);
                        }
                        if (points.Count < 3 ||
                            openings.Any(opening => !HaveClearance(points, opening, openingClearance)) ||
                            circuits.Any(previous => !HaveClearance(previous.Points, points, diameterFeet)))
                            continue;
                        double length = points.Zip(points.Skip(1), (a, b) => a.DistanceTo(b)).Sum() * FeetToMeters;
                        if (length > lengthAllowance + 1e-6)
                            continue;
                        accepted = new Circuit(points, stripAreas[i], length, isAnchor);
                        break;
                    }
                    if (accepted == null)
                        return null;
                    circuits.Add(accepted);
                }
                return circuits;

                bool IsRecess(long cut) => Array.BinarySearch(recessCuts, cut) >= 0;
            }

            IEnumerable<double> GetOutletPositions(double preferred, double min, double max)
            {
                var positions = new List<double> { Math.Clamp(preferred, min, max) };
                if (openings.Count > 0)
                {
                    // If a cutout blocks an approach, move the pair along its
                    // strip together without narrowing the 150 mm spacing.
                    foreach (List<XYZ> opening in openings)
                    {
                        double before = opening.Min(frame.Across) - openingClearance - OutletSpacingFeet - 0.05 / 304.8;
                        double after = opening.Max(frame.Across) + openingClearance + 0.05 / 304.8;
                        if (before >= min && before <= max)
                            positions.Add(before);
                        if (after >= min && after <= max)
                            positions.Add(after);
                    }
                    positions.Add(min + (max - min) * 0.15);
                    positions.Add(min + (max - min) * 0.85);
                }
                return positions.Distinct().OrderBy(across => Math.Abs(across - preferred));
            }
        }

        private static IReadOnlyList<XYZ> CoverageSamples(Paths64 region, Rect64 bounds, Frame frame,
            double pitch, double clearance)
        {
            long margin = (long)Math.Ceiling(clearance * GridPerFoot);
            long step = Math.Max(1, (long)Math.Round(pitch * 0.4 * GridPerFoot));
            Paths64 inset = Clipper.InflatePaths(region, -margin, JoinType.Round, EndType.Polygon);
            var samples = new List<XYZ>();
            for (long x = bounds.left + margin; x < bounds.right - margin; x += step)
            for (long y = bounds.top + margin; y < bounds.bottom - margin; y += step)
            {
                var point = new Point64(x, y);
                if (inset.Any(loop => Clipper.PointInPolygon(point, loop) != PointInPolygonResult.IsOutside))
                    samples.Add(frame.ToWorld(x / GridPerFoot, y / GridPerFoot, 0));
            }
            return samples;
        }

        private static double GetLargestCoverageGap(IReadOnlyList<Circuit> circuits,
            IReadOnlyList<XYZ> samples, double bestGapSquared)
        {
            double largest = 0;
            foreach (XYZ point in samples)
            {
                double closest = double.PositiveInfinity;
                foreach (Circuit circuit in circuits)
                for (int i = 0; i + 1 < circuit.Points.Count; i++)
                    closest = Math.Min(closest, PointDistanceSquared(point, circuit.Points[i], circuit.Points[i + 1]));
                largest = Math.Max(largest, closest);
                if (largest >= bestGapSquared)
                    return largest;
            }
            return largest;
        }

        private static long[] FindRecessLevels(Paths64 region, Rect64 bounds, double pitchFeet)
        {
            var cuts = new SortedSet<long> { bounds.top, bounds.bottom };
            foreach (Path64 loop in region)
            for (int i = 0; i < loop.Count; i++)
            {
                Point64 a = loop[i], b = loop[(i + 1) % loop.Count];
                if (a.Y == b.Y && Math.Abs(a.X - b.X) >= 2 * pitchFeet * GridPerFoot)
                    cuts.Add(a.Y);
            }
            return cuts.ToArray();
        }

        private static long[] DivideAtRecesses(Paths64 region, Rect64 bounds, long[] levels,
            double areaLimit, int count)
        {
            if (levels.Length - 1 > count)
                return null;
            var sections = new List<(Rect64 Bounds, double Area, int Count)>();
            for (int i = 0; i + 1 < levels.Length; i++)
            {
                double area = Area(Clip(region, bounds, levels[i], levels[i + 1]));
                if (area <= 0)
                    return null;
                double minimumCount = Math.Max(1, Math.Ceiling(area / areaLimit - 1e-9));
                if (minimumCount > count)
                    return null;
                sections.Add((new Rect64(bounds.left, levels[i], bounds.right, levels[i + 1]),
                    area, (int)minimumCount));
            }
            int used = sections.Sum(section => section.Count);
            if (used > count)
                return null;
            while (used++ < count)
            {
                int largest = Enumerable.Range(0, sections.Count)
                    .OrderByDescending(i => sections[i].Area / sections[i].Count).First();
                var section = sections[largest];
                sections[largest] = (section.Bounds, section.Area, section.Count + 1);
            }
            return sections.SelectMany(section => DivideByArea(region, section.Bounds,
                section.Area, section.Count).SkipLast(1)).Append(bounds.bottom).ToArray();
        }

        private static long[] DivideByArea(Paths64 region, Rect64 bounds, double area, int count)
        {
            var cuts = new long[count + 1];
            cuts[0] = bounds.top;
            cuts[count] = bounds.bottom;
            for (int i = 1; i < count; i++)
            {
                double wanted = area * i / count;
                long low = cuts[i - 1], high = bounds.bottom;
                while (high - low > 1)
                {
                    long middle = low + (high - low) / 2;
                    if (Area(Clip(region, bounds, bounds.top, middle)) < wanted)
                        low = middle;
                    else
                        high = middle;
                }
                cuts[i] = high;
            }
            return cuts;
        }

        private static Paths64 Clip(Paths64 region, Rect64 bounds, long low, long high)
        {
            if (high <= low || bounds.right <= bounds.left)
                return new Paths64();
            var clip = new Path64
            {
                new Point64(bounds.left, low), new Point64(bounds.right, low),
                new Point64(bounds.right, high), new Point64(bounds.left, high)
            };
            return Clipper.Intersect(region, new Paths64 { clip }, FillRule.NonZero);
        }

        private static double Area(Paths64 region) =>
            Math.Abs(Clipper.Area(region)) / (GridPerFoot * GridPerFoot) * FeetToMeters * FeetToMeters;

        private static bool HaveClearance(IList<XYZ> first, IList<XYZ> second, double clearance)
        {
            double squared = clearance * clearance;
            for (int i = 0; i + 1 < first.Count; i++)
            for (int j = 0; j + 1 < second.Count; j++)
            {
                XYZ a = first[i], b = first[i + 1], c = second[j], d = second[j + 1];
                if (Math.Max(a.X, b.X) + clearance < Math.Min(c.X, d.X) ||
                    Math.Max(c.X, d.X) + clearance < Math.Min(a.X, b.X) ||
                    Math.Max(a.Y, b.Y) + clearance < Math.Min(c.Y, d.Y) ||
                    Math.Max(c.Y, d.Y) + clearance < Math.Min(a.Y, b.Y))
                    continue;
                if (Cross(a, b, c) * Cross(a, b, d) < 0 &&
                    Cross(c, d, a) * Cross(c, d, b) < 0)
                    return false;
                if (PointDistanceSquared(a, c, d) < squared || PointDistanceSquared(b, c, d) < squared ||
                    PointDistanceSquared(c, a, b) < squared || PointDistanceSquared(d, a, b) < squared)
                    return false;
            }
            return true;
        }

        private static double Cross(XYZ a, XYZ b, XYZ c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static double PointDistanceSquared(XYZ p, XYZ a, XYZ b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = dx * dx + dy * dy;
            double t = length <= 1e-18 ? 0 :
                Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
            double x = p.X - a.X - t * dx, y = p.Y - a.Y - t * dy;
            return x * x + y * y;
        }

        private readonly record struct Frame(XYZ Origin, double X, double Y)
        {
            public double Along(XYZ point) => (point.X - Origin.X) * X + (point.Y - Origin.Y) * Y;
            public double Across(XYZ point) => (point.Y - Origin.Y) * X - (point.X - Origin.X) * Y;
            public XYZ Inward => new XYZ(-X, -Y, 0);
            public XYZ ToWorld(double along, double across, double z) =>
                new XYZ(Origin.X + along * X - across * Y, Origin.Y + along * Y + across * X, z);
        }
    }
}
