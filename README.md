<img width="200" height="200" alt="logo" src="https://github.com/user-attachments/assets/491af40a-605a-4094-bf42-f1d4f966ec92" />

# Jellycheckr

`jellycheckr` is a monorepo for an "Are You Still Watching?" feature focused on:

- Jellyfin server plugin (`apps/server-plugin`)
- Jellyfin Web client module (`apps/web-client`)
- Plugin configuration UI (`apps/config-ui`)

## Installation

1. Add `https://shtbox.io/jellycheckr/manifest.json` as a plugin source repository on your Jellyfin server.
2. Find "Jellycheckr AYSW" in the list and install it. Configuration options are available in the plugin settings.

> [!NOTE]
> This plugin relies on the Jellyfin File Transformation plugin for web clients if you want the "pretty" popup:
> https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/tree/main
> When that plugin is missing, Jellycheckr falls back to server-side-only mode.

## Repository Layout

- `apps/server-plugin` - .NET Jellyfin plugin backend
- `apps/web-client` - TypeScript web module for playback prompts
- `apps/config-ui` - Preact + Tailwind configuration UI bundled into plugin web assets
- `packages/contracts` - shared API and configuration contracts
- `docs` - architecture, API, configuration, and developer notes

## Local Build

From the repo root:

- Build the plugin:
  - `dotnet build apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj`
- Publish the plugin (`net8.0`) and create a local zip:
  - `dotnet publish apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj -c Release -f net8.0`
- Local zip output:
  - `apps/server-plugin/artifacts/Jellycheckr_0.1.0.zip`

The packaged zip includes:

- `meta.json`
- plugin assemblies
- bundled plugin `web/` assets

## Versioning

- `0.1.0` is the no-tag baseline only until the first stable semver tag exists.
- After the first stable tag, Git tags are the source of truth for releases.
- Stable package versions are `x.y.z`.
- Stable `meta.json.version` values are also `x.y.z`.
- Prerelease package versions are `x.y.z-beta.N` or `x.y.z-rc.N`.
- Prerelease `meta.json.version` values stay Jellyfin-compatible by using `x.y.z.C`, where `C` is a shared numeric prerelease counter.
- `meta.json.packageVersion` always stores the exact package version, including prerelease suffixes.

## GitHub Release Process

### Why this is tag-based

This repository uses pipeline-injected versioning and tag-based releases so that:

- there are no version bump commits
- `main` stays clean after squash merges
- tags and GitHub Releases are only created after a verified package already exists

### Branch protection and repository settings

Recommended GitHub settings for `main`:

1. Require pull requests before merging.
2. Require status checks to pass before merging.
3. Mark `PR Validation / validate` as a required check.
4. Use squash merges so the PR title is the release decision source of truth.
5. Restrict direct pushes to `main`.
6. Allow GitHub Actions to create and approve pull requests if your org policy requires it.
7. Ensure `GITHUB_TOKEN` has write permission for workflow jobs that create tags and releases.

Release and prerelease publishing rely on GitHub Actions permissions:

- `pull-requests: read` for push-triggered workflows that resolve merged PR metadata (title, labels, and body) from a squash merge commit
- `contents: write` for tag and GitHub Release creation
- `issues: write` and `pull-requests: write` for the PR Release Preview comment
- if repository policy keeps `GITHUB_TOKEN` read-only for the current context, the Release Preview workflow falls back to the job summary instead of posting a PR comment

### Conventional Commit PR titles

PR titles must match:

- `^[A-Za-z][A-Za-z0-9-]*(\([^)]+\))?(!)?: .+`

This follows the Conventional Commits structure `type(scope)!: description`:

- types are case-insensitive
- custom types are allowed
- the `: ` separator is required

Valid examples:

- `feat(ui): add setting`
- `build: initial pipeline implementation`
- `build(pipeline): Adding Pipeline`
- `fix(api): avoid null response`
- `feat!: remove legacy config`
- `docs(readme): update install notes`
- `ci(actions): tighten release permissions`

Release mapping:

- any valid Conventional Commit type is accepted
- `feat` => minor
- `fix`, `perf` => patch
- `!`, `BREAKING CHANGE:`, or `BREAKING-CHANGE:` => major
- all other valid types => no release by default unless a release override label is applied

### Release labels

Supported labels:

- `release:skip` - force no release
- `release:major` - force a major release
- `release:minor` - force a minor release
- `release:patch` - force a patch release
- `release:beta` - opt in to prerelease publishing on the PR
- `release:rc` - opt in to prerelease publishing on the PR

Precedence:

1. `release:skip` overrides everything.
2. `release:major`, `release:minor`, and `release:patch` override the title-derived bump.
3. `release:beta` and `release:rc` only affect prerelease output. They do not change the stable release that is produced after merge.

### PR workflows

#### PR Validation

Runs on every pull request and is the required merge gate.

It performs:

- PR title validation
- `dotnet restore`
- `dotnet build`
- `dotnet test`
- a `dotnet publish` dry run without a release zip

#### Release Preview

Runs on pull requests targeting `main`.

It shows:

- computed release type (`major`, `minor`, `patch`, or `none`)
- next stable version
- next stable manifest version
- whether merge would produce a stable release
- the exact manifest-only GitHub Release body preview
- the next prerelease candidate if a prerelease label is present
- changelog preview since the last stable tag

It updates a single PR comment and also writes the same preview into the workflow summary.

How to read the preview:

- `Next stable version` is the Git tag and zip version that will be used on merge.
- `Next stable manifest version` is the value that will go into `meta.json.version`.
- `Opt-in prerelease` shows the exact prerelease zip version and the numeric Jellyfin manifest version.
- `Release Body Preview` shows the exact text that the GitHub Release body will contain.

### Stable release flow

Stable releases only happen after a merge to `main`.

#### Stage A: Build and Verify

`Enterprise Release - Stage A`:

- computes the next stable version
- restores, builds, tests, and publishes the plugin
- validates the produced zip and `meta.json`
- generates `release-notes.md`
- uploads a release bundle artifact

Stage A does not:

- create a tag
- create a GitHub Release

If the release type resolves to `none`, Stage A exits successfully and skips publishing.

#### Stage B: Release and Publish

`Enterprise Release - Stage B` runs only after Stage A succeeds and only when a release is required.

It:

- downloads the verified bundle from Stage A
- verifies the bundle contents again
- creates or reuses the stable tag
- creates or updates the GitHub Release
- uploads the plugin zip asset
- uploads a `<zip-name>.md5` sidecar checksum asset for the zip
- uploads `release-notes.md` as a separate asset

Stable artifacts appear in two places:

- Stage A workflow artifact: internal verified bundle
- GitHub Release assets: final published zip, `<zip-name>.md5`, and `release-notes.md`

### Prerelease flow

You can publish a prerelease in two ways:

1. Add `release:beta` or `release:rc` to a PR targeting `main`.
2. Run the `Prerelease Build` workflow manually with `workflow_dispatch`.

Behavior:

- PRs in the main repository can build and publish prereleases.
- Fork PRs can build and validate a prerelease package, but publish is skipped.
- Non-`main` branch pushes run a preview-only prerelease build using `beta` numbering and never publish.
- Stable releases still happen only after merge to `main`.

Prerelease artifacts appear in:

- the `Prerelease Build` workflow artifact (when publish is allowed)
- the GitHub prerelease assets, including the zip, `<zip-name>.md5`, and `release-notes.md`

### GitHub Release body format

The GitHub Release body is intentionally not a changelog.

It contains exactly one fenced `jellycheckr-manifest` block and nothing else:

```jellycheckr-manifest
{
  "version": "<computed manifest version>",
  "targetAbi": "<computed targetAbi>",
  "dependencies": []
}
```

Rules:

- `version` is the actual Jellyfin manifest version from the packaged build
- `targetAbi` is the actual packaged target ABI
- `dependencies` is currently always `[]`
- the changelog is shipped as `release-notes.md`, not in the Release body
- the zip checksum is shipped as a sidecar `<zip-name>.md5` asset containing the MD5 hash and original zip filename

## Troubleshooting

### Invalid PR title

- Symptom: `PR Validation / validate` fails immediately.
- Fix: rename the PR title to `type(scope)!: description` and keep the required `: ` separator.

### No release produced

- Symptom: Release Preview shows `none`, or Stage A exits successfully without publishing.
- Causes:
  - title maps to a no-release type (`docs`, `chore`, `ci`, `build`, `test`, `refactor`)
  - `release:skip` is present
- Fix: change the PR title or apply an explicit bump label.

### Prerelease requested but nothing published

- Symptom: prerelease workflow runs but does not publish.
- Causes:
  - no release bump resolved
  - running from a fork PR
  - running from a non-`main` branch push preview
- Fix: use a releasable title or explicit bump label, and publish from a trusted context.

### Version or tag collision

- Symptom: Stage A or publish steps fail because a tag already exists.
- Fix: inspect the existing tag. If it points to a different SHA, a manual version decision is required before rerunning.

### Missing semver tags

- Symptom: first automated release behaves as though there was no previous release.
- Expected behavior: the workflow ignores non-semver tags (including `initial`) and uses `0.1.0` as the baseline.

### Packaging validation failure

- Symptom: Stage A or prerelease build fails in `Validate-PluginPackage.ps1`.
- Common causes:
  - zip name does not match the computed version
  - `meta.json.version` mismatch
  - `meta.json.packageVersion` mismatch
  - `meta.json.targetAbi` mismatch
  - `meta.json.dependencies` is not `[]`
  - assembly version mismatch

### Release body mismatch

- Symptom: publish step fails before updating the GitHub Release.
- Cause: the generated body did not match the required manifest-only fenced block format.
- Fix: inspect the publish script and ensure the body contains only the exact manifest block.

### Missing Actions write permissions

- Symptom: tags, releases, or the Release Preview PR comment fail to update.
- Fix: confirm the workflow has:
  - `pull-requests: read` for push-triggered release context resolution
  - `contents: write` for release jobs
  - `issues: write` and `pull-requests: write` for the Release Preview workflow
  - repository `GITHUB_TOKEN` workflow permissions set to allow write access where comments/releases are expected

## Compatibility

This implementation is designed for current Jellyfin server/plugin patterns and Jellyfin Web.

## Debugging Mode

### HUD Debug Display

<img width="1971" height="1109" alt="image" src="https://github.com/user-attachments/assets/e3514841-02eb-4cd4-8269-0debb6aa603c" />

### Configuration 
<img width="1289" height="1300" alt="image" src="https://github.com/user-attachments/assets/bcbce084-8062-4727-9848-9d9147dd9540" />


