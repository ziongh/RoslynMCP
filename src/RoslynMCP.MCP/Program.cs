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
    /// RoslynMCP main program
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // Check if it's HTTP mode
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
        /// Run HTTP mode server
        /// </summary>
        static void RunHttpServer(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Check if expose to LAN
            bool exposeToLan = args.Contains("--expose-lan");

            // Configuration files and environment variables
            builder.Configuration
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            ConfigureAnalyzerServices(builder.Services, builder.Configuration, args);

            // Register MCP server - HTTP transport
            builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly();

            var app = builder.Build();

            // Explicitly get service instance to trigger constructor and initialization logic
            _ = app.Services.GetRequiredService<IMCPServiceManager>();

            // Map MCP endpoints
            app.MapMcp();

            var port = GetPortFromArgs(args) ?? 3001;
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            
            // Log network information
            LogNetworkInfo(logger, port, exposeToLan);

            // Start server
            var hostAddress = exposeToLan ? "*" : "localhost";
            app.Run($"http://{hostAddress}:{port}");
        }

        /// <summary>
        /// Log network information, display available access addresses
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
        /// Run Stdio mode server
        /// </summary>
        static async Task RunStdioServer(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            
            // Configure files and environment variables
            builder.Configuration
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);
                
            // Configure log output to stderr (required by MCP protocol)
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            ConfigureAnalyzerServices(builder.Services, builder.Configuration, args);

            // Register MCP server - Stdio transport
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();

            // Explicitly get service instance to trigger constructor and initialization logic
            _ = host.Services.GetRequiredService<IMCPServiceManager>();

            await host.RunAsync();
        }

        /// <summary>
        /// Parse port number from command line arguments
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
        /// Parse solution path from command line arguments
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
        /// Parse namespace prefixes from command line arguments
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
            // Register configuration options
            services.Configure<AnalyzerOptions>(configuration.GetSection("AnalyzerOptions"));
            
            // Override configuration from environment variables and command line arguments
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

            // Core services
            services.AddSingleton<DiagnosticLogger>();
            services.AddSingleton<SecurityValidator>();
            
            // Register MSBuildWorkspace as singleton service to improve performance
            services.AddSingleton<MSBuildWorkspace>(provider => MSBuildWorkspace.Create());
            
            // Solution state manager (core)
            services.AddSingleton<RoslynMCP.SymbolCache.ISolutionStateManager>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RoslynMCP.SymbolCache.SolutionStateManager>>();
                var securityValidator = provider.GetRequiredService<SecurityValidator>();
                var workspace = provider.GetRequiredService<MSBuildWorkspace>();
                return new RoslynMCP.SymbolCache.SolutionStateManager(logger, securityValidator, workspace);
            });
            
            // MCP application-level service manager
            services.AddSingleton<IMCPServiceManager, MCPServiceManager>();

            // Query and analysis services - use singleton pattern to avoid repeated initialization
            services.AddSingleton<IQueryService, QueryService>();
            services.AddSingleton<Analysis.Services.IAnalysisService, Analysis.Services.AnalysisService>();
        }
    }

    /// <summary>
    /// Analyzer configuration options
    /// </summary>
    public class AnalyzerOptions
    {
        /// <summary>
        /// Default solution path
        /// </summary>
        public string DefaultSolutionPath { get; set; } = string.Empty;

        /// <summary>
        /// Default namespace prefix list (comma-separated)
        /// </summary>
        public string DefaultNamespacePrefixes { get; set; } = string.Empty;

        /// <summary>
        /// Maximum query result count
        /// </summary>
        public int MaxQueryResults { get; set; } = 20;

        /// <summary>
        /// Maximum graph node count
        /// </summary>
        public int MaxGraphNodes { get; set; } = 1000;

        /// <summary>
        /// Cache expiration time (minutes)
        /// </summary>
        public int CacheExpirationMinutes { get; set; } = 30;
    }
}
