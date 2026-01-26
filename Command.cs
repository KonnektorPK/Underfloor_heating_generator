#region Namespaces
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;
using RevitView = Autodesk.Revit.DB.View;
#endregion


namespace Underfloor_heating_generator
{
    [Transaction(TransactionMode.Manual)]

    public class Command : IExternalCommand
    {
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;

            //MainViewForm form = new MainViewForm(
            //    new PipeTypeService(uidoc.Document),
            //    new UserFormParametrs(uidoc.Document),
            //    new DwgConverter(uidoc));

            FormDWGtoUnderfloor form = new FormDWGtoUnderfloor(
                new PipeTypeService(uidoc.Document),
                new UserFormParametrs(uidoc.Document),
                new DwgConverter(uidoc));

            form.ShowDialog();
            //form.Show();

            return Result.Succeeded;
        }
    }
}

public class PipeTypeService
{
    private readonly Document doc;
    private readonly IList<Element> TypePipe;
    private readonly IList<Element> SystemTypePipe;

    public PipeTypeService(Document _doc)
    {
        TypePipe = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_PipeCurves)
            .WhereElementIsElementType()
            .ToElements();
        SystemTypePipe = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_PipingSystem)
            .WhereElementIsElementType()
            .ToElements();
        doc = _doc;
    }

    public IList<Element> GetPipeTypes()
    {
        return TypePipe;
    }

    public Element GetPipeType(int Index)
    {
        return TypePipe[Index];
    }

    public IList<Element> GetSystemTypesPipe()
    {
        return SystemTypePipe;
    }

    public Element GetSystemTypePipe(int Index)
    {
        return SystemTypePipe[Index];
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
            catch (Exception ex)
            {
                transaction.RollBack();
                MessageBox.Show($"Ошибка транзакции: {ex.Message}",
                    "Обнаружена ошибка!",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
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

public class DwgConverter
{
    private readonly UIDocument uiDoc;
    private IList<Element> importDWGInstance;
    FilteredElementCollector collector;

    public DwgConverter(UIDocument _uiDoc)
    {
        uiDoc = _uiDoc;

        collector = new FilteredElementCollector(uiDoc.Document);
        //importDWGInstance = collector
        //    .OfClass(typeof(ImportInstance))
        //    .WhereElementIsNotElementType()
        //    .ToElements();

        //importDWGInstance = collector
        //    .OfClass(typeof(ImportInstance))
        //    .WhereElementIsNotElementType()
        //    .Cast<ImportInstance>()
        //    .ToList();
    }

    public IList<IList<XYZ>> GetListVerticesFromDwg(ImportInstance dwg, bool flagRounding = true)
    {
        double tolerance = 0.01;
        IList<IList<XYZ>> result = new List<IList<XYZ>>();
        IList<XYZ> currentPolylinePoints = new List<XYZ>();

        XYZ p0, p1, p2, p3;
        p0 = p1 = p2 = p3 = null;

        double minLen = 0.0254;

        GeometryElement geometryElement = dwg.get_Geometry(new Options());

        GeometryInstance geometryInstance = null;
        foreach (GeometryObject obj in geometryElement)
        {
            if (obj is GeometryInstance instance)
            {
                geometryInstance = instance;
                break;
            }
        }

        if (geometryInstance == null) return null;

        XYZ lastPoint = null;

        IList<NurbSpline> splines = geometryInstance.GetInstanceGeometry()
            .Where(g => g is NurbSpline)
            .Cast<NurbSpline>()
            .ToList();

        for (int i = 0; i < splines.Count; i++)
        {
            NurbSpline spline = splines[i];
            IList<XYZ> points = spline.CtrlPoints;

            if (flagRounding)
            {
                p1 = points.First();
                p2 = points.Last();

                Line chord = null;

                if (p1.DistanceTo(p2) > minLen)
                {
                    chord = Line.CreateBound(points.First(), points.Last());

                    XYZ mid = spline.Evaluate(0.5, true);
                    double dist = chord.Distance(mid);

                    if (dist > tolerance)
                    {
                        //currentPolylinePoints.Add(p1);
                        if(i != 0 && i != splines.Count)
                        {
                            if(splines[i - 1].CtrlPoints.First().DistanceTo(p1) > minLen &&
                            p2.DistanceTo(splines[i + 1].CtrlPoints.Last()) > minLen)
                            {
                                Line line1 = Line.CreateBound(splines[i - 1].CtrlPoints.First(), p1);
                                Line line2 = Line.CreateBound(p2, splines[i + 1].CtrlPoints.Last());

                                XYZ p = GetIntersection(line1, line2);

                                if (p != null) { 
                                    currentPolylinePoints.Add(p);
                                }
                            }
                        }

                        lastPoint = p2;
                        continue;
                    }
                }
            }

            XYZ firstPoint = points.First();

            if (lastPoint == null)
            {
                currentPolylinePoints = new List<XYZ>(points);
            }
            else if (firstPoint.DistanceTo(lastPoint) <= tolerance)
            {
                currentPolylinePoints = currentPolylinePoints.Concat(points.Skip(1)).ToList();
            }
            else
            {
                if (currentPolylinePoints.Count > 1)
                {
                    result.Add(new List<XYZ>(currentPolylinePoints));
                }
                currentPolylinePoints = new List<XYZ>(points);
            }

            lastPoint = points.Last();
        }

        if (currentPolylinePoints.Count > 1)
        {
            result.Add(new List<XYZ>(currentPolylinePoints));
        }

        return result;
    }

    public static XYZ GetIntersection(Line ray1, Line ray2)
    {
        Line unbound1 = Line.CreateUnbound(ray1.Origin, ray1.Direction);
        Line unbound2 = Line.CreateUnbound(ray2.Origin, ray2.Direction);

        IntersectionResultArray results;
        SetComparisonResult result = unbound1.Intersect(unbound2, out results);

        if (result == SetComparisonResult.Overlap && results != null && results.Size > 0)
        {
            return results.get_Item(0).XYZPoint;
        }

        return null;
    }

    public IList<XYZ> GetVerticesFromDwg(ImportInstance dwg)
    {

        IList<XYZ> result = new List<XYZ>();
        GeometryElement geometryElement = dwg.get_Geometry(new Options());

        GeometryInstance geometryInstance = null;
        foreach (GeometryObject obj in geometryElement)
        {
            if (obj is GeometryInstance instance)
            {
                geometryInstance = instance;
                break;
            }
        }

        foreach (var line in geometryInstance.GetInstanceGeometry())
        {
            if (line is NurbSpline spline)
            {
                IList<XYZ> points = spline.CtrlPoints;
                result = result.Concat(points).ToList();
            }
        }
        return result;
    }

    public ImportInstance PickDwgFile()
    {
        return this.importDWGInstance[0] as ImportInstance;
    }

    public ImportInstance GetDWGDrawings(int Index)
    {
        return this.importDWGInstance[Index] as ImportInstance;
    }

    public IList<Element> GetDWGDrawings(ElementId LevelID)
    {
        importDWGInstance = collector
            .OfClass(typeof(ImportInstance))
            .WhereElementIsNotElementType()
            .Cast<Element>()
            .Where(x => x.LevelId == LevelID)
            .ToList();

        return this.importDWGInstance;
    }
}

public class UserFormParametrs
{
    private readonly Document doc;
    public Element PipeType { get; private set; }
    public Element SystemTypePipe { get; private set; }
    public double PipeDiametr { get; private set; }
    public double PipeLayingHeight { get; private set; }
    public double DistanceFromWall { get; private set; }
    public double StepLaying { get; private set; }
    public double DistanceFromCollector { get; private set; }

    public UserFormParametrs(Document doc)
    {
        this.doc = doc;
    }

    public void SetParametrs(
        Element PipeType,
        Element SystemTypePipe,
        double pipeDiametr,
        double pipeLayingHeight,
        double distanceFromWall,
        double stepLaying,
        double distanceFromCollector)
    {
        this.PipeType = PipeType;
        this.SystemTypePipe = SystemTypePipe;
        this.PipeDiametr = pipeDiametr;
        this.PipeLayingHeight = pipeLayingHeight;
        this.DistanceFromWall = distanceFromWall;
        this.StepLaying = stepLaying;
        this.DistanceFromCollector = distanceFromCollector;
    }

    public ElementId GetLevelId()
    {
        Autodesk.Revit.DB.View activeView = doc.ActiveView;

        if (activeView.GenLevel != null)
        {
            return activeView.GenLevel.Id;
        }

        MessageBox.Show("Активный вид не связан с уровнем.\n" +
                       "Переключитесь на план этажа.",
                       "Ошибка уровня",
                       MessageBoxButtons.OK,
                       MessageBoxIcon.Warning);
        return null;
    }

    public void GetSaveParametrs()
    {

    }
}