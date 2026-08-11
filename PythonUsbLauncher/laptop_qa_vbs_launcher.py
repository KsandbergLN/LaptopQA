from __future__ import annotations

import ctypes
import subprocess
import sys
from pathlib import Path


def message_box(text: str, title: str = "Laptop QA") -> None:
    ctypes.windll.user32.MessageBoxW(None, text, title, 0x00000010)


def launcher_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def find_vbs(root: Path) -> Path:
    candidates = (
        root / "Windows Laptop QA Launcher.vbs",
        root / "LAPTOP QA" / "Windows Laptop QA Launcher.vbs",
        root.parent / "Windows Laptop QA Launcher.vbs",
    )

    for candidate in candidates:
        if candidate.is_file():
            return candidate

    raise FileNotFoundError(
        "Laptop QA could not find Windows Laptop QA Launcher.vbs.\n\n"
        f"Expected it next to the launcher at:\n{root / 'Windows Laptop QA Launcher.vbs'}"
    )


def main() -> int:
    try:
        root = launcher_root()
        script = find_vbs(root)
        wscript = Path("C:/Windows/System32/wscript.exe")
        command = [str(wscript if wscript.is_file() else "wscript.exe"), str(script)]
        subprocess.Popen(command, cwd=str(script.parent), close_fds=True)
        return 0
    except Exception as exc:
        message_box(f"Laptop QA could not start the VBS launcher:\n\n{exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
