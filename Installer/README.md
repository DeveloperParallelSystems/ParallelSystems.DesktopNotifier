# Desktop Notifier installer

The installer performs a per-user installation and does not require administrator privileges. It:

- installs the self-contained Windows x64 application under `%LOCALAPPDATA%\Programs`;
- adds a Start menu shortcut;
- starts Desktop Notifier after an interactive installation;
- registers Desktop Notifier to run when the current user signs in; and
- removes the startup registration when the application is uninstalled.

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then build the installer from PowerShell:

```powershell
.\Installer\Build-Installer.ps1
```

The setup executable is written to `artifacts\installer`.
