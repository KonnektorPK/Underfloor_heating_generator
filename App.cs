#region Namespaces
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Shapes;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
#endregion

namespace Underfloor_heating_generator
{
    class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "Теплый пол";
            string panelName = "DWG в теплый пол";

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

            application.CreateRibbonTab(tabName);

            RibbonPanel panel = application.CreateRibbonPanel(tabName, panelName);
            PushButtonData button = new PushButtonData("DWGToWarmFloor", "DWG в теплый пол", thisAssemblyPath, "Underfloor_heating_generator.Command");

            string iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(thisAssemblyPath), "icons/WarmFloor.png");

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