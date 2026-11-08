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
                            //currentPolylinePoints.Add(p1);
                            if (i > 0 && i < splines.Count - 1)
                            {
                                if(splines[i - 1].CtrlPoints.First().DistanceTo(p1) > minLen &&
                                p2.DistanceTo(splines[i + 1].CtrlPoints.Last()) > minLen)
                                {
                                    Line line1 = Line.CreateBound(splines[i - 1].CtrlPoints.First(), p1);
                                    Line line2 = Line.CreateBound(p2, splines[i + 1].CtrlPoints.Last());

                                    XYZ p = GetIntersection(line1, line2);

                                    if (p != null) { 
                                        currentPolylinePoints.Add(p);
                                    }
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

            double[] baseAngles = { 0, 22.5, 45, 67.5, 90, 30, 60 };
            var allowedAngles = new List<double>();
            foreach (double angle in baseAngles)
            {
                AddAngle(allowedAngles, angle);
                AddAngle(allowedAngles, 180 - angle);
                AddAngle(allowedAngles, angle + 180);
                AddAngle(allowedAngles, 360 - angle);
            }

            var snapped = new List<XYZ>(points.Count) { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                XYZ originalDelta = points[i] - points[i - 1];
                double x = originalDelta.DotProduct(xAxis);
                double y = originalDelta.DotProduct(yAxis);
                double planarLength = Math.Sqrt(x * x + y * y);
                if (planarLength <= 1e-9)
                {
                    snapped.Add(snapped[i - 1] + originalDelta);
                    continue;
                }

                double angle = Math.Atan2(y, x) * 180 / Math.PI;
                double closest = allowedAngles
                    .OrderBy(candidate => AngularDistance(angle, candidate))
                    .First();
                double radians = closest * Math.PI / 180;
                XYZ direction = xAxis * Math.Cos(radians) + yAxis * Math.Sin(radians);
                double verticalDelta = originalDelta.Z;
                snapped.Add(snapped[i - 1] + direction * planarLength + XYZ.BasisZ * verticalDelta);
            }

            return snapped;
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
