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
    Label,
    Static,
)

from worktree_fun.models import (
    DeleteWorktreeRequest,
    WorktreeView,
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
