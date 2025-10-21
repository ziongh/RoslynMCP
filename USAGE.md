# RoslynMCP Server Usage Guide


## 🛠️ Available Tools

All code analysis tools depend on a loaded solution. Please ensure that a solution has been successfully loaded through environment variables or parameters before calling them.

### Category 1: Solution Management

These tools are used to manage the working environment of the analyzer.

---

#### **`GetSolutionStatus`**
Get the loading status of the current solution, service initialization information, and summary of loaded projects.

*   **Parameters**: None
*   **Example**:
    ```json
    {
      "name": "GetSolutionStatus",
      "arguments": {}
    }
    ```

---

#### **`ReloadSolution`**
Reload the current solution from disk, refreshing all cached symbols. **Use this tool after making code changes** (adding/removing/modifying methods, properties, classes) to ensure the analysis cache reflects the latest state.

*   **When to use**:
    *   After adding new methods, properties, or classes to the codebase
    *   After deleting or moving code between files
    *   After refactoring that changes method signatures or inheritance
    *   When line numbers or references seem stale or incorrect
*   **Performance**: May take 30 seconds to 2 minutes for large solutions
*   **Parameters**: None
*   **Example**:
    ```json
    {
      "name": "ReloadSolution",
      "arguments": {}
    }
    ```

---

#### **`NotifyCodeChanges`**
Notify the server that specific code files have been modified, triggering a fast incremental cache update. **Much faster than ReloadSolution** (2-5 seconds vs 30s-2min) while accurately updating symbols, references, and line numbers.

*   **When to use**:
    *   After making targeted code changes to specific files
    *   When you know exactly which files changed
    *   For iterative development with frequent small changes
    *   When speed is important and you're modifying <10 files at a time
*   **Performance**: 2-5 seconds for most changes
*   **Parameters**:
    *   `changedFiles` (string array, **required**): File paths that have been modified. Can be absolute paths or relative to solution root.
*   **Example**:
    ```json
    {
      "name": "NotifyCodeChanges",
      "arguments": {
        "changedFiles": ["src/MyProject/Services/UserService.cs", "src/MyProject/Models/User.cs"]
      }
    }
    ```

---

### Category 2: Code Query and Analysis

These tools are used for deep exploration and analysis of loaded solutions.

---

#### **`SearchSymbols`**
Search for symbols in C# code using wildcards (`*`, `?`).

*   **Parameters**:
    *   `pattern` (string, **required**): Wildcard pattern for search (e.g. `'*Service'`).
    *   `symbolTypes` (string, *optional*): Symbol types to include, comma-separated. Valid options: `'class'`, `'interface'`, `'method'`, `'property'`, `'field'`, `'enum'`. (Default: `'class,interface,method,property'`).
    *   `maxResults` (int, *optional*): Maximum number of results to return (Default: 20).
    *   `excludeGeneratedFiles` (bool, *optional*): Whether to exclude automatically generated files (Default: `true`).
    *   `excludeSystemTypes` (bool, *optional*): Whether to exclude system types, such as constructors (Default: `true`).
    *   `caseSensitive` (bool, *optional*): Whether search is case-sensitive (Default: `true`).
*   **Example**:
    ```json
    {
      "name": "SearchSymbols",
      "arguments": {
        "pattern": "I*Repository",
        "symbolTypes": "interface"
      }
    }
    ```

---

#### **`GetSymbolDetails`**
Get the complete aggregated analysis of a symbol, including source code, references, and inheritance hierarchy.

**⚡ Automatic Decompilation**: When analyzing symbols from NuGet packages or third-party assemblies, this tool automatically decompiles them and marks them with a 🔷 **Metadata** indicator, so you know when you're inspecting decompiled code versus your solution's source code.

*   **Parameters**:
    *   `symbolName` (string, **required**): Exact symbol name or fully qualified name.
    *   `includeSourceCode` (bool, *optional*): Whether to include source code in the response (Default: `true`).
    *   `includeReferences` (bool, *optional*): Whether to include reference information in the response (Default: `true`).
    *   `includeInheritanceHierarchy` (bool, *optional*): Whether to include inheritance information in the response (Default: `true`).
    *   `maxReferences` (int, *optional*): Maximum number of references to include (Default: 20).
*   **Example**:
    ```json
    {
      "name": "GetSymbolDetails",
      "arguments": {
        "symbolName": "MyNamespace.MyClass",
        "includeSourceCode": true,
        "includeReferences": false
      }
    }
    ```
*   **Metadata Symbol Indicator**: When analyzing third-party symbols, the response includes:
    ```markdown
    ## 📋 Basic Information
    - **Source Type**: 🔷 **Metadata** (from NuGet/third-party assembly - decompiled)
    ```

---

#### **`GetSourceCode`**
Get the complete source code of a specific symbol (class, method, property, etc.).

**⚡ Automatic Decompilation**: If the symbol exists in a NuGet package or third-party assembly (not in your solution's source code), RoslynMCP will automatically decompile it using ILSpy. This allows you to inspect the implementation of any .NET framework class or library symbol.

*   **Parameters**:
    *   `symbolName` (string, **required**): Exact symbol name or fully qualified name.
*   **Example (Source Code)**:
    ```json
    {
      "name": "GetSourceCode",
      "arguments": {
        "symbolName": "MyNamespace.MyClass.MyMethod"
      }
    }
    ```
*   **Example (NuGet Package - Automatic Decompilation)**:
    ```json
    {
      "name": "GetSourceCode",
      "arguments": {
        "symbolName": "System.Collections.Generic.List<T>"
      }
    }
    ```
    **Response**: Returns decompiled source code with header comments indicating it was decompiled from metadata:
    ```csharp
    // Decompiled from metadata
    // Assembly: System.Collections
    
    namespace System.Collections.Generic
    {
        public class List<T> : IList<T>, ...
        {
            private T[] _items;
            // ... full implementation
        }
    }
    ```

---

#### **`GetFileContent`**
Get the complete content of a source file in the solution.

*   **Parameters**:
    *   `filePath` (string, **required**): Path to the file, can be absolute path or relative to solution root directory.
*   **Example**:
    ```json
    {
      "name": "GetFileContent",
      "arguments": {
        "filePath": "src/MyProject/MyFile.cs"
      }
    }
    ```

### Category 3: Relationship and Structure Analysis

These tools are used to understand the interrelationships between code units and the overall structure of the project.

---

#### **`FindReferences`**
Find all code references to a specific symbol.

*   **Parameters**:
    *   `symbolName` (string, **required**): Exact symbol name to find references for.
    *   `includeDefinition` (bool, *optional*): Whether to include the symbol's own definition in results (Default: `true`).
    *   `maxResults` (int, *optional*): Maximum number of references to return (Default: 20).
    *   `excludeGeneratedFiles` (bool, *optional*): Whether to exclude references in auto-generated files (Default: `true`).
*   **Example**:
    ```json
    {
      "name": "FindReferences",
      "arguments": {
        "symbolName": "MyNamespace.MyClass"
      }
    }
    ```

---

#### **`GetInheritanceHierarchy`**
Get the inheritance hierarchy of a class or interface (base classes and derived classes).

*   **Parameters**:
    *   `symbolName` (string, **required**): Symbol name to analyze.
*   **Example**:
    ```json
    {
      "name": "GetInheritanceHierarchy",
      "arguments": {
        "symbolName": "MyNamespace.MyDerivedClass"
      }
    }
    ```

---

#### **`GetMethodBodyInvocations`**
Get all method calls within a specific method body.

*   **Parameters**:
    *   `methodName` (string, **required**): Method name to analyze, can be partial or fully qualified name (e.g. `'MyMethod'` or `'MyClass.MyMethod'`).
    *   `projectName` (string, *optional*): Limit search scope to this project name. Use `ListProjects` to get project names.
*   **Example**:
    ```json
    {
      "name": "GetMethodBodyInvocations",
      "arguments": {
        "methodName": "HandleRequest",
        "projectName": "MyProject.Core"
      }
    }
    ```

---

#### **`ListProjects`**
List all projects in the current solution and their dependencies.

*   **Parameters**:
    *   `maxProjects` (int, *optional*): Maximum number of projects to display (Default: 15, use `-1` to show all).
    *   `maxPackages` (int, *optional*): Maximum number of package references to display per project (Default: 5, use `-1` to show all).
    *   `showSystemPackages` (bool, *optional*): Whether to display system packages in package reference details (Default: `false`).
*   **Example**:
    ```json
    {
      "name": "ListProjects",
      "arguments": {
        "maxProjects": 20
      }
    }
    ```

---

#### **`GetProjectDependencies`**
Get detailed dependencies of a single project (project references and package references).

*   **Parameters**:
    *   `projectName` (string, **required**): Project name to analyze.
*   **Example**:
    ```json
    {
      "name": "GetProjectDependencies",
      "arguments": {
        "projectName": "MyProject.Core"
      }
    }
    ```

---

## 🔷 Automatic Decompilation Feature

RoslynMCP includes **automatic decompilation** of symbols from NuGet packages, third-party assemblies, and .NET framework libraries using **ILSpy**. This powerful feature enables complete code visibility across your entire development ecosystem.

### How It Works

When you request source code for a symbol:

1. **Source Code First**: If the symbol is defined in your solution's source files, the actual source code is returned
2. **Automatic Fallback**: If the symbol exists only in compiled assemblies (metadata), RoslynMCP automatically decompiles it
3. **Clear Attribution**: Decompiled code includes header comments indicating the source assembly
4. **Smart Caching**: Results are cached for 30 minutes to optimize performance

### Supported Symbols

The decompilation feature works with:
- ✅ .NET Framework types (e.g., `System.String`, `List<T>`, `HttpClient`)
- ✅ NuGet package symbols (e.g., `Microsoft.Extensions.Logging.ILogger`)
- ✅ Third-party library classes, methods, properties, fields, and events
- ✅ Any compiled assembly accessible to your solution

### When to Use

**Perfect for:**
- Understanding how framework/library methods are implemented
- Debugging issues in third-party dependencies
- Learning best practices from well-written library code
- Getting complete context when AI analyzes code using external APIs

**Example Use Cases:**
1. **Framework Exploration**: "Show me how `List<T>.Sort()` is implemented"
2. **Library Understanding**: "What does `ILogger.LogInformation` actually do?"
3. **Dependency Analysis**: "How does this NuGet package method handle errors?"

### Example: Decompiling .NET Framework Type

**Request:**
```json
{
  "name": "GetSourceCode",
  "arguments": {
    "symbolName": "System.Text.StringBuilder"
  }
}
```

**Response:**
```csharp
// Decompiled from metadata
// Assembly: System.Runtime

using System;

namespace System.Text
{
    public sealed class StringBuilder
    {
        private char[] m_ChunkChars;
        private StringBuilder m_ChunkPrevious;
        private int m_ChunkLength;
        private int m_ChunkOffset;
        
        public StringBuilder() { }
        public StringBuilder(int capacity) { }
        
        public StringBuilder Append(string value)
        {
            // ... decompiled implementation
        }
        
        // ... more members
    }
}
```

### Visual Indicators

When using `GetSymbolDetails`, metadata symbols are clearly marked:

```markdown
## 📋 Basic Information
- **Name**: `List<T>`
- **Namespace**: System.Collections.Generic
- **Assembly**: System.Collections
- **Source Type**: 🔷 **Metadata** (from NuGet/third-party assembly - decompiled)
```

vs. source code symbols:

```markdown
- **Source Type**: 📄 **Source Code** (from solution)
```

### Performance Notes

- **First Request**: 1-2 seconds for large types (decompilation + caching)
- **Subsequent Requests**: <100ms (served from cache)
- **Cache Expiration**: 30 minutes of inactivity
- **Memory Limit**: 100MB cache size

### Limitations

1. **Assembly Access**: Can only decompile assemblies accessible from the solution's runtime directory
2. **Obfuscated Code**: Decompiled code from obfuscated assemblies may have meaningless variable names
3. **Approximate Representation**: Decompiled code shows a reconstructed version, not the exact original source

For technical implementation details, see [specs/DECOMPILATION_FEATURE.md](../specs/DECOMPILATION_FEATURE.md)

