# S1MCPClientRust

`S1MCPClientRust` is the side-by-side Rust MCP server for Schedule I.

It lives alongside the existing Python server (`S1MCPClient`) while parity work is completed. The primary delivery target is a native Rust binary. A later phase is planned to embed this MCP runtime into a Rust GUI app.

## Current Status

- Migration mode: side-by-side with Python (Python remains available).
- MCP runtime is implemented with `rmcp` over stdio.
- TCP bridge to the in-game mod (`S1MCPServer`) is implemented with length-prefixed JSON messaging.
- Tool surface is available via six routed MCP tools:
  - `s1_player`
  - `s1_npc`
  - `s1_item`
  - `s1_world`
  - `s1_inspect`
  - `s1_game`

## Parity Notes

- This crate follows the same high-level workflow as the Python MCP server: MCP stdio server -> TCP client -> game mod.
- Tool behavior and error shape are aligned first; some formatting and operational details are tracked as follow-up TODOs (see [Known Gaps / TODOs](#known-gaps--todos)).
- Rust currently groups operations by tool + `action` argument (for example, `s1_player` with `action: "get"`, `"teleport"`, etc.).

## Build, Run, Test

From repo root:

```bash
cargo check --manifest-path S1MCPClientRust/Cargo.toml
cargo test --manifest-path S1MCPClientRust/Cargo.toml
cargo run --manifest-path S1MCPClientRust/Cargo.toml
```

From `S1MCPClientRust/`:

```bash
cargo check
cargo test
cargo run
```

## Configuration

Settings are loaded from:

- `S1MCPClientRust/config.json` (preferred when running from repo root)
- `config.json` (preferred when running with `cwd` set to `S1MCPClientRust`)
- Example template: `S1MCPClientRust/config.json.example`

Optional override:

- `S1MCPCLIENTRUST_CONFIG` environment variable can point to an explicit config file path.

Config shape:

```json
{
  "host": "127.0.0.1",
  "port": 8765,
  "log_level": "info",
  "connection_timeout": 10,
  "reconnect_delay": 3,
  "game_il2cpp_path": "",
  "game_mono_path": "",
  "game_executable": "",
  "game_startup_timeout": 120,
  "game_connection_poll_interval": 2
}
```

Field notes:

- `host`, `port`: TCP endpoint for the C# mod (default `127.0.0.1:8765`).
- `connection_timeout`, `reconnect_delay`: TCP timeout and reconnect pacing (seconds).
- `game_il2cpp_path`, `game_mono_path`, `game_executable`: required for `s1_game` launch/close workflows.
- `game_startup_timeout`, `game_connection_poll_interval`: used by `s1_game` launch polling.

## MCP Behavior Summary

- Handshake:
  - On startup, server attempts a TCP `handshake` call to the game mod.
  - If handshake succeeds, server stores returned `instructions` text and exposes it during MCP initialize.
  - If handshake fails at startup, MCP server still starts; game tools stay gated until reconnect.
- Tool gating:
  - `s1_game` is always callable.
  - All other tools require a valid game connection.
  - When disconnected, server attempts a lazy handshake before returning a not-connected error.
- Tool listing and calls:
  - Tools are listed via MCP `list_tools`.
  - Tool calls route through a central dispatcher and return text content responses.

## Integration Notes

- This server is a drop-in MCP stdio process option for clients that currently point at Python.
- For MCP clients (for example Claude Desktop/Cline), point `command` to the Rust binary (or `cargo run` during development) and set `cwd` to `S1MCPClientRust` if using local `config.json`.
- The Rust process communicates with the same game-side mod endpoint as Python by default (`127.0.0.1:8765`).

## MCP Smoke Test (Windows Native Binary)

Use this checklist to validate end-to-end MCP integration with a real client and the native Rust binary.

Sample MCP client config snippet (binary command):

```json
{
  "mcpServers": {
    "s1-rust": {
      "command": "C:\\Users\\SirTidez\\RiderProjects\\S1MCPServer\\S1MCPClientRust\\target\\release\\s1_mcp_client_rust.exe",
      "args": [],
      "cwd": "C:\\Users\\SirTidez\\RiderProjects\\S1MCPServer\\S1MCPClientRust"
    }
  }
}
```

Practical checklist:

- Build release binary once: `cargo build --release --manifest-path S1MCPClientRust/Cargo.toml`.
- Confirm game mod side is reachable at `127.0.0.1:8765` (Schedule I running with `S1MCPServer` loaded).
- Ensure `S1MCPClientRust/config.json` has correct host/port and optional game paths if testing `s1_game` launch.
- Restart MCP client (Claude Desktop/Cline/etc.) after config change, then verify server `s1-rust` is detected.
- Run `list_tools` and verify six tools are present: `s1_player`, `s1_npc`, `s1_item`, `s1_world`, `s1_inspect`, `s1_game`.
- Run a connected tool call, for example `s1_player` with `action: "get"`, and verify player state is returned.
- Run a disconnected-path check by closing the game, then call `s1_world`; verify a clear not-connected error is returned (not a crash).
- Run `s1_game` action (`process_info` or `launch`) and verify it remains callable even when other tools are gated.

Expected outcomes:

- MCP client initializes successfully over stdio with no protocol errors.
- Tool listing succeeds and names/shape match documented parity surface.
- Connected calls return text content responses with game data.
- Disconnected calls for non-`s1_game` tools return actionable connectivity errors.
- `s1_game` continues to work for lifecycle/status operations regardless of connection gate state.

## Known Gaps / TODOs

Tracked parity follow-ups currently in code:

- `src/game/lifecycle.rs`: preserve additional global connection/instruction state behaviors from Python main flow where relevant.
