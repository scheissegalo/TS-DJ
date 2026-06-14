# TS-DJ

TeamSpeak 3 DJ client — a lightweight DJ application with headless TS3 client functionality for streaming music into a channel.

## Phase 1 Status

This is the **scaffolding phase**. The application launches with a UI for connection settings and playback controls, but live TeamSpeak connection and audio streaming are implemented in phase 2.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **libopus** (Linux): `sudo apt install libopus0`
- Windows: bundled `libopus.dll` in `lib/x64/` and `lib/x86/`

## Build & Run

```bash
cd /media/berni/FatExt2TB/clones/TS-DJ
dotnet build TS-DJ.slnx
dotnet run --project TS-DJ.App
```

Settings are stored in SQLite at `~/.local/share/TS-DJ/settings.db` (Linux) or `%LOCALAPPDATA%\TS-DJ\settings.db` (Windows).

## Linux desktop launcher

Install a user-local menu entry and icon:

```bash
chmod +x packaging/linux/install-desktop.sh
./packaging/linux/install-desktop.sh
```

This publishes the app to `~/.local/share/ts-dj`, installs a CLI wrapper at `~/.local/bin/ts-dj`, and registers `ts-dj.desktop` in your application menu (with absolute paths so menu launch finds .NET dependencies). Requires the .NET 8 runtime and `libopus0`.

## Solution Structure

| Project | Purpose |
|---------|---------|
| `TS-DJ.App` | Avalonia UI (MVVM) |
| `TS-DJ.Core` | Models, interfaces, shared constants |
| `TS-DJ.Infrastructure` | Logging, SQLite settings |
| `TS-DJ.Audio` | Audio pipeline (NAudio + TSLib pipes) |
| `TS-DJ.TeamSpeak` | TS3 connection wrapper |
| `TSLib` | TeamSpeak 3 protocol library (copied from [TS3AudioBot](https://github.com/Splamy/TS3AudioBot), targets net6.0) |

## Audio Pipeline (planned)

```
Audio File → NAudio Decoder → PreciseTimedPipe → VolumePipe → EncoderPipe (Opus) → Ts3VoiceTarget → TeamSpeak
```

## License

- **TSLib** and adapted components from TS3AudioBot are licensed under [OSL-3.0](LICENSE).
- See [LICENSE](LICENSE) for full terms.

TSLib originates from the [TS3AudioBot](https://github.com/Splamy/TS3AudioBot) project by Splamy and contributors.
