# Windows Release and Startup Design

## Goal

Produce a testable Windows executable for EnglishFloating and let the user choose whether it launches automatically when they sign in to Windows.

## Release Artifact

- Publish a Windows x64, self-contained, single-file executable.
- Target path for the test release: `C:\Dev\PteFloatingSentence\release\EnglishFloating.exe`.
- The executable requires no installed .NET runtime.
- The release directory is intentionally stable because the Windows startup shortcut points to this executable.
- Packaging does not delete or overwrite user settings, Gemini API key storage, or other application data outside `release`.

## Startup Behavior

- Add a persisted `LaunchAtWindowsSignIn` setting, defaulting to `true` for new installations and legacy settings that do not contain the property.
- Add a `Launch at Windows sign-in` toggle in the Display settings page.
- When enabled, create or update one per-user shortcut in the Windows Startup folder.
- The shortcut targets the current running executable when it is a published `.exe`; during `dotnet run` or test hosts, the setting remains visible but startup registration is not created.
- When disabled, remove only the application-owned shortcut.
- No registry Run key, scheduled task, administrator privilege, or machine-wide modification is used.

## Single Instance

- The published app owns a named, per-user mutex.
- If another instance is already running, a later startup attempt exits without creating another overlay.
- The existing instance retains all current state; this first release does not add foreground-window activation IPC.

## Lifecycle and Error Handling

- Startup registration is reconciled during app startup and whenever the Display setting changes.
- Failure to create or remove the shortcut does not prevent the app from running; the app reports a concise Settings validation/status message.
- If the executable is moved after startup registration, Windows may not be able to launch it at the next sign-in. The user can run the executable from its new location once to repair the shortcut, or toggle the setting off/on.
- Disabling startup removes the shortcut even when the executable is run from a development build, provided the shortcut is identifiable as application-owned.

## Components

- `WindowsStartupRegistration`: isolated infrastructure service that resolves the per-user Startup folder and creates, validates, or removes the `.lnk` shortcut.
- `SingleInstanceGuard`: isolated disposable service wrapping the named mutex.
- `AppSettings`: persisted startup preference.
- `DisplayPage` / `SettingsWindow`: preference UI and error/status surface.
- `App`: coordinates setting persistence, startup reconciliation, and single-instance lifetime.

## Testing

- Unit tests cover startup preference defaults and legacy JSON deserialization.
- Unit tests cover shortcut path ownership, create/update/remove decision logic through an injectable filesystem/shortcut abstraction.
- App lifecycle tests cover duplicate-instance exit behavior without requiring a real second desktop app.
- UI tests cover loading and saving the Display toggle.
- Release verification runs a self-contained publish and confirms `release\EnglishFloating.exe` exists.

## Deferred

- Installer, code signing, auto-update, uninstall entry, and Start-menu integration.
- Machine-wide auto-start and multi-user support.
- Bringing an existing instance to the foreground when a duplicate launch occurs.
