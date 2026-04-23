import os
from dataclasses import (
    dataclass,
)

import pygit2
from textual.containers import (
    Vertical,
)
from textual.widgets import (
    Label,
    ListItem,
    Static,
)


@dataclass(frozen=True, slots=True)
class WorktreeView:
    name: str
    branch: str
    path: str

    @property
    def display_path(self) -> str:
        home = os.path.normpath(os.path.expanduser("~"))
        if self.path == home:
            return "~"

        home_prefix = home + os.sep
        if self.path.startswith(home_prefix):
            return "~" + self.path[len(home) :]

        return self.path


def discover_repository(cwd: str) -> pygit2.Repository:
    repo_path = pygit2.discover_repository(cwd)
    if repo_path is None:
        raise ValueError(f"Not inside a git repository: {cwd}")

    return pygit2.Repository(repo_path)


def get_repository_branch(repo: pygit2.Repository) -> str:
    if repo.head_is_detached:
        return "(detached HEAD)"
    if repo.head_is_unborn:
        return "(unborn branch)"

    return repo.head.shorthand


def get_worktree_branch(worktree) -> str:
    repo_path = pygit2.discover_repository(worktree.path)
    if repo_path is None:
        return "(unknown branch)"

    try:
        worktree_repo = pygit2.Repository(repo_path)
    except pygit2.GitError:
        return "(unknown branch)"

    return get_repository_branch(worktree_repo)


def get_worktree_name(path: str) -> str:
    normalized_path = os.path.normpath(path)
    return os.path.basename(normalized_path) or normalized_path


def build_current_worktree_view(repo: pygit2.Repository) -> WorktreeView:
    path = os.path.normpath(repo.workdir or repo.path)
    return WorktreeView(
        name=get_worktree_name(path),
        branch=get_repository_branch(repo),
        path=path,
    )


def build_worktree_view(worktree) -> WorktreeView:
    return WorktreeView(
        name=worktree.name,
        branch=get_worktree_branch(worktree),
        path=os.path.normpath(worktree.path),
    )


def load_worktree_views(repo: pygit2.Repository) -> list[WorktreeView]:
    worktree_names = repo.list_worktrees()
    worktree_infos = [
        repo.lookup_worktree(worktree_name) for worktree_name in worktree_names
    ]

    worktree_views = [build_current_worktree_view(repo)]
    seen_paths = {worktree_views[0].path}
    for worktree in worktree_infos:
        worktree_view = build_worktree_view(worktree)
        if worktree_view.path in seen_paths:
            continue

        worktree_views.append(worktree_view)
        seen_paths.add(worktree_view.path)

    return worktree_views


def render_worktree(worktree: WorktreeView) -> ListItem:
    return ListItem(
        Vertical(
            Label(worktree.name, classes="worktree-name"),
            Static("Branch: " + worktree.branch, classes="worktree-branch"),
            Static("Path: " + worktree.display_path, classes="worktree-path"),
            classes="worktree-item",
        ),
        name=worktree.path,
    )
