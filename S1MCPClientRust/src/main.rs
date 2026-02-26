use s1_mcp_client_rust::config::Settings;
use s1_mcp_client_rust::logging::init_logger;
use s1_mcp_client_rust::mcp::run_stdio_server;
use s1_mcp_client_rust::server_state::ServerState;
use std::sync::Arc;
use tracing::{error, info, warn};

#[tokio::main(flavor = "multi_thread")]
async fn main() {
    let report = Settings::load_with_diagnostics();
    let settings = report.settings;
    init_logger(&settings.log_level);

    for diagnostic in report.diagnostics {
        warn!(diagnostic_kind = ?diagnostic.kind, message = %diagnostic.message, "Configuration warning");
    }

    info!(
        host = %settings.host,
        port = settings.port,
        "S1MCPClientRust startup"
    );

    let state = Arc::new(ServerState::from_settings(&settings));
    state.startup_handshake().await;

    if let Err(error) = run_stdio_server(state).await {
        error!(error = %error, "MCP stdio server stopped with error");
        std::process::exit(1);
    }
}
