use std::sync::Arc;

use rmcp::model::{CallToolResult, Content, JsonObject, Tool};
use schemars::{schema_for, JsonSchema};
use serde::Deserialize;
use serde_json::Value;

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

// ---------------------------------------------------------------------------
// Typed argument structs (derive Deserialize + JsonSchema)
// ---------------------------------------------------------------------------

/// xyz position used by multiple tools.
#[derive(Debug, Deserialize, JsonSchema)]
pub struct Position {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

// --- s1_player ---

#[derive(Debug, Deserialize, JsonSchema)]
pub struct PlayerArgs {
    /// Operation to perform.
    #[schemars(schema_with = "player_action_schema")]
    pub action: String,
    /// Target coordinates (teleport only).
    pub position: Option<Position>,
    /// Item ID to add (add_item only).
    pub item_id: Option<String>,
    /// How many to add (add_item, default: 1).
    #[schemars(range(min = 1))]
    pub quantity: Option<u64>,
}

fn player_action_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(gen, &["get", "get_inventory", "teleport", "add_item"])
}

// --- s1_npc ---

#[derive(Debug, Deserialize, JsonSchema)]
pub struct NpcArgs {
    /// Operation to perform.
    #[schemars(schema_with = "npc_action_schema")]
    pub action: String,
    /// NPC unique identifier (get/teleport/set_health).
    pub npc_id: Option<String>,
    /// Optional filter for list action.
    #[schemars(schema_with = "npc_filter_schema")]
    pub filter: Option<String>,
    /// Target coordinates (teleport only).
    pub position: Option<Position>,
    /// New health value (set_health only).
    pub health: Option<f64>,
}

fn npc_action_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(gen, &["list", "get", "teleport", "set_health"])
}

fn npc_filter_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(
        gen,
        &["conscious", "unconscious", "in_building", "in_vehicle"],
    )
}

// --- s1_item ---

#[derive(Debug, Deserialize, JsonSchema)]
pub struct ItemArgs {
    /// Operation to perform.
    #[schemars(schema_with = "item_action_schema")]
    pub action: String,
    /// Item unique identifier (get/spawn).
    pub item_id: Option<String>,
    /// Category filter (list only).
    pub category: Option<String>,
    /// Spawn coordinates (spawn only).
    pub position: Option<Position>,
    /// Number to spawn (spawn only, default: 1).
    #[schemars(range(min = 1))]
    pub quantity: Option<u64>,
}

fn item_action_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(gen, &["list", "get", "spawn"])
}

// --- s1_world ---

#[derive(Debug, Deserialize, JsonSchema)]
pub struct WorldArgs {
    /// Operation to perform.
    #[schemars(schema_with = "world_action_schema")]
    pub action: String,
    /// Property unique identifier (get_location).
    pub property_id: Option<String>,
    /// Property name as alternative to property_id (get_location).
    pub property_name: Option<String>,
    /// Vehicle unique identifier (get_vehicle).
    pub vehicle_id: Option<String>,
    /// 0-based save slot index (load_save).
    #[schemars(range(min = 0))]
    pub slot_index: Option<u64>,
    /// Return last N log lines (capture_logs).
    #[schemars(range(min = 1))]
    pub last_n_lines: Option<u64>,
    /// Return first N log lines (capture_logs).
    #[schemars(range(min = 1))]
    pub first_n_lines: Option<u64>,
    /// Filter logs by keyword (capture_logs).
    pub keyword: Option<String>,
    /// Filter logs from timestamp HH:mm:ss (capture_logs).
    pub from_timestamp: Option<String>,
    /// Filter logs to timestamp HH:mm:ss (capture_logs).
    pub to_timestamp: Option<String>,
    /// Regex pattern — only matching lines (capture_logs).
    pub include_pattern: Option<String>,
    /// Regex pattern — exclude matching lines (capture_logs).
    pub exclude_pattern: Option<String>,
}

fn world_action_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(
        gen,
        &[
            "get_state",
            "list_locations",
            "get_location",
            "list_vehicles",
            "get_vehicle",
            "capture_logs",
            "list_saves",
            "load_save",
        ],
    )
}

// --- s1_inspect ---

#[derive(Debug, Deserialize, JsonSchema)]
pub struct InspectArgs {
    /// Operation to perform.
    #[schemars(schema_with = "inspect_action_schema")]
    pub action: String,
    /// Name of the GameObject.
    pub object_name: Option<String>,
    /// Object type hint for inspect_object (default: GameObject).
    pub object_type: Option<String>,
    /// Component type name (partial match supported).
    pub component_type: Option<String>,
    /// Type name for inspect_type / list_members.
    pub type_name: Option<String>,
    /// Dot-notation path for get_member (e.g. 'health.current').
    pub member_path: Option<String>,
    /// Field name for get_field / set_field.
    pub field_name: Option<String>,
    /// Property name for get_component_property / set_component_property.
    pub property_name: Option<String>,
    /// Method name for call_method.
    pub method_name: Option<String>,
    /// Value to set (set_field / set_component_property).
    pub value: Option<Value>,
    /// Arguments for call_method.
    pub args: Option<Vec<Value>>,
    /// Name substring filter for find_objects (case-insensitive).
    pub name_pattern: Option<String>,
    /// Tag filter for find_objects.
    pub tag: Option<String>,
    /// Layer filter for find_objects.
    pub layer: Option<String>,
    /// Type name pattern to search for (search_types).
    pub pattern: Option<String>,
    /// Limit search_types results to Component subclasses only.
    pub component_types_only: Option<bool>,
    /// Include inactive objects in find_by_type.
    pub include_inactive: Option<bool>,
    /// Scene name for get_hierarchy / get_scene_objects.
    pub scene_name: Option<String>,
    /// Max reflection depth for inspect_component (default: 3).
    pub max_depth: Option<u32>,
    /// Member kind filter for list_members (default: all).
    #[schemars(schema_with = "member_type_schema")]
    pub member_type: Option<String>,
    /// Active state for set_active.
    pub active: Option<bool>,
    /// Position for set_transform.
    pub position: Option<Position>,
    /// Euler rotation for set_transform.
    pub rotation: Option<Position>,
    /// Scale for set_transform.
    pub scale: Option<Position>,
}

fn inspect_action_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(
        gen,
        &[
            "find_objects",
            "find_by_type",
            "search_types",
            "list_scenes",
            "get_hierarchy",
            "get_scene_objects",
            "inspect_object",
            "list_components",
            "get_component",
            "inspect_component",
            "get_member",
            "inspect_type",
            "list_members",
            "get_transform",
            "is_active",
            "get_field",
            "set_field",
            "get_component_property",
            "set_component_property",
            "call_method",
            "set_transform",
            "set_active",
        ],
    )
}

fn member_type_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(gen, &["fields", "properties", "methods", "all"])
}

// --- s1_game ---

#[derive(Debug, Deserialize, JsonSchema)]
pub struct GameArgs {
    /// Operation to perform.
    #[schemars(schema_with = "game_action_schema")]
    pub action: String,
    /// Game version to launch (launch only).
    #[schemars(schema_with = "game_version_schema")]
    pub version: Option<String>,
    /// Enable MelonLoader debugger (launch only).
    pub enable_debugger: Option<bool>,
    /// Wait for mod server connection after launch.
    pub wait_for_connection: Option<bool>,
    /// Documentation search topic (search_docs only).
    pub topic: Option<String>,
    /// Max tokens to retrieve (search_docs, default: 5000).
    pub tokens: Option<u32>,
}

fn game_action_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(gen, &["launch", "close", "process_info", "search_docs"])
}

fn game_version_schema(gen: &mut schemars::gen::SchemaGenerator) -> schemars::schema::Schema {
    enum_schema(gen, &["il2cpp", "mono"])
}

// ---------------------------------------------------------------------------
// Schema helpers
// ---------------------------------------------------------------------------

/// Build a string enum schema from a static list of variants.
fn enum_schema(
    _gen: &mut schemars::gen::SchemaGenerator,
    variants: &[&str],
) -> schemars::schema::Schema {
    use schemars::schema::{InstanceType, SchemaObject, StringValidation};
    schemars::schema::Schema::Object(SchemaObject {
        instance_type: Some(InstanceType::String.into()),
        enum_values: Some(variants.iter().map(|v| Value::String(v.to_string())).collect()),
        string: Some(Box::new(StringValidation::default())),
        ..Default::default()
    })
}

/// Convert a `schemars` root schema into a `JsonObject` suitable for rmcp.
fn schema_to_json_object<T: JsonSchema>() -> JsonObject {
    let root = schema_for!(T);
    let value = serde_json::to_value(&root).unwrap_or_default();
    value.as_object().cloned().unwrap_or_default()
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

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
    TOOL_DESCRIPTORS
        .iter()
        .copied()
        .find(|tool| tool.name == tool_name)
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
    match tool_name {
        "s1_player" => schema_to_json_object::<PlayerArgs>(),
        "s1_npc" => schema_to_json_object::<NpcArgs>(),
        "s1_item" => schema_to_json_object::<ItemArgs>(),
        "s1_world" => schema_to_json_object::<WorldArgs>(),
        "s1_inspect" => schema_to_json_object::<InspectArgs>(),
        "s1_game" => schema_to_json_object::<GameArgs>(),
        _ => JsonObject::default(),
    }
}
