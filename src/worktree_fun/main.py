import os
from dataclasses import (
    asdict,
    dataclass,
)
from typing import (
    Literal,
)

import pygit2

from textual.app import (
    App,
    ComposeResult,
)
from textual.lazy import (
    Lazy,
)
from textual.containers import (
    Vertical,
)
from textual.widgets import (
    Footer,
    Header,
    Label,
    ListItem,
    ListView,
    Static,
    TabbedContent,
    TabPane,
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


@dataclass(frozen=True, slots=True)
class PullRequestListView:
    number: int
    title: str
    branch: str
    remote: str | None
    status: Literal["Open", "Merged", "Closed"]
    is_draft_mode: bool

    @property
    def branch_display(self) -> str:
        if self.remote is None:
            return self.branch

        return f"{self.remote}/{self.branch}"

    @property
    def status_display(self) -> str:
        if self.is_draft_mode:
            return f"{self.status} | Draft"

        return self.status


class WorktreeApp(App):
    CSS = """
    ListView {
        border: solid $accent;
    }
    ListItem {
        height: auto;
        padding: 0 1;
    }

    .worktree-item {
        height: auto;
    }

    .worktree-name {
        text-style: bold;
    }

    .worktree-branch {
        color: $accent;
    }

    .worktree-path {
        color: $text-muted;
    }

    .pull-request-item {
        height: auto;
    }

    .pull-request-title {
        text-style: bold;
    }

    .pull-request-branch {
        color: $accent;
    }

    .pull-request-status {
        color: $text-muted;
    }
    """

    BINDINGS = [
        ("q", "quit", "Quit"),
    ]

    pull_requests_loaded: bool

    def compose(self) -> ComposeResult:
        yield Header()
        with TabbedContent(initial="worktrees-pane"):
            with TabPane("Worktrees", id="worktrees-pane"):
                yield ListView(id="worktrees")
            with TabPane("Pull Requests", id="pull-requests-pane"):
                yield Lazy(ListView(id="pull-requests"))
        yield Footer()

    def on_mount(self) -> None:
        self.title = "worktree fun"
        self.pull_requests_loaded = False
        self.log("Mounting worktree app", cwd=os.getcwd())
        self.populate_worktrees()

    def on_tabbed_content_tab_activated(self, event: TabbedContent.TabActivated) -> None:
        if event.pane.id == "pull-requests-pane":
            self.populate_pull_requests()
            return

    def get_repository_branch(self, repo: pygit2.Repository) -> str:
        if repo.head_is_detached:
            return "(detached HEAD)"
        if repo.head_is_unborn:
            return "(unborn branch)"

        return repo.head.shorthand

    def get_worktree_branch(self, worktree) -> str:
        repo_path = pygit2.discover_repository(worktree.path)
        if repo_path is None:
            return "(unknown branch)"

        try:
            worktree_repo = pygit2.Repository(repo_path)
        except pygit2.GitError:
            return "(unknown branch)"

        return self.get_repository_branch(worktree_repo)

    def get_worktree_name(self, path: str) -> str:
        normalized_path = os.path.normpath(path)
        return os.path.basename(normalized_path) or normalized_path

    def build_current_worktree_view(self, repo: pygit2.Repository) -> WorktreeView:
        path = os.path.normpath(repo.workdir or repo.path)
        return WorktreeView(
            name=self.get_worktree_name(path),
            branch=self.get_repository_branch(repo),
            path=path,
        )

    def build_worktree_view(self, worktree) -> WorktreeView:
        return WorktreeView(
            name=worktree.name,
            branch=self.get_worktree_branch(worktree),
            path=os.path.normpath(worktree.path),
        )

    def render_worktree(self, worktree: WorktreeView) -> ListItem:
        return ListItem(
            Vertical(
                Label(worktree.name, classes="worktree-name"),
                Static("Branch: " + worktree.branch, classes="worktree-branch"),
                Static("Path: " + worktree.display_path, classes="worktree-path"),
                classes="worktree-item",
            ),
            name=worktree.path,
        )

    def load_pull_request_list_views(self) -> list[PullRequestListView]:
        return [
            PullRequestListView(
                number=142,
                title="Add pull request list tab",
                branch="feature/pull-request-tab",
                remote="origin",
                status="Open",
                is_draft_mode=True,
            ),
            PullRequestListView(
                number=137,
                title="Include current repo in worktree list",
                branch="dev",
                remote="origin",
                status="Merged",
                is_draft_mode=False,
            ),
            PullRequestListView(
                number=128,
                title="Experiment with worktree browser",
                branch="spike/worktree-browser",
                remote=None,
                status="Closed",
                is_draft_mode=False,
            ),
        ]

    def render_pull_request(self, pull_request: PullRequestListView) -> ListItem:
        return ListItem(
            Vertical(
                Label(
                    f"#{pull_request.number} {pull_request.title}",
                    classes="pull-request-title",
                ),
                Static(
                    "Branch: " + pull_request.branch_display,
                    classes="pull-request-branch",
                ),
                Static(
                    "Status: " + pull_request.status_display,
                    classes="pull-request-status",
                ),
                classes="pull-request-item",
            ),
            name=str(pull_request.number),
        )

    def populate_worktrees(self) -> None:
        # Find the repo
        cwd = os.getcwd()
        repo_path = pygit2.discover_repository(cwd)
        if repo_path is None:
            self.log("Unable to discover repository", cwd=cwd)
            self.exit(message=f"Not inside a git repository: {cwd}")
            return

        repo = pygit2.Repository(repo_path)
        self.log("Repository discovered", workdir=repo.workdir, git_dir=repo.path)

        # Get details for all worktrees
        worktree_names = repo.list_worktrees()
        worktree_infos = [repo.lookup_worktree(w) for w in worktree_names]

        # Get the list object
        list_view = self.query_one("#worktrees", ListView)
        list_view.clear()

        # Populate the list
        worktree_views = [self.build_current_worktree_view(repo)]
        seen_paths = {worktree_views[0].path}
        for worktree in worktree_infos:
            worktree_view = self.build_worktree_view(worktree)
            if worktree_view.path in seen_paths:
                continue

            worktree_views.append(worktree_view)
            seen_paths.add(worktree_view.path)

        for worktree in worktree_views:
            list_view.append(self.render_worktree(worktree))

        self.log(
            "Prepared worktree view",
            count=len(worktree_views),
            worktrees=[asdict(worktree) for worktree in worktree_views],
        )
        list_view.focus()

    def populate_pull_requests(self) -> None:
        if self.pull_requests_loaded:
            return

        list_view = self.query_one("#pull-requests", ListView)
        list_view.clear()

        pull_requests = self.load_pull_request_list_views()
        for pull_request in pull_requests:
            list_view.append(self.render_pull_request(pull_request))

        self.log(
            "Prepared pull request view",
            count=len(pull_requests),
            pull_requests=[asdict(pull_request) for pull_request in pull_requests],
        )
        self.pull_requests_loaded = True


def main() -> None:
    selection = WorktreeApp().run()
    if selection:
        print(f"You selected: {selection}")


if __name__ == "__main__":
    main()
