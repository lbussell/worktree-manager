import pygit2
from textual import (
    work,
)
from textual.app import (
    ComposeResult,
)
from textual.containers import (
    Horizontal,
    Vertical,
)
from textual.screen import (
    ModalScreen,
)
from textual.widgets import (
    Button,
    Checkbox,
    Input,
    Label,
    Select,
    Static,
)

from worktree_fun.git_ops import (
    get_cached_create_worktree_branch_options,
    get_common_git_dir,
    get_worktree_storage_root,
    load_create_worktree_branch_options,
)
from worktree_fun.models import (
    CreateWorktreeBranchOptions,
    CreateWorktreeRequest,
)
from worktree_fun.utils import (
    normalize_worktree_name,
    shorten_home_path,
)


class CreateWorktreeModal(ModalScreen[CreateWorktreeRequest | None]):
    BINDINGS = [
        ("escape", "cancel_dialog", "Cancel"),
    ]

    def __init__(self, repo: pygit2.Repository) -> None:
        super().__init__()
        self.repo_git_dir = str(get_common_git_dir(repo))
        self.branch_options_loaded = False
        self.storage_root_display = shorten_home_path(str(get_worktree_storage_root(repo)))

    def compose(self) -> ComposeResult:
        yield Vertical(
            Label("Create Worktree", classes="create-worktree-title"),
            Label("Worktree name", classes="create-worktree-label"),
            Input(
                placeholder="feature/worktree-name",
                id="create-worktree-name",
            ),
            Static(
                "Worktrees are created under "
                + self.storage_root_display
                + ".",
                classes="create-worktree-help",
            ),
            Static(
                "Loading branches...",
                id="create-worktree-status",
                classes="create-worktree-status",
            ),
            Checkbox(
                "Create a new branch",
                value=False,
                id="create-worktree-create-branch",
            ),
            Vertical(
                Static(
                    "New branch name will match the worktree name.",
                    classes="create-worktree-help",
                ),
                Label("Remote", classes="create-worktree-label"),
                Select(
                    [],
                    allow_blank=True,
                    prompt="Select remote",
                    id="create-worktree-remote",
                ),
                Label("Base branch", classes="create-worktree-label"),
                Select(
                    [],
                    allow_blank=True,
                    prompt="Select base branch",
                    id="create-worktree-base-branch",
                ),
                id="create-worktree-new-branch-fields",
            ),
            Vertical(
                Label("Branch", classes="create-worktree-label"),
                Select(
                    [],
                    allow_blank=True,
                    prompt="Select branch",
                    id="create-worktree-existing-branch",
                ),
                id="create-worktree-existing-branch-fields",
            ),
            Horizontal(
                Button("Cancel", id="cancel-create-worktree"),
                Button(
                    "Create",
                    id="submit-create-worktree",
                    variant="primary",
                    disabled=True,
                ),
                classes="create-worktree-buttons",
            ),
            id="create-worktree-dialog",
        )

    def on_mount(self) -> None:
        create_branch_checkbox = self.query_one(
            "#create-worktree-create-branch",
            Checkbox,
        )
        create_branch_checkbox.disabled = True
        self.query_one("#create-worktree-remote", Select).disabled = True
        self.query_one("#create-worktree-base-branch", Select).disabled = True
        self.query_one("#create-worktree-existing-branch", Select).disabled = True
        self.refresh_base_branch_options()
        self.update_branch_mode_fields()
        self.query_one("#create-worktree-name", Input).focus()

        cached_branch_options = get_cached_create_worktree_branch_options(self.repo_git_dir)
        if cached_branch_options is not None:
            self.apply_branch_options(cached_branch_options)
            return

        self.load_branch_options()

    def on_checkbox_changed(self, event: Checkbox.Changed) -> None:
        if event.checkbox.id == "create-worktree-create-branch":
            self.update_branch_mode_fields()

    def on_select_changed(self, event: Select.Changed) -> None:
        if event.select.id == "create-worktree-remote":
            self.refresh_base_branch_options()

    def on_button_pressed(self, event: Button.Pressed) -> None:
        if event.button.id == "cancel-create-worktree":
            self.dismiss(None)
            return

        if event.button.id == "submit-create-worktree":
            request = self.build_request()
            if request is None:
                return

            self.dismiss(request)

    def action_cancel_dialog(self) -> None:
        self.dismiss(None)

    def show_branch_status(self, message: str, *, visible: bool) -> None:
        status_widget = self.query_one("#create-worktree-status", Static)
        status_widget.update(message)
        status_widget.display = visible

    def apply_branch_options(self, branch_options: CreateWorktreeBranchOptions) -> None:
        if self not in self.app.screen_stack:
            return

        self.branch_options_loaded = True
        create_branch_checkbox = self.query_one(
            "#create-worktree-create-branch",
            Checkbox,
        )
        submit_button = self.query_one("#submit-create-worktree", Button)
        remote_select = self.query_one("#create-worktree-remote", Select)
        existing_branch_select = self.query_one(
            "#create-worktree-existing-branch",
            Select,
        )

        local_branch_names = list(branch_options.local_branch_names)
        remote_names = list(branch_options.remote_names)
        remote_select.set_options(
            [(remote_name, remote_name) for remote_name in remote_names]
        )
        remote_select.disabled = not remote_names
        if remote_names:
            remote_select.value = remote_names[0]

        existing_branch_select.set_options(
            [(branch_name, branch_name) for branch_name in local_branch_names]
        )
        existing_branch_select.disabled = not local_branch_names
        if local_branch_names:
            existing_branch_select.value = local_branch_names[0]

        create_branch_checkbox.disabled = not remote_names
        create_branch_checkbox.value = bool(remote_names and not local_branch_names)
        submit_button.disabled = not (local_branch_names or remote_names)
        remote_name = remote_select.value if isinstance(remote_select.value, str) else None
        self.update_base_branch_options(branch_options, remote_name)
        self.update_branch_mode_fields()

        if local_branch_names or remote_names:
            self.show_branch_status("", visible=False)
            return

        self.show_branch_status("No branches are available.", visible=True)

    def show_branch_loading_error(self, message: str) -> None:
        if self not in self.app.screen_stack:
            return

        self.query_one("#submit-create-worktree", Button).disabled = True
        self.show_branch_status(
            "Unable to load branches: " + message,
            visible=True,
        )

    @work(
        thread=True,
        exclusive=True,
        group="create-worktree-branches",
        exit_on_error=False,
    )
    def load_branch_options(self) -> None:
        try:
            branch_options = load_create_worktree_branch_options(self.repo_git_dir)
        except (OSError, ValueError, pygit2.GitError) as error:
            self.app.call_from_thread(self.show_branch_loading_error, str(error))
            return

        self.app.call_from_thread(self.apply_branch_options, branch_options)

    def refresh_base_branch_options(self) -> None:
        remote_select = self.query_one("#create-worktree-remote", Select)
        remote_name = remote_select.value if isinstance(remote_select.value, str) else None
        self.update_base_branch_options(remote_name=remote_name)

    def update_base_branch_options(
        self,
        branch_options: CreateWorktreeBranchOptions | None = None,
        remote_name: str | None = None,
    ) -> None:
        if branch_options is None:
            cached_branch_options = get_cached_create_worktree_branch_options(
                self.repo_git_dir
            )
            branch_options = cached_branch_options

        base_branch_select = self.query_one("#create-worktree-base-branch", Select)
        if branch_options is None:
            base_branch_select.set_options([])
            base_branch_select.disabled = True
            return

        branch_names = list(branch_options.branch_names_for_remote(remote_name))

        base_branch_select.set_options(
            [(branch_name, branch_name) for branch_name in branch_names]
        )
        base_branch_select.disabled = not branch_names
        if branch_names:
            base_branch_select.value = branch_names[0]

    def update_branch_mode_fields(self) -> None:
        create_branch_checkbox = self.query_one(
            "#create-worktree-create-branch",
            Checkbox,
        )
        create_new_branch = create_branch_checkbox.value
        self.query_one("#create-worktree-new-branch-fields", Vertical).display = (
            create_new_branch
        )
        self.query_one("#create-worktree-existing-branch-fields", Vertical).display = (
            not create_new_branch
        )

    def build_request(self) -> CreateWorktreeRequest | None:
        if not self.branch_options_loaded:
            self.notify(
                "Branch data is still loading.",
                title="Please wait",
                severity="warning",
            )
            return None

        worktree_name_input = self.query_one("#create-worktree-name", Input)
        try:
            worktree_name = normalize_worktree_name(worktree_name_input.value)
        except ValueError as error:
            self.notify(str(error), title="Invalid worktree name", severity="error")
            return None

        create_branch_checkbox = self.query_one(
            "#create-worktree-create-branch",
            Checkbox,
        )
        if create_branch_checkbox.value:
            remote_name = self.query_one("#create-worktree-remote", Select).value
            if not isinstance(remote_name, str):
                self.notify("Select a remote.", title="Missing remote", severity="error")
                return None

            base_branch_name = self.query_one("#create-worktree-base-branch", Select).value
            if not isinstance(base_branch_name, str):
                self.notify(
                    "Select a base branch.",
                    title="Missing base branch",
                    severity="error",
                )
                return None

            return CreateWorktreeRequest(
                worktree_name=worktree_name,
                create_new_branch=True,
                remote_name=remote_name,
                base_branch_name=base_branch_name,
            )

        existing_branch_name = self.query_one(
            "#create-worktree-existing-branch",
            Select,
        ).value
        if not isinstance(existing_branch_name, str):
            self.notify(
                "Select a branch to use.",
                title="Missing branch",
                severity="error",
            )
            return None

        return CreateWorktreeRequest(
            worktree_name=worktree_name,
            create_new_branch=False,
            existing_branch_name=existing_branch_name,
        )
