# RoslynMCP (https://github.com/syan2018/RoslynMCP)

Desensitized RoslynMCP service migrated out, suitable for background deployment and updates of large projects like games, serving development teams to integrate AI for efficiency improvement, without containing other subsequent more analysis features

## 🚀 Quick Start: Connect to AI Agent

We recommend using HTTP (SSE) mode to run the analysis server, which is the most stable and reliable way. This guide will guide you through the complete process from compilation to connecting to the Agent.

### First Step: Environment Preparation
- **Install .NET 9 SDK**: Please ensure that your development environment has [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or higher installed. The server is mainly tested on Windows platform. If subsequent steps fail, please check the .NET environment configuration first.

### Second Step: Compile Project
We provide a convenient PowerShell script to automate the compilation process.

```powershell
# Run this command in the project root directory
.\build-and-deploy.ps1
```
This script will automatically compile the project and publish it to the `release` directory. For most cases, you do not need to care about other compilation options.
*(If you need to publish for other platforms or perform self-contained publishing, you can run `.\build-and-deploy.ps1 -SelfContained` or refer to more options inside the script.)*

### Third Step: Start Server (HTTP/SSE Mode)
After successful compilation, you need to start the server and specify the C# solution to analyze.

Use the following command to start the server. Be sure to replace `<solution path>` and `<namespace prefix>` with your actual configuration.

- `--solution <path>` or `-s <path>`: **(Required)** The **absolute path** to your project's `.sln` file.
- `--namespaces <prefix>` or `-n <prefix>`: **(Recommended)** The namespace prefix of your core business logic (e.g. `MyCompany.MyApp`), multiple prefixes separated by commas. This can significantly improve analysis speed and reduce memory usage.
- `--expose-lan`: **(Optional)** Allow other devices on the LAN to access the server. Without this parameter, only local access is allowed.

```powershell
# Start HTTP (SSE) server (local access only)
# Replace the path and namespace below with your own project information
dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- --http -s "C:\Users\YourUser\Documents\MyProject\MyProject.sln" -n "MyCompany.MyProject"

# If you need other devices to connect (such as running Agent in a virtual machine or remote device), please add --expose-lan
dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- --http --expose-lan -s "C:\path\to\solution.sln" -n "Your.Namespace"
```
The server will run on `http://localhost:3001` by default.

Alternatively, after completing `.\build-and-deploy.ps1` compilation, a `RoslynMCP.MCP.dll` file will be generated in the `release/framework-dependent` directory

At this point, replace the `dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- ` in the above command with `dotnet .\release\framework-dependent\RoslynMCP.MCP.dll`, use Release mode to get higher running and startup efficiency, the stdio mode below is the same

### Fourth Step: Configure Your AI Agent
Now, you can configure the MCP connection in your AI Agent (such as Cursor, Claude Desktop).

- **Connection Type**: `sse`
- **URL**: `http://localhost:3001/sse` (If Agent and server are on different devices, use the server's LAN IP address)

Add the following configuration in the Agent's `mcp.json` or similar configuration file:
```json
{
  "mcpServers": {
    "roslyn-mcp-sse": {
      "transport": "sse",
      "url": "http://localhost:3001/sse"
    }
  }
}
```

After completing the above steps, your AI Agent should be able to communicate normally with the RoslynMCP server.

---

## 🔧 Advanced Configuration
In addition to command line parameters, you can also use environment variables or `launchSettings.json` to configure the server.

#### Environment Variables
Set the following environment variables before starting the server:
- `ROSLYN_MCP_SOLUTION_PATH`: Absolute path to the solution (`.sln`) file.
- `ROSLYN_MCP_NAMESPACE_PREFIXES`: Namespace prefix for core business logic.

**Example (Windows PowerShell):**
```powershell
$env:ROSLYN_MCP_SOLUTION_PATH="C:\path\to\your\solution.sln"
$env:ROSLYN_MCP_NAMESPACE_PREFIXES="MyCompany.MyProject"
dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- --http
```

#### `launchSettings.json` (For IDE Development)
To facilitate development and debugging in Visual Studio or Rider, you can directly modify `src/RoslynMCP.MCP/Properties/launchSettings.json` to preset configurations.

---

## ⚠️ Backup Mode: Stdio (Not Recommended)
We **do not recommend** using Stdio mode, especially when processing large projects (such as complete game projects).

**Existing Issues:**
- **Performance and Stability:** In Stdio mode, the AI Agent client's memory management strategy may cause the server to crash or fail to work properly when loading large solutions.
- **Echo Agent Compatibility:** In Echo Agent, the default Stdio startup method may fail to compile, requiring manual build in advance and using the `--no-build` parameter, which is cumbersome and error-prone.

HTTP (SSE) mode is the optimized and more reliable choice. If your Agent environment really cannot use SSE, you can try the following Stdio configuration.

### Stdio Configuration Steps
1.  **Start Command (without `--http` parameter):**
    ```powershell
    # Start Stdio server
    dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- -s "C:\path\to\your\solution.sln" -n "Your.Namespace"
    ```
2.  **Agent Configuration (`mcp.json`):**
    ```json
    {
      "mcpServers": {
        "roslyn-mcp": {
          "transport": "stdio",
          "command": "dotnet",
          "args": [
            "run",
            "--project",
            "{/path/to/your/csharp-roslyn-mcp/src/RoslynMCP.MCP/RoslynMCP.MCP.csproj}",
            "--no-build", // Required for Echo Agent
            "--",
            "-s",
            "{/path/to/your/solution.sln}",
            "-n",
            "{YOUR_CUSTOM_NAMESPACE_PREFIXES}"
          ],
          "env": { // Can be interchanged with the above -s/-n configuration
            "ROSLYN_MCP_SOLUTION_PATH": "{/path/to/your/solution.sln}",
            "ROSLYN_MCP_NAMESPACE_PREFIXES": "{YOUR_CUSTOM_NAMESPACE_PREFIXES}"
          },
        }
      }
    }
    ```
    > **Note:** In Stdio mode, it is recommended to add `--no-build` in `args` and manually run `dotnet build` in advance to avoid compilation timeout or failure when Agent calls (known issue in Echo Agent).

---

## 🛠️ API Reference
After the server successfully starts and loads the solution, the AI Agent can call a series of powerful code analysis tools through the MCP protocol.

For the complete list of available tools, please refer to:

➡️ **[Roslyn MCP Interface Description](./USAGE.md)**

---

## ✨ Advanced Features

### 🔷 Automatic Decompilation of NuGet Packages and Third-Party Libraries

RoslynMCP now supports **automatic decompilation** of symbols from NuGet packages and third-party assemblies. When you request source code for symbols that exist only in compiled assemblies (such as framework types or library classes), the server automatically decompiles them using **ILSpy**.

**What this means:**
- You can inspect the implementation of **any .NET framework class** (e.g., `List<T>`, `HttpClient`, `StringBuilder`)
- You can view the source code of **NuGet package symbols** (e.g., `Microsoft.Extensions.Logging.ILogger`)
- AI agents get **complete context** when analyzing code that uses external libraries

**How it works:**
1. When you request source code via `GetSourceCode` or `GetSymbolDetails`
2. If the symbol exists only in metadata (no source files available)
3. The server automatically locates the assembly DLL and decompiles it
4. Results are cached for fast subsequent access
5. Decompiled code is clearly marked with header comments

**Example:**
```json
{
  "name": "GetSourceCode",
  "arguments": {
    "symbolName": "System.Collections.Generic.List<T>"
  }
}
```

**Response:**
```csharp
// Decompiled from metadata
// Assembly: System.Collections

namespace System.Collections.Generic
{
    public class List<T> : IList<T>, ICollection<T>, ...
    {
        private T[] _items;
        private int _size;
        // ... full decompiled implementation
    }
}
```

**Benefits:**
- ✅ **Seamless**: Works automatically without configuration
- ✅ **Fast**: Decompiled code is cached (30-minute expiration)
- ✅ **Clear**: Metadata symbols are marked with 🔷 indicator in `GetSymbolDetails`
- ✅ **Comprehensive**: AI agents can analyze your entire codebase AND its dependencies

For technical details, see [DECOMPILATION_FEATURE.md](./specs/DECOMPILATION_FEATURE.md)
