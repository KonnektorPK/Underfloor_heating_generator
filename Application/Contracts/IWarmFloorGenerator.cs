using System;
using Underfloor_heating_generator.Application.Models;

namespace Underfloor_heating_generator.Application.Contracts
{
    public interface IWarmFloorGenerator
    {
        WorkflowResult Generate(WarmFloorRequest request, Action<int, int> progress = null);
    }
}
