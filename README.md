# renovate-presets
Presets for Renovate

## Usage

To use these presets in your Renovate configuration, add them to the `extends` array in your `renovate.json` file:

### Default Configuration

This preset includes recommended settings, non-office hours scheduling, semantic commits, vulnerability alerts, and custom regex managers for NuGet packages (.nuspec files), Docker images, and GitHub releases.

```json
{
  "extends": [
    "github>meziantou/renovate-config"
  ]
}
```

Or reference it explicitly with the preset name:

```json
{
  "extends": [
    "github>meziantou/renovate-config:default"
  ]
}
```

### Auto-merge Configuration

This preset enables automatic merging of pull requests created by Renovate. Use this in addition to the default preset:

```json
{
  "extends": [
    "github>meziantou/renovate-config",
    "github>meziantou/renovate-config:default-automerge"
  ]
}
```

## Available Presets

- **`default`** (`default.json`): Meziantou's default Renovate configuration with recommended settings, custom regex managers for NuGet packages (.nuspec files), Docker images, and GitHub releases
- **`default-automerge`** (`default-automerge.json`): Enables automatic merging of Renovate pull requests

## Tests

The test project contains unit tests for temporary branch naming and cleanup rules, plus live system tests that run Renovate against the public `meziantou/renovate-config-tests` repository.

Run local validation and unit tests:

```shell
npm ci
npx --no-install renovate-config-validator default.json default-automerge.json renovate.json
dotnet test tests --filter "Category!=Live"
```

Live tests require a fine-grained GitHub personal access token in `RENOVATE_TEST_TOKEN`. The token must be scoped to `meziantou/renovate-config-tests` with read access to metadata and read/write access to contents and pull requests.

```shell
RENOVATE_TEST_TOKEN=... dotnet test tests --filter "Category=Live"
```

Live tests create temporary branches under `tests/base/` and `tests/renovate/`. Successful runs clean up immediately. Failed-run artifacts are retained for diagnosis and deleted after 24 hours by a later run.
