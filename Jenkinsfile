pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO                = '1'
        COMPOSE_PROJECT_NAME         = "api-test-${BUILD_NUMBER}"
        ALLURE_RESULTS_DIR           = 'allure-results'
        TEST_RESULTS_DIR             = 'TestResults'
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
            steps {
                sh 'dotnet restore ApiTestFramework.sln'
                sh 'dotnet build ApiTestFramework.sln -c Release --no-restore'
            }
        }

        stage('Start Services') {
            steps {
                sh '''
                    docker compose up -d --build
                    echo "Waiting for services to become healthy..."
                    for i in $(seq 1 60); do
                        AUTH=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5300/swagger/v1/swagger.json)
                        PROD=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5100/swagger/v1/swagger.json)
                        ORD=$(curl -s -o /dev/null -w "%{http_code}"  http://localhost:5200/swagger/v1/swagger.json)
                        if [ "$AUTH" = "200" ] && [ "$PROD" = "200" ] && [ "$ORD" = "200" ]; then
                            echo "All services ready."
                            exit 0
                        fi
                        echo "  auth=$AUTH product=$PROD order=$ORD — attempt $i/60"
                        sleep 3
                    done
                    echo "Services did not become ready in time."
                    docker compose logs
                    exit 1
                '''
            }
        }

        stage('Run Tests') {
            steps {
                sh """
                    mkdir -p ${TEST_RESULTS_DIR}
                    dotnet test tests/ApiTestFramework.Tests/ApiTestFramework.Tests.csproj \\
                        -c Release --no-build \\
                        --logger "trx;LogFileName=results.trx" \\
                        --results-directory ${TEST_RESULTS_DIR} \\
                        -- NUnit.WorkDirectory=${WORKSPACE}
                """
            }
        }
    }

    post {
        always {
            // Publish Allure report (Allure.NUnit writes to allure-results/ in workspace root)
            allure([
                includeProperties: false,
                jdk              : '',
                properties       : [],
                reportBuildPolicy: 'ALWAYS',
                results          : [[path: "${ALLURE_RESULTS_DIR}"]]
            ])

            // Publish TRX as JUnit XML for the test trend graph
            junit(
                testResults            : "${TEST_RESULTS_DIR}/*.trx",
                allowEmptyResults      : true,
                skipPublishingChecks   : true
            )

            // Archive raw artifacts
            archiveArtifacts artifacts: "${TEST_RESULTS_DIR}/**/*,${ALLURE_RESULTS_DIR}/**/*",
                             allowEmptyArchive: true

            // Tear down containers regardless of outcome
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
