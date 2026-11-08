using Autodesk.Revit.DB;
using Underfloor_heating_generator.Application.Contracts;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    public sealed class ActiveLevelProvider : IActiveLevelProvider
    {
        private readonly Document document;

        public ActiveLevelProvider(Document document)
        {
            this.document = document;
        }

        public ElementId GetActiveLevelId() => document.ActiveView.GenLevel?.Id;
    }
}
