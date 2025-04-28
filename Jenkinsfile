pipeline {
    agent any

    environment {
        PROJECT_NAME = 'OnlineAssessment'
        BRANCH_NAME = 'main'
        SONAR_HOST_URL = 'http://your.sonarqube.server'  // Your SonarQube server URL
        SONAR_PROJECT_KEY = 'your_project_key'           // Replace with your project key
        MAVEN_HOME = '/opt/maven'                        // Path to Maven installation
        EC2_USER = 'ec2-ubuntu'                         // EC2 username
        EC2_IP = '100.27.229.169'                       // EC2 instance IP
        DEPLOY_PATH = '/path/to/deployment'              // Path where the artifact will be deployed
    }

    stages {
        stage('Checkout') {
            steps {
                git branch: "${BRANCH_NAME}", url: 'https://github.com/Raj101145/onlineAssessment-new'
            }
        }
        
        stage('Install Maven') {
            steps {
                script {
                    // Ensure Maven is installed and available in the PATH
                    sh 'which mvn || echo "Maven not found, installing..."'
                    sh 'sudo apt-get update && sudo apt-get install -y maven'
                }
            }
        }

        stage('Build') {
            steps {
                script {
                    // Use Maven to build the project
                    sh 'mvn clean install -DskipTests=true'
                }
            }
        }

        stage('Test') {
            steps {
                script {
                    // Run tests using Maven
                    sh 'mvn test'
                }
            }
        }

        stage('SonarQube Analysis') {
            steps {
                script {
                    // Perform SonarQube analysis with Maven
                    sh "mvn sonar:sonar -Dsonar.projectKey=${SONAR_PROJECT_KEY} -Dsonar.host.url=${SONAR_HOST_URL}"
                }
            }
        }

        stage('Deploy') {
            steps {
                script {
                    // Deploy the built artifact to EC2
                    sh "scp -i /path/to/your/private-key.pem target/your-artifact.jar ${EC2_USER}@${EC2_IP}:${DEPLOY_PATH}"
                }
            }
        }
    }

    post {
        success {
            echo 'Build succeeded! Deployment was successful.'
        }
        failure {
            echo 'Build failed. Please check the logs.'
        }
        always {
            echo 'Cleaning up or sending notifications...'
        }
    }
}
