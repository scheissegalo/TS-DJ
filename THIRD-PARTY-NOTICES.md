# Third-Party Notices

TS-DJ bundles the following third-party software in release builds. Each component retains its own license.

## yt-dlp

- **Purpose:** YouTube metadata and audio extraction
- **License:** [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE)
- **Source:** https://github.com/yt-dlp/yt-dlp
- **Bundled in:** Linux and Windows release zips under `tools/yt-dlp/`

## QuickJS

- **Purpose:** JavaScript runtime for yt-dlp YouTube challenge solving
- **License:** MIT
- **Source:** https://bellard.org/quickjs/
- **Bundled in:** Linux and Windows release zips under `tools/js-runtimes/quickjs/`

## FFmpeg

- **Purpose:** Audio transcoding (YouTube to MP3)
- **License:** GPL-3.0 (BtbN static build)
- **Source:** https://github.com/BtbN/FFmpeg-Builds
- **Bundled in:** Windows release zip under `tools/ffmpeg/win-x64/`
- **Note:** GPL-licensed builds require source availability. FFmpeg source is available from https://ffmpeg.org/download.html and the BtbN build repository above.

## libopus

- **Purpose:** Opus voice encoding for TeamSpeak
- **License:** BSD-style (see upstream Opus project)
- **Bundled in:** Windows release zip under `lib/x64/libopus.dll`

## Deno / Node.js / Bun

TS-DJ does **not** bundle these runtimes. If present on the user's system PATH, they may be used as alternatives via **Options → YouTube / yt-dlp**.
