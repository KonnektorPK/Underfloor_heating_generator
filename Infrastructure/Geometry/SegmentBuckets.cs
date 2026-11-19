using System;
using System.Collections.Generic;
using System.Linq;

namespace Underfloor_heating_generator.Infrastructure.Geometry;

internal sealed class SegmentBuckets<T>
{
    private readonly (T Start, T End)[] segments;
    private readonly Func<T, (double X, double Y)> coordinates;
    private readonly Func<T, T, T, double> distanceSquared;
    private readonly double radiusSquared;
    private readonly double cellSize;
    private readonly Dictionary<(long X, long Y), List<int>> buckets = new();
    private readonly List<int> wideSegments = new();

    public SegmentBuckets(IEnumerable<(T Start, T End)> segments,
        Func<T, (double X, double Y)> coordinates, Func<T, T, T, double> distanceSquared, double radius)
    {
        this.segments = segments.ToArray();
        this.coordinates = coordinates;
        this.distanceSquared = distanceSquared;
        radiusSquared = radius * radius;
        cellSize = 2 * radius;
        for (int index = 0; index < this.segments.Length; index++)
        {
            var (start, end) = this.segments[index];
            var (x1, y1) = coordinates(start);
            var (x2, y2) = coordinates(end);
            long minX = Cell(Math.BitDecrement(Math.Min(x1, x2) - radius));
            long maxX = Cell(Math.BitIncrement(Math.Max(x1, x2) + radius));
            long minY = Cell(Math.BitDecrement(Math.Min(y1, y2) - radius));
            long maxY = Cell(Math.BitIncrement(Math.Max(y1, y2) + radius));

            // A long diagonal can span many empty cells. Check it directly.
            if (((double)maxX - minX + 1) * ((double)maxY - minY + 1) > 64)
            {
                wideSegments.Add(index);
                continue;
            }
            for (long x = minX; x <= maxX; x++)
            for (long y = minY; y <= maxY; y++)
            {
                var key = (x, y);
                if (!buckets.TryGetValue(key, out List<int> list))
                    buckets.Add(key, list = new List<int>());
                list.Add(index);
            }
        }
    }

    public double NearestSquared(T point, bool requireExactDistance = true)
    {
        var (x, y) = coordinates(point);
        double nearest = double.PositiveInfinity;
        if (buckets.TryGetValue((Cell(x), Cell(y)), out List<int> candidates))
            foreach (int index in candidates)
                nearest = Math.Min(nearest, DistanceSquared(point, index));
        foreach (int index in wideSegments)
            nearest = Math.Min(nearest, DistanceSquared(point, index));

        // Every segment within radius is indexed here. If all candidates are
        // farther away, an exact ranking must still consider skipped segments.
        if (requireExactDistance && nearest > radiusSquared)
            for (int index = 0; index < segments.Length; index++)
                nearest = Math.Min(nearest, DistanceSquared(point, index));
        return nearest;
    }

    private double DistanceSquared(T point, int index)
    {
        var (start, end) = segments[index];
        return distanceSquared(point, start, end);
    }

    private long Cell(double value) => (long)Math.Clamp(Math.Floor(value / cellSize), -4e18, 4e18);
}
