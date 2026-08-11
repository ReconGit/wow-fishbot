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
- Alt+Tab: continue with background input when enabled
- Hold LMB/RMB while WoW is focused: manual catch for the current bobber
- F8: exit
- Close launcher window: exit

Background catches briefly move and confine the host cursor at the bite, then restore it.

## Configuration

- `fishing-controller.json`
- `EnableBackgroundInput`: targeted background keyboard/click input

## Access

- Process memory: read-only
- Input: Windows keyboard/mouse events
- No injection
- No memory writes
