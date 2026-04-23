import json
import os
import subprocess
from dataclasses import (
    asdict,
)

from textual import work
from textual.app import (
    App,
    ComposeResult,
)
from textual.lazy import (
    Lazy,
)
from textual.widgets import (
    Footer,
    Header,
    ListView,
    TabbedContent,
    TabPane,
    Tabs,
)

from worktree_fun.git_ops import (
    create_worktree,
    delete_worktree,
    discover_repository,
    load_worktree_views,
    render_worktree,
)
from worktree_fun.github import (
    PullRequestListView,
    load_pull_request_list_views,
    render_pull_request,
    render_pull_request_message,
)
from worktree_fun.models import (
    CreateWorktreeRequest,
    DeleteWorktreeRequest,
    DeleteWorktreeResult,
    ForceDeleteBranchRequiredError,
    WorktreeView,
)
from worktree_fun.screens import (
    CreateWorktreeModal,
    DeleteWorktreeModal,
    ForceDeleteBranchModal,
    WorktreeDetailsModal,
)
from worktree_fun.vim_list_view import (
    VimListView,
)


class WorktreeApp(App):
    CSS_PATH = "worktree_app.tcss"

    BINDINGS = [
        ("c", "create_worktree", "Create worktree"),
        ("d", "delete_worktree", "Delete worktree"),
        ("h", "previous_tab", "Previous tab"),
        ("l", "next_tab", "Next tab"),
        ("q", "quit", "Quit"),
    ]

    pull_requests_loaded: bool
    pull_requests_loading: bool
    worktrees_by_path: dict[str, WorktreeView]

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
        self.worktrees_by_path = {}
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

    def action_create_worktree(self) -> None:
        if self.query_one(TabbedContent).active != "worktrees-pane":
            return

        if self.focused != self.query_one("#worktrees", VimListView):
            return

        cwd = os.getcwd()
        try:
            repo = discover_repository(cwd)
        except ValueError as error:
            self.notify(str(error), title="Unable to open repository", severity="error")
            return

        self.push_screen(CreateWorktreeModal(repo), self.handle_create_worktree_request)

    def handle_create_worktree_request(
        self,
        request: CreateWorktreeRequest | None,
    ) -> None:
        if request is None:
            return

        cwd = os.getcwd()
        try:
            repo = discover_repository(cwd)
            worktree = create_worktree(repo, request)
        except (ValueError, OSError) as error:
            self.notify(
                str(error),
                title="Unable to create worktree",
                severity="error",
                timeout=10,
            )
            return

        self.populate_worktrees(highlight_path=worktree.path)
        self.query_one("#worktrees", VimListView).focus()
        self.notify(
            "Created "
            + worktree.name
            + " at "
            + worktree.display_path,
            title="Worktree created",
        )

    def get_selected_worktree(self) -> WorktreeView | None:
        highlighted_item = self.query_one("#worktrees", VimListView).highlighted_child
        if highlighted_item is None or highlighted_item.name is None:
            return None

        return self.worktrees_by_path.get(highlighted_item.name)

    def action_delete_worktree(self) -> None:
        if self.query_one(TabbedContent).active != "worktrees-pane":
            return

        if self.focused != self.query_one("#worktrees", VimListView):
            return

        worktree = self.get_selected_worktree()
        if worktree is None:
            return

        if not worktree.is_linked:
            self.notify(
                "The current worktree can't be deleted from this list.",
                title="Can't delete worktree",
                severity="warning",
            )
            return

        self.push_screen(DeleteWorktreeModal(worktree), self.handle_delete_worktree_request)

    def handle_delete_worktree_request(
        self,
        request: DeleteWorktreeRequest | None,
    ) -> None:
        if request is None:
            return

        self.delete_selected_worktree(request)

    def confirm_force_delete_branch(
        self,
        request: DeleteWorktreeRequest,
        reason: str,
    ) -> None:
        self.push_screen(
            ForceDeleteBranchModal(request, reason),
            self.handle_force_delete_branch_request,
        )

    def handle_force_delete_branch_request(
        self,
        request: DeleteWorktreeRequest | None,
    ) -> None:
        self.query_one("#worktrees", VimListView).focus()
        if request is None:
            return

        self.delete_selected_worktree(request)

    def show_deleted_worktree(self, result: DeleteWorktreeResult) -> None:
        self.populate_worktrees()
        self.query_one("#worktrees", VimListView).focus()

        message = "Deleted " + result.name + " at " + result.display_path
        if result.deleted_branch_name is not None:
            if result.deleted_branch_was_forced:
                message += " and force deleted branch " + result.deleted_branch_name
            else:
                message += " and deleted branch " + result.deleted_branch_name
        elif result.branch_already_absent_name is not None:
            message += ". Branch " + result.branch_already_absent_name + " was already absent"

        self.notify(message, title="Worktree deleted")

    def show_delete_worktree_error(self, message: str) -> None:
        self.populate_worktrees()
        self.query_one("#worktrees", VimListView).focus()
        self.notify(
            message,
            title="Unable to delete worktree",
            severity="error",
            timeout=10,
        )

    def action_previous_tab(self) -> None:
        self.query_one(Tabs).action_previous_tab()

    def action_next_tab(self) -> None:
        self.query_one(Tabs).action_next_tab()

    def on_list_view_selected(self, event: ListView.Selected) -> None:
        if event.list_view.id != "worktrees":
            return

        worktree_path = event.item.name
        if worktree_path is None:
            return

        worktree = self.worktrees_by_path.get(worktree_path)
        if worktree is None:
            self.log("Selected worktree not found", path=worktree_path)
            return

        self.push_screen(WorktreeDetailsModal(worktree))

    def populate_worktrees(self, highlight_path: str | None = None) -> None:
        cwd = os.getcwd()
        try:
            repo = discover_repository(cwd)
        except ValueError as error:
            self.log("Unable to discover repository", cwd=cwd)
            self.exit(message=str(error))
            return

        self.log("Repository discovered", workdir=repo.workdir, git_dir=repo.path)
        worktree_views = load_worktree_views(repo)
        self.worktrees_by_path = {worktree.path: worktree for worktree in worktree_views}

        list_view = self.query_one("#worktrees", VimListView)
        list_view.clear()

        highlight_index: int | None = None
        for index, worktree in enumerate(worktree_views):
            list_view.append(render_worktree(worktree))
            if worktree.path == highlight_path:
                highlight_index = index

        if highlight_index is not None:
            list_view.index = highlight_index
        elif worktree_views:
            list_view.index = 0

        self.log(
            "Prepared worktree view",
            count=len(worktree_views),
            worktrees=[asdict(worktree) for worktree in worktree_views],
        )

    def show_pull_request_message(self, message: str) -> None:
        list_view = self.query_one("#pull-requests", VimListView)
        list_view.clear()
        list_view.append(render_pull_request_message(message))

    def show_loaded_pull_requests(
        self,
        pull_requests: list[PullRequestListView],
    ) -> None:
        list_view = self.query_one("#pull-requests", VimListView)
        list_view.clear()

        if not pull_requests:
            list_view.append(render_pull_request_message("No pull requests found."))
        else:
            list_view.extend(
                render_pull_request(pull_request) for pull_request in pull_requests
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
            pull_requests = load_pull_request_list_views()
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

    @work(
        thread=True,
        exclusive=True,
        group="delete-worktree",
        exit_on_error=False,
    )
    def delete_selected_worktree(self, request: DeleteWorktreeRequest) -> None:
        cwd = os.getcwd()
        try:
            repo = discover_repository(cwd)
            result = delete_worktree(repo, request)
        except ForceDeleteBranchRequiredError as error:
            self.call_from_thread(
                self.confirm_force_delete_branch,
                request,
                str(error),
            )
            return
        except (ValueError, OSError) as error:
            self.call_from_thread(self.show_delete_worktree_error, str(error))
            return

        self.call_from_thread(self.show_deleted_worktree, result)
