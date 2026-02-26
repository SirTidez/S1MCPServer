# TASK_MASTER.md

## Rust Refactor Master Plan

Project: Migrate Python MCP server (`S1MCPClient`) to a side-by-side Rust implementation in `S1MCPClientRust`.

Implementation mode:
- Side-by-side migration (Python remains intact during parity work)
- MCP SDK approach: `rmcp` + thin stdio adapter if needed
- Output target: native binary
- First pass: exact tool/schema/error parity with current Python server

Execution model:
- Multi-subagent delivery with strict file locks (`FILE_LOCK.md`)
- PM-style orchestration: clear owners, dependencies, acceptance criteria, and status updates
- Work is performed in small vertical slices with parity validation at each phase

### Task Status Legend
- `completed`: done and verified for this phase
- `in_progress`: actively being worked on
- `pending`: not started
- `blocked`: waiting on dependency or lock

---

## Phase 0 - Coordination Scaffolding

### T0.1 Create execution scaffolding docs
- Status: `completed`
- Owner: orchestrator
- Deliverables:
  - `TASK_MASTER.md`
  - `FILE_LOCK.md`
- Notes:
  - This is the "creation of all task scaffolding" milestone requested by user.
  - Baseline coordination artifacts are now active and in use.

---

## Phase 1 - Rust Crate Foundation

### T1.1 Create Rust crate skeleton in `S1MCPClientRust`
- Status: `completed`
- Owner: subagent-rust-foundation
- Files:
  - `S1MCPClientRust/Cargo.toml`
  - `S1MCPClientRust/src/main.rs`
  - `S1MCPClientRust/src/lib.rs` (optional)
- Dependencies:
  - T0.1 completed
- Acceptance criteria:
  - Crate builds with `cargo check`
  - Binary entrypoint exists and starts without panic
  - Module layout leaves room for mcp/tcp/tools/config/logging expansion

### T1.2 Add config + logging modules
- Status: `completed`
- Owner: subagent-rust-foundation
- Files:
  - `S1MCPClientRust/src/config/*`
  - `S1MCPClientRust/src/logging/*`
- Dependencies:
  - T1.1
- Acceptance criteria:
  - Config fields match Python keys needed for parity
  - Structured logger outputs to stderr
  - Main wires config + logger initialization

---

## Phase 2 - Transport and Protocol

### T2.1 Port TCP client and reconnect behavior
- Status: `completed`
- Owner: subagent-rust-transport
- Files:
  - `S1MCPClientRust/src/tcp/*`

### T2.2 Port length-prefixed JSON protocol codec
- Status: `completed`
- Owner: subagent-rust-transport
- Files:
  - `S1MCPClientRust/src/protocol/*` or `S1MCPClientRust/src/tcp/protocol.rs`
  - `S1MCPClientRust/src/models/*`

---

## Phase 3 - MCP Server Core (rmcp)

### T3.1 Implement MCP stdio server with `rmcp`
- Status: `completed`
- Owner: subagent-rust-mcp
- Files:
  - `S1MCPClientRust/src/mcp/*`
  - `S1MCPClientRust/src/main.rs`

### T3.2 Implement connection gate + lazy handshake
- Status: `completed`
- Owner: subagent-rust-mcp
- Files:
  - `S1MCPClientRust/src/mcp/*`
  - `S1MCPClientRust/src/server_state.rs` (if created)

---

## Phase 4 - Tool Parity

### T4.1 Implement `s1_player`
- Status: `completed`
- Owner: subagent-rust-tools-a

### T4.2 Implement `s1_npc`
- Status: `completed`
- Owner: subagent-rust-tools-a

### T4.3 Implement `s1_item`
- Status: `completed`
- Owner: subagent-rust-tools-b

### T4.4 Implement `s1_world`
- Status: `completed`
- Owner: subagent-rust-tools-b

### T4.5 Implement `s1_inspect`
- Status: `completed`
- Owner: subagent-rust-tools-c

### T4.6 Implement `s1_game`
- Status: `completed`
- Owner: subagent-rust-tools-c

### T4.7 Add TODO parity-followup markers
- Status: `completed`
- Owner: subagent-rust-tools-all
- Notes:
  - Add concise `TODO:` comments for post-parity improvements only.

---

## Phase 5 - Lifecycle + Docs Search

### T5.1 Port game lifecycle operations
- Status: `completed`
- Owner: subagent-rust-lifecycle
- Files:
  - `S1MCPClientRust/src/game/*`

### T5.2 Port Context7 docs search
- Status: `completed`
- Owner: subagent-rust-lifecycle
- Files:
  - `S1MCPClientRust/src/docs/*`

---

## Phase 6 - Validation and Documentation

### T6.1 Add Rust unit/integration tests
- Status: `completed`
- Owner: subagent-rust-test
- Files:
  - `S1MCPClientRust/tests/*`

### T6.2 Add Rust usage docs + integration notes
- Status: `completed`
- Owner: subagent-rust-docs
- Files:
  - `S1MCPClientRust/README.md`
  - root README updates (if needed)

---

## Phase 7 - Hardening and Parity Polish

### T7.1 Expand MCP input schemas to full parity
- Status: `completed`
- Owner: subagent-rust-hardening-a
- Files:
  - `S1MCPClientRust/src/mcp/tools.rs`

### T7.2 Use shared server config in `s1_game`
- Status: `completed`
- Owner: subagent-rust-hardening-b
- Files:
  - `S1MCPClientRust/src/server_state.rs`
  - `S1MCPClientRust/src/tools/game.rs`

### T7.3 Refactor heartbeat to async scheduler model
- Status: `completed`
- Owner: subagent-rust-hardening-c
- Files:
  - `S1MCPClientRust/src/tcp/client.rs`

### T7.4 Tighten output formatting parity helpers
- Status: `completed`
- Owner: subagent-rust-hardening-a
- Files:
  - `S1MCPClientRust/src/tools/common.rs`
  - `S1MCPClientRust/src/tools/world.rs`

---

## Phase 8 - Production Readiness

### T8.1 Add config load diagnostics and startup reporting
- Status: `completed`
- Owner: subagent-rust-prod-a
- Files:
  - `S1MCPClientRust/src/config/settings.rs`
  - `S1MCPClientRust/src/main.rs`

### T8.2 Add Context7 retry/backoff hardening
- Status: `completed`
- Owner: subagent-rust-prod-a
- Files:
  - `S1MCPClientRust/src/docs/context7.rs`

### T8.3 Publish MCP smoke-test instructions (Rust binary)
- Status: `completed`
- Owner: subagent-rust-prod-b
- Files:
  - `S1MCPClientRust/README.md`

### T8.4 Draft commit-ready changelog grouping and commit message plan
- Status: `completed`
- Owner: subagent-rust-prod-b
- Files:
  - `S1MCPClientRust/COMMIT_PLAN.md`

---

## Current Stop Point

- None. Phase 8 production readiness is complete.

## Active Iteration

- Sprint: `S3 - Production Readiness Complete`
- Focus tasks:
  - All planned production readiness tasks completed
- Next PM checkpoint:
- Optional: Phase 9 release candidate validation against live game session
