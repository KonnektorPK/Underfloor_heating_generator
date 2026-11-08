// Minimal stand-in for the Revit XYZ value type. The production layout source is linked into
// this standalone project, so its geometry can be exercised without loading Revit assemblies.
namespace Autodesk.Revit.DB
{
    internal sealed class XYZ
    {
        public XYZ(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public double DistanceTo(XYZ other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            double dz = Z - other.Z;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
