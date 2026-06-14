# Changelog

All notable changes to TS-DJ are documented here.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Version numbers follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- GitHub Actions CI (build matrix: Linux x64, Windows x64)
- Tag-triggered release workflow with platform zip artifacts
- Centralized versioning via `Directory.Build.props`
- Application version shown in window title and footer

## [0.2.0] — prior releases

Features present before automated releases:

- TeamSpeak 3 streaming and channel selection
- Playlist/queue playback with skip and metadata
- Navidrome library browser and queue integration
- Soundboard with configurable pads and hotkeys
- Linux desktop install script (`packaging/linux/install-desktop.sh`)

[Unreleased]: https://github.com/scheissegalo/TS-DJ/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/scheissegalo/TS-DJ/releases/tag/v0.2.0
