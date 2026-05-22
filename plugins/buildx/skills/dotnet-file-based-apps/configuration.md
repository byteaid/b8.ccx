# User Secrets, Launch Profiles, Runtime Path Access

## User secrets

A stable user-secrets ID is hashed from the **full file path** of the `.cs`. Use the standard CLI with `--file`:

```bash
dotnet user-secrets set "ApiKey" "your-secret-value" --file file.cs
dotnet user-secrets list --file file.cs
```

Notes:

- `list` prints values — do not run it in scripts that go to public log destinations.
- Because the ID is hashed off the **path**, moving / renaming the `.cs` invalidates the secret store association; the secrets are still on disk but the new path won't see them. Either copy the secrets again or symlink to the original path.
- Secrets work for any code that wires up `Microsoft.Extensions.Configuration` with `AddUserSecrets<T>()` — typical in scripts that also use `Microsoft.Extensions.Hosting`.

## Launch profiles

Two locations are honored:

1. **Flat sibling file** `<AppName>.run.json` — file-based-app convention.
2. **Traditional** `Properties/launchSettings.json` — wins when both exist; the CLI logs a warning so you know which file was applied.

### Layout with multiple file-based apps

```
myapps/
  foo.cs
  foo.run.json
  bar.cs
  bar.run.json
```

Each script gets its own profile file based on the app's basename.

### Profile selection priority

1. `--launch-profile <name>` flag.
2. `DOTNET_LAUNCH_PROFILE` env var.
3. First profile in the file.

```bash
dotnet run app.cs --launch-profile https
```

### Example `<AppName>.run.json`

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5000",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    },
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:5001;http://localhost:5000",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
```

### stdin pipe mode does not consult profiles

`dotnet run -` (code piped via stdin) skips launch-profile lookup. The cwd still applies as the working directory, but no environment variables, no `applicationUrl`, no profile-specific settings are loaded.

## Runtime path access

App file and directory paths are exposed at runtime via `System.AppContext.GetData(...)` (named keys; consult the .NET 10 SDK release notes for the canonical key list). Use this when a script needs to locate sibling assets relative to its own `.cs` source.

```csharp
var appFilePath = (string?)AppContext.GetData("AppFilePath");
var dir = Path.GetDirectoryName(appFilePath);
var data = File.ReadAllText(Path.Combine(dir!, "data.json"));
```

`AppContext.BaseDirectory` still works but points at the build output, not the source `.cs` — `GetData` is the only way to resolve back to the source location.

## TFM and SDK pinning

| Mechanism | Effect |
|---|---|
| `#:property TargetFramework=net9.0` | Compile against `net9.0` instead of the file-based default (`net10.0`). |
| `global.json` (any parent dir) | Pin the SDK version used to build/run the file-based app. |

The TFM (compile target) is independent of the SDK version (toolchain). Use `global.json` for repository-wide SDK consistency in CI; use `#:property` only when an individual script must target a different runtime.

## Cross-references

- [directives.md](directives.md) — `#:property` for TFM overrides.
- [layout-and-build.md](layout-and-build.md) — implicit-build-file ladder including `global.json`.
- [troubleshooting.md](troubleshooting.md) — secrets / profile gotchas.
