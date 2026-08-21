# Regenerates Files.wxs from the contents of Drax360NetworkManager\bin\Debug.
# Run this whenever a NuGet dependency is added/removed/upgraded so the installer
# stays in sync. Build the service first (dotnet build) so bin\Debug is populated.
#
#   pwsh DraxServiceSetup\Regenerate-Files.ps1
#
# Emits one full copy of DependencyFiles/WinRuntimeFiles per side-by-side
# instance (1..4) — each instance is its own complete install under
# C:\AMX1\NWM{n} with its own DraxTechnology.dll.config, so the same physical
# binaries need a distinct Component/File per destination folder. The ini
# templates are identical across instances, so they get ONE shared
# ComponentGroup installed once to the shared C:\AMX1\ini folder instead of
# being duplicated per instance. See Product.wxs for the matching
# INSTALLFOLDER{n}/IniFolder/WinRuntimeDir{n} directory tree.

Set-Location $PSScriptRoot
$bin = Join-Path $PSScriptRoot '..\Drax360NetworkManager\bin\Debug'
if (-not (Test-Path $bin)) { throw "$bin not found - run dotnet build first." }

# Exclude files declared explicitly in Product.wxs (the apphost EXE) and pdb
# symbols. On .NET (Core) the managed DLL, .dll.config, .deps.json and
# .runtimeconfig.json all flow through DependencyFiles automatically.
$rootFiles = Get-ChildItem $bin -File | Where-Object {
  $_.Name -ne 'DraxTechnology.exe' -and
  $_.Name -ne 'DraxTechnology.pdb'
} | Sort-Object Name

$iniFiles = Get-ChildItem (Join-Path $bin 'ini') -File -ErrorAction SilentlyContinue | Sort-Object Name
# An empty ini set silently produced an MSI with no templates once already
# (fresh installs then fail firstruncheck with "Error No Ini Files Copied").
# The csproj Never-copy accident of 2026-08-18 nearly repeated it - fail hard.
if (-not $iniFiles -or @($iniFiles).Count -eq 0) {
  throw "No ini templates found in $bin\ini - an MSI built from this would break every fresh install. Check the csproj Content items copy ini\*.ini to the output."
}

# NuGet packages like System.ServiceProcess.ServiceController ship Windows-specific
# DLLs under runtimes\win\lib\net10.0\. The .NET runtime resolves these via the
# runtimeTargets in deps.json, so they must be installed into the matching subfolder
# (C:\AMX{n}\runtimes\win\lib\net10.0\) — not just the app root.
$winRuntimeDir = Join-Path $bin 'runtimes\win\lib\net10.0'
$winRuntimeFiles = if (Test-Path $winRuntimeDir) {
  Get-ChildItem $winRuntimeDir -File | Sort-Object Name
} else { @() }

function Sanitize($name) { ($name -replace '[^A-Za-z0-9_]', '_') }
function StableGuid($s) {
  $sha = [System.Security.Cryptography.SHA1]::Create()
  $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($s))
  $g = [byte[]]::new(16)
  [Array]::Copy($hash, 0, $g, 0, 16)
  ([System.Guid]::new($g)).ToString().ToUpper()
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')

[void]$sb.AppendLine("    <ComponentGroup Id=`"IniFiles`" Directory=`"IniFolder`">")
foreach ($f in $iniFiles) {
  $safe = Sanitize $f.Name
  $guid = StableGuid("DraxSetup:shared:ini\$($f.Name)")
  [void]$sb.AppendLine("      <Component Id=`"cmp_ini_$safe`" Guid=`"$guid`">")
  [void]$sb.AppendLine("        <File Id=`"fil_ini_$safe`" Source=`"`$(var.Drax360Service.TargetDir)ini\$($f.Name)`" KeyPath=`"yes`" />")
  [void]$sb.AppendLine('      </Component>')
}
[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine()

foreach ($instance in 1..4) {
  [void]$sb.AppendLine("    <ComponentGroup Id=`"DependencyFiles$instance`" Directory=`"INSTALLFOLDER$instance`">")
  foreach ($f in $rootFiles) {
    $safe = Sanitize $f.Name
    $guid = StableGuid("DraxSetup:$instance`:$($f.Name)")
    [void]$sb.AppendLine("      <Component Id=`"cmp_$($safe)_$instance`" Guid=`"$guid`">")
    [void]$sb.AppendLine("        <File Id=`"fil_$($safe)_$instance`" Source=`"`$(var.Drax360Service.TargetDir)$($f.Name)`" KeyPath=`"yes`" />")
    [void]$sb.AppendLine('      </Component>')
  }
  [void]$sb.AppendLine('    </ComponentGroup>')
  [void]$sb.AppendLine()
  [void]$sb.AppendLine("    <ComponentGroup Id=`"WinRuntimeFiles$instance`" Directory=`"WinRuntimeDir$instance`">")
  foreach ($f in $winRuntimeFiles) {
    $safe = Sanitize $f.Name
    $guid = StableGuid("DraxSetup:$instance`:runtimes\win\lib\net10.0\$($f.Name)")
    [void]$sb.AppendLine("      <Component Id=`"cmp_winrt_$($safe)_$instance`" Guid=`"$guid`">")
    [void]$sb.AppendLine("        <File Id=`"fil_winrt_$($safe)_$instance`" Source=`"`$(var.Drax360Service.TargetDir)runtimes\win\lib\net10.0\$($f.Name)`" KeyPath=`"yes`" />")
    [void]$sb.AppendLine('      </Component>')
  }
  [void]$sb.AppendLine('    </ComponentGroup>')
  [void]$sb.AppendLine()
}

[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

Set-Content -Path (Join-Path $PSScriptRoot 'Files.wxs') -Value $sb.ToString() -Encoding UTF8 -NoNewline
"Wrote Files.wxs: deps=$($rootFiles.Count) ini=$($iniFiles.Count) winrt=$($winRuntimeFiles.Count) x4 instances"
