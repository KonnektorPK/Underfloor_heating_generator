using Underfloor_heating_generator.Application.Models;

namespace Underfloor_heating_generator.Application.Contracts
{
    public interface IAreaWarmFloorGenerator
    {
        WorkflowResult GenerateOnArea(AreaWarmFloorRequest request);
    }
}
