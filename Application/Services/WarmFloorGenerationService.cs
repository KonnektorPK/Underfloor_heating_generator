using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Underfloor_heating_generator.Application.Contracts;
using Underfloor_heating_generator.Application.Models;

namespace Underfloor_heating_generator.Application.Services
{
    public sealed class WarmFloorGenerationService : IWarmFloorGenerator
    {
        private readonly Document document;
        private readonly IActiveLevelProvider activeLevelProvider;
        private readonly IDwgGeometryReader dwgConverter;
        private readonly IPipeLayoutService pipeTypeService;

        public WarmFloorGenerationService(Document document, IActiveLevelProvider activeLevelProvider,
            IDwgGeometryReader dwgConverter, IPipeLayoutService pipeTypeService)
        {
            this.document = document;
            this.activeLevelProvider = activeLevelProvider;
            this.dwgConverter = dwgConverter;
            this.pipeTypeService = pipeTypeService;
        }

        public WorkflowResult Generate(WarmFloorRequest request, Action<int, int> progress = null)
        {
            if (request == null)
                return WorkflowResult.Failure("Параметры построения не заданы.");

            ElementId levelId = activeLevelProvider.GetActiveLevelId();
            if (levelId == null)
                return WorkflowResult.Failure("Активный вид не связан с уровнем. Переключитесь на план этажа.");

            Element pipeType = document.GetElement(request.PipeTypeId);
            Element systemType = document.GetElement(request.SystemTypeId);
            ImportInstance drawing = dwgConverter.GetDrawing(request.DrawingId);
            if (pipeType == null || systemType == null || drawing == null)
                return WorkflowResult.Failure("Один из выбранных элементов больше недоступен. Обновите окно и повторите.");

            try
            {
                var splines = dwgConverter.GetListVerticesFromDwg(drawing, true,
                    request.AlignDirectionsToAllowedAngles);
                if (splines == null || splines.Count == 0)
                    return WorkflowResult.Failure("В выбранном DWG не найдены линии для построения труб.");

                progress?.Invoke(0, splines.Count);
                for (int i = 0; i < splines.Count; i++)
                {
                    IList<Autodesk.Revit.DB.XYZ> spline = splines[i];
                    if (!pipeTypeService.DrawPipes(spline, request.PipeDiameterMillimeters,
                        request.LayingHeightMillimeters, pipeType.Id, levelId, systemType.Id))
                    {
                        return WorkflowResult.Failure("Не удалось построить один или несколько контуров труб.");
                    }

                    progress?.Invoke(i + 1, splines.Count);
                }

                return WorkflowResult.Success();
            }
            catch (Exception exception)
            {
                return WorkflowResult.Failure("Не удалось построить теплый пол: " + exception.Message);
            }
        }
    }
}
