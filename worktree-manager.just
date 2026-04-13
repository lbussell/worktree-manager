_self := "just -f " + source_file() + " --working-directory \"$(pwd)\""

[private]
default:
    @{{ _self }} "$({{ _self }} --summary | tr ' ' '\n' | grep -v '^default$' | gum choose)"

# Show available recipes
help:
    @{{ _self }} --list

status:
    @echo "🔍 Git status:"
    @echo "  Root: $(git rev-parse --show-toplevel)"
    @echo "  Branch: $(git branch --show-current)"
    @echo "  Commit: $(git rev-parse --short HEAD)"

# Show details for a worktree
show-worktree path:
    #!/usr/bin/env bash
    path="{{ path }}"
    branch=$(git -C "$path" branch --show-current 2>/dev/null)
    commit=$(git -C "$path" rev-parse --short HEAD 2>/dev/null)

    echo "Path:   $path"
    echo "Branch: $branch"
    echo "Commit: $commit"

    if ab_raw=$(git -C "$path" rev-list --left-right --count @{upstream}...HEAD 2>/dev/null); then
        behind=$(echo "$ab_raw" | awk '{print $1}')
        ahead=$(echo "$ab_raw" | awk '{print $2}')
        tracking=""
        [[ "$ahead" -gt 0 ]] && tracking+="↑${ahead}"
        [[ "$behind" -gt 0 ]] && tracking+="↓${behind}"
        [[ -n "$tracking" ]] && echo "Track:  $tracking"
    fi

    pr_info=$(gh pr list --head "$branch" --json number,title,state \
        --jq '.[] | "#\(.number) \(.title) (\(.state))"' 2>/dev/null | head -1)
    [[ -n "$pr_info" && "$pr_info" != "null" ]] && echo "PR:     $pr_info"

    action=$(gum choose --height=8 \
        "Copy path" \
        "Copy branch" \
        "Open in VS Code" \
        "Open in file browser" \
        "Remove worktree (keep branch)" \
        "Remove worktree (delete branch)")
    [[ -z "$action" ]] && exit 0

    case "$action" in
        "Copy path")
            echo "$path" | pbcopy
            echo "Copied: $path"
            ;;
        "Copy branch")
            echo "$branch" | pbcopy
            echo "Copied: $branch"
            ;;
        "Open in VS Code")
            code "$path"
            ;;
        "Open in file browser")
            open "$path"
            ;;
        "Remove worktree (keep branch)")
            {{ _self }} remove-worktree "$path"
            ;;
        "Remove worktree (delete branch)")
            {{ _self }} remove-worktree "$path"
            git branch -D "$branch"
            ;;
    esac

# Show details for a branch
show-branch branch:
    #!/usr/bin/env bash
    branch="{{ branch }}"
    info=$(git for-each-ref --format='%(upstream:short)|%(upstream:track)|%(objectname:short)|%(subject)' "refs/heads/$branch")
    IFS='|' read -r upstream track commit message <<< "$info"

    echo "Branch:   $branch"
    echo "Commit:   $commit"
    echo "Message:  $message"
    [[ -n "$upstream" ]] && echo "Upstream: $upstream"
    [[ -n "$track" ]] && echo "Tracking: $track"

    wt=$(git worktree list --porcelain | awk -v b="refs/heads/$branch" '
        /^worktree / { path=substr($0, 10) }
        $0 == "branch " b { print path }
    ')
    [[ -n "$wt" ]] && echo "Worktree: $wt"
    exit 0

# Show details for a pull request
show-pr number:
    #!/usr/bin/env bash
    data=$(gh pr view {{ number }} --json number,title,state,author,headRefName,url,additions,deletions,repository)

    number=$(echo "$data" | jq -r '.number')
    title=$(echo "$data" | jq -r '.title')
    repo=$(echo "$data" | jq -r '"\(.repository.owner.login)/\(.repository.name)"')
    title=$(echo "$data" | jq -r '.title')
    state=$(echo "$data" | jq -r '.state')
    author=$(echo "$data" | jq -r '.author.login')
    branch=$(echo "$data" | jq -r '.headRefName')
    url=$(echo "$data" | jq -r '.url')
    adds=$(echo "$data" | jq -r '.additions')
    dels=$(echo "$data" | jq -r '.deletions')

    echo "PR:     #${number} ${title}"
    echo "State:  $state"
    echo "Author: $author"
    echo "Branch: $branch"
    echo "Diff:   +${adds} -${dels}"

    action=$(gum choose --height=6 \
        "Open in browser" \
        "Copy link" \
        "Copy link (md)" \
        "Copy branch")
    [[ -z "$action" ]] && exit 0

    case "$action" in
        "Open in browser")
            open "$url"
            ;;
        "Copy link")
            echo "$url" | pbcopy
            echo "Copied: $url"
            ;;
        "Copy link (md)")
            md="${title} (${repo}#${number})"
            echo "$md" | pbcopy
            echo "Copied: $md"
            ;;
        "Copy branch")
            echo "$branch" | pbcopy
            echo "Copied: $branch"
            ;;
    esac

# List worktrees and show details for the selected one
list-worktrees:
    #!/usr/bin/env bash
    selection=$(git worktree list | awk '{print $1}' | gum filter)
    [[ -z "$selection" ]] && exit 0
    {{ _self }} show-worktree "$selection"

# List branches and show details for the selected one
list-branches:
    #!/usr/bin/env bash
    selection=$(git for-each-ref --sort=-committerdate --format='%(refname:short)' refs/heads/ | gum filter)
    [[ -z "$selection" ]] && exit 0
    {{ _self }} show-branch "$selection"

# List open PRs and show details for the selected one
list-prs:
    #!/usr/bin/env bash
    selection=$(gh pr list --json number,title --jq '.[] | "#\(.number) \(.title)"' | gum filter)
    [[ -z "$selection" ]] && exit 0
    pr_number=$(echo "$selection" | grep -o '#[0-9]*' | head -1 | tr -d '#')
    {{ _self }} show-pr "$pr_number"

# Remove a worktree, offering --force if dirty or locked
remove-worktree path:
    #!/usr/bin/env bash
    path="{{ path }}"
    if git worktree remove "$path" 2>/dev/null; then
        echo "Removed: $path"
    else
        echo "Failed to remove worktree (dirty or locked)."
        if gum confirm "Retry with --force?"; then
            git worktree remove --force "$path"
            echo "Removed (forced): $path"
        fi
    fi
