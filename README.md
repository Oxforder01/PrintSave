# PrintSave

Saves a copy of every printed document on this PC, converted to PDF, organized into
`C:\PrintSave\{year}\{month}\{day}\` with a searchable index of metadata (document name,
user, program, printer, time).

- Runs quietly in the background (tray-present so it stays honest and Microsoft Store–eligible later).
- Targets **.NET Framework 4.8** so one build covers Windows 7, 8.1, 10 and 11.
- Capture is done through the Windows print spooler, not by hooking into applications.

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

Phase 0 — capture proof-of-concept. See [`phase0-spike/`](phase0-spike/).

## Build phases

0. **Spike** — console app proves capture works and reports the spool format. ← you are here
1. Service — auto-start background capture into date folders.
2. Metadata + SQLite index.
3. Conversion to PDF (EMF/XPS/PS).
4. Resilience — persistent queue, re-apply printer flags, disk limits, logging.
5. Tray + settings UI over a named pipe.
6. Installer (one UAC prompt), service recovery, clean uninstall.
7. Test matrix across Windows versions and printer types.
