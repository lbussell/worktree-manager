import os
from dataclasses import (
    dataclass,
)


@dataclass(frozen=True, slots=True)
class WorktreeView:
    name: str
    branch: str
    path: str

    @property
    def display_path(self) -> str:
        home = os.path.normpath(os.path.expanduser("~"))
        if self.path == home:
            return "~"

        home_prefix = home + os.sep
        if self.path.startswith(home_prefix):
            return "~" + self.path[len(home) :]

        return self.path
