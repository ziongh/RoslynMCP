#!/bin/bash
# RoslynMCP build and deploy script (Linux/Mac)
# Usage: ./build-and-deploy.sh [release|debug] [self-contained] [runtime]

CONFIGURATION=${1:-Release}
SELF_CONTAINED=${2:-false}
SINGLE_FILE=${3:-false}
RUNTIME=${4:-linux-x64}

# If it's Mac, set default runtime
if [[ "$OSTYPE" == "darwin"* ]]; then
    RUNTIME=${4:-osx-x64}
fi

echo "🔨 Starting compilation of RoslynMCP..."

# Clean previous builds
echo "🧹 Cleaning previous builds..."
dotnet clean

# Build project
echo "🔧 Building project ($CONFIGURATION configuration)..."
dotnet build -c "$CONFIGURATION"
if [ $? -ne 0 ]; then
    echo "❌ Build failed!"
    exit 1
fi

# Publish project
echo "📦 Publishing project..."
# Create output directory
OUTPUT_DIR="release"
mkdir -p "$OUTPUT_DIR"

# Build publish arguments
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
    echo "❌ Publish failed!"
    exit 1
fi

echo "✅ Build and publish completed!"
echo "📂 Publish path: $PUBLISH_PATH"

# Display configuration example
echo ""
echo "📋 MCP client configuration example:"
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
echo "💡 Tips:"
echo "- Replace the paths in the above configuration with your actual paths"
if [ "$SELF_CONTAINED" != "true" ] && [ "$SINGLE_FILE" != "true" ]; then
    echo "- For self-contained version (no dependency on system .NET), use: ./build-and-deploy.sh $CONFIGURATION true false $RUNTIME"
    echo "- For single-file version (recommended), use: ./build-and-deploy.sh $CONFIGURATION true true $RUNTIME"
elif [ "$SINGLE_FILE" = "true" ]; then
    echo "- Single-file version: One executable contains all content, easy to distribute!"
fi