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
            if (points == null || points.Count < 2) return false;
            XYZ intermediatePoint = null;

            using (Transaction transaction = new Transaction(doc, "Добавления труб с фитингами"))
            {
                transaction.Start();
                try
                {
                    //double levelFloorHight = ((doc.GetElement(levelId) as Level).Elevation + levelOffset / 304.8);
                    double levelFloorHight = (levelOffset / 304.8);
                    double minLength = 0.0254;

                    //points = points.Select(p => p / 304.8 + new XYZ(0, 0, levelFloorHight)).ToList();
                    points = points.Select(p => p + new XYZ(0, 0, levelFloorHight)).ToList();

                    Pipe previousPipe = null;
                    Connector previousConnector = null;
                    XYZ startPoint = null;

                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        int index = CountMissingPoints(points, i);
                        //MessageBox.Show(count.ToString());

                        if (intermediatePoint == null)
                        {
                            startPoint = points[i];
                        }
                        else
                        {
                            startPoint = intermediatePoint;
                            intermediatePoint = null;
                        }

                        XYZ endPoint = points[index];

                        if (startPoint.DistanceTo(endPoint) <= minLength)
                        {
                            if (intermediatePoint == null) intermediatePoint = startPoint;
                            continue;
                        }

                        Pipe nextPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId,
                            startPoint, endPoint);

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

                        previousConnector = ExtractPipeConnector(connectorSet2, endPoint);

                        i = index - 1;
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

        public static bool ArePointsOnSameLine(XYZ A, XYZ B, XYZ C, double tolerance = 0.05)
        {
            //MessageBox.Show($"{A.ToString()}\n{B.ToString()}\n{C.ToString()}");

            XYZ AB = B - A;
            XYZ BC = C - B;

            XYZ normAB = AB.Normalize();
            XYZ normBC = BC.Normalize();

            double dot = Math.Abs(normAB.DotProduct(normBC));
            return Math.Abs(dot - 1.0) <= tolerance;
        }

        public static double CosineAngleBetweenLines(Line line1, Line line2)
        {
            XYZ dir1 = (line1.GetEndPoint(1) - line1.GetEndPoint(0)).Normalize();
            XYZ dir2 = (line2.GetEndPoint(1) - line2.GetEndPoint(0)).Normalize();

            return Math.Abs(dir1.DotProduct(dir2));
        }
    }


}
