# S1MCPClientRust Commit Plan

This plan groups migration work into reviewable commits that preserve behavior parity while reducing integration risk.

## Grouped Change Sets

### 1) Foundation

Scope:

- Rust crate scaffolding, entrypoint, and module exports.
- Config loading defaults + logging initialization.
- Baseline runtime boot behavior and local config template.

Why this group exists:

- Establishes a stable executable shell first so all later parity work lands on a consistent runtime base.

### 2) Transport

Scope:

- JSON request/response/ack models.
- Length-prefixed protocol codec.
- TCP client connection, reconnect path, and transport error types.

Why this group exists:

- Isolates wire compatibility and resiliency concerns before MCP-facing behavior is added.

### 3) MCP Core

Scope:

- `rmcp` stdio server wiring and startup flow.
- Server state gate + handshake instruction storage.
- Central MCP tool registration and dispatch scaffolding.

Why this group exists:

- Creates the MCP contract boundary early so tool work can focus on parity logic instead of runtime plumbing.

### 4) Tools

Scope:

- Tool handler parity for `s1_player`, `s1_npc`, `s1_item`, `s1_world`, `s1_inspect`, `s1_game`.
- Shared tool formatting/helpers and dispatch integration.
- Context7 docs lookup and game lifecycle support modules.

Why this group exists:

- Delivers user-visible feature parity in one coherent surface with aligned request/response behavior.

### 5) Hardening

Scope:

- Input schema tightening and formatting alignment to Python behavior.
- Shared settings usage for game lifecycle actions.
- Heartbeat scheduler reliability improvements and retry/backoff behavior.
- Config diagnostics and targeted test coverage additions.

Why this group exists:

- Reduces production failure modes after parity is functionally complete.

### 6) Docs

Scope:

- Rust README usage/integration guidance.
- Native-binary MCP smoke-test instructions for Windows.
- Commit/changelog grouping guidance for final rollout.

Why this group exists:

- Makes migration operational for real users and maintainers, not only compilable.

## Suggested Commit Order

1. Foundation
2. Transport
3. MCP Core
4. Tools
5. Hardening
6. Docs

This order keeps each commit independently understandable and minimizes bisect noise when diagnosing regressions.

## Draft Commit Messages (Why-Focused)

1. `chore(rust): scaffold MCP client runtime to enable side-by-side migration`
2. `feat(rust-transport): add framed TCP protocol layer for stable mod communication`
3. `feat(rust-mcp): wire rmcp stdio server so clients can speak MCP to Rust runtime`
4. `feat(rust-tools): port core Schedule I tools to preserve Python workflow parity`
5. `fix(rust): harden connection lifecycle and diagnostics to reduce integration flakiness`
6. `docs(rust): publish usage, smoke tests, and rollout commit plan for maintainers`

## Pre-Commit Verification Commands

Run from repo root:

```bash
cargo check --manifest-path S1MCPClientRust/Cargo.toml
cargo test --manifest-path S1MCPClientRust/Cargo.toml
```

Optional manual integration verification (Windows):

```bash
cargo build --release --manifest-path S1MCPClientRust/Cargo.toml
```

Then run the README smoke test flow with the native binary configured in your MCP client.
