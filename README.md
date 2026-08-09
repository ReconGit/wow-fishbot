# WoW Fishbot

## Requirements

- Windows
- .NET 9 or newer SDK
- WoW 3.3.5a build 12340
- 16:9 or ultrawide client viewport
- Fishing: backtick
- Lure: Shift+backtick

## Run

- Double-click `Launch.cmd`

or:

```powershell
dotnet restore .\WowFishbot\WowFishbot.csproj
.\Start-FishingController.ps1
```

## Controls

- Backtick: start
- W/A/S/D/Q/E, arrows, Space: idle
- Alt+Tab: idle
- Hold LMB/RMB: manual catch for the current bobber
- F8: exit
- Close launcher window: exit

## Configuration

- `fishing-controller.json`

## Access

- Process memory: read-only
- Input: Windows keyboard/mouse events
- No injection
- No memory writes
