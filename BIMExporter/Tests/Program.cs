using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Web.Script.Serialization;
using BIMExporter.Export;

internal static class Program
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static MeshData Box(Vector3 min, Vector3 max, MaterialColor material)
    {
        var mesh = new MeshData { Material = material };
        var p = new[] {
            new Vector3(min.X,min.Y,min.Z), new Vector3(max.X,min.Y,min.Z),
            new Vector3(max.X,max.Y,min.Z), new Vector3(min.X,max.Y,min.Z),
            new Vector3(min.X,min.Y,max.Z), new Vector3(max.X,min.Y,max.Z),
            new Vector3(max.X,max.Y,max.Z), new Vector3(min.X,max.Y,max.Z)
        };
        var faces = new[] { new[]{0,3,2,1}, new[]{4,5,6,7}, new[]{0,1,5,4}, new[]{3,7,6,2}, new[]{0,4,7,3}, new[]{1,2,6,5} };
        var uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
        foreach (var face in faces)
        {
            var normal = Vector3.Normalize(Vector3.Cross(p[face[1]] - p[face[0]], p[face[2]] - p[face[0]]));
            foreach (var i in new[] { 0,1,2,0,2,3 }) mesh.AddVertex(p[face[i]], normal, uv[i]);
        }
        return mesh;
    }

    public static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }

    private static void Run(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var wall = Box(new Vector3(0,0,0), new Vector3(12,1,9), new MaterialColor(180,190,180,1));
        var glass = Box(new Vector3(3,-.3f,3), new Vector3(9,-.2f,7), new MaterialColor(90,180,205,.35f,"Test glass","material:glass",.12f));
        Assert(!new MaterialColor(90,180,205,.35f,"Test glass","material:other",.12f).Equals(glass.Material), "Distinct material identities must not merge.");
        Assert(!new MaterialColor(90,180,205,.35f,"Test glass","material:glass",.8f).Equals(glass.Material), "Distinct roughness must not merge.");
        Assert(wall.Positions.Count / 3 == 24 && wall.Indices.Count == 36, "Vertex sharing must preserve hard edges and UV seams.");
        var glb = new GlbBuilder();
        Assert(glb.AddElement("bim-test-wall", "Synthetic wall", new[] { wall }), "Wall missing.");
        Assert(glb.AddElement("bim-test-glass", "Synthetic glass", new[] { glass }), "Glass missing.");
        Assert(!glb.AddElement("empty", "Empty", new MeshData[0]), "Empty element should be omitted.");
        var ids = new[] { "bim-test-wall", "bim-test-glass" };
        glb.Package = new {
            schema = "bim-mvp-package/v1", exportedAt = "2026-10-14T00:00:00Z",
            source = new { title = "Synthetic writer test - NOT a Revit export", path = "", revitVersion = "test" },
            files = new { glb = "model.glb" }, exporterVersion = "0.2.0",
            elements = ids.Select((id,i) => new { bimId=id, elementId=i+1, uniqueId=id, name=i==0?"Synthetic wall":"Synthetic glass",
                category="Test geometry", family="", type="Box", parameters=new Dictionary<string,string>{{"Fixture","Synthetic, not exported from Revit"}} }).ToArray(),
            tree = new[] { new { id="category:Test", label="Test geometry", kind="category", children=ids.Select(id=>new {
                id=id, label=id, kind="element", children=new object[0] }).ToArray() } }
        };
        glb.Write(output);
        var bytes = File.ReadAllBytes(output);
        using (var reader = new BinaryReader(new MemoryStream(bytes)))
        {
            Assert(reader.ReadUInt32() == 0x46546c67 && reader.ReadUInt32() == 2, "Invalid GLB header.");
            Assert(reader.ReadInt32() == bytes.Length, "GLB length mismatch.");
            int jsonSize = reader.ReadInt32();
            Assert(jsonSize % 4 == 0 && reader.ReadUInt32() == 0x4e4f534a, "Invalid JSON chunk.");
            var root = (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(System.Text.Encoding.UTF8.GetString(reader.ReadBytes(jsonSize)));
            int binarySize = reader.ReadInt32();
            Assert(binarySize % 4 == 0 && reader.ReadUInt32() == 0x004e4942, "Invalid BIN chunk.");
            var binaryStart = reader.BaseStream.Position;
            Assert(binaryStart + binarySize == bytes.Length, "Invalid binary length.");
            var nodes = (object[])root["nodes"];
            Assert(nodes.Length == 3, "Expected two elements plus coordinate root.");
            var matrix = ((object[])((Dictionary<string,object>)nodes[2])["matrix"]).Select(Convert.ToDouble).ToArray();
            Assert(Math.Abs(matrix[0]*10 - 3.048) < 1e-9 && Math.Abs(matrix[9]*6-1.8288)<1e-9 && Math.Abs(matrix[6]*4+1.2192)<1e-9,
                "Feet/Z-up conversion is incorrect.");
            Assert(((Dictionary<string,object>)root["extras"])["bimPackage"] != null, "Embedded package missing.");
            var materials = (object[])root["materials"];
            Assert((string)((Dictionary<string,object>)materials[0])["alphaMode"] == "OPAQUE", "Opaque material incorrect.");
            Assert((string)((Dictionary<string,object>)materials[1])["alphaMode"] == "BLEND", "Glass opacity not preserved.");
            var glassMaterial = (Dictionary<string,object>)materials[1];
            Assert((string)glassMaterial["name"] == "Test glass", "Material name lost.");
            Assert((string)((Dictionary<string,object>)glassMaterial["extras"])["revitMaterialId"] == "material:glass", "Material identity lost.");
            var glassPbr = (Dictionary<string,object>)glassMaterial["pbrMetallicRoughness"];
            Assert(Math.Abs(Convert.ToDouble(glassPbr["roughnessFactor"]) - .12) < 1e-6, "Roughness lost.");
            var rgba = ((object[])glassPbr["baseColorFactor"]).Select(Convert.ToDouble).ToArray();
            Assert(rgba[2] > rgba[0] && Math.Abs(rgba[3] - .35) < 1e-6, "Color or transparency lost.");
            var views = (object[])root["bufferViews"];
            var accessors = (object[])root["accessors"];
            foreach (Dictionary<string,object> mesh in (object[])root["meshes"])
            {
                Assert(((object[])mesh["primitives"]).Length == 1, "Expected one primitive per material.");
                var primitive = (Dictionary<string,object>)((object[])mesh["primitives"])[0];
                var attributes = (Dictionary<string,object>)primitive["attributes"];
                var position = (Dictionary<string,object>)accessors[Convert.ToInt32(attributes["POSITION"])];
                var normal = (Dictionary<string,object>)accessors[Convert.ToInt32(attributes["NORMAL"])];
                var uv = (Dictionary<string,object>)accessors[Convert.ToInt32(attributes["TEXCOORD_0"])];
                Assert(Convert.ToInt32(position["count"]) == Convert.ToInt32(normal["count"]) && Convert.ToInt32(uv["count"])==24, "Attribute count mismatch.");
                var indices = (Dictionary<string,object>)accessors[Convert.ToInt32(primitive["indices"])];
                var indexView = (Dictionary<string,object>)views[Convert.ToInt32(indices["bufferView"])];
                reader.BaseStream.Position = binaryStart + Convert.ToInt32(indexView["byteOffset"]);
                for (var i=0; i<Convert.ToInt32(indices["count"]); i++) Assert(reader.ReadUInt32()<24, "Index out of bounds.");
                var normalView = (Dictionary<string,object>)views[Convert.ToInt32(normal["bufferView"])];
                reader.BaseStream.Position = binaryStart + Convert.ToInt32(normalView["byteOffset"]);
                for (var i=0; i<24; i++)
                {
                    var n = new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                    Assert(Math.Abs(n.Length()-1)<1e-5, "Normal is not unit length.");
                }
            }
        }
        Console.WriteLine("PASS: GLB headers, metadata, coordinates, normals, UVs, vertex sharing, indices and transparency.");
        Console.WriteLine(output);
    }
}
