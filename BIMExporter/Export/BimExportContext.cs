using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace BIMExporter.Export
{
    internal sealed class ExportedElement
    {
        public BimElementRecord Record;
        public readonly Dictionary<MaterialColor, MeshData> Meshes = new Dictionary<MaterialColor, MeshData>();
    }

    internal sealed class BimExportContext : IExportContext
    {
        private readonly Document _host;
        private readonly Stack<Document> _documents = new Stack<Document>();
        private readonly Stack<Transform> _transforms = new Stack<Transform>();
        private readonly Stack<ElementFrame> _elements = new Stack<ElementFrame>();
        private readonly Stack<string> _links = new Stack<string>();
        private MaterialColor _material = new MaterialColor(180, 180, 180, 1);
        public readonly Dictionary<string, ExportedElement> Elements = new Dictionary<string, ExportedElement>();
        public readonly HashSet<string> Warnings = new HashSet<string>();

        private sealed class ElementFrame
        {
            public Element Element;
            public string LinkPath;
            public MaterialColor PreviousMaterial;
        }

        public BimExportContext(Document document) { _host = document; }
        public bool Start()
        {
            _documents.Push(_host);
            _transforms.Push(Transform.Identity);
            return true;
        }
        public void Finish()
        {
            if (_transforms.Count != 1 || _documents.Count != 1 || _elements.Count != 0 || _links.Count != 0)
                throw new InvalidOperationException("Unbalanced export traversal.");
        }
        public bool IsCanceled() { return false; }
        public RenderNodeAction OnViewBegin(ViewNode node) { return RenderNodeAction.Proceed; }
        public void OnViewEnd(ElementId id) { }
        public RenderNodeAction OnFaceBegin(FaceNode node) { return RenderNodeAction.Proceed; }
        public void OnFaceEnd(FaceNode node) { }
        public void OnLight(LightNode node) { }
        public void OnRPC(RPCNode node) { Warnings.Add("RPC appearances are not exported; only available polymesh geometry is included."); }

        public RenderNodeAction OnElementBegin(ElementId id)
        {
            _elements.Push(new ElementFrame
            {
                Element = _documents.Peek().GetElement(id),
                LinkPath = string.Join("/", _links.Reverse()),
                PreviousMaterial = _material
            });
            _material = new MaterialColor(180, 180, 180, 1);
            return RenderNodeAction.Proceed;
        }
        public void OnElementEnd(ElementId id) { _material = _elements.Pop().PreviousMaterial; }
        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            _transforms.Push(_transforms.Peek().Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }
        public void OnInstanceEnd(InstanceNode node) { _transforms.Pop(); }
        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            var link = _elements.Count > 0 ? _elements.Peek().Element as RevitLinkInstance : null;
            if (link == null) throw new InvalidOperationException("Cannot identify the link instance for stable element IDs.");
            _links.Push(link.UniqueId);
            _documents.Push(node.GetDocument());
            _transforms.Push(_transforms.Peek().Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }
        public void OnLinkEnd(LinkNode node)
        {
            _transforms.Pop();
            _documents.Pop();
            _links.Pop();
        }
        public void OnMaterial(MaterialNode node)
        {
            // Rendering callbacks can be monochrome in non-realistic views. Read the
            // assigned material's shading properties rather than baking that override.
            var source = _documents.Peek().GetElement(node.MaterialId) as Autodesk.Revit.DB.Material;
            var color = source != null && source.Color.IsValid ? source.Color : node.Color;
            var alpha = source != null ? 1 - source.Transparency / 100f : (float)(1 - node.Transparency);
            _material = color != null && color.IsValid
                ? new MaterialColor(color.Red, color.Green, color.Blue, alpha,
                    source?.Name ?? "Default", source == null ? "" : _documents.Peek().ProjectInformation.UniqueId + ":" + source.UniqueId,
                    source == null ? .65f : (float)Math.Max(.08, Math.Sqrt(2.0 / (source.Shininess + 2))))
                : new MaterialColor(180, 180, 180, 1);
            if (source != null && source.AppearanceAssetId != ElementId.InvalidElementId)
                Warnings.Add("Material shading colors and transparency are exported; appearance textures and procedural assets are not included.");
        }

        public void OnPolymesh(PolymeshTopology topology)
        {
            if (_elements.Count == 0 || topology.NumberOfFacets == 0) return;
            var frame = _elements.Peek();
            if (frame.Element == null) throw new InvalidOperationException("Mesh has no owning Revit element.");
            var element = frame.Element;
            var id = StableId.ForElement(element.Document, element);
            if (frame.LinkPath.Length > 0)
                id = StableId.ForLinkedElement(_host, frame.LinkPath, element.Document, element);
            ExportedElement exported;
            if (!Elements.TryGetValue(id, out exported))
            {
                exported = new ExportedElement { Record = BimPackageExporter.CreateRecord(element.Document, element) };
                exported.Record.BimId = id;
                if (frame.LinkPath.Length > 0)
                {
                    exported.Record.Parameters["Source document"] = element.Document.Title;
                    exported.Record.Parameters["Link instance path"] = frame.LinkPath;
                }
                Elements.Add(id, exported);
            }
            MeshData mesh;
            if (!exported.Meshes.TryGetValue(_material, out mesh))
            {
                mesh = new MeshData { Material = _material };
                exported.Meshes.Add(_material, mesh);
            }
            var transform = _transforms.Peek();
            var inverse = transform.Inverse;
            var points = topology.GetPoints();
            var normals = topology.GetNormals();
            var uvs = topology.GetUVs();
            var facets = topology.GetFacets();
            for (var faceIndex = 0; faceIndex < facets.Count; faceIndex++)
            {
                var facet = facets[faceIndex];
                // A reflected placement reverses winding; inverse-transpose preserves outward normals.
                var indices = transform.Determinant < 0
                    ? new[] { facet.V1, facet.V3, facet.V2 } : new[] { facet.V1, facet.V2, facet.V3 };
                foreach (var pointIndex in indices)
                {
                    var point = transform.OfPoint(points[pointIndex]);
                    var normalIndex = topology.DistributionOfNormals == DistributionOfNormals.AtEachPoint ? pointIndex
                        : topology.DistributionOfNormals == DistributionOfNormals.OnEachFacet ? faceIndex : 0;
                    if (normalIndex >= normals.Count) throw new InvalidOperationException("Invalid polymesh normal distribution.");
                    var localNormal = normals[normalIndex];
                    var normal = new XYZ(inverse.BasisX.DotProduct(localNormal), inverse.BasisY.DotProduct(localNormal), inverse.BasisZ.DotProduct(localNormal)).Normalize();
                    var uv = uvs.Count == points.Count ? uvs[pointIndex] : new UV(0, 0);
                    mesh.AddVertex(new Vector3((float)point.X, (float)point.Y, (float)point.Z),
                        new Vector3((float)normal.X, (float)normal.Y, (float)normal.Z), new Vector2((float)uv.U, (float)uv.V));
                }
            }
        }
    }
}
