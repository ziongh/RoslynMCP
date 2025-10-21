using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace RoslynMCP.Query.Services;

public interface IDecompilationService
{
    Task<string?> DecompileSymbolAsync(RoslynSymbol symbol, CancellationToken cancellationToken = default);
    
    bool IsMetadataSymbol(RoslynSymbol symbol);
    
    void ClearCache();
}
