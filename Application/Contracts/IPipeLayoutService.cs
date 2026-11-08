using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Underfloor_heating_generator.Application.Contracts
{
    public interface IPipeLayoutService
    {
        bool DrawPipes(IList<XYZ> points, double pipeDiameter, double levelOffset,
            ElementId pipeTypeId, ElementId levelId, ElementId systemTypeId, ElementId fittingTypeId = null);
    }
}
