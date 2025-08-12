# RoslynMCP

脱敏迁出的 RoslynMCP 服务，适用于游戏等大型项目后台部署更新，服务于开发团队接入 AI 提升效能，不包含其它后续更多分析特性

## 🚀 快速开始: 连接到 AI Agent

我们推荐使用 HTTP (SSE) 模式来运行分析服务器，这是最稳定和可靠的方式。本指南将引导您完成从编译到连接 Agent 的全过程。

### 第一步：环境准备
- **安装 .NET 9 SDK**: 请确保您的开发环境中已安装 [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) 或更高版本。服务器主要在 Windows 平台进行测试。如果后续步骤出错，请优先检查 .NET 环境是否配置正确。

### 第二步：编译项目
我们提供了一个便捷的 PowerShell 脚本来自动化编译过程。

```powershell
# 在项目根目录运行此命令
.\build-and-deploy.ps1
```
此脚本会自动编译项目并将其发布到 `release` 目录。对于大多数情况，您无需关心其它编译选项。
*(如果需要为其他平台或进行自包含发布，可以运行 `.\build-and-deploy.ps1 -SelfContained` 或参考脚本内部的更多选项。)*

### 第三步：启动服务器 (HTTP/SSE 模式)
编译成功后，您需要启动服务器并指定要分析的 C# 解决方案。

使用以下命令启动服务器。请务必将 `<解决方案路径>` 和 `<命名空间前缀>` 替换为您的实际配置。

- `--solution <路径>` 或 `-s <路径>`: **(必需)** 您项目 `.sln` 文件的 **绝对路径**。
- `--namespaces <前缀>` 或 `-n <前缀>`: **(推荐)** 您的核心业务逻辑的命名空间前缀（例如 `MyCompany.MyApp`），多个前缀用逗号分隔。这可以显著提升分析速度和降低内存占用。
- `--expose-lan`: **(可选)** 允许局域网内的其他设备访问服务器。不加此参数则只允许本机访问。

```powershell
# 启动 HTTP (SSE) 服务器（仅限本机访问）
# 将下面的路径和命名空间替换为您自己的项目信息
dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- --http -s "C:\Users\YourUser\Documents\MyProject\MyProject.sln" -n "MyCompany.MyProject"

# 如果需要从其他设备连接（如在虚拟机或远程设备中运行Agent），请添加 --expose-lan
dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- --http --expose-lan -s "C:\path\to\solution.sln" -n "Your.Namespace"
```
服务器默认将在 `http://localhost:3001` 上运行。

另，若完成 `.\build-and-deploy.ps1` 编译，将在 `release/framework-dependent` 目录下生成 `RoslynMCP.MCP.dll` 文件

此时，将上述指令中的 `dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- ` 替换为 `dotnet .\release\framework-dependent\RoslynMCP.MCP.dll` ，使用Release模式获得更高的运行和启动效率，下述stdio模式同理

### 第四步：配置您的 AI Agent
现在，您可以在您的 AI Agent (如 Cursor, Claude Desktop) 中配置 MCP 连接。

- **连接类型**: `sse`
- **URL**: `http://localhost:3001/sse`  (如果Agent和服务器在不同设备，请使用服务器的局域网IP地址)

在 Agent 的 `mcp.json` 或类似配置文件中，添加如下配置：
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

完成以上步骤后，您的 AI Agent 应该已经可以和 RoslynMCP 服务器正常通信了。

---

## 🔧 高级配置
除了通过命令行参数，您还可以使用环境变量或 `launchSettings.json` 来配置服务器。

#### 环境变量
在启动服务器前设置以下环境变量：
- `ROSLYN_MCP_SOLUTION_PATH`: 解决方案 (`.sln`) 文件的绝对路径。
- `ROSLYN_MCP_NAMESPACE_PREFIXES`: 核心业务逻辑的命名空间前缀。

**示例 (Windows PowerShell):**
```powershell
$env:ROSLYN_MCP_SOLUTION_PATH="C:\path\to\your\solution.sln"
$env:ROSLYN_MCP_NAMESPACE_PREFIXES="MyCompany.MyProject"
dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- --http
```

#### `launchSettings.json` (用于 IDE 开发)
为了方便在 Visual Studio 或 Rider 中开发调试，可以直接修改 `src/RoslynMCP.MCP/Properties/launchSettings.json` 来预设配置。

---

## ⚠️ 备用模式: Stdio (不推荐)
我们 **不推荐** 使用 Stdio 模式，尤其是在处理大型项目（如完整游戏项目）时。

**存在的问题:**
- **性能与稳定性:** 在 Stdio 模式下，AI Agent 客户端的内存管理策略可能导致服务器在加载大型解决方案时崩溃或无法正常工作。
- **Echo Agent 兼容性:** 在 Echo Agent 中，默认的 Stdio 启动方式可能无法成功编译，需要预先手动构建并使用 `--no-build` 参数，操作繁琐且容易出错。

HTTP (SSE) 模式是经过优化的、更可靠的选择。如果您的 Agent 环境确实无法使用 SSE，可以尝试以下 Stdio 配置。

### Stdio 配置步骤
1.  **启动命令 (无 `--http` 参数):**
    ```powershell
    # 启动 Stdio 服务器
    dotnet run --project src/RoslynMCP.MCP/RoslynMCP.MCP.csproj -- -s "C:\path\to\your\solution.sln" -n "Your.Namespace"
    ```
2.  **Agent 配置 (`mcp.json`):**
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
            "--no-build", // Echo Agent下需添加
            "--",
            "-s",
            "{/path/to/your/solution.sln}",
            "-n",
            "{YOUR_CUSTOM_NAMESPACE_PREFIXES}"
          ],
          "env": { // 可与上述 -s/-n 配置互相替换
            "ROSLYN_MCP_SOLUTION_PATH": "{/path/to/your/solution.sln}",
            "ROSLYN_MCP_NAMESPACE_PREFIXES": "{YOUR_CUSTOM_NAMESPACE_PREFIXES}"
          },
        }
      }
    }
    ```
    > **注意:** Stdio 模式下，建议在 `args` 中添加 `--no-build`，并提前手动运行 `dotnet build`，以避免 Agent 调用时出现编译超时或失败（已知在 Echo Agent 下存在该问题）。

---

## 🛠️ API 参考
服务器成功启动并加载解决方案后，AI Agent 就可以通过 MCP 协议调用一系列强大的代码分析工具了。

关于可用工具的完整列表，请参阅：

➡️ **[Roslyn MCP 接口说明](./USAGE.md)**
