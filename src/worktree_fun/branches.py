from dataclasses import (
    dataclass,
)
from datetime import (
    datetime,
    timezone,
)

import pygit2
from pygit2.enums import (
    BranchType,
)
from textual.containers import (
    Vertical,
)
from textual.widgets import (
    Label,
    ListItem,
    Static,
)


@dataclass(frozen=True, slots=True)
class BranchView:
    name: str
    last_commit_time: datetime
    upstream_status: str
    is_checked_out: bool

    @property
    def last_commit_display(self) -> str:
        return self.last_commit_time.strftime("%Y-%m-%d %H:%M")


def get_upstream_status(branch: pygit2.Branch) -> str:
    try:
        upstream = branch.upstream
    except pygit2.GitError:
        return "No upstream"

    if upstream is None:
        return "No upstream"

    return "Tracks " + upstream.shorthand


def load_branch_views(repo: pygit2.Repository) -> list[BranchView]:
    branch_views: list[BranchView] = []
    for branch_name in repo.listall_branches(BranchType.LOCAL):
        branch = repo.lookup_branch(branch_name, BranchType.LOCAL)
        if branch is None:
            continue

        commit, _ = repo.resolve_refish(branch_name)
        commit_time = datetime.fromtimestamp(commit.commit_time, tz=timezone.utc)
        commit_time_local = commit_time.astimezone()

        branch_views.append(
            BranchView(
                name=branch_name,
                last_commit_time=commit_time_local,
                upstream_status=get_upstream_status(branch),
                is_checked_out=branch.is_checked_out(),
            )
        )

    branch_views.sort(key=lambda b: b.last_commit_time, reverse=True)
    return branch_views


def render_branch(branch: BranchView) -> ListItem:
    name_text = branch.name
    if branch.is_checked_out:
        name_text = "* " + name_text

    return ListItem(
        Vertical(
            Label(name_text, classes="branch-name"),
            Static(branch.last_commit_display, classes="branch-time"),
            Static(branch.upstream_status, classes="branch-upstream"),
            classes="branch-item list-item-container",
        ),
        name=branch.name,
    )


def render_branch_message(message: str) -> ListItem:
    return ListItem(
        Static(message, classes="branch-message"),
        disabled=True,
    )
