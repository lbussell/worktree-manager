set dotenv-load := false

[private]
default:
    @just --list

app := "WorktreeManager"
project := "WorktreeManager/WorktreeManager.csproj"
artifacts_dir := "artifacts"
publish_dir := artifacts_dir / "publish" / app / "release"
install_dir := env("HOME") / ".local/bin"

validate: build install

# Build the application
build:
    dotnet publish {{ project }} --artifacts-path {{ artifacts_dir }}

# Run the application
run *args:
    dotnet run --project {{ project }} -- {{ args }}

# Install the published binary to ~/.local/bin
install: build
    mkdir -p {{ install_dir }}
    cp {{ publish_dir }}/{{ app }} {{ install_dir }}/{{ app }}
