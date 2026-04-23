import os
import shutil
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
from textual.containers import (
    Horizontal,
    Vertical,
)
from textual.widgets import (
    Label,
    ListItem,
    Static,
)

from worktree_fun.models import (
    CreateWorktreeBranchOptions,
    CreateWorktreeRequest,
    DeleteWorktreeRequest,
    DeleteWorktreeResult,
    ForceDeleteBranchRequiredError,
    WorktreeView,
)
from worktree_fun.utils import (
    get_worktree_identifier,
    normalize_worktree_name,
    shorten_home_path,
)


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


_BRANCH_OPTIONS_CACHE: dict[str, CreateWorktreeBranchOptions] = {}
_BRANCH_OPTIONS_CACHE_LOCK = Lock()


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
            Horizontal(
                Label(worktree.name, classes="worktree-name"),
                Static(worktree.display_path, classes="worktree-path"),
                classes="worktree-header",
            ),
            Static("Branch: " + worktree.branch, classes="worktree-branch"),
            classes="worktree-item list-item-container",
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
