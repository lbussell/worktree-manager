# .NET Project Template

## How to add new projects

Projects go under the `src/` folder.
Projects should be named descriptively, but should not be too verbose.

Example project layout:

```text
- MyAwesomeProject.slnx
- src/
    - Console/
        Console.csproj
    - Library/
        Library.csproj
    - Library.Tests/
        Library.Tests.csproj
```

Test projects go in the `src/` folder too, and should be named the same as the project they are testing but with the `.Tests` suffix.

Projects should have limited namespace nesting.
Only add nesting when differentiation is required.
If all of the projects in the repo will have the same prefix (e.g. `MyProject.Library`, `MyProject.Tests`, `MyProject.Web`), then the prefix does not provide value and should be removed.

### Example: Add a new class library

From the repo root, run the following:

```bash
# Always run with dry-run first to verify correctness
dotnet new classlib --name MyClasslib --output src/MyClasslib --dry-run
# Create the project
dotnet new classlib --name MyClasslib --output src/MyClasslib --dry-run
# Always add projects to the root of the solution - no solution folders
dotnet sln *.slnx add src/MyClasslib --in-root
```
