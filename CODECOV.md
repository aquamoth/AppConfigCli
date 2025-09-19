# Code Coverage Badges

This repo already generates coverage during CI and publishes an HTML report artifact and a Markdown summary to the job summary/PR comment. To show a badge on the README, pick one of the external services below.

Options:
- Codecov (common for open source)
- Coveralls (also popular)
- Keep only GitHub summary/artifacts (no public badge)

---

## Codecov

Codecov supports tokenless uploads for public repos via the GitHub App and provides an easy badge.

1) Install the Codecov GitHub App (recommended)
- Visit the GitHub Marketplace and install Codecov for this repository.
- With the app installed on a public repo, you usually do NOT need a token.

2) Ensure coverage is produced in CI
- Already configured: `XPlat Code Coverage` collector outputs Cobertura XML under `tests/**/TestResults/*/coverage.cobertura.xml` and ReportGenerator creates an HTML report.

3) Upload coverage to Codecov
- Add this step after the coverage generation step in `.github/workflows/ci.yml`:

```yaml
      - name: Upload coverage to Codecov
        uses: codecov/codecov-action@v4
        with:
          files: tests/**/TestResults/*/coverage.cobertura.xml
          fail_ci_if_error: true
          verbose: false
        # If you do NOT use the GitHub App, set a repo secret CODECOV_TOKEN and uncomment:
        # env:
        #   CODECOV_TOKEN: ${{ secrets.CODECOV_TOKEN }}
```

4) Add the badge to README

```markdown
[![codecov](https://codecov.io/gh/aquamoth/AppConfigCli/branch/main/graph/badge.svg)](https://codecov.io/gh/aquamoth/AppConfigCli)
```

Notes
- Default branch for the badge is `main`. Change the URL if your default branch differs.
- Codecov will aggregate multiple CI runs if you add more OS/jobs later.

---

## Coveralls

Coveralls typically expects LCOV. We’ll convert Cobertura to LCOV via ReportGenerator and upload.

1) Ensure coverage is produced in CI
- Already configured: Cobertura output exists.

2) Convert Cobertura to LCOV
- Add this ReportGenerator step (in addition to the existing report):

```yaml
      - name: Generate LCOV from Cobertura
        uses: danielpalme/ReportGenerator-GitHub-Action@v5.3.9
        with:
          reports: 'tests/**/TestResults/*/coverage.cobertura.xml'
          targetdir: 'coveragereport-lcov'
          reporttypes: 'lcov'
```

3) Upload LCOV to Coveralls
- Add the Coveralls action step. For public repos, token can be optional when using GitHub App; otherwise register the repo on coveralls.io and add `COVERALLS_REPO_TOKEN` secret.

```yaml
      - name: Upload coverage to Coveralls
        uses: coverallsapp/github-action@v2
        with:
          file: coveragereport-lcov/lcov.info
          format: lcov
        # env:
        #   COVERALLS_REPO_TOKEN: ${{ secrets.COVERALLS_REPO_TOKEN }}
```

4) Add the badge to README

```markdown
[![Coverage Status](https://coveralls.io/repos/github/aquamoth/AppConfigCli/badge.svg?branch=main)](https://coveralls.io/github/aquamoth/AppConfigCli?branch=main)
```

Notes
- Ensure the repository is activated on coveralls.io.
- If using the GitHub App, token may not be required for public repos.

---

## GitHub Summary Only (no public badge)

This is already set up:
- CI posts a Markdown summary in the job summary and a sticky comment on PRs.
- CI uploads an HTML report artifact named `coverage-report`.

No additional steps are required if you don’t need a public badge.

