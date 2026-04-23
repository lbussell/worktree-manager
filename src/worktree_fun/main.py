import os
from dataclasses import (
    asdict,
    dataclass,
)

import pygit2

from textual.app import (
    App,
    ComposeResult,
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
)


@dataclass(frozen=True, slots=True)
class WorktreeView:
    name: str
    branch: str
    path: str


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
    """

    BINDINGS = [
        ("q", "quit", "Quit"),
    ]

    def compose(self) -> ComposeResult:
        yield Header()
        yield ListView(id="worktrees")
        yield Footer()

    def on_mount(self) -> None:
        self.title = "worktree fun"
        self.log("Mounting worktree app", cwd=os.getcwd())
        self.populate()

    def get_worktree_branch(self, worktree) -> str:
        repo_path = pygit2.discover_repository(worktree.path)
        if repo_path is None:
            return "(unknown branch)"

        try:
            worktree_repo = pygit2.Repository(repo_path)
        except pygit2.GitError:
            return "(unknown branch)"

        if worktree_repo.head_is_detached:
            return "(detached HEAD)"
        if worktree_repo.head_is_unborn:
            return "(unborn branch)"

        return worktree_repo.head.shorthand

    def build_worktree_view(self, worktree) -> WorktreeView:
        return WorktreeView(
            name=worktree.name,
            branch=self.get_worktree_branch(worktree),
            path=worktree.path,
        )

    def render_worktree(self, worktree: WorktreeView) -> ListItem:
        return ListItem(
            Vertical(
                Label(worktree.name, classes="worktree-name"),
                Static("Branch: " + worktree.branch, classes="worktree-branch"),
                Static("Path: " + worktree.path, classes="worktree-path"),
                classes="worktree-item",
            ),
            name=worktree.path,
        )

    def populate(self) -> None:
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
        worktree_views = [self.build_worktree_view(worktree) for worktree in worktree_infos]
        for worktree in worktree_views:
            list_view.append(self.render_worktree(worktree))

        self.log(
            "Prepared worktree view",
            count=len(worktree_views),
            worktrees=[asdict(worktree) for worktree in worktree_views],
        )
        list_view.focus()


def main() -> None:
    selection = WorktreeApp().run()
    if selection:
        print(f"You selected: {selection}")


if __name__ == "__main__":
    main()
