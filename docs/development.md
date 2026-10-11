# Developing Impious Bonum

Building from source, making releases, and how the code is laid out. For using the app, see the [user guide](guide.md).

## Running from source

Requires the .NET 10 SDK on Windows 10/11.

```bash
dotnet run --project src/ImpiousBonum.App -c Release
```

Tests: `dotnet test ImpiousBonum.slnx -c Release` (CI runs the same on every push to `main` and every pull request).

A build run from source shows *development build* in the tray and never checks for updates; only installed copies do.

`docs/widgets.md` is generated from the widget descriptors, and a test fails if it's out of date. After changing a widget's settings, regenerate it:

```bash
dotnet run --project src/ImpiousBonum.App -c Release -- --widget-docs docs/widgets.md
```

## Making a release

1. Set `<Version>` in `Directory.Build.props` to the new version and merge that to `main`.
2. In the repository's **Actions** tab, open **Release**, choose **Run workflow**, leave the branch on `main` and press **Run workflow**.

The [release workflow](../.github/workflows/release.yml) reads the version from `Directory.Build.props`, runs the tests, downloads the previous release so it can build a delta package, packs the installer with Velopack, tags the commit (for example `v0.4.0`) and publishes the GitHub release *Impious Bonum 0.4.0*. Installed copies pick it up on their next update check. A version number can only be released once, so the run stops straight away if that tag already exists.

Pushing a tag yourself (`git tag v0.4.0 <commit>` then `git push origin v0.4.0`) still works too; then the tag decides the version, so keep it in step with `Directory.Build.props`.

To build the installer locally instead: `pwsh build/publish.ps1 -Version 0.2.1` (output in `artifacts/releases`; add `-SkipTests` to skip the test run).

## Update signing

Installed copies only take a release whose full package is signed with the project's update key. The public half is `assets/update-signing-key.pub`, built into the app; the private half is the `UPDATE_SIGNING_KEY` secret of the `release` environment, and the release workflow uploads `ImpiousBonum-<version>-signature.txt` with each release. This is separate from Authenticode code signing, which the installer doesn't have yet.

One-time setup:

1. Create the key (on your own machine, not in CI):

   ```bash
   dotnet run --project tools/ReleaseSigning -- new-key assets/update-signing-key.pub update-signing-key.private.txt
   ```

2. In the repository settings, under Environments, create `release`, add yourself as a required reviewer, and add the contents of `update-signing-key.private.txt` as the secret `UPDATE_SIGNING_KEY`.
3. Keep a copy of the private key offline, then delete the file. If the key is lost, installed copies can't take updates until they are reinstalled by hand.
4. Commit `assets/update-signing-key.pub`.

Releases then pause for your approval, and fail rather than publish if the secret is missing or doesn't match the public key.

## Layout of the code

```
src/ImpiousBonum.Core    metric store, formatting/templates, providers, sampler (no UI)
src/ImpiousBonum.App     WPF dashboard, widgets, editor, tray, monitor placement, updates
src/ImpiousBonum.Sensors elevated sensor service (LibreHardwareMonitor, ETW frame counting) serving the named pipe
tests/                   unit tests for the core and the app (widgets, layout validation, editor)
docs/widgets.md          widget reference, generated from the widget descriptors
build/                   installer build (publish.ps1) and icon generator
tools/ReleaseSigning     creates the update signing key and signs releases
.github/workflows/       CI (ci.yml) and releases (release.yml)
```
