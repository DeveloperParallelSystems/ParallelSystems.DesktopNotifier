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
- A notification settings dialog for changing the morning and evening reminder times.
- Per-user persistence for notification schedule changes.

### Changed

- Renamed the timesheet duration field to hours and displayed values as `HH:mm` without seconds or decimal values.
- Replaced user-facing manual-timesheet wording with timesheet wording.
- Replaced the notification-settings and refresh labels with compact icon buttons.
- Simplified the daily summary by placing remaining hours beneath the day target.
- Enlarged and restyled the work-date calendar with clearer date states and month navigation indicators.
- Increased and vertically centered the selected work-date text for better use of the date field.

### Fixed

- Prevented afternoon times from being saved as the morning schedule and morning times from being saved as the evening schedule.
- Kept `AfternoonNotificationTime` as the sole configuration key while presenting it as the evening reminder in the UI.
- Opened the Desktop Notifier window immediately instead of waiting for its API refresh.
- Made ordinary tray-icon opens select today and removed the duplicate notification-click refresh.

## 1.1.0 - 2026-09-09

### Added

- Configurable morning and evening notification times.
- A morning check for yesterday's timesheet and an unconditional evening reminder for today's timesheet.
- Persistent daily processing state that prevents duplicate scheduled notifications after an application restart.
- Startup catch-up checks when one or both scheduled times have already passed.

### Changed

- Queued simultaneous overdue notifications so morning and evening reminders are displayed separately.
- Opened the work date associated with the notification that the user clicks.
- Defaulted the work-date selection to today's date on startup.

## 1.0.0 - 2026-09-08

### Added

- Timesheet entry by work date.
- Project, hours, task category, client, level, and notes fields.
- Creation of newly entered clients during submission.
- Daily reminders and existing-timesheet indicators.
- Device OS version and process architecture reporting.

### Changed

- Focused the application on timesheet sessions without displaying Revit sessions.
