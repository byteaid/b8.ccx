# Test Seeding — Moved

Test data seeding (the ephemeral-always invariant; the four canonical strategies — direct client / `WithInitFiles` / emulator+SDK / eventing subscriber; the anti-pattern catalogue; the file-splitting rules) now lives in the **`dotnet-testing`** skill, file `seeding.md`.

Load `dotnet-testing` and read § seeding.

This skill (`dotnet-aspire`) still owns the producer-side primitives that seeding builds on: `RunAsEmulator()` / `AsExisting` switching (see [emulators-and-real-infra.md](emulators-and-real-infra.md)), `WithInitFiles` / `WithBindMount` operator semantics (see [apphost-wiring.md](apphost-wiring.md)), and the `Aspire.Hosting.Eventing` event types (`AfterResourcesCreatedEvent`, `ResourceReadyEvent`).
