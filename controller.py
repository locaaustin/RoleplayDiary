"""
Roleplay Diary - CONTROLLER
===========================

The middle. It owns the running state of the app - what is searched for, which
filter is on, which character is open - and it is the only place that knows about
both of the other two files.

The shape of every handler here is the same, and deliberately so:

    the view reports what the player did
        -> ask the model to change something, or ask the view a question
        -> tell the view what to draw now

The view never reaches the model, and the model never knows a window exists.

Run the app with:  python controller.py
"""

from __future__ import annotations

import sys
import traceback
from datetime import datetime
from pathlib import Path

from model import APP_VERSION, Character, CharacterPhoto, CharacterStatus, Diary, DiaryEntry
from view import Ask, MainView


class DiaryController:
    """Wires one :class:`Diary` to one :class:`MainView` and keeps them in step."""

    def __init__(self, root: Path | None = None) -> None:
        self.diary = Diary(root)
        self.view = MainView()

        # The running state of the app lives here, not in the view.
        self._selected_id: int | None = None
        self._search = ""
        self._status_filter: CharacterStatus | None = None

        self._connect()
        self._install_crash_handler()

    # ------------------------------------------------------------------ wiring

    def _connect(self) -> None:
        """Fill in every handler the view declares. See view.Handlers."""
        on = self.view.on
        on.search = self._on_search
        on.filter = self._on_filter
        on.select = self._on_select
        on.new_character = self._on_new_character
        on.edit_profile = self._on_edit_profile
        on.delete_character = self._on_delete_character
        on.change_status = self._on_change_status
        on.save_background = self._on_save_background
        on.add_entry = self._on_add_entry
        on.edit_entry = self._on_edit_entry
        on.delete_entry = self._on_delete_entry
        on.add_photos = self._on_add_photos
        on.set_cover = self._on_set_cover
        on.remove_photo = self._on_remove_photo
        on.set_caption = self._on_set_caption
        on.export_pdf = self._on_export_pdf

    def start(self) -> int:
        self.diary.open()
        self._refresh()
        self.view.run()
        self.diary.close()
        return 0

    # ----------------------------------------------------------------- drawing

    @property
    def _character(self) -> Character | None:
        return self.diary.character(self._selected_id) if self._selected_id else None

    def _refresh(self, keep_tab: bool = True) -> None:
        """
        Re-read the roster, apply the search and filter, and redraw both panes. Every
        handler ends here, so there is one path that decides what is on screen.
        """
        everyone = self.diary.roster()
        visible = [item for item in everyone if self._matches(item)]

        # A character can be edited out of the current filter; close them if so.
        if self._selected_id is not None and all(
            item.id != self._selected_id for item in visible
        ):
            self._selected_id = None

        self.view.show_roster(visible, self._selected_id, len(everyone))

        character = self._character
        if character is not None:
            self.view.show_character(
                character,
                self.diary.entries(character.id),
                self.diary.photos(character.id),
                self.diary.portrait_path(character),
                keep_tab=keep_tab,
            )
            return

        self.view.show_placeholder(*self._placeholder_text(len(everyone), len(visible)))

    def _matches(self, item) -> bool:
        if self._status_filter is not None and item.status != self._status_filter:
            return False
        return item.character.matches(self._search)

    @staticmethod
    def _placeholder_text(total: int, visible: int) -> tuple[str, str]:
        if total == 0:
            return (
                "Your diary is empty",
                "Start with a name. Age, server, background and photos can all come "
                "later, as the character gets played.",
            )
        if visible == 0:
            return (
                "Nothing matches",
                "No character fits that search and filter. Clear them to see everyone "
                "again.",
            )
        return ("No character open", "Pick someone from the list to read their story.")

    # ------------------------------------------------------------- the sidebar

    def _on_search(self, text: str) -> None:
        self._search = text
        self._refresh()

    def _on_filter(self, status: CharacterStatus | None) -> None:
        self._status_filter = status
        self._refresh()

    def _on_select(self, character_id: int) -> None:
        if character_id == self._selected_id:
            return
        self._selected_id = character_id
        # A different character starts on their Story tab, not wherever the last one was.
        self._refresh(keep_tab=False)

    # ---------------------------------------------------------- the characters

    def _on_new_character(self) -> None:
        fields = self.view.ask_new_character()
        if fields is None:
            return

        photos = fields.pop("photos", [])
        character = self.diary.create_character(fields, photos)

        # Clear the filters, so the character they just made is certainly on screen.
        self._search = ""
        self._status_filter = None
        self.view.clear_search()
        self.view.set_filter(None)

        self._selected_id = character.id
        self._refresh(keep_tab=False)
        self._warn_about_skipped(len(photos) - len(self.diary.photos(character.id)))

    def _on_edit_profile(self) -> None:
        character = self._character
        if character is None:
            return

        fields = self.view.ask_edit_character(
            character, self.diary.portrait_path(character)
        )
        if fields is None:
            return

        photos = fields.pop("photos", [])
        skipped = self.diary.update_character(character, fields, photos)
        self._refresh()
        self._warn_about_skipped(skipped)

    def _on_delete_character(self) -> None:
        character = self._character
        if character is None:
            return

        confirmed = self.view.confirm(
            "Delete character",
            f"Delete {character.name} along with every story entry and photo?\n\n"
            "This cannot be undone.",
        )
        if not confirmed:
            return

        self.diary.delete_character(character.id)
        self._selected_id = None
        self._refresh()

    def _on_change_status(self, status: CharacterStatus) -> None:
        character = self._character
        if character is None or character.status == status:
            return

        self.diary.set_status(character, status)
        self._refresh()

    def _on_save_background(self, text: str) -> None:
        character = self._character
        if character is None:
            return

        self.diary.set_background(character, text)
        self._refresh()

    # --------------------------------------------------------------- the story

    def _on_add_entry(self) -> None:
        character = self._character
        if character is None:
            return

        fields = self.view.ask_new_entry()
        if fields is None:
            return

        self.diary.add_entry(character.id, fields)
        self._refresh()

    def _on_edit_entry(self, entry: DiaryEntry) -> None:
        fields = self.view.ask_edit_entry(entry)
        if fields is None:
            return

        self.diary.update_entry(entry, fields)
        self._refresh()

    def _on_delete_entry(self, entry: DiaryEntry) -> None:
        character = self._character
        if character is None:
            return

        confirmed = self.view.confirm(
            "Delete entry",
            f"Delete “{entry.title}” from {character.name}'s story?\n\n"
            "This cannot be undone.",
        )
        if not confirmed:
            return

        self.diary.delete_entry(entry.id)
        self._refresh()

    # ------------------------------------------------------------- the gallery

    def _on_add_photos(self) -> None:
        character = self._character
        if character is None:
            return

        chosen = self.view.pick_images()
        if not chosen:
            return

        skipped = self.diary.add_photos(character, chosen)
        self._refresh()
        self._warn_about_skipped(skipped)

    def _on_set_cover(self, photo: CharacterPhoto) -> None:
        character = self._character
        if character is None:
            return

        self.diary.set_cover(character, photo)
        self._refresh()

    def _on_remove_photo(self, photo: CharacterPhoto) -> None:
        character = self._character
        if character is None:
            return

        confirmed = self.view.confirm(
            "Remove photo",
            "Remove this photo from the diary?\n\nThis cannot be undone.",
            confirm_label="Remove",
        )
        if not confirmed:
            return

        self.diary.remove_photo(character, photo)
        self._refresh()

    def _on_set_caption(self, photo: CharacterPhoto, caption: str) -> None:
        """
        Saved quietly as the player clicks away. No redraw: rebuilding the gallery
        here would snatch the box out from under them as they tab to the next one.
        """
        if caption.strip() != (photo.caption or ""):
            self.diary.set_caption(photo, caption)

    def _warn_about_skipped(self, skipped: int) -> None:
        if skipped > 0:
            self.view.inform(
                "Roleplay Diary",
                f"{skipped} file(s) were skipped because Roleplay Diary cannot read them.",
            )

    # ---------------------------------------------------------------- the export

    def _on_export_pdf(self) -> None:
        character = self._character
        if character is None:
            return

        destination = self.view.pick_pdf_destination(
            str(self.diary.paths.exports), self.diary.suggested_pdf_name(character)
        )
        if destination is None:
            return

        try:
            written = self.diary.export_pdf(character.id, destination)
        except OSError as error:
            self.view.inform(
                "Export failed",
                f"Could not write the PDF.\n\n{error}\n\n"
                "If the file is open in a viewer, close it and try again.",
            )
            return

        if written is None:
            return

        if self.view.offer("Export complete", f"Saved to:\n{written}", "Open it", "Close"):
            self._open_in_shell(written)

    @staticmethod
    def _open_in_shell(path: Path) -> None:
        import os
        import subprocess

        try:
            if sys.platform.startswith("win"):
                os.startfile(str(path))  # noqa: S606  (Windows' own file association)
            else:
                subprocess.Popen(["xdg-open", str(path)])
        except OSError:
            pass

    # --------------------------------------------------------------- crashes

    def _install_crash_handler(self) -> None:
        """
        An unhandled error should leave a file a bug report can be built from, not
        just a window that vanishes. tkinter swallows errors raised inside a button
        press, so that path is redirected here too.
        """

        def handle(kind, error, trace) -> None:
            if issubclass(kind, KeyboardInterrupt):
                sys.__excepthook__(kind, error, trace)
                return

            self._log_crash("".join(traceback.format_exception(kind, error, trace)))
            Ask.crashed(f"{kind.__name__}: {error}", str(self.diary.paths.crash_log))

        sys.excepthook = handle
        self.view.report_callback_exception = handle

    def _log_crash(self, detail: str) -> None:
        try:
            with self.diary.paths.crash_log.open("a", encoding="utf-8") as log:
                log.write(f"\n===== {datetime.now():%Y-%m-%d %H:%M:%S} (v{APP_VERSION}) =====\n")
                log.write(detail)
        except OSError:
            # Nothing useful left to do if even the log cannot be written.
            pass


def main(argv: list[str] | None = None) -> int:
    """Entry point. Takes an optional folder to keep the diary in, for testing."""
    argv = argv if argv is not None else sys.argv[1:]
    root = Path(argv[0]) if argv else None
    return DiaryController(root).start()


if __name__ == "__main__":
    raise SystemExit(main())
