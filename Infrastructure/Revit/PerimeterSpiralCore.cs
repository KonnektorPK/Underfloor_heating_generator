using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Underfloor_heating_generator.Infrastructure.Geometry;

// Revit-independent perimeter-first, counter-flow spiral geometry core.
namespace Underfloor_heating_generator.Infrastructure.Revit;

internal static class PerimeterSpiralCore
{
    private const double Epsilon = 1e-7;
    private const double ClipperScale = 1000.0;
    private const double OffsetArcToleranceMillimeters = 0.15;

    internal static bool TryBuildBoundaryLap(IReadOnlyList<Point> sourceBoundary,
        double pitch, double centerlineClearance, out List<Point> route)
    {
        return TryBuildBoundaryLap(sourceBoundary, pitch, centerlineClearance, null, out route);
    }

    internal static bool TryBuildBoundaryLap(IReadOnlyList<Point> sourceBoundary,
        double pitch, double centerlineClearance, Point? seamTarget, out List<Point> route)
    {
        return TryBuildBoundaryLap(sourceBoundary, pitch, centerlineClearance, seamTarget,
            pitch * 0.25, out route);
    }

    internal static bool TryBuildBoundaryLap(IReadOnlyList<Point> sourceBoundary,
        double pitch, double centerlineClearance, Point? seamTarget, double openingLength,
        out List<Point> route)
    {
        return TryBuildBoundaryBand(sourceBoundary, pitch, centerlineClearance,
            seamTarget, openingLength, 1, out route, out _);
    }

    internal static bool TryBuildBoundaryBand(IReadOnlyList<Point> sourceBoundary,
        double pitch, double centerlineClearance, Point? seamTarget, double openingLength,
        int maximumLaps, out List<Point> route, out int completedLaps)
    {
        route = new List<Point>();
        completedLaps = 0;
        if (sourceBoundary == null || pitch <= 0 || centerlineClearance < 0 || maximumLaps <= 0)
            return false;

        List<Point> boundary = Clean(sourceBoundary);
        if (boundary.Count < 3 || Math.Abs(SignedArea(boundary)) < Epsilon)
            return false;
        if (SignedArea(boundary) < 0)
            boundary.Reverse();

        double cornerRadius = GetDefaultCornerRadius(pitch, centerlineClearance);
        // The vertex centroid can fall outside the deep inset contours of asymmetric
        // rooms. Use a max-clearance interior point as the stable ray origin for every lap.
        Point center = FindInteriorCenter(boundary, pitch);
        var routePoints = new List<Point>();

        for (int layer = 0; layer < maximumLaps; layer++)
        {
            double offsetDistance = centerlineClearance + layer * pitch;
            if (!TryOffset(boundary, offsetDistance, cornerRadius, out List<Point> offset) ||
                !IsSimple(offset) || !IsInsideBoundary(offset, boundary, offsetDistance) ||
                MinimumWidth(offset) < pitch * 1.8)
                break;

            double gap = layer == 0
                ? Math.Min(Math.Max(pitch * 0.25, openingLength), Perimeter(offset) * 0.1)
                : Math.Min(pitch * 0.25, Perimeter(offset) * 0.1);
            bool clockwise = (layer & 1) != 0;
            Point anchor;
            if (layer == 0)
            {
                double maximumX = offset.Max(point => point.X);
                List<Point> supportVertices = offset.Where(point => maximumX - point.X < 1e-5).ToList();
                Point seamPoint = supportVertices.Count >= 2
                    ? new Point(maximumX, supportVertices.Average(point => point.Y))
                    : supportVertices[0];
                Point seamDirection = Normalize(seamPoint - center);
                anchor = seamTarget.HasValue
                    ? OrderFromAnchor(offset, seamTarget.Value, clockwise: false)[0]
                    : RayBoundaryIntersection(offset, center, seamDirection);
                if (seamTarget.HasValue)
                    anchor = GetLayerAnchor(offset, anchor, gap * 0.5);
            }
            else
            {
                anchor = OrderFromAnchor(offset, routePoints[^1], clockwise)[0];
            }

            int routeCountBeforeLayer = routePoints.Count;
            if (!AppendNearlyClosedRing(offset, pitch, gap, clockwise, anchor, routePoints, out _))
            {
                if (routePoints.Count > routeCountBeforeLayer)
                    routePoints.RemoveRange(routeCountBeforeLayer, routePoints.Count - routeCountBeforeLayer);
                break;
            }
            completedLaps++;
        }

        if (completedLaps == 0 ||
            !TryRoundPolyline(routePoints, cornerRadius, 20.0, closed: false,
                out List<Point> roundedRoute))
            return false;

        route = roundedRoute;
        return IsSimplePolyline(route) &&
               IsPathInsideWithClearance(route, boundary, centerlineClearance);
    }

    internal static bool TryBuild(IReadOnlyList<IReadOnlyList<Point>> boundaries,
        double pitch, double centerlineClearance, out List<Point> route)
    {
        return TryBuild(boundaries, pitch, centerlineClearance, null, pitch * 0.25, out route);
    }

    internal static bool TryBuild(IReadOnlyList<IReadOnlyList<Point>> boundaries,
        double pitch, double centerlineClearance, Point? seamTarget, double openingLength,
        out List<Point> route)
    {
        route = new List<Point>();
        if (boundaries == null || boundaries.Count == 0 || pitch <= 0 || centerlineClearance < 0)
            return false;

        if (boundaries.Count > 1)
            return TryBuildCompound(boundaries, pitch, centerlineClearance, seamTarget,
                openingLength, out route);

        List<Point> boundary = Clean(boundaries[0]);
        if (boundary.Count < 3 || Math.Abs(SignedArea(boundary)) < Epsilon)
            return false;
        if (SignedArea(boundary) < 0)
            boundary.Reverse();

        double cornerRadius = GetDefaultCornerRadius(pitch, centerlineClearance);

        var rings = new List<List<Point>>();
        for (int level = 0; level < 256; level++)
        {
            if (!TryOffset(boundary, centerlineClearance + level * pitch, cornerRadius,
                    out List<Point> offset) ||
                offset.Count < 3 || Math.Abs(SignedArea(offset)) < pitch * pitch * 0.05)
                break;
            if (!IsSimple(offset) || !IsInsideBoundary(offset, boundary, centerlineClearance + level * pitch))
                break;
            if (MinimumWidth(offset) < pitch * 1.8 || Perimeter(offset) < pitch * 2.5)
                break;
            rings.Add(offset);
        }

        // A usable bifilar circuit needs at least two offset lanes so supply and return
        // can occupy alternating lanes and turn around inside the region.
        if (rings.Count < 2)
            return TryBuildCompound(boundaries, pitch, centerlineClearance, seamTarget,
                openingLength, out route);

        int deepestEven = Enumerable.Range(0, rings.Count).Where(i => (i & 1) == 0).DefaultIfEmpty(-1).Max();
        int deepestOdd = Enumerable.Range(0, rings.Count).Where(i => (i & 1) != 0).DefaultIfEmpty(-1).Max();
        var inwardLevels = Enumerable.Range(0, deepestEven / 2 + 1).Select(i => i * 2).ToList();
        var outwardLevels = Enumerable.Range(0, (deepestOdd + 1) / 2).Select(i => deepestOdd - i * 2)
            .Where(i => i >= 1).ToList();
        if (deepestEven < 0 || outwardLevels.Count == 0)
            return false;

        // The vertex centroid can be inside the source polygon but outside deeper
        // inset rings on asymmetric shapes. Use the maximum-clearance point so every
        // radial seam ray remains anchored inside every retained offset.
        Point center = FindInteriorCenter(boundary, pitch);
        Point seamDirection = FindStableSeamDirection(boundary, center);
        // The interleaved bridges share the opening in each lap. Give both runs
        // room to turn without pinching them together at the return lanes.
        double gap = Math.Min(pitch * 1.35, rings.Min(Perimeter) * 0.1);
        double exitGap = Math.Min(Math.Max(gap, openingLength),
            Math.Min(Perimeter(rings[0]) * 0.1, pitch * 2.5));
        Point requestedSeam = seamTarget ?? RayBoundaryIntersection(rings[0], center, seamDirection);
        if (TryBuildAlignedCounterFlow(boundary, rings, inwardLevels, outwardLevels,
            pitch, centerlineClearance, cornerRadius, gap, exitGap, center, requestedSeam, out route))
            return true;

        if (seamTarget.HasValue)
        {
            Point requestedDirection = Normalize(seamTarget.Value - center);
            if (requestedDirection.Length > Epsilon)
                seamDirection = requestedDirection;
            if (TryBuildSeamAwareRoute(rings, inwardLevels, outwardLevels, pitch, gap,
                    exitGap, center, seamDirection, seamTarget.Value, boundary,
                    centerlineClearance, out List<Point> seamRoute) &&
                TryRoundPolyline(seamRoute, cornerRadius, 20.0, closed: false,
                    out List<Point> roundedSeamRoute) &&
                HasRoomBetweenRuns(roundedSeamRoute, pitch, pitch * 0.25) &&
                IsPathInsideWithClearance(roundedSeamRoute, boundary, centerlineClearance) &&
                IsQualityAcceptable(boundary, roundedSeamRoute, pitch, centerlineClearance))
            {
                route = roundedSeamRoute;
                RemoveDuplicates(route);
                return true;
            }
        }
        if (!TryBuildCompound(boundaries, pitch, centerlineClearance, seamTarget,
                openingLength, out List<Point> compoundRoute) ||
            !IsQualityAcceptable(boundary, compoundRoute, pitch, centerlineClearance))
            return false;
        route = compoundRoute;
        return true;
    }

    private static bool TryBuildAlignedCounterFlow(IReadOnlyList<Point> boundary,
        IReadOnlyList<List<Point>> rings, IReadOnlyList<int> inwardLevels,
        IReadOnlyList<int> outwardLevels, double pitch, double clearance,
        double cornerRadius, double gap, double exitGap, Point center,
        Point seamTarget, out List<Point> route)
    {
        route = new List<Point>();
        // Keep one common seam through every inset. This lets inward connectors pass
        // through the openings left in the return contours without crossing pipe.
        // Include an 11.25 degree angular lattice (the legacy safe samples) at
        // quarter-step resolution so a narrow valid seam is not skipped.
        const int seamDirections = 128;
        Point targetDirection = Normalize(seamTarget - center);
        if (targetDirection.Length <= Epsilon)
            targetDirection = new Point(1, 0);
        double preferredAngle = Math.Atan2(targetDirection.Y, targetDirection.X);
        IEnumerable<double> directions = Enumerable.Range(0, seamDirections)
            .Select(index => index * (2.0 * Math.PI / seamDirections))
            .Append(preferredAngle)
            .OrderBy(angle => Math.Abs(Math.Atan2(Math.Sin(angle - preferredAngle),
                Math.Cos(angle - preferredAngle))));
        List<Point> best = null;
        foreach (double angle in directions)
        {
            Point direction = new(Math.Cos(angle), Math.Sin(angle));
            var candidate = new List<Point>();
            bool complete = true;

            foreach (int level in inwardLevels)
            {
                double ringGap = level == 0 ? exitGap : gap;
                Point rayAnchor = RayBoundaryIntersection(rings[level], center, direction);
                // Leave the outer opening as a mouth: the inward bridge enters at one
                // edge while the supply lead attaches at the other.
                Point anchor = AdvanceOnRing(rings[level], rayAnchor,
                    level == 0 ? ringGap : ringGap * 0.5, clockwise: false);
                if (!AppendNearlyClosedRing(rings[level], pitch, ringGap, clockwise: false,
                        anchor, candidate, out _, pitch * 5.0))
                {
                    complete = false;
                    break;
                }
            }

            if (!complete)
                continue;

            foreach (int level in outwardLevels)
            {
                double ringGap = level == 1 ? exitGap : gap;
                Point rayAnchor = RayBoundaryIntersection(rings[level], center, direction);
                // The first return contour starts at the inward bridge portal and
                // finishes at the opposite edge of the shared exterior mouth.
                Point anchor = level == 1 ? rayAnchor :
                    AdvanceOnRing(rings[level], rayAnchor, ringGap * 0.5, clockwise: true);
                if (!AppendNearlyClosedRing(rings[level], pitch, ringGap, clockwise: true,
                        anchor, candidate, out _, pitch * 5.0))
                {
                    complete = false;
                    break;
                }
            }

            if (!complete)
                continue;
            if (candidate.Count < 4 || !IsSimplePolyline(candidate))
                continue;
            if (!IsPathInsideWithClearance(candidate, boundary, clearance))
                continue;
            if (!TryRoundPolyline(candidate, cornerRadius, 20.0, closed: false, out List<Point> rounded))
                continue;
            if (!HasRoomBetweenRuns(rounded, pitch, pitch * 0.25))
                continue;
            if (!IsPathInsideWithClearance(rounded, boundary, clearance))
                continue;
            if (!IsQualityAcceptable(boundary, rounded, pitch, clearance))
                continue;

            best = rounded;
            break;
        }

        if (best == null)
            return false;
        route = best;
        RemoveDuplicates(route);
        return true;
    }

    private static bool TryBuildCompound(IReadOnlyList<IReadOnlyList<Point>> sourceBoundaries,
        double pitch, double clearance, Point? seamTarget, double openingLength,
        out List<Point> route)
    {
        route = new List<Point>();
        List<List<Point>> boundaries = sourceBoundaries.Select(Clean)
            .Where(loop => loop.Count >= 3 && Math.Abs(SignedArea(loop)) > Epsilon).ToList();
        if (boundaries.Count != sourceBoundaries.Count || boundaries.Count < 1)
            return false;

        List<Path64> subjects = new();
        for (int i = 0; i < boundaries.Count; i++)
        {
            int nestingDepth = 0;
            for (int j = 0; j < boundaries.Count; j++)
            {
                if (i != j && PointInside(boundaries[j], boundaries[i][0]))
                    nestingDepth++;
            }

            Path64 path = ToClipperPath(boundaries[i]);
            bool wantPositive = (nestingDepth & 1) == 0;
            if ((Clipper.Area(path) > 0) != wantPositive)
                path.Reverse();
            subjects.Add(path);
        }

        var contours = new List<CompoundContour>();
        for (int level = 0; level < 256; level++)
        {
            double distance = clearance + level * pitch;
            Paths64 solution = OffsetCompound(subjects, distance);
            if (solution.Count == 0)
                break;
            foreach (Path64 path in solution)
            {
                bool isOuter = Clipper.Area(path) > 0;
                List<Point> points = FromClipperPath(path);
                bool simpleContour = points.Count >= 3 && IsSimple(points);
                bool contained = simpleContour && IsPathInsideRegionWithClearance(points, boundaries, clearance);
                if (simpleContour && contained)
                    contours.Add(new CompoundContour(level, isOuter, points));
            }
        }

        if (contours.Count < 3)
            return false;

        CompoundContour root = contours.Where(contour => contour.Level == 0 && contour.IsOuter)
            .OrderByDescending(contour => Math.Abs(SignedArea(contour.Points))).FirstOrDefault();
        if (root == null)
            return false;

        double seamGap = Math.Min(Math.Max(pitch * 0.25, openingLength), pitch * 1.5);
        var remaining = new HashSet<CompoundContour>(contours);
        remaining.Remove(root);
        List<Point> outerBoundary = boundaries.OrderByDescending(loop => Math.Abs(SignedArea(loop))).First();
        Point initialAnchor = GetContourAnchor(root.Points, seamTarget, outerBoundary, false);
        if (!AppendNearlyClosedRing(root.Points, pitch,
                Math.Min(seamGap, Perimeter(root.Points) * 0.08), false,
                initialAnchor, route, out _))
            return false;

        double bridgeLimit = Math.Max(pitch * 12.0, Math.Sqrt(
            Math.Pow(outerBoundary.Max(point => point.X) - outerBoundary.Min(point => point.X), 2) +
            Math.Pow(outerBoundary.Max(point => point.Y) - outerBoundary.Min(point => point.Y), 2)));
        CompoundContour previousContour = root;
        while (remaining.Count > 0)
        {
            CompoundContour selected = null;
            List<Point> selectedRoute = null;
            double bestScore = double.PositiveInfinity;
            foreach (CompoundContour contour in remaining.OrderBy(contour => contour.Level))
            foreach (bool clockwise in new[] { false, true })
            {
                Point anchor = OrderFromAnchor(contour.Points, route[^1], clockwise)[0];
                double bridgeLength = Distance(route[^1], anchor);
                if (bridgeLength > bridgeLimit ||
                    !IsPathInsideRegionWithClearance(new[] { route[^1], anchor }, boundaries, clearance))
                    continue;

                var candidate = new List<Point>(route);
                if (!AppendNearlyClosedRing(contour.Points, pitch,
                        Math.Min(pitch * 0.25, Perimeter(contour.Points) * 0.08),
                        clockwise, anchor, candidate, out _, bridgeLimit) ||
                    !PolylineTailIsSimple(route, candidate))
                    continue;

                double score = bridgeLength + Math.Abs(contour.Level - previousContour.Level) * pitch * 0.1 +
                    CompoundTransitionPenalty(previousContour, contour, bridgeLimit);
                if (score >= bestScore) continue;
                selected = contour;
                selectedRoute = candidate;
                bestScore = score;
            }

            if (selected == null)
            {
                Point currentEnd = route[^1];
                foreach (CompoundContour contour in remaining.OrderBy(contour =>
                             DistanceBetweenLoops(new[] { currentEnd }, contour.Points)))
                {
                    if (!TryAppendCompoundContourWithConnector(route, contour, pitch, bridgeLimit,
                            boundaries, clearance, out List<Point> candidate, out double connectorLength))
                        continue;
                    double score = connectorLength + Math.Abs(contour.Level - previousContour.Level) * pitch * 0.1 +
                        CompoundTransitionPenalty(previousContour, contour, bridgeLimit);
                    if (score >= bestScore) continue;
                    selected = contour;
                    selectedRoute = candidate;
                    bestScore = score;
                }
            }

            if (selected == null || selectedRoute == null)
                return false;
            route = selectedRoute;
            remaining.Remove(selected);
            previousContour = selected;
        }

        RemoveDuplicates(route);
        bool simple = IsSimplePolyline(route);
        bool inside = IsPathInsideRegionWithClearance(route, boundaries, clearance);
        return simple && inside;
    }

    private static double CompoundTransitionPenalty(CompoundContour previous,
        CompoundContour candidate, double scale)
    {
        int preferredLevel;
        bool preferredOuter;
        int secondaryLevel;
        bool secondaryOuter;
        if (previous.IsOuter)
        {
            preferredLevel = previous.Level;
            preferredOuter = false;
            secondaryLevel = previous.Level + 1;
            secondaryOuter = true;
        }
        else
        {
            preferredLevel = previous.Level + 1;
            preferredOuter = true;
            secondaryLevel = previous.Level;
            secondaryOuter = false;
        }
        if (candidate.Level == preferredLevel && candidate.IsOuter == preferredOuter)
            return 0;
        if (candidate.Level == secondaryLevel && candidate.IsOuter == secondaryOuter)
            return scale;
        return scale * 2.0;
    }

    private static bool TryAppendCompoundContourWithConnector(IReadOnlyList<Point> route,
        CompoundContour contour, double pitch, double bridgeLimit,
        IReadOnlyList<List<Point>> boundaries, double clearance,
        out List<Point> result, out double connectorLength)
    {
        result = null;
        connectorLength = double.PositiveInfinity;
        Point projected = OrderFromAnchor(contour.Points, route[^1], clockwise: false)[0];
        double contourPerimeter = Perimeter(contour.Points);
        var anchors = new List<Point> { projected };
        for (int sample = 1; sample < 12; sample++)
        {
            double shift = contourPerimeter * sample / 12.0;
            AddDistinct(anchors, AdvanceOnRing(contour.Points, projected, shift, false));
            AddDistinct(anchors, AdvanceOnRing(contour.Points, projected, shift, true));
        }

        foreach (Point anchor in anchors.OrderBy(point => Distance(point, route[^1])))
        {
            if (!TryFindSafeConnector(route, anchor, boundaries, clearance, pitch,
                    out List<Point> connector))
                continue;
            double length = connector.Zip(connector.Skip(1), Distance).Sum();
            if (length > bridgeLimit * 3.0 || length >= connectorLength)
                continue;

            foreach (bool clockwise in new[] { false, true })
            {
                var candidate = new List<Point>(route);
                foreach (Point point in connector.Skip(1))
                    Add(candidate, point);
                double gap = Math.Min(pitch * 0.25, contourPerimeter * 0.08);
                if (!AppendNearlyClosedRing(contour.Points, pitch, gap,
                        clockwise, anchor, candidate, out _, 1.0) ||
                    !PolylineTailIsSimple(route, candidate) ||
                    !IsPathInsideRegionWithClearance(candidate, boundaries, clearance))
                    continue;
                result = candidate;
                connectorLength = length;
                break;
            }
        }
        return result != null;
    }

    private static bool TryFindSafeConnector(IReadOnlyList<Point> route, Point target,
        IReadOnlyList<List<Point>> boundaries, double clearance, double pitch,
        out List<Point> connector)
    {
        connector = new List<Point>();
        Point start = route[^1];
        var direct = new List<Point> { start, target };
        if (IsPathInsideRegionWithClearance(direct, boundaries, clearance) &&
            PolylineTailIsSimple(route, route.Concat(new[] { target }).ToList()))
        {
            connector = direct;
            return true;
        }

        List<Point> outer = boundaries.OrderByDescending(loop => Math.Abs(SignedArea(loop))).First();
        double minX = outer.Min(point => point.X), maxX = outer.Max(point => point.X);
        double minY = outer.Min(point => point.Y), maxY = outer.Max(point => point.Y);
        double step = Math.Max(20.0, pitch / 3.0);
        int width = (int)Math.Ceiling((maxX - minX) / step) + 1;
        int height = (int)Math.Ceiling((maxY - minY) / step) + 1;
        int count = width * height;
        if (width <= 1 || height <= 1 || count > 200_000) return false;

        bool[] allowed = new bool[count];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Point point = new(minX + x * step, minY + y * step);
            if (!PointInsideRegion(boundaries, point) ||
                boundaries.Min(loop => DistanceToLoop(point, loop)) < clearance - 2.0)
                continue;
            bool nearRoute = false;
            for (int i = 0; i + 1 < route.Count - 1; i++)
                if (PointSegmentDistanceSquared(point, route[i], route[i + 1]) <
                    Math.Pow(Math.Max(5.0, pitch * 0.05), 2))
                { nearRoute = true; break; }
            allowed[y * width + x] = !nearRoute;
        }

        int sx = Math.Clamp((int)Math.Round((start.X - minX) / step), 0, width - 1);
        int sy = Math.Clamp((int)Math.Round((start.Y - minY) / step), 0, height - 1);
        int tx = Math.Clamp((int)Math.Round((target.X - minX) / step), 0, width - 1);
        int ty = Math.Clamp((int)Math.Round((target.Y - minY) / step), 0, height - 1);
        int startIndex = sy * width + sx, targetIndex = ty * width + tx;
        allowed[startIndex] = allowed[targetIndex] = true;
        var costs = Enumerable.Repeat(double.PositiveInfinity, count).ToArray();
        var parents = Enumerable.Repeat(-1, count).ToArray();
        var open = new PriorityQueue<int, double>();
        costs[startIndex] = 0;
        open.Enqueue(startIndex, 0);
        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (open.TryDequeue(out int current, out _))
        {
            if (current == targetIndex) break;
            int x = current % width, y = current / width;
            for (int dir = 0; dir < 8; dir++)
            {
                int nx = x + dx[dir], ny = y + dy[dir];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                int next = ny * width + nx;
                if (!allowed[next] || (dir >= 4 &&
                    (!allowed[y * width + nx] || !allowed[ny * width + x]))) continue;
                double cost = costs[current] + (dir < 4 ? 1.0 : Math.Sqrt(2.0));
                if (cost >= costs[next]) continue;
                costs[next] = cost;
                parents[next] = current;
                double heuristic = Math.Sqrt((nx - tx) * (double)(nx - tx) + (ny - ty) * (double)(ny - ty));
                open.Enqueue(next, cost + heuristic);
            }
        }
        if (!double.IsFinite(costs[targetIndex])) return false;

        var reversed = new List<Point>();
        for (int index = targetIndex; index >= 0; index = parents[index])
        {
            reversed.Add(new Point(minX + index % width * step, minY + index / width * step));
            if (index == startIndex) break;
        }
        if (reversed.Count < 2 || Distance(reversed[^1], start) > step * 1.5) return false;
        reversed.Reverse();
        reversed[0] = start;
        reversed[^1] = target;
        RemoveDuplicates(reversed);
        if (!IsPathInsideRegionWithClearance(reversed, boundaries, clearance) ||
            !PolylineTailIsSimple(route, route.Concat(reversed.Skip(1)).ToList()))
            return false;
        connector = reversed;
        return true;
    }

    private static bool ReplacePath(List<Point> destination, List<Point> source)
    {
        destination.Clear();
        destination.AddRange(source);
        return true;
    }

    private static bool PolylineTailIsSimple(IReadOnlyList<Point> existing,
        IReadOnlyList<Point> candidate)
    {
        if (candidate.Count < existing.Count + 1)
            return false;

        int firstNewSegment = Math.Max(0, existing.Count - 1);
        for (int i = firstNewSegment; i + 1 < candidate.Count; i++)
        {
            for (int j = 0; j < i - 1; j++)
            {
                if (SegmentsCross(candidate[i], candidate[i + 1], candidate[j], candidate[j + 1]))
                    return false;
            }
        }

        return true;
    }

    private static Paths64 OffsetCompound(IReadOnlyList<Path64> subjects, double distance)
    {
        var offsetter = new ClipperOffset(2.0,
            OffsetArcToleranceMillimeters * ClipperScale);
        var input = new Paths64();
        input.AddRange(subjects);
        offsetter.AddPaths(input, JoinType.Round, EndType.Polygon);
        var solution = new Paths64();
        offsetter.Execute(-distance * ClipperScale, solution);
        return solution;
    }

    private static Path64 ToClipperPath(IReadOnlyList<Point> points)
    {
        var path = new Path64(points.Count);
        foreach (Point point in points)
            path.Add(new Point64(point.X * ClipperScale, point.Y * ClipperScale));
        return path;
    }

    private static List<Point> FromClipperPath(Path64 path)
    {
        // ClipperOffset already emits sampled round joins. Re-rounding its contour
        // with a fixed radius can erase narrow components after a corridor splits.
        return Clean(path.Select(point =>
            new Point(point.X / ClipperScale, point.Y / ClipperScale)).ToList());
    }

    private static Point GetContourAnchor(IReadOnlyList<Point> contour, Point? seamTarget,
        IReadOnlyList<Point> boundary, bool clockwise)
    {
        if (seamTarget.HasValue)
            return OrderFromAnchor(contour, seamTarget.Value, clockwise)[0];
        Point center = new(boundary.Average(point => point.X), boundary.Average(point => point.Y));
        if (!PointInside(boundary, center))
            center = new Point(boundary[0].X, boundary[0].Y);
        double maxX = contour.Max(point => point.X);
        List<Point> extreme = contour.Where(point => maxX - point.X < 1e-4).ToList();
        Point exterior = extreme.Count == 0 ? contour[0]
            : new Point(maxX, extreme.Average(point => point.Y));
        return RayBoundaryIntersection(contour, center, Normalize(exterior - center));
    }

    private static double DistanceBetweenLoops(IReadOnlyList<Point> first,
        IReadOnlyList<Point> second)
    {
        double minimum = double.PositiveInfinity;
        for (int i = 0; i < first.Count; i++)
        for (int j = 0; j < second.Count; j++)
            minimum = Math.Min(minimum, SegmentDistance(first[i], first[(i + 1) % first.Count],
                second[j], second[(j + 1) % second.Count]));
        return minimum;
    }

    private static bool TryBuildSeamAwareRoute(IReadOnlyList<List<Point>> rings,
        IReadOnlyList<int> inwardLevels, IReadOnlyList<int> outwardLevels, double pitch,
        double gap, double exitGap, Point center, Point seamDirection, Point seamTarget,
        IReadOnlyList<Point> sourceBoundary, double centerlineClearance, out List<Point> route)
    {
        var steps = new List<RingStep>();
        foreach (int level in inwardLevels)
        {
            double ringGap = level == 0 ? exitGap : gap;
            Point anchor;
            if (level == 0)
            {
                anchor = AdvanceOnRing(rings[level],
                    OrderFromAnchor(rings[level], seamTarget, clockwise: false)[0],
                    ringGap * 0.5, clockwise: false);
            }
            else
            {
                Point baseAnchor = RayBoundaryIntersection(rings[level], center, seamDirection);
                anchor = AdvanceOnRing(rings[level], baseAnchor,
                    level / 2.0 * gap, clockwise: false);
            }
            steps.Add(new RingStep(level, ringGap, false, anchor));
        }

        foreach (int level in outwardLevels)
        {
            Point baseAnchor = RayBoundaryIntersection(rings[level], center, seamDirection);
            double phaseShift = (level - 2.0) / 2.0 * gap;
            Point anchor = AdvanceOnRing(rings[level], baseAnchor,
                Math.Abs(phaseShift), clockwise: phaseShift < 0);
            steps.Add(new RingStep(level, gap, true, anchor));
        }

        int explored = 0;
        const int maximumSearchNodes = 6000;
        route = new List<Point>();
        var result = new List<Point>();
        bool found = Search(0, new List<Point>(), null);
        if (found)
            route = result;
        return found;

        bool Search(int index, List<Point> current, Point? outerOpeningPortal)
        {
            if (index == steps.Count)
            {
                RemoveDuplicates(current);
                if (current.Count < 4 || !IsSimplePolyline(current))
                    return false;
                if (!IsQualityAcceptable(sourceBoundary, current, pitch, centerlineClearance))
                    return false;
                result = current;
                return true;
            }

            if (explored >= maximumSearchNodes)
                return false;

            RingStep step = steps[index];
            Point preferredAnchor = step.Anchor;
            if (step.Level == 1 && outerOpeningPortal.HasValue)
                preferredAnchor = AdvanceOnRing(rings[step.Level], outerOpeningPortal.Value,
                    step.Gap * 0.5, clockwise: true);
            List<Point> anchors = GetRingAnchorCandidates(rings[step.Level], pitch,
                current.Count == 0 ? null : current[^1], preferredAnchor);
            foreach (Point candidateAnchor in anchors)
            {
                if (++explored > maximumSearchNodes)
                    return false;
                var candidate = new List<Point>(current);
                if (!AppendNearlyClosedRing(rings[step.Level], pitch, step.Gap,
                        step.Clockwise, candidateAnchor, candidate, out _))
                    continue;
                if (!IsSimplePolyline(candidate))
                    continue;
                Point? nextOuterPortal = outerOpeningPortal;
                if (index == 1 && step.Level == 2 && steps[0].Level == 0 && rings.Count > 1 &&
                    TryFindSegmentIntersection(current[^1], candidate[current.Count], rings[1],
                        out Point openingPortal))
                    nextOuterPortal = openingPortal;
                if (Search(index + 1, candidate, nextOuterPortal))
                    return true;
            }
            return false;
        }
    }

    private static bool TryFindSegmentIntersection(Point start, Point end,
        IReadOnlyList<Point> polygon, out Point intersection)
    {
        intersection = default;
        Point direction = end - start;
        double nearestParameter = double.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            Point edgeStart = polygon[i];
            Point edge = polygon[(i + 1) % polygon.Count] - edgeStart;
            double denominator = Cross(direction, edge);
            if (Math.Abs(denominator) <= Epsilon)
                continue;
            Point delta = edgeStart - start;
            double alongRoute = Cross(delta, edge) / denominator;
            double alongEdge = Cross(delta, direction) / denominator;
            if (alongRoute < -Epsilon || alongRoute > 1 + Epsilon ||
                alongEdge < -Epsilon || alongEdge > 1 + Epsilon || alongRoute >= nearestParameter)
                continue;
            nearestParameter = alongRoute;
            intersection = start + direction * alongRoute;
        }
        return double.IsFinite(nearestParameter);
    }

    private static List<Point> GetRingAnchorCandidates(IReadOnlyList<Point> ring,
        double pitch, Point? previous, Point preferredAnchor, double maximumBridgeLength = 0)
    {
        if (!previous.HasValue)
            return new List<Point> { preferredAnchor };

        const double sampleSpacing = 50.0;
        double maximumBridge = maximumBridgeLength > 0 ? maximumBridgeLength : pitch * 3.5;
        var anchors = new List<Point>();
        for (int i = 0; i < ring.Count; i++)
        {
            Point start = ring[i], end = ring[(i + 1) % ring.Count];
            int edgeSteps = Math.Max(1, (int)Math.Ceiling(Distance(start, end) / sampleSpacing));
            for (int step = 0; step < edgeSteps; step++)
            {
                Point candidate = Lerp(start, end, (double)step / edgeSteps);
                if (Distance(candidate, previous.Value) <= maximumBridge + 1e-6 &&
                    !anchors.Any(existing => Distance(existing, candidate) < 1.0))
                    anchors.Add(candidate);
            }
        }
        Point projected = OrderFromAnchor(ring, preferredAnchor, clockwise: false)[0];
        for (double offset = 5.0; offset <= 100.0; offset += 5.0)
        foreach (bool clockwise in new[] { false, true })
        {
            Point local = AdvanceOnRing(ring, projected, offset, clockwise);
            if (Distance(local, previous.Value) <= maximumBridge + 1e-6 &&
                !anchors.Any(existing => Distance(existing, local) < 1.0))
                anchors.Add(local);
        }
        if (!anchors.Any(existing => Distance(existing, projected) < 1.0))
            anchors.Add(projected);
        return anchors.OrderBy(candidate => Distance(candidate, preferredAnchor))
            .ThenBy(candidate => Distance(candidate, previous.Value)).ToList();
    }

    private static Point AdvanceOnRing(IReadOnlyList<Point> ring, Point anchor,
        double distance, bool clockwise)
    {
        List<Point> ordered = OrderFromAnchor(ring, anchor, clockwise);
        double remaining = distance % Perimeter(ring);
        for (int i = 0; i < ordered.Count; i++)
        {
            Point next = ordered[(i + 1) % ordered.Count];
            double segmentLength = Distance(ordered[i], next);
            if (remaining <= segmentLength)
                return Lerp(ordered[i], next, segmentLength <= Epsilon ? 0 : remaining / segmentLength);
            remaining -= segmentLength;
        }
        return ordered[0];
    }

    private static bool AppendNearlyClosedRing(IReadOnlyList<Point> ring, double pitch, double gap,
        bool clockwise, Point anchor, List<Point> route, out Point endpoint,
        double maximumBridgeLength = 0)
    {
        endpoint = default;
        double perimeter = Perimeter(ring);
        if (perimeter < pitch * 2.5)
            return false;

        // Keep a small opening in every lap. The opening becomes the inward/outward bridge,
        // avoiding the coincident start/end segment that creates a pipe-on-pipe overlap.
        List<Point> ordered = OrderFromAnchor(ring, anchor, clockwise);
        double bridgeLength = route.Count > 0 ? Distance(route[^1], ordered[0]) : 0;
        if (route.Count > 0 && bridgeLength >
            (maximumBridgeLength > 0 ? maximumBridgeLength : pitch * 5.0))
            return false;
        Add(route, ordered[0]);

        double walked = 0;
        for (int step = 1; step <= ordered.Count && walked < perimeter - gap; step++)
        {
            Point next = ordered[step % ordered.Count];
            double segmentLength = Distance(route[^1], next);
            if (segmentLength <= Epsilon)
                continue;
            if (walked + segmentLength > perimeter - gap)
            {
                double remaining = perimeter - gap - walked;
                next = Lerp(route[^1], next, remaining / segmentLength);
                Add(route, next);
                walked = perimeter - gap;
                break;
            }
            Add(route, next);
            walked += segmentLength;
        }
        endpoint = route[^1];
        return walked >= perimeter - gap - 1e-4;
    }

    private static Point GetLayerAnchor(IReadOnlyList<Point> ring, Point target, double shift)
    {
        List<Point> ordered = OrderFromAnchor(ring, target, clockwise: false);
        double perimeter = Perimeter(ring);
        double remaining = shift % perimeter;
        for (int i = 0; i < ordered.Count; i++)
        {
            Point next = ordered[(i + 1) % ordered.Count];
            double length = Distance(ordered[i], next);
            if (remaining <= length)
                return Lerp(ordered[i], next, length <= Epsilon ? 0 : remaining / length);
            remaining -= length;
        }
        return ordered[0];
    }

    private static Point RayBoundaryIntersection(IReadOnlyList<Point> polygon, Point origin, Point direction)
    {
        double nearestT = double.PositiveInfinity;
        Point result = polygon[0];
        for (int i = 0; i < polygon.Count; i++)
        {
            Point a = polygon[i], edge = polygon[(i + 1) % polygon.Count] - a;
            double denominator = Cross(direction, edge);
            if (Math.Abs(denominator) <= Epsilon) continue;
            Point delta = a - origin;
            double t = Cross(delta, edge) / denominator;
            double u = Cross(delta, direction) / denominator;
            if (t < 0 || u < -Epsilon || u > 1 + Epsilon || t >= nearestT) continue;
            nearestT = t;
            result = origin + direction * t;
        }
        return result;
    }

    private static Point FindInteriorCenter(IReadOnlyList<Point> polygon, double pitch)
    {
        double minX = polygon.Min(point => point.X), maxX = polygon.Max(point => point.X);
        double minY = polygon.Min(point => point.Y), maxY = polygon.Max(point => point.Y);
        double step = Math.Max(25, pitch / 3.0);
        Point best = default;
        double bestClearance = -1;
        for (double x = minX + step / 2; x < maxX; x += step)
        for (double y = minY + step / 2; y < maxY; y += step)
        {
            Point candidate = new(x, y);
            if (!PointInside(polygon, candidate)) continue;
            double clearance = DistanceToLoop(candidate, polygon);
            if (clearance <= bestClearance) continue;
            best = candidate;
            bestClearance = clearance;
        }
        return bestClearance > 0 ? best : new Point(polygon.Average(point => point.X), polygon.Average(point => point.Y));
    }

    private static Point FindStableSeamDirection(IReadOnlyList<Point> polygon, Point interiorPoint)
    {
        Point bestDirection = new(1, 0);
        double bestScore = double.NegativeInfinity;
        double probe = 5.0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Point start = polygon[i];
            Point edge = polygon[(i + 1) % polygon.Count] - start;
            double length = edge.Length;
            if (length <= Epsilon) continue;
            Point left = new(-edge.Y / length, edge.X / length);
            Point midpoint = start + edge * 0.5;
            bool leftInside = PointInside(polygon, midpoint + left * probe);
            bool rightInside = PointInside(polygon, midpoint - left * probe);
            if (leftInside == rightInside) continue;
            Point outward = leftInside ? left * -1.0 : left;
            double outwardAlignment = Dot(Normalize(midpoint - interiorPoint), outward);
            if (outwardAlignment <= 0) continue;
            double score = length * (0.5 + 0.5 * outwardAlignment);
            if (score <= bestScore) continue;
            bestScore = score;
            bestDirection = outward;
        }
        return bestDirection;
    }

    private static List<Point> OrderFromAnchor(IReadOnlyList<Point> ring, Point target, bool clockwise)
    {
        int segmentIndex;
        Point anchor;
        {
            double minimumDistance = double.PositiveInfinity;
            segmentIndex = 0;
            anchor = ring[0];
            for (int i = 0; i < ring.Count; i++)
            {
                Point a = ring[i], b = ring[(i + 1) % ring.Count];
                Point edge = b - a;
                double t = Math.Clamp(Dot(target - a, edge) / Dot(edge, edge), 0, 1);
                Point projection = a + edge * t;
                double distance = DistanceSquared(target, projection);
                if (distance >= minimumDistance) continue;
                minimumDistance = distance;
                segmentIndex = i;
                anchor = projection;
            }
        }

        var ordered = new List<Point>(ring.Count + 2) { anchor };
        if (clockwise)
        {
            for (int offset = 0; offset < ring.Count; offset++)
            {
                Point vertex = ring[(segmentIndex - offset + ring.Count) % ring.Count];
                if (Distance(ordered[^1], vertex) > Epsilon)
                    ordered.Add(vertex);
            }
        }
        else
        {
            for (int offset = 1; offset <= ring.Count; offset++)
            {
                Point vertex = ring[(segmentIndex + offset) % ring.Count];
                if (Distance(ordered[^1], vertex) > Epsilon)
                    ordered.Add(vertex);
            }
        }
        if (ordered.Count > 1 && Distance(ordered[^1], ordered[0]) <= Epsilon)
            ordered.RemoveAt(ordered.Count - 1);
        return ordered;
    }

    internal static bool TryOffsetContour(IReadOnlyList<Point> sourceBoundary, double distance,
        double cornerRadius, out List<Point> result)
    {
        result = new List<Point>();
        if (sourceBoundary == null || distance < 0 || cornerRadius <= 0)
            return false;
        List<Point> boundary = Clean(sourceBoundary);
        if (boundary.Count < 3 || Math.Abs(SignedArea(boundary)) < Epsilon)
            return false;
        if (SignedArea(boundary) < 0)
            boundary.Reverse();
        return TryOffset(boundary, distance, cornerRadius, out result) && IsSimple(result);
    }

    private static bool TryOffset(IReadOnlyList<Point> polygon, double distance,
        double cornerRadius, out List<Point> result)
    {
        result = new List<Point>();
        if (polygon == null || polygon.Count < 3 || !double.IsFinite(distance) ||
            !double.IsFinite(cornerRadius) || cornerRadius <= 0)
            return false;

        var subject = new Path64(polygon.Count);
        foreach (Point point in polygon)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                Math.Abs(point.X * ClipperScale) > long.MaxValue / 4.0 ||
                Math.Abs(point.Y * ClipperScale) > long.MaxValue / 4.0)
                return false;
            subject.Add(new Point64(point.X * ClipperScale, point.Y * ClipperScale));
        }

        // Clipper2 performs the Boolean cleanup of inward offsets, which prevents
        // concave corners from folding across neighboring walls.
        if (Clipper.Area(subject) < 0)
            subject.Reverse();
        var offsetter = new ClipperOffset(2.0,
            OffsetArcToleranceMillimeters * ClipperScale);
        offsetter.AddPath(subject, JoinType.Round, EndType.Polygon);
        var offsetPaths = new Paths64();
        offsetter.Execute(-distance * ClipperScale, offsetPaths);
        if (offsetPaths.Count != 1 || offsetPaths[0].Count < 3)
            return false;

        var offset = offsetPaths[0]
            .Select(point => new Point(point.X / ClipperScale, point.Y / ClipperScale)).ToList();
        offset = Clean(offset);
        // Clipper resolves the inset topology; then replace each sharp offset vertex
        // with an explicit circular fillet of the requested centerline bend radius.
        // Keep the shallow chords of the round offset as one arc.
        if (offset.Count < 3 || !TryRoundPolyline(offset, cornerRadius,
                20.0, closed: true, out result, Math.Min(10.0, cornerRadius * 0.5)))
            return false;
        if (SignedArea(result) < 0)
            result.Reverse();
        return IsSimple(result);
    }

    internal static bool TryRoundPolyline(IReadOnlyList<Point> source, double requestedRadius,
        double minimumTurnDegrees, bool closed, out List<Point> result,
        double minimumCornerRadius = 0)
    {
        result = new List<Point>();
        if (source == null || source.Count < (closed ? 3 : 2) || requestedRadius <= 0 ||
            !double.IsFinite(requestedRadius) || !double.IsFinite(minimumTurnDegrees))
            return false;

        List<Point> points = Clean(source);
        if (points.Count < (closed ? 3 : 2))
            return false;

        int count = points.Count;
        var incoming = new Point[count];
        var outgoing = new Point[count];
        var turns = new double[count];
        var radii = new double[count];
        var tangents = new double[count];
        double minimumTurn = Math.Max(0, minimumTurnDegrees) * Math.PI / 180.0;
        double minimumSegmentLength = 2.54;

        int firstCorner = closed ? 0 : 1;
        int lastCorner = closed ? count - 1 : count - 2;
        for (int i = firstCorner; i <= lastCorner; i++)
        {
            int previousIndex = (i + count - 1) % count;
            int nextIndex = (i + 1) % count;
            incoming[i] = Normalize(points[i] - points[previousIndex]);
            outgoing[i] = Normalize(points[nextIndex] - points[i]);
            if (incoming[i].Length <= Epsilon || outgoing[i].Length <= Epsilon)
                return false;

            turns[i] = Math.Atan2(Cross(incoming[i], outgoing[i]),
                Dot(incoming[i], outgoing[i]));
            double absoluteTurn = Math.Abs(turns[i]);
            if (absoluteTurn < minimumTurn)
                continue;
            if (absoluteTurn >= Math.PI - 1e-4)
                return false;

            radii[i] = requestedRadius;
            tangents[i] = requestedRadius * Math.Tan(absoluteTurn * 0.5);
        }

        // Shrink adjacent fillets together when their tangent legs would overlap.
        for (int pass = 0; pass < 16; pass++)
        {
            bool changed = false;
            int edgeCount = closed ? count : count - 1;
            for (int i = 0; i < edgeCount; i++)
            {
                int next = (i + 1) % count;
                double available = Distance(points[i], points[next]) - minimumSegmentLength;
                double used = tangents[i] + tangents[next];
                if (used <= available + 1e-6)
                    continue;
                if (available <= 0 || used <= Epsilon)
                    return false;
                double scale = Math.Max(0, available / used * 0.999);
                if (radii[i] > 0)
                {
                    radii[i] *= scale;
                    tangents[i] = radii[i] * Math.Tan(Math.Abs(turns[i]) * 0.5);
                }
                if (radii[next] > 0)
                {
                    radii[next] *= scale;
                    tangents[next] = radii[next] * Math.Tan(Math.Abs(turns[next]) * 0.5);
                }
                changed = true;
            }
            if (!changed)
                break;
        }

        for (int i = firstCorner; i <= lastCorner; i++)
            if (radii[i] > 0 && radii[i] < minimumCornerRadius - 1e-6)
                return false;

        int outputCount = closed ? count : count;
        for (int i = 0; i < outputCount; i++)
        {
            if (radii[i] <= 0)
            {
                Add(result, points[i]);
                continue;
            }

            Point start = points[i] - incoming[i] * tangents[i];
            Point end = points[i] + outgoing[i] * tangents[i];
            double turnSign = Math.Sign(turns[i]);
            Point normal = new(-incoming[i].Y, incoming[i].X);
            Point center = start + normal * (turnSign * radii[i]);
            double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            double arcTolerance = Math.Min(1.0, Math.Max(0.1, radii[i] * 0.02));
            double toleranceStep = 2.0 * Math.Acos(Math.Clamp(1.0 - arcTolerance / radii[i], -1.0, 1.0));
            double maximumStep = Math.Min(Math.PI / 12.0, toleranceStep);
            if (!double.IsFinite(maximumStep) || maximumStep <= Epsilon)
                return false;
            int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(turns[i]) / maximumStep));
            // Keep the sampled route usable for downstream length and clearance
            // checks even when it contains a very tight turn.
            while (segments > 1 &&
                   2.0 * radii[i] * Math.Sin(Math.Abs(turns[i]) / (2.0 * segments)) <
                   minimumSegmentLength - 1e-6)
                segments--;
            if (2.0 * radii[i] * Math.Sin(Math.Abs(turns[i]) / (2.0 * segments)) <
                minimumSegmentLength - 1e-6)
                return false;
            Add(result, start);
            for (int step = 1; step < segments; step++)
            {
                double angle = startAngle + turns[i] * step / segments;
                Add(result, center + new Point(Math.Cos(angle), Math.Sin(angle)) * radii[i]);
            }
            Add(result, end);
        }

        if (!closed)
            Add(result, points[^1]);
        return result.Count >= (closed ? 3 : 2) &&
               HasMinimumSegmentLengths(result, closed, minimumSegmentLength);
    }

    private static bool HasMinimumSegmentLengths(IReadOnlyList<Point> path, bool closed,
        double minimumLength)
    {
        int edgeCount = closed ? path.Count : path.Count - 1;
        for (int i = 0; i < edgeCount; i++)
            if (Distance(path[i], path[(i + 1) % path.Count]) < minimumLength - 1e-4)
                return false;
        return true;
    }

    private static double GetDefaultCornerRadius(double pitch, double centerlineClearance)
    {
        if (!double.IsFinite(pitch) || pitch <= 0)
            return 0;
        return Math.Min(pitch * 0.2, Math.Max(10.0, centerlineClearance * 0.3));
    }

    private static bool IsInsideBoundary(IReadOnlyList<Point> ring, IReadOnlyList<Point> boundary,
        double requiredDistance)
    {
        foreach (Point point in ring)
        {
            if (!PointInside(boundary, point) || DistanceToLoop(point, boundary) < requiredDistance - 1.5)
                return false;
        }
        for (int i = 0; i < ring.Count; i++)
        {
            Point a = ring[i], b = ring[(i + 1) % ring.Count];
            int samples = Math.Max(1, (int)Math.Ceiling(Distance(a, b) / 25.0));
            for (int k = 1; k < samples; k++)
            {
                Point sample = Lerp(a, b, (double)k / samples);
                if (!PointInside(boundary, sample) || DistanceToLoop(sample, boundary) < requiredDistance - 1.5)
                    return false;
            }
        }
        return true;
    }

    private static bool IsSimple(IReadOnlyList<Point> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
        for (int j = i + 1; j < polygon.Count; j++)
        {
            if (j == i || j == (i + 1) % polygon.Count || i == (j + 1) % polygon.Count)
                continue;
            if (SegmentsCross(polygon[i], polygon[(i + 1) % polygon.Count],
                    polygon[j], polygon[(j + 1) % polygon.Count]))
                return false;
        }
        return true;
    }

    private static bool IsSimplePolyline(IReadOnlyList<Point> path)
    {
        for (int i = 0; i + 1 < path.Count; i++)
        for (int j = i + 2; j + 1 < path.Count; j++)
        {
            // A lane-to-lane bridge may touch a neighboring offset at its endpoint only.
            if (j == i + 2 && Distance(path[i + 1], path[j]) < 1e-5)
                continue;
            if (SegmentsCross(path[i], path[i + 1], path[j], path[j + 1]))
                return false;
        }
        return true;
    }

    internal static bool HasRoomBetweenRuns(IReadOnlyList<Point> path, double localLength,
        double minimumSeparation)
    {
        var station = new double[path.Count];
        var bounds = new (double MinX, double MinY, double MaxX, double MaxY)[Math.Max(0, path.Count - 1)];
        for (int i = 1; i < path.Count; i++)
        {
            station[i] = station[i - 1] + Distance(path[i - 1], path[i]);
            Point a = path[i - 1], b = path[i];
            bounds[i - 1] = (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        for (int i = 0; i < bounds.Length; i++)
        for (int j = i + 2; j < bounds.Length; j++)
        {
            var first = bounds[i];
            var second = bounds[j];
            // Disjoint expanded bounds rule out both a crossing and a clearance violation.
            if (second.MinX - first.MaxX > minimumSeparation || first.MinX - second.MaxX > minimumSeparation ||
                second.MinY - first.MaxY > minimumSeparation || first.MinY - second.MaxY > minimumSeparation)
                continue;
            if (SegmentsCross(path[i], path[i + 1], path[j], path[j + 1]))
                return false;
            // Nearby pieces of the same bend are connected intentionally.
            if (station[j] - station[i + 1] >= localLength &&
                SegmentDistance(path[i], path[i + 1], path[j], path[j + 1]) < minimumSeparation)
                return false;
        }
        return true;
    }

    private static bool IsQualityAcceptable(IReadOnlyList<Point> boundary, IReadOnlyList<Point> route,
        double pitch, double centerlineClearance)
    {
        for (int i = 0; i + 1 < route.Count; i++)
        {
            double length = Distance(route[i], route[i + 1]);
            if (length < 2.54)
                return false;
            int steps = Math.Max(1, (int)Math.Ceiling(length / 25.0));
            for (int j = 0; j <= steps; j++)
            {
                Point sample = Lerp(route[i], route[i + 1], (double)j / steps);
                if (!PointInside(boundary, sample) ||
                    DistanceToLoop(sample, boundary) < centerlineClearance - 2.0)
                {
                    return false;
                }
            }
        }

        double minX = boundary.Min(point => point.X), maxX = boundary.Max(point => point.X);
        double minY = boundary.Min(point => point.Y), maxY = boundary.Max(point => point.Y);
        double sampleStep = Math.Max(50, pitch / 2.0);
        double maximumGap = pitch * 1.65;
        var segmentBuckets = new SegmentBuckets<Point>(route.Zip(route.Skip(1), (a, b) => (a, b)),
            point => (point.X, point.Y), PointSegmentDistanceSquared, maximumGap);
        for (double x = minX + sampleStep / 2; x < maxX; x += sampleStep)
        for (double y = minY + sampleStep / 2; y < maxY; y += sampleStep)
        {
            Point sample = new(x, y);
            if (!PointInside(boundary, sample) || DistanceToLoop(sample, boundary) < centerlineClearance)
                continue;
            if (Math.Sqrt(segmentBuckets.NearestSquared(sample, requireExactDistance: false)) > maximumGap)
                return false;
        }
        return true;
    }

    private static bool IsPathInsideWithClearance(IReadOnlyList<Point> route,
        IReadOnlyList<Point> boundary, double centerlineClearance)
    {
        for (int i = 0; i + 1 < route.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(Distance(route[i], route[i + 1]) / 25.0));
            for (int step = 0; step <= steps; step++)
            {
                Point sample = Lerp(route[i], route[i + 1], (double)step / steps);
                if (!PointInside(boundary, sample) ||
                    DistanceToLoop(sample, boundary) < centerlineClearance - 2.0)
                    return false;
            }
        }
        return true;
    }

    private static bool IsPathInsideRegionWithClearance(IReadOnlyList<Point> route,
        IReadOnlyList<List<Point>> boundaries, double clearance)
    {
        for (int i = 0; i + 1 < route.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(Distance(route[i], route[i + 1]) / 25.0));
            for (int step = 0; step <= steps; step++)
            {
                Point sample = Lerp(route[i], route[i + 1], (double)step / steps);
                if (!PointInsideRegion(boundaries, sample) ||
                    boundaries.Min(loop => DistanceToLoop(sample, loop)) < clearance - 1.0)
                    return false;
            }
        }
        return true;
    }

    private static bool PointInsideRegion(IReadOnlyList<List<Point>> boundaries, Point point)
    {
        bool inside = false;
        foreach (List<Point> loop in boundaries)
            if (PointInside(loop, point))
                inside = !inside;
        return inside;
    }

    private static double SegmentDistance(Point a, Point b, Point c, Point d)
    {
        if (SegmentsCross(a, b, c, d))
            return 0;
        return Math.Min(Math.Min(PointSegmentDistance(a, c, d), PointSegmentDistance(b, c, d)),
            Math.Min(PointSegmentDistance(c, a, b), PointSegmentDistance(d, a, b)));
    }

    private static bool PointInside(IReadOnlyList<Point> polygon, Point point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Point a = polygon[i], b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static double DistanceToLoop(Point point, IReadOnlyList<Point> loop)
    {
        double minimum = double.PositiveInfinity;
        for (int i = 0; i < loop.Count; i++)
            minimum = Math.Min(minimum, PointSegmentDistance(point, loop[i], loop[(i + 1) % loop.Count]));
        return minimum;
    }

    private static double PointSegmentDistance(Point point, Point a, Point b)
    {
        Point edge = b - a;
        double lengthSquared = Dot(edge, edge);
        double t = lengthSquared <= Epsilon ? 0 : Math.Clamp(Dot(point - a, edge) / lengthSquared, 0, 1);
        return Distance(point, a + edge * t);
    }

    private static double PointSegmentDistanceSquared(Point point, Point a, Point b)
    {
        double distance = PointSegmentDistance(point, a, b);
        return distance * distance;
    }

    private static bool SegmentsCross(Point a, Point b, Point c, Point d)
    {
        Point ab = b - a;
        double abC = Cross(ab, c - a), abD = Cross(ab, d - a);
        if (!(abC * abD < -1e-7))
            return false;
        Point cd = d - c;
        return Cross(cd, a - c) * Cross(cd, b - c) < -1e-7;
    }

    private static List<Point> Clean(IEnumerable<Point> points)
    {
        var result = new List<Point>();
        foreach (Point point in points)
            if (result.Count == 0 || Distance(result[^1], point) > Epsilon)
                result.Add(point);
        if (result.Count > 1 && Distance(result[0], result[^1]) <= Epsilon)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static void RemoveDuplicates(List<Point> points)
    {
        List<Point> clean = Clean(points);
        points.Clear();
        points.AddRange(clean);
    }

    private static void Add(List<Point> points, Point point)
    {
        if (points.Count == 0 || Distance(points[^1], point) > Epsilon)
            points.Add(point);
    }

    private static void AddDistinct(List<Point> points, Point point)
    {
        if (!points.Any(existing => Distance(existing, point) < 1.0))
            points.Add(point);
    }

    private static double Perimeter(IReadOnlyList<Point> loop)
    {
        double sum = 0;
        for (int i = 0; i < loop.Count; i++)
            sum += Distance(loop[i], loop[(i + 1) % loop.Count]);
        return sum;
    }

    private static double MinimumWidth(IReadOnlyList<Point> polygon)
    {
        double minimum = double.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            Point edge = polygon[(i + 1) % polygon.Count] - polygon[i];
            if (edge.Length <= Epsilon) continue;
            Point normal = new(-edge.Y / edge.Length, edge.X / edge.Length);
            double min = polygon.Min(point => Dot(point, normal));
            double max = polygon.Max(point => Dot(point, normal));
            minimum = Math.Min(minimum, max - min);
        }
        return minimum;
    }

    private static double SignedArea(IReadOnlyList<Point> loop)
    {
        double area = 0;
        for (int i = 0; i < loop.Count; i++)
            area += Cross(loop[i], loop[(i + 1) % loop.Count]);
        return area / 2;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(DistanceSquared(a, b));
    private static double DistanceSquared(Point a, Point b) => Dot(a - b, a - b);
    private static Point Lerp(Point a, Point b, double t) => a + (b - a) * t;
    private static Point Normalize(Point value) => value.Length <= Epsilon ? default : value / value.Length;
    private static double Cross(Point a, Point b) => a.X * b.Y - a.Y * b.X;
    private static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y;

    private readonly record struct RingStep(int Level, double Gap, bool Clockwise, Point Anchor);

    private sealed record CompoundContour(int Level, bool IsOuter, List<Point> Points);

    internal readonly record struct Point(double X, double Y)
    {
        internal double Length => Math.Sqrt(X * X + Y * Y);
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static Point operator *(Point a, double scalar) => new(a.X * scalar, a.Y * scalar);
        public static Point operator /(Point a, double scalar) => new(a.X / scalar, a.Y / scalar);
    }
}
