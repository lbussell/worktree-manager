from dataclasses import (
    dataclass,
)

from worktree_fun.utils import (
    shorten_home_path,
)


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
