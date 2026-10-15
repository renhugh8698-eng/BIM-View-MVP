# BIMExporter

## 0.3.0: material shading and AddInManager

- Build output: `bin/Release/0.3.0/net48/BIMExporter.dll`.
- For AddInManager, load this DLL and run `BIMExporter.Commands.ExportModelCommand` in Manual mode. The command implements `IExternalCommand`; the ribbon `App` is not the export command. Re-loading behavior depends on your AddInManager version and has not been tested in this environment.
- Each release uses a separate version directory. Do not overwrite a DLL already loaded by Revit. The existing ribbon can still point to the older release until the installer is rerun and Revit restarted; AddInManager can invoke the new command independently.
- The exporter now reads assigned `Material.Color`, `Transparency`, material name and identity from the current document (including linked documents), falling back to the rendering callback for unassigned faces. This intentionally exports material shading rather than monochrome view overrides.
- Shininess is approximately mapped to glTF roughness; this is not a faithful rendering-appearance conversion. Bitmap textures, procedural appearances, metallic/refraction properties and view-specific graphic overrides remain outside this release.
- Re-export your model to get these material changes. Old GLBs containing only gray colors cannot recover their original materials inside the viewer.


Revit 2020 add-in that exports a lightweight BIM package:

- `model.glb`: self-contained geometry, properties and model tree; open this file alone.
- `model.json`: optional sidecar containing the same embedded metadata.

## Build

```powershell
dotnet build BIMExporter.csproj -c Release
```

The project references:

- `C:\Program Files\Autodesk\Revit 2020\RevitAPI.dll`
- `C:\Program Files\Autodesk\Revit 2020\RevitAPIUI.dll`

## Install

From the BIMExporter directory, register the Release output for the current user:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Revit2020.ps1
```

The installer writes an absolute DLL path into:

```text
%APPDATA%\Autodesk\Revit\Addins\2020\BIMExporter.addin
```

Restart Revit 2020 and use `BIM MVP > Export BIM`.

## Update

Close Revit before building to release the loaded DLL. Build with
`dotnet build BIMExporter.csproj -c Release`, then start Revit again.
The registration loads directly from `bin\Release\<PluginVersion>\net48`.
Rebuilding the same version needs no copying. When PluginVersion changes, rerun
the installer to register the new version directory; old loaded DLLs are retained.
Running Revit sessions do not hot-reload the assembly. Debug builds are not used.
Keep the project directory in place; if you move it, rerun the installer.
The installer is repeatable and backs up a changed existing registration.
Real Revit startup and model export still require manual verification.

## 0.2.0 CustomExporter Pipeline

The active non-template 3D view is exported through `CustomExporter` and
`IExportContext`. Visibility, section box and detail level follow that view.
Instance and link transforms are stacked and baked into vertices exactly once.
One GLB root matrix converts feet/Z-up to meters/Y-up. Normals use the correct
per-face, per-facet or per-point distribution and inverse-transpose transformation;
reflections reverse triangle winding. Geometry is merged by exported RGBA material
within each element, preserving normal/UV seams when reusing identical vertices.

The root `extras.bimPackage` contains the existing v1 metadata contract; element
nodes keep `extras.bimId`. Linked element IDs also include host identity and the
ordered path of link instance UniqueIds, distinguishing repeated linked models.
`exporterVersion` is `0.2.0`. Host element IDs remain compatible with prior exports.

Architecture reference: [cowboy1997/Revit2GLTF](https://github.com/cowboy1997/Revit2GLTF).
This implementation uses its CustomExporter approach without copying its symbol-only
mesh cache or first-normal behavior. It does not use FBX, Assimp or Draco.

Current baseline: basic material color/transparency, normals and UV coordinates.
Texture images, advanced appearance assets, cross-instance mesh sharing, Draco,
and RPC appearance assets are not implemented. RPC limitations are recorded in
metadata warnings. Light sources are not exported.

## Executable Tests

From the repository root:

```powershell
dotnet build BIMExporter\Tests\ExporterSmoke.csproj -c Release
& .\BIMExporter\Tests\bin\Release\ExporterSmoke.exe work\synthetic-custom-export.glb
cd BIMViewer
$env:BIM_WRITER_TEST_GLB = (Resolve-Path ..\work\synthetic-custom-export.glb).Path
$env:BIM_TEST_PACKAGE_DIR = 'C:\MyChatFloder\BimViewr\test'
npm test
```

The C# executable compiles the actual GLB writer and validates headers, embedded
metadata, root conversion, material opacity, index bounds, normals, UVs and vertex
sharing. Its fixture is synthetic, not a real Revit export. It does not execute
Revit callbacks or validate real family placement.

Revit 2020 acceptance checks (not automated outside Revit):

1. Restart all Revit instances after installing the new registration.
2. Open a 3D view and export to a new folder. Confirm `exporterVersion: "0.2.0"`.
3. Compare doors/windows against their wall openings and elevations in Revit.
4. Include translated, rotated, mirrored and nested family instances; check
   placement and orientation, plus a wall-cut instance with direct solid geometry.
5. Include a linked model placed twice (with rotation and translation); check both
   placements and confirm identical source elements have different BIM IDs.
6. Only open `model.glb` in BIMViewer; select a window and compare Element ID and
   parameters, then hide and isolate it. Inspect glass opacity and curved surfaces.
7. Compare current-view visibility and section-box cuts with the exported result.

