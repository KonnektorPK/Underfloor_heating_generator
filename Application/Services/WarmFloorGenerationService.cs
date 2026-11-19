using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Plumbing;
using Underfloor_heating_generator.Application.Contracts;
using Underfloor_heating_generator.Application.Models;
using Underfloor_heating_generator.Infrastructure.Revit;

namespace Underfloor_heating_generator.Application.Services
{
    public sealed class WarmFloorGenerationService : IWarmFloorGenerator, IAreaWarmFloorGenerator
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

        public WorkflowResult GenerateOnArea(AreaWarmFloorRequest request)
        {
            if (request == null)
                return WorkflowResult.Failure("Параметры построения не заданы.");

            string limitError = request.Limits?.GetValidationError();
            if (request.Limits == null || limitError != null)
                return WorkflowResult.Failure(limitError ?? "Укажите ограничение по длине или по площади.");

            ElementId levelId = activeLevelProvider.GetActiveLevelId();
            if (levelId == null)
                return WorkflowResult.Failure("Активный вид не связан с уровнем. Переключитесь на план этажа.");

            Element area = document.GetElement(request.AreaId);
            Element pipeType = document.GetElement(request.PipeTypeId);
            Element systemType = document.GetElement(request.SystemTypeId);
            Pipe startPipe = document.GetElement(request.StartPipeId) as Pipe;
            if (area == null || pipeType == null || systemType == null)
                return WorkflowResult.Failure("Выбранная область или тип трубы больше недоступны.");
            if (startPipe == null)
                return WorkflowResult.Failure("Выберите трубу, которая задаёт сторону выводов.");
            if (request.PipeSpacingMillimeters <= request.PipeDiameterMillimeters ||
                request.PipeSpacingMillimeters <= 0 || request.WallOffsetMillimeters < 0 ||
                request.PipeDiameterMillimeters <= 0)
                return WorkflowResult.Failure("Проверьте диаметр, шаг и отступ: шаг должен быть больше диаметра.");

            if (!PipeDiameterMatches(startPipe, request.PipeDiameterMillimeters))
                return WorkflowResult.Failure("Диаметр выбранной трубы должен совпадать с диаметром тёплого пола.");
            if (!PipeSystemMatches(startPipe, systemType.Id))
                return WorkflowResult.Failure("Тип системы должен совпадать с системой выбранной трубы.");

            try
            {
                IList<IList<XYZ>> boundaryLoops = GetAreaBoundaryLoops(area);
                if (boundaryLoops.Count == 0)
                    return WorkflowResult.Failure("Не удалось получить замкнутые границы выбранной области.");

                Level level = document.GetElement(levelId) as Level;
                if (level == null)
                    return WorkflowResult.Failure("Не удалось определить уровень активного вида.");

                Connector startConnector = GetClosestFreeConnector(startPipe, boundaryLoops);
                if (startConnector == null)
                    return WorkflowResult.Failure("У выбранной трубы нет свободного конца.");

                IReadOnlyList<FloorCircuitLayout.Circuit> circuits = FloorCircuitLayout.Build(boundaryLoops,
                    startConnector.Origin, GetPipeDirectionTowardConnector(startPipe, startConnector),
                    request.PipeSpacingMillimeters, request.WallOffsetMillimeters,
                    request.PipeDiameterMillimeters, level.Elevation, request.LayingHeightMillimeters,
                    request.Limits);
                if (circuits.Count == 0)
                    return WorkflowResult.Failure("Не удалось уложить все контуры с раздельными выводами. Проверьте лимит, границы области и направление выбранной трубы.");

                using var group = new TransactionGroup(document, "Построение контуров тёплого пола");
                group.Start();
                try
                {
                    foreach (FloorCircuitLayout.Circuit circuit in circuits)
                    {
                        if (pipeTypeService.DrawPipesConnected(circuit.Points, request.PipeDiameterMillimeters,
                                request.LayingHeightMillimeters, pipeType.Id, levelId, systemType.Id,
                                circuit.ConnectsToSelectedPipe ? startPipe : null, null))
                            continue;
                        group.RollBack();
                        return WorkflowResult.Failure("Revit не смог создать один из контуров.");
                    }
                    if (group.Assimilate() != TransactionStatus.Committed)
                        return WorkflowResult.Failure("Revit не сохранил контуры.");
                }
                catch
                {
                    if (group.GetStatus() == TransactionStatus.Started)
                        group.RollBack();
                    throw;
                }

                double reserve = 2 * request.Limits.CollectorDistanceMeters.GetValueOrDefault();
                string largest = request.Limits.MaximumAreaSquareMeters.HasValue
                    ? $"{circuits.Max(circuit => circuit.AreaSquareMeters):0.##} м²"
                    : $"{circuits.Max(circuit => circuit.LengthMeters) + reserve:0.##} м";
                return WorkflowResult.Success($"Контуров построено: {circuits.Count}. Максимум на контур: {largest}.");
            }
            catch (Exception exception)
            {
                return WorkflowResult.Failure("Не удалось построить теплый пол: " + exception.Message);
            }
        }

        private static Connector GetClosestFreeConnector(Pipe pipe, IList<IList<XYZ>> boundaryLoops)
        {
            Connector best = null;
            double bestDistance = double.PositiveInfinity;
            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector.IsConnected)
                    continue;

                foreach (IList<XYZ> loop in boundaryLoops)
                {
                    for (int i = 0; i < loop.Count; i++)
                    {
                        XYZ a = loop[i];
                        XYZ b = loop[(i + 1) % loop.Count];
                        XYZ direction = b - a;
                        double lengthSquared = direction.X * direction.X + direction.Y * direction.Y;
                        double parameter = lengthSquared <= 1e-12 ? 0 :
                            ((connector.Origin.X - a.X) * direction.X +
                             (connector.Origin.Y - a.Y) * direction.Y) / lengthSquared;
                        parameter = Math.Max(0, Math.Min(1, parameter));
                        XYZ projection = new XYZ(a.X + direction.X * parameter,
                            a.Y + direction.Y * parameter, connector.Origin.Z);
                        double distance = connector.Origin.DistanceTo(projection);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = connector;
                        }
                    }
                }
            }
            return best;
        }

        private static XYZ GetPipeDirectionTowardConnector(Pipe pipe, Connector connector)
        {
            LocationCurve location = pipe.Location as LocationCurve;
            if (location?.Curve == null)
                return XYZ.BasisX;

            XYZ first = location.Curve.GetEndPoint(0);
            XYZ second = location.Curve.GetEndPoint(1);
            XYZ opposite = connector.Origin.DistanceTo(first) <= connector.Origin.DistanceTo(second)
                ? second
                : first;
            XYZ direction = connector.Origin - opposite;
            return direction.GetLength() > 1e-9 ? direction.Normalize() : XYZ.BasisX;
        }

        private static bool PipeDiameterMatches(Pipe pipe, double diameterMillimeters)
        {
            Parameter parameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            return parameter != null && Math.Abs(parameter.AsDouble() * 304.8 - diameterMillimeters) <= 1.0;
        }

        private static bool PipeSystemMatches(Pipe pipe, ElementId systemTypeId)
        {
            Parameter parameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId actualSystemTypeId = parameter?.AsElementId();
            if (actualSystemTypeId == null || actualSystemTypeId.Value == ElementId.InvalidElementId.Value)
                actualSystemTypeId = pipe.MEPSystem?.GetTypeId();
            return actualSystemTypeId != null && actualSystemTypeId.Value == systemTypeId.Value;
        }

        private static IList<IList<XYZ>> GetAreaBoundaryLoops(Element area)
        {
            var result = new List<IList<XYZ>>();
            if (area is Room room)
            {
                var options = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                IList<IList<BoundarySegment>> roomLoops = room.GetBoundarySegments(options);
                if (roomLoops == null)
                    return result;

                foreach (IList<BoundarySegment> roomLoop in roomLoops)
                {
                    var curves = roomLoop.Select(segment => segment.GetCurve());
                    AddCurveLoop(result, curves);
                }
            }
            else if (area is FilledRegion filledRegion)
            {
                foreach (CurveLoop curveLoop in filledRegion.GetBoundaries())
                    AddCurveLoop(result, curveLoop);
            }

            return result;
        }

        private static void AddCurveLoop(IList<IList<XYZ>> result, IEnumerable<Curve> curves)
        {
            var points = new List<XYZ>();
            foreach (Curve curve in curves)
            {
                IList<XYZ> tessellated = curve.Tessellate();
                foreach (XYZ point in tessellated)
                {
                    if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > 1e-8)
                        points.Add(point);
                }
            }

            if (points.Count > 2 && points[0].DistanceTo(points[points.Count - 1]) <= 1e-8)
                points.RemoveAt(points.Count - 1);
            if (points.Count >= 3)
                result.Add(points);
        }
    }
}
