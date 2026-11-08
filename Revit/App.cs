#region Namespaces
using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
#endregion

namespace Underfloor_heating_generator
{
    public sealed class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "Теплый пол";
            string panelName = "DWG в теплый пол";

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

            application.CreateRibbonTab(tabName);

            RibbonPanel panel = application.CreateRibbonPanel(tabName, panelName);
            PushButtonData button = new PushButtonData("DWGToWarmFloor", "DWG в теплый пол", thisAssemblyPath,
                "Underfloor_heating_generator.Revit.Commands.GenerateWarmFloorCommand");

            string iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(thisAssemblyPath), "WarmFloor.png");

            BitmapImage bitmapImage = new BitmapImage(new Uri(iconPath));
            button.LargeImage = bitmapImage;

            panel.AddItem(button);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
