import json
import os
import subprocess
from dataclasses import (
    asdict,
)
from typing import (
    Literal,
)

import pygit2
from textual import (
    work,
)
from textual.app import (
    App,
    ComposeResult,
)
from textual.containers import (
    Vertical,
)
from textual.lazy import (
    Lazy,
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
    Tabs,
)
from worktree_fun.pull_request_list_view import (
    PullRequestListView,
)
from worktree_fun.vim_list_view import (
    VimListView,
)
from worktree_fun.worktree_view import (
    WorktreeView,
)

class WorktreeApp(App):
    CSS_PATH = "worktree_app.tcss"

    BINDINGS = [
        ("h", "previous_tab", "Previous tab"),
        ("l", "next_tab", "Next tab"),
        ("q", "quit", "Quit"),
    ]

    pull_requests_loaded: bool
    pull_requests_loading: bool

    def compose(self) -> ComposeResult:
        yield Header()
        with TabbedContent(initial="worktrees-pane"):
            with TabPane("Worktrees", id="worktrees-pane"):
                yield VimListView(id="worktrees")
            with TabPane("Pull Requests", id="pull-requests-pane"):
                yield Lazy(VimListView(id="pull-requests"))
        yield Footer()

    def on_mount(self) -> None:
        self.title = "worktree fun"
        self.pull_requests_loaded = False
        self.pull_requests_loading = False
        self.log("Mounting worktree app", cwd=os.getcwd())
        self.populate_worktrees()
        self.query_one("#worktrees", VimListView).focus()

    def on_tabbed_content_tab_activated(self, event: TabbedContent.TabActivated) -> None:
        if event.pane.id == "pull-requests-pane":
            self.populate_pull_requests()
            self.query_one("#pull-requests", VimListView).focus()
            return

        if event.pane.id == "worktrees-pane":
            self.query_one("#worktrees", VimListView).focus()

    def action_previous_tab(self) -> None:
        self.query_one(Tabs).action_previous_tab()

    def action_next_tab(self) -> None:
        self.query_one(Tabs).action_next_tab()

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

    def get_pull_request_state(
        self,
        state: str,
    ) -> Literal["Open", "Merged", "Closed"]:
        state_map: dict[str, Literal["Open", "Merged", "Closed"]] = {
            "OPEN": "Open",
            "MERGED": "Merged",
            "CLOSED": "Closed",
        }
        if state not in state_map:
            raise ValueError(f"Unsupported pull request state: {state}")

        return state_map[state]

    def build_pull_request_list_view(
        self,
        pull_request_data: dict[str, object],
    ) -> PullRequestListView:
        number = pull_request_data.get("number")
        if not isinstance(number, int):
            raise ValueError(f"Expected integer pull request number, got: {number!r}")

        title = pull_request_data.get("title")
        if not isinstance(title, str):
            raise ValueError(f"Expected string pull request title, got: {title!r}")

        branch = pull_request_data.get("headRefName")
        if not isinstance(branch, str):
            raise ValueError(f"Expected string pull request branch, got: {branch!r}")

        is_draft_mode = pull_request_data.get("isDraft")
        if not isinstance(is_draft_mode, bool):
            raise ValueError(
                f"Expected boolean pull request draft mode, got: {is_draft_mode!r}"
            )

        state = pull_request_data.get("state")
        if not isinstance(state, str):
            raise ValueError(f"Expected string pull request state, got: {state!r}")

        remote: str | None = None
        remote_data = pull_request_data.get("headRepositoryOwner")
        if remote_data is not None:
            if not isinstance(remote_data, dict):
                raise ValueError(
                    "Expected pull request remote owner data to be an object, "
                    f"got: {remote_data!r}"
                )

            remote_login = remote_data.get("login")
            if remote_login is None:
                remote = None
            elif isinstance(remote_login, str):
                remote = remote_login
            else:
                raise ValueError(
                    "Expected pull request remote owner login to be a string, "
                    f"got: {remote_login!r}"
                )

        return PullRequestListView(
            number=number,
            title=title,
            branch=branch,
            remote=remote,
            status=self.get_pull_request_state(state),
            is_draft_mode=is_draft_mode,
        )

    def load_pull_request_list_views(self) -> list[PullRequestListView]:
        result = subprocess.run(
            [
                "gh",
                "pr",
                "list",
                "--state",
                "all",
                "--limit",
                "100",
                "--json",
                "number,title,headRefName,isDraft,state,headRepositoryOwner",
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        pull_request_data = json.loads(result.stdout)
        if not isinstance(pull_request_data, list):
            raise ValueError(
                "Expected gh pr list JSON output to be a list, "
                f"got: {pull_request_data!r}"
            )

        return [
            self.build_pull_request_list_view(pull_request)
            for pull_request in pull_request_data
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

    def render_pull_request_message(self, message: str) -> ListItem:
        return ListItem(
            Static(message, classes="pull-request-message"),
            disabled=True,
        )

    def populate_worktrees(self) -> None:
        cwd = os.getcwd()
        repo_path = pygit2.discover_repository(cwd)
        if repo_path is None:
            self.log("Unable to discover repository", cwd=cwd)
            self.exit(message=f"Not inside a git repository: {cwd}")
            return

        repo = pygit2.Repository(repo_path)
        self.log("Repository discovered", workdir=repo.workdir, git_dir=repo.path)

        worktree_names = repo.list_worktrees()
        worktree_infos = [repo.lookup_worktree(w) for w in worktree_names]

        list_view = self.query_one("#worktrees", ListView)
        list_view.clear()

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

    def show_pull_request_message(self, message: str) -> None:
        list_view = self.query_one("#pull-requests", ListView)
        list_view.clear()
        list_view.append(self.render_pull_request_message(message))

    def show_loaded_pull_requests(
        self,
        pull_requests: list[PullRequestListView],
    ) -> None:
        list_view = self.query_one("#pull-requests", ListView)
        list_view.clear()

        if not pull_requests:
            list_view.append(self.render_pull_request_message("No pull requests found."))
        else:
            list_view.extend(
                self.render_pull_request(pull_request) for pull_request in pull_requests
            )

        self.log(
            "Prepared pull request view",
            count=len(pull_requests),
            pull_requests=[asdict(pull_request) for pull_request in pull_requests],
        )
        self.pull_requests_loaded = True
        self.pull_requests_loading = False

    def show_pull_request_load_error(self, message: str) -> None:
        self.show_pull_request_message("Unable to load pull requests: " + message)
        self.pull_requests_loading = False
        self.log("Unable to load pull requests", error=message)

    @work(
        thread=True,
        exclusive=True,
        group="pull-requests",
        exit_on_error=False,
    )
    def load_pull_requests(self) -> None:
        try:
            pull_requests = self.load_pull_request_list_views()
        except FileNotFoundError as error:
            self.call_from_thread(
                self.show_pull_request_load_error,
                f"`gh` command not found: {error}",
            )
            return
        except subprocess.CalledProcessError as error:
            error_message = error.stderr.strip() or error.stdout.strip() or str(error)
            self.call_from_thread(self.show_pull_request_load_error, error_message)
            return
        except json.JSONDecodeError as error:
            self.call_from_thread(
                self.show_pull_request_load_error,
                f"Invalid JSON from gh: {error.msg}",
            )
            return
        except ValueError as error:
            self.call_from_thread(self.show_pull_request_load_error, str(error))
            return

        self.call_from_thread(self.show_loaded_pull_requests, pull_requests)

    def populate_pull_requests(self) -> None:
        if self.pull_requests_loaded or self.pull_requests_loading:
            return

        self.pull_requests_loading = True
        self.show_pull_request_message("Loading pull requests...")
        self.load_pull_requests()


def main() -> None:
    selection = WorktreeApp().run()
    if selection:
        print(f"You selected: {selection}")


if __name__ == "__main__":
    main()
