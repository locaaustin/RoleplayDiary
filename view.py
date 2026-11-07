"""
Roleplay Diary - VIEW
=====================

Everything you can see, and nothing else. Built on tkinter, which ships with Python,
so the app needs no GUI package installed.

The view is **passive**. It draws what it is given and reports what the player did by
calling the handlers in :attr:`MainView.on`. It never opens the database, never writes
a file, and never decides what should happen next - that is the controller's job.

It does read the model's objects in order to display them, and borrows the model's
vocabulary (the status and entry-kind labels, the date reader). That is the normal
direction: the view knows about the model, the model knows nothing about the view.
"""

from __future__ import annotations

import tkinter as tk
from tkinter import filedialog, font as tkfont, messagebox
from typing import Callable, Sequence

from PIL import Image as PilImage
from PIL import ImageDraw, ImageOps, ImageTk

from model import (
    APP_VERSION,
    Character,
    CharacterPhoto,
    CharacterStatus,
    DiaryEntry,
    EntryKind,
    FlexibleDate,
    PhotoStorage,
    RosterItem,
)


# =============================================================================
#  Look
# =============================================================================


class Palette:
    """
    Every colour in the app. Names describe the role, not the colour, so a light
    theme means editing this class and nothing else.
    """

    WINDOW = "#0E0F13"
    SURFACE = "#15171D"
    SURFACE_RAISED = "#1C1F27"
    SURFACE_HOVER = "#232733"
    BORDER = "#272B36"
    BORDER_STRONG = "#39404F"

    TEXT = "#ECEEF3"
    TEXT_MUTED = "#8B91A1"
    TEXT_FAINT = "#676E80"

    ACCENT = "#7C5CFF"
    ACCENT_HOVER = "#8E74FF"
    ACCENT_SOFT = "#231E3D"

    ALIVE = "#3ECF8E"
    DEAD = "#F05365"
    DANGER_SOFT = "#2A1A1F"
    SHELVED = "#E8A33D"

    _STATUS = {
        CharacterStatus.ALIVE: ALIVE,
        CharacterStatus.DEAD: DEAD,
        CharacterStatus.SHELVED: SHELVED,
    }

    @classmethod
    def for_status(cls, status: CharacterStatus) -> str:
        return cls._STATUS.get(status, cls.TEXT_MUTED)


class Fonts:
    """
    Named fonts, built once. tkinter needs a window to exist before a font can be
    made, so :meth:`build` is called by the main window and not at import time.
    """

    FAMILY = "Segoe UI"

    display: tkfont.Font
    title: tkfont.Font
    heading: tkfont.Font
    body: tkfont.Font
    small: tkfont.Font
    label: tkfont.Font
    tiny: tkfont.Font

    @classmethod
    def build(cls) -> None:
        cls.display = tkfont.Font(family=cls.FAMILY, size=20, weight="bold")
        cls.title = tkfont.Font(family=cls.FAMILY, size=14, weight="bold")
        cls.heading = tkfont.Font(family=cls.FAMILY, size=10, weight="bold")
        cls.body = tkfont.Font(family=cls.FAMILY, size=10)
        cls.small = tkfont.Font(family=cls.FAMILY, size=9)
        cls.label = tkfont.Font(family=cls.FAMILY, size=8, weight="bold")
        cls.tiny = tkfont.Font(family=cls.FAMILY, size=8)


def rounded_rect(canvas: tk.Canvas, x0, y0, x1, y1, radius, **options):
    """
    A rounded rectangle on a Canvas. tkinter has no such shape, so this is a polygon
    with its corners smoothed - the standard trick, and the reason buttons and badges
    in this app can have soft corners at all.
    """
    radius = max(0, min(radius, (x1 - x0) / 2, (y1 - y0) / 2))
    points = [
        x0 + radius, y0, x1 - radius, y0, x1, y0,
        x1, y0 + radius, x1, y1 - radius, x1, y1,
        x1 - radius, y1, x0 + radius, y1, x0, y1,
        x0, y1 - radius, x0, y0 + radius, x0, y0,
    ]
    return canvas.create_polygon(points, smooth=True, **options)


class Images:
    """
    Loads pictures for display, with their corners rounded.

    Two tkinter facts shape this class. It cannot show PNG or JPEG without Pillow,
    and it throws away any image you do not keep a reference to - hence the cache,
    which is holding those references as much as it is saving work.
    """

    _cache: dict[tuple, ImageTk.PhotoImage] = {}
    _CACHE_LIMIT = 160

    @classmethod
    def rounded(
        cls, path: str | None, width: int, height: int, radius: int, behind: str
    ) -> ImageTk.PhotoImage | None:
        """
        Centre-cropped to the given box with rounded corners. ``behind`` is the colour
        the corners are blended into, since tkinter labels cannot do transparency.
        Returns None if the file is missing or unreadable.
        """
        if not path:
            return None

        try:
            stamp = (path, width, height, radius, behind)
        except TypeError:
            return None

        if stamp in cls._cache:
            return cls._cache[stamp]

        try:
            with PilImage.open(path) as handle:
                picture = ImageOps.exif_transpose(handle).convert("RGB")
        except Exception:
            return None

        scale = max(width / picture.width, height / picture.height)
        picture = picture.resize(
            (max(1, round(picture.width * scale)), max(1, round(picture.height * scale))),
            PilImage.LANCZOS,
        )
        left = max(0, (picture.width - width) // 2)
        top = max(0, (picture.height - height) // 2)
        picture = picture.crop((left, top, left + width, top + height))

        mask = PilImage.new("L", (width, height), 0)
        ImageDraw.Draw(mask).rounded_rectangle(
            (0, 0, width - 1, height - 1), radius=radius, fill=255
        )
        plate = PilImage.new("RGB", (width, height), behind)
        plate.paste(picture, (0, 0), mask)

        if len(cls._cache) > cls._CACHE_LIMIT:
            cls._cache.clear()

        photo = ImageTk.PhotoImage(plate)
        cls._cache[stamp] = photo
        return photo

    @classmethod
    def forget(cls) -> None:
        cls._cache.clear()


# =============================================================================
#  Scrolling
# =============================================================================


class SlimScrollbar(tk.Canvas):
    """A thin scrollbar drawn by hand, because the stock one cannot be themed."""

    THICKNESS = 10
    MIN_THUMB = 30

    def __init__(self, parent: tk.Misc, command: Callable, background: str) -> None:
        super().__init__(
            parent, width=self.THICKNESS, highlightthickness=0, bd=0, bg=background
        )
        self._command = command
        self._first = 0.0
        self._last = 1.0
        self._hovering = False

        self.bind("<Configure>", lambda _event: self._redraw())
        self.bind("<Button-1>", self._jump)
        self.bind("<B1-Motion>", self._jump)
        self.bind("<Enter>", self._enter)
        self.bind("<Leave>", self._leave)

    def set(self, first: str, last: str) -> None:
        """Called by the canvas it scrolls; the signature is tkinter's, not ours."""
        self._first, self._last = float(first), float(last)
        self._redraw()

    @property
    def needed(self) -> bool:
        return (self._last - self._first) < 0.999

    def _enter(self, _event) -> None:
        self._hovering = True
        self._redraw()

    def _leave(self, _event) -> None:
        self._hovering = False
        self._redraw()

    def _redraw(self) -> None:
        self.delete("all")
        if not self.needed:
            return

        height = self.winfo_height()
        top = self._first * height
        bottom = max(self._last * height, top + self.MIN_THUMB)
        colour = Palette.TEXT_FAINT if self._hovering else Palette.BORDER_STRONG
        rounded_rect(self, 2, top + 1, self.THICKNESS - 2, bottom - 1, 3,
                     fill=colour, outline="")

    def _jump(self, event) -> None:
        if not self.needed:
            return
        height = self.winfo_height() or 1
        span = self._last - self._first
        target = (event.y / height) - span / 2
        self._command("moveto", max(0.0, min(1.0 - span, target)))


class ScrollFrame(tk.Frame):
    """
    A scrolling area. tkinter has no such widget, so this is the usual arrangement:
    a canvas with a frame inside it, kept the right size, plus our own scrollbar.

    Put content into :attr:`body`.
    """

    def __init__(self, parent: tk.Misc, background: str) -> None:
        super().__init__(parent, bg=background)
        self._background = background

        self.canvas = tk.Canvas(self, bg=background, highlightthickness=0, bd=0)
        self.scrollbar = SlimScrollbar(self, self.canvas.yview, background)
        self.canvas.configure(yscrollcommand=self.scrollbar.set)

        self.body = tk.Frame(self.canvas, bg=background)
        self._window = self.canvas.create_window((0, 0), window=self.body, anchor="nw")

        self.body.bind("<Configure>", self._on_body_resize)
        self.canvas.bind("<Configure>", self._on_canvas_resize)

        self.scrollbar.pack(side="right", fill="y", pady=2)
        self.canvas.pack(side="left", fill="both", expand=True)

    def _on_body_resize(self, _event) -> None:
        self.canvas.configure(scrollregion=self.canvas.bbox("all"))

    def _on_canvas_resize(self, event) -> None:
        # The inner frame always spans the full width; only height scrolls.
        self.canvas.itemconfigure(self._window, width=event.width)

    def clear(self) -> None:
        for child in self.body.winfo_children():
            child.destroy()
        self.canvas.yview_moveto(0)

    def on_wheel(self, event) -> None:
        if self.scrollbar.needed:
            self.canvas.yview_scroll(int(-event.delta / 120), "units")

    @staticmethod
    def route_wheel(root: tk.Misc) -> Callable:
        """
        One wheel handler for the whole window. Finds whatever is under the pointer
        and walks up to the scrolling area that contains it, so the wheel works over
        any widget without every widget having to bind it.
        """

        def handle(event) -> None:
            widget = root.winfo_containing(event.x_root, event.y_root)
            while widget is not None:
                if isinstance(widget, ScrollFrame):
                    widget.on_wheel(event)
                    return
                widget = getattr(widget, "master", None)

        return handle


# =============================================================================
#  Controls
# =============================================================================


class Button(tk.Canvas):
    """
    A flat button with rounded corners, drawn rather than built, because tkinter's
    own button cannot be shaped and barely accepts colour on Windows.
    """

    #            fill             hover             text               outline
    VARIANTS = {
        "primary":   (Palette.ACCENT, Palette.ACCENT_HOVER, "#FFFFFF", Palette.ACCENT),
        "secondary": (Palette.SURFACE_RAISED, Palette.SURFACE_HOVER, Palette.TEXT,
                      Palette.BORDER_STRONG),
        "ghost":     ("", Palette.SURFACE_HOVER, Palette.TEXT_MUTED, ""),
        "danger":    ("", Palette.DANGER_SOFT, Palette.DEAD, ""),
    }

    def __init__(
        self,
        parent: tk.Misc,
        text: str,
        command: Callable[[], None],
        variant: str = "secondary",
        background: str = Palette.WINDOW,
        padding: tuple[int, int] = (16, 9),
        font: tkfont.Font | None = None,
        width: int | None = None,
    ) -> None:
        self._font = font or Fonts.heading
        self._text = text
        self._command = command
        self._fill, self._hover, self._ink, self._outline = self.VARIANTS[variant]
        self._enabled = True
        self._pressed = False
        self._hovered = False

        pad_x, pad_y = padding
        self._width = width or self._font.measure(text) + pad_x * 2
        self._height = self._font.metrics("linespace") + pad_y * 2

        super().__init__(
            parent, width=self._width, height=self._height,
            highlightthickness=0, bd=0, bg=background,
        )
        self.configure(cursor="hand2")

        self.bind("<Enter>", self._on_enter)
        self.bind("<Leave>", self._on_leave)
        self.bind("<Button-1>", self._on_press)
        self.bind("<ButtonRelease-1>", self._on_release)

        self._draw()

    def set_text(self, text: str) -> None:
        self._text = text
        self._draw()

    def set_enabled(self, enabled: bool) -> None:
        self._enabled = enabled
        self.configure(cursor="hand2" if enabled else "arrow")
        self._draw()

    def _background_now(self) -> str:
        if not self._enabled:
            return Palette.ACCENT_SOFT if self._fill else ""
        if self._pressed or self._hovered:
            return self._hover
        return self._fill

    def _draw(self) -> None:
        self.delete("all")
        fill = self._background_now()
        if fill or self._outline:
            rounded_rect(
                self, 1, 1, self._width - 1, self._height - 1, 8,
                fill=fill or self.cget("bg"),
                outline=self._outline if self._enabled else Palette.BORDER,
            )
        ink = self._ink if self._enabled else Palette.TEXT_FAINT
        self.create_text(
            self._width / 2, self._height / 2, text=self._text, fill=ink, font=self._font
        )

    def _on_enter(self, _event) -> None:
        self._hovered = True
        self._draw()

    def _on_leave(self, _event) -> None:
        self._hovered = self._pressed = False
        self._draw()

    def _on_press(self, _event) -> None:
        self._pressed = True
        self._draw()

    def _on_release(self, _event) -> None:
        was_pressed, self._pressed = self._pressed, False
        self._draw()
        if was_pressed and self._enabled:
            self._command()


class Chip(tk.Canvas):
    """A small pill that is either on or off. Used for the status filters."""

    def __init__(
        self,
        parent: tk.Misc,
        text: str,
        value: object,
        command: Callable[["Chip"], None],
        background: str,
    ) -> None:
        self.value = value
        self._text = text
        self._command = command
        self._checked = False

        self._width = Fonts.label.measure(text) + 26
        self._height = Fonts.label.metrics("linespace") + 12

        super().__init__(
            parent, width=self._width, height=self._height,
            highlightthickness=0, bd=0, bg=background, cursor="hand2",
        )
        self.bind("<Button-1>", lambda _event: self._command(self))
        self._draw()

    @property
    def checked(self) -> bool:
        return self._checked

    def set_checked(self, checked: bool) -> None:
        self._checked = checked
        self._draw()

    def _draw(self) -> None:
        self.delete("all")
        fill = Palette.ACCENT_SOFT if self._checked else ""
        outline = Palette.ACCENT if self._checked else Palette.BORDER
        ink = Palette.TEXT if self._checked else Palette.TEXT_MUTED

        rounded_rect(
            self, 1, 1, self._width - 1, self._height - 1, self._height / 2,
            fill=fill or self.cget("bg"), outline=outline,
        )
        self.create_text(
            self._width / 2, self._height / 2, text=self._text, fill=ink, font=Fonts.label
        )


class Pill(tk.Canvas):
    """A read-only badge: a status, or the word Cover on a gallery photo."""

    def __init__(self, parent: tk.Misc, text: str, colour: str, background: str) -> None:
        width = Fonts.label.measure(text) + 22
        height = Fonts.label.metrics("linespace") + 8
        super().__init__(
            parent, width=width, height=height, highlightthickness=0, bd=0, bg=background
        )
        rounded_rect(self, 1, 1, width - 1, height - 1, height / 2,
                     fill=background, outline=colour)
        self.create_text(width / 2, height / 2, text=text, fill=colour, font=Fonts.label)


class Dot(tk.Canvas):
    """A status marker for the roster, where a whole pill would shout."""

    def __init__(self, parent: tk.Misc, colour: str, background: str) -> None:
        super().__init__(parent, width=8, height=8, highlightthickness=0, bd=0, bg=background)
        self.create_oval(1, 1, 7, 7, fill=colour, outline="")


class Avatar(tk.Canvas):
    """A character's portrait, or their initials when there is no photo yet."""

    def __init__(self, parent: tk.Misc, size: int, radius: int, background: str) -> None:
        super().__init__(
            parent, width=size, height=size, highlightthickness=0, bd=0, bg=background
        )
        self._size = size
        self._radius = radius
        self._background = background
        self._photo: ImageTk.PhotoImage | None = None

    def show(self, initials: str, portrait_path: str | None) -> None:
        self.delete("all")
        self._photo = Images.rounded(
            portrait_path, self._size, self._size, self._radius, self._background
        )

        if self._photo is not None:
            self.create_image(0, 0, image=self._photo, anchor="nw")
            return

        rounded_rect(self, 1, 1, self._size - 1, self._size - 1, self._radius,
                     fill=Palette.SURFACE_RAISED, outline=Palette.BORDER)
        font = tkfont.Font(family=Fonts.FAMILY, size=max(8, self._size // 4), weight="bold")
        self.create_text(
            self._size / 2, self._size / 2, text=initials,
            fill=Palette.TEXT_FAINT, font=font,
        )


class Field(tk.Entry):
    """A single-line input, with placeholder text tkinter does not provide itself."""

    def __init__(self, parent: tk.Misc, placeholder: str = "", width: int = 20) -> None:
        super().__init__(
            parent,
            width=width,
            font=Fonts.body,
            bg=Palette.SURFACE_RAISED,
            fg=Palette.TEXT,
            disabledbackground=Palette.SURFACE_RAISED,
            insertbackground=Palette.TEXT,
            selectbackground=Palette.ACCENT,
            selectforeground="#FFFFFF",
            relief="flat",
            highlightthickness=1,
            highlightbackground=Palette.BORDER,
            highlightcolor=Palette.ACCENT,
        )
        self._placeholder = placeholder
        self._showing_placeholder = False

        self.bind("<FocusIn>", self._on_focus_in)
        self.bind("<FocusOut>", self._on_focus_out)
        self._show_placeholder()

    def _show_placeholder(self) -> None:
        if self._placeholder and not super().get():
            self._showing_placeholder = True
            self.insert(0, self._placeholder)
            self.configure(fg=Palette.TEXT_FAINT)

    def _on_focus_in(self, _event) -> None:
        if self._showing_placeholder:
            self.delete(0, "end")
            self.configure(fg=Palette.TEXT)
            self._showing_placeholder = False

    def _on_focus_out(self, _event) -> None:
        self._show_placeholder()

    def value(self) -> str:
        return "" if self._showing_placeholder else super().get().strip()

    def set_value(self, text: str) -> None:
        self._showing_placeholder = False
        self.delete(0, "end")
        self.configure(fg=Palette.TEXT)
        if text:
            self.insert(0, text)
        else:
            self._show_placeholder()


class TextArea(tk.Text):
    """A multi-line input, styled to match :class:`Field`."""

    def __init__(self, parent: tk.Misc, placeholder: str = "", height: int = 7) -> None:
        super().__init__(
            parent,
            height=height,
            wrap="word",
            font=Fonts.body,
            bg=Palette.SURFACE_RAISED,
            fg=Palette.TEXT,
            insertbackground=Palette.TEXT,
            selectbackground=Palette.ACCENT,
            selectforeground="#FFFFFF",
            relief="flat",
            padx=9,
            pady=7,
            highlightthickness=1,
            highlightbackground=Palette.BORDER,
            highlightcolor=Palette.ACCENT,
        )
        self._placeholder = placeholder
        self._showing_placeholder = False

        self.bind("<FocusIn>", self._on_focus_in)
        self.bind("<FocusOut>", self._on_focus_out)
        self._show_placeholder()

    def _show_placeholder(self) -> None:
        if self._placeholder and not super().get("1.0", "end-1c"):
            self._showing_placeholder = True
            self.insert("1.0", self._placeholder)
            self.configure(fg=Palette.TEXT_FAINT)

    def _on_focus_in(self, _event) -> None:
        if self._showing_placeholder:
            self.delete("1.0", "end")
            self.configure(fg=Palette.TEXT)
            self._showing_placeholder = False

    def _on_focus_out(self, _event) -> None:
        self._show_placeholder()

    def value(self) -> str:
        return "" if self._showing_placeholder else super().get("1.0", "end-1c").strip()

    def set_value(self, text: str) -> None:
        self._showing_placeholder = False
        self.delete("1.0", "end")
        self.configure(fg=Palette.TEXT)
        if text:
            self.insert("1.0", text)
        else:
            self._show_placeholder()


class Dropdown(tk.Frame):
    """
    A themed picker over a LabelledEnum. tkinter's own combobox refuses most colour
    on Windows, so this is a menu button wearing a border, with the chevron drawn as
    a label beside it.
    """

    def __init__(
        self,
        parent: tk.Misc,
        enum_class: type,
        on_change: Callable[[object], None] | None = None,
        width: int = 14,
    ) -> None:
        super().__init__(
            parent,
            bg=Palette.SURFACE_RAISED,
            highlightthickness=1,
            highlightbackground=Palette.BORDER,
            bd=0,
        )
        self._enum_class = enum_class
        self._on_change = on_change
        self._value = next(iter(enum_class))
        self._variable = tk.StringVar(value=self._value.label)

        self._button = tk.Menubutton(
            self,
            textvariable=self._variable,
            font=Fonts.body,
            bg=Palette.SURFACE_RAISED,
            fg=Palette.TEXT,
            activebackground=Palette.SURFACE_HOVER,
            activeforeground=Palette.TEXT,
            relief="flat",
            anchor="w",
            padx=10,
            pady=6,
            width=width,
            cursor="hand2",
            highlightthickness=0,
            indicatoron=False,
        )
        self._button.pack(side="left", fill="x", expand=True)

        self._chevron = tk.Label(
            self, text="▾", font=Fonts.tiny, bg=Palette.SURFACE_RAISED,
            fg=Palette.TEXT_MUTED, padx=9, cursor="hand2",
        )
        self._chevron.pack(side="right", fill="y")
        self._chevron.bind("<Button-1>", self._popup)

        self._menu = tk.Menu(
            self._button,
            tearoff=0,
            font=Fonts.body,
            bg=Palette.SURFACE_RAISED,
            fg=Palette.TEXT,
            activebackground=Palette.ACCENT,
            activeforeground="#FFFFFF",
            relief="flat",
            borderwidth=0,
        )
        for member in enum_class.options():
            self._menu.add_command(
                label=member.label, command=lambda chosen=member: self._choose(chosen)
            )
        self._button.configure(menu=self._menu)

    def _popup(self, _event) -> None:
        self._menu.tk_popup(self.winfo_rootx(), self.winfo_rooty() + self.winfo_height())

    def _choose(self, member) -> None:
        self._value = member
        self._variable.set(member.label)
        if self._on_change is not None:
            self._on_change(member)

    def value(self):
        return self._value

    def set_value(self, member, notify: bool = False) -> None:
        self._value = member
        self._variable.set(member.label)
        if notify and self._on_change is not None:
            self._on_change(member)

    def tint(self, colour: str) -> None:
        """Used by the hero, so the status reads in its own colour."""
        self._button.configure(fg=colour)


def label(
    parent: tk.Misc,
    text: str = "",
    font: tkfont.Font | None = None,
    colour: str = Palette.TEXT,
    background: str = Palette.WINDOW,
    wrap: int = 0,
    anchor: str = "w",
) -> tk.Label:
    """Shorthand for the dozens of plain text labels the screens are made of."""
    return tk.Label(
        parent,
        text=text,
        font=font or Fonts.body,
        fg=colour,
        bg=background,
        wraplength=wrap,
        justify="left",
        anchor=anchor,
    )


def elide(text: str, font: tkfont.Font, width: int) -> str:
    """
    Trim text to fit, ending in an ellipsis. tkinter labels clip mid-letter instead,
    which looks like a rendering fault rather than a deliberate shortening.
    """
    if font.measure(text) <= width:
        return text

    trimmed = text
    while trimmed and font.measure(trimmed + "…") > width:
        trimmed = trimmed[:-1]
    return trimmed.rstrip() + "…"


class FieldBlock(tk.Frame):
    """
    A small-caps caption above an input - the shape of every form row in the app.

    The input is created with the block as its parent, not moved into it afterwards:
    a tkinter widget belongs to whatever it was built inside, for good.
    """

    def __init__(self, parent: tk.Misc, caption: str, background: str = Palette.WINDOW) -> None:
        super().__init__(parent, bg=background)
        label(self, caption.upper(), Fonts.label, Palette.TEXT_MUTED, background).pack(
            anchor="w", pady=(0, 5)
        )


class Card(tk.Frame):
    """A panel with a hairline border. Cards never nest."""

    def __init__(self, parent: tk.Misc, background: str = Palette.SURFACE) -> None:
        super().__init__(
            parent,
            bg=background,
            highlightthickness=1,
            highlightbackground=Palette.BORDER,
            bd=0,
        )
        self.background = background


class TabBar(tk.Frame):
    """Underlined tabs. The selected one is view state, so the view owns it."""

    def __init__(
        self,
        parent: tk.Misc,
        names: Sequence[str],
        on_change: Callable[[int], None],
        background: str,
    ) -> None:
        super().__init__(parent, bg=background)
        self._on_change = on_change
        self._selected = 0
        self._labels: list[tk.Label] = []
        self._underlines: list[tk.Frame] = []

        for index, name in enumerate(names):
            holder = tk.Frame(self, bg=background)
            holder.pack(side="left", padx=(0, 24))

            text = label(holder, name, Fonts.heading, Palette.TEXT_MUTED, background)
            text.pack()
            text.configure(cursor="hand2")
            text.bind("<Button-1>", lambda _event, i=index: self.select(i))

            underline = tk.Frame(holder, height=2, bg=background)
            underline.pack(fill="x", pady=(7, 0))

            self._labels.append(text)
            self._underlines.append(underline)

        self._paint()

    def set_names(self, names: Sequence[str]) -> None:
        for text, name in zip(self._labels, names):
            text.configure(text=name)

    def select(self, index: int, notify: bool = True) -> None:
        self._selected = index
        self._paint()
        if notify:
            self._on_change(index)

    def _paint(self) -> None:
        for index, (text, underline) in enumerate(zip(self._labels, self._underlines)):
            chosen = index == self._selected
            text.configure(fg=Palette.TEXT if chosen else Palette.TEXT_MUTED)
            underline.configure(bg=Palette.ACCENT if chosen else text.master.cget("bg"))


class EmptyState(tk.Frame):
    """Shown wherever a list has nothing in it. Says what to do next."""

    def __init__(
        self,
        parent: tk.Misc,
        title: str,
        subtitle: str = "",
        action: str = "",
        command: Callable[[], None] | None = None,
        background: str = Palette.WINDOW,
    ) -> None:
        super().__init__(parent, bg=background)

        label(self, title, Fonts.title, Palette.TEXT, background, anchor="center").pack(
            pady=(0, 8)
        )
        if subtitle:
            label(
                self, subtitle, Fonts.body, Palette.TEXT_MUTED, background,
                wrap=380, anchor="center",
            ).pack()
        if action and command is not None:
            Button(self, action, command, "primary", background).pack(pady=(18, 0))


# =============================================================================
#  Windows that open on top
# =============================================================================


def darken_title_bar(window: tk.Misc) -> None:
    """
    Windows draws the title bar itself, and by default it draws it light. One call
    per window makes it match the app. A no-op anywhere but Windows.
    """
    try:
        import ctypes

        window.update_idletasks()
        handle = int(window.wm_frame(), 16)
        value = ctypes.c_int(1)
        ctypes.windll.dwmapi.DwmSetWindowAttribute(
            ctypes.c_void_p(handle), ctypes.c_int(20),
            ctypes.byref(value), ctypes.sizeof(value),
        )
    except Exception:
        # Cosmetic only - an older build simply keeps the light bar.
        pass


class ModalWindow(tk.Toplevel):
    """Shared behaviour for every window that opens over the main one."""

    def __init__(self, parent: tk.Misc, title: str) -> None:
        super().__init__(parent, bg=Palette.WINDOW)
        self.title(title)
        self.resizable(False, False)
        self.transient(parent if isinstance(parent, tk.Tk) else parent.winfo_toplevel())
        self.protocol("WM_DELETE_WINDOW", self.cancel)
        self.bind("<Escape>", lambda _event: self.cancel())

    def cancel(self) -> None:
        self.destroy()

    def show(self) -> None:
        """Place over the parent, take the focus, and wait until it is closed."""
        self.update_idletasks()
        parent = self.master
        width, height = self.winfo_width(), self.winfo_height()
        x = parent.winfo_rootx() + (parent.winfo_width() - width) // 2
        y = parent.winfo_rooty() + max(20, (parent.winfo_height() - height) // 3)
        self.geometry(f"+{max(0, x)}+{max(0, y)}")

        darken_title_bar(self)
        self.grab_set()
        self.wait_window(self)


class Message(ModalWindow):
    """
    Our own confirm/inform box. tkinter's is a native Windows dialog, which would
    arrive bright white in the middle of a dark app.
    """

    def __init__(
        self,
        parent: tk.Misc,
        title: str,
        body: str,
        confirm_label: str | None,
        dismiss_label: str,
        destructive: bool,
    ) -> None:
        super().__init__(parent, title)
        self.confirmed = False

        frame = tk.Frame(self, bg=Palette.WINDOW)
        frame.pack(fill="both", expand=True, padx=26, pady=22)

        label(frame, title, Fonts.title, Palette.TEXT, Palette.WINDOW).pack(anchor="w")
        label(
            frame, body, Fonts.body, Palette.TEXT_MUTED, Palette.WINDOW, wrap=420
        ).pack(anchor="w", pady=(10, 20))

        buttons = tk.Frame(frame, bg=Palette.WINDOW)
        buttons.pack(anchor="e")

        Button(buttons, dismiss_label, self.cancel, "ghost", Palette.WINDOW).pack(side="left")
        if confirm_label is not None:
            variant = "danger" if destructive else "primary"
            Button(
                buttons, confirm_label, self._accept, variant, Palette.WINDOW
            ).pack(side="left", padx=(8, 0))

    def _accept(self) -> None:
        self.confirmed = True
        self.destroy()


class Ask:
    """Every question the app puts to the player, in one place."""

    @staticmethod
    def confirm(parent: tk.Misc, title: str, body: str, confirm_label: str = "Delete") -> bool:
        dialog = Message(parent, title, body, confirm_label, "Cancel", destructive=True)
        dialog.show()
        return dialog.confirmed

    @staticmethod
    def offer(parent: tk.Misc, title: str, body: str, yes_label: str, no_label: str) -> bool:
        dialog = Message(parent, title, body, yes_label, no_label, destructive=False)
        dialog.show()
        return dialog.confirmed

    @staticmethod
    def inform(parent: tk.Misc, title: str, body: str) -> None:
        Message(parent, title, body, None, "Close", destructive=False).show()

    @staticmethod
    def images(parent: tk.Misc, start_in: str) -> list[str]:
        """The file picker stays native - this is the one place that should be."""
        chosen = filedialog.askopenfilenames(
            parent=parent,
            title="Add photos",
            initialdir=start_in,
            filetypes=PhotoStorage.file_types(),
        )
        return list(chosen)

    @staticmethod
    def pdf_destination(parent: tk.Misc, start_in: str, suggested: str) -> str | None:
        chosen = filedialog.asksaveasfilename(
            parent=parent,
            title="Export character as PDF",
            initialdir=start_in,
            initialfile=suggested,
            defaultextension=".pdf",
            filetypes=[("PDF document", "*.pdf")],
        )
        if not chosen:
            return None
        return chosen if chosen.lower().endswith(".pdf") else chosen + ".pdf"

    @staticmethod
    def crashed(message: str, log_path: str) -> None:
        """Last resort, after the main window may already be gone."""
        messagebox.showerror(
            "Roleplay Diary",
            f"Something went wrong.\n\n{message}\n\n"
            f"The details were written to:\n{log_path}\n\n"
            "Your diary itself is unaffected.",
        )


class CharacterDialog(ModalWindow):
    """
    New and edit share one window. Photos picked here are *staged*, not imported: a
    brand new character has no id to file them under yet, and cancelling must leave
    nothing behind. The controller imports them once the save succeeds.
    """

    PREVIEW_WIDTH = 148
    PREVIEW_HEIGHT = 178

    def __init__(
        self,
        parent: tk.Misc,
        heading: str,
        save_label: str,
        character: Character | None = None,
        portrait_path: str | None = None,
    ) -> None:
        super().__init__(parent, heading)
        self.result: dict | None = None
        self._existing_portrait = portrait_path
        self._pending: list[str] = []
        self._preview_image: ImageTk.PhotoImage | None = None

        self._build(heading, save_label)
        if character is not None:
            self._fill_from(character)
        self._refresh_photos()
        self._refresh_save()

    @classmethod
    def ask_new(cls, parent: tk.Misc) -> dict | None:
        dialog = cls(parent, "New character", "Create character")
        dialog.show()
        return dialog.result

    @classmethod
    def ask_edit(
        cls, parent: tk.Misc, character: Character, portrait_path: str | None
    ) -> dict | None:
        dialog = cls(parent, "Edit character", "Save changes", character, portrait_path)
        dialog.show()
        return dialog.result

    # ------------------------------------------------------------------- build

    def _build(self, heading: str, save_label: str) -> None:
        page = tk.Frame(self, bg=Palette.WINDOW)
        page.pack(fill="both", expand=True, padx=28, pady=(24, 22))

        label(page, heading, Fonts.title, Palette.TEXT, Palette.WINDOW).pack(anchor="w")
        label(
            page,
            "Only the name is required. Everything else can be filled in later, "
            "as the story goes.",
            Fonts.body, Palette.TEXT_MUTED, Palette.WINDOW, wrap=620,
        ).pack(anchor="w", pady=(6, 20))

        top = tk.Frame(page, bg=Palette.WINDOW)
        top.pack(fill="x")
        self._build_photo_column(top).pack(side="left", anchor="n")

        fields = tk.Frame(top, bg=Palette.WINDOW)
        fields.pack(side="left", fill="x", expand=True, padx=(20, 0))

        name_block = FieldBlock(fields, "Name")
        name_block.pack(fill="x")
        self.name_field = Field(name_block, "Who are they?", width=42)
        self.name_field.bind("<KeyRelease>", lambda _event: self._on_name_typed())
        self.name_field.pack(fill="x", ipady=3)

        nickname_block = FieldBlock(fields, "Nickname")
        nickname_block.pack(fill="x", pady=(14, 0))
        self.nickname_field = Field(nickname_block, "What people call them", width=42)
        self.nickname_field.pack(fill="x", ipady=3)

        pair = tk.Frame(fields, bg=Palette.WINDOW)
        pair.pack(fill="x", pady=(14, 0))

        age_block = FieldBlock(pair, "Age")
        age_block.pack(side="left", fill="x", expand=True)
        self.age_field = Field(age_block, "31", width=10)
        self.age_field.configure(
            validate="key", validatecommand=(self.register(self._digits_only), "%P")
        )
        self.age_field.pack(fill="x", ipady=3)

        status_block = FieldBlock(pair, "Status")
        status_block.pack(side="left", fill="x", expand=True, padx=(14, 0))
        self.status_picker = Dropdown(status_block, CharacterStatus, width=12)
        self.status_picker.pack(fill="x")

        server_block = FieldBlock(page, "Server or community")
        server_block.pack(fill="x", pady=(14, 0))
        self.server_field = Field(
            server_block, "GTA World, a fantasy server, a tabletop group"
        )
        self.server_field.pack(fill="x", ipady=3)

        bottom_pair = tk.Frame(page, bg=Palette.WINDOW)
        bottom_pair.pack(fill="x", pady=(14, 0))

        occupation_block = FieldBlock(bottom_pair, "Occupation")
        occupation_block.pack(side="left", fill="x", expand=True)
        self.occupation_field = Field(occupation_block)
        self.occupation_field.pack(fill="x", ipady=3)

        affiliation_block = FieldBlock(bottom_pair, "Faction or affiliation")
        affiliation_block.pack(side="left", fill="x", expand=True, padx=(14, 0))
        self.affiliation_field = Field(affiliation_block, "Faction, family, crew")
        self.affiliation_field.pack(fill="x", ipady=3)

        background_block = FieldBlock(page, "Background")
        background_block.pack(fill="both", expand=True, pady=(14, 0))
        self.background_area = TextArea(
            background_block,
            "Where they came from, and anything that was true before the story started.",
            height=7,
        )
        self.background_area.pack(fill="both", expand=True)

        footer = tk.Frame(page, bg=Palette.WINDOW)
        footer.pack(fill="x", pady=(22, 0))

        self.save_button = Button(footer, save_label, self._save, "primary", Palette.WINDOW)
        self.save_button.pack(side="right")
        Button(footer, "Cancel", self.cancel, "ghost", Palette.WINDOW).pack(
            side="right", padx=(0, 8)
        )

    def _build_photo_column(self, parent: tk.Misc) -> tk.Frame:
        column = tk.Frame(parent, bg=Palette.WINDOW, width=self.PREVIEW_WIDTH)

        self.preview = tk.Canvas(
            column, width=self.PREVIEW_WIDTH, height=self.PREVIEW_HEIGHT,
            highlightthickness=0, bd=0, bg=Palette.WINDOW,
        )
        self.preview.pack()

        self.add_photos_button = Button(
            column, "Add photos", self._on_add_photos, "secondary", Palette.WINDOW,
            width=self.PREVIEW_WIDTH,
        )
        self.add_photos_button.pack(pady=(10, 0))

        self.clear_photos_button = Button(
            column, "Clear", self._on_clear_photos, "ghost", Palette.WINDOW,
            width=self.PREVIEW_WIDTH,
        )

        self.photo_summary = label(
            column, "", Fonts.tiny, Palette.TEXT_MUTED, Palette.WINDOW,
            wrap=self.PREVIEW_WIDTH, anchor="center",
        )
        self.photo_summary.pack(pady=(8, 0))
        return column

    @staticmethod
    def _digits_only(proposed: str) -> bool:
        return proposed == "" or (proposed.isdigit() and len(proposed) <= 3)

    def _fill_from(self, character: Character) -> None:
        self.name_field.set_value(character.name)
        self.nickname_field.set_value(character.nickname or "")
        self.age_field.set_value(str(character.age) if character.age else "")
        self.status_picker.set_value(character.status)
        self.server_field.set_value(character.server or "")
        self.occupation_field.set_value(character.occupation or "")
        self.affiliation_field.set_value(character.affiliation or "")
        self.background_area.set_value(character.background or "")

    # ------------------------------------------------------------------ photos

    @property
    def _preview_path(self) -> str | None:
        return self._pending[0] if self._pending else self._existing_portrait

    def _on_add_photos(self) -> None:
        for chosen in Ask.images(self, str(_last_image_folder())):
            if chosen not in self._pending:
                self._pending.append(chosen)
        if self._pending:
            _remember_image_folder(self._pending[-1])
        self._refresh_photos()

    def _on_clear_photos(self) -> None:
        self._pending.clear()
        self._refresh_photos()

    def _refresh_photos(self) -> None:
        staged = len(self._pending)

        if staged == 0 and self._existing_portrait:
            summary = "Current cover"
        elif staged == 0:
            summary = "Optional - you can add photos later"
        elif staged == 1:
            summary = "1 photo ready"
        else:
            summary = f"{staged} photos ready, first is the cover"

        self.photo_summary.configure(text=summary)

        if staged:
            self.clear_photos_button.pack(after=self.add_photos_button, pady=(4, 0))
        else:
            self.clear_photos_button.pack_forget()

        self._draw_preview()

    def _draw_preview(self) -> None:
        self.preview.delete("all")
        self._preview_image = Images.rounded(
            self._preview_path, self.PREVIEW_WIDTH, self.PREVIEW_HEIGHT, 11, Palette.WINDOW
        )

        if self._preview_image is not None:
            self.preview.create_image(0, 0, image=self._preview_image, anchor="nw")
            return

        rounded_rect(
            self.preview, 1, 1, self.PREVIEW_WIDTH - 1, self.PREVIEW_HEIGHT - 1, 11,
            fill=Palette.SURFACE_RAISED, outline=Palette.BORDER,
        )
        words = [word for word in self.name_field.value().split() if word]
        initials = "".join(word[0] for word in words[:2]).upper() or "?"
        self.preview.create_text(
            self.PREVIEW_WIDTH / 2, self.PREVIEW_HEIGHT / 2, text=initials,
            fill=Palette.TEXT_FAINT,
            font=tkfont.Font(family=Fonts.FAMILY, size=28, weight="bold"),
        )

    # ------------------------------------------------------------------ saving

    def _on_name_typed(self) -> None:
        self._refresh_save()
        if self._preview_path is None:
            self._draw_preview()

    def _refresh_save(self) -> None:
        self.save_button.set_enabled(bool(self.name_field.value()))

    def _save(self) -> None:
        if not self.name_field.value():
            return
        self.result = {
            "name": self.name_field.value(),
            "nickname": self.nickname_field.value(),
            "age": self.age_field.value(),
            "status": self.status_picker.value(),
            "server": self.server_field.value(),
            "occupation": self.occupation_field.value(),
            "affiliation": self.affiliation_field.value(),
            "background": self.background_area.value(),
            "photos": list(self._pending),
        }
        self.destroy()


class EntryDialog(ModalWindow):
    """One thing that happened: when, what kind, and the telling of it."""

    def __init__(
        self, parent: tk.Misc, heading: str, save_label: str, entry: DiaryEntry | None = None
    ) -> None:
        super().__init__(parent, heading)
        self.result: dict | None = None

        self._build(heading, save_label)
        if entry is not None:
            self._fill_from(entry)
        self._refresh_hint()
        self._refresh_save()

    @classmethod
    def ask_new(cls, parent: tk.Misc) -> dict | None:
        dialog = cls(parent, "Add to the story", "Add entry")
        dialog.show()
        return dialog.result

    @classmethod
    def ask_edit(cls, parent: tk.Misc, entry: DiaryEntry) -> dict | None:
        dialog = cls(parent, "Edit entry", "Save changes", entry)
        dialog.show()
        return dialog.result

    def _build(self, heading: str, save_label: str) -> None:
        page = tk.Frame(self, bg=Palette.WINDOW)
        page.pack(fill="both", expand=True, padx=28, pady=(24, 22))

        label(page, heading, Fonts.title, Palette.TEXT, Palette.WINDOW).pack(anchor="w")

        title_block = FieldBlock(page, "What happened")
        title_block.pack(fill="x", pady=(18, 0))
        self.title_field = Field(title_block, "Married Halvard at the old chapel", width=58)
        self.title_field.bind("<KeyRelease>", lambda _event: self._refresh_save())
        self.title_field.pack(fill="x", ipady=3)

        pair = tk.Frame(page, bg=Palette.WINDOW)
        pair.pack(fill="x", pady=(14, 0))

        kind_block = FieldBlock(pair, "Kind")
        kind_block.pack(side="left", anchor="n")
        self.kind_picker = Dropdown(kind_block, EntryKind, width=16)
        self.kind_picker.pack(fill="x")

        when_column = tk.Frame(pair, bg=Palette.WINDOW)
        when_column.pack(side="left", anchor="n", fill="x", expand=True, padx=(14, 0))

        when_block = FieldBlock(when_column, "When")
        when_block.pack(fill="x")
        self.when_field = Field(
            when_block, "2026-06-20, 20 June 2026, today, or leave blank", width=36
        )
        self.when_field.bind("<KeyRelease>", lambda _event: self._refresh_hint())
        self.when_field.pack(fill="x", ipady=3)

        self.when_hint = label(
            when_column, "", Fonts.tiny, Palette.TEXT_FAINT, Palette.WINDOW, wrap=380
        )
        self.when_hint.pack(anchor="w", pady=(4, 0))

        telling_block = FieldBlock(page, "The telling")
        telling_block.pack(fill="both", expand=True, pady=(14, 0))
        self.body_area = TextArea(
            telling_block, "What happened, and what it meant to them.", height=9
        )
        self.body_area.pack(fill="both", expand=True)

        footer = tk.Frame(page, bg=Palette.WINDOW)
        footer.pack(fill="x", pady=(22, 0))

        self.save_button = Button(footer, save_label, self._save, "primary", Palette.WINDOW)
        self.save_button.pack(side="right")
        Button(footer, "Cancel", self.cancel, "ghost", Palette.WINDOW).pack(
            side="right", padx=(0, 8)
        )

    def _fill_from(self, entry: DiaryEntry) -> None:
        self.title_field.set_value(entry.title)
        self.kind_picker.set_value(entry.kind)
        self.when_field.set_value(entry.occurred_on.isoformat() if entry.occurred_on else "")
        self.body_area.set_value(entry.body or "")

    def _refresh_hint(self) -> None:
        message, understood = FlexibleDate.describe(self.when_field.value())
        self.when_hint.configure(
            text=message, fg=Palette.TEXT_FAINT if understood else Palette.SHELVED
        )

    def _refresh_save(self) -> None:
        self.save_button.set_enabled(bool(self.title_field.value()))

    def _save(self) -> None:
        if not self.title_field.value():
            return
        self.result = {
            "title": self.title_field.value(),
            "kind": self.kind_picker.value(),
            "when": self.when_field.value(),
            "body": self.body_area.value(),
        }
        self.destroy()


_image_folder: list[str] = []


def _last_image_folder() -> str:
    """Players add screenshots in batches from the same folder, so remember it."""
    from pathlib import Path

    return _image_folder[0] if _image_folder else str(Path.home())


def _remember_image_folder(path: str) -> None:
    from pathlib import Path

    _image_folder.clear()
    _image_folder.append(str(Path(path).parent))


# =============================================================================
#  The main window
# =============================================================================


class Handlers:
    """
    Everything the view can report. The controller replaces each of these with a
    real method; until it does they do nothing, so the window is never broken by a
    handler that has not been wired up yet.

    This class is the whole contract between view and controller. If you want to
    know what the app can do, read this list.
    """

    def __init__(self) -> None:
        def ignore(*_args: object) -> None:
            return None

        self.search: Callable[[str], None] = ignore
        self.filter: Callable[[object], None] = ignore
        self.select: Callable[[int], None] = ignore
        self.new_character: Callable[[], None] = ignore
        self.edit_profile: Callable[[], None] = ignore
        self.delete_character: Callable[[], None] = ignore
        self.change_status: Callable[[object], None] = ignore
        self.save_background: Callable[[str], None] = ignore
        self.add_entry: Callable[[], None] = ignore
        self.edit_entry: Callable[[DiaryEntry], None] = ignore
        self.delete_entry: Callable[[DiaryEntry], None] = ignore
        self.add_photos: Callable[[], None] = ignore
        self.set_cover: Callable[[CharacterPhoto], None] = ignore
        self.remove_photo: Callable[[CharacterPhoto], None] = ignore
        self.set_caption: Callable[[CharacterPhoto, str], None] = ignore
        self.export_pdf: Callable[[], None] = ignore


class MainView(tk.Tk):
    """The shell: the roster on the left, the open character on the right."""

    SIDEBAR_WIDTH = 344
    GALLERY_CARD_WIDTH = 272
    NAME_WIDTH = 208
    META_WIDTH = 226

    def __init__(self) -> None:
        super().__init__()
        Fonts.build()

        self.on = Handlers()
        self.title("Roleplay Diary")
        self.geometry("1280x840")
        self.minsize(1000, 660)
        self.configure(bg=Palette.WINDOW)

        self._character: Character | None = None
        self._entries: list[DiaryEntry] = []
        self._photos: list[CharacterPhoto] = []
        self._portrait: str | None = None
        self._tab = 0
        self._gallery_columns = 0

        self._build()
        self.bind_all("<MouseWheel>", ScrollFrame.route_wheel(self))
        self.after(20, lambda: darken_title_bar(self))
        self.update_idletasks()

    # ------------------------------------------------------------------- build

    def _build(self) -> None:
        self._build_sidebar()

        self.content = tk.Frame(self, bg=Palette.WINDOW)
        self.content.pack(side="left", fill="both", expand=True)
        self.content.bind("<Configure>", self._on_content_resize)

    def _build_sidebar(self) -> None:
        sidebar = tk.Frame(self, bg=Palette.SURFACE, width=self.SIDEBAR_WIDTH)
        sidebar.pack(side="left", fill="y")
        sidebar.pack_propagate(False)

        edge = tk.Frame(self, bg=Palette.BORDER, width=1)
        edge.pack(side="left", fill="y")

        head = tk.Frame(sidebar, bg=Palette.SURFACE)
        head.pack(fill="x", padx=18, pady=(20, 0))

        label(head, "Roleplay Diary", Fonts.title, Palette.TEXT, Palette.SURFACE).pack(
            anchor="w"
        )
        label(
            head, f"alpha {APP_VERSION}", Fonts.tiny, Palette.TEXT_FAINT, Palette.SURFACE
        ).pack(anchor="w")

        self.search_field = Field(sidebar, "Search by name, nickname or server", width=34)
        self.search_field.pack(fill="x", padx=18, pady=(18, 0), ipady=3)
        self.search_field.bind(
            "<KeyRelease>", lambda _event: self.on.search(self.search_field.value())
        )

        chips = tk.Frame(sidebar, bg=Palette.SURFACE)
        chips.pack(fill="x", padx=18, pady=(12, 0))

        self._chips: list[Chip] = []
        choices: list[tuple[str, object]] = [("All", None)]
        choices += [(status.label, status) for status in CharacterStatus.options()]
        for text, value in choices:
            chip = Chip(chips, text, value, self._on_chip, Palette.SURFACE)
            chip.pack(side="left", padx=(0, 5))
            self._chips.append(chip)
        self._chips[0].set_checked(True)

        Button(
            sidebar, "New character", lambda: self.on.new_character(), "primary",
            Palette.SURFACE, width=self.SIDEBAR_WIDTH - 36,
        ).pack(padx=18, pady=(14, 0))

        self.roster = ScrollFrame(sidebar, Palette.SURFACE)
        self.roster.pack(fill="both", expand=True, padx=(14, 4), pady=(14, 0))

        self.count_label = label(
            sidebar, "", Fonts.tiny, Palette.TEXT_FAINT, Palette.SURFACE
        )
        self.count_label.pack(anchor="w", padx=18, pady=(8, 14))

    def _on_chip(self, chosen: Chip) -> None:
        for chip in self._chips:
            chip.set_checked(chip is chosen)
        self.on.filter(chosen.value)

    def set_filter(self, value: object) -> None:
        for chip in self._chips:
            chip.set_checked(chip.value == value)

    def clear_search(self) -> None:
        self.search_field.set_value("")

    # ------------------------------------------------------------------ roster

    def show_roster(
        self, items: Sequence[RosterItem], selected_id: int | None, total: int
    ) -> None:
        self.roster.clear()
        self.count_label.configure(
            text="1 character" if total == 1 else f"{total} characters"
        )

        for item in items:
            self._build_roster_row(item, item.id == selected_id).pack(fill="x", pady=(0, 4))

    def _build_roster_row(self, item: RosterItem, selected: bool) -> tk.Frame:
        background = Palette.SURFACE_RAISED if selected else Palette.SURFACE
        border = Palette.ACCENT if selected else Palette.SURFACE

        row = tk.Frame(
            self.roster.body, bg=background,
            highlightthickness=1, highlightbackground=border, bd=0, cursor="hand2",
        )

        inner = tk.Frame(row, bg=background)
        inner.pack(fill="x", padx=10, pady=9)

        avatar = Avatar(inner, 46, 10, background)
        avatar.show(item.character.initials, item.portrait_path)
        avatar.pack(side="left")

        text_column = tk.Frame(inner, bg=background)
        text_column.pack(side="left", fill="x", expand=True, padx=(11, 0))

        heading = tk.Frame(text_column, bg=background)
        heading.pack(fill="x")
        Dot(heading, Palette.for_status(item.status), background).pack(side="left", pady=(0, 1))
        label(
            heading, elide(item.name, Fonts.heading, self.NAME_WIDTH),
            Fonts.heading, Palette.TEXT, background,
        ).pack(side="left", padx=(7, 0))

        label(
            text_column, elide(item.meta, Fonts.tiny, self.META_WIDTH),
            Fonts.tiny, Palette.TEXT_MUTED, background,
        ).pack(anchor="w", pady=(2, 0))

        self._bind_deep(row, "<Button-1>", lambda _event, i=item.id: self.on.select(i))
        if not selected:
            self._bind_deep(
                row, "<Enter>",
                lambda _event, r=row: r.configure(highlightbackground=Palette.BORDER_STRONG),
            )
            self._bind_deep(
                row, "<Leave>",
                lambda _event, r=row: r.configure(highlightbackground=Palette.SURFACE),
            )
        return row

    @staticmethod
    def _bind_deep(widget: tk.Misc, sequence: str, callback: Callable) -> None:
        """
        Bind on a container and everything inside it. A click on the name label is
        still a click on the row, and tkinter will not work that out for us.
        """
        widget.bind(sequence, callback)
        for child in widget.winfo_children():
            MainView._bind_deep(child, sequence, callback)

    # ----------------------------------------------------------------- content

    def _clear_content(self) -> None:
        for child in self.content.winfo_children():
            child.destroy()
        self._gallery_columns = 0

    def show_placeholder(self, title: str, subtitle: str) -> None:
        self._clear_content()
        self._character = None
        EmptyState(
            self.content, title, subtitle, "New character",
            lambda: self.on.new_character(), Palette.WINDOW,
        ).place(relx=0.5, rely=0.42, anchor="center")

    def show_character(
        self,
        character: Character,
        entries: Sequence[DiaryEntry],
        photos: Sequence[CharacterPhoto],
        portrait_path: str | None,
        keep_tab: bool = True,
    ) -> None:
        """Draw one character whole. The open tab survives a refresh."""
        self._character = character
        self._entries = list(entries)
        self._photos = list(photos)
        self._portrait = portrait_path
        if not keep_tab:
            self._tab = 0

        self._clear_content()
        self._build_hero()

        tab_names = self._tab_names()
        self.tabs = TabBar(self.content, tab_names, self._on_tab, Palette.WINDOW)
        self.tabs.pack(fill="x", padx=30)
        self.tabs.select(self._tab, notify=False)

        tk.Frame(self.content, bg=Palette.BORDER, height=1).pack(fill="x")

        self.tab_body = ScrollFrame(self.content, Palette.WINDOW)
        self.tab_body.pack(fill="both", expand=True)
        self._render_tab()

    def _tab_names(self) -> list[str]:
        story = f"Story ({len(self._entries)})" if self._entries else "Story"
        gallery = f"Gallery ({len(self._photos)})" if self._photos else "Gallery"
        return [story, gallery, "Background"]

    def _on_tab(self, index: int) -> None:
        self._tab = index
        self._render_tab()

    def _build_hero(self) -> None:
        character = self._character
        hero = tk.Frame(self.content, bg=Palette.SURFACE)
        hero.pack(fill="x")

        inner = tk.Frame(hero, bg=Palette.SURFACE)
        inner.pack(fill="x", padx=30, pady=(26, 22))

        avatar = Avatar(inner, 96, 16, Palette.SURFACE)
        avatar.show(character.initials, self._portrait)
        avatar.pack(side="left", anchor="n")

        actions = tk.Frame(inner, bg=Palette.SURFACE)
        actions.pack(side="right", anchor="n")

        buttons = tk.Frame(actions, bg=Palette.SURFACE)
        buttons.pack(anchor="e")
        Button(buttons, "Delete", lambda: self.on.delete_character(), "danger",
               Palette.SURFACE).pack(side="left")
        Button(buttons, "Edit profile", lambda: self.on.edit_profile(), "secondary",
               Palette.SURFACE).pack(side="left", padx=(8, 0))
        Button(buttons, "Export PDF", lambda: self.on.export_pdf(), "primary",
               Palette.SURFACE).pack(side="left", padx=(8, 0))

        status_block = tk.Frame(actions, bg=Palette.SURFACE)
        status_block.pack(anchor="e", pady=(12, 0))
        label(status_block, "STATUS", Fonts.label, Palette.TEXT_MUTED, Palette.SURFACE).pack(
            anchor="w", pady=(0, 5)
        )
        self.status_picker = Dropdown(
            status_block, CharacterStatus, lambda value: self.on.change_status(value), 14
        )
        self.status_picker.set_value(character.status)
        self.status_picker.tint(Palette.for_status(character.status))
        self.status_picker.pack(anchor="e")

        identity = tk.Frame(inner, bg=Palette.SURFACE)
        identity.pack(side="left", fill="x", expand=True, padx=(22, 16))

        label(identity, character.name, Fonts.display, Palette.TEXT, Palette.SURFACE).pack(
            anchor="w"
        )
        if character.nickname:
            label(
                identity, character.quoted_nickname, Fonts.body, Palette.TEXT_MUTED,
                Palette.SURFACE,
            ).pack(anchor="w", pady=(2, 0))
        if character.facts_line:
            label(
                identity, character.facts_line, Fonts.body, Palette.TEXT_MUTED,
                Palette.SURFACE, wrap=520,
            ).pack(anchor="w", pady=(4, 0))
        label(
            identity, character.archived_label, Fonts.tiny, Palette.TEXT_FAINT,
            Palette.SURFACE,
        ).pack(anchor="w", pady=(4, 0))


    # -------------------------------------------------------------------- tabs

    def _render_tab(self) -> None:
        self.tab_body.clear()
        page = tk.Frame(self.tab_body.body, bg=Palette.WINDOW)
        page.pack(fill="both", expand=True, padx=30, pady=(20, 26))

        if self._tab == 0:
            self._render_story(page)
        elif self._tab == 1:
            self._render_gallery(page)
        else:
            self._render_background(page)

    def _tab_header(self, parent: tk.Misc, note: str, action: str, command) -> None:
        header = tk.Frame(parent, bg=Palette.WINDOW)
        header.pack(fill="x", pady=(0, 12))
        label(header, note, Fonts.body, Palette.TEXT_MUTED, Palette.WINDOW).pack(side="left")
        Button(header, action, command, "primary", Palette.WINDOW).pack(side="right")

    # ------------------------------------------------------------------- story

    def _render_story(self, page: tk.Frame) -> None:
        self._tab_header(
            page, "Oldest first, the way the story happened.", "Add entry",
            lambda: self.on.add_entry(),
        )

        if not self._entries:
            EmptyState(
                page, "Nothing has happened yet",
                "Add the first thing that mattered - where they arrived, who they met, "
                "what they lost.",
                "Add the first entry", lambda: self.on.add_entry(), Palette.WINDOW,
            ).pack(fill="x", pady=40)
            return

        for entry in self._entries:
            self._build_entry_card(page, entry).pack(fill="x", pady=(0, 10))

    def _build_entry_card(self, parent: tk.Misc, entry: DiaryEntry) -> Card:
        card = Card(parent)
        inner = tk.Frame(card, bg=card.background)
        inner.pack(fill="x", padx=16, pady=14)

        stamp = tk.Frame(inner, bg=card.background, width=96)
        stamp.pack(side="left", anchor="n")
        stamp.pack_propagate(False)
        label(
            stamp, entry.short_date_label, Fonts.label, Palette.TEXT, card.background
        ).pack(anchor="w")
        label(
            stamp, entry.kind.label, Fonts.tiny, Palette.TEXT_MUTED, card.background
        ).pack(anchor="w", pady=(2, 0))
        stamp.configure(height=max(34, Fonts.label.metrics("linespace") * 2 + 8))

        tk.Frame(inner, bg=Palette.BORDER, width=1).pack(side="left", fill="y", padx=(0, 14))

        actions = tk.Frame(inner, bg=card.background)
        actions.pack(side="right", anchor="n")
        Button(actions, "Edit", lambda e=entry: self.on.edit_entry(e), "ghost",
               card.background, width=66).pack()
        Button(actions, "Delete", lambda e=entry: self.on.delete_entry(e), "danger",
               card.background, width=66).pack(pady=(2, 0))

        body = tk.Frame(inner, bg=card.background)
        body.pack(side="left", fill="x", expand=True)
        label(
            body, entry.title, Fonts.heading, Palette.TEXT, card.background, wrap=640
        ).pack(anchor="w")
        if entry.body:
            label(
                body, entry.body, Fonts.body, Palette.TEXT_MUTED, card.background, wrap=640
            ).pack(anchor="w", pady=(5, 0))

        return card

    # ----------------------------------------------------------------- gallery

    def _render_gallery(self, page: tk.Frame) -> None:
        self._tab_header(
            page, "Photos are copied into the diary, so the originals can move.",
            "Add photos", lambda: self.on.add_photos(),
        )

        if not self._photos:
            EmptyState(
                page, "No photos yet",
                "Screenshots of your character as they change over time. The first one "
                "becomes their cover.",
                "Add photos", lambda: self.on.add_photos(), Palette.WINDOW,
            ).pack(fill="x", pady=40)
            return

        columns = self._column_count()
        self._gallery_columns = columns

        grid = tk.Frame(page, bg=Palette.WINDOW)
        grid.pack(fill="x")

        for index, photo in enumerate(self._photos):
            card = self._build_photo_card(grid, photo)
            card.grid(
                row=index // columns, column=index % columns, padx=(0, 12), pady=(0, 12),
                sticky="n",
            )

    def _column_count(self) -> int:
        """How many photo cards fit across the pane at its current width."""
        available = max(self.content.winfo_width() - 80, self.GALLERY_CARD_WIDTH)
        return max(1, min(4, available // self.GALLERY_CARD_WIDTH))

    def _on_content_resize(self, _event) -> None:
        """Re-flow the gallery when the window width changes the column count."""
        if self._character is None or self._tab != 1 or not self._photos:
            return
        if self._column_count() != self._gallery_columns:
            self._render_tab()

    def _build_photo_card(self, parent: tk.Misc, photo: CharacterPhoto) -> Card:
        width, height = 236, 150
        card = Card(parent)

        inner = tk.Frame(card, bg=card.background)
        inner.pack(padx=12, pady=12)

        picture = tk.Canvas(
            inner, width=width, height=height, highlightthickness=0, bd=0,
            bg=card.background,
        )
        picture.pack()

        image = Images.rounded(photo.full_path, width, height, 9, card.background)
        if image is not None:
            picture.create_image(0, 0, image=image, anchor="nw")
            picture.image = image  # tkinter drops images it holds no reference to
        else:
            rounded_rect(picture, 1, 1, width - 1, height - 1, 9,
                         fill=Palette.SURFACE_RAISED, outline=Palette.BORDER)
            picture.create_text(
                width / 2, height / 2, text="missing file",
                fill=Palette.TEXT_FAINT, font=Fonts.tiny,
            )

        caption = Field(inner, "Add a caption", width=30)
        caption.set_value(photo.caption or "")
        caption.pack(fill="x", pady=(9, 0), ipady=2)
        caption.bind(
            "<FocusOut>", lambda _event, p=photo, c=caption: self.on.set_caption(p, c.value())
        )
        caption.bind(
            "<Return>", lambda _event, p=photo, c=caption: self.on.set_caption(p, c.value())
        )

        footer = tk.Frame(inner, bg=card.background)
        footer.pack(fill="x", pady=(9, 0))

        is_cover = self._character is not None and photo.id == self._character.portrait_photo_id
        if is_cover:
            Pill(footer, "Cover", Palette.ACCENT, card.background).pack(side="left")
        else:
            Button(footer, "Set as cover", lambda p=photo: self.on.set_cover(p), "ghost",
                   card.background, padding=(8, 6), font=Fonts.tiny).pack(side="left")

        Button(footer, "Remove", lambda p=photo: self.on.remove_photo(p), "danger",
               card.background, padding=(8, 6), font=Fonts.tiny).pack(side="right")
        label(footer, photo.added_label, Fonts.tiny, Palette.TEXT_FAINT, card.background).pack(
            side="right", padx=(0, 8)
        )

        return card

    # -------------------------------------------------------------- background

    def _render_background(self, page: tk.Frame) -> None:
        label(
            page,
            "The fixed part of the story. Things that happen later belong on the "
            "Story tab.",
            Fonts.body, Palette.TEXT_MUTED, Palette.WINDOW, wrap=760,
        ).pack(anchor="w", pady=(0, 12))

        self.background_area = TextArea(
            page,
            "Where they came from, and anything that was true before the story started.",
            height=16,
        )
        self.background_area.set_value(self._character.background or "")
        self.background_area.pack(fill="both", expand=True)
        self.background_area.bind("<KeyRelease>", lambda _event: self._refresh_background())

        footer = tk.Frame(page, bg=Palette.WINDOW)
        footer.pack(fill="x", pady=(12, 0))

        self.save_background_button = Button(
            footer, "Save background", self._on_save_background, "primary", Palette.WINDOW
        )
        self.save_background_button.pack(side="right")

        self.revert_background_button = Button(
            footer, "Revert", self._on_revert_background, "ghost", Palette.WINDOW
        )
        self._refresh_background()

    def _background_dirty(self) -> bool:
        return self.background_area.value() != (self._character.background or "").strip()

    def _refresh_background(self) -> None:
        dirty = self._background_dirty()
        self.save_background_button.set_enabled(dirty)
        if dirty:
            self.revert_background_button.pack(
                side="right", padx=(0, 8), before=self.save_background_button
            )
        else:
            self.revert_background_button.pack_forget()

    def _on_save_background(self) -> None:
        self.on.save_background(self.background_area.value())

    def _on_revert_background(self) -> None:
        self.background_area.set_value(self._character.background or "")
        self._refresh_background()

    # ----------------------------------------------------------- asking things

    def ask_new_character(self) -> dict | None:
        return CharacterDialog.ask_new(self)

    def ask_edit_character(self, character: Character, portrait: str | None) -> dict | None:
        return CharacterDialog.ask_edit(self, character, portrait)

    def ask_new_entry(self) -> dict | None:
        return EntryDialog.ask_new(self)

    def ask_edit_entry(self, entry: DiaryEntry) -> dict | None:
        return EntryDialog.ask_edit(self, entry)

    def confirm(self, title: str, body: str, confirm_label: str = "Delete") -> bool:
        return Ask.confirm(self, title, body, confirm_label)

    def offer(self, title: str, body: str, yes_label: str, no_label: str = "Close") -> bool:
        return Ask.offer(self, title, body, yes_label, no_label)

    def inform(self, title: str, body: str) -> None:
        Ask.inform(self, title, body)

    def pick_images(self) -> list[str]:
        chosen = Ask.images(self, _last_image_folder())
        if chosen:
            _remember_image_folder(chosen[0])
        return chosen

    def pick_pdf_destination(self, start_in: str, suggested: str) -> str | None:
        return Ask.pdf_destination(self, start_in, suggested)

    # -------------------------------------------------------------------- loop

    def run(self) -> None:
        self.mainloop()




