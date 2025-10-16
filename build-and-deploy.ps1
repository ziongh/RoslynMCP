#!/usr/bin/env pwsh
# RoslynMCP build and deploy script
# Usage: .\build-and-deploy.ps1

param(
    [string]$Configuration = "Release",
    [switch]$SelfContained = $false,
    [switch]$SingleFile = $false,
    [string]$Runtime = "win-x64"
)

Write-Host "🔨 Starting compilation of RoslynMCP..." -ForegroundColor Green

# Clean previous builds
Write-Host "🧹 Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean

# Build project
Write-Host "🔧 Building project ($Configuration configuration)..." -ForegroundColor Yellow
dotnet build -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Publish project
Write-Host "📦 Publishing project..." -ForegroundColor Yellow
# Create output directory
$outputDir = "release"
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Build publish arguments
$publishArgs = @(
    "src/RoslynMCP.MCP/RoslynMCP.MCP.csproj",
    "-c", $Configuration
)

$folderName = "framework-dependent"

if ($SelfContained) {
    $publishArgs += "--self-contained"
    $publishArgs += "-r", $Runtime
    $folderName = "self-contained-$Runtime"
}

if ($SingleFile) {
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    if ($SelfContained) {
        $folderName = "single-file-self-contained-$Runtime"
    } else {
        $folderName = "single-file-framework-dependent-$Runtime"
    }
}

$publishArgs += "-o", "$outputDir/$folderName"
$publishPath = "$outputDir/$folderName"

dotnet publish @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Publish failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Build and publish completed!" -ForegroundColor Green
Write-Host "📂 Publish path: $publishPath" -ForegroundColor Cyan

# Display configuration example
Write-Host "`n📋 MCP client configuration example:" -ForegroundColor Cyan
$fullPath = (Resolve-Path $publishPath).Path

if ($SingleFile -or $SelfContained) {
    $exePath = Join-Path $fullPath "RoslynMCP.MCP.exe"
    Write-Host @"
{
  "mcpServers": {
    "roslyn-mcp": {
      "transport": "stdio",
      "command": "$exePath",
      "args": [
        "-s",
        "{/path/to/your/solution.sln}",
        "-n",
        "{YOUR_CUSTOM_NAMESPACE_PREFIXES}"
      ]
    }
  }
}
"@ -ForegroundColor White
} else {
    $dllPath = Join-Path $fullPath "RoslynMCP.MCP.dll"
    Write-Host @"
{
  "mcpServers": {
    "roslyn-mcp": {
      "transport": "stdio",
      "command": "dotnet",
      "args": [
        "$dllPath",
        "-s",
        "{/path/to/your/solution.sln}",
        "-n",
        "{YOUR_CUSTOM_NAMESPACE_PREFIXES}"
      ]
    }
  }
}
"@ -ForegroundColor White
}

Write-Host "`n💡 Tips:" -ForegroundColor Yellow
Write-Host "- Replace the paths in the above configuration with your actual paths" -ForegroundColor Gray
if (!$SelfContained -and !$SingleFile) {
    Write-Host "- For self-contained version (no dependency on system .NET), use: .\build-and-deploy.ps1 -SelfContained" -ForegroundColor Gray
    Write-Host "- For single-file version (recommended), use: .\build-and-deploy.ps1 -SingleFile -SelfContained" -ForegroundColor Gray
} elseif ($SingleFile) {
    Write-Host "- Single-file version: One EXE contains all content, easy to distribute!" -ForegroundColor Gray
}