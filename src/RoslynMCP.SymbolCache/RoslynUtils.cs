using Microsoft.CodeAnalysis;

namespace RoslynMCP.SymbolCache
{
    /// <summary>
    /// Roslyn 工具类，提供符号遍历等功能
    /// </summary>
    public static class RoslynUtils
    {
        /// <summary>
        /// 递归获取命名空间或类型下的所有符号（包括嵌套类型、方法、属性等）
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
                // 提取方法和属性等成员
                else if (member.Kind == SymbolKind.Method || member.Kind == SymbolKind.Property || member.Kind == SymbolKind.Field)
                {
                    yield return member;
                }
            }
        }

        /// <summary>
        /// 递归获取命名空间或类型下的所有类型（包括嵌套类型）
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
        /// 获取成员的类型
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
        /// 获取底层类型（处理数组和集合类型）
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
        /// 构建继承关系映射
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
