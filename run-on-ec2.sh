#!/bin/bash

# This script runs the Online Assessment Platform on EC2 using port 5058
# Run this script from the OnlineAssessment.Web directory

# Set environment to Production
export ASPNETCORE_ENVIRONMENT=Production

# Check if port 5058 is in use
PORT_CHECK=$(lsof -i :5058 | grep LISTEN)
if [ ! -z "$PORT_CHECK" ]; then
    echo "Port 5058 is already in use. Attempting to free it..."
    echo "Current processes using port 5058:"
    lsof -i :5058

    echo "\nStopping processes using port 5058..."
    PID=$(echo $PORT_CHECK | awk '{print $2}')
    kill -9 $PID
    sleep 2

    # Check again to make sure port is free
    PORT_CHECK=$(lsof -i :5058 | grep LISTEN)
    if [ ! -z "$PORT_CHECK" ]; then
        echo "Warning: Port 5058 is still in use. You may need to manually stop the process."
        echo "Try: sudo lsof -i :5058 and then sudo kill -9 <PID>"
        exit 1
    else
        echo "Port 5058 is now free."
    fi
fi

# Build the application
dotnet build

# Run the application with explicit port 5058
echo "Starting application on http://0.0.0.0:5058"
dotnet run --urls "http://0.0.0.0:5058"
