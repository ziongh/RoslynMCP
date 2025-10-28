# RoslynMCP JSON Output Reference

This document provides complete JSON response schemas for all RoslynMCP tools when `outputAsJson: true` is specified.

## Common Pattern

All JSON responses follow this base structure:

```typescript
{
  success: boolean,     // true if operation succeeded, false otherwise
  error?: string,       // Error message (only present when success = false)
  // ... tool-specific fields
}
```

---

## Solution Management Tools

### GetSolutionStatus

```typescript
{
  success: boolean,
  error?: string,
  isLoaded: boolean,
  solutionPath?: string,
  solutionFileName?: string,
  projectCount: number,
  projectNames: string[],
  status: string
}
```

### ReloadSolution

```typescript
{
  success: boolean,
  error?: string,
  message: string,
  duration?: TimeSpan
}
```

### NotifyCodeChanges

```typescript
{
  success: boolean,
  error?: string,
  message: string,
  filesUpdated: number,
  updatedFiles: string[],
  duration?: TimeSpan
}
```

---

## Code Query Tools

### SearchSymbols

```typescript
{
  success: boolean,
  error?: string,
  pattern: string,
  solutionFileName: string,
  symbolTypes: string,
  caseSensitive: boolean,
  excludeGeneratedFiles: boolean,
  excludeSystemTypes: boolean,
  totalCount: number,
  displayedCount: number,
  isTruncated: boolean,
  results: SymbolSearchResult[]
}

interface SymbolSearchResult {
  name: string,
  fullName: string,
  category: string,
  location: string,
  projectName: string,
  filePath: string,
  lineNumber: number,
  summary: string,
  accessibility: string,
  symbolKind: string,
  namespace: string
}
```

### GetSymbolDetails

```typescript
{
  success: boolean,
  error?: string,
  symbolName: string,
  solutionFileName: string,
  analysisDate: DateTime,
  basicInfo?: SymbolDetails,
  sourceCode?: string,
  references: ReferenceLocation[],
  inheritanceHierarchy?: InheritanceHierarchy
}

interface SymbolDetails {
  name: string,
  fullName: string,
  symbolKind: string,
  accessibility: string,
  namespace: string,
  assemblyName: string,
  members: string[],
  baseTypes: string[],
  interfaces: string[],
  documentation: string,
  sourceLocation: string,
  isFromMetadata: boolean
}
```

### GetSourceCode

```typescript
{
  success: boolean,
  error?: string,
  symbolName: string,
  sourceCode?: string,
  isFromMetadata: boolean
}
```

### GetFileContent

```typescript
{
  success: boolean,
  error?: string,
  filePath: string,
  fileName: string,
  fileExtension: string,
  content?: string,
  lineCount: number
}
```

---

## Relationship Analysis Tools

### FindReferences

```typescript
{
  success: boolean,
  error?: string,
  symbolName: string,
  solutionFileName: string,
  includeDefinition: boolean,
  excludeGeneratedFiles: boolean,
  totalCount: number,
  displayedCount: number,
  isTruncated: boolean,
  references: ReferenceLocation[],
  groupedByFile: Record<string, ReferenceLocation[]>
}

interface ReferenceLocation {
  symbolName: string,
  documentPath: string,
  projectName: string,
  lineNumber: number,
  columnNumber: number,
  lineText: string,
  context: string,
  isDefinition: boolean,
  referenceKind: string
}
```

### GetInheritanceHierarchy

```typescript
{
  success: boolean,
  error?: string,
  symbolName: string,
  hierarchy?: InheritanceHierarchy
}

interface InheritanceHierarchy {
  baseNode?: HierarchyNode,
  derivedNodes: HierarchyNode[]
}

interface HierarchyNode {
  symbolName: string,
  children: HierarchyNode[]
}
```

### GetMethodBodyInvocations

```typescript
{
  success: boolean,
  error?: string,
  methodName: string,
  projectName?: string,
  totalCount: number,
  displayedCount: number,
  isTruncated: boolean,
  invocations: MethodInvocation[],
  groupedByType: Record<string, MethodInvocation[]>
}

interface MethodInvocation {
  calledMethodName: string,
  containingType: string,
  filePath: string,
  lineNumber: number
}
```

---

## Project Management Tools

### ListProjects

```typescript
{
  success: boolean,
  error?: string,
  solutionFileName: string,
  totalProjectCount: number,
  displayedProjectCount: number,
  isTruncated: boolean,
  projects: ProjectInfo[]
}

interface ProjectInfo {
  name: string,
  filePath: string,
  language: string,
  documentPaths: string[],
  projectReferences: string[],
  packageReferences: string[]
}
```

### GetProjectDependencies

```typescript
{
  success: boolean,
  error?: string,
  projectName: string,
  project?: ProjectInfo
}
```

---

## Usage Example

```json
{
  "name": "SearchSymbols",
  "arguments": {
    "pattern": "*Service",
    "symbolTypes": "class,interface",
    "maxResults": 50,
    "outputAsJson": true
  }
}
```

**Response:**
```json
{
  "success": true,
  "pattern": "*Service",
  "solutionFileName": "MySolution.sln",
  "symbolTypes": "class,interface",
  "caseSensitive": true,
  "excludeGeneratedFiles": true,
  "excludeSystemTypes": true,
  "totalCount": 23,
  "displayedCount": 23,
  "isTruncated": false,
  "results": [
    {
      "name": "UserService",
      "fullName": "MyApp.Services.UserService",
      "category": "class",
      "location": "src/Services/UserService.cs:15",
      "projectName": "MyApp",
      "filePath": "src/Services/UserService.cs",
      "lineNumber": 15,
      "summary": "Handles user-related operations",
      "accessibility": "Public",
      "symbolKind": "NamedType",
      "namespace": "MyApp.Services"
    }
    // ... more results
  ]
}
```

---

## Best Practices for LLM Agents

1. **Always check `success` field first** - Only process data if `success: true`
2. **Handle `error` gracefully** - Display or log the error message for debugging
3. **Respect `isTruncated` flag** - Increase `maxResults` if needed
4. **Parse structured data** - Use the strongly-typed schemas for reliable data extraction
5. **Normalize paths** - All file paths are relative to solution root
6. **Cache responses** - Reuse JSON responses to avoid redundant queries

---

## Error Response Format

When an operation fails, the response always includes:

```json
{
  "success": false,
  "error": "Detailed error message explaining what went wrong"
}
```

Common error scenarios:
- Solution not loaded
- Invalid parameters
- Symbol not found
- File not found
- Service unavailable
