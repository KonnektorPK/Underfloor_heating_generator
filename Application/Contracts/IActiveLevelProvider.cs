using Autodesk.Revit.DB;

namespace Underfloor_heating_generator.Application.Contracts
{
    public interface IActiveLevelProvider
    {
        ElementId GetActiveLevelId();
    }
}
