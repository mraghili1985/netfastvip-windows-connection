<#
.SYNOPSIS
    Universal Multi-Brand White-Label Builder for Windows VPN Client.

.DESCRIPTION
    Generates a fully customized, standalone, self-contained portable Windows VPN package (.zip)
    for any reseller or tenant brand with custom branding, app icon, manifest, network adapter name,
    portal endpoints, and launcher.

.PARAMETER BrandName
    The name of the brand/application (e.g. "StarVPN", "MoonNet"). Required.

.PARAMETER PortalUrl
    The base URL of the panel API (e.g. "https://panel.starvpn.com").

.PARAMETER PanelUrl
    The user-facing web portal login URL (e.g. "https://panel.starvpn.com/portal/login").

.PARAMETER TelegramUrl
    Support or channel link on Telegram (e.g. "https://t.me/starvpn").

.PARAMETER SupportUrl
    Direct support link (e.g. "https://t.me/starvpn_support").

.PARAMETER GithubUrl
    Optional repository URL.

.PARAMETER IconPath
    Local path or HTTP/HTTPS URL to a custom .ico file.

.PARAMETER BaseOvpnPath
    Local path or HTTP/HTTPS URL to a custom base.ovpn file.

.PARAMETER CaCertPath
    Local path or HTTP/HTTPS URL to a custom ca.crt certificate.

.PARAMETER Version
    Application version string (default: "3.0.2").

.PARAMETER OutputDir
    Destination folder for the resulting .zip file (default: "dist").

.PARAMETER KeepWorkDir
    Switch to keep intermediate build files for debugging.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-brand.ps1 -BrandName "StarVPN" -PortalUrl "https://panel.starvpn.com" -TelegramUrl "https://t.me/starvpn"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^[a-zA-Z0-9_\-]+$')]
    [string]$BrandName,

    [Parameter(Mandatory = $false)]
    [string]$PortalUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$PanelUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$TelegramUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$SupportUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$GithubUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$IconPath = "",

    [Parameter(Mandatory = $false)]
    [string]$BaseOvpnPath = "",

    [Parameter(Mandatory = $false)]
    [string]$CaCertPath = "",

    [Parameter(Mandatory = $false)]
    [string]$Version = "3.0.2",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "dist",

    [Parameter(Mandatory = $false)]
    [switch]$KeepWorkDir
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [White-Label Builder] Building Brand: $BrandName (v$Version)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Setup paths
$workDir = Join-Path $root "build-work\$BrandName"
$outDirAbs = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $root $OutputDir }
if (!(Test-Path $outDirAbs)) {
    New-Item -ItemType Directory -Path $outDirAbs -Force | Out-Null
}

if (Test-Path $workDir) {
    Write-Host "[-] Cleaning previous work directory..." -ForegroundColor Yellow
    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

# 2. Copy source files (excluding heavy build outputs, stage, and git)
Write-Host "[1/7] Copying source files to isolated workspace..." -ForegroundColor Green
$excludeItems = @('bin', 'obj', 'stage', 'dist', 'build-work', '.git', '.vs', '.antigravity', '*.zip')

Get-ChildItem -Path $root -Exclude $excludeItems | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $workDir -Recurse -Force
}

# 3. Patch SmartVpn.csproj with custom BrandName and AssemblyName
Write-Host "[2/7] Patching project identity in SmartVpn.csproj..." -ForegroundColor Green
$csprojPath = Join-Path $workDir "SmartVpn.csproj"
[string]$csprojContent = Get-Content $csprojPath -Raw -Encoding UTF8

$csprojContent = $csprojContent -replace '<AssemblyName>[^<]+</AssemblyName>', "<AssemblyName>$BrandName</AssemblyName>"
$csprojContent = $csprojContent -replace '<Product>[^<]+</Product>', "<Product>$BrandName VPN Client</Product>"
$csprojContent = $csprojContent -replace '<Company>[^<]+</Company>', "<Company>$BrandName</Company>"
$csprojContent = $csprojContent -replace '<Copyright>[^<]+</Copyright>', "<Copyright>Copyright (c) $BrandName</Copyright>"
$csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$csprojContent = $csprojContent -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
$csprojContent = $csprojContent -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$Version</InformationalVersion>"

Set-Content -Path $csprojPath -Value $csprojContent -Encoding UTF8

# 4. Patch app.manifest
Write-Host "[3/7] Patching app.manifest..." -ForegroundColor Green
$manifestPath = Join-Path $workDir "app.manifest"
if (Test-Path $manifestPath) {
    [string]$manifestContent = Get-Content $manifestPath -Raw -Encoding UTF8
    $manifestContent = $manifestContent -replace 'name="[^"]+\.app"', "name=`"$BrandName.app`""
    Set-Content -Path $manifestPath -Value $manifestContent -Encoding UTF8
}

# 5. Patch config.json
Write-Host "[4/7] Updating brand configuration (config.json)..." -ForegroundColor Green
$configJsonPath = Join-Path $workDir "config.json"
$cfg = @{}
if (Test-Path $configJsonPath) {
    try {
        $cfg = (Get-Content $configJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json -AsHashtable
    } catch {
        $cfg = @{}
    }
}

$cfg["name"] = $BrandName
$cfg["AppName"] = $BrandName
$cfg["AppVersion"] = $Version

if ($PortalUrl.Trim().Length -gt 0) {
    $cleanPortal = $PortalUrl.Trim().TrimEnd('/')
    $cfg["PortalApiUrl"] = "$cleanPortal/"
    if ($PanelUrl.Trim().Length -eq 0) {
        $PanelUrl = "$cleanPortal/portal/login"
    }
}
if ($PanelUrl.Trim().Length -gt 0) {
    $cfg["PanelUrl"] = $PanelUrl.Trim()
}
if ($TelegramUrl.Trim().Length -gt 0) {
    $cfg["TelegramUrl"] = $TelegramUrl.Trim()
}
if ($SupportUrl.Trim().Length -gt 0) {
    $cfg["SupportUrl"] = $SupportUrl.Trim()
}
if ($GithubUrl.Trim().Length -gt 0) {
    $cfg["GithubUrl"] = $GithubUrl.Trim()
}

$cfgJsonString = $cfg | ConvertTo-Json -Depth 10
Set-Content -Path $configJsonPath -Value $cfgJsonString -Encoding UTF8

# 6. Apply custom assets if supplied (Icon, base.ovpn, ca.crt)
if ($IconPath.Trim().Length -gt 0) {
    Write-Host "[-] Downloading/applying custom icon..." -ForegroundColor Yellow
    $destIcon = Join-Path $workDir "app.ico"
    if ($IconPath -match '^https?://') {
        Invoke-WebRequest -Uri $IconPath -OutFile $destIcon -UseBasicParsing
    } elseif (Test-Path $IconPath) {
        Copy-Item -Path $IconPath -Destination $destIcon -Force
    }
}

if ($BaseOvpnPath.Trim().Length -gt 0) {
    Write-Host "[-] Downloading/applying custom base.ovpn..." -ForegroundColor Yellow
    $destOvpn = Join-Path $workDir "base.ovpn"
    if ($BaseOvpnPath -match '^https?://') {
        Invoke-WebRequest -Uri $BaseOvpnPath -OutFile $destOvpn -UseBasicParsing
    } elseif (Test-Path $BaseOvpnPath) {
        Copy-Item -Path $BaseOvpnPath -Destination $destOvpn -Force
    }
}

if ($CaCertPath.Trim().Length -gt 0) {
    Write-Host "[-] Downloading/applying custom ca.crt..." -ForegroundColor Yellow
    $destCa = Join-Path $workDir "ca.crt"
    if ($CaCertPath -match '^https?://') {
        Invoke-WebRequest -Uri $CaCertPath -OutFile $destCa -UseBasicParsing
    } elseif (Test-Path $CaCertPath) {
        Copy-Item -Path $CaCertPath -Destination $destCa -Force
    }
}

# 7. Compile Release win-x64
Write-Host "[5/7] Compiling standalone package (dotnet publish -c Release -r win-x64)..." -ForegroundColor Green
$pubDir = Join-Path $workDir "publish"
$buildLog = Join-Path $workDir "publish.log"

$publishProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "publish `"$csprojPath`" -c Release -r win-x64 --self-contained true -o `"$pubDir`"" `
    -NoNewWindow -Wait -PassThru -RedirectStandardOutput $buildLog -RedirectStandardError (Join-Path $workDir "publish-err.log")

if ($publishProc.ExitCode -ne 0) {
    Write-Host "ERROR: dotnet publish failed with code $($publishProc.ExitCode)." -ForegroundColor Red
    if (Test-Path $buildLog) { Get-Content $buildLog -Tail 30 }
    exit $publishProc.ExitCode
}

# Ensure core tunnel engines are placed in Data\
Write-Host "[6/7] Assembling portable layout and dependencies..." -ForegroundColor Green
$pubData = Join-Path $pubDir "Data"
if (!(Test-Path $pubData)) { New-Item -ItemType Directory -Path $pubData -Force | Out-Null }

@('openvpn', 'wireguard', 'sing-box') | ForEach-Object {
    $srcTool = Join-Path $root $_
    if (Test-Path $srcTool) {
        $destTool = Join-Path $pubData $_
        Copy-Item -Path $srcTool -Destination $destTool -Recurse -Force
    }
}

# Stage structure:
# Root:
#   [BrandName].vbs (Launcher)
#   README.txt
#   App\ (Binaries, dlls, Data)
$stageDir = Join-Path $workDir "stage"
$stageApp = Join-Path $stageDir "App"
New-Item -ItemType Directory -Path $stageApp -Force | Out-Null

Copy-Item -Path "$pubDir\*" -Destination $stageApp -Recurse -Force

# Create customized launcher
$vbsPath = Join-Path $stageDir "$BrandName.vbs"
$vbsContent = "Dim fso, shell, scriptDir, exePath`r`nSet fso = CreateObject(""Scripting.FileSystemObject"")`r`nSet shell = CreateObject(""WScript.Shell"")`r`nscriptDir = fso.GetParentFolderName(WScript.ScriptFullName)`r`nexePath = scriptDir & ""\App\$BrandName.exe""`r`nIf fso.FileExists(exePath) Then`r`n    shell.Run """""" & exePath & """""", 1, False`r`nElse`r`n    MsgBox ""$BrandName.exe not found:"" & vbCrLf & exePath, vbExclamation, ""$BrandName""`r`nEnd If`r`n"
Set-Content -Path $vbsPath -Value $vbsContent -Encoding ASCII

# Create customized README.txt
$readmePath = Join-Path $stageDir "README.txt"
$readmeLines = @(
    "$BrandName - Portable VPN Client",
    "========================================",
    "",
    "How to run:",
    "   Double-click '$BrandName.vbs' to launch the client silently without a console window.",
    "   Or navigate to the 'App' directory and run '$BrandName.exe' directly.",
    "",
    "Security Note (Antivirus / Windows Defender):",
    "   As this is a portable release, Windows SmartScreen may show a prompt on first run.",
    "   Click 'More info' and then 'Run anyway', or add the folder to your antivirus exclusions.",
    "",
    "Support and Links:"
)
if ($TelegramUrl.Trim().Length -gt 0) { $readmeLines += "   Telegram Channel: $TelegramUrl" }
if ($SupportUrl.Trim().Length -gt 0) { $readmeLines += "   Support: $SupportUrl" }
$readmeContent = $readmeLines -join "`r`n"
Set-Content -Path $readmePath -Value $readmeContent -Encoding ASCII
Copy-Item -Path $readmePath -Destination (Join-Path $stageApp "README.txt") -Force

# 8. Create ZIP archive
Write-Host "[7/7] Compressing into portable ZIP..." -ForegroundColor Green
$targetZip = Join-Path $outDirAbs "$BrandName-Windows-v$Version.zip"
if (Test-Path $targetZip) { Remove-Item -Path $targetZip -Force }

$zipScript = Join-Path $root "zip-with-retry.ps1"
if (Test-Path $zipScript) {
    & $zipScript -StagePath $stageDir -ZipPath $targetZip
} else {
    Compress-Archive -Path "$stageDir\*" -DestinationPath $targetZip -Force
}

# 9. Cleanup
if (!$KeepWorkDir) {
    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

$zipItem = Get-Item $targetZip
$zipHash = (Get-FileHash -Path $targetZip -Algorithm SHA256).Hash

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [SUCCESS] White-Label Build Completed!" -ForegroundColor Green
Write-Host " Output File : $($zipItem.FullName)" -ForegroundColor White
Write-Host " File Size   : $([math]::Round($zipItem.Length / 1MB, 2)) MB" -ForegroundColor White
Write-Host " SHA-256     : $zipHash" -ForegroundColor DarkGray
Write-Host " Launcher    : $BrandName.vbs -> App\$BrandName.exe" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan
