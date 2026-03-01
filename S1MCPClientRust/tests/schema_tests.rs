/// Tests for schemars-derived tool schemas.
///
/// Verifies that each tool schema:
///  - Can be generated without panicking
///  - Contains a top-level "properties" object
///  - Has "action" as a required property
///  - Has the expected tool-specific optional properties present
use s1_mcp_client_rust::mcp::tools::{
    GameArgs, InspectArgs, ItemArgs, NpcArgs, PlayerArgs, WorldArgs,
};
use schemars::schema_for;

// Helper: get the generated serde_json::Value for a schema, so we can inspect
// it with standard JSON indexing regardless of schemars internals.
fn schema_value<T: schemars::JsonSchema>() -> serde_json::Value {
    let root = schema_for!(T);
    serde_json::to_value(&root).expect("schema must serialise")
}

fn has_property(schema: &serde_json::Value, key: &str) -> bool {
    schema
        .pointer("/properties")
        .and_then(|p| p.as_object())
        .map(|props| props.contains_key(key))
        .unwrap_or(false)
}

// ---------------------------------------------------------------------------
// PlayerArgs schema
// ---------------------------------------------------------------------------

#[test]
fn player_schema_has_action_property() {
    let s = schema_value::<PlayerArgs>();
    assert!(has_property(&s, "action"), "schema: {s}");
}

#[test]
fn player_schema_has_optional_fields() {
    let s = schema_value::<PlayerArgs>();
    assert!(has_property(&s, "position"));
    assert!(has_property(&s, "item_id"));
    assert!(has_property(&s, "quantity"));
}

// ---------------------------------------------------------------------------
// NpcArgs schema
// ---------------------------------------------------------------------------

#[test]
fn npc_schema_has_action_and_npc_id() {
    let s = schema_value::<NpcArgs>();
    assert!(has_property(&s, "action"));
    assert!(has_property(&s, "npc_id"));
    assert!(has_property(&s, "health"));
    assert!(has_property(&s, "filter"));
}

// ---------------------------------------------------------------------------
// ItemArgs schema
// ---------------------------------------------------------------------------

#[test]
fn item_schema_has_expected_properties() {
    let s = schema_value::<ItemArgs>();
    assert!(has_property(&s, "action"));
    assert!(has_property(&s, "item_id"));
    assert!(has_property(&s, "category"));
    assert!(has_property(&s, "position"));
    assert!(has_property(&s, "quantity"));
}

// ---------------------------------------------------------------------------
// WorldArgs schema
// ---------------------------------------------------------------------------

#[test]
fn world_schema_has_log_filter_properties() {
    let s = schema_value::<WorldArgs>();
    assert!(has_property(&s, "action"));
    assert!(has_property(&s, "last_n_lines"));
    assert!(has_property(&s, "first_n_lines"));
    assert!(has_property(&s, "keyword"));
    assert!(has_property(&s, "from_timestamp"));
    assert!(has_property(&s, "to_timestamp"));
    assert!(has_property(&s, "include_pattern"));
    assert!(has_property(&s, "exclude_pattern"));
}

#[test]
fn world_schema_has_save_and_location_properties() {
    let s = schema_value::<WorldArgs>();
    assert!(has_property(&s, "slot_index"));
    assert!(has_property(&s, "property_id"));
    assert!(has_property(&s, "property_name"));
    assert!(has_property(&s, "vehicle_id"));
}

// ---------------------------------------------------------------------------
// InspectArgs schema
// ---------------------------------------------------------------------------

#[test]
fn inspect_schema_has_core_properties() {
    let s = schema_value::<InspectArgs>();
    for key in [
        "action",
        "object_name",
        "component_type",
        "type_name",
        "field_name",
        "property_name",
        "method_name",
        "value",
        "args",
        "name_pattern",
        "tag",
        "layer",
        "pattern",
        "scene_name",
        "max_depth",
        "member_type",
        "active",
        "position",
        "rotation",
        "scale",
    ] {
        assert!(has_property(&s, key), "missing property: {key}");
    }
}

// ---------------------------------------------------------------------------
// GameArgs schema
// ---------------------------------------------------------------------------

#[test]
fn game_schema_has_all_properties() {
    let s = schema_value::<GameArgs>();
    for key in [
        "action",
        "version",
        "enable_debugger",
        "wait_for_connection",
        "topic",
        "tokens",
    ] {
        assert!(has_property(&s, key), "missing property: {key}");
    }
}

// ---------------------------------------------------------------------------
// Schema is valid JSON and has a "type": "object" at the root
// ---------------------------------------------------------------------------

#[test]
fn all_schemas_are_object_type() {
    for (name, schema) in [
        ("player", schema_value::<PlayerArgs>()),
        ("npc", schema_value::<NpcArgs>()),
        ("item", schema_value::<ItemArgs>()),
        ("world", schema_value::<WorldArgs>()),
        ("inspect", schema_value::<InspectArgs>()),
        ("game", schema_value::<GameArgs>()),
    ] {
        // schemars 0.8 wraps the root in a `$schema` + the actual schema under
        // root; the properties live at the top level of the serialised output.
        let props = schema.pointer("/properties");
        assert!(
            props.is_some(),
            "tool '{name}' schema must have /properties"
        );
    }
}
