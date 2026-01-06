# renovate-presets
Presets for Renovate

## Usage

To use these presets in your Renovate configuration, add them to the `extends` array in your `renovate.json` file:

### Default Configuration

This preset includes recommended settings, non-office hours scheduling, semantic commits, vulnerability alerts, and custom regex managers for .NET, Docker, and GitHub releases.

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

- **`default`** (`default.json`): Meziantou's default Renovate configuration with recommended settings, custom regex managers for NuGet, Docker images, and GitHub releases
- **`default-automerge`** (`default-automerge.json`): Enables automatic merging of Renovate pull requests
