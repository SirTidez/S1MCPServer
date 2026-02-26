use rmcp::model::{CallToolResult, JsonObject};
use serde_json::{json, Map, Value};

use crate::server_state::ServerState;

use super::common::{call_and_format_result, format_action_for_error, is_truthy, text_result};

const TOOL_NAME: &str = "s1_inspect";

pub async fn handle_inspect(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let args = arguments.unwrap_or_default();
    let action_value = args.get("action");
    let action = action_value.and_then(Value::as_str);
    let action_for_log = format_action_for_error(action_value);

    match action {
        Some("find_objects") => {
            let mut params = Map::new();
            for key in ["name_pattern", "tag", "layer", "component_type"] {
                if let Some(value) = args.get(key) {
                    params.insert(key.to_string(), value.clone());
                }
            }

            let params_value = if params.is_empty() {
                Some(json!({}))
            } else {
                Some(Value::Object(params))
            };

            call_and_format_result(
                state,
                "find_gameobjects",
                params_value,
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("find_by_type") => {
            if let Some(error) = require_not_null(&args, &["component_type"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([(
                "component_type".to_string(),
                args.get("component_type").cloned().unwrap_or(Value::Null),
            )]);

            if let Some(value) = args.get("include_inactive") {
                params.insert("include_inactive".to_string(), value.clone());
            }

            call_and_format_result(
                state,
                "find_objects_by_type",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("search_types") => {
            if let Some(error) = require_not_null(&args, &["pattern"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([(
                "pattern".to_string(),
                args.get("pattern").cloned().unwrap_or(Value::Null),
            )]);

            if let Some(value) = args.get("component_types_only") {
                params.insert("component_types_only".to_string(), value.clone());
            }

            call_and_format_result(
                state,
                "search_types",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("list_scenes") => {
            call_and_format_result(
                state,
                "list_scenes",
                Some(json!({})),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_hierarchy") => {
            let mut params = Map::new();
            if let Some(value) = args.get("scene_name") {
                if is_truthy(value) {
                    params.insert("scene_name".to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "get_scene_hierarchy",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_scene_objects") => {
            let mut params = Map::new();
            if let Some(value) = args.get("scene_name") {
                if is_truthy(value) {
                    params.insert("scene_name".to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "get_scene_objects",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("inspect_object") => {
            if let Some(error) = require_not_null(&args, &["object_name"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([(
                "object_name".to_string(),
                args.get("object_name").cloned().unwrap_or(Value::Null),
            )]);

            if let Some(value) = args.get("object_type") {
                if is_truthy(value) {
                    params.insert("object_type".to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "inspect_object",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("list_components") => {
            if let Some(error) = require_not_null(&args, &["object_name"]) {
                return text_result(error);
            }

            call_and_format_result(
                state,
                "list_components",
                Some(json!({ "object_name": args.get("object_name") })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_component") => {
            if let Some(error) = require_not_null(&args, &["object_name", "component_type"]) {
                return text_result(error);
            }

            call_and_format_result(
                state,
                "get_component_by_type",
                Some(json!({
                    "object_name": args.get("object_name"),
                    "component_type": args.get("component_type")
                })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("inspect_component") => {
            if let Some(error) = require_not_null(&args, &["object_name"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([(
                "object_name".to_string(),
                args.get("object_name").cloned().unwrap_or(Value::Null),
            )]);

            if let Some(value) = args.get("component_type") {
                if is_truthy(value) {
                    params.insert("component_type".to_string(), value.clone());
                }
            }
            if let Some(value) = args.get("max_depth") {
                params.insert("max_depth".to_string(), value.clone());
            }

            call_and_format_result(
                state,
                "inspect_component",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_member") => {
            if let Some(error) = require_not_null(&args, &["object_name", "member_path"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([
                (
                    "object_name".to_string(),
                    args.get("object_name").cloned().unwrap_or(Value::Null),
                ),
                (
                    "member_path".to_string(),
                    args.get("member_path").cloned().unwrap_or(Value::Null),
                ),
            ]);

            if let Some(value) = args.get("component_type") {
                if is_truthy(value) {
                    params.insert("component_type".to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "get_member_value",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("inspect_type") => {
            if let Some(error) = require_not_null(&args, &["type_name"]) {
                return text_result(error);
            }

            call_and_format_result(
                state,
                "inspect_type",
                Some(json!({ "type_name": args.get("type_name") })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("list_members") => {
            if let Some(error) = require_not_null(&args, &["type_name"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([(
                "type_name".to_string(),
                args.get("type_name").cloned().unwrap_or(Value::Null),
            )]);

            if let Some(value) = args.get("member_type") {
                if is_truthy(value) {
                    params.insert("member_type".to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "list_members",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_transform") => {
            if let Some(error) = require_not_null(&args, &["object_name"]) {
                return text_result(error);
            }

            call_and_format_result(
                state,
                "get_transform",
                Some(json!({ "object_name": args.get("object_name") })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("is_active") => {
            if let Some(error) = require_not_null(&args, &["object_name"]) {
                return text_result(error);
            }

            call_and_format_result(
                state,
                "is_active",
                Some(json!({ "object_name": args.get("object_name") })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_field") => {
            if let Some(error) = require_not_null(&args, &["object_name", "field_name"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([
                (
                    "object_name".to_string(),
                    args.get("object_name").cloned().unwrap_or(Value::Null),
                ),
                (
                    "field_name".to_string(),
                    args.get("field_name").cloned().unwrap_or(Value::Null),
                ),
            ]);

            if let Some(value) = args.get("component_type") {
                if is_truthy(value) {
                    params.insert("component_type".to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "get_field",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("set_field") => {
            if let Some(error) = require_not_null(&args, &["object_name", "field_name"]) {
                return text_result(error);
            }
            if !args.contains_key("value") {
                return text_result("Error: value is required for set_field");
            }

            let mut params = Map::from_iter([
                (
                    "object_name".to_string(),
                    args.get("object_name").cloned().unwrap_or(Value::Null),
                ),
                (
                    "field_name".to_string(),
                    args.get("field_name").cloned().unwrap_or(Value::Null),
                ),
                (
                    "value".to_string(),
                    args.get("value").cloned().unwrap_or(Value::Null),
                ),
            ]);

            if let Some(value) = args.get("component_type") {
                if is_truthy(value) {
                    params.insert("component_type".to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "set_field",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_component_property") => {
            if let Some(error) = require_not_null(&args, &["object_name", "component_type", "property_name"]) {
                return text_result(error);
            }

            call_and_format_result(
                state,
                "get_component_property",
                Some(json!({
                    "object_name": args.get("object_name"),
                    "component_type": args.get("component_type"),
                    "property_name": args.get("property_name")
                })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("set_component_property") => {
            if let Some(error) = require_not_null(&args, &["object_name", "component_type", "property_name"]) {
                return text_result(error);
            }
            if !args.contains_key("value") {
                return text_result("Error: value is required");
            }

            call_and_format_result(
                state,
                "set_component_property",
                Some(json!({
                    "object_name": args.get("object_name"),
                    "component_type": args.get("component_type"),
                    "property_name": args.get("property_name"),
                    "value": args.get("value")
                })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("call_method") => {
            if let Some(error) = require_not_null(&args, &["object_name", "method_name"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([
                (
                    "object_name".to_string(),
                    args.get("object_name").cloned().unwrap_or(Value::Null),
                ),
                (
                    "method_name".to_string(),
                    args.get("method_name").cloned().unwrap_or(Value::Null),
                ),
            ]);

            if let Some(value) = args.get("args") {
                if is_truthy(value) {
                    params.insert("args".to_string(), value.clone());
                }
            }
            if let Some(value) = args.get("component_type") {
                if is_truthy(value) {
                    params.insert("component_type".to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "call_method",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("set_transform") => {
            if let Some(error) = require_not_null(&args, &["object_name"]) {
                return text_result(error);
            }

            let mut params = Map::from_iter([(
                "object_name".to_string(),
                args.get("object_name").cloned().unwrap_or(Value::Null),
            )]);

            for key in ["position", "rotation", "scale"] {
                if let Some(value) = args.get(key) {
                    params.insert(key.to_string(), value.clone());
                }
            }

            call_and_format_result(
                state,
                "set_transform",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("set_active") => {
            if let Some(error) = require_not_null(&args, &["object_name"]) {
                return text_result(error);
            }
            if !args.contains_key("active") {
                return text_result("Error: active is required");
            }

            call_and_format_result(
                state,
                "set_active",
                Some(json!({
                    "object_name": args.get("object_name"),
                    "active": args.get("active")
                })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        _ => text_result(format!("Error: Unknown action '{}'", action_for_log)),
    }
}

fn require_not_null(args: &JsonObject, keys: &[&str]) -> Option<String> {
    let missing = keys
        .iter()
        .copied()
        .filter(|key| args.get(*key).is_none() || args.get(*key).is_some_and(Value::is_null))
        .collect::<Vec<_>>();

    if missing.is_empty() {
        None
    } else {
        Some(format!(
            "Error: {} required for this action",
            missing.join(", ")
        ))
    }
}
