#!/bin/bash

# This script helps deploy the Online Assessment Platform on EC2
# Run this script from the OnlineAssessment.Web directory

# Stop any existing application
echo "Stopping any existing application..."
sudo systemctl stop onlineassessment.service 2>/dev/null || true

# Check if port 5002 is in use
PORT_CHECK=$(sudo lsof -i :5002 | grep LISTEN)
if [ ! -z "$PORT_CHECK" ]; then
    echo "Port 5002 is already in use. Attempting to free it..."
    PID=$(echo $PORT_CHECK | awk '{print $2}')
    sudo kill -9 $PID
    sleep 2
fi

# Copy systemd service file
echo "Setting up systemd service..."
sudo cp ./Docs/onlineassessment.service /etc/systemd/system/
sudo systemctl daemon-reload

# Set environment to Production
echo "Setting environment to Production..."
export ASPNETCORE_ENVIRONMENT=Production

# Enable and start the service
echo "Starting the application..."
sudo systemctl enable onlineassessment.service
sudo systemctl start onlineassessment.service

# Check status
echo "Checking service status..."
sudo systemctl status onlineassessment.service

# Show URL
echo ""
echo "Application should now be running at: http://$(curl -s http://169.254.169.254/latest/meta-data/public-ipv4):5002"
echo ""
echo "To check logs, run: sudo journalctl -u onlineassessment.service -f"
