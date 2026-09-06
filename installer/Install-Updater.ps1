<#
.SYNOPSIS
	Installs, updates, or uninstalls the ScreenBux.Updater Windows Service.

.DESCRIPTION
	ScreenBux.Updater is the one component in the ScreenBux system that can't install itself:
	once it's running (as an elevated Windows Service), it's responsible for installing and
	updating the ScreenBux Service and Agent on the same machine. This script bootstraps the
	Updater itself - copying its published output, writing its configuration, registering it
	as a Windows Service, and starting it.

	This is intended as a quick way to install/experiment locally. For a production-grade,
	double-click installer with proper Add/Remove Programs integration, see the WiX project at
	installer/ScreenBux.Updater.Installer, which performs the equivalent steps as an MSI.

.PARAMETER PublishedOutputPath
	Path to the published ScreenBux.Updater output (e.g. the result of
	`dotnet publish src/ScreenBux.Updater -c Release -o <path>`). Required for -Install.

.PARAMETER InstallDirectory
	Where to copy the Updater's own files. Defaults to "C:\Program Files\ScreenBux\Updater".

.PARAMETER ServerBaseUrl
	The WebServer base URL the Updater should poll for its update manifest
	(e.g. https://screenbux-api.azurewebsites.net). Required for -Install.

.PARAMETER ServiceInstallDirectory
	Where the managed ScreenBux Service's files should be installed/updated.
	Defaults to "C:\Program Files\ScreenBux\Service".

.PARAMETER ServiceExecutablePath
	Path to the managed ScreenBux Service's executable, used only for its first-time
	registration. Defaults to "<ServiceInstallDirectory>\ScreenBux.Service.exe".

.PARAMETER AgentInstallDirectory
	Where the managed Agent's files should be installed/updated.
	Defaults to "C:\Program Files\ScreenBux\Agent".

.PARAMETER AgentExecutablePath
	Path to the managed Agent's executable, used to relaunch it after an update.
	Defaults to "<AgentInstallDirectory>\ScreenBux.Agent.exe".

.PARAMETER CheckIntervalMinutes
	How often the Updater polls for new versions. Defaults to 60.

.PARAMETER Install
	Installs (or re-installs) the Updater service. This is the default action.

.PARAMETER Uninstall
	Stops and removes the Updater service and deletes its install directory. Does NOT touch
	the managed Service/Agent unless -RemoveManagedComponents is also specified.

.PARAMETER RemoveManagedComponents
	Only meaningful with -Uninstall. Also stops/unregisters the managed ScreenBux Service and
	stops/removes the managed Agent, since the Updater - not any package manager - is what
	installed them; without this switch, uninstalling the Updater would orphan them (still
	registered/running, with no supported way to remove them). This is destructive: it removes
	parental-control enforcement from the machine entirely. Omit it if you only want to remove
	the auto-update mechanism while leaving enforcement running as-is.

.EXAMPLE
	.\Install-Updater.ps1 -PublishedOutputPath .\publish\Updater -ServerBaseUrl https://screenbux-api.azurewebsites.net

.EXAMPLE
	.\Install-Updater.ps1 -Uninstall -RemoveManagedComponents
#>
[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
	[Parameter(ParameterSetName = 'Install')]
	[string]$PublishedOutputPath,

	[Parameter(ParameterSetName = 'Install')]
	[Parameter(ParameterSetName = 'Uninstall')]
	[string]$InstallDirectory = "$env:ProgramFiles\ScreenBux\Updater",

	[Parameter(ParameterSetName = 'Install')]
	[string]$ServerBaseUrl,

	[Parameter(ParameterSetName = 'Install')]
	[string]$ServiceInstallDirectory = "$env:ProgramFiles\ScreenBux\Service",

	[Parameter(ParameterSetName = 'Install')]
	[string]$ServiceExecutablePath,

	[Parameter(ParameterSetName = 'Install')]
	[string]$AgentInstallDirectory = "$env:ProgramFiles\ScreenBux\Agent",

	[Parameter(ParameterSetName = 'Install')]
	[string]$AgentExecutablePath,

	[Parameter(ParameterSetName = 'Install')]
	[int]$CheckIntervalMinutes = 60,

	[Parameter(ParameterSetName = 'Install')]
	[switch]$Install,

	[Parameter(ParameterSetName = 'Uninstall', Mandatory = $true)]
	[switch]$Uninstall,

	[Parameter(ParameterSetName = 'Uninstall')]
	[switch]$RemoveManagedComponents
)

$ErrorActionPreference = 'Stop'
$UpdaterServiceName = 'ScreenBux Updater'
$UpdaterExecutableName = 'ScreenBux.Updater.exe'

function Assert-Administrator {
	$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
	if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
		throw "This script must be run as Administrator (it registers/removes a Windows Service)."
	}
}

function Install-Updater {
	if (-not $PublishedOutputPath) {
		throw "-PublishedOutputPath is required for install. Run 'dotnet publish src/ScreenBux.Updater -c Release -o <path>' first."
	}
	if (-not $ServerBaseUrl) {
		throw "-ServerBaseUrl is required for install (e.g. https://screenbux-api.azurewebsites.net)."
	}
	if (-not (Test-Path $PublishedOutputPath)) {
		throw "PublishedOutputPath '$PublishedOutputPath' does not exist."
	}

	if (-not $ServiceExecutablePath) {
		$ServiceExecutablePath = Join-Path $ServiceInstallDirectory 'ScreenBux.Service.exe'
	}
	if (-not $AgentExecutablePath) {
		$AgentExecutablePath = Join-Path $AgentInstallDirectory 'ScreenBux.Agent.exe'
	}

	Write-Host "Stopping existing '$UpdaterServiceName' service (if present)..."
	$existingService = Get-Service -Name $UpdaterServiceName -ErrorAction SilentlyContinue
	if ($existingService -and $existingService.Status -ne 'Stopped') {
		Stop-Service -Name $UpdaterServiceName -Force
		$existingService.WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))
	}

	Write-Host "Copying published output to '$InstallDirectory'..."
	New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
	Copy-Item -Path (Join-Path $PublishedOutputPath '*') -Destination $InstallDirectory -Recurse -Force

	$appSettingsPath = Join-Path $InstallDirectory 'appsettings.Production.json'
	Write-Host "Writing configuration to '$appSettingsPath'..."
	$config = [ordered]@{
		ServerBaseUrl        = $ServerBaseUrl
		CheckIntervalMinutes = $CheckIntervalMinutes
		Service              = [ordered]@{
			InstallDirectory     = $ServiceInstallDirectory
			ExecutablePath       = $ServiceExecutablePath
			InstalledVersionFile = "$env:ProgramData\ScreenBux\service.version"
		}
		Agent                = [ordered]@{
			InstallDirectory     = $AgentInstallDirectory
			ExecutablePath       = $AgentExecutablePath
			InstalledVersionFile = "$env:ProgramData\ScreenBux\agent.version"
		}
	}
	$config | ConvertTo-Json -Depth 5 | Set-Content -Path $appSettingsPath -Encoding UTF8

	$exePath = Join-Path $InstallDirectory $UpdaterExecutableName
	if (-not (Test-Path $exePath)) {
		throw "Expected executable '$exePath' not found in published output."
	}

	if (-not $existingService) {
		Write-Host "Registering '$UpdaterServiceName' service (LocalSystem, auto-start)..."
		sc.exe create "$UpdaterServiceName" binPath= "`"$exePath`"" start= auto obj= LocalSystem | Out-Null
		if ($LASTEXITCODE -ne 0) {
			throw "sc.exe create failed with exit code $LASTEXITCODE."
		}
	}

	Write-Host "Starting '$UpdaterServiceName'..."
	Start-Service -Name $UpdaterServiceName

	Write-Host "Done. '$UpdaterServiceName' is installed and running, polling $ServerBaseUrl every $CheckIntervalMinutes minute(s)." -ForegroundColor Green
}

function Uninstall-Updater {
	$existingService = Get-Service -Name $UpdaterServiceName -ErrorAction SilentlyContinue

	if ($RemoveManagedComponents) {
		$exePath = Join-Path $InstallDirectory $UpdaterExecutableName
		if (Test-Path $exePath) {
			Write-Host "Removing managed Service and Agent via '$exePath --cleanup-managed-components'..." -ForegroundColor Yellow
			& $exePath --cleanup-managed-components
			if ($LASTEXITCODE -ne 0) {
				Write-Warning "Cleanup of managed Service/Agent reported a failure (exit code $LASTEXITCODE); check the console output above. Continuing with Updater removal."
			}
		}
		else {
			Write-Warning "Updater executable not found at '$exePath'; skipping managed Service/Agent removal. Remove them manually if needed."
		}
	}
	else {
		Write-Host "Leaving the managed ScreenBux Service and Agent in place (pass -RemoveManagedComponents to remove them too)." -ForegroundColor Yellow
	}

	if ($existingService) {
		Write-Host "Stopping and removing '$UpdaterServiceName' service..."
		if ($existingService.Status -ne 'Stopped') {
			Stop-Service -Name $UpdaterServiceName -Force
			$existingService.WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))
		}
		sc.exe delete "$UpdaterServiceName" | Out-Null
	}
	else {
		Write-Host "'$UpdaterServiceName' service is not installed; nothing to stop."
	}

	if (Test-Path $InstallDirectory) {
		Write-Host "Removing install directory '$InstallDirectory'..."
		Remove-Item -Path $InstallDirectory -Recurse -Force
	}

	Write-Host "Done." -ForegroundColor Green
}

Assert-Administrator

if ($Uninstall) {
	Uninstall-Updater
}
else {
	Install-Updater
}
