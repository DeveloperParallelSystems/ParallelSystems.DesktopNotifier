# Desktop Notifier installer

The installer performs a per-user installation and does not require administrator privileges. It:

- installs the self-contained Windows x64 application under `%LOCALAPPDATA%\Programs`;
- adds a Start menu shortcut;
- starts Desktop Notifier after an interactive installation;
- registers Desktop Notifier to run when the current user signs in; and
- removes the startup registration when the application is uninstalled.

The installer bundles Microsoft's signed Evergreen WebView2 bootstrapper and installs
the runtime per user when missing. The first runtime installation requires internet
access. Build-Installer downloads and verifies that bootstrapper before compiling
the installer. Deploy the Timesheet `/desktop-notifier` route before distributing
the wrapper. See [HOSTED-UI.md](../HOSTED-UI.md) for configuration and rollout checks.

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then build the installer from PowerShell:

```powershell
.\Installer\Build-Installer.ps1
```

The setup executable is written to `artifacts\installer`.
