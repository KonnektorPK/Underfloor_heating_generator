using System;

namespace Underfloor_heating_generator.Application.Models
{
    public sealed record FloorCircuitLimits(double? CollectorDistanceMeters,
        double? MaximumLengthMeters, double? MaximumAreaSquareMeters)
    {
        public string GetValidationError()
        {
            bool hasLengthBlock = CollectorDistanceMeters.HasValue || MaximumLengthMeters.HasValue;
            if (MaximumAreaSquareMeters is double area)
                return !hasLengthBlock && double.IsFinite(area) && area > 0
                    ? null
                    : "Укажите положительную площадь или параметры расчёта по длине.";

            if (CollectorDistanceMeters is not double distance || MaximumLengthMeters is not double length ||
                !double.IsFinite(distance) || !double.IsFinite(length) || distance < 0 || length <= 0)
                return "Заполните расстояние от коллектора и максимальную длину либо максимальную площадь.";

            return length > 2 * distance ? null
                : "Максимальная длина контура должна быть больше удвоенного расстояния от коллектора.";
        }
    }
}
