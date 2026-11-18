using Autodesk.Revit.DB;
using System.Reflection;
using Underfloor_heating_generator.Infrastructure.Revit;

const double MmPerFoot = 304.8;
const double Pitch = 150;
const double WallOffset = 100;
const double Diameter = 16;
const double RequiredCenterlineClearance = WallOffset * 0.9 + Diameter / 2.0 - 2.0;

var checks = new List<(string Name, Func<bool> Run)>
{
    ("prototype perimeter spiral on rectangle", () => CheckPerimeterPrototype("prototype rectangle",
        new[] { Rect(0, 0, 6000, 4000) })),
    ("Clipper2 resolves from beside the plugin assembly", CheckSideBySideClipperResolution),
    ("round joins preserve requested 45 and 90 degree bend radii", CheckRoundJoinGeometry),
    ("rounded polygon offsets remain simple at 45 degree bevels", CheckRoundedOffsetBevel),
    ("prototype perimeter spiral on rotated polygon", () => CheckPerimeterPrototype("prototype rotated",
        new[] { Rotate(Rect(0, 0, 5000, 3200), 27) })),
    ("prototype outer lap follows a rotated chamfer", () => CheckPrototypeBoundaryLapForPolygon(
        "prototype rotated chamfer lap",
        Rotate(Polygon((0, 0), (6200, 0), (6200, 2700), (5200, 3900), (0, 3900)), -18))),
    ("prototype perimeter spiral on chamfered polygon", () => CheckPerimeterPrototype("prototype chamfer",
        new[] { Polygon((0, 0), (6200, 0), (6200, 2700), (5200, 3900), (0, 3900)) })),
    ("prototype perimeter spiral on rotated chamfered polygon", () => CheckPerimeterPrototype("prototype rotated chamfer",
        new[] { Rotate(Polygon((0, 0), (6200, 0), (6200, 2700), (5200, 3900), (0, 3900)), -18) })),
    ("prototype outer lap follows a chamfered wall", () => CheckPrototypeBoundaryLap()),
    ("production creates a safe model-line route in a rectangle", () =>
        CheckProductionAreaRoute("model-line rectangle", new[] { Rect(0, 0, 6000, 4000) })),
    ("production creates a safe model-line route in a rotated rectangle", () =>
        CheckProductionAreaRoute("model-line rotated rectangle",
            new[] { Rotate(Rect(0, 0, 5000, 3200), 27) })),
    ("production creates a safe model-line route around a chamfer", () =>
        CheckProductionAreaRoute("model-line chamfer",
            new[] { Polygon((0, 0), (6200, 0), (6200, 2700), (5200, 3900), (0, 3900)) })),
    ("prototype perimeter spiral on screenshot wedge", () => CheckPerimeterPrototype(
        "screenshot wedge perimeter", new[]
        {
            Polygon((0, 0), (3900, 0), (9600, 3900), (9600, 5300), (0, 5300))
        })),
    ("prototype seam-aware perimeter spiral on screenshot wedge", () => CheckPerimeterPrototype(
        "screenshot wedge seam perimeter", new[]
        {
            Polygon((0, 0), (3900, 0), (9600, 3900), (9600, 5300), (0, 5300))
        }, withExitSeam: true)),
    ("production creates a safe model-line route in screenshot wedge", () => CheckProductionAreaRoute(
        "model-line screenshot wedge", new[]
        {
            Polygon((0, 0), (3900, 0), (9600, 3900), (9600, 5300), (0, 5300))
        })),
    ("prototype screenshot wedge with production offset and port seam", () => CheckPerimeterPrototype(
        "wedge offset 110 external port seam", new[]
        {
            Polygon((0, 0), (3900, 0), (9600, 3900), (9600, 5300), (0, 5300))
        }, pitchValue: 150, clearanceValue: 108, eastExit: true, openingValue: 375)),
    ("prototype rejects L-shaped offset collapse", () => CheckPrototypeRejects("prototype L",
        new[] { Polygon((0, 0), (6500, 0), (6500, 2100), (2800, 2100), (2800, 5200), (0, 5200)) })),
    ("prototype cleanly rejects a U-shaped room for production fallback", () => CheckPrototypeRejects("prototype U",
        new[] { Polygon((0, 0), (7000, 0), (7000, 5200), (5100, 5200), (5100, 1900),
            (1900, 1900), (1900, 5200), (0, 5200)) })),
    ("prototype rejects acute notch with insufficient coverage", () => CheckPrototypeRejects("prototype notch",
        new[] { Polygon((0, 0), (7000, 0), (7000, 5000), (3700, 5000), (3400, 3300),
            (3100, 5000), (0, 5000)) })),
    ("production contour route around one internal hole", () => CheckGenerated("production hole contour",
        new[] { Rect(0, 0, 7000, 4800), Rect(2300, 1500, 4700, 3400) },
        checkStraightOffsets: true, maximumCoverageFactor: 1.65)),
    ("chamfered outer wall is followed around an internal obstacle", () => CheckGenerated(
        "chamfer with hole", new[]
        {
            Polygon((0, 0), (7000, 0), (7000, 2800), (5500, 4400), (0, 4400)),
            Rect(2300, 1500, 4700, 3200)
        }, checkStraightOffsets: true, minimumStraightWallOffsets: 9, maximumCoverageFactor: 1.65,
        connectorGapMillimeters: 200)),
    ("chamfer run follows the actual wall at the requested setback", CheckChamferOffset),
    ("chamfer with hole and ports beside the sloped wall", () => CheckGenerated(
        "chamfer ports at bevel", new[]
        {
            Polygon((0, 0), (7000, 0), (7000, 2800), (5500, 4400), (0, 4400)),
            Rect(2300, 1500, 4700, 3200)
        }, checkStraightOffsets: true, minimumStraightWallOffsets: 9, maximumCoverageFactor: 1.65,
        connectorGapMillimeters: 200, connectorCenterOffsetMillimeters: 1500)),
    ("chamfer with hole and ports beside the lower corner", () => CheckGenerated(
        "chamfer ports at lower corner", new[]
        {
            Polygon((0, 0), (7000, 0), (7000, 2800), (5500, 4400), (0, 4400)),
            Rect(2300, 1500, 4700, 3200)
        }, checkStraightOffsets: true, minimumStraightWallOffsets: 9, maximumCoverageFactor: 1.65,
        connectorGapMillimeters: 200, connectorCenterOffsetMillimeters: -1500)),
    ("asymmetric trapezoid with an internal obstacle", () => CheckGenerated(
        "trapezoid with obstacle", new[]
        {
            Polygon((0, 0), (7000, 0), (6200, 4400), (800, 4400)),
            Rect(2700, 1700, 4300, 3000)
        }, maximumCoverageFactor: 1.65)),
    ("octagonal room with an internal obstacle", () => CheckGenerated(
        "octagon with obstacle", new[]
        {
            Polygon((500, 0), (6500, 0), (7000, 500), (7000, 3900),
                (6500, 4400), (500, 4400), (0, 3900), (0, 500)),
            Rect(2500, 1600, 4500, 3000)
        }, maximumCoverageFactor: 1.65)),
    ("short bevel around an internal obstacle", () => CheckGenerated(
        "short bevel", new[]
        {
            Polygon((0, 0), (7000, 0), (7000, 3700), (6300, 4400), (0, 4400)),
            Rect(2400, 1600, 4600, 3100)
        }, maximumCoverageFactor: 1.65, connectorGapMillimeters: 200,
        connectorCenterOffsetMillimeters: 1400)),
    ("steep bevel around an internal obstacle", () => CheckGenerated(
        "steep bevel", new[]
        {
            Polygon((0, 0), (7000, 0), (7000, 1700), (4800, 4400), (0, 4400)),
            Rect(2200, 1600, 4400, 3100)
        }, maximumCoverageFactor: 1.65, connectorGapMillimeters: 200,
        connectorCenterOffsetMillimeters: 1100)),
    ("asymmetric five-sided room with two obstacles", () => CheckGenerated(
        "five-sided double obstacle", new[]
        {
            Polygon((300, 0), (7000, 300), (6700, 3600), (5200, 4400), (0, 4000)),
            Rect(1400, 1100, 2600, 2500), Rect(4200, 1700, 5100, 3000)
        }, maximumCoverageFactor: 1.65)),
    ("rotated trapezoid around an internal obstacle", () => CheckGenerated(
        "rotated trapezoid", new[]
        {
            Rotate(Polygon((0, 0), (7000, 0), (6200, 4400), (800, 4400)), 31),
            Rotate(Rect(2700, 1700, 4300, 3000), 31)
        }, expectedAngle: 31, maximumCoverageFactor: 1.65)),
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
        { Rect(0, 0, 9000, 5000), Rect(1800, 1500, 3000, 3400), Rect(5700, 900, 7600, 4100) },
        maximumCoverageFactor: 1.8)),
    ("curved boundary approximation with a hole", () => CheckGenerated("rounded room", new[]
        { RoundedRectangle(0, 0, 7000, 5000, 700, 8), Circle(3500, 2500, 650, 24) })),
    ("acute notch", () => CheckGenerated("acute notch", new[]
        { Polygon((0, 0), (7000, 0), (7000, 5000), (3700, 5000), (3400, 3300),
            (3100, 5000), (0, 5000)) })),
    ("same-side horizontal pipe connectors", () => CheckGenerated("horizontal connectors",
        new[] { Rect(0, 0, 6000, 4000) }, 0)),
    ("rectangle with close doorway ports keeps the spiral bridges apart", CheckDoorwaySpiral),
    ("rectangular spiral reduces pitch to fill the center", () =>
        CheckRectangularPitch("reduced pitch", 2434, 132, 149.9)),
    ("rectangular spiral can reduce pitch by nearly twelve percent", () =>
        CheckRectangularPitch("near minimum pitch", 1730, 132, 135)),
    ("rectangular spiral increases pitch slightly to fill the center", () =>
        CheckRectangularPitch("increased pitch", 2601, 150.1, 154.5)),
    ("rectangular spiral keeps nominal pitch when the center is filled", () =>
        CheckRectangularPitch("nominal pitch", 2551, 149.99, 150.01)),
    ("same-side vertical pipe connectors", () => CheckGenerated("vertical connectors",
        new[] { Rect(0, 0, 6000, 4000) }, 2)),
    ("adjacent-side pipe connectors", () => CheckGenerated("adjacent connectors",
        new[] { Rect(0, 0, 6000, 4000) }, 3)),
    ("opposite-side pipe connectors route safely", () =>
        CheckGenerated("opposite connectors", new[] { Rect(0, 0, 6000, 4000) }, 1)),
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

static bool CheckSideBySideClipperResolution()
{
    var boundary = new List<PerimeterSpiralCore.Point>
    {
        new(0, 0), new(4000, 0), new(4000, 3000), new(0, 3000)
    };
    if (!PerimeterSpiralCore.TryOffsetContour(boundary, 100, 30, out _))
        return Fail("side-by-side Clipper2", "offset operation failed");

    Assembly clipperAssembly = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name,
            "Clipper2Lib", StringComparison.OrdinalIgnoreCase));
    if (clipperAssembly == null)
        return Fail("side-by-side Clipper2", "assembly was not loaded");
    string expectedPath = Path.Combine(AppContext.BaseDirectory, "Clipper2Lib.dll");
    if (!string.Equals(Path.GetFullPath(clipperAssembly.Location), Path.GetFullPath(expectedPath),
        StringComparison.OrdinalIgnoreCase))
        return Fail("side-by-side Clipper2", $"assembly was not loaded from the plugin directory: {clipperAssembly.Location}");
    if (clipperAssembly.GetName().Version != new Version(2, 0, 0, 0))
        return Fail("side-by-side Clipper2", $"unexpected version {clipperAssembly.GetName().Version}");

    Console.WriteLine($"      Clipper2 loaded from {clipperAssembly.Location}");
    return true;
}

static bool CheckDoorwaySpiral()
{
    const string name = "doorway spiral";
    var loops = new[] { Rect(0, 0, 4350, 2650) };
    const double portGap = 170;
    const double portOffset = 375;
    List<Point> route = Build(loops, connectorGapMillimeters: portGap,
        connectorCenterOffsetMillimeters: portOffset).Select(ToMillimeters).ToList();
    if (route.Count < 4)
        return Fail(name, "no route");

    double inset = WallOffset + Diameter / 2;
    for (int i = 0; i + 1 < route.Count; i++)
    {
        Point a = route[i], b = route[i + 1];
        double dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        if (Math.Min(dx, dy) > 0.05 &&
            (Math.Min(a.X, b.X) < 4350 - inset - 0.05 || Math.Abs(dx - dy) > 0.05))
            return Fail(name, "oblique segment is not a 45-degree lead chamfer");
    }
    var bottomLaneOffsets = route.Zip(route.Skip(1), (a, b) => (a, b))
        .Where(segment => Math.Abs(segment.a.Y - segment.b.Y) < 0.05 &&
            Math.Min(segment.a.X, segment.b.X) < 1800 && Math.Max(segment.a.X, segment.b.X) > 1800 &&
            segment.a.Y < 1000)
        .Select(segment => segment.a.Y).OrderBy(y => y).ToList();
    if (bottomLaneOffsets.Count < 5 || Math.Abs(bottomLaneOffsets[0] - inset) > 0.05)
        return Fail(name, $"unexpected bottom lane offsets: {string.Join(", ", bottomLaneOffsets.Select(y => y.ToString("F1")))}");
    double fittedPitch = bottomLaneOffsets[1] - bottomLaneOffsets[0];
    if (fittedPitch < Pitch * 0.88 - 0.05 || fittedPitch > Pitch * 1.03 + 0.05 ||
        bottomLaneOffsets.Zip(bottomLaneOffsets.Skip(1), (a, b) => b - a)
            .Any(step => Math.Abs(step - fittedPitch) > 0.05))
        return Fail(name, $"uneven bottom lanes or pitch {fittedPitch:F2} mm outside the allowed range");

    double minimum = double.PositiveInfinity;
    for (int i = 0; i + 1 < route.Count; i++)
    {
        double interveningLength = 0;
        for (int j = i + 1; j + 1 < route.Count; j++)
        {
            if (interveningLength >= Pitch)
                minimum = Math.Min(minimum, Math.Sqrt(
                    SegmentDistanceSquared(route[i], route[i + 1], route[j], route[j + 1])));
            interveningLength += Distance(route[j], route[j + 1]);
        }
    }
    if (minimum < Pitch * 0.25)
        return Fail(name, $"separate runs approach within {minimum:F1} mm");

    Console.WriteLine($"      {name}: minimum separation between separate runs {minimum:F1} mm");
    return CheckGenerated(name, loops, connectorGapMillimeters: portGap,
        connectorCenterOffsetMillimeters: portOffset);
}

static bool CheckRectangularPitch(string name, double insetHeight, double minimumPitch, double maximumPitch)
{
    double inset = WallOffset + Diameter / 2;
    var loops = new[] { Rect(0, 0, 4350, insetHeight + 2 * inset) };
    List<Point> route = Build(loops, connectorGapMillimeters: 170,
        connectorCenterOffsetMillimeters: 375).Select(ToMillimeters).ToList();
    var levels = route.Zip(route.Skip(1), (a, b) => (a, b))
        .Where(segment => Math.Abs(segment.a.Y - segment.b.Y) < 0.05 &&
            Math.Min(segment.a.X, segment.b.X) < 1800 && Math.Max(segment.a.X, segment.b.X) > 1800)
        .Select(segment => segment.a.Y).OrderBy(y => y).ToList();
    if (levels.Count < 8 || Math.Abs(levels[0] - inset) > 0.05 ||
        Math.Abs(levels[^1] - inset - insetHeight) > 0.05)
        return Fail(name, "wall setback or number of heating lanes is wrong");

    List<double> gaps = levels.Zip(levels.Skip(1), (a, b) => b - a).ToList();
    double pitch = gaps[0];
    if (pitch < minimumPitch - 0.001 || pitch > maximumPitch + 0.001 ||
        gaps.Any(gap => Math.Abs(gap - pitch) > pitch * 0.03 + 0.05))
        return Fail(name, $"pitch {pitch:F2} mm, gaps {gaps.Min():F2}–{gaps.Max():F2} mm");

    Console.WriteLine($"      {name}: pitch {pitch:F2} mm, largest gap {gaps.Max():F2} mm");
    return CheckGenerated(name, loops, connectorGapMillimeters: 170, connectorCenterOffsetMillimeters: 375);
}

static bool CheckGenerated(string name, IList<IList<XYZ>> loops, int connectorMode = 0,
    double? expectedAngle = null, bool checkStraightOffsets = false, int minimumStraightWallOffsets = 4,
    double maximumCoverageFactor = 1.5, double connectorGapMillimeters = 600,
    double connectorCenterOffsetMillimeters = 0)
{
    IList<XYZ> path = Build(loops, connectorMode, connectorGapMillimeters, connectorCenterOffsetMillimeters);
    if (path.Count < 4)
        return Fail(name, $"expected a complete route, received {path.Count} points");

    if (path.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z)))
        return Fail(name, "route contains a non-finite coordinate");

    List<Point> millimeterPath = path.Select(ToMillimeters).ToList();
    (XYZ expectedStart, XYZ expectedStartDirection, XYZ expectedEnd, XYZ expectedEndDirection) =
        GetConnectors(loops, connectorMode, connectorGapMillimeters, connectorCenterOffsetMillimeters);
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
    int worstCoverageSegment = -1;
    for (double x = minX + clearance; x <= maxX - clearance; x += 200)
    for (double y = minY + clearance; y <= maxY - clearance; y += 200)
    {
        var point = new Point(x, y);
        if (!InsideRegion(mmLoops, point) ||
            mmLoops.Min(loop => PointToLoopDistanceSquared(point, loop)) < clearance * clearance - 1e-5)
            continue;
        double nearest = double.PositiveInfinity;
        int nearestSegment = -1;
        for (int i = bodyStart; i < bodyEnd; i++)
        {
            double distanceSquared = PointSegmentDistanceSquared(point,
                millimeterPath[i], millimeterPath[i + 1]);
            if (distanceSquared < nearest)
            {
                nearest = distanceSquared;
                nearestSegment = i;
            }
        }
        double coverageDistance = Math.Sqrt(nearest);
        if (coverageDistance > largestUncoveredDistance)
        {
            largestUncoveredDistance = coverageDistance;
            worstCoveragePoint = point;
            worstCoverageSegment = nearestSegment;
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
            minimumStraightWallOffsets, bodyStart, bodyEnd))
        return false;

    if (largestUncoveredDistance > maximumCoverageGap)
    {
        return Fail(name, $"unheated coverage gap reaches {largestUncoveredDistance:F1} mm; " +
            $"maximum allowed by this check is {maximumCoverageGap:F0} mm at " +
            $"({worstCoveragePoint.X:F0},{worstCoveragePoint.Y:F0}), closest segment " +
            $"{worstCoverageSegment}: ({millimeterPath[worstCoverageSegment].X:F0},{millimeterPath[worstCoverageSegment].Y:F0})–" +
            $"({millimeterPath[worstCoverageSegment + 1].X:F0},{millimeterPath[worstCoverageSegment + 1].Y:F0})");
    }
    if (largestUncoveredDistance > Pitch * 1.5)
        Console.WriteLine($"      sampled maximum distance to pipe: {largestUncoveredDistance:F1} mm " +
            $"(corner allowance {maximumCoverageFactor:F2} × {Pitch:F0} mm)");

    double length = PathLength(millimeterPath);
    if (length < 1000)
        return Fail(name, $"route is unexpectedly short ({length:F0} mm)");
    return true;
}

static bool CheckChamferOffset()
{
    const string name = "chamfer wall setback";
    var loops = new IList<XYZ>[]
    {
        Polygon((0, 0), (7000, 0), (7000, 2800), (5500, 4400), (0, 4400)),
        Rect(2300, 1500, 4700, 3200)
    };
    IList<XYZ> generated = Build(loops, connectorGapMillimeters: 200);
    if (generated.Count < 4)
        return Fail(name, "no route generated for the chamfered room with an obstacle");

    List<Point> route = generated.Select(ToMillimeters).ToList();
    Point wallStart = new(7000, 2800), wallEnd = new(5500, 4400);
    double wallX = wallEnd.X - wallStart.X;
    double wallY = wallEnd.Y - wallStart.Y;
    double wallLength = Math.Sqrt(wallX * wallX + wallY * wallY);
    double directionX = wallX / wallLength;
    double directionY = wallY / wallLength;
    double nearestSetback = double.PositiveInfinity;
    double longestOverlap = 0;
    for (int i = 0; i + 1 < route.Count; i++)
    {
        double segmentX = route[i + 1].X - route[i].X;
        double segmentY = route[i + 1].Y - route[i].Y;
        double length = Math.Sqrt(segmentX * segmentX + segmentY * segmentY);
        if (length < 500 || Math.Abs(directionX * segmentY / length - directionY * segmentX / length) >
            Math.Sin(2 * Math.PI / 180))
            continue;
        double first = (route[i].X - wallStart.X) * directionX +
                       (route[i].Y - wallStart.Y) * directionY;
        double second = (route[i + 1].X - wallStart.X) * directionX +
                        (route[i + 1].Y - wallStart.Y) * directionY;
        double overlap = Math.Min(wallLength, Math.Max(first, second)) -
                         Math.Max(0, Math.Min(first, second));
        if (overlap <= 0)
            continue;
        double setback = Math.Abs((route[i].X - wallStart.X) * directionY -
                                  (route[i].Y - wallStart.Y) * directionX);
        if (overlap > longestOverlap)
        {
            longestOverlap = overlap;
            nearestSetback = setback;
        }
    }

    double expectedSetback = WallOffset + Diameter / 2.0;
    return longestOverlap >= 500 && Math.Abs(nearestSetback - expectedSetback) <= 15
        ? true
        : Fail(name, $"no long straight run at {expectedSetback:F0} mm from chamfer; " +
                      $"best overlap={longestOverlap:F1} mm, setback={nearestSetback:F1} mm");
}

static bool CheckPerimeterPrototype(string name, IList<IList<XYZ>> boundaries, bool withExitSeam = false,
    double pitchValue = Pitch, double? clearanceValue = null, bool eastExit = false,
    double openingValue = 375)
{
    IList<XYZ> boundary = boundaries[0];
    List<PerimeterSpiralCore.Point> polygon = boundary.Select(point =>
        new PerimeterSpiralCore.Point(point.X * MmPerFoot, point.Y * MmPerFoot)).ToList();
    var loops = new IReadOnlyList<PerimeterSpiralCore.Point>[] { polygon };
    double centerlineClearance = clearanceValue ?? WallOffset + Diameter / 2.0;
    double minX = polygon.Min(point => point.X), maxX = polygon.Max(point => point.X);
    double minY = polygon.Min(point => point.Y), maxY = polygon.Max(point => point.Y);
    var seam = eastExit
        ? new PerimeterSpiralCore.Point(maxX + 600, (minY + maxY) / 2.0)
        : new PerimeterSpiralCore.Point((minX + maxX) / 2.0, (minY + maxY) / 2.0);
    List<PerimeterSpiralCore.Point> route;
    bool built = withExitSeam || eastExit
        ? PerimeterSpiralCore.TryBuild(loops, pitchValue, centerlineClearance, seam, openingValue, out route)
        : PerimeterSpiralCore.TryBuild(loops, pitchValue, centerlineClearance, out route);
    if (!built)
        return Fail(name, "prototype did not produce a complete perimeter-to-center route");
    if (route.Count < 10)
        return Fail(name, $"prototype route is too coarse ({route.Count} points)");
    if (HasSelfCrossing(route.Select(point => new Point(point.X, point.Y)).ToList()))
        return Fail(name, "prototype route crosses itself");

    List<List<Point>> mmLoops = new() { polygon.Select(point => new Point(point.X, point.Y)).ToList() };
    double maximumGap = 1.65 * pitchValue;
    double worstGap = 0;
    Point worstPoint = default;
    double sampleStep = Math.Min(75, pitchValue / 2.0);
    for (double x = minX + centerlineClearance; x <= maxX - centerlineClearance; x += sampleStep)
    for (double y = minY + centerlineClearance; y <= maxY - centerlineClearance; y += sampleStep)
    {
        Point sample = new(x, y);
        if (!InsideRegion(mmLoops, sample) || PointToLoopDistanceSquared(sample, mmLoops[0]) <
            centerlineClearance * centerlineClearance)
            continue;
        double nearest = double.PositiveInfinity;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            Point a = new(route[i].X, route[i].Y), b = new(route[i + 1].X, route[i + 1].Y);
            nearest = Math.Min(nearest, PointSegmentDistanceSquared(sample, a, b));
        }
        double gap = Math.Sqrt(nearest);
        if (gap > worstGap) { worstGap = gap; worstPoint = sample; }
    }
    if (worstGap > maximumGap)
        return Fail(name, $"sampled coverage gap is {worstGap:F1} mm at ({worstPoint.X:F0},{worstPoint.Y:F0}); " +
            $"target <= {maximumGap:F1} mm");

    for (int i = 0; i + 1 < route.Count; i++)
    {
        int samples = Math.Max(1, (int)Math.Ceiling(Distance(
            new Point(route[i].X, route[i].Y), new Point(route[i + 1].X, route[i + 1].Y)) / 10));
        for (int sample = 0; sample <= samples; sample++)
        {
            Point point = Lerp(new Point(route[i].X, route[i].Y), new Point(route[i + 1].X, route[i + 1].Y),
                (double)sample / samples);
            if (!InsideRegion(mmLoops, point) || PointToLoopDistanceSquared(point, mmLoops[0]) <
                (centerlineClearance - 2) * (centerlineClearance - 2))
                return Fail(name, $"route violates the requested boundary clearance at segment {i}");
        }
    }
    Console.WriteLine($"      prototype: {route.Count} vertices, {PathLength(route.Select(p => new Point(p.X, p.Y)).ToList()):F0} mm, " +
        $"maximum sampled coverage gap {worstGap:F1} mm");
    return true;
}

static bool CheckPrototypeRejects(string name, IList<IList<XYZ>> boundaries)
{
    var loops = boundaries.Select(loop => (IReadOnlyList<PerimeterSpiralCore.Point>)loop.Select(point =>
        new PerimeterSpiralCore.Point(point.X * MmPerFoot, point.Y * MmPerFoot)).ToList()).ToList();
    return !PerimeterSpiralCore.TryBuild(loops, Pitch, WallOffset + Diameter / 2.0, out var route) ||
        Fail(name, $"unsupported contour returned {route.Count} points instead of a clear rejection");
}

static bool CheckPrototypeBoundaryLap()
{
    IList<XYZ> source = Polygon((0, 0), (7000, 0), (7000, 2800), (5500, 4400), (0, 4400));
    IReadOnlyList<PerimeterSpiralCore.Point> boundary = source.Select(point =>
        new PerimeterSpiralCore.Point(point.X * MmPerFoot, point.Y * MmPerFoot)).ToList();
    if (!PerimeterSpiralCore.TryBuildBoundaryLap(boundary, Pitch, WallOffset + Diameter / 2.0,
            out List<PerimeterSpiralCore.Point> lap))
        return Fail("prototype boundary lap", "the offset outer perimeter was rejected");

    Point a = new(7000, 2800), b = new(5500, 4400);
    double dx = b.X - a.X, dy = b.Y - a.Y;
    double wallLength = Math.Sqrt(dx * dx + dy * dy), ux = dx / wallLength, uy = dy / wallLength;
    double nearest = double.PositiveInfinity;
    for (int i = 0; i + 1 < lap.Count; i++)
    {
        Point pipeStart = new(lap[i].X, lap[i].Y), pipeEnd = new(lap[i + 1].X, lap[i + 1].Y);
        double pipeDx = pipeEnd.X - pipeStart.X, pipeDy = pipeEnd.Y - pipeStart.Y;
        double pipeLength = Math.Sqrt(pipeDx * pipeDx + pipeDy * pipeDy);
        if (pipeLength < Pitch * 0.5 || Math.Abs(ux * pipeDy - uy * pipeDx) / pipeLength > Math.Sin(2 * Math.PI / 180))
            continue;
        double p0 = (pipeStart.X - a.X) * ux + (pipeStart.Y - a.Y) * uy;
        double p1 = (pipeEnd.X - a.X) * ux + (pipeEnd.Y - a.Y) * uy;
        if (Math.Min(wallLength, Math.Max(p0, p1)) - Math.Max(0, Math.Min(p0, p1)) < wallLength * 0.1)
            continue;
        nearest = Math.Min(nearest, Math.Abs(ux * (pipeStart.Y - a.Y) - uy * (pipeStart.X - a.X)));
    }
    if (!double.IsFinite(nearest))
        return Fail("prototype boundary lap", "no pipe segment follows the chamfer");
    double wallGap = nearest - Diameter / 2.0;
    return wallGap >= WallOffset * 0.9 && wallGap <= WallOffset * 1.15
        ? true
        : Fail("prototype boundary lap", $"chamfer setback is {wallGap:F1} mm");
}

static bool CheckProductionAreaRoute(string name, IList<IList<XYZ>> loops) =>
    CheckGenerated(name, loops, maximumCoverageFactor: 1.65);

static bool CheckPrototypeBoundaryLapForPolygon(string name, IList<XYZ> source)
{
    var boundary = source.Select(point => new PerimeterSpiralCore.Point(
        point.X * MmPerFoot, point.Y * MmPerFoot)).ToList();
    var seam = new PerimeterSpiralCore.Point(
        boundary.Average(point => point.X), boundary.Average(point => point.Y));
    if (!PerimeterSpiralCore.TryBuildBoundaryLap(boundary, Pitch,
            WallOffset + Diameter / 2.0, seam, 700, out var lap))
        return Fail(name, "outer lap did not build");
    if (HasSelfCrossing(lap.Select(point => new Point(point.X, point.Y)).ToList()))
        return Fail(name, "outer lap crosses itself");

    for (int edgeIndex = 0; edgeIndex < boundary.Count; edgeIndex++)
    {
        Point wallStart = new(boundary[edgeIndex].X, boundary[edgeIndex].Y);
        Point wallEnd = new(boundary[(edgeIndex + 1) % boundary.Count].X,
            boundary[(edgeIndex + 1) % boundary.Count].Y);
        double dx = wallEnd.X - wallStart.X, dy = wallEnd.Y - wallStart.Y;
        double wallLength = Math.Sqrt(dx * dx + dy * dy);
        if (wallLength < 500)
            continue;
        double ux = dx / wallLength, uy = dy / wallLength;
        double nearest = double.PositiveInfinity;
        for (int segmentIndex = 0; segmentIndex + 1 < lap.Count; segmentIndex++)
        {
            Point pipeStart = new(lap[segmentIndex].X, lap[segmentIndex].Y);
            Point pipeEnd = new(lap[segmentIndex + 1].X, lap[segmentIndex + 1].Y);
            double pipeDx = pipeEnd.X - pipeStart.X, pipeDy = pipeEnd.Y - pipeStart.Y;
            double pipeLength = Math.Sqrt(pipeDx * pipeDx + pipeDy * pipeDy);
            if (pipeLength < Pitch * 0.5 || Math.Abs(ux * pipeDy - uy * pipeDx) / pipeLength >
                Math.Sin(2 * Math.PI / 180))
                continue;
            double p0 = (pipeStart.X - wallStart.X) * ux + (pipeStart.Y - wallStart.Y) * uy;
            double p1 = (pipeEnd.X - wallStart.X) * ux + (pipeEnd.Y - wallStart.Y) * uy;
            if (Math.Min(wallLength, Math.Max(p0, p1)) - Math.Max(0, Math.Min(p0, p1)) < wallLength * 0.1)
                continue;
            nearest = Math.Min(nearest, Math.Abs(ux * (pipeStart.Y - wallStart.Y) -
                uy * (pipeStart.X - wallStart.X)));
        }
        double wallGap = nearest - Diameter / 2.0;
        if (!double.IsFinite(nearest) || wallGap < WallOffset * 0.9 || wallGap > WallOffset * 1.15)
            return Fail(name, $"wall edge {edgeIndex} setback is {wallGap:F1} mm");
    }
    return true;
}

static bool CheckRoundJoinGeometry()
{
    foreach ((string name, double angleDegrees, IReadOnlyList<PerimeterSpiralCore.Point> source) in new[]
    {
        ("45-degree bend", 45.0, new List<PerimeterSpiralCore.Point>
        {
            new(0, 0), new(100, 0), new(171, 71)
        }),
        ("90-degree bend", 90.0, new List<PerimeterSpiralCore.Point>
        {
            new(0, 0), new(100, 0), new(100, 100)
        })
    })
    {
        const double radius = 20;
        if (!PerimeterSpiralCore.TryRoundPolyline(source, radius, 10, closed: false,
                out List<PerimeterSpiralCore.Point> rounded))
            return Fail(name, "round join could not be constructed");
        if (Distance(new Point(rounded[0].X, rounded[0].Y), new Point(source[0].X, source[0].Y)) > 1e-6 ||
            Distance(new Point(rounded[^1].X, rounded[^1].Y), new Point(source[^1].X, source[^1].Y)) > 1e-6)
            return Fail(name, "round join moved a pipe endpoint");
        if (rounded.Any(point => Distance(new Point(point.X, point.Y),
                new Point(source[1].X, source[1].Y)) < 1e-4))
            return Fail(name, "sharp miter vertex remains in the path");

        double turn = angleDegrees * Math.PI / 180.0;
        var incoming = new PerimeterSpiralCore.Point(1, 0);
        var outgoing = new PerimeterSpiralCore.Point(Math.Cos(turn), Math.Sin(turn));
        double tangent = radius * Math.Tan(turn / 2.0);
        var tangentIn = source[1] - incoming * tangent;
        var tangentOut = source[1] + outgoing * tangent;
        var center = tangentIn + new PerimeterSpiralCore.Point(0, radius);
        int startIndex = rounded.ToList().FindIndex(point =>
            Distance(new Point(point.X, point.Y), new Point(tangentIn.X, tangentIn.Y)) < 0.25);
        int endIndex = rounded.ToList().FindIndex(point =>
            Distance(new Point(point.X, point.Y), new Point(tangentOut.X, tangentOut.Y)) < 0.25);
        if (startIndex < 0 || endIndex <= startIndex)
            return Fail(name, "arc tangency points do not match the requested radius");
        for (int i = startIndex; i <= endIndex; i++)
        {
            double radialError = Math.Abs(Distance(new Point(rounded[i].X, rounded[i].Y),
                new Point(center.X, center.Y)) - radius);
            if (radialError > 0.25)
                return Fail(name, $"arc radius error is {radialError:F2} mm");
        }
        if (endIndex - startIndex < 2)
            return Fail(name, "arc is too coarse to appear round");
    }
    return true;
}

static bool CheckRoundedOffsetBevel()
{
    var rectangle = new List<PerimeterSpiralCore.Point>
    {
        new(0, 0), new(6000, 0), new(6000, 4000), new(0, 4000)
    };
    if (!PerimeterSpiralCore.TryOffsetContour(rectangle, 100, 30, out var offset))
        return Fail("rounded rectangle offset", "rounded offset failed or self-intersected");
    var expectedFilletCenter = new Point(5870, 130);
    if (!offset.Any(point => Math.Abs(Distance(new Point(point.X, point.Y), expectedFilletCenter) - 30) < 0.25 &&
                             point.X < 5900 && point.Y > 100))
        return Fail("rounded rectangle offset", "bottom-right offset corner has no radius-30 mm arc");

    var chamfer = Polygon((0, 0), (6200, 0), (6200, 2700), (5200, 3900), (0, 3900))
        .Select(point => new PerimeterSpiralCore.Point(point.X * MmPerFoot, point.Y * MmPerFoot)).ToList();
    if (!PerimeterSpiralCore.TryOffsetContour(chamfer, 100, 30, out var chamferOffset) ||
        chamferOffset.Count <= chamfer.Count)
        return Fail("rounded 45-degree offset", "bevel offset did not produce a valid round-joined contour");
    return true;
}

static bool CheckExpectedFailure(string name, IList<IList<XYZ>> loops, int connectorMode = 0)
{
    IList<XYZ> path = Build(loops, connectorMode);
    return path.Count == 0 || Fail(name, $"expected a clean no-route result, received {path.Count} points");
}

static IList<XYZ> Build(IList<IList<XYZ>> loops, int connectorMode = 0,
    double connectorGapMillimeters = 600, double connectorCenterOffsetMillimeters = 0)
{
    (XYZ start, XYZ startDirection, XYZ end, XYZ endDirection) = GetConnectors(loops, connectorMode,
        connectorGapMillimeters, connectorCenterOffsetMillimeters);
    return AreaPipeLayout.CreateSpiralPath(loops, Pitch, WallOffset, Diameter,
        0, -77, start, startDirection, end, endDirection);
}

static (XYZ Start, XYZ StartDirection, XYZ End, XYZ EndDirection) GetConnectors(
    IList<IList<XYZ>> loops, int mode, double connectorGapMillimeters = 600,
    double connectorCenterOffsetMillimeters = 0)
{
    double minX = loops.SelectMany(loop => loop).Min(point => point.X) * MmPerFoot;
    double maxX = loops.SelectMany(loop => loop).Max(point => point.X) * MmPerFoot;
    double minY = loops.SelectMany(loop => loop).Min(point => point.Y) * MmPerFoot;
    double maxY = loops.SelectMany(loop => loop).Max(point => point.Y) * MmPerFoot;
    double xCenter = (minX + maxX) / 2;
    double yCenter = (minY + maxY) / 2;
    double connectorCenterY = yCenter + connectorCenterOffsetMillimeters;
    return mode switch
    {
        1 => (FromMillimeters(minX - 600, yCenter), new XYZ(1, 0, 0),
            FromMillimeters(maxX + 600, yCenter), new XYZ(-1, 0, 0)),
        2 => (FromMillimeters(xCenter - connectorGapMillimeters / 2, maxY + 600), new XYZ(0, -1, 0),
            FromMillimeters(xCenter + connectorGapMillimeters / 2, maxY + 600), new XYZ(0, -1, 0)),
        3 => (FromMillimeters(maxX + 600, yCenter - connectorGapMillimeters / 2), new XYZ(-1, 0, 0),
            FromMillimeters(xCenter + connectorGapMillimeters / 2, maxY + 600), new XYZ(0, -1, 0)),
        _ => (FromMillimeters(maxX + 600, connectorCenterY - connectorGapMillimeters / 2), new XYZ(-1, 0, 0),
            FromMillimeters(maxX + 600, connectorCenterY + connectorGapMillimeters / 2), new XYZ(-1, 0, 0))
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
    double expectedAxis = expectedDegrees % 90.0;
    if (expectedAxis < 0) expectedAxis += 90.0;
    for (int i = 0; i + 1 < path.Count; i++)
    {
        double dx = path[i + 1].X - path[i].X, dy = path[i + 1].Y - path[i].Y;
        if (Math.Sqrt(dx * dx + dy * dy) < minLength) continue;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI % 90.0;
        if (angle < 0) angle += 90.0;
        double difference = Math.Abs(angle - expectedAxis);
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
    int minimumMeasuredWalls, int pathStart = 0, int pathEnd = -1)
{
    if (pathEnd < 0) pathEnd = path.Count - 1;
    pathStart = Math.Max(0, pathStart);
    pathEnd = Math.Min(path.Count - 1, pathEnd);
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
        for (int i = pathStart; i < pathEnd; i++)
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

        if (!double.IsFinite(nearest))
        {
            Console.WriteLine($"      wall not measured: loop {loopIndex}, edge {edge}, length {wallLength:F0}, range={pathStart}..{pathEnd}");
            continue;
        }
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
