using Autodesk.Revit.DB;
using Underfloor_heating_generator.Infrastructure.Revit;

const double MmPerFoot = 304.8;
const double Pitch = 150;
const double WallOffset = 100;
const double Diameter = 16;
const double RequiredCenterlineClearance = WallOffset * 0.9 + Diameter / 2.0 - 2.0;

var checks = new List<(string Name, Func<bool> Run)>
{
    ("standard rectangle", () => CheckGenerated("rectangle", new[] { Rect(0, 0, 6000, 4000) })),
    ("small standard room", () => CheckGenerated("small room", new[] { Rect(0, 0, 2400, 2100) })),
    ("rotated rectangle", () => CheckGenerated("rotated rectangle",
        new[] { Rotate(Rect(0, 0, 5000, 3200), 27) }, expectedAngle: 27)),
    ("rotated chamfered room", () => CheckGenerated("rotated chamfer",
        new[] { Rotate(Polygon((0, 0), (6200, 0), (6200, 2700), (5200, 3900), (0, 3900)), -18) },
        expectedAngle: 21.8, checkStraightOffsets: true, minimumStraightWallOffsets: 1,
        maximumCoverageFactor: 1.65)),
    ("L-shaped concave room", () => CheckGenerated("L shape", new[]
        { Polygon((0, 0), (6500, 0), (6500, 2100), (2800, 2100), (2800, 5200), (0, 5200)) })),
    ("U-shaped room", () => CheckGenerated("U shape", new[]
        { Polygon((0, 0), (7000, 0), (7000, 5200), (5100, 5200), (5100, 1900),
            (1900, 1900), (1900, 5200), (0, 5200)) })),
    ("C-shaped room", () => CheckGenerated("C shape", new[]
        { Polygon((0, 0), (7000, 0), (7000, 1700), (2200, 1700), (2200, 4300),
            (7000, 4300), (7000, 6000), (0, 6000)) })),
    ("one internal rectangular obstacle", () => CheckGenerated("one hole", new[]
        { Rect(0, 0, 7000, 4800), Rect(2300, 1500, 4700, 3400) }, checkStraightOffsets: true)),
    ("reversed loop winding with an inner obstacle", () => CheckGenerated("reversed winding", new[]
        { Rect(0, 0, 7000, 4800).Reverse().ToList(), Rect(2300, 1500, 4700, 3400) })),
    ("multiple internal obstacles", () => CheckGenerated("multiple holes", new[]
        { Rect(0, 0, 9000, 5000), Rect(1800, 1500, 3000, 3400), Rect(5700, 900, 7600, 4100) })),
    ("curved boundary approximation with a hole", () => CheckGenerated("rounded room", new[]
        { RoundedRectangle(0, 0, 7000, 5000, 700, 8), Circle(3500, 2500, 650, 24) })),
    ("acute notch", () => CheckGenerated("acute notch", new[]
        { Polygon((0, 0), (7000, 0), (7000, 5000), (3700, 5000), (3400, 3300),
            (3100, 5000), (0, 5000)) })),
    ("same-side horizontal pipe connectors", () => CheckGenerated("horizontal connectors",
        new[] { Rect(0, 0, 6000, 4000) }, 0)),
    ("same-side vertical pipe connectors", () => CheckGenerated("vertical connectors",
        new[] { Rect(0, 0, 6000, 4000) }, 2)),
    ("adjacent-side pipe connectors", () => CheckGenerated("adjacent connectors",
        new[] { Rect(0, 0, 6000, 4000) }, 3)),
    ("opposite-side leads are rejected rather than crossing the coil", () =>
        CheckExpectedFailure("opposite connectors", new[] { Rect(0, 0, 6000, 4000) }, 1)),
    ("narrow corridor must fail cleanly", () => CheckExpectedFailure("narrow corridor",
        new[] { Rect(0, 0, 5000, 300) })),
    ("too-small area must fail cleanly", () => CheckExpectedFailure("tiny area",
        new[] { Rect(0, 0, 150, 150) })),
    ("invalid layout parameters must fail cleanly", CheckInvalidParameters),
    ("degenerate loop must fail cleanly", () => CheckExpectedFailure("degenerate",
        new[] { Polygon((0, 0), (100, 0), (200, 0)) })),
    ("disconnected islands must fail cleanly", () => CheckExpectedFailure("islands",
        new[] { Rect(0, 0, 2400, 2400), Rect(4000, 0, 6400, 2400) }))
};

IEnumerable<(string Name, Func<bool> Run)> selectedChecks = args.Length == 0
    ? checks
    : checks.Where(check => check.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase));
int checksRun = 0;
int failures = 0;
foreach ((string name, Func<bool> run) in selectedChecks)
{
    checksRun++;
    try
    {
        bool passed = run();
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}");
        if (!passed) failures++;
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL  {name}: {exception.GetType().Name}: {exception.Message}");
    }
}

Console.WriteLine($"\n{checksRun - failures}/{checksRun} checks passed.");
return failures == 0 ? 0 : 1;

static bool CheckGenerated(string name, IList<IList<XYZ>> loops, int connectorMode = 0,
    double? expectedAngle = null, bool checkStraightOffsets = false, int minimumStraightWallOffsets = 4,
    double maximumCoverageFactor = 1.5)
{
    IList<XYZ> path = Build(loops, connectorMode);
    if (path.Count < 4)
        return Fail(name, $"expected a complete route, received {path.Count} points");

    if (path.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z)))
        return Fail(name, "route contains a non-finite coordinate");

    List<Point> millimeterPath = path.Select(ToMillimeters).ToList();
    (XYZ expectedStart, XYZ expectedStartDirection, XYZ expectedEnd, XYZ expectedEndDirection) =
        GetConnectors(loops, connectorMode);
    if (Distance(millimeterPath[0], ToMillimeters(expectedStart)) > 0.01 ||
        Distance(millimeterPath[^1], ToMillimeters(expectedEnd)) > 0.01)
        return Fail(name, "route endpoints do not coincide with selected pipe connectors");
    if (!IsCollinear(new Point(millimeterPath[1].X - millimeterPath[0].X,
            millimeterPath[1].Y - millimeterPath[0].Y), expectedStartDirection) ||
        !IsCollinear(new Point(millimeterPath[^1].X - millimeterPath[^2].X,
            millimeterPath[^1].Y - millimeterPath[^2].Y), expectedEndDirection))
        return Fail(name, "route does not preserve the selected connector directions");
    double minimumLength = (0.1 * 25.4) / 304.8;
    for (int i = 0; i + 1 < path.Count; i++)
        if (path[i].DistanceTo(path[i + 1]) < minimumLength - 1e-9)
            return Fail(name, $"segment {i} is shorter than Revit's minimum 0.1 inch");

    if (HasSelfCrossing(millimeterPath))
        return Fail(name, "route crosses itself");

    List<List<Point>> mmLoops = loops.Select(loop => loop.Select(ToMillimeters).ToList()).ToList();
    List<List<Point>> holes = FindInnerLoops(mmLoops);
    double clearance = RequiredCenterlineClearance;
    var safeBodySegments = new bool[millimeterPath.Count - 1];
    for (int i = 0; i < safeBodySegments.Length; i++)
    {
        int samples = Math.Max(1, (int)Math.Ceiling(Distance(millimeterPath[i], millimeterPath[i + 1]) / 10.0));
        safeBodySegments[i] = true;
        for (int sample = 0; sample <= samples; sample++)
        {
            Point point = Lerp(millimeterPath[i], millimeterPath[i + 1], (double)sample / samples);
            double nearest = mmLoops.Min(loop => PointToLoopDistanceSquared(point, loop));
            if (!InsideRegion(mmLoops, point) || nearest < clearance * clearance - 1e-5)
            {
                safeBodySegments[i] = false;
                break;
            }
        }
    }

    int bodyStart = -1;
    int bodyEnd = -1;
    double bestBodyLength = 0;
    for (int i = 0; i < safeBodySegments.Length;)
    {
        if (!safeBodySegments[i]) { i++; continue; }
        int start = i;
        double blockLength = 0;
        while (i < safeBodySegments.Length && safeBodySegments[i])
            blockLength += Distance(millimeterPath[i], millimeterPath[++i]);
        if (blockLength > bestBodyLength)
        {
            bestBodyLength = blockLength;
            bodyStart = start;
            bodyEnd = i;
        }
    }
    if (bestBodyLength < 1000)
        return Fail(name, $"could not identify a safe heating body ({bestBodyLength:F0} mm)");

    double minX = mmLoops.SelectMany(loop => loop).Min(point => point.X);
    double maxX = mmLoops.SelectMany(loop => loop).Max(point => point.X);
    double minY = mmLoops.SelectMany(loop => loop).Min(point => point.Y);
    double maxY = mmLoops.SelectMany(loop => loop).Max(point => point.Y);
    double maximumCoverageGap = Pitch * maximumCoverageFactor;
    double largestUncoveredDistance = 0;
    Point worstCoveragePoint = default;
    for (double x = minX + clearance; x <= maxX - clearance; x += 200)
    for (double y = minY + clearance; y <= maxY - clearance; y += 200)
    {
        var point = new Point(x, y);
        if (!InsideRegion(mmLoops, point) ||
            mmLoops.Min(loop => PointToLoopDistanceSquared(point, loop)) < clearance * clearance - 1e-5)
            continue;
        double nearest = double.PositiveInfinity;
        for (int i = bodyStart; i < bodyEnd; i++)
            nearest = Math.Min(nearest, PointSegmentDistanceSquared(point,
                millimeterPath[i], millimeterPath[i + 1]));
        double coverageDistance = Math.Sqrt(nearest);
        if (coverageDistance > largestUncoveredDistance)
        {
            largestUncoveredDistance = coverageDistance;
            worstCoveragePoint = point;
        }
    }
    for (int i = 0; i + 1 < millimeterPath.Count; i++)
    {
        foreach (List<Point> hole in holes)
        {
            if (SegmentToLoopDistanceSquared(millimeterPath[i], millimeterPath[i + 1], hole) < clearance * clearance - 1e-5)
                return Fail(name, $"route violates {WallOffset} mm hole setback near segment {i}");
        }

        // Connector leads can run outside the room to reach their selected pipe ends. Find
        // the continuous coil body geometrically rather than assuming a fixed number of lead
        // segments; the route generator may add several safe bends around the coil.
        if (i < bodyStart || i >= bodyEnd)
            continue;

        int samples = Math.Max(1, (int)Math.Ceiling(Distance(millimeterPath[i], millimeterPath[i + 1]) / 10.0));
        for (int sample = 0; sample <= samples; sample++)
        {
            Point point = Lerp(millimeterPath[i], millimeterPath[i + 1], (double)sample / samples);
            if (!InsideRegion(mmLoops, point))
                continue;
            double nearest = mmLoops.Min(loop => PointToLoopDistanceSquared(point, loop));
            if (nearest < clearance * clearance - 1e-5)
                return Fail(name, $"centerline clearance is {Math.Sqrt(nearest):F1} mm at segment {i}, " +
                    $"point ({point.X:F1},{point.Y:F1}) of {millimeterPath.Count - 1}; expected >= {clearance:F1} mm");
        }
    }

    if (expectedAngle.HasValue && !HasDirection(millimeterPath, 10, expectedAngle.Value))
        return Fail(name, $"no route segment follows expected angle {expectedAngle.Value:F1} degrees");

    if (checkStraightOffsets && !CheckStraightWallOffsets(name, mmLoops, millimeterPath,
            minimumStraightWallOffsets))
        return false;

    if (largestUncoveredDistance > maximumCoverageGap)
        return Fail(name, $"unheated coverage gap reaches {largestUncoveredDistance:F1} mm; " +
            $"maximum allowed by this check is {maximumCoverageGap:F0} mm at " +
            $"({worstCoveragePoint.X:F0},{worstCoveragePoint.Y:F0})");
    if (largestUncoveredDistance > Pitch * 1.5)
        Console.WriteLine($"      sampled maximum distance to pipe: {largestUncoveredDistance:F1} mm " +
            $"(corner allowance {maximumCoverageFactor:F2} × {Pitch:F0} mm)");

    double length = PathLength(millimeterPath);
    if (length < 1000)
        return Fail(name, $"route is unexpectedly short ({length:F0} mm)");
    return true;
}

static bool CheckExpectedFailure(string name, IList<IList<XYZ>> loops, int connectorMode = 0)
{
    IList<XYZ> path = Build(loops, connectorMode);
    return path.Count == 0 || Fail(name, $"expected a clean no-route result, received {path.Count} points");
}

static IList<XYZ> Build(IList<IList<XYZ>> loops, int connectorMode = 0)
{
    (XYZ start, XYZ startDirection, XYZ end, XYZ endDirection) = GetConnectors(loops, connectorMode);
    return AreaPipeLayout.CreateSpiralPath(loops, Pitch, WallOffset, Diameter,
        0, -77, start, startDirection, end, endDirection);
}

static (XYZ Start, XYZ StartDirection, XYZ End, XYZ EndDirection) GetConnectors(
    IList<IList<XYZ>> loops, int mode)
{
    double minX = loops.SelectMany(loop => loop).Min(point => point.X) * MmPerFoot;
    double maxX = loops.SelectMany(loop => loop).Max(point => point.X) * MmPerFoot;
    double minY = loops.SelectMany(loop => loop).Min(point => point.Y) * MmPerFoot;
    double maxY = loops.SelectMany(loop => loop).Max(point => point.Y) * MmPerFoot;
    double xCenter = (minX + maxX) / 2;
    double yCenter = (minY + maxY) / 2;
    return mode switch
    {
        1 => (FromMillimeters(minX - 600, yCenter), new XYZ(1, 0, 0),
            FromMillimeters(maxX + 600, yCenter), new XYZ(-1, 0, 0)),
        2 => (FromMillimeters(xCenter - 300, maxY + 600), new XYZ(0, -1, 0),
            FromMillimeters(xCenter + 300, maxY + 600), new XYZ(0, -1, 0)),
        3 => (FromMillimeters(maxX + 600, yCenter - 300), new XYZ(-1, 0, 0),
            FromMillimeters(xCenter + 300, maxY + 600), new XYZ(0, -1, 0)),
        _ => (FromMillimeters(maxX + 600, yCenter - 300), new XYZ(-1, 0, 0),
            FromMillimeters(maxX + 600, yCenter + 300), new XYZ(-1, 0, 0))
    };
}

static bool CheckInvalidParameters()
{
    IList<IList<XYZ>> loops = new List<IList<XYZ>> { Rect(0, 0, 4000, 3000) };
    (XYZ start, XYZ startDirection, XYZ end, XYZ endDirection) = GetConnectors(loops, 0);
    bool basicInvalids = AreaPipeLayout.CreateSpiralPath(loops, 0, WallOffset, Diameter, 0, 0,
               start, startDirection, end, endDirection).Count == 0 &&
           AreaPipeLayout.CreateSpiralPath(loops, Pitch, WallOffset, -1, 0, 0,
               start, startDirection, end, endDirection).Count == 0 &&
           AreaPipeLayout.CreateSpiralPath(loops, Diameter, WallOffset, Diameter, 0, 0,
               start, startDirection, end, endDirection).Count == 0 &&
           AreaPipeLayout.CreateSpiralPath(loops, Pitch, -1, Diameter, 0, 0,
               start, startDirection, end, endDirection).Count == 0 &&
           AreaPipeLayout.CreateSpiralPath(loops, double.NaN, WallOffset, Diameter, 0, 0,
               start, startDirection, end, endDirection).Count == 0 &&
           AreaPipeLayout.CreateSpiralPath(loops, Pitch, WallOffset, Diameter, 0, 0,
               start, new XYZ(0, 0, 0), end, endDirection).Count == 0;

    var nonFiniteLoop = new List<IList<XYZ>>
    {
        new List<XYZ> { FromMillimeters(0, 0), FromMillimeters(4000, 0),
            new XYZ(double.PositiveInfinity, 3000 / MmPerFoot, 0), FromMillimeters(0, 3000) }
    };
    return basicInvalids && AreaPipeLayout.CreateSpiralPath(nonFiniteLoop, Pitch, WallOffset,
        Diameter, 0, 0, start, startDirection, end, endDirection).Count == 0;
}

static IList<XYZ> Rect(double minX, double minY, double maxX, double maxY) =>
    Polygon((minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY));

static IList<XYZ> Polygon(params (double X, double Y)[] points) =>
    points.Select(point => FromMillimeters(point.X, point.Y)).ToList();

static IList<XYZ> Rotate(IList<XYZ> points, double degrees)
{
    double radians = degrees * Math.PI / 180.0;
    double cosine = Math.Cos(radians);
    double sine = Math.Sin(radians);
    return points.Select(point =>
    {
        Point mm = ToMillimeters(point);
        return FromMillimeters(mm.X * cosine - mm.Y * sine, mm.X * sine + mm.Y * cosine);
    }).ToList();
}

static IList<XYZ> RoundedRectangle(double minX, double minY, double maxX, double maxY,
    double radius, int segmentsPerCorner)
{
    var points = new List<XYZ>();
    (double X, double Y, double Start)[] corners =
    {
        (maxX - radius, minY + radius, -Math.PI / 2),
        (maxX - radius, maxY - radius, 0),
        (minX + radius, maxY - radius, Math.PI / 2),
        (minX + radius, minY + radius, Math.PI)
    };
    foreach ((double x, double y, double start) in corners)
        for (int i = 0; i <= segmentsPerCorner; i++)
        {
            double angle = start + i * Math.PI / (2 * segmentsPerCorner);
            points.Add(FromMillimeters(x + radius * Math.Cos(angle), y + radius * Math.Sin(angle)));
        }
    return points;
}

static IList<XYZ> Circle(double centerX, double centerY, double radius, int segments)
{
    return Enumerable.Range(0, segments)
        .Select(i =>
        {
            double angle = 2 * Math.PI * i / segments;
            return FromMillimeters(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
        }).ToList();
}

static XYZ FromMillimeters(double x, double y) => new(x / MmPerFoot, y / MmPerFoot, 0);
static Point ToMillimeters(XYZ point) => new(point.X * MmPerFoot, point.Y * MmPerFoot);
static bool Fail(string name, string message) { Console.WriteLine($"      {name}: {message}"); return false; }

static List<List<Point>> FindInnerLoops(List<List<Point>> loops) => loops
    .Where((loop, index) => loops.Where((_, other) => other != index).Any(other => PointInside(other, loop[0])))
    .ToList();

static bool InsideRegion(IList<List<Point>> loops, Point point)
{
    bool inside = false;
    foreach (List<Point> loop in loops)
        if (PointInside(loop, point)) inside = !inside;
    return inside;
}

static bool PointInside(IList<Point> loop, Point point)
{
    bool inside = false;
    for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
    {
        Point a = loop[i], b = loop[j];
        if ((a.Y > point.Y) != (b.Y > point.Y) &&
            point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            inside = !inside;
    }
    return inside;
}

static double PointToLoopDistanceSquared(Point point, IList<Point> loop)
{
    double min = double.PositiveInfinity;
    for (int i = 0; i < loop.Count; i++)
        min = Math.Min(min, SegmentDistanceSquared(point, point, loop[i], loop[(i + 1) % loop.Count]));
    return min;
}

static double SegmentToLoopDistanceSquared(Point a, Point b, IList<Point> loop)
{
    double min = double.PositiveInfinity;
    for (int i = 0; i < loop.Count; i++)
        min = Math.Min(min, SegmentDistanceSquared(a, b, loop[i], loop[(i + 1) % loop.Count]));
    return min;
}

static double SegmentDistanceSquared(Point a, Point b, Point c, Point d)
{
    if (SegmentsCross(a, b, c, d)) return 0;
    return Math.Min(Math.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d)),
        Math.Min(PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b)));
}

static double PointSegmentDistanceSquared(Point point, Point start, Point end)
{
    double dx = end.X - start.X, dy = end.Y - start.Y;
    double length2 = dx * dx + dy * dy;
    if (length2 < 1e-12) return DistanceSquared(point, start);
    double t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length2, 0, 1);
    return DistanceSquared(point, new Point(start.X + t * dx, start.Y + t * dy));
}

static bool HasSelfCrossing(IList<Point> path)
{
    for (int i = 0; i + 1 < path.Count; i++)
        for (int j = i + 2; j + 1 < path.Count; j++)
            if (SegmentsCross(path[i], path[i + 1], path[j], path[j + 1])) return true;
    return false;
}

static bool SegmentsCross(Point a, Point b, Point c, Point d)
{
    double abC = Cross(a, b, c), abD = Cross(a, b, d);
    double cdA = Cross(c, d, a), cdB = Cross(c, d, b);
    return abC * abD < -1e-6 && cdA * cdB < -1e-6;
}

static bool HasDirection(IList<Point> path, double minLength, double expectedDegrees)
{
    for (int i = 0; i + 1 < path.Count; i++)
    {
        double dx = path[i + 1].X - path[i].X, dy = path[i + 1].Y - path[i].Y;
        if (Math.Sqrt(dx * dx + dy * dy) < minLength) continue;
        double angle = Math.Abs(Math.Atan2(dy, dx) * 180 / Math.PI) % 90;
        double difference = Math.Abs(angle - expectedDegrees % 90);
        if (Math.Min(difference, 90 - difference) <= 2) return true;
    }
    return false;
}

static bool IsCollinear(Point vector, XYZ direction)
{
    double length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
    double directionLength = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
    if (length <= 1e-9 || directionLength <= 1e-9) return false;
    double dot = Math.Abs((vector.X * direction.X + vector.Y * direction.Y) /
        (length * directionLength));
    return dot >= 0.7;
}

static bool CheckStraightWallOffsets(string name, IList<List<Point>> loops, IList<Point> path,
    int minimumMeasuredWalls)
{
    double minAllowed = RequiredCenterlineClearance;
    double maxAllowed = WallOffset * 1.15 + Diameter / 2.0 + 5.0;
    int checkedWalls = 0;
    for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
    {
        List<Point> loop = loops[loopIndex];
    for (int edge = 0; edge < loop.Count; edge++)
    {
        Point wallStart = loop[edge], wallEnd = loop[(edge + 1) % loop.Count];
        double wallDx = wallEnd.X - wallStart.X, wallDy = wallEnd.Y - wallStart.Y;
        double wallLength = Math.Sqrt(wallDx * wallDx + wallDy * wallDy);
        if (wallLength < Pitch * 3) continue;
        double ux = wallDx / wallLength, uy = wallDy / wallLength;
        double nearest = double.PositiveInfinity;
        Point nearestStart = default, nearestEnd = default;
        for (int i = 3; i + 4 < path.Count; i++)
        {
            Point pipeStart = path[i], pipeEnd = path[i + 1];
            double pipeDx = pipeEnd.X - pipeStart.X, pipeDy = pipeEnd.Y - pipeStart.Y;
            double pipeLength = Math.Sqrt(pipeDx * pipeDx + pipeDy * pipeDy);
            if (pipeLength < Pitch * 0.5) continue;
            double parallelError = Math.Abs(ux * pipeDy - uy * pipeDx) / pipeLength;
            if (parallelError > Math.Sin(2 * Math.PI / 180)) continue;

            double firstProjection = (pipeStart.X - wallStart.X) * ux + (pipeStart.Y - wallStart.Y) * uy;
            double secondProjection = (pipeEnd.X - wallStart.X) * ux + (pipeEnd.Y - wallStart.Y) * uy;
            double overlap = Math.Min(wallLength, Math.Max(firstProjection, secondProjection)) -
                Math.Max(0, Math.Min(firstProjection, secondProjection));
            if (overlap < Math.Min(Pitch * 0.5, wallLength * 0.1)) continue;
            double distance = Math.Abs(ux * (pipeStart.Y - wallStart.Y) - uy * (pipeStart.X - wallStart.X));
            if (distance < nearest)
            {
                nearest = distance;
                nearestStart = pipeStart;
                nearestEnd = pipeEnd;
            }
        }

        if (!double.IsFinite(nearest)) continue;
        checkedWalls++;
        if (nearest < minAllowed - 0.1 || nearest > maxAllowed)
            return Fail(name, $"loop {loopIndex}, edge {edge} has a parallel run {nearest - Diameter / 2:F1} mm from wall; " +
                $"allowed surface gap is {WallOffset * 0.9:F0}–{WallOffset * 1.15:F0} mm; " +
                $"closest segment ({nearestStart.X:F0},{nearestStart.Y:F0})–({nearestEnd.X:F0},{nearestEnd.Y:F0})");
    }
    }

    return checkedWalls >= minimumMeasuredWalls ||
        Fail(name, $"only {checkedWalls} long wall offsets could be measured; expected {minimumMeasuredWalls}");
}

static double PathLength(IList<Point> path)
{
    double length = 0;
    for (int i = 0; i + 1 < path.Count; i++) length += Distance(path[i], path[i + 1]);
    return length;
}

static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
static double Distance(Point a, Point b) => Math.Sqrt(DistanceSquared(a, b));
static double DistanceSquared(Point a, Point b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
readonly record struct Point(double X, double Y);
