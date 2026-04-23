from worktree_fun.app import (
    WorktreeApp,
)


def main() -> None:
    selection = WorktreeApp().run()
    if selection:
        print(f"You selected: {selection}")


if __name__ == "__main__":
    main()
