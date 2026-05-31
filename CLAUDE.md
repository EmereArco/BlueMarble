# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

BlueMarble: Windows desktop app that renders a NASA Blue Marble wallpaper with a live day/night solar terminator overlay. C# / .NET 8 / WinUI 3 (unpackaged, self-contained WindowsAppSDK 1.6), SkiaSharp for composition, H.NotifyIcon for tray. Currently Phase-1 skeleton — prefer minimal scaffolding over speculative architecture.

## Build & test

Always run from PowerShell.

- **Tests** (pure .NET, works with the dotnet CLI alone):
  `dotnet test BlueMarble.Tests/BlueMarble.Tests.csproj -c Debug`
- **WinUI app**: `dotnet build`/`dotnet run` **do not work** — they look for `Microsoft.Build.Packaging.Pri.Tasks.dll` under `C:\Program Files\dotnet\sdk\<ver>\…`, which the .NET SDK doesn't ship. Use Visual Studio Build Tools' MSBuild instead:
  ```powershell
  & "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" `
    BlueMarble\BlueMarble.csproj -p:Configuration=Debug -p:Platform=x64
  ```
  The build output is `BlueMarble\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\BlueMarble.exe`. Launch it directly; it's a tray-icon app with no console output.
- **Publish (Release)**: same MSBuild, with `-t:Publish -p:Configuration=Release -p:RuntimeIdentifier=win-x64` (or `win-arm64`). Produces trimmed, single-file, self-contained.

**Why MSBuild and not dotnet**: WindowsAppSDK 1.6's `MrtCore.PriGen.targets` resolves the Pri.Tasks DLL relative to the running MSBuild's root. VS Build Tools' MSBuild finds it at `…\BuildTools\MSBuild\…\AppxPackage\`; the standalone .NET SDK doesn't have an equivalent. Do not try to "fix" this inside the csproj — it's an environment constraint.

Running the app from Claude is OK (`BlueMarble.exe` is a tray app, returns immediately on launch). Don't `dotnet run` — it'll trigger the broken build path.

## Platforms

Project defines `x64;ARM64` only — no `Any CPU`. Any build/publish change must consider both RIDs (`win-x64`, `win-arm64`).

## Test project rules (strict)

`BlueMarble.Tests` targets `net9.0` (pure .NET) and links specific source files from the main `net8.0-windows` project via `<Compile Include="..\BlueMarble\…" Link="…" />`. This is intentional: tests run on any OS / CI without WinUI.

- Never add WinUI, WindowsAppSDK, or Windows-only package references to `BlueMarble.Tests.csproj`.
- Only files containing pure logic (no `using Microsoft.UI.*`, no P/Invoke, no `H.NotifyIcon`) are eligible to be linked into the test project. If a file you want to test depends on WinUI, refactor the pure logic into a separate file first.
- If a test requires Windows-only behavior, that's a signal the code under test is in the wrong layer — push back, don't relax this rule.

## Layout

```
BlueMarble/
├── Imagery/        GIBS WMS client, tile cache, providers
├── Composition/    SolarGeometry, FrameComposer        (pure — testable)
├── Wallpaper/      WallpaperPosition, WallpaperApplier (Position is pure)
├── UI/             TrayIconHost, PrefetchRefreshController
├── Native/         P/Invoke, COM interop
└── Settings/       AppSettings
BlueMarble.Tests/   xUnit, links pure files from above
```

## Style

- `Nullable` and `ImplicitUsings` are enabled across the solution — write nullable-aware code, don't add redundant `using` directives that ImplicitUsings already covers.
- `LangVersion=latest`, `AllowUnsafeBlocks=true` — modern C# features are fine; `unsafe` is allowed but should be confined to `Native/` or hot composition paths.
