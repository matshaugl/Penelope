# Publishes a self-contained Penelope and, when Inno Setup is installed, builds PenelopeSetup.exe.
# The sample plugin is not included. Sign-in startup stays off until the person turns it on in the app.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts\publish\win-x64"
$pluginOut = Join-Path $publish "plugins\ollama"

$npm = Join-Path $env:LOCALAPPDATA "penelope-build\node\node-v22.20.0-win-x64\npm.cmd"
if (-not (Test-Path $npm)) {
    $found = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if ($found) { $npm = $found.Source }
}
if (-not (Test-Path $npm)) {
    throw "npm was not found. Install Node, then run this script again."
}

dotnet run --project (Join-Path $root "scripts\BrandExport\BrandExport.csproj") -c Release -- `
    (Join-Path $root "branding\penelope.svg") `
    (Join-Path $root "src\Penelope.Host\Assets") `
    (Join-Path $root "web\public")
if ($LASTEXITCODE -ne 0) { throw "The logo export failed." }

Push-Location (Join-Path $root "web")
try {
    & $npm run build
    if ($LASTEXITCODE -ne 0) { throw "The web build failed." }
}
finally {
    Pop-Location
}

if (Test-Path $publish) {
    Remove-Item $publish -Recurse -Force
}

dotnet publish (Join-Path $root "src\Penelope.Host\Penelope.Host.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publish `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "The host publish failed." }

dotnet build (Join-Path $root "src\Penelope.Modules.Ollama\Penelope.Modules.Ollama.csproj") `
    -c Release `
    -p:CopyToPlugins=false
if ($LASTEXITCODE -ne 0) { throw "The Ollama plugin build failed." }

$ollamaBin = Join-Path $root "src\Penelope.Modules.Ollama\bin\Release\net10.0"
New-Item -ItemType Directory -Force -Path $pluginOut | Out-Null
Copy-Item (Join-Path $ollamaBin "Penelope.Modules.Ollama.dll") $pluginOut
Copy-Item (Join-Path $ollamaBin "Penelope.Modules.Ollama.deps.json") $pluginOut
Copy-Item (Join-Path $root "src\Penelope.Modules.Ollama\manifest.json") $pluginOut
Copy-Item (Join-Path $root "src\Penelope.Modules.Ollama\catalog.json") $pluginOut
Copy-Item (Join-Path $root "plugins\ollama\ui") (Join-Path $pluginOut "ui") -Recurse -Force

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Published to $publish. Inno Setup was not found, so PenelopeSetup.exe was not built."
    exit 0
}

& $iscc "/DPublishDir=$publish" (Join-Path $root "installer\penelope.iss")
if ($LASTEXITCODE -ne 0) { throw "The installer compile failed." }
Write-Host "Installer: $(Join-Path $root 'artifacts\PenelopeSetup.exe')"
