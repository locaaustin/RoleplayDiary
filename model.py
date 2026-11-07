"""
Roleplay Diary - MODEL
======================

The data and everything that may be done to it: the characters, their stories and
photos, how all of that is stored, and how it is exported to PDF.

**This file must never import tkinter.** That single rule is what keeps the model a
model: it can be run, inspected and tested from a plain script with no window open.
The controller talks to the :class:`Diary` class at the bottom; nothing outside this
file writes SQL.
"""

from __future__ import annotations

import os
import shutil
import sqlite3
import tempfile
import uuid
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import date, datetime, timedelta, timezone
from enum import IntEnum
from pathlib import Path
from typing import ClassVar, Iterable, Iterator, Sequence
from xml.sax.saxutils import escape

from PIL import Image as PilImage
from PIL import ImageOps
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.platypus import (
    Image,
    KeepTogether,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)

APP_VERSION = "0.3.0-alpha"


# =============================================================================
#  Choices
# =============================================================================


class LabelledEnum(IntEnum):
    """
    An int enum whose members also carry the wording the screen shows. Stored as the
    integer, displayed as the label, so rewording a label never migrates data.
    """

    label: str

    def __new__(cls, value: int, label: str) -> "LabelledEnum":
        member = int.__new__(cls, value)
        member._value_ = value
        member.label = label
        return member

    @classmethod
    def coerce(cls, value: object) -> "LabelledEnum":
        """Never throw on an unexpected database value; fall back to the first member."""
        try:
            return cls(int(value))  # type: ignore[arg-type]
        except (TypeError, ValueError):
            return next(iter(cls))

    @classmethod
    def options(cls) -> Sequence["LabelledEnum"]:
        return tuple(cls)

    @classmethod
    def labels(cls) -> list[str]:
        return [member.label for member in cls]

    @classmethod
    def from_label(cls, label: str) -> "LabelledEnum":
        for member in cls:
            if member.label == label:
                return member
        return next(iter(cls))


class CharacterStatus(LabelledEnum):
    ALIVE = (0, "Alive")
    DEAD = (1, "Deceased")
    SHELVED = (2, "Shelved")


class EntryKind(LabelledEnum):
    JOURNAL = (0, "Journal")
    MILESTONE = (1, "Milestone")
    RELATIONSHIP = (2, "Relationship")
    CAREER = (3, "Career")
    CRIME = (4, "Crime")
    INJURY = (5, "Injury")
    DEATH = (6, "Death")


# =============================================================================
#  Dates
# =============================================================================


class IsoFormat:
    """
    Text conversions for the two shapes SQLite stores: full UTC timestamps and plain
    calendar dates.

    The parser is deliberately forgiving. Rows written by the original C# version use
    .NET round-trip format, whose seven fractional digits and trailing Z Python's own
    parser rejects.
    """

    DATE_FORMAT = "%Y-%m-%d"

    @staticmethod
    def utc_now() -> datetime:
        return datetime.now(timezone.utc)

    @staticmethod
    def to_text(moment: datetime) -> str:
        if moment.tzinfo is None:
            moment = moment.replace(tzinfo=timezone.utc)
        return moment.astimezone(timezone.utc).isoformat()

    @staticmethod
    def parse(text: object) -> datetime:
        if isinstance(text, datetime):
            return text
        if not text:
            return IsoFormat.utc_now()

        cleaned = str(text).strip()
        if cleaned[-1:] in ("Z", "z"):
            cleaned = cleaned[:-1] + "+00:00"

        if "." in cleaned:
            head, _, tail = cleaned.partition(".")
            digits = ""
            while tail and tail[0].isdigit():
                digits, tail = digits + tail[0], tail[1:]
            cleaned = head + "." + digits[:6].ljust(6, "0") + tail if digits else head + tail

        try:
            parsed = datetime.fromisoformat(cleaned)
        except ValueError:
            return IsoFormat.utc_now()

        return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)

    @staticmethod
    def date_to_text(value: date | None) -> str | None:
        return value.strftime(IsoFormat.DATE_FORMAT) if value else None

    @staticmethod
    def parse_date(text: object) -> date | None:
        if isinstance(text, datetime):
            return text.date()
        if isinstance(text, date):
            return text
        if not text:
            return None
        try:
            return datetime.strptime(str(text).strip()[:10], IsoFormat.DATE_FORMAT).date()
        except ValueError:
            return None


class FlexibleDate:
    """
    Dates people actually type. Anything unrecognised means undated rather than an
    error, because being made to format a date correctly is no way to write a diary.
    """

    FORMATS = (
        "%Y-%m-%d", "%Y/%m/%d", "%d/%m/%Y", "%m/%d/%Y", "%d-%m-%Y", "%d.%m.%Y",
        "%d %B %Y", "%d %b %Y", "%B %d %Y", "%b %d %Y", "%B %d, %Y", "%b %d, %Y",
        "%B %Y", "%b %Y", "%Y",
    )

    KEYWORDS = {"today": 0, "now": 0, "yesterday": -1, "tomorrow": 1}

    @classmethod
    def parse(cls, text: str) -> date | None:
        cleaned = " ".join((text or "").split())
        if not cleaned:
            return None

        offset = cls.KEYWORDS.get(cleaned.casefold())
        if offset is not None:
            return date.today() + timedelta(days=offset)

        for pattern in cls.FORMATS:
            try:
                return datetime.strptime(cleaned, pattern).date()
            except ValueError:
                continue
        return None

    @classmethod
    def describe(cls, text: str) -> tuple[str, bool]:
        """A sentence for the hint under the field, and whether it was understood."""
        if not (text or "").strip():
            return ("No date - the entry will sit where you added it.", True)

        parsed = cls.parse(text)
        if parsed is None:
            return ("Not a date we recognise, so this will be saved undated.", False)
        return (f"Reads as {parsed:%d %B %Y}.", True)


# =============================================================================
#  The things a diary is made of
# =============================================================================


@dataclass
class Record:
    """Shared identity and birth time for everything the diary stores."""

    id: int = 0
    created_utc: datetime = field(default_factory=IsoFormat.utc_now)

    @property
    def is_saved(self) -> bool:
        """False until a repository has given it a row, and therefore an id."""
        return self.id > 0

    def touch(self) -> None:
        """Hook for records that track their own last-modified time."""


@dataclass
class Character(Record):
    """One character: the identity fields, plus the slow-moving life story."""

    name: str = ""
    nickname: str | None = None
    age: int | None = None
    server: str | None = None
    occupation: str | None = None
    affiliation: str | None = None
    status: CharacterStatus = CharacterStatus.ALIVE
    background: str | None = None
    portrait_photo_id: int | None = None
    updated_utc: datetime = field(default_factory=IsoFormat.utc_now)

    COLUMNS: ClassVar[str] = (
        "Id, Name, Nickname, Age, Server, Occupation, Affiliation, "
        "Status, Background, PortraitPhotoId, CreatedUtc, UpdatedUtc"
    )

    @classmethod
    def from_row(cls, row: sqlite3.Row) -> "Character":
        return cls(
            id=row["Id"],
            name=row["Name"],
            nickname=row["Nickname"],
            age=row["Age"],
            server=row["Server"],
            occupation=row["Occupation"],
            affiliation=row["Affiliation"],
            status=CharacterStatus.coerce(row["Status"]),
            background=row["Background"],
            portrait_photo_id=row["PortraitPhotoId"],
            created_utc=IsoFormat.parse(row["CreatedUtc"]),
            updated_utc=IsoFormat.parse(row["UpdatedUtc"]),
        )

    def touch(self) -> None:
        self.updated_utc = IsoFormat.utc_now()

    @property
    def quoted_nickname(self) -> str:
        return "“" + self.nickname + "”" if self.nickname else ""

    @property
    def display_name(self) -> str:
        return (self.name + " " + self.quoted_nickname) if self.nickname else self.name

    @property
    def age_label(self) -> str:
        return f"{self.age} years old" if self.age else ""

    @property
    def archived_label(self) -> str:
        return f"archived {self.created_utc.astimezone():%d %B %Y}"

    @property
    def initials(self) -> str:
        """Placeholder shown wherever a portrait is missing."""
        words = [word for word in self.name.split() if word]
        return "".join(word[0] for word in words[:2]).upper() or "?"

    @property
    def facts_line(self) -> str:
        parts = [self.age_label, self.server, self.occupation, self.affiliation]
        return " · ".join(part for part in parts if part)

    def meta_line(self, entry_count: int) -> str:
        """The one-line summary under the name in the roster."""
        parts = [part for part in (self.age_label, self.server, self.occupation) if part]
        parts.append("1 entry" if entry_count == 1 else f"{entry_count} entries")
        return " · ".join(parts)

    def matches(self, search: str) -> bool:
        needle = search.strip().casefold()
        if not needle:
            return True
        return any(
            needle in value.casefold()
            for value in (self.name, self.nickname or "", self.server or "")
        )


@dataclass
class DiaryEntry(Record):
    """Something that happened. The unit the story is told in."""

    character_id: int = 0
    title: str = ""
    body: str | None = None
    kind: EntryKind = EntryKind.JOURNAL
    occurred_on: date | None = None
    updated_utc: datetime = field(default_factory=IsoFormat.utc_now)

    COLUMNS: ClassVar[str] = (
        "Id, CharacterId, Title, Body, Kind, OccurredOn, CreatedUtc, UpdatedUtc"
    )

    @classmethod
    def from_row(cls, row: sqlite3.Row) -> "DiaryEntry":
        return cls(
            id=row["Id"],
            character_id=row["CharacterId"],
            title=row["Title"],
            body=row["Body"],
            kind=EntryKind.coerce(row["Kind"]),
            occurred_on=IsoFormat.parse_date(row["OccurredOn"]),
            created_utc=IsoFormat.parse(row["CreatedUtc"]),
            updated_utc=IsoFormat.parse(row["UpdatedUtc"]),
        )

    def touch(self) -> None:
        self.updated_utc = IsoFormat.utc_now()

    @property
    def is_dated(self) -> bool:
        return self.occurred_on is not None

    @property
    def date_label(self) -> str:
        return f"{self.occurred_on:%d %B %Y}" if self.occurred_on else "undated"

    @property
    def short_date_label(self) -> str:
        return f"{self.occurred_on:%d %b %Y}" if self.occurred_on else "undated"

    @property
    def timeline_key(self) -> tuple[date, int]:
        """
        Oldest first. Undated entries fall back to when they were written, so they
        land in the story roughly where the player added them.
        """
        return (self.occurred_on or self.created_utc.date(), self.id)


@dataclass
class CharacterPhoto(Record):
    """
    A screenshot. The file itself is copied into the diary's own media folder, so
    moving or deleting the original never breaks the archive.
    """

    character_id: int = 0
    file_name: str = ""
    caption: str | None = None
    taken_on: date | None = None
    full_path: str | None = None

    COLUMNS: ClassVar[str] = "Id, CharacterId, FileName, Caption, TakenOn, CreatedUtc"

    @classmethod
    def from_row(cls, row: sqlite3.Row) -> "CharacterPhoto":
        return cls(
            id=row["Id"],
            character_id=row["CharacterId"],
            file_name=row["FileName"],
            caption=row["Caption"],
            taken_on=IsoFormat.parse_date(row["TakenOn"]),
            created_utc=IsoFormat.parse(row["CreatedUtc"]),
        )

    @property
    def added_label(self) -> str:
        return f"added {self.created_utc.astimezone():%d %b %Y}"


@dataclass
class RosterItem:
    """A character as the sidebar needs them: with their counts already worked out."""

    character: Character
    entry_count: int = 0
    portrait_path: str | None = None

    @property
    def id(self) -> int:
        return self.character.id

    @property
    def name(self) -> str:
        return self.character.name

    @property
    def status(self) -> CharacterStatus:
        return self.character.status

    @property
    def meta(self) -> str:
        return self.character.meta_line(self.entry_count)


# =============================================================================
#  Where things are kept
# =============================================================================


class AppPaths:
    """
    Every file the diary owns lives under one folder in the user's roaming profile,
    so backing up the whole archive means copying a single directory.

    The layout is deliberately the one earlier versions used, so an existing diary is
    picked up rather than orphaned.
    """

    FOLDER_NAME = "RoleplayDiary"
    DATABASE_NAME = "roleplaydiary.db"

    def __init__(self, root: Path | None = None) -> None:
        self._root = Path(root) if root is not None else self._default_root()
        for folder in (self._root, self.media, self.exports):
            folder.mkdir(parents=True, exist_ok=True)

    @staticmethod
    def _default_root() -> Path:
        """%AppData%/RoleplayDiary on Windows, a sensible equivalent elsewhere."""
        appdata = os.environ.get("APPDATA")
        base = Path(appdata) if appdata else Path.home() / ".local" / "share"
        return base / AppPaths.FOLDER_NAME

    @property
    def root(self) -> Path:
        return self._root

    @property
    def database(self) -> Path:
        return self._root / self.DATABASE_NAME

    @property
    def media(self) -> Path:
        return self._root / "media"

    @property
    def exports(self) -> Path:
        return self._root / "exports"

    @property
    def crash_log(self) -> Path:
        return self._root / "crash.log"

    def media_for(self, character_id: int) -> Path:
        """One folder per character, created on demand."""
        folder = self.media / str(character_id)
        folder.mkdir(parents=True, exist_ok=True)
        return folder


class Database:
    """
    Owns the one connection the app uses and brings the schema up to date on open.

    Schema changes are append-only: add a script to MIGRATIONS and SQLite's own
    ``user_version`` pragma records how far an install has got. Index 0 is migration 1.
    **Never edit a script that has shipped** - existing diaries have already run it.
    """

    MIGRATIONS: tuple[str, ...] = (
        # 1 - the original schema.
        """
        CREATE TABLE Characters (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            Name            TEXT    NOT NULL,
            Nickname        TEXT    NULL,
            Age             INTEGER NULL,
            Server          TEXT    NULL,
            Occupation      TEXT    NULL,
            Affiliation     TEXT    NULL,
            Status          INTEGER NOT NULL DEFAULT 0,
            Background      TEXT    NULL,
            PortraitPhotoId INTEGER NULL,
            CreatedUtc      TEXT    NOT NULL,
            UpdatedUtc      TEXT    NOT NULL
        );

        CREATE TABLE DiaryEntries (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            CharacterId INTEGER NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            Title       TEXT    NOT NULL,
            Body        TEXT    NULL,
            Kind        INTEGER NOT NULL DEFAULT 0,
            OccurredOn  TEXT    NULL,
            CreatedUtc  TEXT    NOT NULL,
            UpdatedUtc  TEXT    NOT NULL
        );

        CREATE TABLE Photos (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            CharacterId INTEGER NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            FileName    TEXT    NOT NULL,
            Caption     TEXT    NULL,
            TakenOn     TEXT    NULL,
            CreatedUtc  TEXT    NOT NULL
        );

        CREATE INDEX IX_DiaryEntries_CharacterId ON DiaryEntries(CharacterId);
        CREATE INDEX IX_Photos_CharacterId ON Photos(CharacterId);
        """,
    )

    def __init__(self, paths: AppPaths) -> None:
        self._paths = paths
        self._connection: sqlite3.Connection | None = None

    def open(self) -> sqlite3.Connection:
        """Connect, enforce foreign keys, then migrate. Safe to call twice."""
        if self._connection is not None:
            return self._connection

        connection = sqlite3.connect(self._paths.database)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA journal_mode = WAL")
        self._connection = connection

        self._migrate()
        return connection

    def close(self) -> None:
        if self._connection is not None:
            self._connection.close()
            self._connection = None

    @property
    def connection(self) -> sqlite3.Connection:
        if self._connection is None:
            raise RuntimeError("Database.open() must be called before use.")
        return self._connection

    @contextmanager
    def transaction(self) -> Iterator[sqlite3.Connection]:
        """Commit on success, roll back on anything raised inside the block."""
        connection = self.connection
        try:
            yield connection
        except Exception:
            connection.rollback()
            raise
        else:
            connection.commit()

    @property
    def schema_version(self) -> int:
        return int(self.connection.execute("PRAGMA user_version").fetchone()[0])

    def _migrate(self) -> None:
        current = self.schema_version
        for version, script in enumerate(self.MIGRATIONS, start=1):
            if version <= current:
                continue
            with self.transaction() as connection:
                connection.executescript(script)
                # Pragmas take no parameters, hence the f-string; version is an int.
                connection.execute(f"PRAGMA user_version = {version}")


# =============================================================================
#  Tables
# =============================================================================


class Repository:
    """Shared plumbing: query, execute, insert-and-return-id, delete by id."""

    TABLE: ClassVar[str] = ""

    def __init__(self, database: Database) -> None:
        self._database = database

    @property
    def _connection(self) -> sqlite3.Connection:
        return self._database.connection

    def _query(self, sql: str, params: Sequence[object] = ()) -> list[sqlite3.Row]:
        return self._connection.execute(sql, tuple(params)).fetchall()

    def _query_one(self, sql: str, params: Sequence[object] = ()) -> sqlite3.Row | None:
        return self._connection.execute(sql, tuple(params)).fetchone()

    def _execute(self, sql: str, params: Sequence[object] = ()) -> int:
        with self._database.transaction() as connection:
            return connection.execute(sql, tuple(params)).rowcount

    def _insert(self, sql: str, params: Sequence[object]) -> int:
        with self._database.transaction() as connection:
            return int(connection.execute(sql, tuple(params)).lastrowid or 0)

    def count(self) -> int:
        row = self._query_one(f"SELECT COUNT(*) FROM {self.TABLE}")
        return int(row[0]) if row else 0

    def delete(self, record_id: int) -> None:
        self._execute(f"DELETE FROM {self.TABLE} WHERE Id = ?", (record_id,))


class CharacterRepository(Repository):
    """The roster."""

    TABLE = "Characters"

    def get_all(self) -> list[Character]:
        """Living first, then deceased, then shelved; alphabetical inside each group."""
        rows = self._query(
            f"SELECT {Character.COLUMNS} FROM {self.TABLE} "
            "ORDER BY Status, Name COLLATE NOCASE"
        )
        return [Character.from_row(row) for row in rows]

    def get(self, character_id: int) -> Character | None:
        row = self._query_one(
            f"SELECT {Character.COLUMNS} FROM {self.TABLE} WHERE Id = ?", (character_id,)
        )
        return Character.from_row(row) if row else None

    def insert(self, character: Character) -> Character:
        """Fills in the new id on the object it was given, then returns it."""
        character.touch()
        character.id = self._insert(
            f"INSERT INTO {self.TABLE} "
            "(Name, Nickname, Age, Server, Occupation, Affiliation, Status, "
            " Background, PortraitPhotoId, CreatedUtc, UpdatedUtc) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            (
                character.name, character.nickname, character.age, character.server,
                character.occupation, character.affiliation, int(character.status),
                character.background, character.portrait_photo_id,
                IsoFormat.to_text(character.created_utc),
                IsoFormat.to_text(character.updated_utc),
            ),
        )
        return character

    def update(self, character: Character) -> None:
        character.touch()
        self._execute(
            f"UPDATE {self.TABLE} SET "
            "Name = ?, Nickname = ?, Age = ?, Server = ?, Occupation = ?, "
            "Affiliation = ?, Status = ?, Background = ?, PortraitPhotoId = ?, "
            "UpdatedUtc = ? WHERE Id = ?",
            (
                character.name, character.nickname, character.age, character.server,
                character.occupation, character.affiliation, int(character.status),
                character.background, character.portrait_photo_id,
                IsoFormat.to_text(character.updated_utc), character.id,
            ),
        )

    def set_status(self, character_id: int, status: CharacterStatus) -> None:
        self._execute(
            f"UPDATE {self.TABLE} SET Status = ?, UpdatedUtc = ? WHERE Id = ?",
            (int(status), IsoFormat.to_text(IsoFormat.utc_now()), character_id),
        )

    def set_portrait(self, character_id: int, photo_id: int | None) -> None:
        self._execute(
            f"UPDATE {self.TABLE} SET PortraitPhotoId = ?, UpdatedUtc = ? WHERE Id = ?",
            (photo_id, IsoFormat.to_text(IsoFormat.utc_now()), character_id),
        )


class DiaryEntryRepository(Repository):
    """The timeline. Always read oldest-first, the way a story is told."""

    TABLE = "DiaryEntries"

    def get_for_character(self, character_id: int) -> list[DiaryEntry]:
        rows = self._query(
            f"SELECT {DiaryEntry.COLUMNS} FROM {self.TABLE} WHERE CharacterId = ? "
            "ORDER BY COALESCE(OccurredOn, CreatedUtc) ASC, Id ASC",
            (character_id,),
        )
        return [DiaryEntry.from_row(row) for row in rows]

    def entry_counts(self) -> dict[int, int]:
        """Every character's entry count in one query, for the roster."""
        rows = self._query(
            f"SELECT CharacterId, COUNT(*) AS Total FROM {self.TABLE} GROUP BY CharacterId"
        )
        return {row["CharacterId"]: row["Total"] for row in rows}

    def insert(self, entry: DiaryEntry) -> DiaryEntry:
        entry.touch()
        entry.id = self._insert(
            f"INSERT INTO {self.TABLE} "
            "(CharacterId, Title, Body, Kind, OccurredOn, CreatedUtc, UpdatedUtc) "
            "VALUES (?, ?, ?, ?, ?, ?, ?)",
            (
                entry.character_id, entry.title, entry.body, int(entry.kind),
                IsoFormat.date_to_text(entry.occurred_on),
                IsoFormat.to_text(entry.created_utc),
                IsoFormat.to_text(entry.updated_utc),
            ),
        )
        return entry

    def update(self, entry: DiaryEntry) -> None:
        entry.touch()
        self._execute(
            f"UPDATE {self.TABLE} SET Title = ?, Body = ?, Kind = ?, OccurredOn = ?, "
            "UpdatedUtc = ? WHERE Id = ?",
            (
                entry.title, entry.body, int(entry.kind),
                IsoFormat.date_to_text(entry.occurred_on),
                IsoFormat.to_text(entry.updated_utc), entry.id,
            ),
        )


class PhotoRepository(Repository):
    """
    The gallery. Rows hold only a file name; this is the one place that knows how to
    turn that into a path on disk.
    """

    TABLE = "Photos"

    def __init__(self, database: Database, paths: AppPaths) -> None:
        super().__init__(database)
        self._paths = paths

    def _build(self, row: sqlite3.Row) -> CharacterPhoto:
        photo = CharacterPhoto.from_row(row)
        photo.full_path = str(self._paths.media / str(photo.character_id) / photo.file_name)
        return photo

    def get_for_character(self, character_id: int) -> list[CharacterPhoto]:
        rows = self._query(
            f"SELECT {CharacterPhoto.COLUMNS} FROM {self.TABLE} WHERE CharacterId = ? "
            "ORDER BY Id ASC",
            (character_id,),
        )
        return [self._build(row) for row in rows]

    def group_by_character(self) -> dict[int, list[CharacterPhoto]]:
        grouped: dict[int, list[CharacterPhoto]] = {}
        rows = self._query(f"SELECT {CharacterPhoto.COLUMNS} FROM {self.TABLE} ORDER BY Id")
        for row in rows:
            photo = self._build(row)
            grouped.setdefault(photo.character_id, []).append(photo)
        return grouped

    def insert(self, photo: CharacterPhoto) -> CharacterPhoto:
        photo.id = self._insert(
            f"INSERT INTO {self.TABLE} (CharacterId, FileName, Caption, TakenOn, CreatedUtc) "
            "VALUES (?, ?, ?, ?, ?)",
            (
                photo.character_id, photo.file_name, photo.caption,
                IsoFormat.date_to_text(photo.taken_on),
                IsoFormat.to_text(photo.created_utc),
            ),
        )
        photo.full_path = str(self._paths.media / str(photo.character_id) / photo.file_name)
        return photo

    def set_caption(self, photo_id: int, caption: str | None) -> None:
        self._execute(f"UPDATE {self.TABLE} SET Caption = ? WHERE Id = ?", (caption, photo_id))

    def delete(self, record_id: int) -> None:
        """Also clears the portrait pointer, which has no foreign key to cascade."""
        with self._database.transaction() as connection:
            connection.execute(
                "UPDATE Characters SET PortraitPhotoId = NULL WHERE PortraitPhotoId = ?",
                (record_id,),
            )
            connection.execute(f"DELETE FROM {self.TABLE} WHERE Id = ?", (record_id,))


# =============================================================================
#  Photos on disk
# =============================================================================


class PhotoStorage:
    """
    Photos are *copied in*, never referenced in place: the player can clean out their
    screenshots folder a year later and the archive still holds together.
    """

    SUPPORTED_EXTENSIONS = frozenset({".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"})

    def __init__(self, paths: AppPaths, photos: PhotoRepository) -> None:
        self._paths = paths
        self._photos = photos

    @classmethod
    def can_read(cls, source: str | Path) -> bool:
        return Path(source).suffix.lower() in cls.SUPPORTED_EXTENSIONS

    @classmethod
    def file_types(cls) -> list[tuple[str, str]]:
        """The extension list in the shape tkinter's file dialog wants."""
        patterns = " ".join(f"*{ext}" for ext in sorted(cls.SUPPORTED_EXTENSIONS))
        return [("Images", patterns), ("All files", "*.*")]

    def import_one(self, character_id: int, source: str | Path) -> CharacterPhoto | None:
        """
        Copy one file in and record it. Returns None for anything unreadable, so a bad
        file in a multi-select never aborts the rest.
        """
        origin = Path(source)
        if not self.can_read(origin) or not origin.is_file():
            return None

        stored_name = f"{uuid.uuid4().hex}{origin.suffix.lower()}"
        destination = self._paths.media_for(character_id) / stored_name

        try:
            shutil.copy2(origin, destination)
        except OSError:
            return None

        return self._photos.insert(
            CharacterPhoto(character_id=character_id, file_name=stored_name)
        )

    def import_many(
        self, character_id: int, sources: Iterable[str | Path]
    ) -> list[CharacterPhoto]:
        """
        Import several, skipping any the app cannot read. The order of the result
        matches the order given, so the caller can treat the first as the cover.
        """
        imported = [self.import_one(character_id, source) for source in sources]
        return [photo for photo in imported if photo is not None]

    def remove(self, photo: CharacterPhoto) -> None:
        """Delete the row and the file. A file already gone is not an error."""
        self._photos.delete(photo.id)
        if photo.full_path:
            Path(photo.full_path).unlink(missing_ok=True)

    def remove_all_for(self, character_id: int) -> None:
        """Used when a whole character is deleted."""
        for photo in self._photos.get_for_character(character_id):
            self.remove(photo)

        folder = self._paths.media / str(character_id)
        if folder.is_dir():
            shutil.rmtree(folder, ignore_errors=True)


class CharacterArchive:
    """A character with their whole story and gallery attached, read-only."""

    def __init__(
        self, character: Character, entries: list[DiaryEntry], photos: list[CharacterPhoto]
    ) -> None:
        self.character = character
        self.entries = sorted(entries, key=lambda entry: entry.timeline_key)
        self.photos = list(photos)

    @property
    def portrait(self) -> CharacterPhoto | None:
        """The chosen cover, or the newest photo when nothing was chosen."""
        chosen = self.character.portrait_photo_id
        if chosen is not None:
            for photo in self.photos:
                if photo.id == chosen:
                    return photo
        return self.photos[-1] if self.photos else None

    @property
    def portrait_path(self) -> str | None:
        portrait = self.portrait
        return portrait.full_path if portrait else None

    @property
    def span_label(self) -> str:
        """The dated range the timeline covers, for the cover page."""
        dated = [entry.occurred_on for entry in self.entries if entry.occurred_on]
        if not dated:
            return ""
        first, last = min(dated), max(dated)
        return str(first.year) if first.year == last.year else f"{first.year} – {last.year}"


# =============================================================================
#  Export
# =============================================================================


class PdfPalette:
    """Print colours. Deliberately not the app's dark theme."""

    INK = colors.HexColor("#15171D")
    BODY = colors.HexColor("#33384A")
    MUTED = colors.HexColor("#767D90")
    RULE = colors.HexColor("#DADEE7")
    ACCENT = colors.HexColor("#5B3FD9")

    _STATUS = {
        CharacterStatus.ALIVE: colors.HexColor("#1C8B5B"),
        CharacterStatus.DEAD: colors.HexColor("#C2314A"),
        CharacterStatus.SHELVED: colors.HexColor("#B07214"),
    }

    @classmethod
    def for_status(cls, status: CharacterStatus) -> colors.Color:
        return cls._STATUS.get(status, cls.MUTED)


class PdfImageFactory:
    """
    Turns whatever the player imported into something reportlab will certainly draw:
    each file is re-encoded as a plain JPEG in a scratch folder thrown away when the
    export finishes. Exotic formats and broken files are skipped, not fatal.
    """

    MAX_EDGE = 1600

    def __init__(self, scratch: Path) -> None:
        self._scratch = scratch

    def flowable(self, source: str | None, max_width: float, max_height: float) -> Image | None:
        if not source or not Path(source).is_file():
            return None

        try:
            with PilImage.open(source) as handle:
                image = ImageOps.exif_transpose(handle).convert("RGB")
                image.thumbnail((self.MAX_EDGE, self.MAX_EDGE))
                normalised = self._scratch / f"{uuid.uuid4().hex}.jpg"
                image.save(normalised, "JPEG", quality=88)
                width, height = image.size
        except Exception:
            return None

        scale = min(max_width / width, max_height / height)
        return Image(str(normalised), width=width * scale, height=height * scale)


class PdfExporter:
    """Writes one character's whole archive to a file."""

    PAGE_SIZE = A4
    MARGIN = 18 * mm

    def __init__(self) -> None:
        self._styles = self._build_styles()

    @staticmethod
    def suggested_file_name(character_name: str) -> str:
        """A file name Windows will accept, whatever the character is called."""
        invalid = '<>:"/\\|?*'
        cleaned = "".join("_" if ch in invalid or ord(ch) < 32 else ch for ch in character_name)
        cleaned = cleaned.strip().rstrip(".")
        return f"{cleaned or 'Character'} - Roleplay Diary.pdf"

    def export(self, archive: CharacterArchive, destination: str | Path) -> Path:
        target = Path(destination)
        target.parent.mkdir(parents=True, exist_ok=True)

        with tempfile.TemporaryDirectory(prefix="rpdiary-pdf-") as scratch:
            images = PdfImageFactory(Path(scratch))
            document = SimpleDocTemplate(
                str(target),
                pagesize=self.PAGE_SIZE,
                leftMargin=self.MARGIN, rightMargin=self.MARGIN,
                topMargin=self.MARGIN, bottomMargin=self.MARGIN,
                title=f"{archive.character.name} - Roleplay Diary",
                author="Roleplay Diary",
            )

            story = self._cover(archive, images)
            story += self._profile(archive)
            story += self._timeline(archive)
            story += self._gallery(archive, images)

            document.build(story, onFirstPage=self._decorate, onLaterPages=self._decorate)

        return target

    @property
    def content_width(self) -> float:
        return self.PAGE_SIZE[0] - 2 * self.MARGIN

    def _build_styles(self) -> dict[str, ParagraphStyle]:
        base = ParagraphStyle(
            "body", fontName="Helvetica", fontSize=10, leading=15, textColor=PdfPalette.BODY
        )
        return {
            "body": base,
            "cover_name": ParagraphStyle(
                "cover_name", parent=base, fontName="Helvetica-Bold", fontSize=30,
                leading=34, alignment=TA_CENTER, textColor=PdfPalette.INK),
            "cover_nickname": ParagraphStyle(
                "cover_nickname", parent=base, fontSize=14, leading=18,
                alignment=TA_CENTER, textColor=PdfPalette.MUTED),
            "cover_meta": ParagraphStyle(
                "cover_meta", parent=base, fontSize=10, leading=16,
                alignment=TA_CENTER, textColor=PdfPalette.MUTED),
            "section": ParagraphStyle(
                "section", parent=base, fontName="Helvetica-Bold", fontSize=8,
                leading=12, textColor=PdfPalette.ACCENT),
            "entry_title": ParagraphStyle(
                "entry_title", parent=base, fontName="Helvetica-Bold", fontSize=12,
                leading=16, textColor=PdfPalette.INK),
            "entry_date": ParagraphStyle(
                "entry_date", parent=base, fontName="Helvetica-Bold", fontSize=8,
                leading=11, textColor=PdfPalette.INK),
            "muted": ParagraphStyle(
                "muted", parent=base, fontSize=8, leading=11, textColor=PdfPalette.MUTED),
            "field_label": ParagraphStyle(
                "field_label", parent=base, fontName="Helvetica-Bold", fontSize=8,
                leading=12, textColor=PdfPalette.MUTED),
        }

    def _text(self, raw: str, style: str) -> Paragraph:
        """Escape anything the player typed, then honour their line breaks."""
        return Paragraph(escape(raw or "").replace("\n", "<br/>"), self._styles[style])

    def _rule(self, space_before: float = 0) -> Table:
        rule = Table([[""]], colWidths=[self.content_width], rowHeights=[0.6])
        rule.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), PdfPalette.RULE)]))
        rule.spaceBefore = space_before
        return rule

    def _heading(self, text: str) -> list:
        return [Spacer(1, 9 * mm), self._text(text.upper(), "section"), self._rule(2)]

    def _cover(self, archive: CharacterArchive, images: PdfImageFactory) -> list:
        character = archive.character
        story: list = [Spacer(1, 14 * mm)]

        picture = images.flowable(archive.portrait_path, self.content_width * 0.62, 105 * mm)
        if picture is not None:
            picture.hAlign = "CENTER"
            story += [picture, Spacer(1, 12 * mm)]
        else:
            story += [Spacer(1, 30 * mm)]

        story.append(self._text(character.name, "cover_name"))
        if character.nickname:
            story += [Spacer(1, 2 * mm), self._text(character.quoted_nickname, "cover_nickname")]

        facts = [p for p in (character.age_label, character.server, character.occupation) if p]
        if facts:
            story += [Spacer(1, 5 * mm), self._text(" · ".join(facts), "cover_meta")]

        status = Paragraph(
            f'<font color="{PdfPalette.for_status(character.status).hexval()}">'
            f"<b>{escape(character.status.label)}</b></font>",
            self._styles["cover_meta"],
        )
        story += [Spacer(1, 4 * mm), status]

        tail = [part for part in (archive.span_label, character.archived_label) if part]
        story += [Spacer(1, 3 * mm), self._text(" · ".join(tail), "cover_meta")]
        story.append(PageBreak())
        return story

    def _profile(self, archive: CharacterArchive) -> list:
        character = archive.character
        fields = [
            ("Name", character.name),
            ("Nickname", character.nickname),
            ("Age", str(character.age) if character.age else None),
            ("Status", character.status.label),
            ("Server or community", character.server),
            ("Occupation", character.occupation),
            ("Faction or affiliation", character.affiliation),
        ]

        rows = [
            [self._text(label, "field_label"), self._text(value, "body")]
            for label, value in fields
            if value
        ]

        label_width = 42 * mm
        table = Table(rows, colWidths=[label_width, self.content_width - label_width])
        table.setStyle(TableStyle([
            ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ("TOPPADDING", (0, 0), (-1, -1), 3),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
            ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ]))

        story = [self._text("PROFILE", "section"), self._rule(2), Spacer(1, 4 * mm), table]

        if character.background:
            story += self._heading("Background")
            story += [Spacer(1, 3 * mm), self._text(character.background, "body")]

        return story

    def _timeline(self, archive: CharacterArchive) -> list:
        story = self._heading(f"Timeline · {len(archive.entries)} entries")

        if not archive.entries:
            return story + [Spacer(1, 4 * mm), self._text("No entries yet.", "muted")]

        date_width = 34 * mm
        for entry in archive.entries:
            left = [
                self._text(entry.short_date_label, "entry_date"),
                self._text(entry.kind.label, "muted"),
            ]
            right: list = [self._text(entry.title, "entry_title")]
            if entry.body:
                right += [Spacer(1, 1.5 * mm), self._text(entry.body, "body")]

            row = Table([[left, right]], colWidths=[date_width, self.content_width - date_width])
            row.setStyle(TableStyle([
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (0, 0), 0),
                ("LEFTPADDING", (1, 0), (1, 0), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 0),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
                ("LINEBEFORE", (1, 0), (1, 0), 0.8, PdfPalette.RULE),
            ]))
            story.append(KeepTogether([Spacer(1, 5 * mm), row]))

        return story

    def _gallery(self, archive: CharacterArchive, images: PdfImageFactory) -> list:
        if not archive.photos:
            return []

        gutter = 6 * mm
        cell_width = (self.content_width - gutter) / 2

        rows: list = []
        pending: list = []

        for photo in archive.photos:
            picture = images.flowable(photo.full_path, cell_width, 58 * mm)
            if picture is None:
                continue

            cell: list = [picture]
            if photo.caption:
                cell += [Spacer(1, 1.5 * mm), self._text(photo.caption, "muted")]
            pending.append(cell)

            if len(pending) == 2:
                rows.append(self._photo_row(pending, cell_width, gutter))
                pending = []

        if pending:
            rows.append(self._photo_row(pending, cell_width, gutter))

        if not rows:
            return []

        # Keep the heading with the first row, so it never strands at a page foot.
        heading = self._heading(f"Gallery · {len(archive.photos)} photos")
        heading.append(Spacer(1, 4 * mm))
        return [KeepTogether(heading + [rows[0]])] + rows[1:]

    def _photo_row(self, cells: list, cell_width: float, gutter: float) -> KeepTogether:
        padded = cells + [[]] * (2 - len(cells))
        row = Table([[padded[0], "", padded[1]]], colWidths=[cell_width, gutter, cell_width])
        row.setStyle(TableStyle([
            ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ("LEFTPADDING", (0, 0), (-1, -1), 0),
            ("RIGHTPADDING", (0, 0), (-1, -1), 0),
            ("TOPPADDING", (0, 0), (-1, -1), 0),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
        ]))
        return KeepTogether([row, Spacer(1, 4 * mm)])

    def _decorate(self, canvas, document) -> None:
        """Page footer. Page one is the cover and gets no number."""
        canvas.saveState()
        canvas.setFont("Helvetica", 7.5)
        canvas.setFillColor(PdfPalette.MUTED)

        baseline = self.MARGIN * 0.6
        canvas.drawString(self.MARGIN, baseline, "Roleplay Diary")
        if document.page > 1:
            canvas.drawRightString(self.PAGE_SIZE[0] - self.MARGIN, baseline, str(document.page))
        canvas.restoreState()


# =============================================================================
#  The one door into all of the above
# =============================================================================


class Diary:
    """
    Everything the controller is allowed to ask for, behind one object.

    The controller never sees a repository, a connection or a file path. It asks the
    diary for characters and tells the diary what the player did; the diary decides
    what that means for the database and the disk.
    """

    def __init__(self, root: Path | None = None) -> None:
        self.paths = AppPaths(root)
        self._database = Database(self.paths)
        self._characters: CharacterRepository | None = None
        self._entries: DiaryEntryRepository | None = None
        self._photos: PhotoRepository | None = None
        self._storage: PhotoStorage | None = None
        self._pdf = PdfExporter()

    # ------------------------------------------------------------------ lifetime

    def open(self) -> "Diary":
        self._database.open()
        self._characters = CharacterRepository(self._database)
        self._entries = DiaryEntryRepository(self._database)
        self._photos = PhotoRepository(self._database, self.paths)
        self._storage = PhotoStorage(self.paths, self._photos)
        return self

    def close(self) -> None:
        self._database.close()

    def __enter__(self) -> "Diary":
        return self.open()

    def __exit__(self, *_: object) -> None:
        self.close()

    @property
    def schema_version(self) -> int:
        return self._database.schema_version

    # ----------------------------------------------------------------- the roster

    def roster(self) -> list[RosterItem]:
        """Every character, with their entry count and cover photo resolved."""
        counts = self._entries.entry_counts()
        grouped = self._photos.group_by_character()

        items = []
        for character in self._characters.get_all():
            photos = grouped.get(character.id, [])
            items.append(
                RosterItem(
                    character=character,
                    entry_count=counts.get(character.id, 0),
                    portrait_path=self._pick_portrait(photos, character),
                )
            )
        return items

    @staticmethod
    def _pick_portrait(photos: list[CharacterPhoto], character: Character) -> str | None:
        for photo in photos:
            if photo.id == character.portrait_photo_id:
                return photo.full_path
        return photos[-1].full_path if photos else None

    def character(self, character_id: int) -> Character | None:
        return self._characters.get(character_id)

    # -------------------------------------------------------------- characters

    def create_character(self, fields: dict, photos: Sequence[str] = ()) -> Character:
        """
        Make a character, then import any photos that were picked while creating
        them. The photos could only be staged until now, because filing them needs
        an id and the character did not have one yet.
        """
        character = Character()
        self._apply(character, fields)
        self._characters.insert(character)

        imported = self._storage.import_many(character.id, photos)
        if imported:
            character.portrait_photo_id = imported[0].id
            self._characters.set_portrait(character.id, character.portrait_photo_id)

        return character

    def update_character(
        self, character: Character, fields: dict, photos: Sequence[str] = ()
    ) -> int:
        """Returns how many of the given photos could not be read."""
        self._apply(character, fields)
        self._characters.update(character)

        imported = self._storage.import_many(character.id, photos)
        if imported and character.portrait_photo_id is None:
            # A character with no cover yet adopts the first photo added here.
            character.portrait_photo_id = imported[0].id
            self._characters.set_portrait(character.id, character.portrait_photo_id)

        return len(photos) - len(imported)

    @staticmethod
    def _apply(character: Character, fields: dict) -> None:
        def blank(key: str) -> str | None:
            value = (fields.get(key) or "").strip()
            return value or None

        character.name = (fields.get("name") or "").strip()
        character.nickname = blank("nickname")
        character.server = blank("server")
        character.occupation = blank("occupation")
        character.affiliation = blank("affiliation")
        character.background = blank("background")
        character.status = fields.get("status", CharacterStatus.ALIVE)

        age = (fields.get("age") or "").strip()
        character.age = int(age) if age.isdigit() and 0 < int(age) < 1000 else None

    def set_status(self, character: Character, status: CharacterStatus) -> None:
        character.status = status
        self._characters.set_status(character.id, status)

    def set_background(self, character: Character, text: str) -> None:
        character.background = text.strip() or None
        self._characters.update(character)

    def delete_character(self, character_id: int) -> None:
        """Photos first - the files on disk have no foreign key to cascade."""
        self._storage.remove_all_for(character_id)
        self._characters.delete(character_id)

    # ------------------------------------------------------------------ entries

    def entries(self, character_id: int) -> list[DiaryEntry]:
        return self._entries.get_for_character(character_id)

    def add_entry(self, character_id: int, fields: dict) -> DiaryEntry:
        entry = DiaryEntry(character_id=character_id)
        self._apply_entry(entry, fields)
        return self._entries.insert(entry)

    def update_entry(self, entry: DiaryEntry, fields: dict) -> None:
        self._apply_entry(entry, fields)
        self._entries.update(entry)

    @staticmethod
    def _apply_entry(entry: DiaryEntry, fields: dict) -> None:
        entry.title = (fields.get("title") or "").strip()
        entry.kind = fields.get("kind", EntryKind.JOURNAL)
        entry.occurred_on = FlexibleDate.parse(fields.get("when") or "")
        entry.body = (fields.get("body") or "").strip() or None

    def delete_entry(self, entry_id: int) -> None:
        self._entries.delete(entry_id)

    # ------------------------------------------------------------------- photos

    def photos(self, character_id: int) -> list[CharacterPhoto]:
        return self._photos.get_for_character(character_id)

    def add_photos(self, character: Character, sources: Sequence[str]) -> int:
        """Returns how many files were skipped because they could not be read."""
        imported = self._storage.import_many(character.id, sources)
        if imported and character.portrait_photo_id is None:
            character.portrait_photo_id = imported[0].id
            self._characters.set_portrait(character.id, character.portrait_photo_id)
        return len(sources) - len(imported)

    def set_cover(self, character: Character, photo: CharacterPhoto) -> None:
        character.portrait_photo_id = photo.id
        self._characters.set_portrait(character.id, photo.id)

    def remove_photo(self, character: Character, photo: CharacterPhoto) -> None:
        self._storage.remove(photo)
        if character.portrait_photo_id == photo.id:
            character.portrait_photo_id = None

    def set_caption(self, photo: CharacterPhoto, caption: str) -> None:
        photo.caption = caption.strip() or None
        self._photos.set_caption(photo.id, photo.caption)

    def portrait_path(self, character: Character) -> str | None:
        return self._pick_portrait(self.photos(character.id), character)

    # ------------------------------------------------------------------- export

    def archive(self, character_id: int) -> CharacterArchive | None:
        character = self._characters.get(character_id)
        if character is None:
            return None
        return CharacterArchive(
            character, self.entries(character_id), self.photos(character_id)
        )

    def suggested_pdf_name(self, character: Character) -> str:
        return self._pdf.suggested_file_name(character.name)

    def export_pdf(self, character_id: int, destination: str | Path) -> Path | None:
        archive = self.archive(character_id)
        if archive is None:
            return None
        return self._pdf.export(archive, destination)


