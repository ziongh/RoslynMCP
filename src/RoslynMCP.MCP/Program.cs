using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Sockets;
using RoslynMCP.Query.Services;

using Microsoft.Extensions.Caching.Memory;
using RoslynMCP.SymbolCache.Diagnostics;
using RoslynMCP.SymbolCache.Security;
using RoslynMCP.SymbolCache;
using RoslynMCP.Analysis.Services;
using ModelContextProtocol.Server;
using RoslynMCP.MCP.Services;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMCP.MCP
{
    /// <summary>
    /// RoslynMCP 主程序
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // 检查是否是HTTP模式
            bool httpMode = args.Contains("--http") || args.Contains("-h");
            
            if (httpMode)
            {
                RunHttpServer(args);
            }
            else
            {
                await RunStdioServer(args);
            }
        }

        /// <summary>
        /// 运行HTTP模式服务器
        /// </summary>
        static void RunHttpServer(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 检查是否暴露到局域网
            bool exposeToLan = args.Contains("--expose-lan");

            // 配置文件和环境变量
            builder.Configuration
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);

            // 配置日志
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            ConfigureAnalyzerServices(builder.Services, builder.Configuration, args);

            // 注册MCP服务器 - HTTP传输
            builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly();

            var app = builder.Build();

            // 显式获取服务实例，触发其构造函数和初始化逻辑
            _ = app.Services.GetRequiredService<IMCPServiceManager>();

            // 映射MCP端点
            app.MapMcp();

            var port = GetPortFromArgs(args) ?? 3001;
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            
            // 记录网络信息
            LogNetworkInfo(logger, port, exposeToLan);

            // 启动服务器
            var hostAddress = exposeToLan ? "*" : "localhost";
            app.Run($"http://{hostAddress}:{port}");
        }

        /// <summary>
        /// 记录网络信息，显示可用的访问地址
        /// </summary>
        static void LogNetworkInfo(ILogger logger, int port, bool exposeToLan)
        {
            logger.LogInformation("==================================================");
            logger.LogInformation("  RoslynMCP.MCP Server is running in HTTP mode.");
            logger.LogInformation("--------------------------------------------------");
            
            // Log localhost access
            logger.LogInformation($"  - Local: http://localhost:{port}/sse");

            // Log LAN IP addresses if exposed
            if (exposeToLan)
            {
                try
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            logger.LogInformation($"  - LAN:   http://{ip}:{port}/sse");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Could not determine LAN IP address: {Message}", ex.Message);
                }
            }
            
            logger.LogInformation("==================================================");
        }

        /// <summary>
        /// 运行Stdio模式服务器
        /// </summary>
        static async Task RunStdioServer(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            
            // 配置文件和环境变量
            builder.Configuration
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);
                
            // 配置日志输出到stderr（MCP协议要求）
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            ConfigureAnalyzerServices(builder.Services, builder.Configuration, args);

            // 注册MCP服务器 - Stdio传输
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();

            // 显式获取服务实例，触发其构造函数和初始化逻辑
            _ = host.Services.GetRequiredService<IMCPServiceManager>();

            await host.RunAsync();
        }

        /// <summary>
        /// 从命令行参数中解析端口号
        /// </summary>
        static int? GetPortFromArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--port" || args[i] == "-p")
                {
                    if (int.TryParse(args[i + 1], out int port))
                    {
                        return port;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 从命令行参数中解析解决方案路径
        /// </summary>
        static string? GetSolutionPathFromArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--solution" || args[i] == "-s")
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        /// <summary>
        /// 从命令行参数中解析命名空间前缀
        /// </summary>
        static string? GetNamespacesFromArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--namespaces" || args[i] == "-n")
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static void ConfigureAnalyzerServices(IServiceCollection services, IConfiguration configuration, string[] args)
        {
            // 注册配置选项
            services.Configure<AnalyzerOptions>(configuration.GetSection("AnalyzerOptions"));
            
            // 从环境变量和命令行参数覆盖配置
            var solutionPath = GetSolutionPathFromArgs(args) ?? Environment.GetEnvironmentVariable("ROSLYN_MCP_SOLUTION_PATH");
            var namespaces = GetNamespacesFromArgs(args) ?? Environment.GetEnvironmentVariable("ROSLYN_MCP_NAMESPACE_PREFIXES");

            services.PostConfigure<AnalyzerOptions>(options =>
            {
                if (!string.IsNullOrEmpty(solutionPath))
                {
                    options.DefaultSolutionPath = solutionPath;
                }
                if (!string.IsNullOrEmpty(namespaces))
                {
                    options.DefaultNamespacePrefixes = namespaces;
                }
            });

            // 核心服务
            services.AddSingleton<DiagnosticLogger>();
            services.AddSingleton<SecurityValidator>();
            
            // 注册MSBuildWorkspace为单例服务，提高性能
            services.AddSingleton<MSBuildWorkspace>(provider => MSBuildWorkspace.Create());
            
            // 解决方案状态管理器（核心）
            services.AddSingleton<RoslynMCP.SymbolCache.ISolutionStateManager>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RoslynMCP.SymbolCache.SolutionStateManager>>();
                var securityValidator = provider.GetRequiredService<SecurityValidator>();
                var workspace = provider.GetRequiredService<MSBuildWorkspace>();
                return new RoslynMCP.SymbolCache.SolutionStateManager(logger, securityValidator, workspace);
            });
            
            // MCP应用级服务管理器
            services.AddSingleton<IMCPServiceManager, MCPServiceManager>();

            // 查询和分析服务 - 使用单例模式以避免重复初始化
            services.AddSingleton<IQueryService, QueryService>();
            services.AddSingleton<Analysis.Services.IAnalysisService, Analysis.Services.AnalysisService>();
        }
    }

    /// <summary>
    /// 分析器配置选项
    /// </summary>
    public class AnalyzerOptions
    {
        /// <summary>
        /// 默认解决方案路径
        /// </summary>
        public string DefaultSolutionPath { get; set; } = string.Empty;

        /// <summary>
        /// 默认命名空间前缀列表（逗号分隔）
        /// </summary>
        public string DefaultNamespacePrefixes { get; set; } = string.Empty;

        /// <summary>
        /// 最大查询结果数量
        /// </summary>
        public int MaxQueryResults { get; set; } = 20;

        /// <summary>
        /// 最大图节点数量
        /// </summary>
        public int MaxGraphNodes { get; set; } = 1000;

        /// <summary>
        /// 缓存过期时间（分钟）
        /// </summary>
        public int CacheExpirationMinutes { get; set; } = 30;
    }
}
