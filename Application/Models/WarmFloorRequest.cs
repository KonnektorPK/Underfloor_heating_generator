using Autodesk.Revit.DB;

namespace Underfloor_heating_generator.Application.Models
{
    public sealed class WarmFloorRequest
    {
        public WarmFloorRequest(ElementId pipeTypeId, ElementId systemTypeId, ElementId drawingId,
            double pipeDiameterMillimeters, double layingHeightMillimeters,
            bool alignDirectionsToAllowedAngles)
        {
            PipeTypeId = pipeTypeId;
            SystemTypeId = systemTypeId;
            DrawingId = drawingId;
            PipeDiameterMillimeters = pipeDiameterMillimeters;
            LayingHeightMillimeters = layingHeightMillimeters;
            AlignDirectionsToAllowedAngles = alignDirectionsToAllowedAngles;
        }

        public ElementId PipeTypeId { get; }
        public ElementId SystemTypeId { get; }
        public ElementId DrawingId { get; }
        public double PipeDiameterMillimeters { get; }
        public double LayingHeightMillimeters { get; }
        public bool AlignDirectionsToAllowedAngles { get; }
    }
}
