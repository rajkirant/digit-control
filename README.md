# Digit Control

A small Windows window with one textbox. Type digits and press Enter; each
digit is one option:

| Digit | Option | Digit | Option |
| --- | --- | --- | --- |
| `1` | Brightness up 1% | `0` | Brightness down 1% |
| `2` | Volume up 1% | `3` | Volume down 1% |
| `4` | Mute / unmute | `5` | Play / pause music or video |
| `6` | Dark mode on / off | `7` | Keep the PC awake on / off |
| `8` | Turn the screen off | `9` | Lock the PC |

How the digits combine:

- **Steps add up.** `111` raises brightness by 3%, `3333` lowers the volume
  by 4%, and `1100` leaves brightness where it was.
- **On / off options flip each time they appear.** `4` mutes, `44` mutes and
  unmutes, so nothing changes.
- **8 and 9 happen once**, however often you type them. `98` locks the PC and
  turns the screen off.
- Digits can be mixed in any order: `1112` is brightness +3% and volume +1%.

Spaces are ignored. Anything that isn't a digit is reported and nothing is
changed. After applying, the digits stay selected, so pressing Enter again
repeats them and typing replaces them. Esc closes the window.

When the window opens it shows the current brightness, volume, dark mode and
keep-awake state, and after each Enter it shows what changed.

## Build and run

1. Download or clone this repository on a Windows 10 or 11 PC.
2. Double-click `build.bat`. It uses the C# compiler that comes with Windows,
   so nothing needs installing, and writes `DigitControl.exe` next to it.
3. Double-click `DigitControl.exe`.

## Notes on each option

- **Brightness** changes every screen that lets software set it, each from its
  own level. Laptop screens work through WMI; some older laptops only have a
  few steps (say every 10%), so a 1% change may not show until it reaches the
  next one. External monitors work through DDC/CI, which some monitors ship
  with switched off; look for "DDC/CI" in the monitor's own menu. Monitors on
  some docks, adapters and KVM switches can't be reached.
- **Volume and mute** act on the default speakers or headphones, the same ones
  the taskbar volume icon controls.
- **Play / pause** presses the keyboard's play / pause media key, so it
  controls whatever answers that key: Spotify, a browser tab, Media Player.
- **Dark mode** switches both Windows and apps, the same as
  Settings > Personalization > Colors.
- **Keep awake** stops Windows sleeping or turning the screen off for as long
  as the window stays open. Closing the window turns it off. The title bar
  says "(keeping awake)" while it is on.
- **Turn the screen off** waits one second so that letting go of Enter doesn't
  wake the screen straight away. Moving the mouse or pressing a key wakes it.
- **Lock the PC** is the same as Windows + L.

## Adding an option

The options are one table in `DigitControl.cs` (`Options.All`). Add a line
there with its digit, its description and what it changes, add a field to
`Changes` if it needs one, and add the step to `MainForm.Apply`. The window
lists the table, so nothing else in the UI needs changing.
