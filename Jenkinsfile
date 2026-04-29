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
                    # Services have fixed container_name values, so we use them directly.
                    # We reach each service on its internal port 8080 via its container IP
                    # (Jenkins container != Docker host, so published ports are not on 'localhost').
                    for i in $(seq 1 60); do
                        AUTH_IP=$(docker inspect auth-service    --format "{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}" 2>/dev/null || true)
                        PROD_IP=$(docker inspect product-service --format "{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}" 2>/dev/null || true)
                        ORD_IP=$(docker  inspect order-service   --format "{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}" 2>/dev/null || true)

                        if [ -z "$AUTH_IP" ] || [ -z "$PROD_IP" ] || [ -z "$ORD_IP" ]; then
                            echo "  Containers not yet running (attempt $i/60)"
                            sleep 5
                            continue
                        fi

                        AUTH=$(curl -s -o /dev/null -w "%{http_code}" http://${AUTH_IP}:8080/swagger/v1/swagger.json 2>/dev/null || echo 0)
                        PROD=$(curl -s -o /dev/null -w "%{http_code}" http://${PROD_IP}:8080/swagger/v1/swagger.json 2>/dev/null || echo 0)
                        ORD=$(curl  -s -o /dev/null -w "%{http_code}" http://${ORD_IP}:8080/swagger/v1/swagger.json  2>/dev/null || echo 0)
                        echo "  auth=${AUTH} product=${PROD} order=${ORD} (attempt $i/60)"

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
                    // Join the compose network so service DNS names resolve inside the container.
                    // Config is overridden via TEST_-prefixed env vars to point at service names:port 8080.
                    args  "--network ${COMPOSE_PROJECT_NAME}_default"
                    reuseNode true
                }
            }
            steps {
                sh """
                    mkdir -p ${TEST_RESULTS_DIR}
                    export TEST_AuthService__BaseUrl=http://auth-service:8080
                    export TEST_ProductService__BaseUrl=http://product-service:8080
                    export TEST_OrderService__BaseUrl=http://order-service:8080
                    dotnet test tests/ApiTestFramework.Tests/ApiTestFramework.Tests.csproj \\
                        -c Release --no-build \\
                        --logger "trx;LogFileName=results.trx" \\
                        --results-directory ${TEST_RESULTS_DIR}
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
