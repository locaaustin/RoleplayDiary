# Roleplay Diary

A Windows desktop app for archiving roleplay characters. Built for players on heavy-RP
servers — GTA World, fantasy servers, anywhere people pour hundreds of hours into one
character and want the whole story kept somewhere better than a Discord channel.

> **Alpha.** Functional and usable, but expect rough edges. See
> [Known limitations](#known-limitations).

## What it does

- **Keep a roster of characters** — name, nickname, age, server, occupation, faction.
  Only the name is required; everything else can be filled in later.
- **Grow the story over time** — add a timeline entry whenever something happens: a
  marriage, a first job, an arrest, a quiet night that mattered. Entries carry a date
  and a kind (Journal, Milestone, Relationship, Career, Crime, Injury, Death) and are
  always read oldest-first. Dates are typed however you like — `2026-06-20`,
  `20 June 2026`, `today` — and anything unrecognised is simply saved undated.
- **Mark characters alive, deceased or shelved** — and filter the roster by status.
- **Build a gallery** — screenshots are *copied into* the diary, so moving or deleting
  the original file never breaks the archive. Photos can be attached while first
  creating the character, and the first one becomes the cover.
- **Export the whole life as a PDF** — cover page with the portrait, the profile, the
  background, the full timeline, then the gallery.

## Running it

**Requirements:** Windows 10 or 11 and [Python 3.11 or newer](https://www.python.org/downloads/).

```bash
pip install -r requirements.txt
```

That installs two packages — `reportlab` for the PDF and `pillow` for reading images.
The interface itself is tkinter, which is part of Python, so there is no GUI toolkit
to install.

Then either double-click `RoleplayDiary.pyw`, or:

```bash
python controller.py
```

`.pyw` is the Windows extension for a Python program with no console window, so
double-clicking behaves like any other desktop app. To put it on the taskbar or Start
menu, make a shortcut to that file.

## Where your data lives

Everything is under `%AppData%\RoleplayDiary`:

| Path | What it is |
| --- | --- |
| `roleplaydiary.db` | SQLite database: characters, entries, photo records |
| `media\<characterId>\` | the imported image files |
| `exports\` | default folder offered when exporting a PDF |
| `crash.log` | written only if something goes wrong — attach it to bug reports |

**Backing up** means copying that one folder. Nothing is stored anywhere else, and
nothing leaves the machine. SQLite needs no setup: the database file and its tables
are created on first launch.

## How the code is laid out

Three files, in the classic model–view–controller split.

```
model.py        the data and everything that may be done to it
view.py         everything you can see
controller.py   the flow between them, and the entry point

RoleplayDiary.pyw   double-click launcher
requirements.txt    reportlab and pillow
pyproject.toml      package metadata, for `pip install -e .`
FILES.txt           a plain-language tour of all of the above
```

**[`model.py`](model.py)** holds the characters, entries and photos; where they are
stored; the SQLite schema and its migrations; importing images; and the PDF exporter.
The rule that makes it a model is at the top of the file: **it must never import
tkinter.** That means the whole data side can be driven from a plain script with no
window open, which is how it is checked.

**[`view.py`](view.py)** is tkinter and nothing else. It is *passive*: it draws what
it is handed and reports what the player did by calling the handlers in
`MainView.on`. It never opens the database, never writes a file, and never decides
what should happen next. The `Handlers` class is the entire contract between the view
and the controller — read it and you know everything the app can do.

**[`controller.py`](controller.py)** owns the running state (what is searched for,
which filter is on, which character is open) and is the only file that imports both
of the others. Every handler has the same shape:

> the view reports what the player did → ask the model to change something, or ask
> the view a question → tell the view what to draw now

### Notes for anyone working on it

**Adding a database column.** Append a new SQL script to `Database.MIGRATIONS` in
`model.py`. Scripts run in order and SQLite's own `user_version` pragma records how
far an install has got. **Never edit a script that has shipped** — existing diaries
have already run it.

**Theming.** Every colour is a token on `view.Palette`. No widget writes a colour code
of its own, so a light theme means editing that one class.

**Why tkinter looks like this.** tkinter has no rounded corners, no placeholder text,
no scrolling frame and barely accepts colour on Windows. The small classes near the
top of `view.py` — `Button`, `Chip`, `Pill`, `Field`, `ScrollFrame`, `SlimScrollbar` —
exist to supply those, each drawn on a canvas or assembled by hand. Build new screens
out of those rather than raw tkinter widgets, or the dark theme will not hold.

### Upgrading from the earlier versions

The app has been through a C# release and a PySide6 one. The database file and folder
layout have never changed, so an existing diary is picked up as-is. Timestamps written
by the C# version are in .NET round-trip format; the parser reads both that and
Python's.

## Known limitations

Things deliberately left for after the alpha:

- No undo. Deletes are confirmed, then permanent.
- No in-app backup, restore, or import/export of the database itself.
- The roster and gallery are not virtualised, so a few hundred photos on one character
  will feel heavy.
- Timeline dates are single calendar dates — no ranges, and no in-character calendars
  for fantasy settings.
- Entry bodies are plain text; no formatting or links.
- One diary per Windows user account; no profiles or sync.
- The PDF uses the standard Helvetica font, which covers Latin characters only.
  Cyrillic, CJK or emoji in a name or entry will not export correctly.
- No app icon yet, and the window uses standard Windows chrome.

## Third-party licensing

[reportlab](https://www.reportlab.com/) is BSD and [Pillow](https://python-pillow.org/)
is MIT-CMU. tkinter is part of Python itself. Nothing here carries a copyleft
obligation, so the project is free to be licensed however you like.
