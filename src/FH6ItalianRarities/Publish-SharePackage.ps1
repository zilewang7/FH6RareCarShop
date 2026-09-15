[CmdletBinding()]
param(
    [string]$Version = "2.0.0"
)

$ErrorActionPreference = "Stop"
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "..\.."))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "release"))
$packageName = "FH6-Rare-Car-Shop-v$Version-win-x64"
$packageDirectory = [IO.Path]::GetFullPath((Join-Path $releaseRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $releaseRoot "$packageName.zip"))
$dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$project = Join-Path $projectRoot "FH6RareCarShop.csproj"
$publishDirectory = Join-Path $projectRoot "bin\Release\net8.0-windows\win-x64\publish"
$selfTestDll = Join-Path $projectRoot "bin\Release\net8.0-windows\win-x64\FH6RareCarShop.dll"

if (-not $packageDirectory.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolved package directory escaped the release root."
}

& $dotnet build $project -c Release -r win-x64 `
    -p:PublishSingleFile=false -p:SelfContained=false -p:Version=$Version `
    -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

& $dotnet $selfTestDll --self-test
if ($LASTEXITCODE -ne 0) {
    throw "Catalog self-test failed with exit code $LASTEXITCODE."
}

& $dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=false -p:Version=$Version `
    -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDirectory | Out-Null

$publishedExe = Join-Path $publishDirectory "FH6RareCarShop.exe"
Copy-Item -LiteralPath $publishedExe -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot "README.txt") -Destination $packageDirectory

$hash = Get-FileHash -LiteralPath (Join-Path $packageDirectory "FH6RareCarShop.exe") -Algorithm SHA256
"SHA256  $($hash.Hash)  FH6RareCarShop.exe" |
    Set-Content -LiteralPath (Join-Path $packageDirectory "SHA256.txt") -Encoding ascii

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal

Get-Item -LiteralPath $publishedExe, $zipPath |
    Select-Object FullName, Length, LastWriteTime
