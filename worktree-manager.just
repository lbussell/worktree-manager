[private]
default:
    @just -f {{ source_file() }} --working-directory "$(pwd)" "$(just -f {{ source_file() }} --summary | tr ' ' '\n' | grep -v '^default$' | gum choose)"

# List all git worktrees with associated PR info
list:
    #!/usr/bin/env bash
    prs=$(gh pr list --state all --json headRefName,number,state --limit 200 2>/dev/null || echo '[]')
    {
        echo "Path,Branch,Commit,PR"
        while IFS= read -r line; do
            path=$(echo "$line" | awk '{print $1}')
            commit=$(echo "$line" | awk '{print $2}')
            branch=$(echo "$line" | sed -n 's/.*\[\(.*\)\]/\1/p')
            pr_info=$(echo "$prs" | jq -r --arg b "$branch" '.[] | select(.headRefName == $b) | "#\(.number) (\(.state))"' | head -1)
            echo "${path/$HOME/~},$branch,$commit,$pr_info"
        done < <(git worktree list)
    } | gum table -p

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
    {
        echo "PR,Title,Worktree"
        gh pr list --json number,title,headRefName --jq '.[] | [.number, .headRefName, .title] | @tsv' \
        | while IFS=$'\t' read -r number branch title; do
            wt=$(echo "$worktrees" | grep "\[$branch\]" | awk '{print $1}')
            echo "#$number,$title,${wt/$HOME/~}"
        done
    } | gum table -p
