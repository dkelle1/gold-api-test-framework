pipeline {
    // Run on the Jenkins controller which has Docker socket access
    agent { label 'built-in' }

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT      = '1'
        DOTNET_NOLOGO                    = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        // When the SDK container runs as uid 1000 (jenkins), /.dotnet is not writable.
        // Point the CLI home to the writable workspace directory instead.
        DOTNET_CLI_HOME                  = "${WORKSPACE}/.dotnet-home"
        COMPOSE_PROJECT_NAME             = "api-test-${BUILD_NUMBER}"
        ALLURE_RESULTS_DIR               = 'allure-results'
        TEST_RESULTS_DIR                 = 'TestResults'
        DOTNET_IMAGE                     = 'mcr.microsoft.com/dotnet/sdk:8.0'
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

        stage('Restore & Build') {
            agent {
                docker {
                    image "${DOTNET_IMAGE}"
                    reuseNode true
                }
            }
            steps {
                sh 'dotnet restore ApiTestFramework.sln'
                sh 'dotnet build ApiTestFramework.sln -c Release --no-restore'
            }
        }

        stage('Start Services') {
            steps {
                sh 'docker compose up -d --build'
                sh '''
                    echo "Waiting for services to become healthy..."
                    for i in $(seq 1 60); do
                        AUTH=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5300/swagger/v1/swagger.json 2>/dev/null || echo 0)
                        PROD=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5100/swagger/v1/swagger.json 2>/dev/null || echo 0)
                        ORD=$(curl  -s -o /dev/null -w "%{http_code}" http://localhost:5200/swagger/v1/swagger.json 2>/dev/null || echo 0)
                        echo "  auth=$AUTH product=$PROD order=$ORD (attempt $i/60)"
                        if [ "$AUTH" = "200" ] && [ "$PROD" = "200" ] && [ "$ORD" = "200" ]; then
                            echo "All services ready."
                            exit 0
                        fi
                        sleep 5
                    done
                    echo "Services did not become ready in time."
                    docker compose logs
                    exit 1
                '''
            }
        }

        stage('Run Tests') {
            agent {
                docker {
                    image "${DOTNET_IMAGE}"
                    // --network host lets the container reach localhost:5300/5100/5200
                    args  '--network host'
                    reuseNode true
                }
            }
            steps {
                sh """
                    mkdir -p ${TEST_RESULTS_DIR}
                    dotnet test tests/ApiTestFramework.Tests/ApiTestFramework.Tests.csproj \\
                        -c Release --no-build \\
                        --logger "trx;LogFileName=results.trx" \\
                        --results-directory ${TEST_RESULTS_DIR} \\
                        -- NUnit.WorkDirectory="${WORKSPACE}"
                """
            }
        }
    }

    post {
        always {
            // Allure report (Allure.NUnit writes to allure-results/ in workspace root)
            allure([
                includeProperties: false,
                jdk              : '',
                properties       : [],
                reportBuildPolicy: 'ALWAYS',
                results          : [[path: "${ALLURE_RESULTS_DIR}"]]
            ])

            // TRX as JUnit for the trend graph
            junit(
                testResults          : "${TEST_RESULTS_DIR}/*.trx",
                allowEmptyResults    : true,
                skipPublishingChecks : true
            )

            archiveArtifacts artifacts: "${TEST_RESULTS_DIR}/**/*,${ALLURE_RESULTS_DIR}/**/*",
                             allowEmptyArchive: true

            sh 'docker compose down -v || true'
        }

        success {
            echo 'All tests passed!'
        }

        failure {
            sh 'docker compose logs --tail=100 || true'
            echo 'Tests failed — check the Allure report for details.'
        }
    }
}
