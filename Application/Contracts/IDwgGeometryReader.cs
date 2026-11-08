using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Underfloor_heating_generator.Application.Contracts
{
    public interface IDwgGeometryReader
    {
        ImportInstance GetDrawing(ElementId drawingId);
        IList<IList<XYZ>> GetListVerticesFromDwg(ImportInstance drawing, bool flagRounding = true);
    }
}
