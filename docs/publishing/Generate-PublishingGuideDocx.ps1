<#
.SYNOPSIS
	Generates docs/publishing/ScreenBux-Distributed-Publishing-Guide.docx from scratch using
	minimal OpenXML (WordprocessingML), so no external tools (Word, pandoc, etc.) are required.

.DESCRIPTION
	Content is defined as a simple array of "blocks" below (heading1/heading2/heading3/normal/
	bullet/code), which is rendered into a valid .docx package (a zip with the standard OPC
	parts: [Content_Types].xml, _rels/.rels, word/document.xml, word/_rels/document.xml.rels,
	word/styles.xml). Re-run this script any time the content changes to regenerate the docx.
#>

$ErrorActionPreference = 'Stop'

$outputPath = Join-Path $PSScriptRoot 'ScreenBux-Distributed-Publishing-Guide.docx'

function New-Block([string]$Type, [string]$Text) {
	[PSCustomObject]@{ Type = $Type; Text = $Text }
}

$blocks = @(
	(New-Block h1 'ScreenBux - Distributed Publishing & Test Guide')
	(New-Block normal 'Use this document as a working checklist while standing up a full distributed test of ScreenBux (WebServer, WebClient, Service, Agent, and optionally the Updater). Add your own notes inline as you go - this is a Word document specifically so you can annotate it.')

	(New-Block h2 '0. Prerequisites')
	(New-Block bullet 'SQL Server reachable from the WebServer machine: LocalDB (single-machine only), a local SQL Server instance, or Azure SQL (required if components run on separate machines).')
	(New-Block bullet '.NET 10 SDK on every machine that will build/publish a component. The Service and Agent require Windows (P/Invoke, Windows Service, WPF).')
	(New-Block bullet 'If testing across separate physical/virtual machines, note each machine''s reachable hostname or IP now: you will need the WebServer''s address for the Service''s ServerBaseUrl, the WebClient''s PolicyApiBaseUrl/MonitoringHubUrl, and the Agent''s target Service.')
	(New-Block bullet 'Decide up front: single-machine loopback test (everything on localhost, simplest) vs. multi-machine test (closer to production, requires real hostnames/ports and firewall rules).')

	(New-Block h2 '1. Prepare the database')
	(New-Block normal 'EF Core migrations live in ScreenBux.Data/Migrations and are applied against whatever connection string ScreenBux.WebServer resolves at runtime (appsettings.json / appsettings.{Environment}.json / user-secrets). The design-time factory (AppDbContextFactory) was updated to read that same configuration, so dotnet ef now targets the same database the WebServer actually uses - no more silent mismatches.')
	(New-Block code 'dotnet ef database update -p src\ScreenBux.Data -s src\ScreenBux.WebServer')
	(New-Block normal 'Sanity check which database/server this resolves to before applying:')
	(New-Block code 'dotnet ef dbcontext info -p src\ScreenBux.Data -s src\ScreenBux.WebServer')
	(New-Block bullet 'ASPNETCORE_ENVIRONMENT defaults to Development if unset (matching dotnet ef''s normal behavior). Set $env:ASPNETCORE_ENVIRONMENT="Production" first if you need to target production settings instead.')
	(New-Block bullet 'Set the real connection string via appsettings.Development.json (dev) or `dotnet user-secrets set "ConnectionStrings:AppDb" "..."` (recommended over committing secrets to appsettings.json).')

	(New-Block h2 '2. Configure & publish the WebServer')
	(New-Block normal 'Set secrets from within src\ScreenBux.WebServer (do not commit these):')
	(New-Block code 'dotnet user-secrets set "ConnectionStrings:AppDb" "Server=...;Initial Catalog=ScreenBux;..."' + [Environment]::NewLine + 'dotnet user-secrets set "Jwt:SigningKey" "<a long random string>"')
	(New-Block bullet 'Check the CORS allow-list in ScreenBux.WebServer/Program.cs - it is currently a fixed localhost allow-list. Add the WebClient''s real origin (including its Azure hostname once deployed) before testing cross-machine/cross-origin.')
	(New-Block normal 'Publish:')
	(New-Block code 'dotnet publish src\ScreenBux.WebServer -c Release -o .\publish\WebServer')
	(New-Block normal 'Run standalone first to verify (before wiring as a Windows Service/IIS site/App Service). Note: this --urls port is an arbitrary local choice for a bare Kestrel process - it has nothing to do with Azure App Service, which always exposes 443/80 externally regardless of port choice here.')
	(New-Block code 'cd .\publish\WebServer' + [Environment]::NewLine + 'dotnet ScreenBux.WebServer.dll --urls "https://0.0.0.0:7000"')
	(New-Block normal 'For local dev via `dotnet run`/Visual Studio F5 (not a bare published .dll), the actual configured port comes from launchSettings.json instead: WebServer HTTPS profile is https://localhost:44323 (HTTP 5246).')

	(New-Block h2 '3. Configure & publish the WebClient')
	(New-Block normal 'Point the WebClient at the real WebServer address in appsettings.json (or an environment-specific override):')
	(New-Block code '"MonitoringHubUrl": "https://<webserver-host>:7000/monitoringHub",' + [Environment]::NewLine + '"PolicyApiBaseUrl": "https://<webserver-host>:7000"')
	(New-Block normal 'Publish and run:')
	(New-Block code 'dotnet publish src\ScreenBux.WebClient -c Release -o .\publish\WebClient' + [Environment]::NewLine + 'cd .\publish\WebClient' + [Environment]::NewLine + 'dotnet ScreenBux.WebClient.dll --urls "https://0.0.0.0:5001"')
	(New-Block normal 'Local dev via `dotnet run`/F5 instead uses launchSettings.json: WebClient HTTPS profile is https://localhost:7123 (HTTP 5239).')
	(New-Block normal 'Browse to the WebClient, register an account through the UI, and log in.')

	(New-Block h2 '4. Generate a device link code')
	(New-Block normal 'While logged into the WebClient, use the "Link Device" page - it calls POST api/devices/linkcode, returning a short-lived (15 minute) code. Have it ready for the next step.')

	(New-Block h2 '5. Configure & publish the Service')
	(New-Block normal 'Point ServerBaseUrl and MonitoringHubUrl at the real WebServer address (not localhost if it is a different machine) in src\ScreenBux.Service\appsettings.json:')
	(New-Block code '"ServerBaseUrl": "https://<webserver-host>:7000",' + [Environment]::NewLine + '"MonitoringHubUrl": "https://<webserver-host>:7000/monitoringHub"')
	(New-Block normal 'Publish:')
	(New-Block code 'dotnet publish src\ScreenBux.Service -c Release -o .\publish\Service')
	(New-Block normal 'Run it once interactively (elevated) to redeem the link code and confirm it creates device.json + policy.json:')
	(New-Block code 'cd .\publish\Service' + [Environment]::NewLine + '.\ScreenBux.Service.exe --link-code <code-from-step-4>')
	(New-Block bullet 'Confirm the exact link-code mechanism against DeviceIdentityService/Program.cs (CLI flag vs. config value vs. first-run prompt) before relying on the command above verbatim.')
	(New-Block normal 'Once linked, install it as a genuine Windows Service so it survives reboots (see INSTALLATION.md section 1), e.g.:')
	(New-Block code 'sc create ScreenBuxService binPath="C:\path\to\ScreenBux.Service.exe" start=auto' + [Environment]::NewLine + 'sc start ScreenBuxService')

	(New-Block h2 '6. Configure & publish the Agent')
	(New-Block normal 'Set the Service''s expected Agent:ExecutablePath in its appsettings.json to match where you will install the Agent, e.g. C:\Program Files\ScreenBux\Agent\ScreenBux.Agent.exe. Publish directly to that path:')
	(New-Block code 'dotnet publish src\ScreenBux.Agent -c Release -o "C:\Program Files\ScreenBux\Agent"')
	(New-Block normal 'Add a shortcut to shell:startup, or launch manually the first time to confirm it connects to the Service over the named pipe (ScreenBuxServicePipe).')

	(New-Block h2 '7. (Optional) Updater / auto-update path')
	(New-Block normal 'To exercise the auto-update pipeline end-to-end: publish the Updater, run installer\Install-Updater.ps1 (or build/install the WiX MSI under installer\ScreenBux.Updater.Installer), and populate the Updates:Service / Updates:Agent Version + DownloadUrl entries in the WebServer''s appsettings.json so GET api/updates/latest returns something installable.')
	(New-Block code 'dotnet publish src\ScreenBux.Updater -c Release -o .\publish\Updater' + [Environment]::NewLine + '.\installer\Install-Updater.ps1 -PublishedOutputPath .\publish\Updater -ServerBaseUrl https://<webserver-host>')
	(New-Block bullet 'The Updater now stops the Service (and the AgentWatchdogService it hosts) before touching any Agent files, and restarts it afterward - this prevents the watchdog from racing an in-progress Agent update.')

	(New-Block h2 '8. Verification checklist')
	(New-Block bullet 'WebClient shows the device as "linked" after the Service redeems the code.')
	(New-Block bullet 'Foreground window changes on the Agent''s machine show up live in the WebClient''s Monitoring page (via SignalR / MonitoringHub).')
	(New-Block bullet 'Editing policy in the WebClient''s Policy page pushes a PolicyUpdated SignalR event, and the Service''s local policy.json updates within a few seconds.')
	(New-Block bullet 'Enforcement: add a PolicyRule blocking a test app by process name, launch it on the Agent machine, confirm the Service closes it.')
	(New-Block bullet 'Device time grants (if testing that feature): confirm GrantUpdated pushes correctly pause enforcement until the grant expires.')

	(New-Block h2 '9. Publishing to Azure App Service')
	(New-Block normal 'Use two separate App Services - one for ScreenBux.WebServer (API + SignalR hub), one for ScreenBux.WebClient (Blazor Server UI). They are architecturally independent processes talking over HTTPS/SignalR, exactly like in local dev, and benefit from independent scaling/restart/deploy cycles.')
	(New-Block bullet 'App Service always exposes standard web ports externally: 443 (HTTPS) and 80 (HTTP, redirects to HTTPS). You do not choose a port for Azure deployment - ASPNETCORE_URLS / internal port negotiation is handled automatically by the platform (IIS+ANCM on Windows plans, or the container/Linux default of 8080 unless overridden via WEBSITES_PORT).')
	(New-Block bullet 'Azure requires globally-unique *.azurewebsites.net hostnames. If your desired name is taken, the portal auto-appends a random suffix instead of failing. To get a deterministic name every time, create the App Service via `az webapp create --name <exact-name>` (fails outright on conflict rather than silently renaming) - recommended for repeatable IaC/CI deployments of both App Services.')
	(New-Block bullet 'Optionally bind a custom domain (App Service > Custom domains) plus a free App Service Managed Certificate, and use that domain in config instead of the raw azurewebsites.net hostname.')
	(New-Block normal 'After deployment, update configuration to use the Azure HTTPS URLs (no port suffix):')
	(New-Block bullet 'WebServer: CORS allow-list must include the WebClient''s Azure hostname (or custom domain).')
	(New-Block bullet 'WebClient: PolicyApiBaseUrl / MonitoringHubUrl -> WebServer''s Azure HTTPS URL.')
	(New-Block bullet 'Service: ServerBaseUrl / MonitoringHubUrl -> WebServer''s Azure HTTPS URL.')
	(New-Block bullet 'WebServer secrets (ConnectionStrings:AppDb, Jwt:SigningKey) -> set as App Service Application Settings or Key Vault references, never committed to appsettings.json.')

	(New-Block h2 'Notes')
	(New-Block normal '(Use this space to jot down anything you discover while going through the process - actual link-code CLI syntax, real Azure hostnames once created, issues hit along the way, etc.)')
)

# ---- Minimal OpenXML (.docx) generation ----

function Escape-Xml([string]$text) {
	if ($null -eq $text) { return '' }
	return $text -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'
}

$bodyXmlParts = New-Object System.Collections.Generic.List[string]

foreach ($b in $blocks) {
	$escaped = Escape-Xml $b.Text
	switch ($b.Type) {
		'h1' {
			$bodyXmlParts.Add("<w:p><w:pPr><w:pStyle w:val=`"Heading1`"/></w:pPr><w:r><w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:p>")
		}
		'h2' {
			$bodyXmlParts.Add("<w:p><w:pPr><w:pStyle w:val=`"Heading2`"/></w:pPr><w:r><w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:p>")
		}
		'bullet' {
			$bodyXmlParts.Add("<w:p><w:pPr><w:pStyle w:val=`"ListParagraph`"/><w:numPr><w:ilvl w:val=`"0`"/><w:numId w:val=`"1`"/></w:numPr></w:pPr><w:r><w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:p>")
		}
		'code' {
			$lines = $b.Text -split "`r?`n"
			foreach ($line in $lines) {
				$lineEsc = Escape-Xml $line
				$bodyXmlParts.Add("<w:p><w:pPr><w:pStyle w:val=`"Code`"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii=`"Consolas`" w:hAnsi=`"Consolas`"/></w:rPr><w:t xml:space=`"preserve`">$lineEsc</w:t></w:r></w:p>")
			}
		}
		default {
			$bodyXmlParts.Add("<w:p><w:r><w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:p>")
		}
	}
}

$bodyXml = [string]::Join('', $bodyXmlParts)

$documentXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
	$bodyXml
	<w:sectPr>
	  <w:pgSz w:w="12240" w:h="15840"/>
	  <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
	</w:sectPr>
  </w:body>
</w:document>
"@

$stylesXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:docDefaults>
	<w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="22"/></w:rPr></w:rPrDefault>
  </w:docDefaults>
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
	<w:name w:val="Normal"/>
	<w:pPr><w:spacing w:after="160" w:line="276" w:lineRule="auto"/></w:pPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading1">
	<w:name w:val="heading 1"/>
	<w:basedOn w:val="Normal"/>
	<w:pPr><w:spacing w:before="240" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr>
	<w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:b/><w:sz w:val="36"/><w:color w:val="2E74B5"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading2">
	<w:name w:val="heading 2"/>
	<w:basedOn w:val="Normal"/>
	<w:pPr><w:spacing w:before="200" w:after="100"/><w:outlineLvl w:val="1"/></w:pPr>
	<w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:b/><w:sz w:val="28"/><w:color w:val="1F4E79"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="ListParagraph">
	<w:name w:val="List Paragraph"/>
	<w:basedOn w:val="Normal"/>
	<w:pPr><w:ind w:left="720"/><w:contextualSpacing/></w:pPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Code">
	<w:name w:val="Code"/>
	<w:basedOn w:val="Normal"/>
	<w:pPr><w:shd w:val="clear" w:color="auto" w:fill="F2F2F2"/><w:spacing w:after="0"/></w:pPr>
	<w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/><w:sz w:val="20"/></w:rPr>
  </w:style>
</w:styles>
"@

$numberingXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:abstractNum w:abstractNumId="0">
	<w:lvl w:ilvl="0">
	  <w:numFmt w:val="bullet"/>
	  <w:lvlText w:val="&#8226;"/>
	  <w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr>
	  <w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol" w:hint="default"/></w:rPr>
	</w:lvl>
  </w:abstractNum>
  <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
</w:numbering>
"@

$contentTypesXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>
"@

$rootRelsXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>
"@

$documentRelsXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
</Relationships>
"@

$coreXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:title>ScreenBux - Distributed Publishing &amp; Test Guide</dc:title>
  <dc:creator>GitHub Copilot</dc:creator>
  <cp:lastModifiedBy>GitHub Copilot</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">$(Get-Date -Format o)</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">$(Get-Date -Format o)</dcterms:modified>
</cp:coreProperties>
"@

$appXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
  <Application>ScreenBux Docs Generator</Application>
</Properties>
"@

if (Test-Path $outputPath) { Remove-Item $outputPath -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$fs = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::CreateNew)
$zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)

function Add-ZipEntry($archive, [string]$entryName, [string]$content) {
	$entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
	$stream = $entry.Open()
	$writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
	$writer.Write($content)
	$writer.Flush()
	$writer.Close()
	$stream.Close()
}

Add-ZipEntry $zip '[Content_Types].xml' $contentTypesXml
Add-ZipEntry $zip '_rels/.rels' $rootRelsXml
Add-ZipEntry $zip 'docProps/core.xml' $coreXml
Add-ZipEntry $zip 'docProps/app.xml' $appXml
Add-ZipEntry $zip 'word/document.xml' $documentXml
Add-ZipEntry $zip 'word/styles.xml' $stylesXml
Add-ZipEntry $zip 'word/numbering.xml' $numberingXml
Add-ZipEntry $zip 'word/_rels/document.xml.rels' $documentRelsXml

$zip.Dispose()
$fs.Dispose()

Write-Host "Generated: $outputPath"
