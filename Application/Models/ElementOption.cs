using Autodesk.Revit.DB;

namespace Underfloor_heating_generator.Application.Models
{
    public sealed class ElementOption
    {
        public ElementOption(string name, ElementId id)
        {
            Name = name;
            Id = id;
        }

        public string Name { get; }
        public ElementId Id { get; }

        public override string ToString() => Name;
    }
}
