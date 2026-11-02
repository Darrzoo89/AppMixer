# AppMixer

A small per-app volume mixer for Windows, in the style of the Xbox Game Bar audio widget.
It lists every app that's using audio (games, Discord, browsers, music players...) and gives each one its own volume slider, mute button and live level meter.

## Features

- **Per-app volume and mute.** If an app runs as several processes (Discord, Chrome), they're combined into one row.
- **Live level meters** and a dot that shows which apps are making sound right now.
- **Output device picker** that follows your Windows default device unless you choose another one.
- **Master volume** for the selected device.
- **"Playing only" filter** that hides apps that have been silent for the last 10 seconds.
- **Keep on top** pin for use over games.
- **Mouse wheel** over any slider nudges it by 2.
- **Uses your Windows accent color.**
- **System tray icon:**
  - Left-click shows or hides the mixer.
  - Middle-click mutes everything.
  - Right-click opens a menu with **Start with Windows** and **Exit**.
  - Closing the window sends it to the tray.
- **Single instance.** Launching it again brings up the existing window.

## Download

Get `AppMixer.exe` from the [latest release](../../releases/latest) and run it. Nothing to install.

You need the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64). Windows will offer to install it if it's missing.

## Build from source

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

You need the .NET 8 SDK. The app uses the Windows Core Audio API directly, so it has no NuGet dependencies.

## Settings

Window position, size, pin and filter settings are saved to `%AppData%\AppMixer\settings.json`.
**Start with Windows** adds an `AppMixer` entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Untick it in the tray menu to remove it.
