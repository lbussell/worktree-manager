from dataclasses import (
    dataclass,
)
from typing import (
    Literal,
)


@dataclass(frozen=True, slots=True)
class PullRequestListView:
    number: int
    title: str
    branch: str
    remote: str | None
    status: Literal["Open", "Merged", "Closed"]
    is_draft_mode: bool

    @property
    def branch_display(self) -> str:
        if self.remote is None:
            return self.branch

        return f"{self.remote}/{self.branch}"

    @property
    def status_display(self) -> str:
        if self.is_draft_mode:
            return f"{self.status} | Draft"

        return self.status
