# Deploying the Online Assessment Platform on EC2

This guide provides instructions for deploying the Online Assessment Platform on an Amazon EC2 instance.

## Prerequisites

- An Amazon EC2 instance running Amazon Linux or Ubuntu
- .NET 6.0 SDK installed on the EC2 instance
- Git installed on the EC2 instance

## Deployment Steps

### 1. Clone the Repository

```bash
git clone <repository-url>
cd OnlineAssessmnetPlatform
```

### 2. Configure the Application

The application is configured to run on port 5057. Make sure this port is open in your EC2 security group.

### 3. Run the Application Manually

You can run the application manually using the provided script:

```bash
chmod +x run-on-ec2.sh
./run-on-ec2.sh
```

### 4. Run the Application as a Service (Recommended)

To run the application as a service that starts automatically when the EC2 instance boots:

1. Copy the service file to the systemd directory:
   ```bash
   sudo cp onlineassessment.service /etc/systemd/system/
   ```

2. Enable and start the service:
   ```bash
   sudo systemctl enable onlineassessment.service
   sudo systemctl start onlineassessment.service
   ```

3. Check the service status:
   ```bash
   sudo systemctl status onlineassessment.service
   ```

### 5. Verify the Application is Running

Open a web browser and navigate to:
```
http://<your-ec2-public-ip>:5057
```

## Troubleshooting

### Port 5057 is Already in Use

If port 5057 is already in use, you can find and stop the process using it:

```bash
sudo lsof -i :5057
sudo kill -9 <PID>
```

### Checking Application Logs

To view the application logs:

```bash
sudo journalctl -u onlineassessment.service -f
```

### Restarting the Service

If you need to restart the service:

```bash
sudo systemctl restart onlineassessment.service
```

## Important Notes

- The application uses port 5057 consistently throughout the codebase
- The PayU integration is configured to use the EC2 public IP with port 5057 for callbacks
- Make sure your EC2 security group allows inbound traffic on port 5057
