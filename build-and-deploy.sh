#!/bin/bash
# RoslynMCP 编译和部署脚本 (Linux/Mac)
# 使用方法: ./build-and-deploy.sh [release|debug] [self-contained] [runtime]

CONFIGURATION=${1:-Release}
SELF_CONTAINED=${2:-false}
SINGLE_FILE=${3:-false}
RUNTIME=${4:-linux-x64}

# 如果是 Mac，设置默认运行时
if [[ "$OSTYPE" == "darwin"* ]]; then
    RUNTIME=${4:-osx-x64}
fi

echo "🔨 开始编译 RoslynMCP..."

# 清理之前的构建
echo "🧹 清理之前的构建..."
dotnet clean

# 编译项目
echo "🔧 编译项目 ($CONFIGURATION 配置)..."
dotnet build -c "$CONFIGURATION"
if [ $? -ne 0 ]; then
    echo "❌ 编译失败!"
    exit 1
fi

# 发布项目
echo "📦 发布项目..."
# 创建输出目录
OUTPUT_DIR="release"
mkdir -p "$OUTPUT_DIR"

# 构建发布参数
PUBLISH_ARGS="src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -c $CONFIGURATION"
FOLDER_NAME="framework-dependent"

if [ "$SELF_CONTAINED" = "true" ]; then
    PUBLISH_ARGS="$PUBLISH_ARGS --self-contained -r $RUNTIME"
    FOLDER_NAME="self-contained-$RUNTIME"
fi

if [ "$SINGLE_FILE" = "true" ]; then
    PUBLISH_ARGS="$PUBLISH_ARGS -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true"
    if [ "$SELF_CONTAINED" = "true" ]; then
        FOLDER_NAME="single-file-self-contained-$RUNTIME"
    else
        FOLDER_NAME="single-file-framework-dependent-$RUNTIME"
    fi
fi

PUBLISH_ARGS="$PUBLISH_ARGS -o $OUTPUT_DIR/$FOLDER_NAME"
PUBLISH_PATH="$OUTPUT_DIR/$FOLDER_NAME"

dotnet publish $PUBLISH_ARGS

if [ $? -ne 0 ]; then
    echo "❌ 发布失败!"
    exit 1
fi

echo "✅ 编译和发布完成!"
echo "📂 发布路径: $PUBLISH_PATH"

# 显示配置示例
echo ""
echo "📋 MCP 客户端配置示例:"
FULL_PATH=$(realpath "$PUBLISH_PATH")

if [ "$SINGLE_FILE" = "true" ] || [ "$SELF_CONTAINED" = "true" ]; then
    EXE_PATH="$FULL_PATH/RoslynMCP.MCP"
    cat << EOF
{
  "mcpServers": {
    "roslyn-mcp": {
      "transport": "stdio",
      "command": "$EXE_PATH",
      "args": [
        "-s",
        "{/path/to/your/solution.sln}",
        "-n",
        "{YOUR_CUSTOM_NAMESPACE_PREFIXES}"
      ]
    }
  }
}
EOF
else
    DLL_PATH="$FULL_PATH/RoslynMCP.MCP.dll"
    cat << EOF
{
  "mcpServers": {
    "roslyn-mcp": {
      "transport": "stdio",
      "command": "dotnet",
      "args": [
        "$DLL_PATH",
        "-s",
        "{/path/to/your/solution.sln}",
        "-n",
        "{YOUR_CUSTOM_NAMESPACE_PREFIXES}"
      ]
    }
  }
}
EOF
fi

echo ""
echo "💡 提示:"
echo "- 将上述配置中的路径替换为您的实际路径"
if [ "$SELF_CONTAINED" != "true" ] && [ "$SINGLE_FILE" != "true" ]; then
    echo "- 如需自包含版本（不依赖系统.NET），请使用: ./build-and-deploy.sh $CONFIGURATION true false $RUNTIME"
    echo "- 如需单文件版本（推荐），请使用: ./build-and-deploy.sh $CONFIGURATION true true $RUNTIME"
elif [ "$SINGLE_FILE" = "true" ]; then
    echo "- 单文件版本：一个可执行文件包含所有内容，便于分发！"
fi