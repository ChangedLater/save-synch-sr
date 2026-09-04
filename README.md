# StarRupture Save Sync

A self-contained Windows (WPF) app that shares a single StarRupture save between a
group of friends, using a **git repository as the backing store** (via LibGit2Sharp —
no git CLI required).

## How it works

- A working clone of your shared repo lives in
  `%LOCALAPPDATA%\StarRuptureSync\repo` — never inside the Steam save folder.
- On first run the repo is cloned. If the remote was just created and has never
  been pushed to (no commits, no `main`), the app seeds it with an initial commit
  and pushes `main` for you.
- Each **session** (the name you gave the game in-game) is a folder in the repo
  containing its save slots (`0.sav` / `0.met`, `AutoSave0.sav` / `AutoSave0.met`, …).
- Local vs. repo versions are compared by **SHA-256 file hash**.
- Your Steam save folder is only touched when you press **Download**.
- The main window shows whether StarRupture is running (re-checked every 30 s, plus a
  **Check now** button). While it is running, Upload and Download are disabled.
- Local saves are backed up to `%LOCALAPPDATA%\StarRuptureSync\backups\<session>\<timestamp>`
  before every download (backups are not committed to git).

## First run

You are asked for:

| Field | Notes |
|-------|-------|
| Username | Recorded as the git commit author so the group can see who uploaded a version. |
| Git repository URL | HTTPS URL of the shared repo. Synchronisation always uses the `main` branch. |
| Git personal access token | Stored locally, DPAPI-encrypted (`CurrentUser`). Leave blank on later runs to keep the stored one. |
| SaveGames folder | Auto-detected, or Browse. |

### Save-folder auto-detection order

1. Steam path from registry `HKCU\Software\Valve\Steam\SteamPath`
2. `[SteamPath]\userdata\[SteamID]\1631270\remote\Saved\SaveGames\`
   (if several Steam IDs have it, you are asked to pick)
3. `%LOCALAPPDATA%\StarRupture\Saved\SaveGames\`

## Synchronising

1. **Refresh** — `fetch`, then `reset --hard origin/main`, then compare every
   session (repo + local) by hash.
2. Select a session. Status is one of:
   - *Up to date*
   - *A newer version is available to download*
   - *Your local save is newer – upload to share it*
   - *No local copy – create the session in-game first* (shows step-by-step instructions)
   - *Local only – not uploaded yet*

   The detail pane shows a one-line file summary (e.g. "All 4 files identical",
   "remote has 2 newer files  •  2 identical"). **View file details…** opens a window
   with the per-file status, local last-write time, and the time of the last commit
   that changed each file on the remote.
3. **Download** (repo → Steam): verifies StarRupture is not running, backs up your
   current local save, then copies the repo files in. If your local copy looks
   *newer* than the remote (or both changed), a warning dialog asks you to confirm
   before it is overwritten.
4. **Upload** (Steam → repo): replaces the repo's copy of the session with your local
   files, commits as you, and pushes.
   - If the push is rejected because `origin/main` advanced, the app re-fetches,
     shows **who** pushed and **when** (from the commit metadata) and its message, and
     makes you choose:
     - *Discard my upload and re-pull their version*, or
     - *Overwrite their version with mine* (force push).

## Build

```bash
dotnet build StarRuptureSync/StarRuptureSync.csproj -c Release
```

## Publish a self-contained single-file exe

```bash
dotnet publish StarRuptureSync/StarRuptureSync.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:_IsPublishing=true -o publish
```

Output: `publish/StarRuptureSync.exe` (no .NET runtime install required on the target machine).

## Requirements

- .NET 10 SDK to build. The published exe needs nothing pre-installed.
