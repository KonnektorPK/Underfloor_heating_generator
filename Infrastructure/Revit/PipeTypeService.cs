using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Underfloor_heating_generator.Application.Contracts;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    public sealed class PipeTypeService : IPipeLayoutService
    {
        private readonly Document doc;

        public PipeTypeService(Document _doc)
        {
            doc = _doc;
        }

        public bool DrawPipes(
            IList<XYZ> points, //массив точек
            double pipeDiameter, //внешний диаметр трубы
            double levelOffset, //отступ от уровня для теплого пола
            ElementId pipeTypeId, //тип трубы
            ElementId levelId, //уровень
            ElementId systemTypeId, //тип системы трубы
            ElementId fittingTypeId = null) //тип соединительной детали
        {
            return DrawPipes(points, pipeDiameter, levelOffset, pipeTypeId, levelId,
                systemTypeId, null, null);
        }

        public bool DrawPipesConnected(IList<XYZ> points, double pipeDiameter, double levelOffset,
            ElementId pipeTypeId, ElementId levelId, ElementId systemTypeId,
            Pipe startPipe, Pipe endPipe)
        {
            return DrawPipes(points, pipeDiameter, levelOffset, pipeTypeId, levelId,
                systemTypeId, startPipe, endPipe);
        }

        private bool DrawPipes(IList<XYZ> points, double pipeDiameter, double levelOffset,
            ElementId pipeTypeId, ElementId levelId, ElementId systemTypeId,
            Pipe startPipe, Pipe endPipe)
        {
            if (points == null || points.Count < 2) return false;
            double minimumPipeLength = Math.Max(doc.Application.ShortCurveTolerance,
                (0.1 * 25.4) / 304.8);
            double levelFloorHight = levelOffset / 304.8;
            points = RemoveShortSegments(points.Select(point =>
                point + new XYZ(0, 0, levelFloorHight)).ToList(), minimumPipeLength);
            if (points.Count < 2)
                return false;

            using (Transaction transaction = new Transaction(doc, "Добавления труб с фитингами"))
            {
                transaction.Start();
                try
                {
                    //double levelFloorHight = ((doc.GetElement(levelId) as Level).Elevation + levelOffset / 304.8);
                    Pipe previousPipe = null;
                    Connector previousConnector = null;
                    Pipe firstPipe = null;
                    Pipe lastPipe = null;
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        int index = CountMissingPoints(points, i);
                        XYZ startPoint = points[i];
                        XYZ endPoint = points[index];

                        if (startPoint.DistanceTo(endPoint) <= minimumPipeLength)
                            throw new InvalidOperationException("После очистки в трассе остался слишком короткий участок трубы.");

                        Pipe nextPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId,
                            startPoint, endPoint);
                        if (firstPipe == null)
                            firstPipe = nextPipe;

                        Parameter diameterParam = nextPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        diameterParam.Set(pipeDiameter / 304.8);

                        ConnectorSet connectorSet2 = nextPipe.ConnectorManager.Connectors;
                        Connector startConnector2 = ExtractPipeConnector(connectorSet2, startPoint);

                        if (previousConnector != null && startConnector2 != null)
                        {
                            try
                            {
                                doc.Create.NewElbowFitting(previousConnector, startConnector2);
                            }
                            catch
                            {
                                previousConnector.ConnectTo(startConnector2);
                            }
                        }

                        previousPipe = nextPipe;
                        lastPipe = nextPipe;

                        previousConnector = ExtractPipeConnector(connectorSet2, endPoint);

                        i = index - 1;
                    }

                    if (firstPipe == null || lastPipe == null)
                        throw new InvalidOperationException("Трасса не содержит отрезков достаточной длины.");

                    if (startPipe != null)
                    {
                        Connector source = FindFreeConnector(startPipe, points[0]);
                        Connector generated = ExtractPipeConnector(firstPipe.ConnectorManager.Connectors, points[0]);
                        ConnectExternalConnectors(source, generated);
                    }
                    if (endPipe != null)
                    {
                        XYZ endPoint = points[points.Count - 1];
                        Connector target = FindFreeConnector(endPipe, endPoint);
                        Connector generated = ExtractPipeConnector(lastPipe.ConnectorManager.Connectors, endPoint);
                        ConnectExternalConnectors(generated, target);
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.RollBack();
                    throw;
                }
            }

            return true;
        }

        private static IList<XYZ> RemoveShortSegments(IList<XYZ> source, double minimumLength)
        {
            var points = source.ToList();
            int index = 0;
            while (index < points.Count - 1)
            {
                if (points[index].DistanceTo(points[index + 1]) > minimumLength)
                {
                    index++;
                    continue;
                }

                if (index == 0)
                    points.RemoveAt(1); // keep the selected start connector exact
                else if (index + 1 == points.Count - 1)
                    points.RemoveAt(index); // keep the selected end connector exact
                else
                {
                    double removeStartError = DistanceToSegment(points[index],
                        points[index - 1], points[index + 1]);
                    double removeEndError = DistanceToSegment(points[index + 1],
                        points[index], points[index + 2]);
                    points.RemoveAt(removeStartError <= removeEndError ? index : index + 1);
                }

                index = Math.Max(0, index - 1);
            }
            return points;
        }

        private static double DistanceToSegment(XYZ point, XYZ start, XYZ end)
        {
            XYZ vector = end - start;
            double lengthSquared = vector.DotProduct(vector);
            if (lengthSquared <= 1e-18)
                return point.DistanceTo(start);
            double parameter = (point - start).DotProduct(vector) / lengthSquared;
            parameter = Math.Max(0, Math.Min(1, parameter));
            return point.DistanceTo(start + vector * parameter);
        }

        private static Connector FindFreeConnector(Pipe pipe, XYZ point)
        {
            Connector best = null;
            double bestDistance = double.PositiveInfinity;
            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector.IsConnected)
                    continue;
                double distance = connector.Origin.DistanceTo(point);
                if (distance < bestDistance)
                {
                    best = connector;
                    bestDistance = distance;
                }
            }

            if (best == null || bestDistance > 0.01)
                throw new InvalidOperationException("У выбранной трубы нет свободного конца в точке подключения трассы.");
            return best;
        }

        private void ConnectExternalConnectors(Connector first, Connector second)
        {
            if (first == null || second == null)
                throw new InvalidOperationException("Не удалось определить соединители трубы.");
            if (first.Origin.DistanceTo(second.Origin) > 0.01)
                throw new InvalidOperationException("Конец трассы не совпадает с соединителем выбранной трубы.");

            try
            {
                doc.Create.NewUnionFitting(first, second);
            }
            catch
            {
                try
                {
                    doc.Create.NewElbowFitting(first, second);
                }
                catch
                {
                    first.ConnectTo(second);
                }
            }
        }

        public static Connector ExtractPipeConnector(ConnectorSet connectors, XYZ point)
        {
            foreach (Connector connector in connectors)
            {
                if (connector.Origin.IsAlmostEqualTo(point))
                {
                    return connector;
                }
            }

            return null;
        }

        public static int CountMissingPoints(IList<XYZ> points, int count)
        {
            for (int i = count; i < points.Count - 2; i++)
            {
                if (!ArePointsOnSameLine(points[i], points[i + 1], points[i + 2]))
                {
                    return i + 1;
                }
            }
            return points.Count - 1;
        }

        public static bool ArePointsOnSameLine(XYZ A, XYZ B, XYZ C, double tolerance = 1e-6)
        {
            XYZ AB = B - A;
            XYZ BC = C - B;

            if (AB.GetLength() <= 1e-9 || BC.GetLength() <= 1e-9)
                return false;

            XYZ normAB = AB.Normalize();
            XYZ normBC = BC.Normalize();

            // Only combine forward, nearly identical directions. The previous
            // absolute-dot check also merged reversals, and its 0.05 threshold
            // allowed turns of almost 18 degrees to become one chord.
            double dot = normAB.DotProduct(normBC);
            return dot > 0 && 1.0 - dot <= tolerance;
        }

        public static double CosineAngleBetweenLines(Line line1, Line line2)
        {
            XYZ dir1 = (line1.GetEndPoint(1) - line1.GetEndPoint(0)).Normalize();
            XYZ dir2 = (line2.GetEndPoint(1) - line2.GetEndPoint(0)).Normalize();

            return Math.Abs(dir1.DotProduct(dir2));
        }
    }


}
