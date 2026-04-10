_self := "just -f " + source_file() + " --working-directory \"$(pwd)\""

[private]
default:
    @{{ _self }} "$({{ _self }} --summary | tr ' ' '\n' | grep -v '^default$' | gum choose)"

# Show available recipes
help:
    @{{ _self }} --list

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
    now=$(date +%s)
    echo "Age,Path,Branch,Commit,Status,PR"
    {
    while IFS= read -r line; do
        path=$(echo "$line" | awk '{print $1}')
        commit=$(echo "$line" | awk '{print $2}')
        branch=$(echo "$line" | sed -n 's/.*\[\(.*\)\]/\1/p')
        pr_info=$(echo "$prs" | jq -r --arg b "$branch" '.[] | select(.headRefName == $b) | "#\(.number) (\(.state))"' | head -1)

        # Commit epoch and relative age
        epoch=$(git -C "$path" log -1 --format='%ct' 2>/dev/null || echo "0")
        diff=$((now - epoch))
        if   (( diff >= 2592000 )); then age="$((diff / 2592000))M"
        elif (( diff >= 604800 ));  then age="$((diff / 604800))w"
        elif (( diff >= 86400 ));   then age="$((diff / 86400))d"
        elif (( diff >= 3600 ));    then age="$((diff / 3600))h"
        else                             age="$((diff / 60))m"
        fi

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

        echo "$epoch,$age,${path/$HOME/~},$branch,$commit,$status,$pr_info"
    done < <(git worktree list)
    } | sort -t',' -k1 -rn | cut -d',' -f2-

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

# List recently merged pull requests
merged:
    #!/usr/bin/env bash
    now=$(date +%s)
    data=$(
        printf "Age\tPR\tTitle\tBranch\n"
        gh pr list --state merged --json number,title,headRefName,mergedAt --limit 20 \
            --jq '.[] | [.mergedAt, .number, .headRefName, .title] | @tsv' \
        | while IFS=$'\t' read -r merged_at number branch title; do
            epoch=$(TZ=UTC date -jf "%Y-%m-%dT%H:%M:%SZ" "$merged_at" +%s 2>/dev/null || date -d "$merged_at" +%s 2>/dev/null)
            diff=$((now - epoch))
            if   (( diff >= 2592000 )); then age="$((diff / 2592000))M"
            elif (( diff >= 604800 ));  then age="$((diff / 604800))w"
            elif (( diff >= 86400 ));   then age="$((diff / 86400))d"
            elif (( diff >= 3600 ));    then age="$((diff / 3600))h"
            else                             age="$((diff / 60))m"
            fi
            printf "%s\t#%s\t%s\t%s\n" "$age" "$number" "$title" "$branch"
        done
    )
    row=$(echo "$data" | gum table -s $'\t' --lazy-quotes)
    [[ -z "$row" ]] && exit 0

    echo "$row"
    pr_number=$(echo "$row" | grep -o '#[0-9]*' | head -1 | tr -d '#')

    action=$(gum choose --height=4 \
        "Open in browser" \
        "Copy link to clipboard")
    [[ -z "$action" ]] && exit 0

    case "$action" in
        "Open in browser")
            gh pr view "$pr_number" --web
            ;;
        "Copy link to clipboard")
            url=$(gh pr view "$pr_number" --json url --jq '.url')
            echo "$url" | pbcopy
            echo "Copied: $url"
            ;;
    esac

# List all local branches with upstream, tracking status, and worktree info
branches:
    @{{ _self }} _branches-tsv | gum table -s $'\t' --lazy-quotes -p

[private]
_branches-tsv:
    #!/usr/bin/env bash
    # Build worktree lookup as "branch|path" lines
    wt_lookup=""
    while IFS= read -r line; do
        case "$line" in
            worktree\ *) wt_path="${line#worktree }" ;;
            branch\ *)
                b="${line#branch refs/heads/}"
                wt_lookup+="$b|$wt_path"$'\n'
                ;;
        esac
    done < <(git worktree list --porcelain)

    now=$(date +%s)
    echo "Age	Branch	Tracking	Commit	Message	Worktree"
    git for-each-ref --sort=-committerdate --format='%(refname:short)|%(upstream:short)|%(upstream:track)|%(objectname:short)|%(committerdate:unix)|%(subject)' refs/heads/ \
    | while IFS='|' read -r branch upstream track commit epoch message; do
        # Normalize tracking info
        if [[ "$track" == "[gone]" ]]; then
            tracking="missing"
        elif [[ -n "$track" ]]; then
            # Parse [ahead N, behind M] into ↑N↓M format
            tracking=""
            ahead=$(echo "$track" | grep -o 'ahead [0-9]*' | grep -o '[0-9]*')
            behind=$(echo "$track" | grep -o 'behind [0-9]*' | grep -o '[0-9]*')
            [[ -n "$ahead" ]] && tracking+="↑${ahead}"
            [[ -n "$behind" ]] && tracking+="↓${behind}"
        else
            tracking="✓"
        fi
        [[ -z "$upstream" ]] && tracking=""

        # Relative age
        diff=$((now - epoch))
        if   (( diff >= 2592000 )); then age="$((diff / 2592000))M"
        elif (( diff >= 604800 ));  then age="$((diff / 604800))w"
        elif (( diff >= 86400 ));   then age="$((diff / 86400))d"
        elif (( diff >= 3600 ));    then age="$((diff / 3600))h"
        else                             age="$((diff / 60))m"
        fi

        wt=$(echo "$wt_lookup" | grep "^${branch}|" | head -1 | cut -d'|' -f2)
        wt="${wt/$HOME/~}"

        # Truncate message to 40 chars
        [[ ${#message} -gt 40 ]] && message="${message:0:37}..."

        printf "%s\t%s\t%s\t%s\t%s\t%s\n" "$age" "$branch" "$tracking" "$commit" "$message" "$wt"
    done
