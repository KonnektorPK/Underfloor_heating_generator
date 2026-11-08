using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Underfloor_heating_generator.Application.Contracts;
using Underfloor_heating_generator.Application.Models;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    public sealed class RevitWarmFloorOptionsProvider : IWarmFloorOptionsProvider
    {
        private readonly Document document;
        private readonly IActiveLevelProvider activeLevelProvider;

        public RevitWarmFloorOptionsProvider(Document document, IActiveLevelProvider activeLevelProvider)
        {
            this.document = document;
            this.activeLevelProvider = activeLevelProvider;
        }

        public bool HasActiveLevel => activeLevelProvider.GetActiveLevelId() != null;

        public IReadOnlyList<ElementOption> GetPipeTypes() =>
            CollectTypes(BuiltInCategory.OST_PipeCurves);

        public IReadOnlyList<ElementOption> GetSystemTypes() =>
            CollectTypes(BuiltInCategory.OST_PipingSystem);

        public IReadOnlyList<ElementOption> GetDrawings()
        {
            ElementId levelId = activeLevelProvider.GetActiveLevelId();
            if (levelId == null) return new List<ElementOption>();

            return new FilteredElementCollector(document)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .Cast<ImportInstance>()
                .Where(drawing => drawing.LevelId == levelId)
                .Select(drawing => new ElementOption(
                    drawing.get_Parameter(BuiltInParameter.IMPORT_SYMBOL_NAME)?.AsString() ?? drawing.Name,
                    drawing.Id))
                .ToList();
        }

        private IReadOnlyList<ElementOption> CollectTypes(BuiltInCategory category) =>
            new FilteredElementCollector(document)
                .OfCategory(category)
                .WhereElementIsElementType()
                .ToElements()
                .Select(element => new ElementOption(element.Name, element.Id))
                .ToList();
    }
}
