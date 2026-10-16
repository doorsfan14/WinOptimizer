# Releases and installation

## WinOptimizer 26.0.0 preview

Version 26.0.0 is an unsigned preview prerelease. It is intended for evaluation, not as a claim of stable or independently certified behavior. Hardware sensors vary by manufacturer and firmware. Read the release notes before installing.

The GitHub Actions release workflow runs the automated tests, publishes the x64 app folder, creates a portable ZIP, builds a per-user Inno Setup installer, and attaches those files to a GitHub prerelease. The workflow must finish successfully before the assets appear on the Releases page. It does not require a code-signing certificate; Windows may show an unknown-publisher warning.

## Install on Windows

Download `WinOptimizer-Setup-26.0.0-win-x64.exe` from the repository's GitHub Releases page. It installs per user under `%LOCALAPPDATA%\Programs\WinOptimizer` and does not request administrator rights. Optional desktop shortcut creation is offered by the installer. User settings are kept during upgrades and uninstall.

The portable archive is a framework-dependent x64 build. It requires the .NET 10 Windows Desktop Runtime. Extract the ZIP and run `WinOptimizer.exe`. It does not install or modify system components.

## Build and package from source

On Windows with the .NET 10 SDK, restore the test project, run the tests, and publish with `scripts/build.ps1`. To compile the setup executable locally, install Inno Setup 6 and pass `-Package`. The GitHub Actions workflow installs Inno Setup on its temporary runner.

```powershell
dotnet restore .\tests\WinOptimizer.Tests\WinOptimizer.Tests.csproj --configfile .\NuGet.Config
dotnet test .\tests\WinOptimizer.Tests\WinOptimizer.Tests.csproj --configuration Release
.\scripts\build.ps1 -Configuration Release
```

No installer should be called verified until the Windows workflow has completed successfully and the attached assets can be downloaded and inspected.

