_self := "just -f " + source_file() + " --working-directory \"$(pwd)\""

[private]
default:
    #!/usr/bin/env bash
    git rev-parse --git-dir &>/dev/null && {{ _self }} status
    choice=$(gum choose \
        "new" \
        "list-worktrees" \
        "list-branches" \
        "list-prs" \
        "help") || exit 0
    [[ -z "$choice" ]] && exit 0
    {{ _self }} "$choice" || exit 0

# Show available recipes
help:
    @{{ _self }} --list

status:
    @echo "Root: $(git rev-parse --show-toplevel)"
    @echo "Branch: $(git branch --show-current)"
    @echo "Commit: $(git rev-parse --short HEAD)"

# Create a new branch (and optionally a worktree)
new:
    #!/usr/bin/env bash

    branch=$({{ _self }} _create-branch) || exit 0
    [[ -z "$branch" ]] && exit 0

    if gum confirm "Create worktree?"; then
        wt_path=$({{ _self }} _create-worktree "$branch") || exit 0
        [[ -n "$wt_path" ]] && {{ _self }} show-worktree "$wt_path"
    else
        {{ _self }} show-branch "$branch"
    fi

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

    pr_number=$(gh pr list --head "$branch" --json number --jq '.[0].number' 2>/dev/null)
    [[ "$pr_number" == "null" ]] && pr_number=""
    [[ -n "$pr_number" ]] && echo "PR:       #$pr_number"

    options=("Copy branch" "Switch to branch")
    [[ -n "$upstream" ]] && options+=("Open remote branch in browser")
    [[ -n "$wt" ]] && options+=("Show worktree")
    [[ -n "$pr_number" ]] && options+=("Show PR")
    options+=("Delete branch")

    action=$(printf '%s\n' "${options[@]}" | gum choose)
    [[ -z "$action" ]] && exit 0

    case "$action" in
        "Copy branch")
            echo "$branch" | pbcopy
            echo "Copied: $branch"
            ;;
        "Switch to branch")
            git switch "$branch"
            ;;
        "Open remote branch in browser")
            remote=$(echo "$upstream" | cut -d'/' -f1)
            remote_url=$(git remote get-url "$remote" 2>/dev/null)
            gh browse --branch "$branch" -R "$remote_url"
            ;;
        "Show worktree")
            {{ _self }} show-worktree "$wt"
            ;;
        "Show PR")
            {{ _self }} show-pr "$pr_number"
            ;;
        "Delete branch")
            if gum confirm "Delete branch $branch?"; then
                git branch -D "$branch"
            fi
            ;;
    esac

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

    # Check if branch has a worktree
    wt=$(git worktree list --porcelain | awk -v b="refs/heads/$branch" '
        /^worktree / { path=substr($0, 10) }
        $0 == "branch " b { print path }
    ')
    [[ -n "$wt" ]] && echo "Worktree: $wt"

    options=("Open in browser" "Copy link" "Copy link (md)" "Copy branch" "Switch to branch" "Checkout PR")
    if [[ -n "$wt" ]]; then
        options+=("Show worktree")
    else
        options+=("Checkout PR to worktree")
    fi

    action=$(printf '%s\n' "${options[@]}" | gum choose)
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
        "Switch to branch")
            git switch "$branch"
            ;;
        "Checkout PR")
            if ! gh pr checkout "$number" 2>/dev/null; then
                echo "Checkout failed."
                if gum confirm "Retry with --force?"; then
                    gh pr checkout "$number" --force
                fi
            fi
            ;;
        "Show worktree")
            {{ _self }} show-worktree "$wt"
            ;;
        "Checkout PR to worktree")
            wt_path=$({{ _self }} _create-worktree "$branch")
            [[ -n "$wt_path" ]] && {{ _self }} show-worktree "$wt_path"
            ;;
    esac

# List worktrees and show details for the selected one
list-worktrees:
    #!/usr/bin/env bash
    selection=$(git worktree list | awk '{print $1}' | gum filter) || exit 0
    [[ -z "$selection" ]] && exit 0
    {{ _self }} show-worktree "$selection"

# List branches and show details for the selected one
list-branches:
    #!/usr/bin/env bash
    selection=$({{ _self }} _choose-branch) || exit 0
    [[ -z "$selection" ]] && exit 0
    {{ _self }} show-branch "$selection"

# List open PRs and show details for the selected one
list-prs:
    #!/usr/bin/env bash
    selection=$(gh pr list --json number,title --jq '.[] | "#\(.number) \(.title)"' | gum filter) || exit 0
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

[private]
_choose-base-branch:
    #!/usr/bin/env bash
    choice=$(gum choose --header="Base branch:" \
        "Current branch" \
        "upstream/main" \
        "upstream/nightly" \
        "Other") || exit 0
    [[ -z "$choice" ]] && exit 0

    case "$choice" in
        "Current branch")
            git branch --show-current
            ;;
        upstream/*)
            git fetch upstream >&2
            echo "$choice"
            ;;
        "Other")
            {{ _self }} _choose-branch || exit 0
            ;;
    esac

[private]
_choose-branch:
    #!/usr/bin/env bash
    remotes=$(git remote)
    options=("local")
    while IFS= read -r r; do
        [[ -n "$r" ]] && options+=("$r")
    done <<< "$remotes"

    scope=$(printf '%s\n' "${options[@]}" | gum choose --header="Branch scope:") || exit 0
    [[ -z "$scope" ]] && exit 0

    if [[ "$scope" == "local" ]]; then
        result=$(git for-each-ref --sort=-committerdate --format='%(refname:short)' refs/heads/ | gum filter) || exit 0
    else
        result=$(git for-each-ref --sort=-committerdate --format='%(refname:short)' "refs/remotes/$scope/" | gum filter) || exit 0
    fi
    echo "$result"

[private]
_create-branch:
    #!/usr/bin/env bash

    base=$({{ _self }} _choose-base-branch) || exit 0
    [[ -z "$base" ]] && exit 0

    branch_name=$(gum input --header="Branch name:") || exit 0
    [[ -z "$branch_name" ]] && exit 0

    if gum confirm "Use prefix dev/loganbussell/?"; then
        full_branch="dev/loganbussell/$branch_name"
    else
        full_branch="$branch_name"
    fi

    git branch "$full_branch" "$base" >&2
    echo >&2 "Created branch $full_branch from $base"
    echo "$full_branch"

[private]
_create-worktree branch:
    #!/usr/bin/env bash
    branch="{{ branch }}"
    dir_name="${branch#dev/loganbussell/}"
    repo_name=$(basename "$(git rev-parse --show-toplevel)")
    wt_path="$HOME/w/$repo_name/$dir_name"
    git worktree add "$wt_path" "$branch" >&2
    echo "$wt_path"
