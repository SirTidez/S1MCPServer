use std::sync::Arc;

use rmcp::model::{CallToolResult, Content, JsonObject, Tool};
use serde_json::{json, Value};

use crate::server_state::ServerState;
use crate::tools::{game, inspect, item, npc, player, world};

#[derive(Debug, Clone, Copy)]
pub struct ToolDescriptor {
    pub name: &'static str,
    pub description: &'static str,
}

pub const TOOL_DESCRIPTORS: [ToolDescriptor; 6] = [
    ToolDescriptor {
        name: "s1_player",
        description: "Interact with the player character (state, inventory, teleport, add items).",
    },
    ToolDescriptor {
        name: "s1_npc",
        description: "Interact with NPCs (list/get/teleport/set health).",
    },
    ToolDescriptor {
        name: "s1_item",
        description: "Interact with game items (list/get/spawn).",
    },
    ToolDescriptor {
        name: "s1_world",
        description: "Query world state, locations, vehicles, logs, and saves.",
    },
    ToolDescriptor {
        name: "s1_inspect",
        description: "Inspect and manipulate Unity GameObjects via reflection.",
    },
    ToolDescriptor {
        name: "s1_game",
        description: "Manage game lifecycle and search S1API documentation.",
    },
];

pub fn list_tools() -> Vec<Tool> {
    TOOL_DESCRIPTORS
        .iter()
        .map(|tool| {
            Tool::new(
                tool.name,
                tool.description,
                Arc::new(input_schema_for_tool(tool.name)),
            )
        })
        .collect()
}

pub async fn dispatch_tool(
    tool_name: &str,
    arguments: Option<JsonObject>,
    state: &ServerState,
) -> CallToolResult {
    let Some(tool) = find_tool(tool_name) else {
        return text_result(format!(
            "Error: Unknown tool '{}'. Available tools: {}",
            tool_name,
            all_tool_names_csv()
        ));
    };

    if let Err(error_message) = state.can_call_tool(tool.name).await {
        return text_result(error_message);
    }

    match tool.name {
        "s1_player" => player::handle_player(arguments, state).await,
        "s1_npc" => npc::handle_npc(arguments, state).await,
        "s1_item" => item::handle_item(arguments, state).await,
        "s1_world" => world::handle_world(arguments, state).await,
        "s1_inspect" => inspect::handle_inspect(arguments, state).await,
        "s1_game" => game::handle_game(arguments, state).await,
        _ => text_result(format!(
            "Error: Unknown tool '{}'. Available tools: {}",
            tool_name,
            all_tool_names_csv()
        )),
    }
}

fn find_tool(tool_name: &str) -> Option<ToolDescriptor> {
    TOOL_DESCRIPTORS.iter().copied().find(|tool| tool.name == tool_name)
}

fn all_tool_names_csv() -> String {
    TOOL_DESCRIPTORS
        .iter()
        .map(|tool| tool.name)
        .collect::<Vec<_>>()
        .join(", ")
}

fn text_result(text: String) -> CallToolResult {
    CallToolResult::success(vec![Content::text(text)])
}

fn input_schema_for_tool(tool_name: &str) -> JsonObject {
    let schema = match tool_name {
        "s1_player" => json!({
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["get", "get_inventory", "teleport", "add_item"],
                    "description": "Operation to perform"
                },
                "position": {
                    "type": "object",
                    "properties": {
                        "x": { "type": "number" },
                        "y": { "type": "number" },
                        "z": { "type": "number" }
                    },
                    "required": ["x", "y", "z"],
                    "description": "Target coordinates (teleport only)"
                },
                "item_id": {
                    "type": "string",
                    "description": "Item ID to add (add_item only)"
                },
                "quantity": {
                    "type": "integer",
                    "minimum": 1,
                    "description": "How many to add (add_item, default: 1)",
                    "default": 1
                }
            },
            "required": ["action"]
        }),
        "s1_npc" => json!({
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["list", "get", "teleport", "set_health"],
                    "description": "Operation to perform"
                },
                "npc_id": {
                    "type": "string",
                    "description": "NPC unique identifier (get/teleport/set_health)"
                },
                "filter": {
                    "type": "string",
                    "enum": ["conscious", "unconscious", "in_building", "in_vehicle"],
                    "description": "Optional filter for list action"
                },
                "position": {
                    "type": "object",
                    "properties": {
                        "x": { "type": "number" },
                        "y": { "type": "number" },
                        "z": { "type": "number" }
                    },
                    "required": ["x", "y", "z"],
                    "description": "Target coordinates (teleport only)"
                },
                "health": {
                    "type": "number",
                    "description": "New health value (set_health only)"
                }
            },
            "required": ["action"]
        }),
        "s1_item" => json!({
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["list", "get", "spawn"],
                    "description": "Operation to perform"
                },
                "item_id": {
                    "type": "string",
                    "description": "Item unique identifier (get/spawn)"
                },
                "category": {
                    "type": "string",
                    "description": "Category filter (list only)"
                },
                "position": {
                    "type": "object",
                    "properties": {
                        "x": { "type": "number" },
                        "y": { "type": "number" },
                        "z": { "type": "number" }
                    },
                    "required": ["x", "y", "z"],
                    "description": "Spawn coordinates (spawn only)"
                },
                "quantity": {
                    "type": "integer",
                    "minimum": 1,
                    "description": "Number to spawn (spawn only, default: 1)",
                    "default": 1
                }
            },
            "required": ["action"]
        }),
        "s1_world" => json!({
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": [
                        "get_state", "list_locations", "get_location",
                        "list_vehicles", "get_vehicle",
                        "capture_logs", "list_saves", "load_save"
                    ],
                    "description": "Operation to perform"
                },
                "property_id": {
                    "type": "string",
                    "description": "Property unique identifier (get_location)"
                },
                "property_name": {
                    "type": "string",
                    "description": "Property name as alternative to property_id (get_location)"
                },
                "vehicle_id": {
                    "type": "string",
                    "description": "Vehicle unique identifier (get_vehicle)"
                },
                "slot_index": {
                    "type": "integer",
                    "minimum": 0,
                    "description": "0-based save slot index (load_save)"
                },
                "last_n_lines": {
                    "type": "integer",
                    "minimum": 1,
                    "description": "Return last N log lines (capture_logs)"
                },
                "first_n_lines": {
                    "type": "integer",
                    "minimum": 1,
                    "description": "Return first N log lines (capture_logs)"
                },
                "keyword": {
                    "type": "string",
                    "description": "Filter logs by keyword (capture_logs)"
                },
                "from_timestamp": {
                    "type": "string",
                    "description": "Filter logs from timestamp HH:mm:ss (capture_logs)"
                },
                "to_timestamp": {
                    "type": "string",
                    "description": "Filter logs to timestamp HH:mm:ss (capture_logs)"
                },
                "include_pattern": {
                    "type": "string",
                    "description": "Regex pattern — only matching lines (capture_logs)"
                },
                "exclude_pattern": {
                    "type": "string",
                    "description": "Regex pattern — exclude matching lines (capture_logs)"
                }
            },
            "required": ["action"]
        }),
        "s1_inspect" => json!({
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": [
                        "find_objects", "find_by_type", "search_types",
                        "list_scenes", "get_hierarchy", "get_scene_objects",
                        "inspect_object", "list_components", "get_component", "inspect_component",
                        "get_member", "inspect_type", "list_members", "get_transform", "is_active",
                        "get_field", "set_field", "get_component_property", "set_component_property",
                        "call_method", "set_transform", "set_active"
                    ],
                    "description": "Operation to perform"
                },
                "object_name": {
                    "type": "string",
                    "description": "Name of the GameObject"
                },
                "object_type": {
                    "type": "string",
                    "description": "Object type hint for inspect_object (default: GameObject)"
                },
                "component_type": {
                    "type": "string",
                    "description": "Component type name (partial match supported)"
                },
                "type_name": {
                    "type": "string",
                    "description": "Type name for inspect_type / list_members"
                },
                "member_path": {
                    "type": "string",
                    "description": "Dot-notation path for get_member (e.g. 'health.current')"
                },
                "field_name": {
                    "type": "string",
                    "description": "Field name for get_field / set_field"
                },
                "property_name": {
                    "type": "string",
                    "description": "Property name for get_component_property / set_component_property"
                },
                "method_name": {
                    "type": "string",
                    "description": "Method name for call_method"
                },
                "value": {
                    "description": "Value to set (set_field / set_component_property)"
                },
                "args": {
                    "type": "array",
                    "description": "Arguments for call_method"
                },
                "name_pattern": {
                    "type": "string",
                    "description": "Name substring filter for find_objects (case-insensitive)"
                },
                "tag": {
                    "type": "string",
                    "description": "Tag filter for find_objects"
                },
                "layer": {
                    "type": "string",
                    "description": "Layer filter for find_objects"
                },
                "pattern": {
                    "type": "string",
                    "description": "Type name pattern to search for (search_types)"
                },
                "component_types_only": {
                    "type": "boolean",
                    "description": "Limit search_types results to Component subclasses only"
                },
                "include_inactive": {
                    "type": "boolean",
                    "description": "Include inactive objects in find_by_type"
                },
                "scene_name": {
                    "type": "string",
                    "description": "Scene name for get_hierarchy / get_scene_objects"
                },
                "max_depth": {
                    "type": "integer",
                    "description": "Max reflection depth for inspect_component (default: 3)",
                    "default": 3
                },
                "member_type": {
                    "type": "string",
                    "enum": ["fields", "properties", "methods", "all"],
                    "description": "Member kind filter for list_members (default: all)"
                },
                "active": {
                    "type": "boolean",
                    "description": "Active state for set_active"
                },
                "position": {
                    "type": "object",
                    "properties": {
                        "x": { "type": "number" },
                        "y": { "type": "number" },
                        "z": { "type": "number" }
                    },
                    "description": "Position for set_transform"
                },
                "rotation": {
                    "type": "object",
                    "properties": {
                        "x": { "type": "number" },
                        "y": { "type": "number" },
                        "z": { "type": "number" }
                    },
                    "description": "Euler rotation for set_transform"
                },
                "scale": {
                    "type": "object",
                    "properties": {
                        "x": { "type": "number" },
                        "y": { "type": "number" },
                        "z": { "type": "number" }
                    },
                    "description": "Scale for set_transform"
                }
            },
            "required": ["action"]
        }),
        "s1_game" => json!({
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["launch", "close", "process_info", "search_docs"],
                    "description": "Operation to perform"
                },
                "version": {
                    "type": "string",
                    "enum": ["il2cpp", "mono"],
                    "description": "Game version to launch (launch only)"
                },
                "enable_debugger": {
                    "type": "boolean",
                    "description": "Enable MelonLoader debugger (launch only)",
                    "default": false
                },
                "wait_for_connection": {
                    "type": "boolean",
                    "description": "Wait for mod server connection after launch",
                    "default": true
                },
                "topic": {
                    "type": "string",
                    "description": "Documentation search topic (search_docs only)"
                },
                "tokens": {
                    "type": "integer",
                    "description": "Max tokens to retrieve (search_docs, default: 5000)",
                    "default": 5000
                }
            },
            "required": ["action"]
        }),
        _ => json!({
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": [],
                    "description": "Operation to perform"
                }
            },
            "required": ["action"]
        }),
    };

    schema_to_object(schema)
}

fn schema_to_object(schema: Value) -> JsonObject {
    schema.as_object().cloned().unwrap_or_default()
}
