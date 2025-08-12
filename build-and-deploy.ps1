#!/usr/bin/env pwsh
# RoslynMCP 编译和部署脚本
# 使用方法: .\build-and-deploy.ps1

param(
    [string]$Configuration = "Release",
    [switch]$SelfContained = $false,
    [switch]$SingleFile = $false,
    [string]$Runtime = "win-x64"
)

Write-Host "🔨 开始编译 RoslynMCP..." -ForegroundColor Green

# 清理之前的构建
Write-Host "🧹 清理之前的构建..." -ForegroundColor Yellow
dotnet clean

# 编译项目
Write-Host "🔧 编译项目 ($Configuration 配置)..." -ForegroundColor Yellow
dotnet build -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 编译失败!" -ForegroundColor Red
    exit 1
}

# 发布项目
Write-Host "📦 发布项目..." -ForegroundColor Yellow
# 创建输出目录
$outputDir = "release"
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# 构建发布参数
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
    Write-Host "❌ 发布失败!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ 编译和发布完成!" -ForegroundColor Green
Write-Host "📂 发布路径: $publishPath" -ForegroundColor Cyan

# 显示配置示例
Write-Host "`n📋 MCP 客户端配置示例:" -ForegroundColor Cyan
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

Write-Host "`n💡 提示:" -ForegroundColor Yellow
Write-Host "- 将上述配置中的路径替换为您的实际路径" -ForegroundColor Gray
if (!$SelfContained -and !$SingleFile) {
    Write-Host "- 如需自包含版本（不依赖系统.NET），请使用: .\build-and-deploy.ps1 -SelfContained" -ForegroundColor Gray
    Write-Host "- 如需单文件版本（推荐），请使用: .\build-and-deploy.ps1 -SingleFile -SelfContained" -ForegroundColor Gray
} elseif ($SingleFile) {
    Write-Host "- 单文件版本：一个EXE包含所有内容，便于分发！" -ForegroundColor Gray
}