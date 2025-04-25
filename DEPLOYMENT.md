# Deployment Guide for Online Assessment Application

This guide provides instructions for deploying the Online Assessment application to an AWS EC2 instance.

## Prerequisites

- An AWS EC2 instance running Amazon Linux 2 or later
- SSH access to the EC2 instance
- Basic knowledge of Linux commands
- A domain name (optional but recommended)

## Deployment Options

You can deploy the application using one of the following methods:

1. **Direct deployment** using the provided `deploy.sh` script
2. **Docker deployment** using Docker and Docker Compose

## Option 1: Direct Deployment

### Step 1: Connect to your EC2 instance

```bash
ssh -i your-key.pem ec2-user@your-ec2-public-ip
```

### Step 2: Clone the repository

```bash
git clone https://github.com/yourusername/OnlineAssessmentMcqs.git
cd OnlineAssessmentMcqs
```

### Step 3: Update the deployment script

Edit the `deploy.sh` script and update the `EC2_PUBLIC_IP` variable with your EC2 instance's public IP address:

```bash
nano deploy.sh
# Update this line:
EC2_PUBLIC_IP="YOUR_EC2_PUBLIC_IP"
```

### Step 4: Make the script executable and run it

```bash
chmod +x deploy.sh
./deploy.sh
```

The script will:
- Install .NET SDK if not already installed
- Update the configuration with your EC2 IP address
- Build and publish the application
- Create a systemd service to run the application
- Configure the firewall to allow traffic on port 5001

### Step 5: Verify the deployment

Open a web browser and navigate to:

```
http://your-ec2-public-ip:5001
```

## Option 2: Docker Deployment

### Step 1: Connect to your EC2 instance

```bash
ssh -i your-key.pem ubuntu@your-ec2-public-ip
```

### Step 2: Clone the repository

```bash
git clone https://github.com/yourusername/OnlineAssessmentMcqs.git
cd OnlineAssessmentMcqs
```

### Step 3: Install Docker and Docker Compose

```bash
# Install Docker
sudo yum update -y
sudo amazon-linux-extras install docker -y
sudo yum install -y docker
sudo systemctl start docker
sudo systemctl enable docker

# Install Docker Compose
sudo curl -L "https://github.com/docker/compose/releases/download/v2.18.1/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
sudo chmod +x /usr/local/bin/docker-compose

# Add your user to the docker group
sudo usermod -aG docker $USER
newgrp docker
```

### Step 4: Update the configuration

Edit the `OnlineAssessment.Web/appsettings.Production.json` file and update all occurrences of `YOUR_EC2_PUBLIC_IP` with your EC2 instance's public IP address:

```bash
sed -i "s/YOUR_EC2_PUBLIC_IP/your-ec2-public-ip/g" OnlineAssessment.Web/appsettings.Production.json
```

### Step 5: Build and run the Docker container

```bash
docker-compose up -d
```

### Step 6: Verify the deployment

Open a web browser and navigate to:

```
http://your-ec2-public-ip:5001
```

## Setting Up a Domain Name (Optional)

For a more professional setup, you can configure a domain name to point to your application:

1. Register a domain name if you don't already have one
2. Create an A record in your DNS settings pointing to your EC2 instance's public IP
3. Update the `appsettings.Production.json` file to use your domain name instead of the IP address

## Configuring HTTPS (Optional but Recommended)

For a secure connection, you should set up HTTPS:

1. Install Nginx as a reverse proxy:
   ```bash
   sudo amazon-linux-extras install nginx1 -y
   sudo yum install -y nginx
   sudo systemctl start nginx
   sudo systemctl enable nginx
   ```

2. Install Certbot for Let's Encrypt SSL certificates:
   ```bash
   sudo yum install -y python3-certbot-nginx
   ```

3. Configure Nginx as a reverse proxy:
   ```bash
   sudo nano /etc/nginx/sites-available/onlineassessment
   ```

   Add the following configuration:
   ```
   server {
       listen 80;
       server_name your-domain.com;

       location / {
           proxy_pass http://localhost:5001;
           proxy_http_version 1.1;
           proxy_set_header Upgrade $http_upgrade;
           proxy_set_header Connection keep-alive;
           proxy_set_header Host $host;
           proxy_cache_bypass $http_upgrade;
           proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
           proxy_set_header X-Forwarded-Proto $scheme;
       }
   }
   ```

4. Enable the site and get an SSL certificate:
   ```bash
   sudo ln -s /etc/nginx/sites-available/onlineassessment /etc/nginx/sites-enabled/
   sudo nginx -t
   sudo systemctl reload nginx
   sudo certbot --nginx -d your-domain.com
   ```

5. Update the application configuration to use HTTPS:
   ```bash
   nano OnlineAssessment.Web/appsettings.Production.json
   ```

   Update the URLs to use HTTPS and your domain name:
   ```json
   "PayU": {
     "SuccessUrl": "https://your-domain.com/Payment/Success",
     "FailureUrl": "https://your-domain.com/Payment/Failure"
   },
   "ApplicationUrl": "https://your-domain.com"
   ```

## Troubleshooting

### Checking Application Logs

If you deployed using the systemd service:
```bash
sudo journalctl -u onlineassessment -f
```

If you deployed using Docker:
```bash
docker-compose logs -f
```

### Checking Nginx Logs (if using Nginx)

```bash
sudo tail -f /var/log/nginx/error.log
sudo tail -f /var/log/nginx/access.log
```

### Common Issues

1. **Application not accessible**: Check that the security group for your EC2 instance allows inbound traffic on port 5001 (or 80/443 if using Nginx)

2. **Database connection issues**: Verify that your database connection string is correct and that the database server allows connections from your EC2 instance

3. **PayU integration not working**: Make sure the PayU configuration in `appsettings.Production.json` is correct and that the callback URLs are accessible from the internet
