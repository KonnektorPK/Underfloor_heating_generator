using System.Collections.Generic;
using Underfloor_heating_generator.Application.Models;

namespace Underfloor_heating_generator.Application.Contracts
{
    public interface IWarmFloorOptionsProvider
    {
        bool HasActiveLevel { get; }
        IReadOnlyList<ElementOption> GetPipeTypes();
        IReadOnlyList<ElementOption> GetSystemTypes();
        IReadOnlyList<ElementOption> GetDrawings();
    }
}
