# Forbidden — Mocks / fakes wired into consumer DI (moved)

The full ban (banned packages `Moq` / `NSubstitute` / `FakeItEasy` / `WireMock.Net` in the test project; banned `services.AddSingleton<I…, Fake…>()` in any host `Program.cs`; the stub-project replacement pattern) lives in **`dotnet-testing`** § forbidden-patterns § 1 and § 4.

Load `dotnet-testing` and read `forbidden-patterns.md`.

## Companion conventions

- [../csharp-style/time-provider.md](../csharp-style/time-provider.md) — inject `TimeProvider`; the test class uses `FakeTimeProvider` (not the consumer's DI).
- [../csharp-style/guid-createversion7.md](../csharp-style/guid-createversion7.md) — predictable IDs without `Mock<IGuidGenerator>`.
- [no-test-specific-branches.md](no-test-specific-branches.md) — pointer to the broader ban on test-flavoured code paths.
