# Developing Impious Bonum

Building from source, making releases, and how the code is laid out. For using the app, see the [user guide](guide.md).

## Running from source

Requires the .NET 10 SDK on Windows 10/11.

```bash
dotnet run --project src/ImpiousBonum.App -c Release
```

Tests: `dotnet test ImpiousBonum.slnx -c Release` (CI runs the same on every push to `main` and every pull request).

Code layout follows `.editorconfig`, and CI checks it with `dotnet format whitespace ImpiousBonum.slnx --verify-no-changes`. Running `dotnet format whitespace ImpiousBonum.slnx` fixes anything it finds. The build treats warnings as errors.

A build run from source shows *development build* in the tray and never checks for updates; only installed copies do.

`docs/widgets.md` is generated from the widget descriptors, and a test fails if it's out of date. After changing a widget's settings, regenerate it:

```bash
dotnet run --project src/ImpiousBonum.App -c Release -- --widget-docs docs/widgets.md
```

## Screenshots

The pictures in the README, the guide and the gallery are drawn with made-up readings, so they don't show anyone's PC:

```bash
pwsh build/screenshots.ps1
```

It draws the default layout, each gallery layout, the themes collage and the widget pictures with `ImpiousBonum.exe --snapshot <file> --layout <name> --demo`. Each widget picture in `docs/images/widgets` is drawn from a small layout of the same name in `build/screenshots`; add a layout there for a new picture (a test checks they're valid and have every reading they show). The readings are in `src/ImpiousBonum.Core/Demo/DemoReadings.cs`. The editor's screenshot is taken by hand: `ImpiousBonum.exe --data-dir <an empty folder> --demo` runs a copy with those readings next to an installed one.

It also draws `docs/images/social-preview.png` from `build/social-preview.json`: the picture shown when the repository's link is shared. Upload it by hand in the repository's **Settings → General → Social preview**.

Without a Windows PC to hand, run the **Screenshots** workflow from the Actions tab on your branch: it draws them on a Windows runner (in British English, on UK time) and commits the pictures that changed to that branch.

## Making a release

1. In [`CHANGELOG.md`](../CHANGELOG.md), rename *Unreleased* to `## x.y.z` (it should already say what's new, for people using the app): it becomes the release notes. Set `<Version>` in `Directory.Build.props` to the new version, and merge both to `main`.
2. In the repository's **Actions** tab, open **Release**, choose **Run workflow**, leave the branch on `main` and press **Run workflow**.

The [release workflow](../.github/workflows/release.yml) reads the version from `Directory.Build.props`, runs the tests, downloads the previous release so it can build a delta package, packs the installer with Velopack, tags the commit (for example `v0.4.0`) and publishes the GitHub release *Impious Bonum 0.4.0*. Installed copies pick it up on their next update check. A version number can only be released once, so the run stops straight away if that tag already exists.

The release fails before building anything if `CHANGELOG.md` has no section for the version. Its notes are set from that section after uploading, with links made absolute and an install line added (`build/release-notes.ps1`). To change a release's notes afterwards, edit the changelog on `main` and run the **Release notes** workflow from the Actions tab, with a version or `all`.

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
build/                   installer (publish.ps1), release notes, screenshots and the icon generator;
                         screenshots/ has the layouts the widget pictures are drawn from
tools/ReleaseSigning     creates the update signing key and signs releases
.github/workflows/       CI (ci.yml), releases (release.yml), release notes (release-notes.yml) and screenshots
                         (screenshots.yml)
```

Inside `src/ImpiousBonum.App`:

```
App.xaml.cs              startup, the once-a-second tick, shutdown and --snapshot
App.Tray.cs              what each tray menu item does
App.Layouts.cs           the editor, saved layouts, the gallery, game layouts, the schedule and pages
App.Screens.cs           window placement, more dashboards, dimming, the tablet view and USB screens
App.Sources.cs           the sensor service, weather, calendars, news, Claude Code and custom data
App.Automation.cs        alerts, logging, session recaps and hotkeys
App.Updates.cs           update checks and restarting into an update
App.Backup.cs            restoring a backup, which starts the app again
Widgets/                 one class per widget type, each with a WidgetDescriptor listing its settings
Layout/                  layout and theme files, the built-in themes, backgrounds and the gallery
Editor/                  the layout editor: session (undo, selection), preview, generated property panel
Shell/                   tray, dialogs, settings, monitors, hotkeys, updates and the other windows
Controls/                small WPF helpers shared by the windows (placeholders, dialog behaviour)
Styles.xaml              the shared look of the editor and dialogs, on top of WPF's Fluent theme
```
