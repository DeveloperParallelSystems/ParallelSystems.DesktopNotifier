# Hosted notifier UI

The Windows process owns `NotifierService`, machine registration, tray notifications and updater shutdown. `MainWindow` embeds the independently deployed Timesheet route `/desktop-notifier`. Timesheet opens by default. Employees linked to an active administrator account can switch to the compact dashboard without signing in. The hosted page makes ordinary API requests directly with browser `fetch`; the native bridge supplies machine/device context and handles local desktop services. Catalog reads and writes remain native-bridged because the desktop host rechecks the machine-mapped employee's current catalog permission before each privileged request.

Dashboard API requests also originate in the browser and carry the scoped `X-Parallel-Dashboard` header. The API resolves the machine's employee and active linked administrator account on every request, requiring employee access to remain enabled. This uses the existing desktop machine identity trust model. It creates no website session and cannot access user-management or other unrelated administrator endpoints. The server-side dashboard allowlist remains authoritative. Deploy the updated API and `sp_DesktopGetDashboardUser` procedure with the wrapper and website.

## Configuration and deployment

`WebAppUrl` in `notifier.settings.json` defaults to `/desktop-notifier` on the resolved `ApiBaseUrl`. Override it when the website has a different origin. Production requires HTTPS. HTTP is permitted only for loopback development, for example `http://localhost:5173/desktop-notifier`. The website bundle uses `VITE_API_BASE_URL` (default `/api`) for browser API requests. The native bridge provides machine name, registered device ID and product version; the browser does not infer a machine identity from URL parameters.

1. Build and deploy `ParallelSystems.Web.Timesheet` first. Verify a direct request to `/desktop-notifier` returns the SPA entry point. The existing server's `MapFallbackToFile` supports this when its website assets are installed.
   A successful HTTP response alone is insufficient: open that route in an ordinary browser and verify it says to open Desktop Notifier. If it shows login or the dashboard, the server is still serving the older website bundle. The wrapper keeps the page hidden until its native handshake completes and reports redirects through its recovery screen.
2. Keep bridge version 1 supported when deploying subsequent website releases. Incompatible clients receive an update-required message. Do not remove support until installed wrappers have migrated.
3. Build the Windows product in Release and use `Installer/Build-Installer.ps1`. It downloads and verifies Microsoft's signed Evergreen bootstrapper and bundles it in the installer. Missing runtimes are installed per user before application files are installed. Internet access is needed for that first runtime installation; managed machines can provision Evergreen in advance.
4. Existing updater deployments that use a direct ZIP package must provision WebView2 first or distribute the installer; the app reports missing runtimes and keeps the tray/background services alive.
5. Retain the previous website deployment and desktop installer for rollback. The dashboard adds `sp_DesktopGetDashboardUser`, installed by the API's database initializer. Roll back the website to a bridge-v1-compatible build before reverting the wrapper.

The browser profile is `%LOCALAPPDATA%\Parallel Systems\Desktop Notifier\WebView2`, outside versioned installations. `webview.log` alongside it records failure categories, not URLs, messages, or personal-information payloads. Existing reminder settings/state paths are unchanged. There is no offline UI bundle or offline submission queue.

The notifier reads `TimesheetDesktop.PersistentPromptEntryEnabled` and `TimesheetDesktop.PersistentPromptEntryTimeStart` from the server API. When enabled, at the configured start hour in local computer time, the native background service checks whether timesheet notifications are enabled for the employee and whether today's timesheet has any recorded time. If notifications are enabled and no time is recorded, it opens a persistent bottom-right WebView prompt. The page loads prompt data and submits timesheets directly to the API, prefilled with entries from the most recent prior workday within the past year. A single suggested entry starts expanded; multiple entries start collapsed. Continuing in the main window carries the suggestions there as new, unsaved entries. The prompt remains visible until submission or handoff. An absent employee preference is treated as disabled; deploy the updated API and `sp_DesktopGetPersonalInformation` with the desktop application.

## API ownership

The hosted notifier owns its user-initiated HTTP calls. These use the browser API module and include the native machine/device context where the desktop API requires it. This includes daily-work options, saved-session reads, calendar dates, timesheet submission, personal-information reads and updates, and compact-dashboard requests.

The local application retains these API calls:

- `POST /api/v2/desktop/daily-work/devices/ensure` registers the computer and provides the device ID shared with the hosted page.
- `GET /api/v2/desktop/daily-work/status` checks missing-time alerts and reminder eligibility.
- `GET /api/v2/desktop/daily-work/settings` reads the server-controlled persistent-prompt schedule.
- `GET /api/v2/desktop/daily-work/personal-information?machineName=...` checks notification preferences and current administrator status for background notification behavior. The hosted page separately uses browser requests for its profile UI.
- `GET /api/projects`, `GET /api/levels`, `GET /api/package-names`, and `GET /api/tasks`, plus their catalog `POST`/`PUT` operations, remain native-bridged so the host can recheck `CanManageCatalogs` before every read or write. Do not move these catalog calls to browser fetch unless the API server enforces the same permission check itself.

Legacy WPF view-model and dialog classes remain for regression fixtures; the production application hosts the WebView UI and does not use those legacy views.

## Bridge version 1

`AdminDisableInAndOutNotifications` defaults to false. Linked administrators can change it in Settings > Notifications. The preference is loaded from `notifier.settings.json`, with an optional per-user override in `notification-settings.json`. Each reminder check verifies the current employee's `IsAdministrator` through the API before suppressing either daily Windows notification; non-administrators and failed role lookups keep reminders enabled. The 1 p.m. entry prompt and missing-timesheet banner are unaffected. Deploy the Timesheet settings UI alongside the desktop update. Bridge snapshots expose `adminDisableInAndOutNotifications`; `saveSchedule` accepts that optional boolean and preserves the existing preference when omitted by older pages.

Requests: `{ version: 1, id, operation, payload }`. Responses contain the same `version` and `id`, plus `result` or `error: { code, message }`. Events use `{ version: 1, event, payload }`.

Operations are allowlisted: `initialize`, `ready`, `continuePrompt`, `recordSubmission`, `catalog`, `saveCatalog`, `saveSchedule`, `dismissAlert`, `checkUpdates`, `openUpdater`, `clientState`, `freezeAck`, and `navigationHandled`. `catalog` and `saveCatalog` are the only user-facing API operations still executed by native code; the host checks current catalog permission before forwarding them. `continuePrompt` transfers suggested session fields to the main timesheet as unsaved entries; the main-window `navigate` event carries the work date and suggestions. After a successful browser submission from the prompt, `recordSubmission` updates local reminder state and causes the main window to reload the date. See `DesktopBridge` and `src/features/notifier/bridge.js` for payloads. Browser submission uses the registered device and converts the local work date at 09:00 to UTC using the existing convention.

The bridge accepts only the configured origin, port and exact notifier path, with no query or fragment. Frames, downloads, new windows, browser accelerators, and unrelated navigation are blocked. Production developer tools and host objects are disabled. Every privileged catalog read/write rechecks the machine-mapped employee's current permission.

The page freezes controls synchronously before acknowledging `prepareUpdate`. Native code requires a clean, idle acknowledgment with the current nonce within three seconds, then checks its own operation leases before returning `ready`. Unsaved forms, pending calls, a disconnected renderer, or no acknowledgment return `busy`. `abortUpdate` restores editing. Never retry a timed-out write automatically: reconcile the saved state first.

## Validation

- `dotnet test ../ParallelSystems.Test/ParallelSystems.DesktopNotifier/ParallelSystems.DesktopNotifier.Tests.csproj -c Release`
- `dotnet run --project ../ParallelSystems.Test/DesktopNotifier.RoleSettings/DesktopNotifier.RoleSettings.csproj -c Release`
- `dotnet test ../ParallelSystems.Updater/tests/ParallelSystems.Updater.WindowsTests/ParallelSystems.Updater.WindowsTests.csproj -c Release --filter FullyQualifiedName~NotifierShutdownTests`
- In the website: `npm run build`, `node --test --test-isolation=none tests/*.test.mjs`, and `node tests/notifier.browser.mjs` with Playwright installed (or `PLAYWRIGHT_MODULE` set to a playwright-core file URL).
- With the built website served on localhost: `dotnet run --project ../ParallelSystems.Test/DesktopNotifier.WebHost/DesktopNotifier.WebHost.csproj -c Release -- http://localhost:5173/desktop-notifier <unique-artifact-directory>`. This offscreen harness uses the real WPF/WebView2 host with fake API responses and an isolated browser profile, testing the handshake, native identity, update freeze/abort, and hidden-window reminders without starting production services.

Before rollout, test the installer on clean Windows accounts with and without Evergreen, plus offline/proxy installation failure. Verify startup registration, close-to-tray, a second `--show-main` launch, notification navigation, API/website outage recovery, and prepare/abort/commit through the real updater. The automated browser suite uses a simulated native transport; it does not substitute for the Windows host smoke check.

The hosted page remains the main timesheet frontend. The 1 p.m. entry prompt uses the same WebView2 host and hosted route, with `manualEntryPrompt` mode supplied by a separate native bridge. Only loading/recovery remains WPF. Deploy the website with prompt support before this wrapper; older pages fail the explicit prompt handshake and show recovery. The prompt bridge allows only prediction loading, submission for its native work date, and continuation in the main window. Yesterday’s session IDs are discarded. Updates wait while the reminder is open, and recovery offers Retry or Continue in main window. Legacy WPF view-model/dialog code remains for existing regression fixtures.
