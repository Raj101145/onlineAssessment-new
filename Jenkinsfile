pipeline {
    agent any
    
    stages {
        stage('Create Jenkinsfile') {
            steps {
                script {
                    // Create the Jenkinsfile in the repository
                    writeFile file: 'Jenkinsfile', text: """pipeline {
    agent any
    environment {
        PROJECT_NAME = 'OnlineAssessment'
        BRANCH_NAME = 'main'
    }
    stages {
        stage('Checkout') {
            steps {
                git branch: "\${BRANCH_NAME}", url: 'https://github.com/Raj101145/onlineAssessment-new'
            }
        }
        stage('Build') {
            steps {
                script {
                    sh './gradlew build'
                }
            }
        }
        stage('Test') {
            steps {
                script {
                    sh './gradlew test'
                }
            }
        }
        stage('SonarQube Analysis') {
            steps {
                script {
                    sh 'mvn sonar:sonar -Dsonar.projectKey=your_project_key'
                }
            }
        }
        stage('Deploy') {
            steps {
                script {
                    sh 'scp target/your-artifact.jar ec2-ubuntu@100.27.229.169:/path/to/deployment'
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
}"""
                }
            }
        }
    }
}
