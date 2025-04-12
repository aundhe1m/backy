#!/bin/bash

# Backy Agent installation script
# This script installs the Backy Agent as a systemd service

set -e

# Configuration
INSTALL_DIR="/opt/backy/agent"
SERVICE_NAME="backy-agent"
SERVICE_FILE="backy-agent.service"
DEFAULT_API_KEY=$(openssl rand -hex 16)
DEFAULT_PORT=5151

# Check if running as root
if [ "$EUID" -ne 0 ]; then
  echo "Please run as root"
  exit 1
fi

echo "=== Backy Agent Installation ==="
echo

# Check for required commands
for cmd in dotnet systemctl; do
  if ! command -v $cmd &> /dev/null; then
    echo "$cmd is required but not installed. Please install it first."
    exit 1
  fi
done

# Create installation directory
echo "Creating installation directory..."
mkdir -p $INSTALL_DIR

# Copy application files
echo "Copying application files..."
cp -r * $INSTALL_DIR/

# Ask for configuration
read -p "API Port (default: $DEFAULT_PORT): " API_PORT
API_PORT=${API_PORT:-$DEFAULT_PORT}

read -p "API Key (default: auto-generated): " API_KEY
API_KEY=${API_KEY:-$DEFAULT_API_KEY}

read -p "Disable API authentication? (not recommended for production) [y/N]: " DISABLE_AUTH
DISABLE_AUTH=${DISABLE_AUTH:-n}
if [[ "$DISABLE_AUTH" =~ ^[Yy]$ ]]; then
  DISABLE_AUTH="true"
  echo "WARNING: API authentication will be disabled. This is not recommended for production environments."
else
  DISABLE_AUTH="false"
fi

read -p "Excluded drives (comma separated, e.g., /dev/sda,/dev/sdb): " EXCLUDED_DRIVES

# Update configuration
echo "Updating configuration..."
CONFIG_FILE="$INSTALL_DIR/appsettings.json"

# Convert comma-separated drives to JSON array format
DRIVES_JSON="[]"
if [ ! -z "$EXCLUDED_DRIVES" ]; then
  DRIVES_JSON="["
  IFS=',' read -ra DRIVES <<< "$EXCLUDED_DRIVES"
  for i in "${!DRIVES[@]}"; do
    if [ $i -gt 0 ]; then
      DRIVES_JSON="$DRIVES_JSON, "
    fi
    DRIVES_JSON="$DRIVES_JSON \"${DRIVES[$i]}\""
  done
  DRIVES_JSON="$DRIVES_JSON]"
fi

# Create or update settings
cat > $CONFIG_FILE << EOL
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "AgentSettings": {
    "ApiPort": $API_PORT,
    "ApiKey": "$API_KEY",
    "ExcludedDrives": $DRIVES_JSON,
    "DisableApiAuthentication": $DISABLE_AUTH
  }
}
EOL

# Install the service
echo "Installing systemd service..."
cp "$INSTALL_DIR/$SERVICE_FILE" "/etc/systemd/system/$SERVICE_NAME.service"
systemctl daemon-reload
systemctl enable $SERVICE_NAME

# Set permissions
echo "Setting permissions..."
chmod +x $INSTALL_DIR/install.sh

# Start the service
echo "Starting service..."
systemctl start $SERVICE_NAME

# Show status
echo
echo "=== Installation Complete ==="
echo "API Key: $API_KEY"
echo "API Port: $API_PORT"
echo "API Authentication: ${DISABLE_AUTH/true/DISABLED}"
echo "API Authentication: ${DISABLE_AUTH/false/ENABLED}"
echo "Service Status:"
systemctl status $SERVICE_NAME --no-pager

echo
echo "You can access the Swagger documentation at http://localhost:$API_PORT/swagger"
echo "To check the service logs: journalctl -u $SERVICE_NAME -f"
echo