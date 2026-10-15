using System.Collections.Generic;

namespace BIMExporter.Export
{
    public class ExportResult
    {
        public string GlbPath { get; set; }
        public string JsonPath { get; set; }
        public int ElementCount { get; set; }
    }

    public class BimElementRecord
    {
        public string BimId { get; set; }
        public int ElementId { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public Dictionary<string, string> Parameters { get; set; }
    }

    public class BimTreeNode
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Kind { get; set; }
        public List<BimTreeNode> Children { get; set; }
    }
}
