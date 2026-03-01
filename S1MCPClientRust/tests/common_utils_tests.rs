/// Tests for tools::common utility functions.
///
/// Covers python_like_text formatting (including escaping and nested
/// containers), is_truthy across all JSON value kinds, and
/// format_action_for_error.
use serde_json::{json, Value};

use s1_mcp_client_rust::tools::common::{format_action_for_error, is_truthy, python_like_text};

// ---------------------------------------------------------------------------
// python_like_text
// ---------------------------------------------------------------------------

#[test]
fn python_like_text_null_is_none() {
    assert_eq!(python_like_text(&Value::Null), "None");
}

#[test]
fn python_like_text_booleans() {
    assert_eq!(python_like_text(&json!(true)), "True");
    assert_eq!(python_like_text(&json!(false)), "False");
}

#[test]
fn python_like_text_numbers() {
    assert_eq!(python_like_text(&json!(42)), "42");
    assert_eq!(python_like_text(&json!(-7)), "-7");
    assert_eq!(python_like_text(&json!(3.14)), "3.14");
}

#[test]
fn python_like_text_string_top_level_no_quotes() {
    // At the top level a string is returned bare (not single-quoted)
    assert_eq!(python_like_text(&json!("hello")), "hello");
}

#[test]
fn python_like_text_string_in_array_single_quoted() {
    let result = python_like_text(&json!(["a", "b"]));
    assert_eq!(result, "['a', 'b']");
}

#[test]
fn python_like_text_nested_array() {
    let result = python_like_text(&json!([[1, 2], [3]]));
    assert_eq!(result, "[[1, 2], [3]]");
}

#[test]
fn python_like_text_object() {
    // Single-key object — key and string value are single-quoted
    let result = python_like_text(&json!({ "key": "val" }));
    assert_eq!(result, "{'key': 'val'}");
}

#[test]
fn python_like_text_object_mixed_types() {
    let result = python_like_text(&json!({ "a": 1, "b": true, "c": null }));
    // Order is stable (serde_json preserves insertion order)
    assert!(result.contains("'a': 1"));
    assert!(result.contains("'b': True"));
    assert!(result.contains("'c': None"));
}

#[test]
fn python_like_text_escapes_backslash_in_key() {
    let result = python_like_text(&json!({ "a\\b": "v" }));
    assert!(
        result.contains("'a\\\\b'"),
        "backslash must be escaped: {result}"
    );
}

#[test]
fn python_like_text_escapes_single_quote_in_string() {
    // "it's" in an array → ['it\'s']
    let result = python_like_text(&json!(["it's"]));
    assert_eq!(
        result, "['it\\'s']",
        "single quotes must be escaped: {result}"
    );
}

// ---------------------------------------------------------------------------
// is_truthy
// ---------------------------------------------------------------------------

#[test]
fn is_truthy_null_is_false() {
    assert!(!is_truthy(&Value::Null));
}

#[test]
fn is_truthy_booleans() {
    assert!(is_truthy(&json!(true)));
    assert!(!is_truthy(&json!(false)));
}

#[test]
fn is_truthy_numbers() {
    assert!(is_truthy(&json!(1)));
    assert!(is_truthy(&json!(-1)));
    assert!(is_truthy(&json!(0.1)));
    assert!(!is_truthy(&json!(0)));
    assert!(!is_truthy(&json!(0.0)));
}

#[test]
fn is_truthy_strings() {
    assert!(is_truthy(&json!("hello")));
    assert!(!is_truthy(&json!("")));
}

#[test]
fn is_truthy_arrays() {
    assert!(is_truthy(&json!([1])));
    assert!(!is_truthy(&json!([])));
}

#[test]
fn is_truthy_objects() {
    assert!(is_truthy(&json!({ "k": "v" })));
    assert!(!is_truthy(&json!({})));
}

// ---------------------------------------------------------------------------
// format_action_for_error
// ---------------------------------------------------------------------------

#[test]
fn format_action_for_error_none_is_python_none() {
    assert_eq!(format_action_for_error(None), "None");
}

#[test]
fn format_action_for_error_string_value() {
    assert_eq!(format_action_for_error(Some(&json!("get"))), "get");
}

#[test]
fn format_action_for_error_null_value() {
    assert_eq!(format_action_for_error(Some(&Value::Null)), "None");
}

#[test]
fn format_action_for_error_integer_value() {
    assert_eq!(format_action_for_error(Some(&json!(42))), "42");
}
