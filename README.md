# LangSwitch

LangSwitch is a Windows 11 tray app that fixes text typed with the wrong keyboard layout. Select the text in an editable field and press **Left Alt+Shift**. Windows switches layouts as usual, and LangSwitch replaces the selection with the characters those same physical keys produce in the new layout. With US QWERTY and Persian (Standard), `sghl` becomes `سلام`.

## Run

1. Download `LangSwitch.exe` from [GitHub Releases](https://github.com/saeedizadinia/langswitch/releases). Place it somewhere you intend to keep it and launch it. The x64 executable includes its .NET runtime.
2. Ensure Windows has at least two keyboard layouts and uses Left Alt+Shift to switch. In **Settings → Time & language → Typing → Advanced keyboard settings → Input language hot keys**, select **Between input languages**, then **Change Key Sequence**.
3. Right-click the LangSwitch icon in the notification area to pause conversion, change **Start at sign-in**, or exit. Start at sign-in is enabled on first launch. Turn it off before removing the executable.

LangSwitch supports physical keyboard layouts and leaves characters without a matching key unchanged. It works with editable controls that expose a selection through Windows UI Automation, or that can confirm they are writable and support Ctrl+C. Native Windows edit controls support multiline replacement. Other supported editors use single-line Unicode input. Password fields, elevated windows, IME composition, and dead-key sequences are outside the supported scope.

Clipboard fallback restores supported clipboard contents when its copy is still the latest clipboard write. When the existing formats cannot be saved safely, the app skips fallback. Windows clipboard history may record a fallback copy. LangSwitch keeps selected text in memory during conversion and does not log it.

## Build and check

The tray app and its Windows integration live in `src/LangSwitch`. The regression checks and editor fixtures live in `tests`.

With the .NET 10 SDK on Windows, build the solution and publish the self-contained executable:

```powershell
dotnet build .\LangSwitch.slnx -c Release
dotnet publish .\src\LangSwitch\LangSwitch.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\dist\win-x64
```

Run mapping and hook checks with US and Persian (Standard) configured. The checks also try the standard Russian layout. Open the native editor fixture with `-- --editors`, or open `tests\browser.html` in a browser to try the shortcut on an editable field.

```powershell
dotnet run --project .\tests\LangSwitch.Checks -c Release
dotnet run --project .\tests\LangSwitch.Checks -c Release -- --editors
```
