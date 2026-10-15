[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'BIMExporter.csproj') -Raw
$versionNode = $project.SelectSingleNode("//*[local-name()='PluginVersion']")
if ($null -eq $versionNode -or $versionNode.InnerText -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid plugin version in project.' }
$assemblyPath = Join-Path $PSScriptRoot ('bin\Release\' + $versionNode.InnerText + '\net48\BIMExporter.dll')
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'Release DLL is missing. Build BIMExporter.csproj with -c Release first.'
}
$assemblyPath = (Resolve-Path -LiteralPath $assemblyPath).Path
$assemblyName = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
if ($assemblyName.Name -ne 'BIMExporter') {
    throw 'The Release DLL is not BIMExporter.'
}

$addinDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2020'
$manifestPath = Join-Path $addinDirectory 'BIMExporter.addin'
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'BIMExporter.addin') -Raw
$manifest.RevitAddIns.AddIn.Assembly = $assemblyPath
New-Item -ItemType Directory -Path $addinDirectory -Force | Out-Null
if (Test-Path -LiteralPath $manifestPath) {
    [xml]$existing = Get-Content -LiteralPath $manifestPath -Raw
    if ($existing.OuterXml -ne $manifest.OuterXml) {
        $backupPath = $manifestPath + '.' + [Guid]::NewGuid().ToString('N') + '.bak'
        Copy-Item -LiteralPath $manifestPath -Destination $backupPath
        Write-Output "Previous registration backed up: $backupPath"
    }
}
$manifest.Save($manifestPath)
[xml]$installed = Get-Content -LiteralPath $manifestPath -Raw
if ($installed.RevitAddIns.AddIn.Assembly -ne $assemblyPath) {
    throw 'Installed manifest verification failed.'
}
Write-Output "Installed: $manifestPath"
Write-Output "Loads directly from: $assemblyPath"
Write-Output 'Updates: close Revit, build Release, then start Revit. No DLL copy is needed.'
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    Write-Output 'Revit is running. Save your work and restart it to load this registration.'
}
