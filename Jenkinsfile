pipeline {
    agent any

    tools {
        maven 'Maven 3.9.1' // MUST match the Maven name configured in Global Tool Configuration
    }

    environment {
        PROJECT_NAME = 'OnlineAssessment'
        BRANCH_NAME = 'main'
        SONAR_HOST_URL = 'http://your.sonarqube.server'     // Replace with your real SonarQube server URL
        SONAR_PROJECT_KEY = 'your_project_key'              // Replace with your SonarQube project key
        EC2_USER = 'ubuntu'                                 // Likely 'ubuntu' for EC2 Ubuntu instances
        EC2_IP = '100.27.229.169'                           // Your EC2 public IP
        DEPLOY_PATH = '/path/to/deployment'                 // Path where you want to copy the JAR
        PRIVATE_KEY_PATH = '/path/to/your/private-key.pem'  // Path to your .pem SSH key
    }

    stages {
        stage('Checkout') {
            steps {
                git branch: "${BRANCH_NAME}", url: 'https://github.com/Raj101145/onlineAssessment-new'
            }
        }

        stage('Build') {
            steps {
                sh 'mvn clean install -DskipTests=true'
            }
        }

        stage('Test') {
            steps {
                sh 'mvn test'
            }
        }

        stage('SonarQube Analysis') {
            steps {
                withSonarQubeEnv('YourSonarQubeServer') { // Name of the Sonar server in Jenkins config
                    sh """
                        mvn sonar:sonar \
                        -Dsonar.projectKey=${SONAR_PROJECT_KEY} \
                        -Dsonar.host.url=${SONAR_HOST_URL}
                    """
                }
            }
        }

        stage('Deploy to EC2') {
            steps {
                script {
                    // Dynamically find the generated jar
                    def jarName = sh(script: "ls target/*.jar | grep -v 'original-' | xargs basename", returnStdout: true).trim()
                    
                    // Copy the artifact to EC2
                    sh """
                        chmod 400 ${PRIVATE_KEY_PATH}
                        scp -o StrictHostKeyChecking=no -i ${PRIVATE_KEY_PATH} target/${jarName} ${EC2_USER}@${EC2_IP}:${DEPLOY_PATH}/
                    """
                }
            }
        }
    }

    post {
        success {
            echo '✅ Build, Test, SonarQube Analysis, and Deployment completed successfully!'
        }
        failure {
            echo '❌ Build failed. Please check the pipeline logs.'
        }
        always {
            echo 'ℹ️ Pipeline completed. Sending notifications (optional).'
        }
    }
}
