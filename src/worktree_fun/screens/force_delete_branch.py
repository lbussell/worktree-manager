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
    Label,
    Static,
)

from worktree_fun.models import (
    DeleteWorktreeRequest,
)


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
