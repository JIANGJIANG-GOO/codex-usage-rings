$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
$binaryDirectory = Join-Path $packageRoot 'bin'
New-Item -ItemType Directory -Force -Path $binaryDirectory | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $packageRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll', (Join-Path $frameworkPath 'WPF\UIAutomationClient.dll'), (Join-Path $frameworkPath 'WPF\UIAutomationTypes.dll'), (Join-Path $frameworkPath 'WPF\WindowsBase.dll'))
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/win32manifest:' + (Join-Path $packageRoot 'app.manifest')),('/out:' + (Join-Path $binaryDirectory 'CodexUsageRings.exe')))
$compilerArgs += @($references | ForEach-Object { '/r:' + $_ })
& $compilerPath @compilerArgs @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Built bin\CodexUsageRings.exe'
