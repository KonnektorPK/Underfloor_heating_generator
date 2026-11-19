using Autodesk.Revit.DB;

namespace Underfloor_heating_generator.Application.Models
{
    public sealed class AreaWarmFloorRequest
    {
        public AreaWarmFloorRequest(ElementId areaId, ElementId startPipeId,
            ElementId pipeTypeId, ElementId systemTypeId,
            double pipeDiameterMillimeters, double layingHeightMillimeters,
            double wallOffsetMillimeters, double pipeSpacingMillimeters, FloorCircuitLimits limits)
        {
            AreaId = areaId;
            StartPipeId = startPipeId;
            PipeTypeId = pipeTypeId;
            SystemTypeId = systemTypeId;
            PipeDiameterMillimeters = pipeDiameterMillimeters;
            LayingHeightMillimeters = layingHeightMillimeters;
            WallOffsetMillimeters = wallOffsetMillimeters;
            PipeSpacingMillimeters = pipeSpacingMillimeters;
            Limits = limits;
        }

        public ElementId AreaId { get; }
        public ElementId StartPipeId { get; }
        public ElementId PipeTypeId { get; }
        public ElementId SystemTypeId { get; }
        public double PipeDiameterMillimeters { get; }
        public double LayingHeightMillimeters { get; }
        public double WallOffsetMillimeters { get; }
        public double PipeSpacingMillimeters { get; }
        public FloorCircuitLimits Limits { get; }
    }
}
