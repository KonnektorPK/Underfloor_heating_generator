#region Namespaces
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Controls;
using System.Windows.Forms;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
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
                double levelFloorHight = ((doc.GetElement(levelId) as Level).Elevation + levelOffset / 304.8);
                double minLength = 0.0254;

                //points = points.Select(p => p / 304.8 + new XYZ(0, 0, levelFloorHight)).ToList();
                points = points.Select(p => p + new XYZ(0, 0, levelFloorHight)).ToList();

                Pipe previousPipe = null;
                Connector previousConnector = null;

                for (int i = 0; i < points.Count - 1; i++)
                {
                    int count = CountMissingPoints(points, i);
                    XYZ startPoint = points[i];

                    if (intermediatePoint == null)
                    {
                        startPoint = points[i];
                    }
                    else
                    {
                        startPoint = intermediatePoint;
                        intermediatePoint = null;
                    }

                    i += count;
                    XYZ endPoint = points[i + 1];

                    if (startPoint.DistanceTo(endPoint) < minLength)
                    {
                        intermediatePoint = startPoint;
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
        int result = 0;
        for (int i = count; i < points.Count - 2; i++)
        {
            if (ArePointsOnSameLine(points[i], points[i + 1], points[i + 2]))
            {
                result++;
            }
            else
            {
                return result;
            }
        }
        return result;
    }

    public static bool ArePointsOnSameLine(XYZ A, XYZ B, XYZ C, double tolerance = 1e-8)
    {
        XYZ AB = B - A;
        XYZ BC = C - B;

        XYZ normAB = AB.Normalize();
        XYZ normBC = BC.Normalize();

        double dot = Math.Abs(normAB.DotProduct(normBC));
        return Math.Abs(dot - 1.0) <= tolerance;
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