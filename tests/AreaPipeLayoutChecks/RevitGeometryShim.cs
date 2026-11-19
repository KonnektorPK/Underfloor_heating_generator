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

        public static XYZ operator +(XYZ left, XYZ right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static XYZ operator -(XYZ left, XYZ right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static XYZ operator *(XYZ point, double factor) => new(point.X * factor, point.Y * factor, point.Z * factor);
        public double DotProduct(XYZ other) => X * other.X + Y * other.Y + Z * other.Z;
        public XYZ CrossProduct(XYZ other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
        public double GetLength() => System.Math.Sqrt(DotProduct(this));
        public XYZ Normalize() => this * (1 / GetLength());

        public double DistanceTo(XYZ other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            double dz = Z - other.Z;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
