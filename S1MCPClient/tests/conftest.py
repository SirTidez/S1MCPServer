"""Shared pytest fixtures for S1MCPClient tests."""

import asyncio
import json
import struct
import threading
from typing import Any, Optional
from unittest.mock import AsyncMock, MagicMock

import pytest

from src.models.response import Response, ErrorResponse
from src.tcp_client import TcpClient


# ---------------------------------------------------------------------------
# Response helpers
# ---------------------------------------------------------------------------

def make_response(result: Any = None, *, request_id: int = 1) -> Response:
    """Return a successful Response with the given result."""
    return Response(id=request_id, result=result, error=None)


def make_error_response(message: str, code: int = -32603, *, request_id: int = 1) -> Response:
    """Return an error Response."""
    return Response(
        id=request_id,
        result=None,
        error=ErrorResponse(code=code, message=message),
    )


# ---------------------------------------------------------------------------
# Mock TCP client fixture
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_tcp() -> AsyncMock:
    """A mock TcpClient whose async_call can be configured per-test."""
    client = AsyncMock(spec=TcpClient)
    # Default: return an empty success response
    client.async_call.return_value = make_response({})
    return client
