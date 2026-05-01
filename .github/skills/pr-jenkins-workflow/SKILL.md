---
name: pr-jenkins-workflow
description: Team delivery workflow for implementing changes, creating PRs, validating PRs in Jenkins, documenting outcomes, and updating changelog before manual merge.
---

# PR + Jenkins Workflow

Use this workflow as the default delivery process for this repository unless the user explicitly asks for a different flow.

## Required sequence

1. Implement requested code changes in the current branch.
2. Create or update a GitHub Pull Request with a clear summary of changes.
3. Run the Jenkins job that validates the PR (PR test pipeline).
4. Document what was changed and the test outcome.
5. Update changelog with user-visible changes.
6. Leave merge as a manual step for the user.

## Expected communication checkpoints

- After implementation: summarize files changed and key behavior impact.
- After PR creation/update: share PR link and title.
- After Jenkins run: report pass/fail and key failing tests if any.
- After documentation/changelog updates: list updated docs and entries.
- Before handoff: confirm ready for manual merge.

## Notes

- Do not perform merge unless user explicitly requests it.
- If CI fails, fix issues and rerun Jenkins PR validation.
- Keep changelog entries concise and traceable to PR scope.

## Jenkins REST API operation

Use environment variables for Jenkins access:

- `JENKINS_URL` (example: `http://localhost:8080`)
- `JENKINS_USER`
- `JENKINS_TOKEN`

### Standard PR job flow

1. Get crumb: `GET /crumbIssuer/api/json`
2. Read base job config: `GET /job/<base-job>/config.xml`
3. Update branch in XML (`<name>*/<branch></name>`)
4. Create or update PR job:
- create: `POST /createItem?name=<target-job>` with XML body
- update: `POST /job/<target-job>/config.xml` with XML body
5. Trigger build: `POST /job/<target-job>/build`
6. Track queue/build:
- queue: `GET /queue/item/<id>/api/json`
- build: `GET /job/<target-job>/<build>/api/json`
- logs: `GET /job/<target-job>/<build>/consoleText`

### PowerShell pattern (non-interactive)

Use `-UseBasicParsing` and Basic Auth header built from env vars.

```powershell
$pair = "$env:JENKINS_USER`:$env:JENKINS_TOKEN"
$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$headers = @{ Authorization = "Basic $auth" }

$crumb = Invoke-RestMethod -Uri "$env:JENKINS_URL/crumbIssuer/api/json" -Headers $headers -UseBasicParsing
$headers[$crumb.crumbRequestField] = $crumb.crumb

Invoke-WebRequest -Method Post -Uri "$env:JENKINS_URL/job/<job-name>/build" -Headers $headers -UseBasicParsing
```

### Known CI infra issue and fixes

If build fails with:
`permission denied while trying to connect to /var/run/docker.sock`

Preferred durable fix:

- Ensure Jenkins startup logic handles docker.sock with `GID=0` (root), e.g. add `jenkins` user to `root` group at startup.
- Keep this in container start script so it survives restarts.

Fallback runtime fix (temporary):

```bash
chmod 666 /var/run/docker.sock
```

Validation before rerun:

```bash
docker exec -u jenkins jenkins docker version
```
