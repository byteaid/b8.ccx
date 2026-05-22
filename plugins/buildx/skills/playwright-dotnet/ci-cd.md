# CI/CD

GitHub Actions, Azure Pipelines, browser caching, Docker.

## GitHub Actions

```yaml
name: E2E Tests
on:
  push: { branches: [main] }
  pull_request: { branches: [main] }

jobs:
  test:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }

      - name: Restore & Build
        run: dotnet build

      - name: Install Playwright Browsers
        run: pwsh src/MyApp.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps

      - name: Run tests
        run: |
          dotnet test src/MyApp.Tests \
            --no-build \
            --settings src/MyApp.Tests/.runsettings \
            --logger "trx;LogFileName=results.trx" \
            --logger "html;LogFileName=results.html"
        env:
          CI: true

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: |
            **/TestResults/**/*.trx
            **/TestResults/**/*.html
          retention-days: 30

      - name: Upload traces and screenshots on failure
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: playwright-artifacts
          path: |
            **/TestResults/**/*.zip
            **/TestResults/**/*.png
            **/TestResults/**/*.webm
          retention-days: 14
```

### Cache browsers

```yaml
      - name: Cache Playwright browsers
        uses: actions/cache@v4
        with:
          path: ~/.cache/ms-playwright
          key: playwright-${{ runner.os }}-${{ hashFiles('**/Microsoft.Playwright.csproj') }}

      - name: Install Playwright Browsers
        run: pwsh src/MyApp.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps
        # idempotent — on cache hit, no download
```

## Azure Pipelines

```yaml
trigger:
  branches: { include: [main] }

pool:
  vmImage: 'ubuntu-latest'

steps:
  - task: UseDotNet@2
    inputs: { version: '10.0.x' }

  - script: dotnet build
    displayName: 'Build'

  - script: pwsh src/MyApp.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps
    displayName: 'Install Playwright browsers'

  - script: |
      dotnet test src/MyApp.Tests \
        --no-build \
        --settings src/MyApp.Tests/.runsettings \
        --logger "trx;LogFileName=results.trx"
    displayName: 'Run E2E tests'
    env:
      CI: true

  - task: PublishTestResults@2
    condition: always()
    inputs:
      testRunner: VSTest
      testResultsFiles: '**/TestResults/**/*.trx'

  - task: PublishBuildArtifacts@1
    condition: failed()
    inputs:
      PathtoPublish: '**/TestResults'
      ArtifactName: playwright-artifacts
```

## Docker

Official image with browsers preinstalled:

```
mcr.microsoft.com/playwright/dotnet:v1.51.0-noble
```

Tags:
- `v1.51.0-noble` — Ubuntu 24.04 (recommended)
- `v1.51.0-jammy` — Ubuntu 22.04
- `latest` — latest stable

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/playwright/dotnet:v1.51.0-noble

WORKDIR /app
COPY . .
RUN dotnet restore
RUN dotnet build --no-restore

# Browsers already installed in the image
ENTRYPOINT ["dotnet", "test", "--no-build", "--logger", "trx"]
```

### GH Actions with container

```yaml
jobs:
  test:
    runs-on: ubuntu-latest
    container:
      image: mcr.microsoft.com/playwright/dotnet:v1.51.0-noble
    steps:
      - uses: actions/checkout@v4
      - run: dotnet build
      - run: dotnet test --no-build
```

## Tips

| # | Rule |
|---|---|
| 1 | `--with-deps` on Ubuntu — without it Chromium fails on `libatk-bridge`/`libgbm`. |
| 2 | `timeout-minutes: 30` on the job to avoid infinite runs on hangs. |
| 3 | Videos only on failure — disk + I/O cost. |
| 4 | Traces always on failure — cheap (<1MB) and invaluable. |
| 5 | If `channel: msedge`, run `playwright.ps1 install msedge` in CI. Or use bundled `chromium` in CI and `msedge` only locally. |
| 6 | MSTest `<Workers>0</Workers>` = auto (cores). On 2-core runners, set `<Workers>2</Workers>` explicitly. |
| 7 | Aspire tests need Docker on the runner (`DistributedApplicationTestingBuilder` may spin up containers). GH `ubuntu-latest` and Azure `ubuntu-latest` both have it. Windows agents may need Docker Desktop. |

## Sources

- https://playwright.dev/dotnet/docs/ci
- https://playwright.dev/dotnet/docs/docker
- https://hub.docker.com/r/microsoft/playwright-dotnet
