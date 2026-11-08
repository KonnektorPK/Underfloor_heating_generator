using System.Collections.Generic;
using System.Linq;
using System;
using Autodesk.Revit.DB;
using Underfloor_heating_generator.Application.Contracts;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    public sealed class DwgConverter : IDwgGeometryReader
    {
        private readonly Document document;

        public DwgConverter(Document document)
        {
            this.document = document;
        }

        public IList<IList<XYZ>> GetListVerticesFromDwg(ImportInstance dwg, bool flagRounding = true,
            bool alignDirectionsToAllowedAngles = false)
        {
            double tolerance = 0.01;
            IList<IList<XYZ>> result = new List<IList<XYZ>>();
            IList<XYZ> currentPolylinePoints = new List<XYZ>();

            XYZ p1, p2;

            double minLen = 0.0254;

            GeometryElement geometryElement = dwg.get_Geometry(new Options());

            GeometryInstance geometryInstance = null;
            foreach (GeometryObject obj in geometryElement)
            {
                if (obj is GeometryInstance instance)
                {
                    geometryInstance = instance;
                    break;
                }
            }

            if (geometryInstance == null) return null;

            XYZ lastPoint = null;

            IList<NurbSpline> splines = geometryInstance.GetInstanceGeometry()
                .Where(g => g is NurbSpline)
                .Cast<NurbSpline>()
                .ToList();

            for (int i = 0; i < splines.Count; i++)
            {
                NurbSpline spline = splines[i];
                IList<XYZ> points = spline.CtrlPoints;

                if (flagRounding)
                {
                    p1 = points.First();
                    p2 = points.Last();

                    Line chord = null;

                    if (p1.DistanceTo(p2) > minLen)
                    {
                        chord = Line.CreateBound(points.First(), points.Last());

                        double dist = GetMaximumDistanceFromChord(spline, chord);

                        if (dist > tolerance)
                        {
                            if (i > 0 && i < splines.Count - 1)
                            {
                                if (splines[i - 1].CtrlPoints.First().DistanceTo(p1) > minLen &&
                                    p2.DistanceTo(splines[i + 1].CtrlPoints.Last()) > minLen)
                                {
                                    Line line1 = Line.CreateBound(splines[i - 1].CtrlPoints.First(), p1);
                                    Line line2 = Line.CreateBound(p2, splines[i + 1].CtrlPoints.Last());

                                    XYZ p = GetIntersection(line1, line2);
                                    if (p != null)
                                        currentPolylinePoints.Add(p);
                                }
                            }

                            lastPoint = p2;
                            continue;
                        }
                    }
                }

                XYZ firstPoint = points.First();

                if (lastPoint == null)
                {
                    currentPolylinePoints = new List<XYZ>(points);
                }
                else if (firstPoint.DistanceTo(lastPoint) <= tolerance)
                {
                    currentPolylinePoints = currentPolylinePoints.Concat(points.Skip(1)).ToList();
                }
                else
                {
                    if (currentPolylinePoints.Count > 1)
                    {
                        result.Add(new List<XYZ>(currentPolylinePoints));
                    }
                    currentPolylinePoints = new List<XYZ>(points);
                }

                lastPoint = points.Last();
            }

            if (currentPolylinePoints.Count > 1)
            {
                result.Add(new List<XYZ>(currentPolylinePoints));
            }

            if (alignDirectionsToAllowedAngles)
            {
                XYZ xAxis = GetViewXAxis();
                XYZ yAxis = GetViewYAxis(xAxis);
                for (int i = 0; i < result.Count; i++)
                {
                    // Remove DWG control-point noise before angle snapping, so a single
                    // straight run does not alternate between nearby allowed directions.
                    result[i] = SimplifyPolyline(result[i], tolerance);
                    result[i] = SnapPolylineDirections(result[i], xAxis, yAxis);
                }
            }

            return result;
        }

        private XYZ GetViewXAxis()
        {
            XYZ direction = document.ActiveView?.RightDirection;
            if (direction == null)
                return XYZ.BasisX;

            XYZ projected = new XYZ(direction.X, direction.Y, 0);
            return projected.GetLength() > 1e-9 ? projected.Normalize() : XYZ.BasisX;
        }

        private XYZ GetViewYAxis(XYZ xAxis)
        {
            XYZ direction = document.ActiveView?.UpDirection;
            if (direction != null)
            {
                XYZ projected = new XYZ(direction.X, direction.Y, 0);
                if (projected.GetLength() > 1e-9)
                    return projected.Normalize();
            }

            return XYZ.BasisZ.CrossProduct(xAxis).Normalize();
        }

        private static double GetMaximumDistanceFromChord(NurbSpline spline, Line chord)
        {
            double maximumDistance = 0;
            double[] parameters = { 0.2, 0.4, 0.6, 0.8 };
            foreach (double parameter in parameters)
            {
                XYZ point = spline.Evaluate(parameter, true);
                maximumDistance = Math.Max(maximumDistance, chord.Distance(point));
            }

            return maximumDistance;
        }

        private static IList<XYZ> SimplifyPolyline(IList<XYZ> points, double tolerance)
        {
            if (points == null || points.Count < 3)
                return points;

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;

            var ranges = new Stack<(int Start, int End)>();
            ranges.Push((0, points.Count - 1));
            double toleranceSquared = tolerance * tolerance;

            while (ranges.Count > 0)
            {
                (int start, int end) = ranges.Pop();
                double maximumDistanceSquared = toleranceSquared;
                int farthestIndex = -1;

                for (int i = start + 1; i < end; i++)
                {
                    double distanceSquared = DistanceToSegmentSquared(points[i], points[start], points[end]);
                    if (distanceSquared > maximumDistanceSquared)
                    {
                        maximumDistanceSquared = distanceSquared;
                        farthestIndex = i;
                    }
                }

                if (farthestIndex >= 0)
                {
                    keep[farthestIndex] = true;
                    ranges.Push((start, farthestIndex));
                    ranges.Push((farthestIndex, end));
                }
            }

            var simplified = new List<XYZ>();
            for (int i = 0; i < points.Count; i++)
            {
                if (keep[i])
                    simplified.Add(points[i]);
            }

            return simplified;
        }

        private static double DistanceToSegmentSquared(XYZ point, XYZ start, XYZ end)
        {
            XYZ segment = end - start;
            double lengthSquared = segment.DotProduct(segment);
            if (lengthSquared <= 1e-18)
                return point.DistanceTo(start) * point.DistanceTo(start);

            double parameter = (point - start).DotProduct(segment) / lengthSquared;
            parameter = Math.Max(0, Math.Min(1, parameter));
            XYZ closestPoint = start + segment * parameter;
            double distance = point.DistanceTo(closestPoint);
            return distance * distance;
        }

        private static IList<XYZ> SnapPolylineDirections(IList<XYZ> points, XYZ xAxis, XYZ yAxis)
        {
            if (points == null || points.Count < 2)
                return points;

            var allowedAngles = new List<double>();
            // Use every 5-degree direction in the active view's XY plane.
            // Normalize and deduplicate through AddAngle to keep the snap set
            // consistent with the existing angular-distance logic.
            for (int angle = 0; angle < 360; angle += 5)
            {
                AddAngle(allowedAngles, angle);
            }

            var runs = new List<DirectionRun>();
            for (int i = 1; i < points.Count; i++)
            {
                XYZ originalDelta = points[i] - points[i - 1];
                double x = originalDelta.DotProduct(xAxis);
                double y = originalDelta.DotProduct(yAxis);
                if (x * x + y * y <= 1e-18)
                    continue;

                double angle = Math.Atan2(y, x) * 180 / Math.PI;
                double closest = allowedAngles
                    .OrderBy(candidate => AngularDistance(angle, candidate))
                    .First();

                if (runs.Count > 0 && AngularDistance(runs[runs.Count - 1].AngleDegrees, closest) < 1e-8)
                    runs[runs.Count - 1].EndPointIndex = i;
                else
                    runs.Add(new DirectionRun(i - 1, i, closest));
            }

            if (runs.Count == 0)
                return new List<XYZ>(points);

            foreach (DirectionRun run in runs)
                FitRunToSourcePoints(run, points, xAxis, yAxis);

            var snapped = new List<XYZ>(runs.Count + 1)
            {
                ProjectOntoRun(points[runs[0].StartPointIndex], runs[0], xAxis, yAxis)
            };

            for (int i = 1; i < runs.Count; i++)
            {
                int sourceIndex = runs[i].StartPointIndex;
                XYZ join = IntersectRuns(runs[i - 1], runs[i], points[sourceIndex], xAxis, yAxis);
                snapped.Add(join);
            }

            DirectionRun lastRun = runs[runs.Count - 1];
            snapped.Add(ProjectOntoRun(points[lastRun.EndPointIndex], lastRun, xAxis, yAxis));
            return snapped;
        }

        private sealed class DirectionRun
        {
            public DirectionRun(int startPointIndex, int endPointIndex, double angleDegrees)
            {
                StartPointIndex = startPointIndex;
                EndPointIndex = endPointIndex;
                AngleDegrees = angleDegrees;
            }

            public int StartPointIndex { get; }
            public int EndPointIndex { get; set; }
            public double AngleDegrees { get; }
            public double NormalOffset { get; set; }
        }

        private static void FitRunToSourcePoints(DirectionRun run, IList<XYZ> points, XYZ xAxis, XYZ yAxis)
        {
            double radians = run.AngleDegrees * Math.PI / 180;
            double normalX = -Math.Sin(radians);
            double normalY = Math.Cos(radians);
            double offsetSum = 0;
            int pointCount = 0;

            for (int i = run.StartPointIndex; i <= run.EndPointIndex; i++)
            {
                double x = points[i].DotProduct(xAxis);
                double y = points[i].DotProduct(yAxis);
                offsetSum += x * normalX + y * normalY;
                pointCount++;
            }

            run.NormalOffset = pointCount > 0 ? offsetSum / pointCount : 0;
        }

        private static XYZ ProjectOntoRun(XYZ point, DirectionRun run, XYZ xAxis, XYZ yAxis)
        {
            double radians = run.AngleDegrees * Math.PI / 180;
            double directionX = Math.Cos(radians);
            double directionY = Math.Sin(radians);
            double normalX = -directionY;
            double normalY = directionX;
            double x = point.DotProduct(xAxis);
            double y = point.DotProduct(yAxis);
            double along = x * directionX + y * directionY;
            double projectedX = along * directionX + run.NormalOffset * normalX;
            double projectedY = along * directionY + run.NormalOffset * normalY;
            return xAxis * projectedX + yAxis * projectedY + XYZ.BasisZ * point.Z;
        }

        private static XYZ IntersectRuns(DirectionRun first, DirectionRun second, XYZ sourceJoin,
            XYZ xAxis, XYZ yAxis)
        {
            double firstRadians = first.AngleDegrees * Math.PI / 180;
            double secondRadians = second.AngleDegrees * Math.PI / 180;
            double firstDx = Math.Cos(firstRadians);
            double firstDy = Math.Sin(firstRadians);
            double secondDx = Math.Cos(secondRadians);
            double secondDy = Math.Sin(secondRadians);
            double denominator = firstDx * secondDy - firstDy * secondDx;

            if (Math.Abs(denominator) < 1e-9)
                return (ProjectOntoRun(sourceJoin, first, xAxis, yAxis) +
                    ProjectOntoRun(sourceJoin, second, xAxis, yAxis)) / 2;

            double firstBaseX = -firstDy * first.NormalOffset;
            double firstBaseY = firstDx * first.NormalOffset;
            double secondBaseX = -secondDy * second.NormalOffset;
            double secondBaseY = secondDx * second.NormalOffset;
            double parameter = ((secondBaseX - firstBaseX) * secondDy -
                (secondBaseY - firstBaseY) * secondDx) / denominator;
            double intersectionX = firstBaseX + firstDx * parameter;
            double intersectionY = firstBaseY + firstDy * parameter;
            return xAxis * intersectionX + yAxis * intersectionY + XYZ.BasisZ * sourceJoin.Z;
        }

        private static void AddAngle(IList<double> angles, double angle)
        {
            double normalized = (angle % 360 + 360) % 360;
            if (!angles.Any(existing => Math.Abs(existing - normalized) < 1e-9))
                angles.Add(normalized);
        }

        private static double AngularDistance(double first, double second)
        {
            double difference = Math.Abs(first - second) % 360;
            return Math.Min(difference, 360 - difference);
        }

        public static XYZ GetIntersection(Line ray1, Line ray2)
        {
            Line unbound1 = Line.CreateUnbound(ray1.Origin, ray1.Direction);
            Line unbound2 = Line.CreateUnbound(ray2.Origin, ray2.Direction);

            IntersectionResultArray results;
            SetComparisonResult result = unbound1.Intersect(unbound2, out results);

            if (result == SetComparisonResult.Overlap && results != null && results.Size > 0)
            {
                return results.get_Item(0).XYZPoint;
            }

            return null;
        }

        public ImportInstance GetDrawing(ElementId drawingId)
        {
            return document.GetElement(drawingId) as ImportInstance;
        }
    }


}
