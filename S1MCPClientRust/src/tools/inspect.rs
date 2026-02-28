use rmcp::model::{CallToolResult, JsonObject};
use serde_json::{json, Map, Value};

use crate::mcp::tools::InspectArgs;
use crate::server_state::ServerState;

use super::common::{call_and_format_result, text_result};

const TOOL_NAME: &str = "s1_inspect";

pub async fn handle_inspect(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let raw = Value::Object(arguments.unwrap_or_default());
    let args: InspectArgs = match serde_json::from_value(raw) {
        Ok(a) => a,
        Err(e) => return text_result(format!("Error: invalid arguments: {e}")),
    };

    let action = args.action.as_str();

    match action {
        "find_objects" => {
            let mut params = Map::new();
            if let Some(v) = args.name_pattern { params.insert("name_pattern".into(), json!(v)); }
            if let Some(v) = args.tag { params.insert("tag".into(), json!(v)); }
            if let Some(v) = args.layer { params.insert("layer".into(), json!(v)); }
            if let Some(v) = args.component_type { params.insert("component_type".into(), json!(v)); }
            call_and_format_result(state, "find_gameobjects", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "find_by_type" => {
            let Some(component_type) = args.component_type else {
                return text_result("Error: component_type required for this action");
            };
            let mut params = Map::from_iter([("component_type".into(), json!(component_type))]);
            if let Some(v) = args.include_inactive { params.insert("include_inactive".into(), json!(v)); }
            call_and_format_result(state, "find_objects_by_type", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "search_types" => {
            let Some(pattern) = args.pattern else {
                return text_result("Error: pattern required for this action");
            };
            let mut params = Map::from_iter([("pattern".into(), json!(pattern))]);
            if let Some(v) = args.component_types_only { params.insert("component_types_only".into(), json!(v)); }
            call_and_format_result(state, "search_types", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "list_scenes" => {
            call_and_format_result(state, "list_scenes", Some(json!({})), TOOL_NAME, action).await
        }
        "get_hierarchy" => {
            let mut params = Map::new();
            if let Some(v) = args.scene_name { params.insert("scene_name".into(), json!(v)); }
            call_and_format_result(state, "get_scene_hierarchy", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "get_scene_objects" => {
            let mut params = Map::new();
            if let Some(v) = args.scene_name { params.insert("scene_name".into(), json!(v)); }
            call_and_format_result(state, "get_scene_objects", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "inspect_object" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            let mut params = Map::from_iter([("object_name".into(), json!(object_name))]);
            if let Some(v) = args.object_type { params.insert("object_type".into(), json!(v)); }
            call_and_format_result(state, "inspect_object", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "list_components" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            call_and_format_result(
                state, "list_components",
                Some(json!({ "object_name": object_name })),
                TOOL_NAME, action,
            ).await
        }
        "get_component" => {
            let (Some(object_name), Some(component_type)) = (args.object_name, args.component_type) else {
                return text_result("Error: object_name, component_type required for this action");
            };
            call_and_format_result(
                state, "get_component_by_type",
                Some(json!({ "object_name": object_name, "component_type": component_type })),
                TOOL_NAME, action,
            ).await
        }
        "inspect_component" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            let mut params = Map::from_iter([("object_name".into(), json!(object_name))]);
            if let Some(v) = args.component_type { params.insert("component_type".into(), json!(v)); }
            if let Some(v) = args.max_depth { params.insert("max_depth".into(), json!(v)); }
            call_and_format_result(state, "inspect_component", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "get_member" => {
            let (Some(object_name), Some(member_path)) = (args.object_name, args.member_path) else {
                return text_result("Error: object_name, member_path required for this action");
            };
            let mut params = Map::from_iter([
                ("object_name".into(), json!(object_name)),
                ("member_path".into(), json!(member_path)),
            ]);
            if let Some(v) = args.component_type { params.insert("component_type".into(), json!(v)); }
            call_and_format_result(state, "get_member_value", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "inspect_type" => {
            let Some(type_name) = args.type_name else {
                return text_result("Error: type_name required for this action");
            };
            call_and_format_result(
                state, "inspect_type",
                Some(json!({ "type_name": type_name })),
                TOOL_NAME, action,
            ).await
        }
        "list_members" => {
            let Some(type_name) = args.type_name else {
                return text_result("Error: type_name required for this action");
            };
            let mut params = Map::from_iter([("type_name".into(), json!(type_name))]);
            if let Some(v) = args.member_type { params.insert("member_type".into(), json!(v)); }
            call_and_format_result(state, "list_members", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "get_transform" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            call_and_format_result(
                state, "get_transform",
                Some(json!({ "object_name": object_name })),
                TOOL_NAME, action,
            ).await
        }
        "is_active" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            call_and_format_result(
                state, "is_active",
                Some(json!({ "object_name": object_name })),
                TOOL_NAME, action,
            ).await
        }
        "get_field" => {
            let (Some(object_name), Some(field_name)) = (args.object_name, args.field_name) else {
                return text_result("Error: object_name, field_name required for this action");
            };
            let mut params = Map::from_iter([
                ("object_name".into(), json!(object_name)),
                ("field_name".into(), json!(field_name)),
            ]);
            if let Some(v) = args.component_type { params.insert("component_type".into(), json!(v)); }
            call_and_format_result(state, "get_field", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "set_field" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            let Some(field_name) = args.field_name else {
                return text_result("Error: field_name required for this action");
            };
            let Some(value) = args.value else {
                return text_result("Error: value is required for set_field");
            };
            let mut params = Map::from_iter([
                ("object_name".into(), json!(object_name)),
                ("field_name".into(), json!(field_name)),
                ("value".into(), value),
            ]);
            if let Some(v) = args.component_type { params.insert("component_type".into(), json!(v)); }
            call_and_format_result(state, "set_field", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "get_component_property" => {
            let (Some(object_name), Some(component_type), Some(property_name)) =
                (args.object_name, args.component_type, args.property_name) else {
                return text_result("Error: object_name, component_type, property_name required for this action");
            };
            call_and_format_result(
                state, "get_component_property",
                Some(json!({
                    "object_name": object_name,
                    "component_type": component_type,
                    "property_name": property_name
                })),
                TOOL_NAME, action,
            ).await
        }
        "set_component_property" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            let Some(component_type) = args.component_type else {
                return text_result("Error: component_type required for this action");
            };
            let Some(property_name) = args.property_name else {
                return text_result("Error: property_name required for this action");
            };
            let Some(value) = args.value else {
                return text_result("Error: value is required");
            };
            call_and_format_result(
                state, "set_component_property",
                Some(json!({
                    "object_name": object_name,
                    "component_type": component_type,
                    "property_name": property_name,
                    "value": value
                })),
                TOOL_NAME, action,
            ).await
        }
        "call_method" => {
            let (Some(object_name), Some(method_name)) = (args.object_name, args.method_name) else {
                return text_result("Error: object_name, method_name required for this action");
            };
            let mut params = Map::from_iter([
                ("object_name".into(), json!(object_name)),
                ("method_name".into(), json!(method_name)),
            ]);
            if let Some(v) = args.args { params.insert("args".into(), json!(v)); }
            if let Some(v) = args.component_type { params.insert("component_type".into(), json!(v)); }
            call_and_format_result(state, "call_method", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "set_transform" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            let mut params = Map::from_iter([("object_name".into(), json!(object_name))]);
            if let Some(p) = args.position {
                params.insert("position".into(), json!({ "x": p.x, "y": p.y, "z": p.z }));
            }
            if let Some(r) = args.rotation {
                params.insert("rotation".into(), json!({ "x": r.x, "y": r.y, "z": r.z }));
            }
            if let Some(s) = args.scale {
                params.insert("scale".into(), json!({ "x": s.x, "y": s.y, "z": s.z }));
            }
            call_and_format_result(state, "set_transform", Some(Value::Object(params)), TOOL_NAME, action).await
        }
        "set_active" => {
            let Some(object_name) = args.object_name else {
                return text_result("Error: object_name required for this action");
            };
            let Some(active) = args.active else {
                return text_result("Error: active is required");
            };
            call_and_format_result(
                state, "set_active",
                Some(json!({ "object_name": object_name, "active": active })),
                TOOL_NAME, action,
            ).await
        }
        other => text_result(format!("Error: Unknown action '{other}'")),
    }
}
