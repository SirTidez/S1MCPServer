# CODE_STANDARDS.md

This document defines coding conventions for the two coordinated projects in this repo:
- `S1MCPServer/` (C# MelonLoader mod)
- `S1MCPClient/` (Python MCP server)

Use these standards to keep changes consistent and safe across the Unity runtime and MCP tooling.

## Cross-Cutting Principles
- Prefer small, focused changes; avoid sweeping refactors unless required.
- Keep behavior stable unless explicitly changing it; avoid log/message churn.
- Validate inputs early and return actionable errors.
- When editing protocol payloads, update both client and server in the same change.
- Preserve existing formatting and structure in each file.

## C# Standards (S1MCPServer)

### Naming
- Types, public methods, and properties: `PascalCase`.
- Locals and parameters: `camelCase`.
- Private fields: `_camelCase`.
- Constants: `PascalCase`.

### File/Namespace Layout
- Use file-scoped namespaces (`namespace S1MCPServer;`).
- One primary type per file when possible.

### Imports
- Keep `using` statements sorted with BCL first, then project namespaces.
- Avoid unused `using` directives.

### Nullability and Defensive Code
- Project uses `<Nullable>disable</Nullable>`; rely on explicit null checks.
- Guard against missing Unity objects and invalid IDs.
- Prefer explicit checks over null-forgiving operators.

### Threading and Unity Constraints
- Unity object access must run on the main thread (via command handlers).
- TCP/IO work stays on background threads (`TcpServer`).
- Use queues (`CommandQueue`, `ResponseQueue`) for cross-thread coordination.

### Error Handling and Logging
- Catch exceptions at command boundaries.
- Log with `ModLogger` only; no `Console.WriteLine`.
- Use `ProtocolHandler.CreateErrorResponse` for JSON-RPC error replies.
- Include contextual details (method, ID, reason) in error responses.

### Serialization and Models
- Use the existing `Models/` types for request/response payloads.
- Keep JSON-RPC field names stable (`id`, `method`, `params`, `result`, `error`).

## Python Standards (S1MCPClient)

### Naming and Structure
- Modules and functions: `snake_case`.
- Classes: `PascalCase`.
- Constants: `UPPER_SNAKE_CASE`.

### Types and Async
- Use type hints for public functions and tool handlers.
- Prefer `async def` + `await` for tool handlers and network calls.

### Imports
- Standard library, third-party, then local imports.
- Avoid wildcard imports.

### Logging and Errors
- Use module-level `logger` from `utils.logger`.
- Do not `print` for runtime logs.
- Tool handlers must return `list[TextContent]` on both success and failure.
- Convert exceptions to user-friendly `TextContent` error messages.

### Tool Registry
- Keep tool schemas and handlers in `src/tools/`.
- Register handlers in `TOOL_HANDLERS` with unique keys.
- Register tools in `src/main.py`; avoid duplicate names.

### Configuration
- Read config via `Config.from_file()`.
- Treat `config.json` as optional; handle missing values gracefully.

## Tests
- C#: xUnit via `S1MCPServer.Tests` project.
- Python: pytest with `live_game` marker for integration tests.
- Keep unit tests deterministic and independent of the game runtime.

## Formatting Guidance
- No formal formatter configured; follow the existing file style.
- Keep line lengths reasonable; wrap long strings when clarity improves.
- Avoid reformatting unrelated code.

## When Adding Features
- Add server handler + client tool in the same change.
- Update `CommandRouter` registration for new mod commands.
- Keep method names aligned between client and server.
- Add or update tests where feasible.
