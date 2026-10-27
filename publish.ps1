<#
.SYNOPSIS
    Builds PowerPen.exe: a single file that runs on any 64-bit Windows 10/11 PC without installing .NET.

.DESCRIPTION
    Creates, in the output folder:
      PowerPen.exe              the whole app in one file
      README.md                 how to use it
      LICENSE                   the MIT license, which must ship with every copy
      PowerPen-<version>.zip    both of the above, ready to attach to a GitHub release

    Run from the repository folder:
      powershell -ExecutionPolicy Bypass -File publish.ps1
#>
param(
    # Folder to put the results in (deleted and recreated each time).
    [string]$Output = 'publish'
)

$ErrorActionPreference = 'Stop'
$outputFolder = Join-Path $PSScriptRoot $Output
if (Test-Path $outputFolder) { Remove-Item $outputFolder -Recurse -Force }

# Self-contained: the .NET runtime is included, so users don't need to install anything.
# Single file + native libraries inside: one PowerPen.exe instead of a folder of DLLs.
# Compression: roughly halves the size, at the cost of a slightly slower first start.
# No debug symbols: nothing but the exe in the output.
dotnet publish (Join-Path $PSScriptRoot 'src\PowerPen\PowerPen.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $outputFolder
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE). See the messages above." }

Copy-Item (Join-Path $PSScriptRoot 'README.md'), (Join-Path $PSScriptRoot 'LICENSE') $outputFolder

# The product version may carry a "+commit" suffix from the build; the file name only needs the number.
$exe = Join-Path $outputFolder 'PowerPen.exe'
$version = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''
$zip = Join-Path $outputFolder "PowerPen-$version.zip"
Compress-Archive -Path $exe, (Join-Path $outputFolder 'README.md'), (Join-Path $outputFolder 'LICENSE') -DestinationPath $zip

Get-ChildItem $outputFolder | Format-Table Name, @{ Name = 'Size (MB)'; Expression = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
"Attach $zip to a GitHub release."
