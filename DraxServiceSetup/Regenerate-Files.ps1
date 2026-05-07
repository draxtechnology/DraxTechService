# Regenerates Files.wxs from the contents of Drax360NetworkManager\bin\Debug.
# Run this whenever a NuGet dependency is added/removed/upgraded so the installer
# stays in sync. Build the service first (dotnet build) so bin\Debug is populated.
#
#   pwsh DraxServiceSetup\Regenerate-Files.ps1

Set-Location $PSScriptRoot
$bin = Join-Path $PSScriptRoot '..\Drax360NetworkManager\bin\Debug'
if (-not (Test-Path $bin)) { throw "$bin not found - run dotnet build first." }

$rootFiles = Get-ChildItem $bin -File | Where-Object {
  $_.Name -ne 'DraxTechnology.exe' -and
  $_.Name -ne 'DraxTechnology.exe.config' -and
  $_.Name -ne 'DraxTechnology.pdb'
} | Sort-Object Name

$iniFiles = Get-ChildItem (Join-Path $bin 'ini') -File -ErrorAction SilentlyContinue | Sort-Object Name

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
[void]$sb.AppendLine('    <ComponentGroup Id="DependencyFiles" Directory="INSTALLFOLDER">')
foreach ($f in $rootFiles) {
  $safe = Sanitize $f.Name
  $guid = StableGuid("DraxSetup:$($f.Name)")
  [void]$sb.AppendLine("      <Component Id=`"cmp_$safe`" Guid=`"$guid`">")
  [void]$sb.AppendLine("        <File Id=`"fil_$safe`" Source=`"`$(var.Drax360Service.TargetDir)$($f.Name)`" KeyPath=`"yes`" />")
  [void]$sb.AppendLine('      </Component>')
}
[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine()
[void]$sb.AppendLine('    <ComponentGroup Id="IniFiles" Directory="IniFolder">')
foreach ($f in $iniFiles) {
  $safe = Sanitize $f.Name
  $guid = StableGuid("DraxSetup:ini\$($f.Name)")
  [void]$sb.AppendLine("      <Component Id=`"cmp_ini_$safe`" Guid=`"$guid`">")
  [void]$sb.AppendLine("        <File Id=`"fil_ini_$safe`" Source=`"`$(var.Drax360Service.TargetDir)ini\$($f.Name)`" KeyPath=`"yes`" />")
  [void]$sb.AppendLine('      </Component>')
}
[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

Set-Content -Path (Join-Path $PSScriptRoot 'Files.wxs') -Value $sb.ToString() -Encoding UTF8 -NoNewline
"Wrote Files.wxs: deps=$($rootFiles.Count) ini=$($iniFiles.Count)"
