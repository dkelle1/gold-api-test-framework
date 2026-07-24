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

        // Fast, service-free tests of the framework itself
        // (SchemaExtractor, BuilderScaffolder, NegativeCaseGenerator, DriftChecker)
        stage('Framework Unit Tests') {
            agent {
                docker {
                    image "${DOTNET_IMAGE}"
                    reuseNode true
                }
            }
            steps {
                sh """
                    mkdir -p ${TEST_RESULTS_DIR}
                    dotnet test tests/ApiTestFramework.OpenApi.Tests/ApiTestFramework.OpenApi.Tests.csproj \\
                        -c Release --no-build \\
                        --logger "trx;LogFileName=unit-results.trx" \\
                        --results-directory ${TEST_RESULTS_DIR}
                """
            }
        }

        // Gate: committed *.g.cs builders and test scaffolds must match what the
        // generator produces from the committed swagger files. A red build here
        // means someone changed swagger/DTOs without running scripts/regenerate-all.ps1.
        stage('Verify Generated Code') {
            agent {
                docker {
                    image "${DOTNET_IMAGE}"
                    reuseNode true
                }
            }
            steps {
                sh '''
                    dotnet run --project tests/ApiTestFramework.Generator.Cli -c Release --no-build -- .
                    git -c safe.directory='*' diff --exit-code -- \
                        'tests/ApiTestFramework.Steps/Builders' \
                        'tests/ApiTestFramework.Tests/Generated' \
                        || { echo 'ERROR: generated code is stale — run scripts/regenerate-all.ps1 and commit.'; exit 1; }
                '''
            }
        }

        stage('Start Services') {
            steps {
                // --wait blocks until all container healthchecks pass (or timeout)
                sh 'docker compose up -d --build --wait'
            }
        }

        // Gate: the committed swagger files must structurally match what the
        // running services actually serve (endpoints, schemas, property types).
        // Soft differences (nullable/required flags) are warnings only.
        stage('Swagger Drift Check') {
            agent {
                docker {
                    image "${DOTNET_IMAGE}"
                    args  "--network ${COMPOSE_PROJECT_NAME}_default"
                    reuseNode true
                }
            }
            steps {
                sh '''
                    mkdir -p live-swagger
                    curl -sf http://auth-service:8080/swagger/v1/swagger.json    -o live-swagger/auth-swagger.json
                    curl -sf http://product-service:8080/swagger/v1/swagger.json -o live-swagger/product-swagger.json
                    curl -sf http://order-service:8080/swagger/v1/swagger.json   -o live-swagger/order-swagger.json
                    dotnet run --project tests/ApiTestFramework.Generator.Cli -c Release --no-build -- check-drift live-swagger .
                '''
            }
        }

        stage('Run Tests') {
            agent {
                docker {
                    image "${DOTNET_IMAGE}"
                    // Join the same compose network so service names resolve
                    args  "--network ${COMPOSE_PROJECT_NAME}_default"
                    reuseNode true
                }
            }
            steps {
                sh """
                    mkdir -p ${TEST_RESULTS_DIR}
                    mkdir -p ${ALLURE_RESULTS_DIR}
                    export TEST_AuthService__BaseUrl=http://auth-service:8080
                    export TEST_ProductService__BaseUrl=http://product-service:8080
                    export TEST_OrderService__BaseUrl=http://order-service:8080
                    dotnet test tests/ApiTestFramework.Tests/ApiTestFramework.Tests.csproj \\
                        -c Release --no-build \\
                        --logger "trx;LogFileName=results.trx" \\
                        --results-directory ${TEST_RESULTS_DIR}
                    # Allure.NUnit 2.12.x writes results relative to the test assembly dir.
                    # Copy them to the workspace-root dir that the Jenkins Allure plugin reads.
                    find tests -path "*/bin/*/allure-results" -type d | while IFS= read -r dir; do
                        cp -r "\$dir"/. ${ALLURE_RESULTS_DIR}/
                    done
                """
            }
        }
    }

    post {
        always {
            // Capture service logs before teardown (useful for debugging)
            sh 'docker compose logs --tail=100 || true'

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
            echo 'Tests failed — check the Allure report for details.'
        }
    }
}
