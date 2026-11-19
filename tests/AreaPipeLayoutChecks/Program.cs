using Autodesk.Revit.DB;
using System.Reflection;
using Underfloor_heating_generator.Application.Models;
using Underfloor_heating_generator.Infrastructure.Geometry;
using Underfloor_heating_generator.Infrastructure.Revit;

const double MmPerFoot = 304.8;
const double Pitch = 150;
const double WallOffset = 100;
const double Diameter = 16;
const double WallMeasurementTolerance = 0.02;

var checks = new List<(string Name, Func<bool> Run)>
{
    ("circuit limits require exactly one complete mode", CheckCircuitLimits),
    ("spatial buckets preserve exact coverage distances", CheckSegmentBuckets),
    ("sampled bends become one tangent elbow vertex", CheckSingleElbowVertices),
    ("real heating coils send the turns to Revit as single elbow vertices", CheckGeneratedElbows),
    ("screenshot with three wall recesses at 10 mm per pixel", () => CheckRecessedScreenshot(10)),
    ("screenshot with three wall recesses at 12 mm per pixel", () => CheckRecessedScreenshot(12)),
    ("screenshot with three wall recesses at 15 mm per pixel", () => CheckRecessedScreenshot(15)),
    ("screenshot with three wall recesses rotated 27 degrees", () => CheckRecessedScreenshot(12, 27)),
    ("long runs keep their pitch near a stepped wall", CheckSteppedWallPitch),
    ("one circuit uses the selected pipe and adds a return", () =>
        CheckCircuitPlan("single circuit", new FloorCircuitLimits(null, null, 50), 1)),
    ("area limit divides the floor into four circuits on the same side", () =>
        CheckCircuitPlan("area circuits", new FloorCircuitLimits(null, null, 12), 4)),
    ("exact circuit area limit works when the pipe is on a split line", () =>
        CheckCircuitPlan("area circuits at seam", new FloorCircuitLimits(null, null, 10.85), 4,
            startY: 3100)),
    ("circuit length limit includes collector distance in both directions", () =>
        CheckCircuitPlan("length circuits", new FloorCircuitLimits(12, 100, null), 4)),
    ("circuit length without a collector run needs fewer loops", () =>
        CheckCircuitPlan("length circuits without reserve", new FloorCircuitLimits(0, 100, null), 3)),
    ("rotated circuits with an opening use net area", () =>
        CheckCircuitPlan("rotated circuits", new FloorCircuitLimits(null, null, 24), 2, withHole: true)),
    ("circuits leave the wall without crossing an opening on the lead side", () =>
        CheckCircuitPlan("opening beside the leads", new FloorCircuitLimits(null, null, 12), 4,
            openingNearOutlet: true)),
    ("rotated circuits can route around two openings", () =>
        CheckCircuitPlan("two rotated openings", new FloorCircuitLimits(null, null, 12), 4,
            withHole: true, openingNearOutlet: true)),
    ("five circuits from the screenshot cover the full width and have separate 150 mm outlets", () =>
        CheckCircuitPlan("right-side outlet from screenshot", new FloorCircuitLimits(null, null, 17), 5,
            startY: 7750, width: 9886, height: 8330, right: true, connectorDistance: 1320,
            checkCoverage: true)),
    ("circuits of 20 mm have 150 mm outlets at a 200 mm laying pitch", () =>
        CheckCircuitPlan("20 mm tubes, 200 mm laying pitch", new FloorCircuitLimits(null, null, 12), 4,
            diameter: 20, pitch: 200)),
    ("circuits build when the selected pipe is just outside the wall", () =>
        CheckCircuitPlan("short selected pipe", new FloorCircuitLimits(null, null, 12), 4,
            startY: 3100, connectorDistance: 16)),
    ("circuit at the end of the pipe on the wall builds with a 10 m² limit", () =>
        CheckCircuitPlan("screenshot rectangle with one circuit", new FloorCircuitLimits(null, null, 10), 1,
            startY: 2150, width: 3200, height: 2700, right: true, connectorDistance: 0)),
    ("circuits split a rectangle with a pipe on the wall and a 10 m² limit", () =>
        CheckCircuitPlan("screenshot rectangle split into two circuits", new FloorCircuitLimits(null, null, 10), 2,
            startY: 2880, width: 4200, height: 3550, right: true, connectorDistance: 0)),
    ("circuits in a room with a chamfer have separate outlet pairs", () =>
        CheckCircuitPlan("chamfered room outlet", new FloorCircuitLimits(null, null, 12), 4,
            chamfer: true)),
    ("circuits in a recessed room have separate outlet pairs", () =>
        CheckCircuitPlan("recessed room outlet", new FloorCircuitLimits(null, null, 12), 3,
            recessed: true)),
    ("circuits with no room to turn fail without partial paths", () =>
        FloorCircuitLayout.Build(new[] { Rect(0, 0, 225, 800) }, FromMillimeters(-80, 400),
            new XYZ(1, 0, 0), Pitch, WallOffset, Diameter, 0, -77, new FloorCircuitLimits(null, null, 12))
            .Count == 0),
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
    ("prototype rejects narrow triangular cut with insufficient coverage", () => CheckPrototypeRejects("prototype notch",
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
        }, checkStraightOffsets: true, minimumStraightWallOffsets: 8, maximumCoverageFactor: 1.65)),
    ("octagonal room with an internal obstacle", () => CheckGenerated(
        "octagon with obstacle", new[]
        {
            Polygon((500, 0), (6500, 0), (7000, 500), (7000, 3900),
                (6500, 4400), (500, 4400), (0, 3900), (0, 500)),
            Rect(2500, 1600, 4500, 3000)
        }, checkStraightOffsets: true, minimumStraightWallOffsets: 12, maximumCoverageFactor: 1.65)),
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
        { Polygon((0, 0), (6500, 0), (6500, 2100), (2800, 2100), (2800, 5200), (0, 5200)) },
        checkStraightOffsets: true, minimumStraightWallOffsets: 6)),
    ("U-shaped room", () => CheckGenerated("U shape", new[]
        { Polygon((0, 0), (7000, 0), (7000, 5200), (5100, 5200), (5100, 1900),
            (1900, 1900), (1900, 5200), (0, 5200)) },
        checkStraightOffsets: true, minimumStraightWallOffsets: 8)),
    ("C-shaped room", () => CheckGenerated("C shape", new[]
        { Polygon((0, 0), (7000, 0), (7000, 1700), (2200, 1700), (2200, 4300),
            (7000, 4300), (7000, 6000), (0, 6000)) },
        checkStraightOffsets: true, minimumStraightWallOffsets: 8)),
    ("one internal rectangular obstacle", () => CheckGenerated("one hole", new[]
        { Rect(0, 0, 7000, 4800), Rect(2300, 1500, 4700, 3400) }, checkStraightOffsets: true)),
    ("reversed loop winding with an inner obstacle", () => CheckGenerated("reversed winding", new[]
        { Rect(0, 0, 7000, 4800).Reverse().ToList(), Rect(2300, 1500, 4700, 3400) })),
    ("multiple internal obstacles", () => CheckGenerated("multiple holes", new[]
        { Rect(0, 0, 9000, 5000), Rect(1800, 1500, 3000, 3400), Rect(5700, 900, 7600, 4100) },
        checkStraightOffsets: true, minimumStraightWallOffsets: 12, maximumCoverageFactor: 1.8)),
    ("chamfer and internal obstacle at a 65 mm setback", () => CheckGenerated(
        "chamfer with a 65 mm setback", new[]
        {
            Polygon((0, 0), (7000, 0), (7000, 2800), (5500, 4400), (0, 4400)),
            Rect(2300, 1500, 4700, 3200)
        }, checkStraightOffsets: true, minimumStraightWallOffsets: 9, maximumCoverageFactor: 1.65,
        connectorGapMillimeters: 200, wallOffsetMillimeters: 65)),
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

static bool CheckSingleElbowVertices()
{
    foreach (double degrees in new[] { 90.0, -90.0, 45.0, 135.0 })
    {
        double radians = degrees * Math.PI / 180.0;
        var sharp = new[] { new PerimeterSpiralCore.Point(-200, 0), new(0, 0),
            new(200 * Math.Cos(radians), 200 * Math.Sin(radians)) };
        if (!PerimeterSpiralCore.TryRoundPolyline(sharp, 30, 20, closed: false, out var rounded))
            return Fail("single elbow", $"could not sample bend {degrees}°");
        // Rotate into a nonhorizontal plane and offset its elevation.
        var input = rounded.Select(p => new XYZ(p.X / MmPerFoot, p.Y * 0.8 / MmPerFoot,
            5 + p.Y * 0.6 / MmPerFoot)).ToList();
        List<XYZ> output = PipeFittingPath.ToFittingVertices(input, out var elbows);
        if (output.Count != 3 || elbows.Count != 1 || !elbows.Contains(output[1]) ||
            !ReferenceEquals(output[0], input[0]) || !ReferenceEquals(output[^1], input[^1]) ||
            output[1].DistanceTo(new XYZ(0, 0, 5)) > 1e-8)
            return Fail("single elbow", $"sampled {degrees}° bend did not become the original tangent intersection");
    }

    var chamfer = Polygon((-250, 0), (-20, 0), (0, 20), (0, 250));
    var sharpPath = PipeFittingPath.ToFittingVertices(chamfer, out var roundedElbows);
    if (!sharpPath.SequenceEqual(chamfer) || roundedElbows.Count != 0)
        return Fail("single elbow", "an intentional chamfer changed");

    var square = new[] { new PerimeterSpiralCore.Point(-250, 0), new(0, 0), new(0, 140), new(250, 140) };
    if (!PerimeterSpiralCore.TryRoundPolyline(square, 28, 20, closed: false, out var twoTurns))
        return false;
    var combined = PipeFittingPath.ToFittingVertices(twoTurns.Select(p => FromMillimeters(p.X, p.Y)).ToList(), out var twoElbows);
    return combined.Count == 4 && twoElbows.Count == 2 &&
        combined[1].DistanceTo(FromMillimeters(0, 0)) < 1e-8 && combined[2].DistanceTo(FromMillimeters(0, 140)) < 1e-8;
}

static bool CheckGeneratedElbows()
{
    IList<XYZ> rounded = Build(new[] { Rect(0, 0, 7000, 4800), Rect(2300, 1500, 4700, 3400) }, 0);
    List<XYZ> vertices = PipeFittingPath.ToFittingVertices(rounded, out var elbows);
    if (elbows.Count < 20 || vertices.Count >= rounded.Count / 2 ||
        !ReferenceEquals(rounded[0], vertices[0]) || !ReferenceEquals(rounded[^1], vertices[^1]) ||
        vertices.Zip(vertices.Skip(1), (a, b) => a.DistanceTo(b)).Any(length => length * MmPerFoot < 2.54 - 1e-5))
        return Fail("production elbow vertices", $"sampled {rounded.Count}, fitting vertices {vertices.Count}, single elbows {elbows.Count}");
    Console.WriteLine($"      production coil: {rounded.Count} sampled vertices, {vertices.Count} fitting vertices, {elbows.Count} merged bends");
    return true;
}

static bool CheckSegmentBuckets()
{
    const double radius = 50;
    (Point Start, Point End)[] segments =
    {
        (new Point(-380, -220), new Point(250, -220)),
        (new Point(-150, -120), new Point(-150, 240)),
        (new Point(-900, 1400), new Point(1400, 2200)),
        (new Point(60, 125), new Point(60, 125))
    };
    var buckets = new SegmentBuckets<Point>(segments, point => (point.X, point.Y),
        PointSegmentDistanceSquared, radius);
    var samples = new List<Point> { new(-200, 50), new(100, 100), new(100, 3100), new(4000, 4000) };
    var random = new Random(8416);
    for (int i = 0; i < 300; i++)
        samples.Add(new Point(random.Next(-700, 2000), random.Next(-500, 2500)));

    foreach (Point point in samples)
    {
        double expected = segments.Min(segment => PointSegmentDistanceSquared(point, segment.Start, segment.End));
        if (buckets.NearestSquared(point) != expected ||
            (buckets.NearestSquared(point, requireExactDistance: false) <= radius * radius) != (expected <= radius * radius))
            return Fail("spatial buckets", $"invalid coverage distance at {point}");
    }
    return true;
}

static bool CheckCircuitLimits()
{
    FloorCircuitLimits[] invalid =
    {
        new(null, null, null), new(2, null, null), new(null, 70, null), new(2, 70, 12),
        new(20, 40, null), new(-1, 80, null), new(null, null, 0), new(null, null, double.NaN)
    };
    return invalid.All(limits => limits.GetValidationError() != null) &&
        new FloorCircuitLimits(0, 80, null).GetValidationError() == null &&
        new FloorCircuitLimits(null, null, 12).GetValidationError() == null;
}

static bool CheckSteppedWallPitch()
{
    const string name = "stepped wall pitch";
    foreach (double height in new[] { 3900.0, 4000.0, 4100.0 })
    {
        var outline = Polygon((0, 0), (3200, 0), (3200, 580), (4900, 580), (4900, 630), (6900, 630),
            (6900, height), (0, height));
        var plan = FloorCircuitLayout.Build(new[] { outline }, FromMillimeters(6900, 780),
            new XYZ(-1, 0, 0), Pitch, WallOffset, Diameter, 0, -77, new FloorCircuitLimits(null, null, 14));
        double area = (6900 * height - 1700 * 580 - 2000 * 630) / 1e6;
        if (plan.Count == 0 || plan.Any(circuit => circuit.AreaSquareMeters > 14.000001) ||
            Math.Abs(plan.Sum(circuit => circuit.AreaSquareMeters) - area) > 0.001)
            return Fail(name, $"missing or oversized circuit at height {height:F0} mm");
        var paths = plan.Select(circuit => circuit.Points.Select(ToMillimeters).ToList()).ToList();
        if (paths.Any(path => HasSelfCrossing(path) ||
            path.Zip(path.Skip(1), Distance).Any(length => length < 2.54 - 1e-5)))
            return Fail(name, $"invalid pipe geometry at height {height:F0} mm");
        double gap = paths.Min(path => MinimumLongRunPitch(path, Pitch));
        Console.WriteLine($"      {name} at height {height:F0} mm: {plan.Count} circuits, minimum pitch {gap:F1} mm");
        if (!double.IsFinite(gap) || gap < Pitch * 0.88 - 0.5)
            return Fail(name, $"long parallel runs are only {gap:F1} mm apart");
    }
    return true;
}

static double MinimumLongRunPitch(IList<Point> points, double pitch)
{
    double minimum = double.PositiveInfinity;
    for (int index = 0; index + 1 < points.Count; index++)
    {
        Point a = points[index], b = points[index + 1];
        bool horizontal = Math.Abs(a.Y - b.Y) < 0.01;
        if (!horizontal && Math.Abs(a.X - b.X) >= 0.01)
            continue;
        for (int other = index + 2; other + 1 < points.Count; other++)
        {
            Point c = points[other], d = points[other + 1];
            if ((horizontal ? Math.Abs(c.Y - d.Y) : Math.Abs(c.X - d.X)) >= 0.01)
                continue;
            double firstLow = horizontal ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
            double firstHigh = horizontal ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
            double secondLow = horizontal ? Math.Min(c.X, d.X) : Math.Min(c.Y, d.Y);
            double secondHigh = horizontal ? Math.Max(c.X, d.X) : Math.Max(c.Y, d.Y);
            if (Math.Min(firstHigh, secondHigh) - Math.Max(firstLow, secondLow) >= 3 * pitch)
                minimum = Math.Min(minimum, horizontal ? Math.Abs(a.Y - c.Y) : Math.Abs(a.X - c.X));
        }
    }
    return minimum;
}

static bool CheckRecessedScreenshot(double scale, double angle = 0)
{
    const string name = "screenshot with three wall recesses";
    var outline = Polygon(new (double X, double Y)[]
    {
        (0, 0), (181, 0), (181, 37), (263, 37), (263, 0), (421, 0),
        (421, 105), (386, 105), (386, 266), (421, 266), (421, 328),
        (204, 328), (204, 363), (0, 363)
    }.Select(p => (p.X * scale, p.Y * scale)).ToArray());
    XYZ start = FromMillimeters(421 * scale, 80 * scale);
    var direction = new XYZ(-1, 0, 0);
    IList<XYZ> layout = outline;
    if (angle != 0)
    {
        layout = Rotate(outline, angle);
        start = Rotate(new[] { start }, angle)[0];
        direction = Rotate(new[] { direction }, angle)[0];
    }
    var limits = new FloorCircuitLimits(null, null, 10);
    var plan = FloorCircuitLayout.Build(new[] { layout }, start, direction,
        Pitch, WallOffset, Diameter, 0, -77, limits);
    if (plan.Count == 0)
        return Fail(name, $"no route at scale {scale}");
    double requiredArea = (421 * 363 - 82 * 37 - 35 * 161 - 217 * 35) * scale * scale / 1e6;
    if (Math.Abs(plan.Sum(c => c.AreaSquareMeters) - requiredArea) > 0.001 ||
        plan.Any(c => c.AreaSquareMeters > 10.000001))
        return Fail(name, "area outside the limit or missing during subdivision");
    var anchor = plan.Where(c => c.ConnectsToSelectedPipe).ToList();
    if (anchor.Count != 1 || Distance(ToMillimeters(anchor[0].Points[0]), ToMillimeters(start)) > 0.01)
        return Fail(name, "connection to selected pipe changed");
    var segments = new List<(Point Start, Point End)>();
    foreach (var circuit in plan)
    {
        List<Point> points = (angle == 0 ? circuit.Points : Rotate(circuit.Points, -angle))
            .Select(ToMillimeters).ToList();
        if (Math.Abs(Math.Abs(points[^1].Y - points[0].Y) - 150) > 0.001 ||
            points[1].X >= points[0].X || points[^2].X >= points[^1].X ||
            Math.Abs(points[1].Y - points[0].Y) > 0.001 ||
            Math.Abs(points[^2].Y - points[^1].Y) > 0.001 || HasSelfCrossing(points) ||
            points.Zip(points.Skip(1), Distance).Any(length => length < 2.54 - 1e-5))
            return Fail(name, "incorrect outlet spacing, self-crossing or too-short pipe");
        var next = points.Zip(points.Skip(1), (a, b) => (Start: a, End: b)).ToList();
        foreach (var segment in next)
        foreach (var previous in segments)
            if (SegmentDistanceSquared(segment.Start, segment.End, previous.Start, previous.End) < Diameter * Diameter - 0.001)
                return Fail(name, "two circuits intersect or touch");
        segments.AddRange(next);
    }
    var boundary = new List<List<Point>> { outline.Select(ToMillimeters).ToList() };
    double clearance = WallOffset + Diameter / 2;
    double maxDistance = 0;
    Point worstPoint = default;
    for (double x = clearance; x < 421 * scale - clearance; x += 60)
    for (double y = clearance; y < 363 * scale - clearance; y += 60)
    {
        var sample = new Point(x, y);
        if (!InsideRegion(boundary, sample) || PointToLoopDistanceSquared(sample, boundary[0]) < clearance * clearance)
            continue;
        double distance = Math.Sqrt(segments.Min(s => PointSegmentDistanceSquared(sample, s.Start, s.End)));
        if (distance > maxDistance)
        {
            maxDistance = distance;
            worstPoint = sample;
        }
    }
    Console.WriteLine($"      {name} at scale {scale}, angle {angle}: {plan.Count} circuits, worst sampled gap {maxDistance:F1} mm at {worstPoint}");
    return maxDistance <= 0.85 * Pitch || Fail(name, $"unheated gap at scale {scale}: {maxDistance:F1} mm");
}

static bool CheckCircuitPlan(string name, FloorCircuitLimits limits, int expectedCount,
    bool withHole = false, double startY = 5200, double width = 7000, double height = 6200,
    bool right = false, double connectorDistance = 450, double diameter = Diameter,
    bool chamfer = false, bool recessed = false, bool openingNearOutlet = false, double pitch = Pitch,
    bool checkCoverage = false)
{
    XYZ start = FromMillimeters(right ? width + connectorDistance : -connectorDistance, startY);
    XYZ direction = right ? new XYZ(-1, 0, 0) : new XYZ(1, 0, 0);
    var loops = new List<IList<XYZ>> { Rect(0, 0, width, height) };
    double expectedArea = width * height / 1e6;
    if (chamfer)
    {
        loops[0] = Polygon((0, 0), (width, 0), (width, height - 1250), (width - 900, height), (0, height));
        expectedArea -= 0.5625;
    }
    if (recessed)
    {
        loops[0] = Polygon((0, 0), (width, 0), (width, 1700), (width - 3900, 1700),
            (width - 3900, 4300), (width, 4300), (width, height), (0, height));
        expectedArea -= 10.14;
    }
    if (openingNearOutlet)
    {
        loops.Add(Rect(350, 2200, 800, 2850));
        expectedArea -= 0.2925;
    }
    if (withHole)
    {
        loops.Add(Rect(2800, 2400, 3600, 2900));
        loops = loops.Select(loop => Rotate(loop, 23)).ToList();
        start = Rotate(new[] { start }, 23)[0];
        direction = Rotate(new[] { direction }, 23)[0];
        expectedArea -= 0.4;
    }

    var plan = FloorCircuitLayout.Build(loops, start, direction, pitch, WallOffset, diameter,
        0, -77, limits);
    if (plan.Count != expectedCount)
        return Fail(name, $"unexpected circuit count {plan.Count}");
    if (plan.Count(circuit => circuit.ConnectsToSelectedPipe) != 1 ||
        plan.Single(circuit => circuit.ConnectsToSelectedPipe).Points[0]
            .DistanceTo(new XYZ(start.X, start.Y, start.Z + 77 / MmPerFoot)) > 1e-7)
        return Fail(name, "selected endpoint was not preserved exactly");
    if (Math.Abs(plan.Sum(circuit => circuit.AreaSquareMeters) - expectedArea) > 0.0001)
        return Fail(name, "subdivision does not conserve heated area");
    double furthestWall = loops[0].Min(point => point.X * direction.X + point.Y * direction.Y);
    var outletSpans = new List<(double Min, double Max)>();
    for (int i = 0; i < plan.Count; i++)
    {
        var circuit = plan[i];
        if (limits.MaximumAreaSquareMeters is double area && circuit.AreaSquareMeters > area + 1e-6 ||
            limits.MaximumLengthMeters is double length &&
            circuit.LengthMeters + 2 * limits.CollectorDistanceMeters.GetValueOrDefault() > length + 1e-6)
            return Fail(name, "a circuit exceeds its limit");
        var outletOffsets = new List<double>();
        var outletPositions = new List<double>();
        foreach (var (port, next) in new[] { (circuit.Points[0], circuit.Points[1]),
            (circuit.Points[^1], circuit.Points[^2]) })
        {
            double along = port.X * direction.X + port.Y * direction.Y;
            double across = port.X * direction.Y - port.Y * direction.X;
            double sideError = (next.X - port.X) * direction.Y - (next.Y - port.Y) * direction.X;
            if (along > furthestWall + 1e-7 || Math.Abs(sideError) > 1e-7)
                return Fail(name, "a port is not pointing outward on the selected side");
            outletOffsets.Add(across * MmPerFoot);
            outletPositions.Add(along);
        }
        if (Math.Abs(outletOffsets.Max() - outletOffsets.Min() - 150) > 0.001 ||
            Math.Abs(outletPositions[0] - outletPositions[1]) * MmPerFoot > 0.001 ||
            Math.Abs(circuit.Points[0].Z - circuit.Points[^1].Z) * MmPerFoot > 0.001)
            return Fail(name, "the outlet pair is not 150 mm apart on one line");
        outletSpans.Add((outletOffsets.Min(), outletOffsets.Max()));
        List<Point> points = circuit.Points.Select(ToMillimeters).ToList();
        if (HasSelfCrossing(points))
            return Fail(name, "a circuit crosses itself");
        int wallCrossings = 0;
        for (int k = 0; k + 1 < circuit.Points.Count; k++)
        {
            XYZ a = circuit.Points[k], b = circuit.Points[k + 1];
            if (a.DistanceTo(b) * MmPerFoot < 2.54 - 1e-5)
                return Fail(name, "a pipe segment is too short to create");
            double fromWall = a.X * direction.X + a.Y * direction.Y - furthestWall;
            double toWall = b.X * direction.X + b.Y * direction.Y - furthestWall;
            bool aInside = fromWall > 1e-9, bInside = toWall > 1e-9;
            if (aInside != bInside)
                wallCrossings++;
        }
        if (wallCrossings != 2)
            return Fail(name, "a circuit has more or fewer than two wall crossings");
        foreach (IList<XYZ> loop in loops.Skip(1))
        {
            List<Point> hole = loop.Select(ToMillimeters).ToList();
            for (int k = 0; k + 1 < points.Count; k++)
                if (SegmentToLoopDistanceSquared(points[k], points[k + 1], hole) <
                    Math.Pow(WallOffset * 0.98 + diameter / 2 - WallMeasurementTolerance, 2))
                    return Fail(name, "a circuit violates the hole setback");
        }
        foreach (var previous in plan.Take(i))
        {
            List<Point> other = previous.Points.Select(ToMillimeters).ToList();
            for (int k = 0; k + 1 < points.Count; k++)
            for (int l = 0; l + 1 < other.Count; l++)
                if (SegmentDistanceSquared(points[k], points[k + 1], other[l], other[l + 1]) < diameter * diameter - 0.001)
                    return Fail(name, "two circuits intersect or touch");
        }
    }
    var sortedSpans = outletSpans.OrderBy(span => span.Min).ToList();
    if (sortedSpans.Zip(sortedSpans.Skip(1), (a, b) => b.Min - a.Max).Any(gap => gap < diameter))
        return Fail(name, "outlet pairs are interleaved or too close");
    if (checkCoverage && !CheckFullWidthCoverage(name, plan, width, height, pitch, diameter))
        return false;
    Console.WriteLine($"      {name}: {plan.Count} circuits, areas {string.Join(", ", plan.Select(c => c.AreaSquareMeters.ToString("F2")))} m², lengths {string.Join(", ", plan.Select(c => c.LengthMeters.ToString("F2")))} m");
    return true;
}

static bool CheckFullWidthCoverage(string name, IReadOnlyList<FloorCircuitLayout.Circuit> plan,
    double width, double height, double pitch, double diameter)
{
    var segments = plan.SelectMany(circuit => circuit.Points.Zip(circuit.Points.Skip(1),
        (a, b) => (Start: ToMillimeters(a), End: ToMillimeters(b)))).ToList();
    double inset = WallOffset + diameter / 2;
    double maximumDistance = 0;
    Point worst = default;
    int columns = (int)Math.Ceiling(width / (pitch / 2));
    int rows = (int)Math.Ceiling(height / (pitch / 2));
    for (int i = 0; i <= columns; i++)
    for (int j = 0; j <= rows; j++)
    {
        var sample = new Point(inset + (width - 2 * inset) * i / columns,
            inset + (height - 2 * inset) * j / rows);
        double distance = Math.Sqrt(segments.Min(segment =>
            PointSegmentDistanceSquared(sample, segment.Start, segment.End)));
        if (distance > maximumDistance)
        {
            maximumDistance = distance;
            worst = sample;
        }
    }
    Console.WriteLine($"      {name}: maximum distance to a pipe over the full width is {maximumDistance:F1} mm at {worst}");
    return maximumDistance <= pitch ||
        Fail(name, $"an unheated strip remains: {maximumDistance:F1} mm to a pipe");
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
    double connectorCenterOffsetMillimeters = 0, double wallOffsetMillimeters = WallOffset)
{
    IList<XYZ> path = Build(loops, connectorMode, connectorGapMillimeters, connectorCenterOffsetMillimeters,
        wallOffsetMillimeters);
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
    double clearance = wallOffsetMillimeters * 0.98 + Diameter / 2.0 - WallMeasurementTolerance;
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
                return Fail(name, $"route violates {wallOffsetMillimeters} mm hole setback near segment {i}");
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
            minimumStraightWallOffsets, wallOffsetMillimeters, bodyStart, bodyEnd))
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
    return longestOverlap >= 500 && Math.Abs(nearestSetback - expectedSetback) <= WallOffset * 0.02 + WallMeasurementTolerance
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
    return Math.Abs(wallGap - WallOffset) <= WallOffset * 0.02 + WallMeasurementTolerance
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
        if (!double.IsFinite(nearest) || Math.Abs(wallGap - WallOffset) > WallOffset * 0.02 + WallMeasurementTolerance)
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
    double connectorGapMillimeters = 600, double connectorCenterOffsetMillimeters = 0,
    double wallOffsetMillimeters = WallOffset)
{
    (XYZ start, XYZ startDirection, XYZ end, XYZ endDirection) = GetConnectors(loops, connectorMode,
        connectorGapMillimeters, connectorCenterOffsetMillimeters);
    return AreaPipeLayout.CreateSpiralPath(loops, Pitch, wallOffsetMillimeters, Diameter,
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
    int minimumMeasuredWalls, double wallOffsetMillimeters, int pathStart = 0, int pathEnd = -1)
{
    if (pathEnd < 0) pathEnd = path.Count - 1;
    pathStart = Math.Max(0, pathStart);
    pathEnd = Math.Min(path.Count - 1, pathEnd);
    double minAllowed = wallOffsetMillimeters * 0.98 + Diameter / 2.0 - WallMeasurementTolerance;
    double maxAllowed = wallOffsetMillimeters * 1.02 + Diameter / 2.0 + WallMeasurementTolerance;
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
        if (nearest < minAllowed || nearest > maxAllowed)
            return Fail(name, $"loop {loopIndex}, edge {edge} has a parallel run {nearest - Diameter / 2:F1} mm from wall; " +
                $"allowed surface gap is {wallOffsetMillimeters * 0.98:F2}–{wallOffsetMillimeters * 1.02:F2} mm; " +
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
