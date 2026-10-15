# BIM Lightweight Online Viewer MVP

This workspace contains two independent MVP projects:

- `BIMExporter`: Revit 2020 add-in, C#/.NET Framework 4.8, exports a local `model.glb` + `model.json` package.
- `BIMViewer`: Vue 3 + TypeScript + Three.js web viewer, loads the exported package from local files and supports picking, properties, model tree, hide, and isolate.

The MVP is intentionally local-first. It does not include login, database storage, cloud upload, permissions, or model management.

## Architecture

1. Revit 2020 opens an `.rvt` model.
2. `BIMExporter` exports the active 3D view through Revit CustomExporter, creates stable element IDs (including link instance paths), and writes:
   - `model.glb`: geometry plus metadata in `extras.bimPackage`, with stable IDs on element nodes.
   - `model.json`: optional metadata sidecar for interoperability.
3. `BIMViewer` loads the GLB alone; legacy exports still accept a GLB/JSON pair.
4. Three.js raycasting reads `node.userData.bimId`, maps it to `model.json`, and updates tree selection and the property panel.

## Data Package

`model.json` schema:

```json
{
  "schema": "bim-mvp-package/v1",
  "exportedAt": "2026-10-14T00:00:00.000Z",
  "source": {
    "revitVersion": "2020",
    "title": "Project",
    "path": "C:\\path\\project.rvt"
  },
  "files": {
    "glb": "model.glb"
  },
  "elements": [
    {
      "bimId": "bim-...",
      "elementId": 12345,
      "uniqueId": "...",
      "name": "Basic Wall",
      "category": "Walls",
      "family": "Basic Wall",
      "type": "Generic - 200mm",
      "parameters": {}
    }
  ],
  "tree": []
}
```

`bimId` is stable for the same Revit document and element because it is derived from the project/document identity and `Element.UniqueId`.

## BIMExporter Build

Requirements:

- Windows
- Autodesk Revit 2020 installed
- Revit API assemblies at `C:\Program Files\Autodesk\Revit 2020\RevitAPI.dll`
- .NET Framework 4.8 targeting support or Visual Studio Build Tools/MSBuild

Build:

```powershell
dotnet build BIMExporter\BIMExporter.csproj -c Release
```

Install into Revit 2020:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File BIMExporter\Install-Revit2020.ps1
```

Restart Revit 2020, load a model, use the `BIM MVP > Export BIM` ribbon button, and choose an output folder. The add-in should create `model.glb`, `model.json`, and an error log only if export fails.

The installer reads PluginVersion and registers `BIMExporter/bin/Release/<PluginVersion>/net48/BIMExporter.dll`. For
future updates, close Revit, rebuild Release, then start Revit. No DLL copying is
needed for same-version rebuilds. Rerun the installer after a version change. Keep the project directory in place or rerun the installer after moving
it. Already running Revit sessions do not hot-reload plugin updates.

## BIMViewer Build And Test

Requirements:

- Node.js 20+ recommended

Install:

```powershell
cd BIMViewer
npm install
```

Test:

```powershell
npm run test
```

Production build:

```powershell
npm run build
```

Local development:

```powershell
npm run dev
```

Then open the printed local URL. Use **打开模型** to select the new `model.glb`
alone, or **打开文件夹** to select the export folder. For legacy exports without
embedded properties, select the matching JSON too; either selection order works.
Legacy PascalCase JSON is supported; incompatible IDs and ambiguous file sets
are rejected. Files are read locally in the browser, not uploaded to a server.

To run the optional regression against an actual export:

```powershell
cd BIMViewer
$env:BIM_TEST_PACKAGE_DIR = 'C:\MyChatFloder\BimViewr\test'
npm test
```

Exporter 0.2.0 replaces manual geometry traversal with CustomExporter callbacks.
Export follows the active 3D view. Normals, UVs, basic transparency and per-element
material merging are included. Cross-instance mesh sharing, Draco and texture
images remain deferred until real Revit acceptance testing. Old GLBs must be
regenerated after restarting Revit. See BIMExporter/README.md for the C# smoke test
and the linked/mirrored/nested family acceptance checklist.

## Verified In This Workspace

- `BIMViewer`: unit tests passed.
- `BIMViewer`: production build passed.
- `BIMExporter`: Release build passed against local Revit 2020 API assemblies.

Not verified here:

- Opening Revit's UI and running the add-in against a real `.rvt` model.

Manual Revit validation steps:

1. Build `BIMExporter` in Release mode.
2. Install `BIMExporter.dll` and `BIMExporter.addin` using the install commands above.
3. Start Revit 2020.
4. Open a small architectural sample model and switch to a 3D view.
5. Click `BIM MVP > Export BIM`.
6. Confirm `model.glb` and `model.json` are created.
7. Run `BIMViewer`, load only `model.glb`, and verify model display, picking, properties, tree selection, hide, and isolate.

