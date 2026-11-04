param([string]$ReportPath)
$ErrorActionPreference = 'Stop'
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
$binaryDirectory = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $binaryDirectory | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$testFiles = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll', (Join-Path $frameworkPath 'WPF\UIAutomationClient.dll'), (Join-Path $frameworkPath 'WPF\UIAutomationTypes.dll'), (Join-Path $frameworkPath 'WPF\WindowsBase.dll'))
$testPath = Join-Path $binaryDirectory 'Tests.exe'
$compilerArgs = @('/nologo','/target:exe','/platform:x64','/main:AllTests',('/out:' + $testPath))
$compilerArgs += @($references | ForEach-Object { '/r:' + $_ })
& $compilerPath @compilerArgs @sourceFiles @testFiles
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
if ($ReportPath) { & $testPath $ReportPath } else { & $testPath }
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
