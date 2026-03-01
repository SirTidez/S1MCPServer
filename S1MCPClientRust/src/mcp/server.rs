use std::sync::Arc;

use rmcp::model::{
    CallToolRequestParam, CallToolResult, Implementation, InitializeResult, ListToolsResult,
    PaginatedRequestParam, ServerCapabilities, ToolsCapability,
};
use rmcp::service::{RequestContext, ServiceExt};
use rmcp::transport::io::stdio;
use rmcp::{ErrorData as McpError, RoleServer, ServerHandler};
use tracing::info;

use crate::mcp::tools;
use crate::server_state::ServerState;

const SERVER_NAME: &str = "s1mcpclient";
const SERVER_VERSION: &str = "0.1.0";

#[derive(Debug)]
pub struct S1McpServer {
    state: Arc<ServerState>,
}

impl S1McpServer {
    pub fn new(state: Arc<ServerState>) -> Self {
        Self { state }
    }
}

impl ServerHandler for S1McpServer {
    fn get_info(&self) -> InitializeResult {
        InitializeResult {
            protocol_version: Default::default(),
            capabilities: ServerCapabilities {
                tools: Some(ToolsCapability::default()),
                ..ServerCapabilities::default()
            },
            server_info: Implementation {
                name: SERVER_NAME.to_string(),
                title: None,
                version: SERVER_VERSION.to_string(),
                icons: None,
                website_url: None,
            },
            instructions: None,
        }
    }

    async fn initialize(
        &self,
        request: rmcp::model::InitializeRequestParam,
        context: RequestContext<RoleServer>,
    ) -> Result<InitializeResult, McpError> {
        if context.peer.peer_info().is_none() {
            context.peer.set_peer_info(request);
        }

        let mut info = self.get_info();
        info.instructions = self.state.instructions_snapshot().await;
        Ok(info)
    }

    async fn list_tools(
        &self,
        _request: Option<PaginatedRequestParam>,
        _context: RequestContext<RoleServer>,
    ) -> Result<ListToolsResult, McpError> {
        Ok(ListToolsResult::with_all_items(tools::list_tools()))
    }

    async fn call_tool(
        &self,
        request: CallToolRequestParam,
        _context: RequestContext<RoleServer>,
    ) -> Result<CallToolResult, McpError> {
        let result = tools::dispatch_tool(request.name.as_ref(), request.arguments, &self.state).await;
        Ok(result)
    }
}

pub async fn run_stdio_server(state: Arc<ServerState>) -> Result<(), Box<dyn std::error::Error>> {
    let server = S1McpServer::new(state);
    let running = server.serve(stdio()).await?;

    info!(name = SERVER_NAME, version = SERVER_VERSION, "MCP stdio server started");
    let _ = running.waiting().await?;
    Ok(())
}
