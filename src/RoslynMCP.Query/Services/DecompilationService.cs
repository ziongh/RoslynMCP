using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Metadata;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;
using RoslynModuleSymbol = Microsoft.CodeAnalysis.IModuleSymbol;
using RoslynAssemblySymbol = Microsoft.CodeAnalysis.IAssemblySymbol;

namespace RoslynMCP.Query.Services;

public sealed class DecompilationService : IDecompilationService, IDisposable
{
    private readonly ILogger<DecompilationService> _logger;
    private readonly IMemoryCache _cache;
    private readonly Dictionary<string, CSharpDecompiler> _decompilerCache = new();
    private readonly SemaphoreSlim _decompileLock = new(1, 1);
    private bool _disposed;

    public DecompilationService(ILogger<DecompilationService> logger, IMemoryCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    public bool IsMetadataSymbol(RoslynSymbol symbol) =>
        !symbol.Locations.Any(l => l.IsInSource) &&
        symbol.Locations.Any(l => l.IsInMetadata);

    public async Task<string?> DecompileSymbolAsync(RoslynSymbol symbol, CancellationToken cancellationToken = default)
    {
        if (!IsMetadataSymbol(symbol))
        {
            _logger.LogWarning("Symbol {SymbolName} is not a metadata symbol", symbol.ToDisplayString());
            return null;
        }

        var assemblyName = symbol.ContainingAssembly?.Name;
        if (string.IsNullOrEmpty(assemblyName))
        {
            _logger.LogWarning("Cannot decompile symbol without assembly information: {SymbolName}", 
                symbol.ToDisplayString());
            return null;
        }

        var cacheKey = $"decompiled_{symbol.ToDisplayString()}";
        
        if (_cache.TryGetValue<string>(cacheKey, out var cachedCode))
        {
            _logger.LogDebug("Returning cached decompiled code for {SymbolName}", symbol.ToDisplayString());
            return cachedCode;
        }

        await _decompileLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<string>(cacheKey, out cachedCode))
                return cachedCode;

            var metadataLocation = symbol.Locations.FirstOrDefault(l => l.IsInMetadata);
            if (metadataLocation?.MetadataModule is not RoslynModuleSymbol moduleSymbol)
            {
                _logger.LogWarning("Cannot determine metadata module for {SymbolName}", symbol.ToDisplayString());
                return null;
            }

            var assemblyPath = GetAssemblyPath(moduleSymbol);
            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            {
                _logger.LogWarning("Assembly file not found for {AssemblyName}: {Path}", 
                    assemblyName, assemblyPath ?? "null");
                return null;
            }

            var decompiler = GetOrCreateDecompiler(assemblyPath);
            if (decompiler == null)
                return null;

            var decompiledCode = DecompileSymbol(decompiler, symbol);
            
            if (!string.IsNullOrEmpty(decompiledCode))
            {
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                    .SetSize(decompiledCode.Length);
                    
                _cache.Set(cacheKey, decompiledCode, cacheOptions);
                
                _logger.LogInformation("Successfully decompiled {SymbolName} from {Assembly}", 
                    symbol.ToDisplayString(), assemblyName);
            }

            return decompiledCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decompilation failed for {SymbolName}", symbol.ToDisplayString());
            return null;
        }
        finally
        {
            _decompileLock.Release();
        }
    }

    private string? DecompileSymbol(CSharpDecompiler decompiler, RoslynSymbol symbol)
    {
        try
        {
            var fullTypeName = GetFullMetadataName(symbol);
            
            _logger.LogDebug("Attempting to decompile type: {TypeName}", fullTypeName);
            
            var typeDefinition = FindType(decompiler, fullTypeName);
            if (typeDefinition == null)
            {
                _logger.LogWarning("Type not found for {TypeName}", fullTypeName);
                return null;
            }

            var code = symbol switch
            {
                INamedTypeSymbol => decompiler.DecompileTypeAsString(typeDefinition.FullTypeName),
                IMethodSymbol methodSymbol => DecompileMethod(decompiler, typeDefinition, methodSymbol),
                IPropertySymbol propertySymbol => DecompileProperty(decompiler, typeDefinition, propertySymbol),
                IFieldSymbol or IEventSymbol => decompiler.DecompileTypeAsString(typeDefinition.FullTypeName),
                _ => decompiler.DecompileTypeAsString(typeDefinition.FullTypeName)
            };

            return code;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decompiling symbol {SymbolName}", symbol.ToDisplayString());
            return null;
        }
    }

    private string? DecompileMethod(CSharpDecompiler decompiler, ITypeDefinition typeDef, IMethodSymbol methodSymbol)
    {
        var typeCode = decompiler.DecompileTypeAsString(typeDef.FullTypeName);
        var methodPattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(methodSymbol.Name)}\s*(<[^>]+>)?\s*\(";
        
        var lines = typeCode.Split('\n');
        var methodStartIndex = -1;
        var braceCount = 0;
        var inMethod = false;
        var methodLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (!inMethod && System.Text.RegularExpressions.Regex.IsMatch(lines[i], methodPattern))
            {
                methodStartIndex = i;
                inMethod = true;
            }

            if (inMethod)
            {
                methodLines.Add(lines[i]);
                braceCount += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
                
                if (methodStartIndex >= 0 && braceCount == 0 && lines[i].Contains('}'))
                    break;
            }
        }

        return methodLines.Any() ? string.Join("\n", methodLines) : typeCode;
    }

    private string? DecompileProperty(CSharpDecompiler decompiler, ITypeDefinition typeDef, IPropertySymbol propertySymbol)
    {
        var typeCode = decompiler.DecompileTypeAsString(typeDef.FullTypeName);
        var propertyPattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(propertySymbol.Name)}\s*\{{";
        
        var lines = typeCode.Split('\n');
        var propertyLines = new List<string>();
        var capturing = false;
        var braceCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (!capturing && System.Text.RegularExpressions.Regex.IsMatch(lines[i], propertyPattern))
                capturing = true;

            if (capturing)
            {
                propertyLines.Add(lines[i]);
                braceCount += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
                
                if (braceCount == 0 && lines[i].Contains('}'))
                    break;
            }
        }

        return propertyLines.Any() ? string.Join("\n", propertyLines) : typeCode;
    }

    private ITypeDefinition? FindType(CSharpDecompiler decompiler, string fullTypeName)
    {
        try
        {
            var typeSystem = decompiler.TypeSystem;
            
            var typeName = new FullTypeName(fullTypeName);
            var typeDefinition = typeSystem.FindType(typeName).GetDefinition();
            
            return typeDefinition;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not find type for {TypeName}", fullTypeName);
            return null;
        }
    }

    private string GetFullMetadataName(RoslynSymbol symbol)
    {
        var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        
        if (containingType == null)
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "");

        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "");
            
        return typeName;
    }

    private CSharpDecompiler? GetOrCreateDecompiler(string assemblyPath)
    {
        if (_decompilerCache.TryGetValue(assemblyPath, out var cached))
            return cached;

        try
        {
            var settings = new DecompilerSettings
            {
                ThrowOnAssemblyResolveErrors = false,
            };

            var decompiler = new CSharpDecompiler(assemblyPath, settings);
            _decompilerCache[assemblyPath] = decompiler;
            
            _logger.LogInformation("Created decompiler for assembly: {Path}", assemblyPath);
            return decompiler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create decompiler for {Path}", assemblyPath);
            return null;
        }
    }

    private string? GetAssemblyPath(RoslynModuleSymbol moduleSymbol)
    {
        if (moduleSymbol.ContainingAssembly?.Locations.FirstOrDefault() is { } location)
        {
            if (location.Kind == LocationKind.MetadataFile)
            {
                foreach (var refLocation in moduleSymbol.Locations)
                {
                    if (refLocation.Kind == LocationKind.MetadataFile)
                    {
                        var path = refLocation.GetType()
                            .GetProperty("MetadataModule")?
                            .GetValue(refLocation)
                            ?.GetType()
                            .GetProperty("Name")?
                            .GetValue(refLocation.GetType()
                                .GetProperty("MetadataModule")?
                                .GetValue(refLocation)) as string;
                        
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            return path;
                    }
                }
            }
        }

        var moduleName = moduleSymbol.Name;
        if (string.IsNullOrEmpty(moduleName))
            return null;

        var assemblyName = moduleSymbol.ContainingAssembly?.Identity.Name;
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        var potentialPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll"),
            Path.Combine(Environment.CurrentDirectory, $"{assemblyName}.dll"),
        };

        foreach (var path in potentialPaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found assembly at: {Path}", path);
                return path;
            }
        }

        _logger.LogWarning("Could not locate assembly file for {AssemblyName}", assemblyName);
        return null;
    }

    public void ClearCache()
    {
        _decompilerCache.Clear();
        _logger.LogInformation("Decompiler cache cleared");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _decompileLock.Dispose();
        _decompilerCache.Clear();
        _disposed = true;
    }
}
