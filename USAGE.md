# RoslynMCP 服务器使用指南


## 🛠️ 可用工具

所有代码分析工具都依赖于一个已加载的解决方案。请确保在调用它们之前，已经通过环境变量或参数成功加载了一个解决方案。

### 类别 1: 解决方案管理

这类工具用于管理分析器的工作环境。

---

#### **`GetSolutionStatus`**
获取当前解决方案的加载状态、服务初始化信息以及已加载项目的摘要。

*   **参数**: 无
*   **示例**:
    ```json
    {
      "name": "GetSolutionStatus",
      "arguments": {}
    }
    ```



### 类别 2: 代码查询与分析

这类工具用于深度探索和分析已加载的解决方案。

---

#### **`SearchSymbols`**
使用通配符 (`*`, `?`) 在 C# 代码中搜索符号。

*   **参数**:
    *   `pattern` (string, **必需**): 用于搜索的通配符模式 (例如 `'*Service'`)。
    *   `symbolTypes` (string, *可选*): 要包含的符号类型，逗号分隔。有效选项: `'class'`, `'interface'`, `'method'`, `'property'`, `'field'`, `'enum'`。 (默认: `'class,interface,method,property'`)。
    *   `maxResults` (int, *可选*): 返回的最大结果数 (默认: 20)。
    *   `excludeGeneratedFiles` (bool, *可选*): 是否排除自动生成的文件 (默认: `true`)。
    *   `excludeSystemTypes` (bool, *可选*): 是否排除系统类型，如构造函数 (默认: `true`)。
    *   `caseSensitive` (bool, *可选*): 搜索是否区分大小写 (默认: `true`)。
*   **示例**:
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
获取一个符号的完整聚合分析，包括源代码、引用和继承层次结构。

*   **参数**:
    *   `symbolName` (string, **必需**): 确切的符号名称或完全限定名称。
    *   `includeSourceCode` (bool, *可选*): 是否在响应中包含源代码 (默认: `true`)。
    *   `includeReferences` (bool, *可选*): 是否在响应中包含引用信息 (默认: `true`)。
    *   `includeInheritanceHierarchy` (bool, *可选*): 是否在响应中包含继承信息 (默认: `true`)。
    *   `maxReferences` (int, *可选*): 最多包含的引用数量 (默认: 20)。
*   **示例**:
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
获取特定符号（类、方法、属性等）的完整源代码。

*   **参数**:
    *   `symbolName` (string, **必需**): 确切的符号名称或完全限定名称。
*   **示例**:
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
获取解决方案中一个源文件的完整内容。

*   **参数**:
    *   `filePath` (string, **必需**): 文件的路径，可以是绝对路径或相对于解决方案根目录的路径。
*   **示例**:
    ```json
    {
      "name": "GetFileContent",
      "arguments": {
        "filePath": "src/MyProject/MyFile.cs"
      }
    }
    ```

### 类别 3: 关系与结构分析

这类工具用于理解代码单元之间的相互关系和项目的整体结构。

---

#### **`FindReferences`**
查找特定符号的所有代码引用。

*   **参数**:
    *   `symbolName` (string, **必需**): 要查找引用的确切符号名称。
    *   `includeDefinition` (bool, *可选*): 是否在结果中包含符号自身的定义 (默认: `true`)。
    *   `maxResults` (int, *可选*): 返回的最大引用数 (默认: 20)。
    *   `excludeGeneratedFiles` (bool, *可选*): 是否排除自动生成文件中的引用 (默认: `true`)。
*   **示例**:
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
获取一个类或接口的继承层次结构（基类和派生类）。

*   **参数**:
    *   `symbolName` (string, **必需**): 要分析的符号名称。
*   **示例**:
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
获取一个特定方法体内部的所有方法调用。

*   **参数**:
    *   `methodName` (string, **必需**): 要分析的方法名称，可以是部分或完全限定名称 (例如 `'MyMethod'` 或 `'MyClass.MyMethod'`)。
    *   `projectName` (string, *可选*): 将搜索范围限定于此项目名称。使用 `ListProjects` 获取项目名称。
*   **示例**:
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
列出当前解决方案中的所有项目及其依赖项。

*   **参数**:
    *   `maxProjects` (int, *可选*): 最多显示的项目数量 (默认: 15, 使用 `-1` 显示全部)。
    *   `maxPackages` (int, *可选*): 每个项目最多显示的包引用数量 (默认: 5, 使用 `-1` 显示全部)。
    *   `showSystemPackages` (bool, *可选*): 是否在包引用详情中显示系统包 (默认: `false`)。
*   **示例**:
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
获取单个项目的详细依赖项（项目引用和包引用）。

*   **参数**:
    *   `projectName` (string, **必需**): 要分析的项目名称。
*   **示例**:
    ```json
    {
      "name": "GetProjectDependencies",
      "arguments": {
        "projectName": "MyProject.Core"
      }
    }
    ```

