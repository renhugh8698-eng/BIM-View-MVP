using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;

namespace BIMExporter.Export
{
    public static class BimPackageExporter
    {
        public static ExportResult Export(Document document, string outputFolder)
        {
            var view = document.ActiveView as View3D;
            if (view == null || view.IsTemplate)
                throw new InvalidOperationException("Switch to a non-template 3D view before exporting.");
            Directory.CreateDirectory(outputFolder);

            var glb = new GlbBuilder();
            var records = new List<BimElementRecord>();
            var context = new BimExportContext(document);
            using (var exporter = new CustomExporter(document, context)
            {
                IncludeGeometricObjects = false,
                ShouldStopOnError = true
            })
                exporter.Export((View)view);

            foreach (var element in context.Elements.Values)
            {
                var record = element.Record;
                if (glb.AddElement(record.BimId, DisplayName(record), element.Meshes.Values.ToList()))
                    records.Add(record);
            }
            if (records.Count == 0) throw new InvalidOperationException("The active 3D view contains no exportable mesh geometry.");

            var glbPath = Path.Combine(outputFolder, "model.glb");
            var jsonPath = Path.Combine(outputFolder, "model.json");

            var payload = new
            {
                schema = "bim-mvp-package/v1",
                exportedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                source = new
                {
                    revitVersion = document.Application.VersionNumber,
                    title = document.Title,
                    path = document.PathName,
                    view = view.Name,
                    viewId = view.Id.IntegerValue
                },
                files = new { glb = "model.glb" },
                elements = records.Select(record => new
                {
                    bimId = record.BimId,
                    elementId = record.ElementId,
                    uniqueId = record.UniqueId,
                    name = record.Name,
                    category = record.Category,
                    family = record.Family,
                    type = record.Type,
                    parameters = record.Parameters
                }).ToList(),
                tree = BuildTree(records).Select(SerializeTreeNode).ToList(),
                exporterVersion = "0.3.0",
                warnings = context.Warnings.ToList()
            };

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            glb.Package = payload;
            glb.Write(glbPath);
            File.WriteAllText(jsonPath, serializer.Serialize(payload));

            return new ExportResult
            {
                GlbPath = glbPath,
                JsonPath = jsonPath,
                ElementCount = records.Count
            };
        }

        private static object SerializeTreeNode(BimTreeNode node)
        {
            return new
            {
                id = node.Id,
                label = node.Label,
                kind = node.Kind,
                children = node.Children.Select(SerializeTreeNode).ToList()
            };
        }

        internal static BimElementRecord CreateRecord(Document document, Element element)
        {
            var type = document.GetElement(element.GetTypeId()) as ElementType;
            var family = type is FamilySymbol symbol ? symbol.FamilyName : "";

            return new BimElementRecord
            {
                BimId = StableId.ForElement(document, element),
                ElementId = element.Id.IntegerValue,
                UniqueId = element.UniqueId,
                Name = element.Name ?? "",
                Category = element.Category != null ? element.Category.Name : "",
                Family = family,
                Type = type != null ? type.Name : "",
                Parameters = ReadParameters(element, type)
            };
        }

        private static Dictionary<string, string> ReadParameters(Element element, ElementType type)
        {
            var values = new SortedDictionary<string, string>();
            AddParameters(values, element.Parameters);
            if (type != null)
                AddParameters(values, type.Parameters);
            return values.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static void AddParameters(IDictionary<string, string> values, ParameterSet parameters)
        {
            foreach (Parameter parameter in parameters)
            {
                if (parameter.Definition == null)
                    continue;

                var name = parameter.Definition.Name;
                if (string.IsNullOrWhiteSpace(name) || values.ContainsKey(name))
                    continue;

                var value = ParameterValue(parameter);
                if (!string.IsNullOrWhiteSpace(value))
                    values[name] = value;
            }
        }

        private static string ParameterValue(Parameter parameter)
        {
            try
            {
                var formatted = parameter.AsValueString();
                if (!string.IsNullOrWhiteSpace(formatted))
                    return formatted;

                switch (parameter.StorageType)
                {
                    case StorageType.Double:
                        return parameter.AsDouble().ToString("G6", CultureInfo.InvariantCulture);
                    case StorageType.Integer:
                        return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                    case StorageType.String:
                        return parameter.AsString();
                    case StorageType.ElementId:
                        return parameter.AsElementId().IntegerValue.ToString(CultureInfo.InvariantCulture);
                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        private static string DisplayName(BimElementRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.Family) && !string.IsNullOrWhiteSpace(record.Type))
                return record.Family + " - " + record.Type;
            if (!string.IsNullOrWhiteSpace(record.Type))
                return record.Type;
            return record.Name;
        }

        private static List<BimTreeNode> BuildTree(IReadOnlyCollection<BimElementRecord> records)
        {
            return records
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "Uncategorized" : r.Category)
                .OrderBy(g => g.Key)
                .Select(category => new BimTreeNode
                {
                    Id = "category:" + category.Key,
                    Label = category.Key,
                    Kind = "category",
                    Children = category
                        .OrderBy(r => DisplayName(r))
                        .Select(r => new BimTreeNode
                        {
                            Id = r.BimId,
                            Label = DisplayName(r),
                            Kind = "element",
                            Children = new List<BimTreeNode>()
                        })
                        .ToList()
                })
                .ToList();
        }
    }
}
