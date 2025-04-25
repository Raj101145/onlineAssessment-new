# EC2 Deployment Guide

This guide explains how to deploy the Online Assessment Platform to an Amazon EC2 instance.

## Prerequisites

- An AWS account with access to EC2
- An EC2 instance running Amazon Linux 2
- Basic knowledge of Linux commands and SSH

## Step 1: Install .NET SDK on EC2

Connect to your EC2 instance via SSH and install the .NET SDK:

```bash
# Update the system
sudo yum update -y

# Install .NET SDK
sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm
sudo yum install -y dotnet-sdk-8.0
```

Verify the installation:

```bash
dotnet --version
```

## Step 2: Clone the Repository

Clone the repository to your EC2 instance:

```bash
# Navigate to the home directory
cd ~

# Clone the repository
git clone https://your-repository-url.git
cd OnlineAssessmentPlatform
```

## Step 3: Configure the Application

Create a production settings file:

```bash
cd OnlineAssessment.Web
```

Edit the `appsettings.Production.json` file:

```bash
nano appsettings.Production.json
```

Update the following settings:

```json
{
  "ApplicationUrl": "http://YOUR_EC2_PUBLIC_IP:5002",
  "UseHttps": "false",
  "Cookie": {
    "SameSite": "Lax",
    "SecurePolicy": "None"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5002"
      }
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "server=YOUR_MYSQL_SERVER;port=3306;database=YOUR_DATABASE;user=YOUR_USER;password=YOUR_PASSWORD"
  }
}
```

Replace:
- `YOUR_EC2_PUBLIC_IP` with your EC2 instance's public IP address (e.g., "65.2.172.129")
- `YOUR_MYSQL_SERVER`, `YOUR_DATABASE`, `YOUR_USER`, and `YOUR_PASSWORD` with your MySQL database details

### Important HTTP Configuration Notes

1. We're using port 5002 because port 5000 is often used by system services on Linux
2. The `UseHttps` setting is set to `false` since we're running on HTTP
3. The `Cookie` section explicitly sets:
   - `SameSite` to `Lax` to allow redirects from payment gateways
   - `SecurePolicy` to `None` to allow cookies over HTTP
4. The Kestrel server is configured to listen on all network interfaces (`0.0.0.0`)

## Step 4: Use the Deployment Script

We've created a deployment script to automate the setup process. Make it executable and run it:

```bash
chmod +x ./Docs/deploy-ec2.sh
./Docs/deploy-ec2.sh
```

This script will:
1. Stop any existing application
2. Check if port 5002 is in use and free it if necessary
3. Set up a systemd service
4. Set the environment to Production
5. Start the application

## Step 5: Manual Setup (Alternative)

If you prefer to set up manually, follow these steps:

### Set Environment to Production

```bash
export ASPNETCORE_ENVIRONMENT=Production
```

### Build the Application

```bash
dotnet build
```

### Create a Systemd Service

```bash
sudo cp ./Docs/onlineassessment.service /etc/systemd/system/
sudo systemctl daemon-reload
```

Or create it manually:

```bash
sudo nano /etc/systemd/system/onlineassessment.service
```

Add the following content:

```
[Unit]
Description=Online Assessment Platform
After=network.target

[Service]
WorkingDirectory=/home/ubuntu/OnlineAssessmnetPlatform/OnlineAssessment.Web
ExecStart=/usr/bin/dotnet run --urls "http://0.0.0.0:5002"
Restart=always
RestartSec=10
SyslogIdentifier=onlineassessment
User=ubuntu
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

### Enable and Start the Service

```bash
sudo systemctl enable onlineassessment.service
sudo systemctl start onlineassessment.service
```

### Check Service Status

```bash
sudo systemctl status onlineassessment.service
```

## Step 6: Configure Security Group

Make sure your EC2 security group allows inbound traffic on port 5002:

1. Go to the AWS Console > EC2 > Security Groups
2. Select the security group attached to your EC2 instance
3. Click on "Edit inbound rules"
4. Add a rule:
   - Type: Custom TCP
   - Port range: 5002
   - Source: 0.0.0.0/0 (or restrict to specific IPs if needed)
   - Description: "Application port"
5. Save rules

## Step 7: Test the Application

Open a web browser and navigate to:

```
http://YOUR_EC2_PUBLIC_IP:5002
```

## Troubleshooting

### Check Application Logs

```bash
sudo journalctl -u onlineassessment.service -f
```

### Check if Port 5002 is in Use

```bash
sudo lsof -i :5002
```

### Test PayU Integration

Navigate to:

```
http://YOUR_EC2_PUBLIC_IP:5002/Payment/TestPayU
```

This will show you the current PayU configuration and generated URLs. Verify that the Success URL shows port 5002.

### Common Issues

1. **Session Expiration**: If you encounter session expiration issues after payment:
   - Verify that the ApplicationUrl is correctly set to your EC2 public IP
   - Check that Cookie:SecurePolicy is set to "None" for HTTP
   - Ensure Cookie:SameSite is set to "Lax" to allow redirects
   - Verify that the PayU success URL includes the testId parameter

2. **Cookie Issues**: If cookies are not being set or maintained:
   - Check browser console for cookie-related errors
   - Verify that all cookie settings are configured for HTTP
   - Try clearing browser cookies and cache
   - Test with a different browser

3. **Database Connection**: If the application cannot connect to the database, check your connection string in appsettings.Production.json.

4. **Permission Issues**: If the application cannot write to files or directories, check the permissions:

```bash
sudo chown -R ec2-user:ec2-user /home/ec2-user/OnlineAssessmentPlatform
```

### HTTP-Specific Troubleshooting

1. **Verify HTTP Configuration**:
   ```bash
   curl -I http://YOUR_EC2_IP:5002
   ```
   This should return HTTP/1.1 200 OK without any HTTPS redirects.

2. **Check Cookie Headers**:
   ```bash
   curl -I -v http://YOUR_EC2_IP:5002/Auth/Login
   ```
   Look for Set-Cookie headers and verify they don't have the Secure flag.

3. **Test Session Persistence**:
   ```bash
   # Start a session
   curl -c cookies.txt -b cookies.txt http://YOUR_EC2_IP:5002/Auth/Login

   # Use the session
   curl -c cookies.txt -b cookies.txt http://YOUR_EC2_IP:5002/Test
   ```
   This should maintain the session across requests.

4. **Test PayU Redirect**:
   ```bash
   # Simulate a PayU redirect back to your application
   curl -v "http://YOUR_EC2_IP:5002/Payment/Success?testId=123&txnid=test123"
   ```
   This should not show any connection errors.
