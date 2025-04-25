# Online Assessment MCQs Platform Deployment Guide (AWS EC2 + Hostinger MySQL)

This guide will walk you through deploying the Online Assessment MCQs web application on an AWS EC2 instance, with the MySQL database hosted on Hostinger. It covers all steps from server setup to making the application live.

---

## Prerequisites

- AWS account with permission to launch EC2 instances
- Hostinger account with MySQL database created
- Domain name (optional, for production)
- SSH client (e.g., Terminal, PuTTY)
- Docker (recommended for easier deployment)
- .NET 8.0 SDK (if not using Docker)

---

## 1. Prepare Hostinger MySQL Database

1. Log in to your Hostinger panel.
2. Create a new MySQL database and user.
3. Note down:
   - Database name
   - Username
   - Password
   - Host (usually something like `mysql.hostinger.com`)
4. Allow remote connections if needed (check Hostinger docs).

---

## 2. Launch and Configure AWS EC2 Instance

1. **Launch EC2 Instance:**
   - Choose Ubuntu 22.04 LTS (recommended) or Windows Server.
   - Select instance type (e.g., t2.micro for testing).
   - Configure security group:
     - Allow inbound: SSH (22), HTTP (80), HTTPS (443), and your app port (e.g., 5000-5004).
   - Launch and download the PEM key.

2. **Connect via SSH:**
   ```sh
   ssh -i path/to/key.pem ec2-user@<EC2_PUBLIC_IP>
   ```

3. **Update and Install Dependencies:**
   ```sh
   sudo yum update -y
   sudo amazon-linux-extras install docker -y
   sudo yum install -y docker
   sudo systemctl start docker
   sudo systemctl enable docker
   sudo usermod -aG docker $USER
   # Install Docker Compose
   sudo curl -L "https://github.com/docker/compose/releases/download/v2.18.1/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
   sudo chmod +x /usr/local/bin/docker-compose
   # (Optional) For non-Docker deployment
   sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm
   sudo yum install -y dotnet-sdk-8.0
   ```
   Logout and log back in if you added yourself to the docker group.

---

## 3. Deploy the Application

### Option A: Using Docker (Recommended)

1. **Copy Project Files:**
   - SCP or Git clone your project to the EC2 instance.
   - Example (using SCP):
     ```sh
     scp -i path/to/key.pem -r /local/path/to/OnlineAssessmentMcqs-main\ 2 ec2-user@<EC2_PUBLIC_IP>:/home/ec2-user/
     ```

2. **Configure Connection String:**
   - Edit `appsettings.Production.json` (or `appsettings.json`) in the project.
   - Set the `ConnectionStrings:DefaultConnection` to use Hostinger's DB info:
     ```json
     "ConnectionStrings": {
       "DefaultConnection": "Server=<HOSTINGER_DB_HOST>;Database=<DB_NAME>;User=<USER>;Password=<PASSWORD>;"
     }
     ```

3. **Build and Run with Docker:**
   - Ensure a `Dockerfile` and (optionally) `docker-compose.yml` are present.
   - Build and run:
     ```sh
     cd /home/ec2-user/OnlineAssessmentMcqs-main\ 2
     docker build -t online-assessment .
     docker run -d -p 5000:5000 --env ASPNETCORE_ENVIRONMENT=Production online-assessment
     ```
   - For dynamic ports (5000-5004), map as needed:
     ```sh
     docker run -d -p 5000-5004:5000-5004 ...
     ```

### Option B: Without Docker

1. **Install .NET 8.0:** (see above)
2. **Publish the App:**
   ```sh
   dotnet publish -c Release -o out
   cd out
   dotnet OnlineAssessment.Web.dll
   ```

---

## 4. Configure Security & Firewall

- Ensure EC2 security group allows inbound traffic on your app ports.
- (Optional) Use Nginx as a reverse proxy for SSL and domain binding.

---

## 5. Set Up Domain and SSL (Optional but Recommended)

1. Point your domain's A record to the EC2 public IP.
2. Use Nginx and Certbot for SSL:
   ```sh
   sudo amazon-linux-extras install nginx1 -y
   sudo yum install -y nginx
   sudo systemctl start nginx
   sudo systemctl enable nginx
   sudo yum install -y python3-certbot-nginx
   # Configure Nginx, then:
   sudo certbot --nginx -d yourdomain.com
   ```

---

## 6. Make the App Live

- Ensure the app is running and accessible on the public IP/domain.
- Test endpoints (login, test booking, etc.).
- Monitor logs for errors.

---

## 7. Maintenance & Tips

- Use `docker logs <container>` or check logs in `/logs` folder.
- For updates, pull new code, rebuild, and restart the container or app.
- Secure your PEM key and credentials.

---

## Troubleshooting

- **DB Connection Issues:**
  - Check Hostinger's remote DB access settings.
  - Verify security group/firewall rules.
- **Ports Not Accessible:**
  - Confirm EC2 security group and firewall settings.
- **App Crashes:**
  - Check logs for stack traces and errors.

---

## Useful Links
- [AWS EC2 Documentation](https://docs.aws.amazon.com/ec2/)
- [Hostinger MySQL Remote Access](https://www.hostinger.com/tutorials/how-to-connect-to-mysql-database-remotely)
- [Docker Documentation](https://docs.docker.com/)
- [ASP.NET Core Deployment](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/)

---

## Contact & Support
## Tech Lead : hasrsh.singh@bridgegroupsolutions.com
## Assistant : iqbalshoeb455@gmail.com
## Assistant : karanbeer126@gmail.com
