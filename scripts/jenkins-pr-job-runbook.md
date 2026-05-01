# Jenkins PR Job Runbook

This runbook documents how to create/update and run a PR validation job using Jenkins REST API.

## Required environment variables

Set these once in your shell or user environment:

- `JENKINS_URL` (example: `http://localhost:8080`)
- `JENKINS_USER`
- `JENKINS_TOKEN`

## PowerShell bootstrap

```powershell
$pair = "$env:JENKINS_USER`:$env:JENKINS_TOKEN"
$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$headers = @{ Authorization = "Basic $auth" }
$crumb = Invoke-RestMethod -Uri "$env:JENKINS_URL/crumbIssuer/api/json" -Headers $headers -UseBasicParsing
$headers[$crumb.crumbRequestField] = $crumb.crumb
```

## Create or update PR job from base pipeline

```powershell
$sourceJob = 'api-tests-framework'
$targetJob = 'api-tests-framework-pr8-allure-onetimesetup-guard'
$branch = 'feat/allure-onetimesetup-guard'

$config = Invoke-WebRequest -Uri "$env:JENKINS_URL/job/$sourceJob/config.xml" -Headers $headers -UseBasicParsing
$xml = $config.Content
$xml = [Regex]::Replace($xml, '<name>\*/[^<]+</name>', "<name>*/$branch</name>", 1)

$exists = $true
try {
    Invoke-RestMethod -Uri "$env:JENKINS_URL/job/$targetJob/api/json" -Headers $headers -UseBasicParsing | Out-Null
} catch {
    $exists = $false
}

if ($exists) {
    Invoke-WebRequest -Method Post -Uri "$env:JENKINS_URL/job/$targetJob/config.xml" -Headers ($headers + @{ 'Content-Type'='application/xml' }) -Body $xml -UseBasicParsing | Out-Null
} else {
    Invoke-WebRequest -Method Post -Uri "$env:JENKINS_URL/createItem?name=$targetJob" -Headers ($headers + @{ 'Content-Type'='application/xml' }) -Body $xml -UseBasicParsing | Out-Null
}
```

## Trigger and monitor

```powershell
$resp = Invoke-WebRequest -Method Post -Uri "$env:JENKINS_URL/job/$targetJob/build" -Headers $headers -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
$queueUrl = $resp.Headers['Location']
$queueUrl

Invoke-RestMethod -Uri "$env:JENKINS_URL/job/$targetJob/api/json?tree=lastBuild[number,url,building,result],lastCompletedBuild[number,url,result],inQueue,nextBuildNumber" -Headers $headers -UseBasicParsing
```

## Troubleshooting

If the pipeline fails with Docker socket permission denied:

Preferred durable fix:

1. Handle `docker.sock` GID at Jenkins startup.
2. If socket GID is `0`, add `jenkins` user to `root` group at startup.
3. Keep this logic in entrypoint/start script so it survives container restarts.

Temporary fallback:

```powershell
docker exec jenkins sh -lc "chmod 666 /var/run/docker.sock"
docker exec -u jenkins jenkins sh -lc "docker version"
```

Then rerun the Jenkins job.
