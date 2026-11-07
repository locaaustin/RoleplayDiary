"""
Double-click launcher for Windows.

The .pyw extension tells Python to run without a console window, so this behaves
like a normal desktop app. It needs no install: it adds the folder it lives in to
the import path and starts the controller from there.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from controller import main  # noqa: E402  (path set above)

if __name__ == "__main__":
    raise SystemExit(main([]))
