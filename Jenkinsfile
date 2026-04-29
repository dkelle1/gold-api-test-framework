pipeline {
    agent any

    tools {
        dotnetsdk 'dotnet-8'
    }

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
        TEST_ENVIRONMENT = 'CI'
        AUTH_SERVICE_URL = 'http://localhost:5300'
        PRODUCT_SERVICE_URL = 'http://localhost:5100'
        ORDER_SERVICE_URL = 'http://localhost:5200'
    }

    options {
        timeout(time: 30, unit: 'MINUTES')
        timestamps()
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
            }
        }

        stage('Restore') {
            steps {
                sh 'dotnet restore ApiTestFramework.sln'
            }
        }

        stage('Build Microservices') {
            steps {
                sh 'dotnet build src/AuthService/AuthService.csproj -c Release --no-restore'
                sh 'dotnet build src/ProductService/ProductService.csproj -c Release --no-restore'
                sh 'dotnet build src/OrderService/OrderService.csproj -c Release --no-restore'
            }
        }

        stage('Build Test Framework') {
            steps {
                sh 'dotnet build tests/ApiTestFramework.Tests/ApiTestFramework.Tests.csproj -c Release --no-restore'
            }
        }

        stage('Start Microservices') {
            steps {
                script {
                    // Start AuthService in background
                    sh '''
                        nohup dotnet run --project src/AuthService/AuthService.csproj \
                            -c Release --no-build \
                            --urls http://localhost:5300 &
                        echo $! > auth-service.pid
                    '''

                    // Start ProductService in background
                    sh '''
                        nohup dotnet run --project src/ProductService/ProductService.csproj \
                            -c Release --no-build \
                            --urls http://localhost:5100 &
                        echo $! > product-service.pid
                    '''

                    // Start OrderService in background
                    sh '''
                        nohup dotnet run --project src/OrderService/OrderService.csproj \
                            -c Release --no-build \
                            --urls http://localhost:5200 &
                        echo $! > order-service.pid
                    '''

                    // Wait for services to be ready
                    sh '''
                        echo "Waiting for services to start..."
                        for i in $(seq 1 30); do
                            if curl -s http://localhost:5300/swagger/v1/swagger.json > /dev/null 2>&1 && \
                               curl -s http://localhost:5100/swagger/v1/swagger.json > /dev/null 2>&1 && \
                               curl -s http://localhost:5200/swagger/v1/swagger.json > /dev/null 2>&1; then
                                echo "All three services are ready!"
                                break
                            fi
                            echo "Waiting... ($i/30)"
                            sleep 2
                        done
                    '''
                }
            }
        }

        stage('Generate NSwag Clients') {
            steps {
                script {
                    // Optional: regenerate NSwag clients from live swagger
                    sh '''
                        if command -v nswag &> /dev/null; then
                            cd tests/ApiTestFramework.Clients
                            nswag run nswag-product.nswag /runtime:Net80 || true
                            nswag run nswag-order.nswag /runtime:Net80 || true
                            cd ../..
                        else
                            echo "NSwag CLI not installed, using existing generated clients"
                        fi
                    '''
                }
            }
        }

        stage('Run Tests') {
            steps {
                sh '''
                    dotnet test tests/ApiTestFramework.Tests/ApiTestFramework.Tests.csproj \
                        -c Release --no-build \
                        --logger "trx;LogFileName=test-results.trx" \
                        --results-directory TestResults \
                        -- NUnit.WorkDirectory=TestResults
                '''
            }
        }

        stage('Generate Allure Report') {
            steps {
                script {
                    allure([
                        includeProperties: false,
                        jdk: '',
                        properties: [],
                        reportBuildPolicy: 'ALWAYS',
                        results: [[path: 'TestResults/allure-results']]
                    ])
                }
            }
        }
    }

    post {
        always {
            script {
                // Stop microservices
                sh '''
                    if [ -f auth-service.pid ]; then
                        kill $(cat auth-service.pid) || true
                        rm auth-service.pid
                    fi
                    if [ -f product-service.pid ]; then
                        kill $(cat product-service.pid) || true
                        rm product-service.pid
                    fi
                    if [ -f order-service.pid ]; then
                        kill $(cat order-service.pid) || true
                        rm order-service.pid
                    fi
                '''
            }

            // Archive test results
            archiveArtifacts artifacts: 'TestResults/**/*', allowEmptyArchive: true

            // Publish NUnit results
            nunit testResultsPattern: 'TestResults/*.trx'
        }

        success {
            echo 'All tests passed!'
        }

        failure {
            echo 'Some tests failed. Check the Allure report for details.'
        }
    }
}
