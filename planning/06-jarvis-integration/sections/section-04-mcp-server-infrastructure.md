# Section 04: MCP Server Infrastructure

## Overview

Adds Model Context Protocol (MCP) server support to the PBA API project. The MCP server is a separate process spawned by OpenClaw Gateway using stdio transport. It shares the same codebase and service implementations as the HTTP API but runs as an independent process. When the `--mcp` flag is detected at startup, the application registers MCP services and starts the stdio transport instead of the HTTP server.

The MCP server uses the `ModelContextProtocol` NuGet package (v1.1.0+) for the .NET MCP SDK. Tool classes are discovered automatically via assembly scanning of classes decorated with `[McpServerToolType]`.

## Dependencies

- **section-03-scoped-api-keys**: MCP tools operate with write-scope privileges. The MCP process connects directly to the database (no HTTP), so API key scoping does not apply to the MCP process itself. However, when MCP tools create audit entries, the actor is recorded as `jarvis/openclaw` which maps to write-scope access.

## Architecture

The PBA API project produces two runtime modes from the same codebase:

1. **HTTP mode (default):** `dotnet run` or `dotnet PersonalBrandAssistant.Api.dll` starts the normal Minimal API HTTP server with all existing endpoints. This is the mode used by the Angular dashboard, jarvis-monitor, and jarvis-hud.

2. **MCP mode:** `dotnet PersonalBrandAssistant.Api.dll --mcp` starts the MCP stdio server. No HTTP listener is created. The process reads MCP JSON-RPC messages from stdin and writes responses to stdout. OpenClaw Gateway spawns this process and communicates over stdio.

Both modes share the same DI container configuration for services, database context, and infrastructure. The difference is only in the host setup (HTTP vs stdio) and which features are registered.

## Tests (Write First)

Test file: `tests/PersonalBrandAssistant.Application.Tests/McpServer/McpServerInfrastructureTests.cs`

```csharp
// Test: --mcp flag starts MCP server (stdio transport)
//   Use a process-based test that spawns the published binary with --mcp
//   Send an MCP "initialize" JSON-RPC message to stdin
//   Assert the process responds with a valid MCP initialize response on stdout
//   Note: This is a heavier integration test; may be marked [Trait("Category", "Integration")]

// Test: without --mcp flag starts HTTP server (normal API)
//   Use WebApplicationFactory<Program> (default, no --mcp)
//   Assert the HTTP server responds to GET /health

// Test: MCP server discovers all tool classes via assembly scanning
//   Use reflection to find all types with [McpServerToolType] attribute
//   Assert count matches expected (13 tools across 4 tool classes)
//   This can be a simple unit test using typeof(Program).Assembly

// Test: MCP server tool count matches expected (13 tools)
//   Use reflection to find all methods with [McpServerTool] attribute
//   across all [McpServerToolType] classes
//   Assert count == 13
```

## File Paths

### Modified Files

- `src/PersonalBrandAssistant.Api/Program.cs` -- Add `--mcp` flag detection and conditional startup path.
- `src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj` -- Add `ModelContextProtocol` NuGet package reference.

### New Files

- `src/PersonalBrandAssistant.Api/McpTools/` -- Directory for tool classes (created empty; populated by sections 05-08).
- `tests/PersonalBrandAssistant.Application.Tests/McpServer/McpServerInfrastructureTests.cs` -- Infrastructure tests.

## NuGet Package

Add to `PersonalBrandAssistant.Api.csproj`:

```xml
<PackageReference Include="ModelContextProtocol" Version="1.1.0" />
```

This package provides:
- `[McpServerToolType]` attribute for tool class discovery
- `[McpServerTool]` attribute for tool method discovery
- `[Description]` attribute support for tool/parameter documentation
- `AddMcpServer()` service registration extension
- `WithStdioServerTransport()` for stdio communication
- `WithToolsFromAssembly()` for assembly scanning

## Program.cs Modification

The `Program.cs` file needs a conditional branch at the top level. The key design decision: detect `--mcp` early and fork the host building logic.

```csharp
var isMcpMode = args.Contains("--mcp");

if (isMcpMode)
{
    // MCP stdio server path
    var builder = Host.CreateApplicationBuilder(args);

    // Register shared services (same as HTTP mode)
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Register MCP server with stdio transport and assembly scanning
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    await app.RunAsync();
}
else
{
    // Existing HTTP server path (unchanged)
    var builder = WebApplication.CreateBuilder(args);
    // ... all existing HTTP setup ...
}
```

The MCP path uses `Host.CreateApplicationBuilder` (generic host) instead of `WebApplication.CreateBuilder` (web host) because no HTTP infrastructure is needed. The shared service registration (`AddApplication()`, `AddInfrastructure()`) works with both host types since they both use `IServiceCollection`.

The `WithToolsFromAssembly()` call scans the API assembly for all types decorated with `[McpServerToolType]` and registers their `[McpServerTool]` methods as MCP tools. Tool classes in `McpTools/` are discovered automatically without explicit registration.

## Tool Class Structure

Tool classes live in `src/PersonalBrandAssistant.Api/McpTools/` and follow this pattern:

```csharp
[McpServerToolType]
public class ExampleTools
{
    [McpServerTool, Description("Tool description for LLM consumption")]
    public static async Task<string> pba_tool_name(
        IServiceProvider serviceProvider,
        [Description("Parameter description")] string param1,
        CancellationToken ct)
    {
        // Resolve scoped services from the provider
        // Call application service layer
        // Return JSON-serialized result
    }
}
```

Key patterns:
- Tool methods are `static async Task<string>`. The MCP SDK injects `IServiceProvider` as a special parameter.
- Scoped services (like `IApplicationDbContext`) are resolved from `IServiceProvider` per invocation, ensuring proper lifetime management.
- Parameters use `[Description]` attributes that the LLM reads to understand what to pass.
- Return values are JSON strings that the LLM interprets.

## Tool Description Strategy

Each tool method and its parameters must have detailed `[Description]` attributes. These descriptions are the primary mechanism for LLM tool selection -- the LLM reads them to decide which tool to invoke. Descriptions should include:

- **What the tool does** (1 sentence)
- **When to use it** (trigger phrases like "schedule a post", "what's trending")
- **What it returns** (shape of the response)
- **Constraints** (e.g., "content must be in Approved state to publish")

Example:

```csharp
[McpServerTool]
[Description("Creates new content in the PBA pipeline from a topic. Use when asked to 'write a post', 'create content about X', or 'draft something for LinkedIn'. Returns the new content ID and initial status. If autonomy is set to manual, creates a draft for approval rather than auto-publishing.")]
public static async Task<string> pba_create_content(...)
```

## Published Binary

The MCP binary is produced by `dotnet publish` and placed at a known path that OpenClaw can reference. The publish command:

```bash
dotnet publish src/PersonalBrandAssistant.Api -c Release -o /path/to/published/pba-mcp
```

OpenClaw's configuration references this path:

```json
{
  "mcpServers": {
    "pba": {
      "command": "dotnet",
      "args": ["/path/to/published/pba-mcp/PersonalBrandAssistant.Api.dll", "--mcp"],
      "env": {
        "ConnectionStrings__DefaultConnection": "${PBA_DB_CONNECTION}",
        "ApiKey": "${PBA_API_KEY}"
      }
    }
  }
}
```

The `--mcp` argument in args triggers MCP mode. Environment variables provide database connection and API key configuration to the spawned process.

## Implementation Notes

- The MCP process connects to the same PostgreSQL database as the HTTP API. Both processes can run simultaneously without conflict since they operate on the same data with EF Core's concurrency handling.
- Serilog should be configured in MCP mode to log to file only (not stdout), since stdout is reserved for MCP JSON-RPC messages. Configure this in the MCP branch of `Program.cs`.
- The MCP process does not need CORS, Swagger, or exception handler middleware -- these are HTTP-only concerns.
- Tool classes in `McpTools/` should not reference `HttpContext` or any ASP.NET-specific types. They work through the application service layer only.
- The `IServiceProvider` parameter in tool methods provides access to scoped services. Create a scope per tool invocation: `using var scope = serviceProvider.CreateScope();` then resolve services from `scope.ServiceProvider`.
