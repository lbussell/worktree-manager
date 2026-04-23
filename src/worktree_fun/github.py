import json
import subprocess
from dataclasses import (
    dataclass,
)
from typing import (
    Literal,
)

from textual.containers import (
    Vertical,
)
from textual.widgets import (
    Label,
    ListItem,
    Static,
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


def get_pull_request_state(
    state: str,
) -> Literal["Open", "Merged", "Closed"]:
    state_map: dict[str, Literal["Open", "Merged", "Closed"]] = {
        "OPEN": "Open",
        "MERGED": "Merged",
        "CLOSED": "Closed",
    }
    if state not in state_map:
        raise ValueError(f"Unsupported pull request state: {state}")

    return state_map[state]


def build_pull_request_list_view(
    pull_request_data: dict[str, object],
) -> PullRequestListView:
    number = pull_request_data.get("number")
    if not isinstance(number, int):
        raise ValueError(f"Expected integer pull request number, got: {number!r}")

    title = pull_request_data.get("title")
    if not isinstance(title, str):
        raise ValueError(f"Expected string pull request title, got: {title!r}")

    branch = pull_request_data.get("headRefName")
    if not isinstance(branch, str):
        raise ValueError(f"Expected string pull request branch, got: {branch!r}")

    is_draft_mode = pull_request_data.get("isDraft")
    if not isinstance(is_draft_mode, bool):
        raise ValueError(
            f"Expected boolean pull request draft mode, got: {is_draft_mode!r}"
        )

    state = pull_request_data.get("state")
    if not isinstance(state, str):
        raise ValueError(f"Expected string pull request state, got: {state!r}")

    remote: str | None = None
    remote_data = pull_request_data.get("headRepositoryOwner")
    if remote_data is not None:
        if not isinstance(remote_data, dict):
            raise ValueError(
                "Expected pull request remote owner data to be an object, "
                f"got: {remote_data!r}"
            )

        remote_login = remote_data.get("login")
        if remote_login is None:
            remote = None
        elif isinstance(remote_login, str):
            remote = remote_login
        else:
            raise ValueError(
                "Expected pull request remote owner login to be a string, "
                f"got: {remote_login!r}"
            )

    return PullRequestListView(
        number=number,
        title=title,
        branch=branch,
        remote=remote,
        status=get_pull_request_state(state),
        is_draft_mode=is_draft_mode,
    )


def load_pull_request_list_views() -> list[PullRequestListView]:
    result = subprocess.run(
        [
            "gh",
            "pr",
            "list",
            "--state",
            "all",
            "--limit",
            "100",
            "--json",
            "number,title,headRefName,isDraft,state,headRepositoryOwner",
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    pull_request_data = json.loads(result.stdout)
    if not isinstance(pull_request_data, list):
        raise ValueError(
            "Expected gh pr list JSON output to be a list, "
            f"got: {pull_request_data!r}"
        )

    return [
        build_pull_request_list_view(pull_request) for pull_request in pull_request_data
    ]


def get_pull_request_status_class(status: str) -> str:
    status_classes: dict[str, str] = {
        "Open": "pull-request-status-open",
        "Closed": "pull-request-status-closed",
        "Merged": "pull-request-status-merged",
    }
    return status_classes.get(status, "")


def render_pull_request(pull_request: PullRequestListView) -> ListItem:
    status_class = get_pull_request_status_class(pull_request.status)
    return ListItem(
        Vertical(
            Label(
                f"#{pull_request.number} {pull_request.title}",
                classes="pull-request-title",
            ),
            Static(
                "Branch: " + pull_request.branch_display,
                classes="pull-request-branch",
            ),
            Static(
                "Status: " + pull_request.status_display,
                classes=f"pull-request-status {status_class}",
            ),
            classes="pull-request-item list-item-container",
        ),
        name=str(pull_request.number),
    )


def render_pull_request_message(message: str) -> ListItem:
    return ListItem(
        Static(message, classes="pull-request-message"),
        disabled=True,
    )
