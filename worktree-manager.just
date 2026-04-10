[private]
default:
    @just -f {{ source_file() }} --working-directory "$(pwd)" "$(just -f {{ source_file() }} --summary | tr ' ' '\n' | grep -v '^default$' | gum choose)"

# List all git worktrees with associated PR info
list:
    #!/usr/bin/env bash
    echo "📂 Current worktrees:"
    prs=$(gh pr list --state all --json headRefName,number,state --limit 200 2>/dev/null || echo '[]')
    while IFS= read -r line; do
        branch=$(echo "$line" | sed -n 's/.*\[\(.*\)\]/\1/p')
        if [[ -n "$branch" ]]; then
            pr_info=$(echo "$prs" | jq -r --arg b "$branch" '.[] | select(.headRefName == $b) | "PR #\(.number) (\(.state))"' | head -1)
            if [[ -n "$pr_info" ]]; then
                printf "%s  %s\n" "$line" "$pr_info"
            else
                echo "$line"
            fi
        else
            echo "$line"
        fi
    done < <(git worktree list)

# Show worktree status summary
status:
    @echo "🔍 Worktree status:"
    @echo "  Root: $(git rev-parse --show-toplevel)"
    @echo "  Branch: $(git branch --show-current)"
    @echo "  Commit: $(git rev-parse --short HEAD)"

# List open pull requests with associated worktree paths
pr:
    #!/usr/bin/env bash
    worktrees=$(git worktree list)
    gh pr list --json number,title,headRefName --jq '.[] | [.number, .headRefName, .title] | @tsv' \
    | while IFS=$'\t' read -r number branch title; do
        wt=$(echo "$worktrees" | grep "\[$branch\]" | awk '{print $1}')
        if [[ -n "$wt" ]]; then
            printf "#%-6s %-50s 📂 %s\n" "$number" "$title" "$wt"
        else
            printf "#%-6s %s\n" "$number" "$title"
        fi
    done
