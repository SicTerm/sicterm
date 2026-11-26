[CmdletBinding()]
param(
    # Where SicExplorerCommand.dll is copied once built; nothing is copied when not given.
    [string]$Destination
)
<#
Builds src\Sic.ExplorerCommand (the COM DLL behind Open in SIC on Windows 11's own right-click menu) with MSBuild from the
Visual Studio Build Tools' "Desktop development with C++" workload, found through vswhere, and copies the DLL to -Destination.
Publish-App.ps1 runs it. Install the tools once per PC:

  winget install --id Microsoft.VisualStudio.2022.BuildTools --exact --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"

Outputs the DLL's path.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'src\Sic.ExplorerCommand\SicExplorerCommand.vcxproj'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw "Visual Studio's installer (vswhere.exe) was not found. Install the Build Tools' C++ workload; see this script's help." }
$msbuild = & $vswhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -latest -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'No Visual Studio instance with the C++ tools (Microsoft.VisualStudio.Component.VC.Tools.x86.x64) was found. Install the Build Tools workload; see this script''s help.' }

& $msbuild $project -nologo -verbosity:minimal -restore:false -p:Configuration=Release -p:Platform=x64 | Out-Host
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed for SicExplorerCommand (exit code $LASTEXITCODE)." }
$dll = Join-Path $repo 'src\Sic.ExplorerCommand\bin\x64\Release\SicExplorerCommand.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "The build produced no DLL at $dll." }
if ($Destination) { Copy-Item -LiteralPath $dll -Destination (Join-Path $Destination 'SicExplorerCommand.dll') -Force }
$dll
