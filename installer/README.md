# ScreenBux.Updater installer

Two ways to install/uninstall the `ScreenBux.Updater` service (the one component that installs
and updates the ScreenBux Service and Agent, and therefore can't install itself):

## 1. Quick start: `Install-Updater.ps1`

For local experimentation and manual deployments. See the script's comment-based help
(`Get-Help .\Install-Updater.ps1 -Full`) for all parameters.

```powershell
dotnet publish ..\src\ScreenBux.Updater -c Release -o .\publish\Updater
.\Install-Updater.ps1 -PublishedOutputPath .\publish\Updater -ServerBaseUrl https://screenbux-api.azurewebsites.net

# Later, to remove everything the Updater provisioned (Updater + managed Service + Agent):
.\Install-Updater.ps1 -Uninstall -RemoveManagedComponents

# Or, to remove only the Updater itself and leave enforcement running:
.\Install-Updater.ps1 -Uninstall
```

## 2. Production packaging: `ScreenBux.Updater.Installer` (WiX v5 MSI)

`ScreenBux.Updater.Installer.wixproj` builds an MSI for the Updater using the
[WiX Toolset v5 SDK](https://wixtoolset.org/) (`WixToolset.Sdk`), which builds via plain
`dotnet build`/`msbuild` - no Visual Studio extension required.

```powershell
dotnet publish ..\src\ScreenBux.Updater -c Release -o .\ScreenBux.Updater.Installer\PublishedOutput
dotnet build .\ScreenBux.Updater.Installer -c Release
msiexec /i .\ScreenBux.Updater.Installer\bin\Release\ScreenBux.Updater.Installer.msi SERVERBASEURL=https://screenbux-api.azurewebsites.net
```

Uninstalling the MSI (`msiexec /x` or via Add/Remove Programs) runs a deferred custom action
that invokes `ScreenBux.Updater.exe --cleanup-managed-components` **before** removing the
Updater's own service/files, so the managed Service and Agent are stopped and removed too -
this only fires on a genuine uninstall, not during a version upgrade.

### Known follow-ups before this is CI/production-ready

- **JSON config generation**: `appsettings.Production.template.json` is patched via WiX's
  `util:XmlFile`, which only understands XML. Since `ScreenBux.Updater` reads JSON, this needs
  to be replaced with either a small custom action that writes real JSON, or an
  installer-time text substitution mechanism, before relying on this for real deployments.
- **File harvesting**: `Product.wxs` uses a `Files/@Include` glob against `PublishedOutput\**\*.*`;
  verify this matches the actual publish output (trimmed/self-contained vs. framework-dependent)
  before shipping.
- **Code signing**: neither the MSI nor the Updater/Service/Agent executables are signed here;
  add signing to the release pipeline before external distribution.
- Add this `.wixproj` to `ScreenBux2.sln` once WiX v5 tooling is confirmed available in CI
  (it is *not* currently referenced by the solution to avoid breaking `dotnet build
  ScreenBux2.sln` on machines without the WiX workload/NuGet feed reachable).
