from textual.app import (
    ComposeResult,
)
from textual.containers import (
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
    WorktreeView,
)


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
