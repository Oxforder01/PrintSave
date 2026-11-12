# PrintSave

Saves a copy of every printed document on this PC, converted to PDF, organized into
`C:\PrintSave\{year}\{month}\{day}\` with a searchable index of metadata (document name,
user, program, printer, time).

- Runs quietly in the background (tray-present, not hidden).
- Targets **.NET Framework 4.8** so one build covers Windows 7, 8.1, 10 and 11.
- Capture is done through the Windows print spooler, not by hooking into applications.

## Download & install

Grab `PrintSave.msi` from the [latest release](../../releases/latest), double-click it, and
approve the single UAC prompt. It installs a background service (the capturer), a tray app and
a command-line tool; uninstall from **Settings → Apps** (your saved PDFs stay put).

The MSI is Authenticode-signed with a self-signed certificate, so Windows shows an
"unknown publisher" prompt — expected until it's signed with a CA-issued certificate.

> Not on the Microsoft Store: Store policy rejects apps that need elevation for any part of
> their functionality, and PrintSave relies on a LocalSystem service to read the print spooler.

## The "original format" reality

By the time a document reaches the spooler the source file (`.docx`, `.jpg`, ...) is gone —
the app has rendered it into a print language. What the spooler holds depends on the driver:

| Spool format | Typical source | PrintSave output |
|---|---|---|
| EMF   | Most classic GDI drivers | Convert to PDF |
| XPS   | XPS / class drivers, Win 11 | Convert to PDF |
| PDF   | Protected Print Mode, some IPP | Already PDF |
| PostScript | PS drivers | Convert to PDF (Ghostscript) |
| PCL / vendor RAW | Some network/vendor drivers | Partial / not reliably recoverable |

So the product saves a **PDF** per the chosen setting. **Phase 0 exists to find out which of
these formats _your_ printers actually produce**, because that decides most of the later work.

## Status

Capture, PDF conversion (XPS vector + EMF raster), the Windows service, the metadata index,
the tray UI and a one-click MSI installer all work.

## Build phases

0. ✅ **Spike** — proved capture works and reported the spool format (HP ENVY → XPS).
1. ✅ **Capture engine** — `PrintSave.Core`, date folders (`2026\November\12\`).
2. ✅ **Metadata + SQLite index** — `PrintSave.Index`, `{root}\.state\index.db` (FTS5 search).
3. 🟡 **Conversion to PDF** — XPS (vector, selectable text) and EMF (raster, 300 DPI) done in
   `PrintSave.Conversion`; a `CompositeConverter` dispatches by detected format. PostScript not
   done — it needs Ghostscript (AGPL), which conflicts with the clean/Store licensing goal, so
   it's deferred. EMF covers the common GDI-driver case; raw PCL remains save-as-spool.
4. ✅ **Resilience** — persistent dedupe, flag re-apply, logging, and a low-disk-space warning:
   the service checks the archive drive every 5 min and, below the threshold, writes a warning
   to the log and Event Log; the tray shows a balloon + a red menu line. Default 5 GB
   (`WarnFreeSpaceGB` in `config.json`, 0 disables). Nothing is ever auto-deleted.
5. ✅ **Service** — `PrintSave.Service` (LocalSystem, auto-start, recovery, Event Log).
6. ✅ **Tray + settings UI** — `PrintSave.Tray`: status menu, index search (open files), settings (folder-security toggle, start-at-sign-in).
7. ✅ **Installer** — `installer\PrintSave.wxs` (WiX v5): one UAC prompt installs the service,
   tray and console to `Program Files`, starts the service, adds a Start-menu shortcut and an
   all-users **Startup** shortcut (the tray appears at sign-in), and launches the tray right
   away. Clean uninstall stops/removes the service, force-closes the tray and removes the
   install folder; saved PDFs are left in place. Still to do: test matrix across Windows
   versions/printers.

   The tray's autostart uses an all-users Startup shortcut rather than a per-user `Run`
   registry value, because a per-machine installer writes `HKCU` to the wrong hive. The
   Settings "start at sign-in" checkbox remains a per-user override. (Note: the service — which
   does the actual capturing — autostarts independently and survives reboots regardless.)

## Installer

Build `artifacts\PrintSave.msi` (no elevation needed to build):

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

It publishes the three executables into one staging folder and compiles the WiX package.
Needs the WiX **v5** global tool (`dotnet tool install --global wix --version 5.0.2`); the
script adds the Util + UI extensions itself. WiX v7 is avoided on purpose — it is gated behind
a paid maintenance-fee EULA, while v5 stays MS-RL / free.

Then double-click `PrintSave.msi` (one UAC prompt) to install, or
`Settings > Apps` / `msiexec /x PrintSave.msi` to remove.

## Index

Every captured document gets a row: document name, user, machine, printer, source/saved
format, pages, print time, size. On each start the service reconciles the index with the
folders: files saved before the index existed are added (date/time and name come from the
path), and rows for files that were deleted are removed.

Search from a terminal (no elevation needed to read your own archive):

```
PrintSave.ConsoleHost.exe --search "invoice smith"   # every word matches as a prefix
PrintSave.ConsoleHost.exe --list                     # newest 50
PrintSave.ConsoleHost.exe --reindex                  # rescan folders now
PrintSave.ConsoleHost.exe --stats
PrintSave.ConsoleHost.exe --settings                 # show settings (archive root, folder security)
PrintSave.ConsoleHost.exe --secure on                # lock archive to you + SYSTEM + Admins (admin)
PrintSave.ConsoleHost.exe --secure off               # restore easiest access (default) (admin)
```

Add `--root <folder>` to point at a different archive.

## License

MIT — see [LICENSE](LICENSE). Third-party components bundled with the installer are listed,
with their own licenses, in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
