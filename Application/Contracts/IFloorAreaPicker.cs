using Underfloor_heating_generator.Application.Models;

namespace Underfloor_heating_generator.Application.Contracts
{
    public interface IFloorAreaPicker
    {
        ElementOption PickArea();
        ElementOption PickPipe(string prompt);
    }
}
