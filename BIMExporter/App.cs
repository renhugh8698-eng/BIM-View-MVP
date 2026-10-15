using Autodesk.Revit.UI;
using BIMExporter.Commands;

namespace BIMExporter
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            var panel = application.CreateRibbonPanel("BIM MVP");
            var data = new PushButtonData(
                "ExportBimPackage",
                "Export BIM",
                typeof(App).Assembly.Location,
                typeof(ExportModelCommand).FullName);

            panel.AddItem(data);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
