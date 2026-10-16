[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string] $Runtime = 'win-x64',
    [switch] $Package
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repoRoot 'src/WinOptimizer.App/WinOptimizer.App.csproj'
$testProject = Join-Path $repoRoot 'tests/WinOptimizer.Tests/WinOptimizer.Tests.csproj'
$publishDir = Join-Path $repoRoot "artifacts/publish/$Runtime"

Push-Location $repoRoot
try {
    dotnet restore $testProject --configfile (Join-Path $repoRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    dotnet test $testProject --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

    dotnet publish $appProject --configuration $Configuration --runtime $Runtime --self-contained false --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    node (Join-Path $repoRoot 'tests/WinOptimizer.Tests/website.test.mjs')
    if ($LASTEXITCODE -ne 0) { throw "website validation failed with exit code $LASTEXITCODE" }

    if ($Package) {
        $compilerCandidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe')
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

        if (-not $compilerCandidates) {
            throw 'Inno Setup 6 ISCC.exe was not found. Install Inno Setup 6 or omit -Package.'
        }

        & $compilerCandidates[0] (Join-Path $repoRoot 'installer/WinOptimizer.iss')
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
    }
}
finally {
    Pop-Location
}
