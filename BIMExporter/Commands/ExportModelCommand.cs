using System;
using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMExporter.Export;

namespace BIMExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active Revit document.";
                return Result.Failed;
            }

            if (!(uiDoc.Document.ActiveView is View3D) || uiDoc.Document.ActiveView.IsTemplate)
            {
                TaskDialog.Show("BIM Exporter", "Switch to a 3D view before exporting. The export follows this view's visibility and section box.");
                return Result.Cancelled;
            }

            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Export active 3D view: model.glb (self-contained) and optional model.json sidecar";
                dialog.ShowNewFolderButton = true;

                if (dialog.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;

                try
                {
                    var result = BimPackageExporter.Export(uiDoc.Document, dialog.SelectedPath);
                    TaskDialog.Show(
                        "BIM Exporter",
                        string.Format("Export completed (0.3.0). Open model.glb in BIMViewer.\n\nGLB: {0}\nOptional JSON: {1}\nElements: {2}\n\nMaterial shading colors are exported. Appearance textures are not included.",
                            result.GlbPath,
                            result.JsonPath,
                            result.ElementCount));
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                    File.WriteAllText(Path.Combine(dialog.SelectedPath, "bim-export-error.txt"), ex.ToString());
                    return Result.Failed;
                }
            }
        }
    }
}
