using Microsoft.CodeAnalysis;

namespace RoslynMCP.SymbolCache
{
    /// <summary>
    /// Roslyn utility class, provides symbol traversal and other functions
    /// </summary>
    public static class RoslynUtils
    {
        /// <summary>
        /// Recursively get all symbols under a namespace or type (including nested types, methods, properties, etc.)
        /// </summary>
        public static IEnumerable<ISymbol> GetAllSymbols(INamespaceOrTypeSymbol container)
        {
            foreach (var member in container.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    foreach (var nestedSymbol in GetAllSymbols(ns))
                    {
                        yield return nestedSymbol;
                    }
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    foreach (var nestedSymbol in GetAllSymbols(type))
                    {
                        yield return nestedSymbol;
                    }
                }
                // Extract members like methods and properties
                else if (member.Kind == SymbolKind.Method || member.Kind == SymbolKind.Property || member.Kind == SymbolKind.Field)
                {
                    yield return member;
                }
            }
        }

        /// <summary>
        /// Recursively get all types under a namespace or type (including nested types)
        /// </summary>
        public static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceOrTypeSymbol container)
        {
            foreach (var member in container.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    foreach (var nestedType in GetAllTypes(ns)) yield return nestedType;
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    foreach (var nestedType in GetAllTypes(type)) yield return nestedType;
                }
            }
        }



        /// <summary>
        /// Get member's type
        /// </summary>
        public static ITypeSymbol? GetMemberType(ISymbol member)
        {
            return member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };
        }

        /// <summary>
        /// Get underlying type (handle array and collection types)
        /// </summary>
        public static ITypeSymbol GetUnderlyingType(ITypeSymbol typeSymbol, out bool isCollection)
        {
            isCollection = false;
            if (typeSymbol is IArrayTypeSymbol arrayType)
            {
                isCollection = true;
                return arrayType.ElementType;
            }

            if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable))
            {
                var typeArgs = namedType.TypeArguments;
                if (typeArgs.Length > 0)
                {
                    isCollection = true;
                    // For Dictionary<K,V>, we are interested in V. For List<T>, we are interested in T.
                    return typeArgs.Last();
                }
            }
            return typeSymbol;
        }

        /// <summary>
        /// Build inheritance relationship mapping
        /// </summary>
        public static Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> BuildInheritanceMap(
            IEnumerable<INamedTypeSymbol> classSymbols, 
            HashSet<INamedTypeSymbol> protoSymbolSet)
        {
            var map = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
            foreach (var classSymbol in classSymbols)
            {
                var baseType = classSymbol.BaseType;
                while (baseType != null && protoSymbolSet.Contains(baseType))
                {
                    if (!map.ContainsKey(baseType))
                    {
                        map[baseType] = new List<INamedTypeSymbol>();
                    }
                    map[baseType].Add(classSymbol);
                    baseType = baseType.BaseType;
                }
            }
            return map;
        }
    }
}
