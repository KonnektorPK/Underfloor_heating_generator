using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Underfloor_heating_generator.Infrastructure.Revit;

internal static class PipeFittingPath
{
    // Measurements used to recognize a sampled arc, in Revit's internal feet.
    private const double PositionTolerance = 0.025 / 304.8;
    private const double AngularTolerance = 0.15 * Math.PI / 180;
    private const double MinimumAngle = 2 * Math.PI / 180;

    internal static List<XYZ> ToFittingVertices(IList<XYZ> source, out HashSet<XYZ> singleElbows)
    {
        singleElbows = new HashSet<XYZ>(ReferenceEqualityComparer.Instance);
        var vertices = new List<XYZ>(source.Count);
        for (int index = 0; index < source.Count; index++)
        {
            if (index > 0 && index + 3 < source.Count &&
                TryGetTangentCorner(source, index, out int arcEnd, out XYZ corner))
            {
                vertices.Add(corner);
                singleElbows.Add(corner);
                index = arcEnd;
            }
            else
                vertices.Add(source[index]);
        }
        return vertices;
    }

    private static bool TryGetTangentCorner(IList<XYZ> points, int startIndex, out int endIndex, out XYZ corner)
    {
        endIndex = startIndex;
        corner = null;
        XYZ start = points[startIndex];
        XYZ preceding = start - points[startIndex - 1];
        XYZ firstChord = points[startIndex + 1] - start;
        if (preceding.GetLength() <= PositionTolerance || firstChord.GetLength() <= PositionTolerance)
            return false;

        XYZ incoming = preceding.Normalize();
        XYZ chordDirection = firstChord.Normalize();
        XYZ cross = incoming.CrossProduct(chordDirection);
        double entryTurn = Math.Atan2(cross.GetLength(), incoming.DotProduct(chordDirection));
        if (entryTurn < AngularTolerance || entryTurn > Math.PI / 8 + AngularTolerance)
            return false;

        XYZ planeNormal = cross.Normalize();
        XYZ inward = planeNormal.CrossProduct(incoming);
        double radius = firstChord.DotProduct(firstChord) / (2 * inward.DotProduct(firstChord));
        XYZ center = start + inward * radius;
        XYZ startRadial = start - center;
        double previousSweep = 0;

        for (int index = startIndex + 1; index + 1 < points.Count; index++)
        {
            XYZ end = points[index];
            XYZ radial = end - center;
            if (Math.Abs(radial.DotProduct(planeNormal)) > PositionTolerance ||
                Math.Abs(radial.GetLength() - radius) > PositionTolerance)
                break;
            double sweep = Math.Atan2(planeNormal.DotProduct(startRadial.CrossProduct(radial)), startRadial.DotProduct(radial));
            if (sweep <= previousSweep + AngularTolerance || sweep - previousSweep > Math.PI / 4 + AngularTolerance)
                break;
            previousSweep = sweep;

            // At least two chords and a matching exit tangent distinguish a
            // sampled arc from an intentional straight chamfer.
            if (index - startIndex < 2 || sweep < MinimumAngle)
                continue;
            XYZ next = points[index + 1] - end;
            if (next.GetLength() <= PositionTolerance)
                break;
            XYZ outgoing = next.Normalize();
            XYZ tangent = planeNormal.CrossProduct(radial).Normalize();
            if (outgoing.DotProduct(tangent) < Math.Cos(AngularTolerance))
                continue;

            double denominator = planeNormal.DotProduct(incoming.CrossProduct(outgoing));
            if (denominator <= Math.Sin(AngularTolerance))
                break;
            double advance = planeNormal.DotProduct((end - start).CrossProduct(outgoing)) / denominator;
            XYZ intersection = start + incoming * advance;
            XYZ toEnd = end - intersection;
            if (advance <= PositionTolerance || toEnd.DotProduct(outgoing) <= PositionTolerance ||
                toEnd.CrossProduct(outgoing).GetLength() > PositionTolerance)
                break;

            endIndex = index;
            corner = intersection;
            return true;
        }
        return false;
    }
}
