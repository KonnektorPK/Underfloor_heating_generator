using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Underfloor_heating_generator.Application.Contracts;
using Underfloor_heating_generator.Application.Models;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    public sealed class FloorAreaPicker : IFloorAreaPicker
    {
        private readonly UIDocument uiDocument;

        public FloorAreaPicker(UIDocument uiDocument)
        {
            this.uiDocument = uiDocument;
        }

        public ElementOption PickArea()
        {
            Reference reference = uiDocument.Selection.PickObject(ObjectType.Element,
                new FloorAreaSelectionFilter(),
                "Выберите помещение или цветовую область (Filled Region) для теплого пола.");

            Element element = uiDocument.Document.GetElement(reference);
            if (element == null)
                return null;

            string name = element.Name;
            if (element is Room room)
            {
                string number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
                string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                name = string.IsNullOrWhiteSpace(number) ? roomName : number + " — " + roomName;
            }
            else if (element is FilledRegion)
            {
                name = "Цветовая область — " + name;
            }

            return new ElementOption(name, element.Id);
        }

        public ElementOption PickPipe(string prompt)
        {
            Reference reference = uiDocument.Selection.PickObject(ObjectType.Element,
                new PipeSelectionFilter(), prompt);
            Pipe pipe = uiDocument.Document.GetElement(reference) as Pipe;
            return pipe == null ? null : new ElementOption(
                string.Format("Труба {0} — {1}", pipe.Id.Value, pipe.Name), pipe.Id);
        }

        private sealed class FloorAreaSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                element is FilledRegion || element is Room room && room.Area > 0;

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        private sealed class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Pipe;
            public bool AllowReference(Reference reference, XYZ position) => true;
        }
    }
}
