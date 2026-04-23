_default:
    @just --list

# Install the app so it can be used anywhere
install:
    uv tool install --editable . --python 3.14

# Run the app locally
run:
    uv run wt

# Opens the app and the debug log side-by-side in ghostty
run-dev:
    osascript "{{justfile_directory()}}/scripts/open-textual-dev.applescript" "{{justfile_directory()}}"
