using System;
using System.Collections.Generic;
using System.Drawing;

namespace Underfloor_heating_generator.Infrastructure.Geometry
{
    /// <summary>A continuous rectangular spiral with both leads on the right.</summary>
    public static class RectangularSpiral
    {
        internal const double MinimumPitchFactor = 0.88;
        internal const double MaximumPitchFactor = 1.03;
        private const double MinimumCenterLeg = 5.08;

        internal static int CountRoomTurns(double width, double height, double pitch)
        {
            double turns = 1 + Math.Floor(Math.Min(width - 5 * pitch,
                height - 3 * pitch - MinimumCenterLeg) / (4 * pitch));
            return turns >= 1 && turns <= 256 ? (int)turns : 0;
        }

        internal static double SelectRoomPitch(double width, double height, double nominalPitch, double diameter)
        {
            // Compare the space between the two innermost horizontal runs. Their
            // spacing changes when an extra pair of runs fits in the center.
            const double acceptableCenterRatio = 1.03;
            double bestPitch = nominalPitch;
            double bestRatio = CenterGapRatio(nominalPitch);
            if (bestRatio <= acceptableCenterRatio)
                return nominalPitch;

            const int samples = 150; // 0.1 percentage point per step.
            for (int i = 0; i <= samples; i++)
            {
                double factor = MinimumPitchFactor +
                    (MaximumPitchFactor - MinimumPitchFactor) * i / samples;
                double pitch = nominalPitch * factor;
                double ratio = CenterGapRatio(pitch);
                double cost = Math.Max(acceptableCenterRatio, ratio);
                double bestCost = Math.Max(acceptableCenterRatio, bestRatio);
                if (cost < bestCost - 1e-9 ||
                    Math.Abs(cost - bestCost) <= 1e-9 &&
                    Math.Abs(pitch - nominalPitch) < Math.Abs(bestPitch - nominalPitch))
                {
                    bestRatio = ratio;
                    bestPitch = pitch;
                }
            }
            return bestPitch;

            double CenterGapRatio(double pitch)
            {
                if (pitch <= diameter || pitch < MinimumCenterLeg)
                    return double.PositiveInfinity;
                int turns = CountRoomTurns(width, height, pitch);
                if (turns == 0)
                    return double.PositiveInfinity;

                double centerWidth = width - 4 * turns * pitch;
                double centerHeight = height - (4 * turns - 2) * pitch;
                double gap = CanFillCenter(centerWidth, centerHeight, pitch)
                    ? centerHeight - 2 * pitch
                    : centerHeight;
                return gap / pitch;
            }
        }

        private static bool CanFillCenter(double width, double height, double pitch)
        {
            return width >= 2 * pitch + 0.05 && height >= 3 * pitch + 0.05;
        }

        /// <param name="numTurns">Number of rectangular laps on the inward branch.
        /// The outward branch adds one interleaved lap for each inward lap.</param>
        /// <param name="traceWidth">Physical width of the trace.</param>
        /// <param name="gap">Clear space between neighboring traces.</param>
        /// <param name="innerWidth">Clear center width, measured between trace edges.</param>
        /// <param name="innerHeight">Clear center height, measured between trace edges. Must exceed the gap.</param>
        /// <param name="chamferSize">Horizontal and vertical trim of the first lead's
        /// 45-degree chamfer. Zero gives square leads. The second lead is a true parallel offset.</param>
        public static List<PointF> Generate(int numTurns, float traceWidth, float gap,
            float innerWidth, float innerHeight, float chamferSize)
        {
            return Generate(numTurns, traceWidth, gap, innerWidth, innerHeight, chamferSize, null);
        }

        internal static List<PointF> GenerateRoomSpiral(int numTurns, float traceWidth, float gap,
            float innerWidth, float innerHeight, double entryHeight)
        {
            return Generate(numTurns, traceWidth, gap, innerWidth, innerHeight, 0, entryHeight);
        }

        private static List<PointF> Generate(int numTurns, float traceWidth, float gap,
            float innerWidth, float innerHeight, float chamferSize, double? entryHeight)
        {
            if (numTurns < 1 || 8L * numTurns + 10 > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(numTurns));
            RequirePositive(traceWidth, nameof(traceWidth));
            RequirePositive(gap, nameof(gap));
            RequirePositive(innerWidth, nameof(innerWidth));
            RequirePositive(innerHeight, nameof(innerHeight));
            if (innerWidth < gap)
                throw new ArgumentOutOfRangeException(nameof(innerWidth), "Center width must be at least the gap.");
            if (innerHeight <= gap)
                throw new ArgumentOutOfRangeException(nameof(innerHeight), "Center height must exceed the gap.");
            if (!float.IsFinite(chamferSize) || chamferSize < 0)
                throw new ArgumentOutOfRangeException(nameof(chamferSize));

            double pitch = (double)traceWidth + gap;
            double lapStep = 2 * pitch;
            double lastInset = (numTurns - 1) * lapStep;
            double outerWidth = innerWidth + (double)traceWidth + 4 * pitch + 2 * lastInset;
            double outerHeight = innerHeight + (double)traceWidth + 2 * pitch + 2 * lastInset;
            double diagonalShift = (Math.Sqrt(2) - 1) * pitch;
            if (chamferSize > 0 && chamferSize >= outerHeight - pitch + diagonalShift)
                throw new ArgumentOutOfRangeException(nameof(chamferSize), "Chamfer must leave a vertical lead segment.");

            double leadHeight = entryHeight ?? outerHeight;
            double leadX = entryHeight.HasValue ? outerWidth : outerWidth + chamferSize + 2 * pitch;
            var inward = new List<PointF>();
            var outward = new List<PointF>(); // Built from the lead toward the center, then reversed.
            Add(inward, leadX, leadHeight);
            Add(outward, leadX, leadHeight + pitch);
            if (chamferSize > 0)
            {
                Add(inward, outerWidth + chamferSize, outerHeight);
                Add(inward, outerWidth, outerHeight - chamferSize);
                // Offset the entire lead by one pitch, including its diagonal.
                // An equal trim on both corners would shrink the gap along the diagonal.
                Add(outward, outerWidth + chamferSize - diagonalShift, outerHeight + pitch);
                Add(outward, outerWidth - pitch, outerHeight - chamferSize + diagonalShift);
            }
            else
            {
                if (!entryHeight.HasValue)
                    Add(inward, outerWidth, leadHeight);
                Add(outward, outerWidth - pitch, leadHeight + pitch);
            }

            for (int lap = 0; lap < numTurns; lap++)
            {
                double inset = lap * lapStep;
                double left = inset;
                double right = outerWidth - inset;
                double bottom = inset;
                double top = outerHeight - inset;
                Add(inward, right, bottom);
                Add(inward, left, bottom);
                Add(inward, left, top);
                Add(outward, right - pitch, bottom + pitch);
                Add(outward, left + pitch, bottom + pitch);
                Add(outward, left + pitch, top - pitch);

                if (lap == 0 && entryHeight.HasValue && top - leadHeight >= 4 * pitch + 5.0)
                {
                    // Follow the wall above the ports too. Turn across to the
                    // next lane through the gap above both parallel leads.
                    double bridgeHeight = leadHeight + lapStep;
                    Add(inward, right, top);
                    Add(inward, right, bridgeHeight);
                    Add(inward, right - lapStep, bridgeHeight);
                    Add(outward, right - pitch, top - pitch);
                    Add(outward, right - pitch, bridgeHeight + pitch);
                    Add(outward, right - lapStep - pitch, bridgeHeight + pitch);
                }
                else
                {
                    Add(inward, right - lapStep, top);
                    Add(outward, right - lapStep - pitch, top - pitch);
                }
            }

            double centerX = outerWidth - lastInset - lapStep;
            double centerY = lastInset + lapStep;
            Add(inward, centerX, centerY);
            if (entryHeight.HasValue && CanFillCenter(
                innerWidth + (double)traceWidth, innerHeight + (double)traceWidth, pitch))
            {
                // Use the remaining central strip if two more parallel runs fit.
                Add(inward, lastInset + lapStep, centerY);
                Add(outward, centerX - pitch, centerY + pitch);
                Add(outward, lastInset + lapStep, centerY + pitch);
            }
            else
            {
                Add(outward, centerX - pitch, centerY);
            }
            outward.Reverse();
            inward.AddRange(outward);
            return inward;
        }

        private static void RequirePositive(float value, string parameter)
        {
            if (!float.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(parameter, "Value must be finite and positive.");
        }

        private static void Add(List<PointF> path, double x, double y)
        {
            var point = new PointF((float)x, (float)y);
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || path.Count > 0 && path[^1] == point)
                throw new ArgumentException("Dimensions are outside the usable range of PointF.");
            path.Add(point);
        }
    }
}
