# Changelog

All notable changes to this repository are documented in this file.

## Unreleased

### Added
- Added framework self-tests fixture `TokenIsolationFrameworkTests` under `tests/ApiTestFramework.Tests/Framework`.
- Added Jenkins PR runbook for REST API job create/trigger flow in `scripts/jenkins-pr-job-runbook.md`.
- Added repository workflow skill in `.github/skills/pr-jenkins-workflow/SKILL.md`.

### Changed
- Extracted token-isolation framework tests from ProductService test suite into a dedicated framework suite.
- Updated Copilot repository instructions to reference the Jenkins PR runbook and required delivery flow.

### Removed
- Removed `MultiUserProductTests` from `tests/ApiTestFramework.Tests/ProductService` after extraction.
- Removed temporary repository artifact `jenkins-pr8-config.xml` (Jenkins job config export).
