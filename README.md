# GanjoorPdfToGdb (spike)

Converts a scanned Persian poetry book (PDF) into

* a Desktop Ganjoor database (`<poet>.gdb`) – same four tables as `DbBrowser.CreateEmptyDB`,
* a JSON sidecar (`<poet>.pages.json`) linking every poem and verse to its **1-based PDF page index**,
* a report (`<poet>.report.txt`) listing everything that deserves a human look.

Target: **.NET 10**, cross-platform (Windows / macOS / Linux). It does not depend on the WinForms desktop code.

## Projects

| project | what |
|---|---|
| `GanjoorPdfToGdb.Core` | all the logic (model client, page cache, assembler, GDB + JSON writers, `ConversionJob`) |
| `GanjoorPdfToGdb.Cli` | `pdf2gdb` command line tool (cross-platform) |
| `GanjoorPdfToGdb.WinForms` | **Pdf2GdbStudio**, a Windows Forms window around the same `ConversionJob` (Windows only) |

```
dotnet build -c Release
```

PDF pages are rendered with poppler's `pdftoppm`/`pdfinfo`. They are looked up in this order:
the poppler folder given in the options, a folder named `poppler` next to the program (anywhere below it,
e.g. `poppler\Library\bin`), then PATH (`brew install poppler` on macOS). Alternatively pass a folder of
already rendered page images with `--images-dir`.

## Windows app (Pdf2GdbStudio)

1. Unzip a Windows build of poppler and put the whole folder in a folder named `poppler` next to
   `Pdf2GdbStudio.exe`. (Or put it in `src\GanjoorPdfToGdb.WinForms\poppler` before building; it is copied
   to the output automatically.) poppler is GPL software: check that redistributing it suits you.
2. Start the app, drop a PDF onto the window (or use *انتخاب*), type the poet name, the service address,
   model and API key, and press **شروع تبدیل**.
3. **آزمایش** converts only the first 3 pages of the range (written to a separate "(آزمایش)" file): a cheap
   way to check the key, the model and the output before the full run.
   **ساخت دوباره بدون اتصال** rebuilds the output from the cached pages without any API calls.
   **توقف** stops cleanly; the next start continues from the cache.
4. Errors open in a window with selectable text (copy button), not a disappearing message.
5. Settings are remembered in `%AppData%\GanjoorPdfToGdb`. The API key is saved there only if you tick the box
   (stored as plain text in your user profile).

Page ranges accept Persian digits and «همه» (e.g. `۷۴-۱۱۳۰`).

## Run

```
export GDB_API_KEY=...            # Windows: setx GDB_API_KEY ...
dotnet run --project src/GanjoorPdfToGdb.Cli -- run \
    --pdf soha.pdf --poet "طالب آملی" --out out \
    --base-url https://api.avalai.ir/v1 --model claude-sonnet-5-5 \
    --pages 74-1130
```

Try a few pages first (`--pages 600-601`), look at `out/work/pages/0600.json` and the report, then go wide.
`--config appsettings.sample.json` also works; it accepts your existing key names
(`OpenAIBaseUrl`, `OpenAIAPIKey`, `OpenAITitleModel`). Environment variables override the file.

Every model answer is cached in `out/work/pages/NNNN.json`: an interrupted run resumes where it stopped, and

```
dotnet run --project src/GanjoorPdfToGdb.Cli -- assemble --pdf soha.pdf --poet "..." --out out
```

rebuilds the GDB/JSON from the cache with no API calls (use it after changing title templates etc.).
Delete a page's cache file to have the model re-read that page.

## How it works

1. Each page image goes to the model with a fixed JSON contract (`Llm/PagePrompts.cs`): page kind
   (poetry / prose / contents / index / title / blank), running header, and ordered blocks: category and
   poem headings, one `verse_row` per printed line (`two_column` / `centered` / `single`), footnotes,
   margin verse counters.
2. Pages are processed **in order**; the previous page's state (current section, open poem, last rows) is
   sent along, so poems that continue across pages work without a heading.
3. `BookAssembler` (deterministic) builds categories → poems → rows, drops non-poetry pages and footnotes,
   normalizes text (ی/ک, Persian digits, tatweel, footnote markers, ۀ → هٔ) and checks the margin verse
   counters (a centered pair counts as one bayt) to detect missing/duplicated lines.
4. `PoemBuilder` turns rows into verses. A centered pair whose second line ends with the poem's rhyme is
   stored as an ordinary Right+Left bayt (typographic centering); otherwise it stays CenteredVerse1/2.
   Every such decision is flagged.

## Flags (in the sidecar and the report)

| flag | meaning |
|---|---|
| `centered-treated-as-normal` | centered couplet ends with the poem's rhyme → stored as Right+Left |
| `centered-treated-as-normal-couplet-rhyme` | masnavi: the two centered lines rhyme with each other, like the other bayts → Right+Left |
| `centered-kept` | centered couplet does not rhyme → stored as CenteredVerse1/2 |
| `centered-unverified` | not enough rhyme evidence either way → kept centered, please check |
| `centered-odd-row` | an odd number of centered lines in a row |
| `rhyme-outlier` | a second hemistich that does not end with the poem's (otherwise clear) rhyme: proofread it |
| `counter-jump` | margin verse counters disagree with the bayts counted, and the next number confirms it: lines missing/duplicated, or numbering restarts |
| `counter-misread` | a single margin number is off but its neighbours fit: a misprint or misread digit, the text is probably fine |
| `unsure-word:<word>` | the model named this specific word as uncertain: check it |
| `model-uncertain` | (older cached pages) imprecise flag; kept on the verse only, not in the report |
| `footnote-marker-removed` | a marker like (۱) was stripped from the verse |
| `no-heading` | verses found with no poem heading |
| `duplicate-title-renamed` | repeated title (e.g. «قطعه») got a counter |
| `page-missing` | a page failed and the poem may be incomplete |

## Sidecar JSON

`schema`, `source` (file, sha256, pageCount, `pageIndexBase: 1`), `generator`, `poet`, `categories[]` →
`poems[]` (`gdbPoemId`, `key`, `title`, `printedNumber`, `pdfPages`, `firstVerseHash`, `verses[]` with
`pdfPage`, `marginNumber`, `flags`, `review`), `footnotes[]` (text the model took for footnotes) and `issues[]`. `key` and `firstVerseHash` let you match a published poem
back to the sidecar even if the site renumbers the ids on import.

## Status / not done yet

* The **WinForms window has not been compiled or run**: the build environment had no Windows desktop SDK.
  It is a thin layer (all real work is in `ConversionJob`, which the CLI also uses and which was tested),
  but expect to fix a small compile error or layout detail on first build. Please send me the output.
* Verified so far: compiles on .NET 10; end-to-end run against a **local mock** OpenAI endpoint using real
  pages of the sample PDF (render → request with image → parse → assemble → GDB + JSON; resume from cache;
  assemble-only mode; failure handling). The GDB schema was compared with the desktop DDL.
* **Not** verified: real model output quality (OCR accuracy, heading detection, centered-line detection) –
  no endpoint was reachable from the build environment. First job: run 20 pages and compare with typed text.
* `Microsoft.Data.Sqlite` is used by `GdbWriter` (NuGet); during development it was exercised through a
  stand-in on the system SQLite, so the first build on your machine is the first real NuGet restore.
* Planned: geometric layout pass (line extents/gaps from the image) to confirm centered vs two-column and
  margin numbers independently of the model; bounding boxes in the sidecar; PDFium renderer (no poppler
  dependency); sub-categories; poet biography from the preface; review UI; optional two-model cross-check.
* `tools/mock_server.py` is a synthetic OpenAI-compatible server for offline tests (its page answers are made up).

## Sub-sections and poem numbers

* A category heading that the model marks as level 2, or that starts with a word in `SubSectionPrefixes`
  (default «حرف», e.g. «حرف الف» inside the ghazals), becomes a sub-category of the current main section.
  A running header equal to the enclosing section never opens a new category.
* Printed poem numbers are checked against their sequence (`AutoCorrectPoemNumbers`). A block of numbers that fits
  exactly into the gap between its neighbours (e.g. 350–400 between 249 and 301) is renumbered, flag `number-corrected`
  (the original is kept as `printedNumberRaw` in the JSON). Other breaks get the flag `number-jump` and an entry in the report.
  Re-run `assemble` on an existing work folder to apply this without calling the model again.

## Books that mix prose and verse (گلستان، تذکرة الشعراء)

Turn on `IncludeProse` (CLI: `--include-prose`; Windows app: the checkbox under the book fields).

* Prose is kept as **paragraph verses** (position -1). One printed paragraph = one verse, with no line breaks;
  a paragraph cut by a page break is joined to its continuation.
* Verses printed on their own lines stay verses. In this mode two stacked centered lines are one ordinary bayt
  (Right + Left), and a lone leftover line becomes a Single verse flagged `odd-hemistich`.
* Entry headings («ذکر …»، «حکایت …») start a new poem; big divisions («باب»، «طبقه») become categories.
* Title pages, contents, indexes and blank pages are still skipped. Margin line counters are ignored (they count
  printed lines, so the verse-counter check is off in this mode).
* Prose mode has its own cache folder (`work/pages-prose`), so it never mixes with an earlier poetry-only run of the same book.

## Linking published poems to the PDF pages on naskban.ir

After a book is published on ganjoor.net, export the poet from the site as a `.gdb` (poem ids and urls then match the
site database) and link it to the PDF stored on naskban.ir:

* Windows app: button «پیوند به گنجور / نسک‌بان…» (file `.gdb`, the converter's `.pages.json`, the Naskban book id, your
  Naskban user name and password).
* Command line:
  `pdf2gdb link --gdb taleb.gdb --json book.pages.json --book-id 24017` (user/password from `NASKBAN_USER` /
  `NASKBAN_PASSWORD` or `--user` / `--password`; `--preview` only builds the report; `--first-page-only`; `--machine`).

How it works: the poems of the `.gdb` are matched to the poems of the JSON by text (ids, titles and categories may have
been edited on the site; merged or split poems are handled), and every poem is linked to exactly the pages its verses are
printed on (`POST https://api.naskban.ir/api/pdf/ganjoor` with the poem id, its address, the full title
«گنجور » … » title», the book id and the page number; `isTextOriginalSource` is true). Links go to the Naskban moderation queue.
A `*.naskban-<id>.report.txt` next to the `.gdb` lists doubtful matches and unmatched poems; a `*.done.txt` remembers what was
sent, so a re-run (or a run after an interruption) sends only what is missing. A saved password is encrypted with Windows DPAPI.

## Building the installer (end-user setup.exe)

`installer\build-installer.cmd` (or `build-installer.ps1 -Version 1.0.1`) publishes the Windows app self-contained
(win-x64, no .NET installation needed on the user's PC) and wraps it with Inno Setup into
`installer\output\Pdf2GdbStudio-Setup-<version>.exe`. The setup installs for the current user without an administrator
prompt, adds a desktop shortcut and a Start menu shortcut (both on by default) plus an uninstaller, and upgrades an older
version in place. Put the unzipped poppler folder in `src\GanjoorPdfToGdb.WinForms\poppler` before building to ship it
inside the app (check poppler's licence terms for redistribution). The icon is `src\GanjoorPdfToGdb.WinForms\app.ico`
(`tools\make_icon.py` regenerates it).
