_self := "just -f " + source_file() + " --working-directory \"$(pwd)\""

[private]
default:
    @{{ _self }} "$({{ _self }} --summary | tr ' ' '\n' | grep -v '^default$' | gum choose)"

# List all git worktrees with associated PR info
list:
    @{{ _self }} _list-csv | gum table -p

# Choose a worktree and perform an action
choose:
    #!/usr/bin/env bash
    csv=$({{ _self }} _list-csv)
    row=$(echo "$csv" | gum table)
    [[ -z "$row" ]] && exit 0

    echo "$row"

    IFS=',' read -r path branch commit status pr_info <<< "$row"
    # Expand ~ back to $HOME for commands
    full_path="${path/#\~/$HOME}"
    pr_number=$(echo "$pr_info" | grep -o '#[0-9]*' | tr -d '#')

    echo "  Path:   $full_path"
    echo "  Branch: $branch"
    [[ -n "$pr_number" ]] && echo "  PR:     #$pr_number"

    action=$(gum choose --height=6 \
        "Print worktree path" \
        "Open PR in browser" \
        "Remove worktree" \
        "Remove worktree and branch")
    [[ -z "$action" ]] && exit 0

    echo "→ $action"
    case "$action" in
        "Print worktree path")
            echo "$full_path"
            ;;
        "Open PR in browser")
            if [[ -n "$pr_number" ]]; then
                gh pr view "$pr_number" --web
            else
                echo "No PR associated with branch $branch"
            fi
            ;;
        "Remove worktree")
            {{ _self }} _remove-worktree "$full_path"
            ;;
        "Remove worktree and branch")
            {{ _self }} _remove-worktree-and-branch "$full_path" "$branch"
            ;;
    esac

[private]
_remove-worktree path:
    git worktree remove "{{ path }}"

[private]
_remove-worktree-and-branch path branch:
    git worktree remove "{{ path }}"
    git branch -D "{{ branch }}"

[private]
_list-csv:
    #!/usr/bin/env bash
    prs=$(gh pr list --state all --json headRefName,number,state --limit 200 2>/dev/null || echo '[]')
    echo "Path,Branch,Commit,Status,PR"
    while IFS= read -r line; do
        path=$(echo "$line" | awk '{print $1}')
        commit=$(echo "$line" | awk '{print $2}')
        branch=$(echo "$line" | sed -n 's/.*\[\(.*\)\]/\1/p')
        pr_info=$(echo "$prs" | jq -r --arg b "$branch" '.[] | select(.headRefName == $b) | "#\(.number) (\(.state))"' | head -1)

        # Ahead/behind upstream
        ab=""
        if ab_raw=$(git -C "$path" rev-list --left-right --count @{upstream}...HEAD 2>/dev/null); then
            behind=$(echo "$ab_raw" | awk '{print $1}')
            ahead=$(echo "$ab_raw" | awk '{print $2}')
            [[ "$ahead" -gt 0 ]] && ab+="↑${ahead}"
            [[ "$behind" -gt 0 ]] && ab+="↓${behind}"
        fi

        # Lines added/removed (uncommitted vs HEAD) and unstaged file count
        changes=""
        diffstat=$(git -C "$path" diff HEAD --numstat 2>/dev/null | awk '{a+=$1; r+=$2} END {printf "%d %d", a, r}')
        added=$(echo "$diffstat" | awk '{print $1}')
        removed=$(echo "$diffstat" | awk '{print $2}')
        unstaged=$(git -C "$path" diff --name-only 2>/dev/null | wc -l | tr -d ' ')
        [[ "$added" -gt 0 ]] && changes+="+${added}"
        [[ "$removed" -gt 0 ]] && changes+="-${removed}"
        [[ "$unstaged" -gt 0 ]] && changes+="u${unstaged}"

        status="${ab} ${changes}"
        status=$(echo "$status" | xargs) # trim whitespace

        echo "${path/$HOME/~},$branch,$commit,$status,$pr_info"
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
    {
        echo "PR,Title,Worktree"
        gh pr list --json number,title,headRefName --jq '.[] | [.number, .headRefName, .title] | @tsv' \
        | while IFS=$'\t' read -r number branch title; do
            wt=$(echo "$worktrees" | grep "\[$branch\]" | awk '{print $1}')
            echo "#$number,$title,${wt/$HOME/~}"
        done
    } | gum table -p
