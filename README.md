<p align="center">
  <img src="docs/icon.png" width="96" alt="Digit Control icon">
</p>

<h1 align="center">Digit Control</h1>

<p align="center">
  <b>Control Windows by typing numbers.</b><br>
  Brightness, volume, media, dark mode, sleep, lock and apps, each one a two-digit code.
</p>

<p align="center">
  <a href="https://github.com/rajkirant/digit-control/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/rajkirant/digit-control?label=download"></a>
  <a href="https://github.com/rajkirant/digit-control/actions/workflows/build.yml"><img alt="Build" src="https://github.com/rajkirant/digit-control/actions/workflows/build.yml/badge.svg"></a>
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6">
  <img alt="One small exe, nothing to install" src="https://img.shields.io/badge/size-one%20small%20.exe-brightgreen">
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-blue"></a>
</p>

---

Type `11` and press Enter: brightness goes up 1%. Type `23` and the volume goes
up 10%. Type `11x5 23 41`: brightness up 5%, volume up 10% and dark mode on, all
at once.

- **One window, one textbox.** No menus to dig through. Every option is listed
  in the window, and clicking one runs it.
- **Works from the command line too.** `DigitControl.exe 53` locks the PC, so
  any code can become a desktop shortcut, a hotkey or a scheduled task.
- **Laptop screens and external monitors.** Brightness works on both, each
  monitor from its own level.
- **Tiny and private.** One small `.exe` with no installer, no background
  service, no network access and nothing to configure. It only does what you
  type.

## Download

**[Download DigitControl.exe](https://github.com/rajkirant/digit-control/releases/latest/download/DigitControl.exe)**
from the latest release and double-click it. It needs Windows 10 or 11, and
nothing else.

Windows may show "Windows protected your PC" the first time, because the
`.exe` isn't code-signed. Click **More info**, then **Run anyway**. If you'd
rather not trust a download, [build it yourself](#build-it-yourself) from the
source in this repository in a few seconds.

## The codes

Every code is two digits. The first digit is the group.

| Display | | Sound | | Media | |
| --- | --- | --- | --- | --- | --- |
| `11` | Brightness up 1% | `21` | Volume up 1% | `31` | Play / pause |
| `12` | Brightness down 1% | `22` | Volume down 1% | `32` | Next track |
| `13` | Brightness up 10% | `23` | Volume up 10% | `33` | Previous track |
| `14` | Brightness down 10% | `24` | Volume down 10% | `34` | Stop |
| `15` | Brightness 100% | `25` | Volume 50% | | |
| `16` | Brightness 50% | `26` | Mute | | |
| `17` | Brightness 0% | `27` | Unmute | | |
| `18` | Turn the screen off | `28` | Mute / unmute | | |

| Appearance | | Power | | Open | |
| --- | --- | --- | --- | --- | --- |
| `41` | Dark mode | `51` | Keep awake on | `61` | Task Manager |
| `42` | Light mode | `52` | Keep awake off | `62` | Settings |
| `43` | Switch dark / light | `53` | Lock the PC | `63` | File Explorer |
| `44` | Show desktop / restore | `54` | Sleep | `64` | Calculator |
| | | | | `65` | Screenshot (snip) |
| | | | | `66` | Notepad |
| | | | | `67` | Command Prompt |

## Typing codes

| You type | What happens |
| --- | --- |
| `11` | Brightness up 1% |
| `11 11 11` or `111111` | Brightness up 3%. Spaces are optional, since every code is two digits |
| `11x5` | Brightness up 5%. `x` repeats the code before it, up to 100 times |
| `15 24` | Brightness to 100%, volume down 10% |
| `26 53` | Mute, then lock the PC |
| `43` | Dark mode if it was light, light if it was dark |

Codes run in the order you type them. If you type a code that doesn't exist,
the window says which one and nothing runs. After a run the codes stay
selected, so pressing Enter again repeats them.

When the window opens it shows the current brightness, volume, dark mode and
keep-awake state. After each run it shows what changed:

```
Brightness: 50% → 55%
Volume: 30% → 40%
Dark mode: on
```

**Keyboard:** Enter runs the codes. Esc minimizes the window.
**Ctrl+Alt+D** brings it back from anywhere while it's running.

## Command line, shortcuts and hotkeys

Pass codes as arguments to run them without opening the window:

```
DigitControl.exe 11x5 21      brightness up 5%, volume up 1%
DigitControl.exe 18           turn the screen off
DigitControl.exe --list       print every code
```

The exit code is `0` when everything worked, `1` when an option didn't (for
example no screen allows brightness changes), and `2` when a code doesn't
exist.

Some ways to use it:

- **Desktop shortcut:** right-click the desktop, **New > Shortcut**, and enter
  `"C:\path\to\DigitControl.exe" 18`. Double-clicking it turns the screen off.
- **Keyboard shortcut:** open that shortcut's **Properties** and set a
  **Shortcut key**, such as Ctrl+Alt+M for `26` (mute).
- **Schedule:** in Task Scheduler, run `DigitControl.exe 41` at sunset and
  `DigitControl.exe 42` in the morning.

## Notes on the options

- **Brightness** changes every screen that lets software set it. Laptop screens
  work through Windows (WMI). Some older laptops only have a few steps, say
  every 10%, so a 1% change may not show until it reaches the next one.
  External monitors work through DDC/CI, which some monitors ship with switched
  off; look for "DDC/CI" in the monitor's own menu. Monitors behind some docks,
  adapters and KVM switches can't be reached.
- **Volume and mute** act on the default speakers or headphones, the ones the
  taskbar volume icon controls.
- **Media keys** (`31` to `34`) press the keyboard's media keys, so they control
  whatever answers them: Spotify, YouTube in a browser, Media Player.
- **Dark and light mode** switch both Windows and apps, the same as
  Settings > Personalization > Colors.
- **Keep awake** stops Windows sleeping or turning the screen off for as long
  as the window stays open. The title bar says "(keeping awake)" while it's on.
  It needs the window, so it doesn't work from the command line.
- **Turn the screen off** and **Sleep** wait one second, so letting go of Enter
  doesn't wake the PC straight back up.

## Build it yourself

1. Download this repository (**Code > Download ZIP**) or clone it.
2. Double-click `build.bat`. It uses the C# compiler that comes with Windows,
   so nothing needs installing, and writes `DigitControl.exe` next to it.

The whole tool is one file, [`DigitControl.cs`](DigitControl.cs). It uses only
the .NET Framework 4 that ships with Windows, and Windows' own APIs.

## Add your own option

Every option is one line in the `Options.All` table in `DigitControl.cs`:

```csharp
Act(66, "Open", "Notepad", r => Apps.Open("notepad.exe", "Notepad")),
```

Pick an unused code, give it a group and a description, and say what it does.
The window, `--list` and the code parser all read the table, so nothing else
needs changing. Ideas and pull requests are welcome; there's an
[issue template for suggesting an option](https://github.com/rajkirant/digit-control/issues/new?template=new-option.md).

## License

[MIT](LICENSE). Use it, change it, share it.
