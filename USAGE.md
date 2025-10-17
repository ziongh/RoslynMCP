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

---

#### **`GetSourceCode`**
Get the complete source code of a specific symbol (class, method, property, etc.).

*   **Parameters**:
    *   `symbolName` (string, **required**): Exact symbol name or fully qualified name.
*   **Example**:
    ```json
    {
      "name": "GetSourceCode",
      "arguments": {
        "symbolName": "MyNamespace.MyClass.MyMethod"
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

