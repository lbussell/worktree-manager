[private]
default:
    @just -f {{ source_file() }} --working-directory "$(pwd)" "$(just -f {{ source_file() }} --summary | tr ' ' '\n' | grep -v '^default$' | gum choose)"

# List all git worktrees
list:
    @echo "📂 Current worktrees:"
    @git worktree list

# Show worktree status summary
status:
    @echo "🔍 Worktree status:"
    @echo "  Root: $(git rev-parse --show-toplevel)"
    @echo "  Branch: $(git branch --show-current)"
    @echo "  Commit: $(git rev-parse --short HEAD)"
