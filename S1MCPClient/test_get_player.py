"""Test script to call get_player and display results."""

import sys
import json
from pathlib import Path

# Add src to path
sys.path.insert(0, str(Path(__file__).parent / "src"))

from src.tcp_client import TcpClient, TcpConnectionError
from src.utils.config import Config
from src.utils.logger import setup_logger, get_logger

def main():
    """Test get_player tool."""
    # Setup logging
    config = Config.from_file()
    setup_logger(config.log_level)
    logger = get_logger()
    
    # Create TCP client
    client = TcpClient(
        host=config.host,
        port=config.port,
        timeout=config.connection_timeout,
        reconnect_delay=config.reconnect_delay
    )
    
    try:
        logger.info(f"Connecting to server at {config.host}:{config.port}...")
        client.connect()
        logger.info("Connected successfully!")
        
        # Call get_player
        logger.info("Calling get_player...")
        response = client.call_with_retry("get_player", {})
        
        if response.error:
            print(f"\n❌ Error: {response.error.message} (code: {response.error.code})")
            if response.error.data:
                print(f"Error data: {json.dumps(response.error.data, indent=2)}")
        else:
            print("\n✅ Player Information:")
            print("=" * 60)
            print(json.dumps(response.result, indent=2))
            print("=" * 60)
        
    except TcpConnectionError as e:
        logger.error(f"Connection error: {e}")
        print(f"\n❌ Connection Error: {e}")
        print("\nMake sure:")
        print("1. The game is running with the S1MCPServer mod loaded")
        print("2. The mod has started the TCP server on port 8765")
        sys.exit(1)
    except Exception as e:
        logger.error(f"Unexpected error: {e}", exc_info=True)
        print(f"\n❌ Unexpected Error: {e}")
        sys.exit(1)
    finally:
        if client.is_connected():
            logger.info("Disconnecting...")
            client.disconnect()

if __name__ == "__main__":
    main()


