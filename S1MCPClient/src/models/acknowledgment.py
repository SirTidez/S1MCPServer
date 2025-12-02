"""Acknowledgment model for JSON-RPC communication."""

from pydantic import BaseModel, Field


class Acknowledgment(BaseModel):
    """Acknowledgment model for confirming receipt of responses."""
    
    id: int = Field(..., description="Request ID that this acknowledgment corresponds to")
    status: str = Field(default="received", description="Acknowledgment status")
    
    class Config:
        """Pydantic configuration."""
        json_schema_extra = {
            "example": {
                "id": 1,
                "status": "received"
            }
        }


