pipeline {
    agent { label 'built-in' }

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
                bat 'dotnet restore ApiTestFramework.sln'
                bat 'dotnet build ApiTestFramework.sln -c Release --no-restore'
            }
        }

        stage('Start Services') {
            steps {
                bat 'docker compose up -d --build'
                powershell '''
                    Write-Host "Waiting for services to become healthy..."
                    $timeout = 60
                    for ($i = 1; $i -le $timeout; $i++) {
                        $auth = try { (Invoke-WebRequest http://localhost:5300/swagger/v1/swagger.json -UseBasicParsing -TimeoutSec 2).StatusCode } catch { 0 }
                        $prod = try { (Invoke-WebRequest http://localhost:5100/swagger/v1/swagger.json -UseBasicParsing -TimeoutSec 2).StatusCode } catch { 0 }
                        $ord  = try { (Invoke-WebRequest http://localhost:5200/swagger/v1/swagger.json -UseBasicParsing -TimeoutSec 2).StatusCode } catch { 0 }
                        Write-Host "  auth=$auth product=$prod order=$ord (attempt $i/$timeout)"
                        if ($auth -eq 200 -and $prod -eq 200 -and $ord -eq 200) {
                            Write-Host "All services ready."
                            exit 0
                        }
                        Start-Sleep -Seconds 5
                    }
                    Write-Host "Services did not become ready in time."
                    docker compose logs
                    exit 1
                '''
            }
        }

        stage('Run Tests') {
            steps {
                powershell """
                    if (-not (Test-Path ${TEST_RESULTS_DIR})) { New-Item -ItemType Directory ${TEST_RESULTS_DIR} | Out-Null }
                    dotnet test tests/ApiTestFramework.Tests/ApiTestFramework.Tests.csproj `
                        -c Release --no-build `
                        --logger "trx;LogFileName=results.trx" `
                        --results-directory ${TEST_RESULTS_DIR} `
                        -- NUnit.WorkDirectory="${env:WORKSPACE}"
                """
            }
        }
    }

    post {
        always {
            allure([
                includeProperties: false,
                jdk              : '',
                properties       : [],
                reportBuildPolicy: 'ALWAYS',
                results          : [[path: "${ALLURE_RESULTS_DIR}"]]
            ])

            junit(
                testResults          : "${TEST_RESULTS_DIR}/*.trx",
                allowEmptyResults    : true,
                skipPublishingChecks : true
            )

            archiveArtifacts artifacts: "${TEST_RESULTS_DIR}/**/*,${ALLURE_RESULTS_DIR}/**/*",
                             allowEmptyArchive: true

            bat 'docker compose down -v || exit 0'
        }

        success {
            echo 'All tests passed!'
        }

        failure {
            bat 'docker compose logs --tail=100 || exit 0'
            echo 'Tests failed — check the Allure report for details.'
        }
    }
}
