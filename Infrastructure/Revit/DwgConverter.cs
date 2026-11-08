using System.Collections.Generic;
using System.Linq;
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

        public IList<IList<XYZ>> GetListVerticesFromDwg(ImportInstance dwg, bool flagRounding = true)
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

                        XYZ mid = spline.Evaluate(0.5, true);
                        double dist = chord.Distance(mid);

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

            return result;
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
