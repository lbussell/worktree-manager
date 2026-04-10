set dotenv-load := false

app := "WorktreeManager"
install_dir := env("HOME") / ".local/bin"

# Build the application
build:
    dotnet publish {{ app }}.cs

# Run the application
run *args:
    dotnet run {{ app }}.cs -- {{ args }}

# Install the published binary to ~/.local/bin
install: build
    mkdir -p {{ install_dir }}
    cp artifacts/{{ app }}/{{ app }} {{ install_dir }}/{{ app }}
