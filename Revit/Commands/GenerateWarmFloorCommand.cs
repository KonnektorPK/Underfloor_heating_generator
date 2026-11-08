using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Underfloor_heating_generator.Application.Services;
using Underfloor_heating_generator.Infrastructure.Revit;
using Underfloor_heating_generator.UI.Views;

namespace Underfloor_heating_generator.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class GenerateWarmFloorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;
            Document document = uiDocument.Document;

            var activeLevelProvider = new ActiveLevelProvider(document);
            var optionsProvider = new RevitWarmFloorOptionsProvider(document, activeLevelProvider);
            var generator = new WarmFloorGenerationService(document, activeLevelProvider,
                new DwgConverter(document), new PipeTypeService(document));
            var window = new MainWindow(optionsProvider, generator);
            window.SetOwner(commandData.Application.MainWindowHandle);
            window.ShowDialog();

            return Result.Succeeded;
        }
    }
}
