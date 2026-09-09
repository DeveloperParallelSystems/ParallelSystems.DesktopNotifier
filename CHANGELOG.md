# Changelog

Notable changes to Parallel Systems Desktop Notifier are recorded here.

This project follows [Semantic Versioning](https://semver.org/):

- **Major** versions contain incompatible changes.
- **Minor** versions add backward-compatible functionality.
- **Patch** versions contain backward-compatible fixes.

## Unreleased

Add changes here as they are completed. Move them into a dated version section when releasing.

### Added

- A loading indicator while timesheet data is being retrieved.

### Changed

### Fixed

- Opened the Desktop Notifier window immediately instead of waiting for its API refresh.
- Made ordinary tray-icon opens select today and removed the duplicate notification-click refresh.

## 1.1.0 - 2026-09-09

### Added

- Configurable morning and afternoon notification times.
- A morning check for yesterday's manual timesheet and an unconditional afternoon reminder for today's timesheet.
- Persistent daily processing state that prevents duplicate scheduled notifications after an application restart.
- Startup catch-up checks when one or both scheduled times have already passed.

### Changed

- Queued simultaneous overdue notifications so morning and afternoon reminders are displayed separately.
- Opened the work date associated with the notification that the user clicks.
- Defaulted the work-date selection to today's date on startup.

## 1.0.0 - 2026-09-08

### Added

- Manual timesheet entry by work date.
- Project, duration, task category, client, level, and notes fields.
- Creation of clients entered manually during submission.
- Daily reminders and existing-timesheet indicators.
- Device OS version and process architecture reporting.

### Changed

- Focused the application on manual sessions without displaying Revit sessions.
