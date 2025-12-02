"""Debug/inspection MCP tools."""

from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger


logger = get_logger()


def get_debug_tools(tcp_client: TcpClient) -> list[Tool]:
    """
    Get all Debug MCP tools.
    
    Args:
        tcp_client: TCP client instance
    
    Returns:
        List of MCP Tool definitions
    """
    return [
        Tool(
            name="s1_inspect_object",
            description="Inspect a Unity GameObject or component using reflection. Useful for debugging and discovering game object properties.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The name of the GameObject to inspect"
                    },
                    "object_type": {
                        "type": "string",
                        "description": "The type of object to inspect (e.g., 'GameObject', 'Component', or a specific component type name)",
                        "default": "GameObject"
                    }
                },
                "required": ["object_name"]
            }
        ),
        Tool(
            name="s1_inspect_component",
            description="""Inspect any component by type name with deep reflection. The most powerful tool for debugging GameObject components.

**Features:**
- Supports partial type name matching (e.g., 'Dealer', 'NPC', 'Movement')
- Deep property/field inspection (configurable max_depth)
- Works with GameObject name, NPC ID, or finds first match in scene
- Includes private fields for deep debugging

**Common debugging workflows:**
1. NPC not moving: Inspect 'Dealer' component → check 'Home' property
2. NPC frozen: Inspect 'NPCMovement' → check 'HasDestination', 'IsPaused'
3. Component behavior: Inspect any component → examine state

**TIP:** If you get "Component not found", the error will suggest similar component names. Use s1_list_components to see all available options.""",
            inputSchema={
                "type": "object",
                "properties": {
                    "component_type": {
                        "type": "string",
                        "description": "The component type name (supports partial matching, e.g., 'Dealer', 'NPC', 'NPCPrefabIdentity')"
                    },
                    "object_name": {
                        "type": "string",
                        "description": "Optional: The GameObject name to inspect component on. If not provided, finds first match in scene."
                    },
                    "npc_id": {
                        "type": "string",
                        "description": "Optional: The NPC ID to inspect component on. Alternative to object_name."
                    },
                    "max_depth": {
                        "type": "integer",
                        "description": "Maximum depth for deep inspection (default: 3)",
                        "default": 3
                    }
                },
                "required": ["component_type"]
            }
        ),
        Tool(
            name="s1_get_member_value",
            description="""Get any property or field value from an object with support for nested access paths.

**Nested path syntax:** Use dots to access nested properties (e.g., 'Dealer.Home.BuildingName')

**Use cases:**
- Quick value checks without full component inspection
- Accessing deeply nested properties
- Following object references (e.g., Home -> BuildingName)

**Example paths:**
- 'Dealer.Home' → Get the home building reference
- 'Dealer.Home.BuildingName' → Get the building name directly
- 'NPCMovement.HasDestination' → Check if NPC has a destination
- 'Health.CurrentHealth' → Get current health value

**TIP:** Start with s1_inspect_component to discover what properties are available, then use this for quick lookups.""",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The name of the GameObject (optional if npc_id is provided)"
                    },
                    "npc_id": {
                        "type": "string",
                        "description": "Optional: The NPC ID to get member value from. Alternative to object_name."
                    },
                    "member_path": {
                        "type": "string",
                        "description": "The member path (supports nested access with dots, e.g., 'Dealer.Home.BuildingName')"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Component type name if accessing a component member (e.g., 'Dealer', 'NPC')"
                    }
                },
                "required": ["member_path"]
            }
        ),
        Tool(
            name="s1_get_component_by_type",
            description="Find a component by type name on a specific GameObject. Quick lookup to verify a component exists.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The name of the GameObject"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "The component type name (supports partial matching)"
                    }
                },
                "required": ["object_name", "component_type"]
            }
        ),
        Tool(
            name="s1_list_components",
            description="""List all components attached to a GameObject or NPC. Essential for discovering what components exist before inspecting them.

**When to use this tool:**
- Before using s1_inspect_component to see what components are available
- When you get a "Component not found" error
- To discover what components a GameObject has

**Common use cases:**
- Debugging why an NPC isn't working (list components to see what's available)
- Exploring mod-added components
- Finding the correct component name to inspect

**TIP:** Game components from ScheduleOne will have helpful debug hints in the response.""",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The name of the GameObject to list components for"
                    },
                    "npc_id": {
                        "type": "string",
                        "description": "Alternative: The NPC ID to list components for (instead of object_name)"
                    }
                },
                "required": []
            }
        ),
        Tool(
            name="s1_find_gameobjects",
            description="""Search for GameObjects in the scene by name pattern, tag, layer, or component type. Essential for discovering runtime objects.

**Features:**
- Search by name pattern (substring matching)
- Filter by tag, layer, or component type
- Filter by active state
- Returns up to 1000 results

**Common use cases:**
- Finding all NPCs: search with component_type='NPC'
- Finding objects with a specific tag
- Discovering objects by name pattern
- Finding all active/inactive objects""",
            inputSchema={
                "type": "object",
                "properties": {
                    "name_pattern": {
                        "type": "string",
                        "description": "Optional: Name pattern to search for (substring match)"
                    },
                    "tag": {
                        "type": "string",
                        "description": "Optional: Filter by Unity tag"
                    },
                    "layer": {
                        "type": "integer",
                        "description": "Optional: Filter by Unity layer"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Filter by component type name (e.g., 'NPC', 'Dealer')"
                    },
                    "active_only": {
                        "type": "boolean",
                        "description": "Optional: Only return active GameObjects"
                    }
                },
                "required": []
            }
        ),
        Tool(
            name="s1_search_types",
            description="""Search for types (classes, components) by name pattern. Essential for discovering available component types.

**Features:**
- Search across all loaded assemblies
- Option to search only Component types
- Returns type metadata (name, namespace, base types)

**Common use cases:**
- Discovering component types: search_types('NPC', component_types_only=True)
- Finding types by partial name
- Exploring available classes in the game""",
            inputSchema={
                "type": "object",
                "properties": {
                    "pattern": {
                        "type": "string",
                        "description": "The pattern to search for (case-insensitive substring)"
                    },
                    "component_types_only": {
                        "type": "boolean",
                        "description": "If true, only search Component types",
                        "default": False
                    }
                },
                "required": ["pattern"]
            }
        ),
        Tool(
            name="s1_get_scene_hierarchy",
            description="""Get the full GameObject hierarchy tree for a scene. Shows parent-child relationships.

**Features:**
- Builds complete hierarchy tree
- Filter by active state
- Limit depth to prevent huge responses
- Shows scene structure

**Common use cases:**
- Understanding scene structure
- Finding root objects
- Navigating GameObject relationships""",
            inputSchema={
                "type": "object",
                "properties": {
                    "scene_name": {
                        "type": "string",
                        "description": "Optional: Scene name (if not provided, uses all scenes)"
                    },
                    "active_only": {
                        "type": "boolean",
                        "description": "Optional: Only include active GameObjects",
                        "default": False
                    },
                    "max_depth": {
                        "type": "integer",
                        "description": "Maximum depth to traverse (default: 10)",
                        "default": 10
                    }
                },
                "required": []
            }
        ),
        Tool(
            name="s1_list_scenes",
            description="List all loaded scenes with their metadata (name, path, root count, etc.).",
            inputSchema={
                "type": "object",
                "properties": {},
                "required": []
            }
        ),
        Tool(
            name="s1_get_hierarchy",
            description="""Get parent and children relationships for a specific GameObject.

**Returns:**
- Parent GameObject name (if any)
- List of child GameObject names
- Child count""",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The name of the GameObject"
                    }
                },
                "required": ["object_name"]
            }
        ),
        Tool(
            name="s1_find_objects_by_type",
            description="""Find all GameObjects that have a specific component type attached.

**Features:**
- Searches all GameObjects in the scene
- Returns objects with the specified component type
- Includes object name, path, and position

**Common use cases:**
- Finding all NPCs with a specific component
- Locating objects with a particular behavior
- Discovering all instances of a component type""",
            inputSchema={
                "type": "object",
                "properties": {
                    "component_type": {
                        "type": "string",
                        "description": "The component type name to search for"
                    }
                },
                "required": ["component_type"]
            }
        ),
        Tool(
            name="s1_get_scene_objects",
            description="""Get all GameObjects organized by scene.

**Features:**
- Lists all GameObjects in all loaded scenes
- Groups objects by scene name
- Includes object name, path, and active state

**Common use cases:**
- Understanding scene structure
- Finding objects across multiple scenes
- Discovering all objects in the game""",
            inputSchema={
                "type": "object",
                "properties": {},
                "required": []
            }
        ),
        Tool(
            name="s1_list_members",
            description="""List all members (fields, properties, methods) of a type or object instance.

**Features:**
- List fields with types and access modifiers
- List properties with read/write capabilities
- List methods with signatures and parameters
- Option to include private members

**Common use cases:**
- Discovering what members a type has
- Understanding component API
- Finding available methods/properties before calling them""",
            inputSchema={
                "type": "object",
                "properties": {
                    "type_name": {
                        "type": "string",
                        "description": "The type name to list members for"
                    },
                    "object_name": {
                        "type": "string",
                        "description": "Alternative: GameObject name to list members of its type"
                    },
                    "include_private": {
                        "type": "boolean",
                        "description": "Include private members",
                        "default": False
                    }
                },
                "required": []
            }
        ),
        Tool(
            name="s1_inspect_type",
            description="""Inspect a type by name with detailed reflection information.

**Features:**
- Get type metadata (name, namespace, base types)
- List all fields, properties, and methods
- Option to include private members
- Useful for understanding type structure before inspecting instances

**Common use cases:**
- Discovering type structure
- Understanding component APIs
- Exploring available types in the game""",
            inputSchema={
                "type": "object",
                "properties": {
                    "type_name": {
                        "type": "string",
                        "description": "The type name to inspect"
                    },
                    "include_private": {
                        "type": "boolean",
                        "description": "Include private members",
                        "default": False
                    }
                },
                "required": ["type_name"]
            }
        ),
        Tool(
            name="s1_get_field",
            description="Get a field value from a GameObject or component.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    },
                    "field_name": {
                        "type": "string",
                        "description": "The field name"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Component type if accessing component field"
                    }
                },
                "required": ["object_name", "field_name"]
            }
        ),
        Tool(
            name="s1_set_field",
            description="Set a field value on a GameObject or component.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    },
                    "field_name": {
                        "type": "string",
                        "description": "The field name"
                    },
                    "value": {
                        "description": "The value to set"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Component type if setting component field"
                    }
                },
                "required": ["object_name", "field_name", "value"]
            }
        ),
        Tool(
            name="s1_get_component_property",
            description="Get a property value from a GameObject or component.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    },
                    "property_name": {
                        "type": "string",
                        "description": "The property name"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Component type if accessing component property"
                    }
                },
                "required": ["object_name", "property_name"]
            }
        ),
        Tool(
            name="s1_set_component_property",
            description="Set a property value on a GameObject or component.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    },
                    "property_name": {
                        "type": "string",
                        "description": "The property name"
                    },
                    "value": {
                        "description": "The value to set"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Component type if setting component property"
                    }
                },
                "required": ["object_name", "property_name", "value"]
            }
        ),
        Tool(
            name="s1_call_method",
            description="Invoke a method on a GameObject or component.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    },
                    "method_name": {
                        "type": "string",
                        "description": "The method name to call"
                    },
                    "parameters": {
                        "type": "array",
                        "description": "Optional: Method parameters",
                        "items": {}
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Component type if calling component method"
                    }
                },
                "required": ["object_name", "method_name"]
            }
        ),
        Tool(
            name="s1_is_active",
            description="Check if a GameObject or component is active/enabled.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Component type to check enabled state"
                    }
                },
                "required": ["object_name"]
            }
        ),
        Tool(
            name="s1_set_active",
            description="Set the active/enabled state of a GameObject or component.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    },
                    "active": {
                        "type": "boolean",
                        "description": "The active state to set"
                    },
                    "component_type": {
                        "type": "string",
                        "description": "Optional: Component type to set enabled state"
                    }
                },
                "required": ["object_name", "active"]
            }
        ),
        Tool(
            name="s1_get_transform",
            description="Get transform information (position, rotation, scale) for a GameObject.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    }
                },
                "required": ["object_name"]
            }
        ),
        Tool(
            name="s1_set_transform",
            description="Set transform (position, rotation, scale) for a GameObject.",
            inputSchema={
                "type": "object",
                "properties": {
                    "object_name": {
                        "type": "string",
                        "description": "The GameObject name"
                    },
                    "position": {
                        "type": "object",
                        "description": "Optional: Position {x, y, z}",
                        "properties": {
                            "x": {"type": "number"},
                            "y": {"type": "number"},
                            "z": {"type": "number"}
                        }
                    },
                    "rotation": {
                        "type": "object",
                        "description": "Optional: Rotation {x, y, z, w}",
                        "properties": {
                            "x": {"type": "number"},
                            "y": {"type": "number"},
                            "z": {"type": "number"},
                            "w": {"type": "number"}
                        }
                    },
                    "scale": {
                        "type": "object",
                        "description": "Optional: Scale {x, y, z}",
                        "properties": {
                            "x": {"type": "number"},
                            "y": {"type": "number"},
                            "z": {"type": "number"}
                        }
                    }
                },
                "required": ["object_name"]
            }
        )
    ]


async def handle_s1_inspect_object(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_inspect_object tool call."""
    object_name = arguments.get("object_name")
    object_type = arguments.get("object_type", "GameObject")
    
    if not object_name:
        return [TextContent(type="text", text="Error: object_name is required")]
    
    try:
        response = tcp_client.call_with_retry("inspect_object", {
            "object_name": object_name,
            "object_type": object_type
        })
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_inspect_object: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_inspect_component(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_inspect_component tool call."""
    component_type = arguments.get("component_type")
    object_name = arguments.get("object_name")
    npc_id = arguments.get("npc_id")
    max_depth = arguments.get("max_depth", 3)
    
    if not component_type:
        return [TextContent(type="text", text="Error: component_type is required")]
    
    try:
        params = {
            "component_type": component_type,
            "max_depth": max_depth
        }
        if object_name:
            params["object_name"] = object_name
        if npc_id:
            params["npc_id"] = npc_id
        
        response = tcp_client.call_with_retry("inspect_component", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_inspect_component: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_member_value(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_member_value tool call."""
    object_name = arguments.get("object_name")
    npc_id = arguments.get("npc_id")
    member_path = arguments.get("member_path")
    component_type = arguments.get("component_type")
    
    if not member_path:
        return [TextContent(type="text", text="Error: member_path is required")]
    
    if not object_name and not npc_id:
        return [TextContent(type="text", text="Error: either object_name or npc_id is required")]
    
    try:
        params = {
            "member_path": member_path
        }
        if object_name:
            params["object_name"] = object_name
        if npc_id:
            params["npc_id"] = npc_id
        if component_type:
            params["component_type"] = component_type
        
        response = tcp_client.call_with_retry("get_member_value", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_member_value: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_component_by_type(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_component_by_type tool call."""
    object_name = arguments.get("object_name")
    component_type = arguments.get("component_type")
    
    if not object_name or not component_type:
        return [TextContent(type="text", text="Error: object_name and component_type are required")]
    
    try:
        response = tcp_client.call_with_retry("get_component_by_type", {
            "object_name": object_name,
            "component_type": component_type
        })
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_component_by_type: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_list_components(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_list_components tool call."""
    object_name = arguments.get("object_name")
    npc_id = arguments.get("npc_id")

    if not object_name and not npc_id:
        return [TextContent(type="text", text="Error: either object_name or npc_id is required")]

    try:
        params = {}
        if object_name:
            params["object_name"] = object_name
        if npc_id:
            params["npc_id"] = npc_id

        response = tcp_client.call_with_retry("list_components", params)

        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]

        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_list_components: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_find_gameobjects(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_find_gameobjects tool call."""
    try:
        params = {}
        if "name_pattern" in arguments:
            params["name_pattern"] = arguments["name_pattern"]
        if "tag" in arguments:
            params["tag"] = arguments["tag"]
        if "layer" in arguments:
            params["layer"] = arguments["layer"]
        if "component_type" in arguments:
            params["component_type"] = arguments["component_type"]
        if "active_only" in arguments:
            params["active_only"] = arguments["active_only"]
        
        response = tcp_client.call_with_retry("find_gameobjects", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_find_gameobjects: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_search_types(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_search_types tool call."""
    pattern = arguments.get("pattern")
    if not pattern:
        return [TextContent(type="text", text="Error: pattern is required")]
    
    try:
        params = {"pattern": pattern}
        if "component_types_only" in arguments:
            params["component_types_only"] = arguments["component_types_only"]
        
        response = tcp_client.call_with_retry("search_types", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_search_types: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_scene_hierarchy(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_scene_hierarchy tool call."""
    try:
        params = {}
        if "scene_name" in arguments:
            params["scene_name"] = arguments["scene_name"]
        if "active_only" in arguments:
            params["active_only"] = arguments["active_only"]
        if "max_depth" in arguments:
            params["max_depth"] = arguments["max_depth"]
        
        response = tcp_client.call_with_retry("get_scene_hierarchy", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_scene_hierarchy: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_list_scenes(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_list_scenes tool call."""
    try:
        response = tcp_client.call_with_retry("list_scenes", {})
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_list_scenes: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_hierarchy(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_hierarchy tool call."""
    object_name = arguments.get("object_name")
    if not object_name:
        return [TextContent(type="text", text="Error: object_name is required")]
    
    try:
        response = tcp_client.call_with_retry("get_hierarchy", {"object_name": object_name})
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_hierarchy: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_list_members(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_list_members tool call."""
    try:
        params = {}
        if "type_name" in arguments:
            params["type_name"] = arguments["type_name"]
        if "object_name" in arguments:
            params["object_name"] = arguments["object_name"]
        if "include_private" in arguments:
            params["include_private"] = arguments["include_private"]
        
        response = tcp_client.call_with_retry("list_members", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_list_members: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_inspect_type(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_inspect_type tool call."""
    type_name = arguments.get("type_name")
    if not type_name:
        return [TextContent(type="text", text="Error: type_name is required")]
    
    try:
        params = {"type_name": type_name}
        if "include_private" in arguments:
            params["include_private"] = arguments["include_private"]
        
        response = tcp_client.call_with_retry("inspect_type", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_inspect_type: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_find_objects_by_type(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_find_objects_by_type tool call."""
    component_type = arguments.get("component_type")
    if not component_type:
        return [TextContent(type="text", text="Error: component_type is required")]
    
    try:
        response = tcp_client.call_with_retry("find_objects_by_type", {
            "component_type": component_type
        })
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_find_objects_by_type: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_scene_objects(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_scene_objects tool call."""
    try:
        response = tcp_client.call_with_retry("get_scene_objects", {})
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_scene_objects: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_field(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_field tool call."""
    object_name = arguments.get("object_name")
    field_name = arguments.get("field_name")
    if not object_name or not field_name:
        return [TextContent(type="text", text="Error: object_name and field_name are required")]
    
    try:
        params = {"object_name": object_name, "field_name": field_name}
        if "component_type" in arguments:
            params["component_type"] = arguments["component_type"]
        
        response = tcp_client.call_with_retry("get_field", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_field: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_set_field(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_set_field tool call."""
    object_name = arguments.get("object_name")
    field_name = arguments.get("field_name")
    value = arguments.get("value")
    if not object_name or not field_name or value is None:
        return [TextContent(type="text", text="Error: object_name, field_name, and value are required")]
    
    try:
        params = {"object_name": object_name, "field_name": field_name, "value": value}
        if "component_type" in arguments:
            params["component_type"] = arguments["component_type"]
        
        response = tcp_client.call_with_retry("set_field", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_set_field: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_component_property(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_component_property tool call."""
    object_name = arguments.get("object_name")
    property_name = arguments.get("property_name")
    if not object_name or not property_name:
        return [TextContent(type="text", text="Error: object_name and property_name are required")]
    
    try:
        params = {"object_name": object_name, "property_name": property_name}
        if "component_type" in arguments:
            params["component_type"] = arguments["component_type"]
        
        response = tcp_client.call_with_retry("get_component_property", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_component_property: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_set_component_property(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_set_component_property tool call."""
    object_name = arguments.get("object_name")
    property_name = arguments.get("property_name")
    value = arguments.get("value")
    if not object_name or not property_name or value is None:
        return [TextContent(type="text", text="Error: object_name, property_name, and value are required")]
    
    try:
        params = {"object_name": object_name, "property_name": property_name, "value": value}
        if "component_type" in arguments:
            params["component_type"] = arguments["component_type"]
        
        response = tcp_client.call_with_retry("set_component_property", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_set_component_property: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_call_method(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_call_method tool call."""
    object_name = arguments.get("object_name")
    method_name = arguments.get("method_name")
    if not object_name or not method_name:
        return [TextContent(type="text", text="Error: object_name and method_name are required")]
    
    try:
        params = {"object_name": object_name, "method_name": method_name}
        if "parameters" in arguments:
            params["parameters"] = arguments["parameters"]
        if "component_type" in arguments:
            params["component_type"] = arguments["component_type"]
        
        response = tcp_client.call_with_retry("call_method", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_call_method: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_is_active(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_is_active tool call."""
    object_name = arguments.get("object_name")
    if not object_name:
        return [TextContent(type="text", text="Error: object_name is required")]
    
    try:
        params = {"object_name": object_name}
        if "component_type" in arguments:
            params["component_type"] = arguments["component_type"]
        
        response = tcp_client.call_with_retry("is_active", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_is_active: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_set_active(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_set_active tool call."""
    object_name = arguments.get("object_name")
    active = arguments.get("active")
    if not object_name or active is None:
        return [TextContent(type="text", text="Error: object_name and active are required")]
    
    try:
        params = {"object_name": object_name, "active": active}
        if "component_type" in arguments:
            params["component_type"] = arguments["component_type"]
        
        response = tcp_client.call_with_retry("set_active", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_set_active: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_transform(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_transform tool call."""
    object_name = arguments.get("object_name")
    if not object_name:
        return [TextContent(type="text", text="Error: object_name is required")]
    
    try:
        response = tcp_client.call_with_retry("get_transform", {"object_name": object_name})
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_transform: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_set_transform(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_set_transform tool call."""
    object_name = arguments.get("object_name")
    if not object_name:
        return [TextContent(type="text", text="Error: object_name is required")]
    
    try:
        params = {"object_name": object_name}
        if "position" in arguments:
            params["position"] = arguments["position"]
        if "rotation" in arguments:
            params["rotation"] = arguments["rotation"]
        if "scale" in arguments:
            params["scale"] = arguments["scale"]
        
        response = tcp_client.call_with_retry("set_transform", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_set_transform: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


# Tool handler mapping
TOOL_HANDLERS = {
    "s1_inspect_object": handle_s1_inspect_object,
    "s1_inspect_component": handle_s1_inspect_component,
    "s1_get_member_value": handle_s1_get_member_value,
    "s1_get_component_by_type": handle_s1_get_component_by_type,
    "s1_list_components": handle_s1_list_components,
    "s1_find_gameobjects": handle_s1_find_gameobjects,
    "s1_search_types": handle_s1_search_types,
    "s1_get_scene_hierarchy": handle_s1_get_scene_hierarchy,
    "s1_list_scenes": handle_s1_list_scenes,
    "s1_get_hierarchy": handle_s1_get_hierarchy,
    "s1_find_objects_by_type": handle_s1_find_objects_by_type,
    "s1_get_scene_objects": handle_s1_get_scene_objects,
    "s1_list_members": handle_s1_list_members,
    "s1_inspect_type": handle_s1_inspect_type,
    "s1_get_field": handle_s1_get_field,
    "s1_set_field": handle_s1_set_field,
    "s1_get_component_property": handle_s1_get_component_property,
    "s1_set_component_property": handle_s1_set_component_property,
    "s1_call_method": handle_s1_call_method,
    "s1_is_active": handle_s1_is_active,
    "s1_set_active": handle_s1_set_active,
    "s1_get_transform": handle_s1_get_transform,
    "s1_set_transform": handle_s1_set_transform,
}

