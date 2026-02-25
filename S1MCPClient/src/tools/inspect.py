"""Inspect tool — all Unity reflection and debug operations."""

import json
from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger

logger = get_logger()

TOOL_NAME = "s1_inspect"

TOOL = Tool(
    name=TOOL_NAME,
    description=(
        "Inspect and manipulate Unity GameObjects via reflection. Choose an action:\n\n"
        "DISCOVERY:\n"
        "- find_objects: Search GameObjects by name/tag/layer/component_type (all optional)\n"
        "- find_by_type: Find all objects of a type (requires: type_name; optional: include_inactive)\n"
        "- search_types: Search type names across all assemblies (requires: query; optional: include_non_public)\n"
        "- list_scenes: List all loaded Unity scenes\n"
        "- get_hierarchy: Get scene object tree (optional: scene_name)\n"
        "- get_scene_objects: Get all objects in a scene (optional: scene_name)\n\n"
        "INSPECTION:\n"
        "- inspect_object: Inspect a GameObject (requires: object_name; optional: object_type)\n"
        "- list_components: List all components on an object (requires: object_name)\n"
        "- get_component: Get component by type (requires: object_name, component_type)\n"
        "- inspect_component: Deep-inspect a component (requires: object_name; optional: component_type, max_depth)\n"
        "- get_member: Get a nested member value via dot-path (requires: object_name, member_path; optional: component_type)\n"
        "- inspect_type: Get type metadata and members (requires: type_name)\n"
        "- list_members: List fields/properties/methods of a type (requires: type_name; optional: member_type)\n"
        "- get_transform: Get position/rotation/scale (requires: object_name)\n"
        "- is_active: Check if a GameObject is active (requires: object_name)\n\n"
        "MODIFICATION:\n"
        "- get_field: Get a field value (requires: object_name, field_name; optional: component_type)\n"
        "- set_field: Set a field value (requires: object_name, field_name, value; optional: component_type)\n"
        "- get_component_property: Get component property (requires: object_name, component_type, property_name)\n"
        "- set_component_property: Set component property (requires: object_name, component_type, property_name, value)\n"
        "- call_method: Invoke a method via reflection (requires: object_name, method_name; optional: args, component_type)\n"
        "- set_transform: Set position/rotation/scale (requires: object_name; optional: position, rotation, scale)\n"
        "- set_active: Set active state (requires: object_name, active)"
    ),
    inputSchema={
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
                "description": "Type name for find_by_type / inspect_type / list_members"
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
            "name": {
                "type": "string",
                "description": "Name filter for find_objects"
            },
            "tag": {
                "type": "string",
                "description": "Tag filter for find_objects"
            },
            "layer": {
                "type": "string",
                "description": "Layer filter for find_objects"
            },
            "query": {
                "type": "string",
                "description": "Search query for search_types"
            },
            "include_non_public": {
                "type": "boolean",
                "description": "Include non-public types in search_types"
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
                    "x": {"type": "number"},
                    "y": {"type": "number"},
                    "z": {"type": "number"}
                },
                "description": "Position for set_transform"
            },
            "rotation": {
                "type": "object",
                "properties": {
                    "x": {"type": "number"},
                    "y": {"type": "number"},
                    "z": {"type": "number"}
                },
                "description": "Euler rotation for set_transform"
            },
            "scale": {
                "type": "object",
                "properties": {
                    "x": {"type": "number"},
                    "y": {"type": "number"},
                    "z": {"type": "number"}
                },
                "description": "Scale for set_transform"
            }
        },
        "required": ["action"]
    }
)


def _req(arguments: Dict[str, Any], *keys: str):
    """Return error text if any key is missing, else None."""
    missing = [k for k in keys if arguments.get(k) is None]
    if missing:
        return f"Error: {', '.join(missing)} required for this action"
    return None


async def handle(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    action = arguments.get("action")

    # Build (method, params) pairs for each action
    try:
        if action == "find_objects":
            params = {k: arguments[k] for k in ("name", "tag", "layer", "component_type") if k in arguments}
            resp = await tcp_client.async_call("find_gameobjects", params or {})

        elif action == "find_by_type":
            if err := _req(arguments, "type_name"):
                return [TextContent(type="text", text=err)]
            params = {"type_name": arguments["type_name"]}
            if "include_inactive" in arguments:
                params["include_inactive"] = arguments["include_inactive"]
            resp = await tcp_client.async_call("find_objects_by_type", params)

        elif action == "search_types":
            if err := _req(arguments, "query"):
                return [TextContent(type="text", text=err)]
            params = {"query": arguments["query"]}
            if "include_non_public" in arguments:
                params["include_non_public"] = arguments["include_non_public"]
            resp = await tcp_client.async_call("search_types", params)

        elif action == "list_scenes":
            resp = await tcp_client.async_call("list_scenes", {})

        elif action == "get_hierarchy":
            params = {}
            if sn := arguments.get("scene_name"):
                params["scene_name"] = sn
            resp = await tcp_client.async_call("get_scene_hierarchy", params or {})

        elif action == "get_scene_objects":
            params = {}
            if sn := arguments.get("scene_name"):
                params["scene_name"] = sn
            resp = await tcp_client.async_call("get_scene_objects", params or {})

        elif action == "inspect_object":
            if err := _req(arguments, "object_name"):
                return [TextContent(type="text", text=err)]
            params = {"object_name": arguments["object_name"]}
            if ot := arguments.get("object_type"):
                params["object_type"] = ot
            resp = await tcp_client.async_call("inspect_object", params)

        elif action == "list_components":
            if err := _req(arguments, "object_name"):
                return [TextContent(type="text", text=err)]
            resp = await tcp_client.async_call("list_components", {"object_name": arguments["object_name"]})

        elif action == "get_component":
            if err := _req(arguments, "object_name", "component_type"):
                return [TextContent(type="text", text=err)]
            resp = await tcp_client.async_call("get_component_by_type", {
                "object_name": arguments["object_name"],
                "component_type": arguments["component_type"]
            })

        elif action == "inspect_component":
            if err := _req(arguments, "object_name"):
                return [TextContent(type="text", text=err)]
            params = {"object_name": arguments["object_name"]}
            if ct := arguments.get("component_type"):
                params["component_type"] = ct
            if md := arguments.get("max_depth"):
                params["max_depth"] = md
            resp = await tcp_client.async_call("inspect_component", params)

        elif action == "get_member":
            if err := _req(arguments, "object_name", "member_path"):
                return [TextContent(type="text", text=err)]
            params = {"object_name": arguments["object_name"], "member_path": arguments["member_path"]}
            if ct := arguments.get("component_type"):
                params["component_type"] = ct
            resp = await tcp_client.async_call("get_member_value", params)

        elif action == "inspect_type":
            if err := _req(arguments, "type_name"):
                return [TextContent(type="text", text=err)]
            resp = await tcp_client.async_call("inspect_type", {"type_name": arguments["type_name"]})

        elif action == "list_members":
            if err := _req(arguments, "type_name"):
                return [TextContent(type="text", text=err)]
            params = {"type_name": arguments["type_name"]}
            if mt := arguments.get("member_type"):
                params["member_type"] = mt
            resp = await tcp_client.async_call("list_members", params)

        elif action == "get_transform":
            if err := _req(arguments, "object_name"):
                return [TextContent(type="text", text=err)]
            resp = await tcp_client.async_call("get_transform", {"object_name": arguments["object_name"]})

        elif action == "is_active":
            if err := _req(arguments, "object_name"):
                return [TextContent(type="text", text=err)]
            resp = await tcp_client.async_call("is_active", {"object_name": arguments["object_name"]})

        elif action == "get_field":
            if err := _req(arguments, "object_name", "field_name"):
                return [TextContent(type="text", text=err)]
            params = {"object_name": arguments["object_name"], "field_name": arguments["field_name"]}
            if ct := arguments.get("component_type"):
                params["component_type"] = ct
            resp = await tcp_client.async_call("get_field", params)

        elif action == "set_field":
            if err := _req(arguments, "object_name", "field_name"):
                return [TextContent(type="text", text=err)]
            if "value" not in arguments:
                return [TextContent(type="text", text="Error: value is required for set_field")]
            params = {
                "object_name": arguments["object_name"],
                "field_name": arguments["field_name"],
                "value": arguments["value"]
            }
            if ct := arguments.get("component_type"):
                params["component_type"] = ct
            resp = await tcp_client.async_call("set_field", params)

        elif action == "get_component_property":
            if err := _req(arguments, "object_name", "component_type", "property_name"):
                return [TextContent(type="text", text=err)]
            resp = await tcp_client.async_call("get_component_property", {
                "object_name": arguments["object_name"],
                "component_type": arguments["component_type"],
                "property_name": arguments["property_name"]
            })

        elif action == "set_component_property":
            if err := _req(arguments, "object_name", "component_type", "property_name"):
                return [TextContent(type="text", text=err)]
            if "value" not in arguments:
                return [TextContent(type="text", text="Error: value is required")]
            resp = await tcp_client.async_call("set_component_property", {
                "object_name": arguments["object_name"],
                "component_type": arguments["component_type"],
                "property_name": arguments["property_name"],
                "value": arguments["value"]
            })

        elif action == "call_method":
            if err := _req(arguments, "object_name", "method_name"):
                return [TextContent(type="text", text=err)]
            params = {"object_name": arguments["object_name"], "method_name": arguments["method_name"]}
            if a := arguments.get("args"):
                params["args"] = a
            if ct := arguments.get("component_type"):
                params["component_type"] = ct
            resp = await tcp_client.async_call("call_method", params)

        elif action == "set_transform":
            if err := _req(arguments, "object_name"):
                return [TextContent(type="text", text=err)]
            params = {"object_name": arguments["object_name"]}
            for k in ("position", "rotation", "scale"):
                if k in arguments:
                    params[k] = arguments[k]
            resp = await tcp_client.async_call("set_transform", params)

        elif action == "set_active":
            if err := _req(arguments, "object_name"):
                return [TextContent(type="text", text=err)]
            if "active" not in arguments:
                return [TextContent(type="text", text="Error: active is required")]
            resp = await tcp_client.async_call("set_active", {
                "object_name": arguments["object_name"],
                "active": arguments["active"]
            })

        else:
            return [TextContent(type="text", text=f"Error: Unknown action '{action}'")]

        if resp.error:
            return [TextContent(type="text", text=f"Error: {resp.error.message} (code: {resp.error.code})")]
        return [TextContent(type="text", text=json.dumps(resp.result, indent=2))]

    except Exception as e:
        logger.error(f"Error in s1_inspect/{action}: {e}")
        return [TextContent(type="text", text=f"Error: {e}")]


TOOL_HANDLERS = {TOOL_NAME: handle}


def get_tools(_tcp_client: TcpClient) -> list[Tool]:
    return [TOOL]
