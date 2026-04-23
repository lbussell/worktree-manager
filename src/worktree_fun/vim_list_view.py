from textual import (
    events,
)
from textual.widgets import (
    ListView,
)


class VimListView(ListView):
    def move_to_first_item(self) -> None:
        for index, item in enumerate(self._nodes):
            if not item.disabled:
                self.index = index
                return

    def move_to_last_item(self) -> None:
        for index in range(len(self._nodes) - 1, -1, -1):
            if not self._nodes[index].disabled:
                self.index = index
                return

    def on_key(self, event: events.Key) -> None:
        if event.character is None:
            return

        match event.character:
            case "j" | "J":
                self.action_cursor_down()
                event.stop()
            case "k" | "K":
                self.action_cursor_up()
                event.stop()
            case "g":
                self.move_to_first_item()
                event.stop()
            case "G":
                self.move_to_last_item()
                event.stop()
