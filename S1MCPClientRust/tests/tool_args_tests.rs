/// Tests for all 6 typed tool argument structs.
///
/// Focus: serde_json::from_value round-trips, required-field absense,
/// optional defaults, and type mismatches that the new Serde path must catch.
use serde_json::json;

use s1_mcp_client_rust::mcp::tools::{
    GameArgs, InspectArgs, ItemArgs, NpcArgs, PlayerArgs, WorldArgs,
};

// ---------------------------------------------------------------------------
// PlayerArgs
// ---------------------------------------------------------------------------

#[test]
fn player_args_action_required() {
    // Missing `action` must produce a Serde error.
    let result = serde_json::from_value::<PlayerArgs>(json!({}));
    assert!(result.is_err(), "PlayerArgs without action must fail");
}

#[test]
fn player_args_minimal_get() {
    let args: PlayerArgs = serde_json::from_value(json!({ "action": "get" })).unwrap();
    assert_eq!(args.action, "get");
    assert!(args.item_id.is_none());
    assert!(args.position.is_none());
    assert!(args.quantity.is_none());
}

#[test]
fn player_args_add_item_with_quantity() {
    let args: PlayerArgs = serde_json::from_value(json!({
        "action": "add_item",
        "item_id": "weed_brick",
        "quantity": 5
    }))
    .unwrap();
    assert_eq!(args.item_id.as_deref(), Some("weed_brick"));
    assert_eq!(args.quantity, Some(5));
}

#[test]
fn player_args_teleport_with_position() {
    let args: PlayerArgs = serde_json::from_value(json!({
        "action": "teleport",
        "position": { "x": 1.0, "y": 2.5, "z": -3.0 }
    }))
    .unwrap();
    let pos = args.position.expect("position should be Some");
    assert_eq!(pos.x, 1.0);
    assert_eq!(pos.y, 2.5);
    assert_eq!(pos.z, -3.0);
}

#[test]
fn player_args_position_wrong_type_rejected() {
    // position must be an object, not a string
    let result =
        serde_json::from_value::<PlayerArgs>(json!({ "action": "teleport", "position": "here" }));
    assert!(result.is_err());
}

// ---------------------------------------------------------------------------
// NpcArgs
// ---------------------------------------------------------------------------

#[test]
fn npc_args_list_no_filter() {
    let args: NpcArgs = serde_json::from_value(json!({ "action": "list" })).unwrap();
    assert_eq!(args.action, "list");
    assert!(args.filter.is_none());
    assert!(args.npc_id.is_none());
}

#[test]
fn npc_args_set_health_full() {
    let args: NpcArgs = serde_json::from_value(json!({
        "action": "set_health",
        "npc_id": "dealer_01",
        "health": 75.5
    }))
    .unwrap();
    assert_eq!(args.npc_id.as_deref(), Some("dealer_01"));
    assert_eq!(args.health, Some(75.5));
}

#[test]
fn npc_args_action_required() {
    assert!(serde_json::from_value::<NpcArgs>(json!({})).is_err());
}

#[test]
fn npc_args_health_string_rejected() {
    let result = serde_json::from_value::<NpcArgs>(json!({
        "action": "set_health",
        "npc_id": "x",
        "health": "full"
    }));
    assert!(result.is_err());
}

// ---------------------------------------------------------------------------
// ItemArgs
// ---------------------------------------------------------------------------

#[test]
fn item_args_list_with_category() {
    let args: ItemArgs = serde_json::from_value(json!({
        "action": "list",
        "category": "drugs"
    }))
    .unwrap();
    assert_eq!(args.category.as_deref(), Some("drugs"));
    assert!(args.item_id.is_none());
}

#[test]
fn item_args_spawn_full() {
    let args: ItemArgs = serde_json::from_value(json!({
        "action": "spawn",
        "item_id": "weed_brick",
        "position": { "x": 0.0, "y": 1.0, "z": 0.0 },
        "quantity": 3
    }))
    .unwrap();
    assert_eq!(args.item_id.as_deref(), Some("weed_brick"));
    assert_eq!(args.quantity, Some(3));
    assert!(args.position.is_some());
}

#[test]
fn item_args_action_required() {
    assert!(serde_json::from_value::<ItemArgs>(json!({})).is_err());
}

// ---------------------------------------------------------------------------
// WorldArgs
// ---------------------------------------------------------------------------

#[test]
fn world_args_get_state_minimal() {
    let args: WorldArgs = serde_json::from_value(json!({ "action": "get_state" })).unwrap();
    assert_eq!(args.action, "get_state");
    assert!(args.property_id.is_none());
    assert!(args.slot_index.is_none());
    assert!(args.last_n_lines.is_none());
}

#[test]
fn world_args_capture_logs_filters() {
    let args: WorldArgs = serde_json::from_value(json!({
        "action": "capture_logs",
        "last_n_lines": 100,
        "keyword": "error",
        "include_pattern": "^\\[ERR"
    }))
    .unwrap();
    assert_eq!(args.last_n_lines, Some(100));
    assert_eq!(args.keyword.as_deref(), Some("error"));
    assert_eq!(args.include_pattern.as_deref(), Some("^\\[ERR"));
}

#[test]
fn world_args_load_save_with_slot() {
    let args: WorldArgs = serde_json::from_value(json!({
        "action": "load_save",
        "slot_index": 0
    }))
    .unwrap();
    assert_eq!(args.slot_index, Some(0));
}

#[test]
fn world_args_action_required() {
    assert!(serde_json::from_value::<WorldArgs>(json!({})).is_err());
}

// ---------------------------------------------------------------------------
// InspectArgs
// ---------------------------------------------------------------------------

#[test]
fn inspect_args_find_objects_optional_filters() {
    let args: InspectArgs = serde_json::from_value(json!({
        "action": "find_objects",
        "name_pattern": "Player",
        "tag": "Player"
    }))
    .unwrap();
    assert_eq!(args.name_pattern.as_deref(), Some("Player"));
    assert_eq!(args.tag.as_deref(), Some("Player"));
    assert!(args.object_name.is_none());
}

#[test]
fn inspect_args_set_field_with_value() {
    let args: InspectArgs = serde_json::from_value(json!({
        "action": "set_field",
        "object_name": "Player",
        "field_name": "health",
        "value": 100
    }))
    .unwrap();
    assert_eq!(args.object_name.as_deref(), Some("Player"));
    assert_eq!(args.field_name.as_deref(), Some("health"));
    assert_eq!(args.value, Some(json!(100)));
}

#[test]
fn inspect_args_set_transform_with_all_vectors() {
    let args: InspectArgs = serde_json::from_value(json!({
        "action": "set_transform",
        "object_name": "Player",
        "position": { "x": 1.0, "y": 2.0, "z": 3.0 },
        "rotation": { "x": 0.0, "y": 90.0, "z": 0.0 },
        "scale":    { "x": 1.0, "y": 1.0, "z": 1.0 }
    }))
    .unwrap();
    let pos = args.position.unwrap();
    let rot = args.rotation.unwrap();
    let scl = args.scale.unwrap();
    assert_eq!(pos.y, 2.0);
    assert_eq!(rot.y, 90.0);
    assert_eq!(scl.x, 1.0);
}

#[test]
fn inspect_args_call_method_with_args_array() {
    let args: InspectArgs = serde_json::from_value(json!({
        "action": "call_method",
        "object_name": "Enemy",
        "method_name": "TakeDamage",
        "args": [50, true]
    }))
    .unwrap();
    let method_args = args.args.unwrap();
    assert_eq!(method_args.len(), 2);
    assert_eq!(method_args[0], json!(50));
}

#[test]
fn inspect_args_action_required() {
    assert!(serde_json::from_value::<InspectArgs>(json!({})).is_err());
}

// ---------------------------------------------------------------------------
// GameArgs
// ---------------------------------------------------------------------------

#[test]
fn game_args_launch_full() {
    let args: GameArgs = serde_json::from_value(json!({
        "action": "launch",
        "version": "il2cpp",
        "enable_debugger": true,
        "wait_for_connection": false
    }))
    .unwrap();
    assert_eq!(args.version.as_deref(), Some("il2cpp"));
    assert_eq!(args.enable_debugger, Some(true));
    assert_eq!(args.wait_for_connection, Some(false));
}

#[test]
fn game_args_search_docs_with_tokens() {
    let args: GameArgs = serde_json::from_value(json!({
        "action": "search_docs",
        "topic": "Player",
        "tokens": 2000
    }))
    .unwrap();
    assert_eq!(args.topic.as_deref(), Some("Player"));
    assert_eq!(args.tokens, Some(2000));
}

#[test]
fn game_args_search_docs_missing_tokens_is_none() {
    let args: GameArgs =
        serde_json::from_value(json!({ "action": "search_docs", "topic": "NPC" })).unwrap();
    assert_eq!(args.tokens, None);
}

#[test]
fn game_args_action_required() {
    assert!(serde_json::from_value::<GameArgs>(json!({})).is_err());
}

#[test]
fn game_args_tokens_wrong_type_rejected() {
    // tokens must be u32, not a string
    let result = serde_json::from_value::<GameArgs>(json!({
        "action": "search_docs",
        "topic": "NPC",
        "tokens": "lots"
    }));
    assert!(result.is_err());
}
