_default:
    @just --list

run:
    uv run wt

install:
    uv tool install --editable . --python 3.14
