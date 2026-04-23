import hashlib
import os
import re
from pathlib import (
    Path,
)

_WORKTREE_IDENTIFIER_PATTERN = re.compile(r"[^A-Za-z0-9._-]+")


def shorten_home_path(path: str) -> str:
    home = os.path.normpath(os.path.expanduser("~"))
    normalized_path = os.path.normpath(path)
    if normalized_path == home:
        return "~"

    home_prefix = home + os.sep
    if normalized_path.startswith(home_prefix):
        return "~" + normalized_path[len(home) :]

    return normalized_path


def normalize_worktree_name(worktree_name: str) -> str:
    normalized_name = worktree_name.strip()
    if not normalized_name:
        raise ValueError("Worktree name is required.")

    path = Path(normalized_name)
    if path.is_absolute():
        raise ValueError("Worktree name must be a relative path.")

    if any(part in {".", ".."} for part in path.parts):
        raise ValueError("Worktree name cannot contain '.' or '..' path segments.")

    return normalized_name


def get_worktree_identifier(worktree_name: str) -> str:
    safe_name = _WORKTREE_IDENTIFIER_PATTERN.sub("-", worktree_name).strip("-.")
    if not safe_name:
        safe_name = "worktree"

    if safe_name == worktree_name:
        return safe_name

    digest = hashlib.sha1(worktree_name.encode("utf-8")).hexdigest()[:8]
    return f"{safe_name}-{digest}"
