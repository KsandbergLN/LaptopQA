from __future__ import annotations

import ctypes
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path


APP_EXES = ("LaptopQATestingV5.exe", "LaptopQATestingV4.exe")
MUTABLE_ITEMS = ("Laptop-QA-Config.json", ".runtime", "hardware", "hash", "logs", "QA sheets")


def message_box(text: str, title: str = "Laptop QA") -> None:
    ctypes.windll.user32.MessageBoxW(None, text, title, 0x00000010)


def launcher_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def find_source_app(root: Path) -> Path:
    candidates = (
        root / "LAPTOP QA" / "App",
        root / "LAPTOP QA",
        root / "App",
        root,
    )

    for candidate in candidates:
        if any((candidate / exe).is_file() for exe in APP_EXES):
            return candidate

    expected = root / "LAPTOP QA" / "App"
    raise FileNotFoundError(f"Laptop QA could not find the app folder.\n\nExpected:\n{expected}")


def find_app_exe(app_dir: Path) -> Path:
    for exe in APP_EXES:
        path = app_dir / exe
        if path.is_file():
            return path
    raise FileNotFoundError(f"Laptop QA could not find the app EXE inside:\n{app_dir}")


def version_from_exe(exe: Path) -> str:
    name = exe.name.lower()
    if "v5" in name:
        return "V5"
    return "V4"


def safe_stamp(exe: Path) -> str:
    stat = exe.stat()
    return f"{int(stat.st_mtime)}-{stat.st_size}"


def local_app_dir(version: str, stamp: str) -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if not local_app_data:
        raise EnvironmentError("LOCALAPPDATA is not available for this Windows user.")
    return Path(local_app_data) / "Laptop QA" / version / stamp / "LAPTOP QA"


def source_package_root(source_app: Path, root: Path) -> Path:
    if source_app.name.lower() == "app":
        return source_app.parent
    if source_app.parent.name.lower() == "laptop qa":
        return source_app.parent
    return root


def log_path(package_root: Path) -> Path:
    return package_root / "logs" / "launcher-python.log"


def write_log(path: Path, message: str) -> None:
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        timestamp = time.strftime("%Y-%m-%d %H:%M:%S")
        with path.open("a", encoding="utf-8") as handle:
            handle.write(f"[{timestamp}] {message}\n")
    except Exception:
        pass


def copy_tree_contents(source: Path, destination: Path, *, skip_mutable: bool) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    skip_names = set(MUTABLE_ITEMS) if skip_mutable else set()

    for item in source.iterdir():
        if item.name in skip_names:
            continue

        target = destination / item.name
        if item.is_dir():
            if target.exists() and not target.is_dir():
                target.unlink()
            shutil.copytree(item, target, dirs_exist_ok=True)
        else:
            shutil.copy2(item, target)


def copy_mutable_to_local(source_app: Path, local_app: Path) -> None:
    local_app.mkdir(parents=True, exist_ok=True)
    for item_name in MUTABLE_ITEMS:
        source = source_app / item_name
        target = local_app / item_name
        try:
            if source.is_dir():
                shutil.copytree(source, target, dirs_exist_ok=True)
            elif source.is_file():
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)
            elif item_name != "Laptop-QA-Config.json":
                target.mkdir(parents=True, exist_ok=True)
        except Exception:
            # Runtime files can be locked by another run. The app can rebuild them.
            pass


def sync_outputs_back(local_app: Path, source_app: Path, log: Path) -> None:
    for item_name in MUTABLE_ITEMS:
        local_item = local_app / item_name
        source_item = source_app / item_name
        try:
            if local_item.is_dir():
                shutil.copytree(local_item, source_item, dirs_exist_ok=True)
            elif local_item.is_file():
                source_item.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(local_item, source_item)
        except Exception as exc:
            write_log(log, f"Could not sync {item_name}: {exc}")


def prune_old_staged_versions(version_root: Path, current_stamp: str, log: Path) -> None:
    try:
        for child in version_root.iterdir():
            if child.name == current_stamp or not child.is_dir():
                continue
            try:
                shutil.rmtree(child)
                write_log(log, f"Removed old staged copy: {child}")
            except Exception as exc:
                write_log(log, f"Could not remove old staged copy {child}: {exc}")
    except Exception:
        pass


def stage_app(source_app: Path, local_app: Path, source_exe: Path, log: Path) -> Path:
    local_exe = local_app / source_exe.name
    if not local_exe.is_file():
        if local_app.exists():
            shutil.rmtree(local_app, ignore_errors=True)
        write_log(log, f"Staging app from {source_app} to {local_app}")
        copy_tree_contents(source_app, local_app, skip_mutable=True)
    else:
        write_log(log, f"Using existing staged app: {local_app}")

    copy_mutable_to_local(source_app, local_app)
    local_exe = local_app / source_exe.name
    if not local_exe.is_file():
        raise FileNotFoundError(f"The staged app EXE was not created:\n{local_exe}")
    return local_exe


def main() -> int:
    root = launcher_root()
    fallback_log = Path(os.environ.get("TEMP", str(root))) / "Laptop-QA-python-launcher.log"

    try:
        source_app = find_source_app(root)
        package_root = source_package_root(source_app, root)
        log = log_path(package_root)
        write_log(log, f"Launcher started from {root}")
        write_log(log, f"Source app folder: {source_app}")

        source_exe = find_app_exe(source_app)
        version = version_from_exe(source_exe)
        stamp = safe_stamp(source_exe)
        local_app = local_app_dir(version, stamp)
        local_exe = stage_app(source_app, local_app, source_exe, log)

        prune_old_staged_versions(local_app.parent.parent, stamp, log)

        write_log(log, f"Launching local app: {local_exe}")
        process = subprocess.Popen([str(local_exe)], cwd=str(local_app))
        exit_code = process.wait()
        write_log(log, f"Local app exited with code {exit_code}")
        sync_outputs_back(local_app, source_app, log)
        write_log(log, "Synced output folders back to package.")
        return exit_code
    except Exception as exc:
        write_log(fallback_log, f"Launcher failed: {exc}")
        try:
            root = launcher_root()
            source_app = find_source_app(root)
            write_log(log_path(source_package_root(source_app, root)), f"Launcher failed: {exc}")
        except Exception:
            pass
        message_box(f"Laptop QA could not start:\n\n{exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
