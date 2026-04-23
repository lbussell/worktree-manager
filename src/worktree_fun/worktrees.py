import hashlib
import os
import re
import shutil
from dataclasses import (
    dataclass,
)
from pathlib import (
    Path,
)
from threading import (
    Lock,
)

import pygit2
from pygit2.enums import (
    BranchType,
)
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
    ListItem,
    Select,
    Static,
)

_WORKTREE_IDENTIFIER_PATTERN = re.compile(r"[^A-Za-z0-9._-]+")


@dataclass(frozen=True, slots=True)
class WorktreeView:
    name: str
    branch: str
    path: str
    linked_worktree_name: str | None = None
    local_branch_name: str | None = None

    @property
    def display_path(self) -> str:
        return shorten_home_path(self.path)

    @property
    def is_linked(self) -> bool:
        return self.linked_worktree_name is not None

    @property
    def can_delete_branch(self) -> bool:
        return self.local_branch_name is not None


@dataclass(frozen=True, slots=True)
class CreateWorktreeRequest:
    worktree_name: str
    create_new_branch: bool
    existing_branch_name: str | None = None
    remote_name: str | None = None
    base_branch_name: str | None = None


@dataclass(frozen=True, slots=True)
class DeleteWorktreeRequest:
    name: str
    path: str
    linked_worktree_name: str
    delete_branch: bool
    branch_name: str | None = None
    force_delete_branch: bool = False

    @property
    def display_path(self) -> str:
        return shorten_home_path(self.path)

    def with_force_delete_branch(self) -> "DeleteWorktreeRequest":
        return DeleteWorktreeRequest(
            name=self.name,
            path=self.path,
            linked_worktree_name=self.linked_worktree_name,
            delete_branch=self.delete_branch,
            branch_name=self.branch_name,
            force_delete_branch=True,
        )


@dataclass(frozen=True, slots=True)
class DeleteWorktreeResult:
    name: str
    path: str
    deleted_branch_name: str | None = None
    deleted_branch_was_forced: bool = False
    branch_already_absent_name: str | None = None

    @property
    def display_path(self) -> str:
        return shorten_home_path(self.path)


class ForceDeleteBranchRequiredError(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class CreateWorktreeBranchOptions:
    local_branch_names: tuple[str, ...]
    remote_branch_names_by_remote: tuple[tuple[str, tuple[str, ...]], ...]

    @property
    def remote_names(self) -> tuple[str, ...]:
        return tuple(
            remote_name for remote_name, _branch_names in self.remote_branch_names_by_remote
        )

    def branch_names_for_remote(self, remote_name: str | None) -> tuple[str, ...]:
        if remote_name is None:
            return ()

        for known_remote_name, branch_names in self.remote_branch_names_by_remote:
            if known_remote_name == remote_name:
                return branch_names

        return ()


_BRANCH_OPTIONS_CACHE: dict[str, CreateWorktreeBranchOptions] = {}
_BRANCH_OPTIONS_CACHE_LOCK = Lock()


class WorktreeDetailsModal(ModalScreen[None]):
    BINDINGS = [
        ("escape", "close_dialog", "Close"),
        ("enter", "close_dialog", "Close"),
        ("q", "close_dialog", "Close"),
    ]

    def __init__(self, worktree: WorktreeView) -> None:
        super().__init__()
        self.worktree = worktree

    def compose(self) -> ComposeResult:
        yield Vertical(
            Label("Worktree Details", classes="worktree-details-title"),
            Static("Name: " + self.worktree.name, classes="worktree-details-field"),
            Static("Branch: " + self.worktree.branch, classes="worktree-details-field"),
            Static("Path: " + self.worktree.path, classes="worktree-details-field"),
            Button("Close", id="close-worktree-details", variant="primary"),
            id="worktree-details-dialog",
        )

    def on_button_pressed(self, event: Button.Pressed) -> None:
        if event.button.id == "close-worktree-details":
            self.dismiss(None)

    def action_close_dialog(self) -> None:
        self.dismiss(None)


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


class DeleteWorktreeModal(ModalScreen[DeleteWorktreeRequest | None]):
    BINDINGS = [
        ("escape", "cancel_dialog", "Cancel"),
    ]

    def __init__(self, worktree: WorktreeView) -> None:
        if worktree.linked_worktree_name is None:
            raise ValueError("Only linked worktrees can be deleted.")

        super().__init__()
        self.worktree = worktree

    def compose(self) -> ComposeResult:
        delete_branch_help = (
            "Also delete the local branch after the worktree is removed."
            if self.worktree.can_delete_branch
            else "This worktree is not on a local branch, so the branch can't be deleted."
        )
        yield Vertical(
            Label("Delete Worktree", classes="delete-worktree-title"),
            Static("Name: " + self.worktree.name, classes="delete-worktree-field"),
            Static("Branch: " + self.worktree.branch, classes="delete-worktree-field"),
            Static(
                "Path: " + self.worktree.display_path,
                classes="delete-worktree-field",
            ),
            Checkbox(
                "Delete branch as well",
                value=False,
                disabled=not self.worktree.can_delete_branch,
                id="delete-worktree-delete-branch",
            ),
            Static(delete_branch_help, classes="delete-worktree-help"),
            Horizontal(
                Button("Cancel", id="cancel-delete-worktree"),
                Button(
                    "Delete",
                    id="submit-delete-worktree",
                    variant="error",
                ),
                classes="delete-worktree-buttons",
            ),
            id="delete-worktree-dialog",
        )

    def on_mount(self) -> None:
        self.query_one("#cancel-delete-worktree", Button).focus()

    def on_button_pressed(self, event: Button.Pressed) -> None:
        if event.button.id == "cancel-delete-worktree":
            self.dismiss(None)
            return

        if event.button.id == "submit-delete-worktree":
            delete_branch = self.query_one(
                "#delete-worktree-delete-branch",
                Checkbox,
            ).value
            self.dismiss(
                DeleteWorktreeRequest(
                    name=self.worktree.name,
                    path=self.worktree.path,
                    linked_worktree_name=self.worktree.linked_worktree_name,
                    delete_branch=delete_branch,
                    branch_name=self.worktree.local_branch_name,
                )
            )

    def action_cancel_dialog(self) -> None:
        self.dismiss(None)


class ForceDeleteBranchModal(ModalScreen[DeleteWorktreeRequest | None]):
    BINDINGS = [
        ("escape", "cancel_dialog", "Cancel"),
    ]

    def __init__(self, request: DeleteWorktreeRequest, reason: str) -> None:
        super().__init__()
        self.request = request
        self.reason = reason

    def compose(self) -> ComposeResult:
        branch_name = self.request.branch_name or "(unknown branch)"
        yield Vertical(
            Label("Force Delete Branch", classes="force-delete-branch-title"),
            Static(self.reason, classes="force-delete-branch-warning"),
            Static(
                "Worktree: " + self.request.name,
                classes="force-delete-branch-field",
            ),
            Static(
                "Branch: " + branch_name,
                classes="force-delete-branch-field",
            ),
            Static(
                "Path: " + self.request.display_path,
                classes="force-delete-branch-field",
            ),
            Static(
                "This will delete the worktree and then force delete the branch.",
                classes="force-delete-branch-help",
            ),
            Horizontal(
                Button("Cancel", id="cancel-force-delete-branch"),
                Button(
                    "Force Delete",
                    id="submit-force-delete-branch",
                    variant="error",
                ),
                classes="force-delete-branch-buttons",
            ),
            id="force-delete-branch-dialog",
        )

    def on_mount(self) -> None:
        self.query_one("#cancel-force-delete-branch", Button).focus()

    def on_button_pressed(self, event: Button.Pressed) -> None:
        if event.button.id == "cancel-force-delete-branch":
            self.dismiss(None)
            return

        if event.button.id == "submit-force-delete-branch":
            self.dismiss(self.request.with_force_delete_branch())

    def action_cancel_dialog(self) -> None:
        self.dismiss(None)


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


def get_repo_root_path(repo: pygit2.Repository) -> Path:
    git_dir = Path(repo.path).resolve()
    if git_dir.name == ".git":
        return git_dir.parent

    if git_dir.parent.name == "worktrees" and git_dir.parent.parent.name == ".git":
        return git_dir.parent.parent.parent

    if repo.workdir is not None:
        return Path(repo.workdir).resolve()

    return git_dir.parent


def get_repo_name(repo: pygit2.Repository) -> str:
    return get_repo_root_path(repo).name


def get_common_git_dir(repo: pygit2.Repository) -> Path:
    git_dir = Path(repo.path).resolve()
    if git_dir.name == ".git":
        return git_dir

    if git_dir.parent.name == "worktrees" and git_dir.parent.parent.name == ".git":
        return git_dir.parent.parent

    return git_dir


def get_worktree_storage_root(repo: pygit2.Repository) -> Path:
    return Path.home() / "w" / get_repo_name(repo)


def get_worktree_target_path(repo: pygit2.Repository, worktree_name: str) -> Path:
    return get_worktree_storage_root(repo) / Path(worktree_name)


def get_worktree_identifier(worktree_name: str) -> str:
    safe_name = _WORKTREE_IDENTIFIER_PATTERN.sub("-", worktree_name).strip("-.")
    if not safe_name:
        safe_name = "worktree"

    if safe_name == worktree_name:
        return safe_name

    digest = hashlib.sha1(worktree_name.encode("utf-8")).hexdigest()[:8]
    return f"{safe_name}-{digest}"


def get_worktree_display_name(
    repo: pygit2.Repository,
    path: str,
    fallback_name: str,
) -> str:
    storage_root = get_worktree_storage_root(repo).resolve()
    worktree_path = Path(path).resolve()
    try:
        return worktree_path.relative_to(storage_root).as_posix()
    except ValueError:
        return fallback_name


def discover_repository(cwd: str) -> pygit2.Repository:
    repo_path = pygit2.discover_repository(cwd)
    if repo_path is None:
        raise ValueError(f"Not inside a git repository: {cwd}")

    return pygit2.Repository(repo_path)


def open_repository_at_path(path: str) -> pygit2.Repository | None:
    repo_path = pygit2.discover_repository(path)
    if repo_path is None:
        return None

    try:
        return pygit2.Repository(repo_path)
    except pygit2.GitError:
        return None


def get_repository_branch_details(repo: pygit2.Repository) -> tuple[str, str | None]:
    if repo.head_is_detached:
        return "(detached HEAD)", None
    if repo.head_is_unborn:
        return "(unborn branch)", None

    branch_name = repo.head.shorthand
    local_branch = repo.lookup_branch(branch_name, BranchType.LOCAL)
    return branch_name, branch_name if local_branch is not None else None


def get_repository_branch(repo: pygit2.Repository) -> str:
    branch_name, _local_branch_name = get_repository_branch_details(repo)
    return branch_name


def get_worktree_branch_details(worktree) -> tuple[str, str | None]:
    worktree_repo = open_repository_at_path(worktree.path)
    if worktree_repo is None:
        return "(unknown branch)", None

    return get_repository_branch_details(worktree_repo)


def get_worktree_name(path: str) -> str:
    normalized_path = os.path.normpath(path)
    return os.path.basename(normalized_path) or normalized_path


def build_current_worktree_view(repo: pygit2.Repository) -> WorktreeView:
    path = os.path.normpath(repo.workdir or repo.path)
    default_name = get_worktree_name(path)
    branch_name, local_branch_name = get_repository_branch_details(repo)
    return WorktreeView(
        name=get_worktree_display_name(repo, path, default_name),
        branch=branch_name,
        path=path,
        local_branch_name=local_branch_name,
    )


def build_worktree_view(repo: pygit2.Repository, worktree) -> WorktreeView:
    branch_name, local_branch_name = get_worktree_branch_details(worktree)
    return WorktreeView(
        name=get_worktree_display_name(repo, worktree.path, worktree.name),
        branch=branch_name,
        path=os.path.normpath(worktree.path),
        linked_worktree_name=worktree.name,
        local_branch_name=local_branch_name,
    )


def get_branch_commit_time(repo: pygit2.Repository, refish: str) -> int:
    commit, _ = repo.resolve_refish(refish)
    return commit.commit_time


def list_available_local_branch_names(repo: pygit2.Repository) -> list[str]:
    branch_names = []
    for branch_name in repo.listall_branches(BranchType.LOCAL):
        branch = repo.lookup_branch(branch_name, BranchType.LOCAL)
        if branch is None or branch.is_checked_out():
            continue

        branch_names.append(branch_name)

    branch_names.sort(
        key=lambda branch_name: get_branch_commit_time(repo, branch_name),
        reverse=True,
    )
    return branch_names


def list_remote_branch_names_by_remote(
    repo: pygit2.Repository,
) -> dict[str, list[str]]:
    branches_by_remote: dict[str, list[str]] = {}
    for full_branch_name in repo.listall_branches(BranchType.REMOTE):
        remote_name, branch_name = full_branch_name.split("/", 1)
        if branch_name == "HEAD":
            continue

        branches_by_remote.setdefault(remote_name, []).append(full_branch_name)

    sorted_branches_by_remote: dict[str, list[str]] = {}
    for remote_name in sorted(branches_by_remote):
        remote_branch_names = branches_by_remote[remote_name]
        remote_branch_names.sort(
            key=lambda branch_name: get_branch_commit_time(repo, branch_name),
            reverse=True,
        )
        sorted_branches_by_remote[remote_name] = [
            branch_name.split("/", 1)[1] for branch_name in remote_branch_names
        ]

    return sorted_branches_by_remote


def get_cached_create_worktree_branch_options(
    repo_git_dir: str,
) -> CreateWorktreeBranchOptions | None:
    with _BRANCH_OPTIONS_CACHE_LOCK:
        return _BRANCH_OPTIONS_CACHE.get(repo_git_dir)


def load_create_worktree_branch_options(
    repo_git_dir: str,
) -> CreateWorktreeBranchOptions:
    cached_branch_options = get_cached_create_worktree_branch_options(repo_git_dir)
    if cached_branch_options is not None:
        return cached_branch_options

    repo = pygit2.Repository(repo_git_dir)
    branch_options = CreateWorktreeBranchOptions(
        local_branch_names=tuple(list_available_local_branch_names(repo)),
        remote_branch_names_by_remote=tuple(
            (
                remote_name,
                tuple(branch_names),
            )
            for remote_name, branch_names in list_remote_branch_names_by_remote(
                repo
            ).items()
        ),
    )
    with _BRANCH_OPTIONS_CACHE_LOCK:
        _BRANCH_OPTIONS_CACHE[repo_git_dir] = branch_options

    return branch_options


def invalidate_create_worktree_branch_options_cache(repo: pygit2.Repository) -> None:
    repo_git_dir = str(get_common_git_dir(repo))
    with _BRANCH_OPTIONS_CACHE_LOCK:
        _BRANCH_OPTIONS_CACHE.pop(repo_git_dir, None)


def load_worktree_views(repo: pygit2.Repository) -> list[WorktreeView]:
    worktree_names = repo.list_worktrees()
    worktree_infos = [
        repo.lookup_worktree(worktree_name) for worktree_name in worktree_names
    ]

    worktree_views = [build_current_worktree_view(repo)]
    seen_paths = {worktree_views[0].path}
    for worktree in worktree_infos:
        worktree_view = build_worktree_view(repo, worktree)
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


def ensure_worktree_is_clean(path: Path) -> None:
    worktree_repo = open_repository_at_path(str(path))
    if worktree_repo is None:
        raise ValueError(
            "Unable to inspect worktree state at " + shorten_home_path(str(path))
        )

    if worktree_repo.status():
        raise ValueError(
            "Worktree has uncommitted changes at "
            + shorten_home_path(str(path))
            + ". Commit, stash, or clean it before deleting."
        )


def get_branch_delete_base(
    repo: pygit2.Repository,
    branch: pygit2.Branch,
) -> tuple[str, pygit2.Oid] | None:
    try:
        upstream = branch.upstream
    except pygit2.GitError as error:
        raise ValueError(
            f"Unable to determine the upstream for branch {branch.shorthand}: {error}"
        ) from error

    if upstream is not None and upstream.target is not None:
        return upstream.shorthand, upstream.target

    if repo.head_is_detached or repo.head_is_unborn or repo.head.target is None:
        return None

    return repo.head.shorthand, repo.head.target


def ensure_branch_can_be_deleted(
    repo: pygit2.Repository,
    branch: pygit2.Branch,
) -> None:
    if branch.target is None:
        raise ValueError(f"Branch {branch.shorthand} does not point to a commit.")

    delete_base = get_branch_delete_base(repo, branch)
    if delete_base is None:
        raise ForceDeleteBranchRequiredError(
            "Git couldn't determine whether branch "
            + branch.shorthand
            + " is safely merged. Force delete it only if you're sure you don't need that branch history."
        )

    base_name, base_target = delete_base
    if branch.target == base_target or repo.descendant_of(base_target, branch.target):
        return

    raise ForceDeleteBranchRequiredError(
        "Branch "
        + branch.shorthand
        + " is not fully merged into "
        + base_name
        + ". Force delete it only if you're sure you don't need that branch history."
    )


def delete_worktree(
    repo: pygit2.Repository,
    request: DeleteWorktreeRequest,
) -> DeleteWorktreeResult:
    if request.linked_worktree_name not in repo.list_worktrees():
        raise ValueError(f"Worktree not found: {request.name}")

    worktree = repo.lookup_worktree(request.linked_worktree_name)
    worktree_path = Path(request.path)

    branch = None
    if request.delete_branch:
        if request.branch_name is None:
            raise ValueError(
                "This worktree is not on a local branch, so the branch can't be deleted."
            )

        branch = repo.lookup_branch(request.branch_name, BranchType.LOCAL)
        if branch is not None and not request.force_delete_branch:
            ensure_branch_can_be_deleted(repo, branch)

    if worktree_path.exists():
        if not worktree_path.is_dir():
            raise ValueError(
                "Worktree path is not a directory: "
                + shorten_home_path(str(worktree_path))
            )

        ensure_worktree_is_clean(worktree_path)
        try:
            shutil.rmtree(worktree_path)
        except OSError as error:
            raise ValueError(
                "Unable to remove worktree directory "
                + shorten_home_path(str(worktree_path))
                + f": {error}"
            ) from error
    elif not worktree.is_prunable:
        raise ValueError(
            "Worktree path is missing, but Git won't prune it yet: "
            + shorten_home_path(str(worktree_path))
        )

    try:
        worktree.prune()
    except pygit2.GitError as error:
        raise ValueError(f"Unable to prune worktree {request.name}: {error}") from error

    deleted_branch_name = None
    branch_already_absent_name = None
    if request.delete_branch:
        if request.branch_name is None:
            raise ValueError(
                "This worktree is not on a local branch, so the branch can't be deleted."
            )

        if branch is None:
            branch_already_absent_name = request.branch_name
        else:
            try:
                branch.delete()
            except pygit2.GitError as error:
                raise ValueError(
                    "Deleted worktree "
                    + request.name
                    + " at "
                    + shorten_home_path(str(worktree_path))
                    + f", but unable to delete branch {request.branch_name}: {error}"
                ) from error
            deleted_branch_name = request.branch_name

    invalidate_create_worktree_branch_options_cache(repo)
    return DeleteWorktreeResult(
        name=request.name,
        path=str(worktree_path),
        deleted_branch_name=deleted_branch_name,
        deleted_branch_was_forced=request.force_delete_branch and deleted_branch_name is not None,
        branch_already_absent_name=branch_already_absent_name,
    )


def create_worktree(
    repo: pygit2.Repository,
    request: CreateWorktreeRequest,
) -> WorktreeView:
    worktree_name = normalize_worktree_name(request.worktree_name)
    worktree_path = get_worktree_target_path(repo, worktree_name)
    worktree_identifier = get_worktree_identifier(worktree_name)

    if worktree_path.exists():
        raise ValueError(
            "Worktree path already exists: " + shorten_home_path(str(worktree_path))
        )

    if worktree_identifier in repo.list_worktrees():
        raise ValueError(f"Worktree name already exists: {worktree_name}")

    worktree_path.parent.mkdir(parents=True, exist_ok=True)

    created_branch = None
    if request.create_new_branch:
        if request.remote_name is None or request.base_branch_name is None:
            raise ValueError("Remote and base branch are required to create a branch.")

        if repo.lookup_branch(worktree_name, BranchType.LOCAL) is not None:
            raise ValueError(f"Branch already exists: {worktree_name}")

        remote_branch_name = f"{request.remote_name}/{request.base_branch_name}"
        remote_branch = repo.lookup_branch(remote_branch_name, BranchType.REMOTE)
        if remote_branch is None:
            raise ValueError(f"Remote branch not found: {remote_branch_name}")

        base_commit = repo[remote_branch.target]
        try:
            created_branch = repo.create_branch(worktree_name, base_commit)
        except pygit2.GitError as error:
            raise ValueError(
                f"Unable to create branch {worktree_name}: {error}"
            ) from error

        branch_reference = created_branch
    else:
        if request.existing_branch_name is None:
            raise ValueError("A branch selection is required.")

        branch_reference = repo.lookup_branch(
            request.existing_branch_name,
            BranchType.LOCAL,
        )
        if branch_reference is None:
            raise ValueError(f"Branch not found: {request.existing_branch_name}")

        if branch_reference.is_checked_out():
            raise ValueError(
                f"Branch is already checked out: {request.existing_branch_name}"
            )

    try:
        worktree = repo.add_worktree(
            worktree_identifier,
            str(worktree_path),
            branch_reference,
        )
    except (pygit2.GitError, OSError) as error:
        if created_branch is not None:
            try:
                created_branch.delete()
            except pygit2.GitError as delete_error:
                raise ValueError(
                    "Unable to create worktree "
                    f"{worktree_name}: {error}. "
                    "Also failed to delete the new branch: "
                    f"{delete_error}"
                ) from delete_error

        raise ValueError(f"Unable to create worktree {worktree_name}: {error}") from error
    invalidate_create_worktree_branch_options_cache(repo)
    return build_worktree_view(repo, worktree)
