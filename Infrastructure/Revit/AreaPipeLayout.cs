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
            double angle = DominantAngle(boundaryLoops);
            var loops = boundaryLoops.Select(loop => CleanLoop(loop.Select(point => ToLocal(point, angle))))
                .Where(loop => loop.Count >= 3).ToList();
            if (loops.Count == 0)
                return new List<XYZ>();

            var start = ToLocal(startConnection, angle);
            var end = ToLocal(endConnection, angle);
            var startDir = Normalize(ToLocalVector(startDirection, angle));
            var endDir = Normalize(ToLocalVector(endDirection, angle));
            double clearance = edgeOffsetMillimeters + pipeDiameterMillimeters / 2.0;

            List<GridCell> cells = BuildCells(loops, spacingMillimeters, clearance);
            if (cells.Count == 0)
                return new List<XYZ>();

            GridCell entry = null;
            int entrySide = -1;
            double entryScore = double.PositiveInfinity;
            Point2 pipeMidpoint = (start + end) / 2.0;
            foreach (GridCell cell in cells)
            {
                for (int side = 0; side < 4; side++)
                {
                    Point2 normal = SideNormal(side);
                    Point2 midpoint = SideMidpoint(cell, side);
                    double edgeDepth = DistanceToBoundary(loops, midpoint);
                    double facing = Dot(pipeMidpoint - midpoint, normal);
                    if (edgeDepth > clearance + spacingMillimeters * 1.1 || facing <= 0)
                        continue;

                    double score = midpoint.DistanceSquaredTo(pipeMidpoint) - facing * 0.1;
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
            BuildSpanningTree(entry, loops, clearance, spacingMillimeters);
            HashSet<GridCell> component = GetTreeComponent(entry);
            if (component.Count != cells.Count)
                return new List<XYZ>();

            List<Point2> loopPath = TraceCellContour(entry, entrySide, component);
            if (loopPath == null || loopPath.Count < 2)
                return new List<XYZ>();

            List<Point2> bestPath = null;
            double bestScore = double.PositiveInfinity;
            for (int orientation = 0; orientation < 2; orientation++)
            {
                List<Point2> orientedLoop = new List<Point2>(loopPath);
                if (orientation == 1)
                    orientedLoop.Reverse();

                Point2 loopStart = orientedLoop[0];
                Point2 loopEnd = orientedLoop[orientedLoop.Count - 1];
                List<Point2> fromStart;
                List<Point2> toEnd;
                ConnectPair(loopStart, start, startDir, loopEnd, end, endDir,
                    SideNormal(entrySide), spacingMillimeters, out fromStart, out toEnd);
                var candidate = new List<Point2>();
                for (int i = fromStart.Count - 1; i >= 0; i--)
                    AddDistinct(candidate, fromStart[i]);
                foreach (Point2 point in orientedLoop)
                    AddDistinct(candidate, point);
                for (int i = 1; i < toEnd.Count; i++)
                    AddDistinct(candidate, toEnd[i]);

                int crossings = CountCrossings(candidate);
                double score = PolylineLength(fromStart) + PolylineLength(toEnd) +
                    1000000.0 * crossings + candidate.Count;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPath = candidate;
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

        private static List<GridCell> BuildCells(IList<List<Point2>> loops, double pitch, double clearance)
        {
            double minX = loops.SelectMany(loop => loop).Min(point => point.X);
            double maxX = loops.SelectMany(loop => loop).Max(point => point.X);
            double minY = loops.SelectMany(loop => loop).Min(point => point.Y);
            double maxY = loops.SelectMany(loop => loop).Max(point => point.Y);
            double availableWidth = maxX - minX - 2 * clearance;
            double availableHeight = maxY - minY - 2 * clearance;
            if (availableWidth < pitch || availableHeight < pitch)
                return new List<GridCell>();

            double gridStep = 2 * pitch;
            int columns = (int)Math.Floor((availableWidth + pitch) / gridStep);
            int rows = (int)Math.Floor((availableHeight + pitch) / gridStep);
            if ((long)columns * rows > MaximumCells)
                return new List<GridCell>();

            var result = new List<GridCell>();
            var byKey = new Dictionary<CellKey, GridCell>();
            double usedWidth = (columns - 1) * gridStep + pitch;
            double usedHeight = (rows - 1) * gridStep + pitch;
            double originX = minX + clearance + (availableWidth - usedWidth) / 2.0;
            double originY = minY + clearance + (availableHeight - usedHeight) / 2.0;

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    double x0 = originX + column * gridStep;
                    double y0 = originY + row * gridStep;
                    var cell = new GridCell(column, row, x0, y0, x0 + pitch, y0 + pitch);
                    if (!IsCellSafe(cell, loops, clearance))
                        continue;
                    cell.Id = result.Count;
                    result.Add(cell);
                    byKey.Add(new CellKey(column, row), cell);
                }
            }

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

        private static List<Point2> ConnectToPipe(Point2 endpoint, Point2 pipePoint, Point2 pipeDirection,
            Point2 outwardNormal, double clearance, double pitch, Point2 firstLoopPoint, Point2 lastLoopPoint)
        {
            Point2 lateral = new Point2(-outwardNormal.Y, outwardNormal.X);
            double depthStart = Dot(pipePoint - endpoint, outwardNormal);
            double lateralOffset = Dot(pipePoint - endpoint, lateral);
            bool parallel = Math.Abs(Dot(pipeDirection, outwardNormal)) > 0.7;

            double depthEnd = Dot(pipePoint - firstLoopPoint, outwardNormal);
            double depthReturn = Dot(pipePoint - lastLoopPoint, outwardNormal);
            double d = Math.Max(1.0, Math.Min(Math.Max(1.0, depthEnd), Math.Max(1.0, depthReturn)));
            double h1 = Math.Min(Math.Max(pitch, d / 3.0), d / 2.0);
            double h2 = Math.Min(h1 + pitch, d * 0.8);

            // Separate the two orthogonal leads on the side of the loop so they cannot cross.
            double routeOffset = Math.Abs(lateralOffset) < 15 ? 0 :
                (Dot(endpoint, lateral) > Dot(pipePoint, lateral) ? -h1 : h1);
            var points = new List<Point2> { endpoint };
            if (parallel)
            {
                if (Math.Abs(lateralOffset) >= 15)
                {
                    AddDistinct(points, endpoint + outwardNormal * h1);
                    AddDistinct(points, endpoint + outwardNormal * h1 + lateral * lateralOffset);
                }
            }
            else if (Math.Abs(depthStart) >= 1)
            {
                AddDistinct(points, endpoint + outwardNormal * depthStart);
            }
            AddDistinct(points, pipePoint + lateral * routeOffset);
            AddDistinct(points, pipePoint);
            return points;
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
            int steps = Math.Max(1, (int)Math.Ceiling(start.DistanceTo(end) / 25.0));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                Point2 point = start + (end - start) * t;
                if (!IsSafePoint(loops, point, clearance))
                    return false;
            }
            return true;
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

        private static void ConnectPair(Point2 startEndpoint, Point2 startPoint, Point2 startDirection,
            Point2 endEndpoint, Point2 endPoint, Point2 endDirection, Point2 outwardNormal,
            double pitch, out List<Point2> startRoute, out List<Point2> endRoute)
        {
            Point2 lateral = new Point2(-outwardNormal.Y, outwardNormal.X);
            double startDepth = Dot(startPoint - startEndpoint, outwardNormal);
            double endDepth = Dot(endPoint - endEndpoint, outwardNormal);
            bool startParallel = Math.Abs(Dot(startDirection, outwardNormal)) > 0.7;
            bool endParallel = Math.Abs(Dot(endDirection, outwardNormal)) > 0.7;
            double d = Math.Max(1.0, Math.Min(startDepth, endDepth));
            double h1 = Math.Min(Math.Max(pitch, d / 3.0), d / 2.0);
            double h2 = Math.Min(h1 + pitch, d * 0.8);
            double startLateral = Dot(startPoint - startEndpoint, lateral);
            double endLateral = Dot(endPoint - endEndpoint, lateral);
            double startH = h1;
            double endH = h1;
            if (startLateral * endLateral > 0)
            {
                double sign = startLateral > 0 ? 1 : -1;
                if (Dot(startEndpoint, lateral) * sign > Dot(endEndpoint, lateral) * sign)
                    endH = h2;
                else
                    startH = h2;
            }

            startRoute = CreatePipeRoute(startEndpoint, startPoint, startParallel,
                startDepth, startLateral, outwardNormal, lateral, startH);
            endRoute = CreatePipeRoute(endEndpoint, endPoint, endParallel,
                endDepth, endLateral, outwardNormal, lateral, endH);
        }

        private static List<Point2> CreatePipeRoute(Point2 endpoint, Point2 pipePoint,
            bool parallel, double depth, double lateralOffset, Point2 outwardNormal,
            Point2 lateral, double jog)
        {
            var path = new List<Point2> { endpoint };
            if (parallel)
            {
                if (Math.Abs(lateralOffset) >= 15)
                {
                    Point2 first = endpoint + outwardNormal * jog;
                    AddDistinct(path, first);
                    AddDistinct(path, first + lateral * lateralOffset);
                }
            }
            else if (Math.Abs(depth) >= 1)
                AddDistinct(path, endpoint + outwardNormal * depth);

            AddDistinct(path, pipePoint);
            return path;
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
