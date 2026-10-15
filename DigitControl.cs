// Digit Control: control Windows by typing short number codes.
//
// Every option has its own two-digit code, grouped by its first digit:
//
//   1x  display      2x  sound      3x  media
//   4x  appearance   5x  power      6x  open an app
//
// Type codes into the window and press Enter ("11 11 21", or "111121"
// since every code is two digits), add "x5" to repeat a code five times
// ("11x5"), or pass the same codes on the command line:
//
//   DigitControl.exe 11x5 21
//
// Build with build.bat, which uses the C# compiler that ships with Windows.
// That compiler only knows C# 5, so this file avoids anything newer
// (no $"..." strings, no ?., no => members).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DigitControl
{
    // Options that move a level are merged when they follow each other, so
    // "11x50" reads and sets the brightness once instead of fifty times
    enum Level { None, Brightness, Volume }

    class Option
    {
        public int Code;
        public string Group;
        public string Description;
        public Level Level;
        public Func<int, int> Adjust;   // for Level options: old percent to new percent
        public Func<Runner, string> Run; // for the rest: does it, returns one line
    }

    static class Options
    {
        public static readonly string[] Groups = { "Display", "Sound", "Media", "Appearance", "Power", "Open" };

        // To add an option, add a line here with an unused code; the window,
        // the command line's --list and the parser all read this table.
        public static readonly Option[] All =
        {
            Step(11, "Display", "Brightness up 1%", Level.Brightness, b => b + 1),
            Step(12, "Display", "Brightness down 1%", Level.Brightness, b => b - 1),
            Step(13, "Display", "Brightness up 10%", Level.Brightness, b => b + 10),
            Step(14, "Display", "Brightness down 10%", Level.Brightness, b => b - 10),
            Step(15, "Display", "Brightness 100%", Level.Brightness, b => 100),
            Step(16, "Display", "Brightness 50%", Level.Brightness, b => 50),
            Step(17, "Display", "Brightness 0%", Level.Brightness, b => 0),
            Act(18, "Display", "Turn the screen off", Power.ScreenOff),

            Step(21, "Sound", "Volume up 1%", Level.Volume, v => v + 1),
            Step(22, "Sound", "Volume down 1%", Level.Volume, v => v - 1),
            Step(23, "Sound", "Volume up 10%", Level.Volume, v => v + 10),
            Step(24, "Sound", "Volume down 10%", Level.Volume, v => v - 10),
            Step(25, "Sound", "Volume 50%", Level.Volume, v => 50),
            Act(26, "Sound", "Mute", r => Volume.SetMute(true)),
            Act(27, "Sound", "Unmute", r => Volume.SetMute(false)),
            Act(28, "Sound", "Mute / unmute", r => Volume.SetMute(null)),

            Act(31, "Media", "Play / pause", r => Media.Press(Media.PlayPause, "Play / pause")),
            Act(32, "Media", "Next track", r => Media.Press(Media.Next, "Next track")),
            Act(33, "Media", "Previous track", r => Media.Press(Media.Previous, "Previous track")),
            Act(34, "Media", "Stop", r => Media.Press(Media.Stop, "Stop")),

            Act(41, "Appearance", "Dark mode", r => Theme.Set(true)),
            Act(42, "Appearance", "Light mode", r => Theme.Set(false)),
            Act(43, "Appearance", "Switch dark / light", r => Theme.Toggle()),
            Act(44, "Appearance", "Show desktop / restore", r => Desktop.Toggle()),

            Act(51, "Power", "Keep awake on", r => Power.KeepAwake(r, true)),
            Act(52, "Power", "Keep awake off", r => Power.KeepAwake(r, false)),
            Act(53, "Power", "Lock the PC", r => Power.Lock()),
            Act(54, "Power", "Sleep", Power.Sleep),

            Act(61, "Open", "Task Manager", r => Apps.Open("taskmgr.exe", "Task Manager")),
            Act(62, "Open", "Settings", r => Apps.Open("ms-settings:", "Settings")),
            Act(63, "Open", "File Explorer", r => Apps.Open("explorer.exe", "File Explorer")),
            Act(64, "Open", "Calculator", r => Apps.Open("calc.exe", "Calculator")),
            Act(65, "Open", "Screenshot (snip)", r => Apps.Open("ms-screenclip:", "Screenshot")),
            Act(66, "Open", "Notepad", r => Apps.Open("notepad.exe", "Notepad")),
            Act(67, "Open", "Command Prompt", r => Apps.Open("cmd.exe", "Command Prompt")),
        };

        public static Option Find(int code)
        {
            foreach (Option option in All)
                if (option.Code == code) return option;
            return null;
        }

        static Option Step(int code, string group, string description, Level level, Func<int, int> adjust)
        {
            return new Option { Code = code, Group = group, Description = description, Level = level, Adjust = adjust };
        }

        static Option Act(int code, string group, string description, Func<Runner, string> run)
        {
            return new Option { Code = code, Group = group, Description = description, Run = run };
        }
    }

    // Turns typed text into the options to run, in order
    static class Parser
    {
        const int MaxRepeat = 100;

        public static List<Option> Parse(string input, out string error)
        {
            var steps = new List<Option>();
            var unknown = new List<string>();
            error = null;

            foreach (string token in input.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = Regex.Match(token, @"^(\d+)(?:[xX*](\d+))?$");
                if (!match.Success)
                {
                    error = "\"" + token + "\" isn't a code. Codes are two digits, like 11 or 21; add x5 to repeat one five times.";
                    return null;
                }
                string digits = match.Groups[1].Value;
                if (digits.Length % 2 != 0)
                {
                    error = "\"" + token + "\" has an odd number of digits. Every code is two digits, like 11 or 21.";
                    return null;
                }
                int repeat = 1;
                if (match.Groups[2].Success && (!int.TryParse(match.Groups[2].Value, out repeat) || repeat < 1 || repeat > MaxRepeat))
                {
                    error = "\"" + token + "\": repeat a code between 1 and " + MaxRepeat + " times.";
                    return null;
                }

                for (int i = 0; i < digits.Length; i += 2)
                {
                    string code = digits.Substring(i, 2);
                    Option option = Options.Find(int.Parse(code));
                    if (option == null)
                    {
                        if (!unknown.Contains(code)) unknown.Add(code);
                        continue;
                    }
                    // A repeat count belongs to the code just before it
                    int times = i == digits.Length - 2 ? repeat : 1;
                    for (int t = 0; t < times; t++) steps.Add(option);
                }
            }

            if (unknown.Count > 0)
            {
                error = "No option has the code " + string.Join(", ", unknown) + ". Nothing was changed.";
                return null;
            }
            if (steps.Count == 0)
            {
                error = "Type at least one code, like 11.";
                return null;
            }
            return steps;
        }
    }

    // A friendly failure: its message is shown as it is
    class Problem : Exception
    {
        public Problem(string message) : base(message) { }
    }

    // One run of a list of options: what it reports, and what is left to do
    // a moment later (screen off and sleep wait, so letting go of Enter or
    // the mouse does not undo them)
    class Runner
    {
        public bool InWindow;
        public bool Failed;
        public readonly List<string> Lines = new List<string>();
        public readonly List<Action> Later = new List<Action>();
    }

    static class Engine
    {
        public static Runner Run(List<Option> steps, bool inWindow)
        {
            var runner = new Runner { InWindow = inWindow };
            int i = 0;
            while (i < steps.Count)
            {
                Option option = steps[i];
                if (option.Level == Level.None)
                {
                    Guard(runner, option.Description, delegate { runner.Lines.Add(option.Run(runner)); });
                    i++;
                    continue;
                }

                Func<int, int> adjust = x => x;
                while (i < steps.Count && steps[i].Level == option.Level)
                {
                    adjust = Then(adjust, steps[i].Adjust);
                    i++;
                }
                if (option.Level == Level.Brightness)
                    Guard(runner, "Brightness", delegate { runner.Lines.AddRange(Brightness.Change(adjust)); });
                else
                    Guard(runner, "Volume", delegate { runner.Lines.Add(Volume.Change(adjust)); });
            }
            return runner;
        }

        // The current state, shown when the window opens
        public static Runner Describe()
        {
            var runner = new Runner { InWindow = true };
            Guard(runner, "Brightness", delegate { runner.Lines.AddRange(Brightness.Change(x => x)); });
            Guard(runner, "Volume", delegate { runner.Lines.Add(Volume.Describe()); });
            Guard(runner, "Dark mode", delegate { runner.Lines.Add(Theme.Describe()); });
            runner.Lines.Add(Power.DescribeKeepAwake());
            return runner;
        }

        static Func<int, int> Then(Func<int, int> first, Func<int, int> next)
        {
            return x => Percent.Clamp(next(first(x)));
        }

        // One option failing never stops the others
        static void Guard(Runner runner, string name, Action step)
        {
            try
            {
                step();
            }
            catch (Problem e)
            {
                runner.Failed = true;
                runner.Lines.Add(name + ": " + e.Message);
            }
            catch (Exception e)
            {
                runner.Failed = true;
                runner.Lines.Add(name + ": did not work (" + e.Message.Trim() + ")");
            }
        }
    }

    static class Percent
    {
        public static string Levels(int before, int after)
        {
            if (before == after) return before + "%";
            return before + "% → " + after + "%";
        }

        public static int Clamp(int percent)
        {
            return Math.Max(0, Math.Min(100, percent));
        }
    }

    // Screen brightness in percent. Laptop screens are reached through WMI,
    // external monitors through DDC/CI, which most monitors support but some
    // ship with switched off in their own on-screen menu.
    static class Brightness
    {
        class Screen
        {
            public string Name;
            public int Before;
            public int After;
            public bool Refused;
        }

        // Sets every screen that allows it to adjust(its own current level)
        // and returns one line per screen
        public static List<string> Change(Func<int, int> adjust)
        {
            var screens = new List<Screen>();
            ChangeBuiltIn(adjust, screens);
            ChangeExternal(adjust, screens);
            if (screens.Count == 0)
                throw new Problem("no screen here lets software change it. External monitors need " +
                                  "DDC/CI turned on in their own menu.");

            var lines = new List<string>();
            foreach (Screen screen in screens)
            {
                string label = screens.Count == 1 ? "Brightness" : "Brightness, " + screen.Name;
                lines.Add(screen.Refused
                    ? label + ": " + screen.Before + "% (the screen refused the change)"
                    : label + ": " + Percent.Levels(screen.Before, screen.After));
            }
            return lines;
        }

        static void ChangeBuiltIn(Func<int, int> adjust, List<Screen> screens)
        {
            var current = new Dictionary<string, int>();
            try
            {
                using (var query = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (ManagementObject screen in query.Get())
                        current[(string)screen["InstanceName"]] = Convert.ToInt32(screen["CurrentBrightness"]);

                using (var query = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    foreach (ManagementObject screen in query.Get())
                    {
                        int before;
                        if (!current.TryGetValue((string)screen["InstanceName"], out before)) continue;
                        var result = new Screen { Name = "built-in screen", Before = before, After = Percent.Clamp(adjust(before)) };
                        try
                        {
                            if (result.After != before)
                                screen.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)result.After });
                        }
                        catch (ManagementException)
                        {
                            result.Refused = true;
                        }
                        screens.Add(result);
                    }
            }
            catch (ManagementException)
            {
                // Desktops have no WMI brightness at all; their monitors are
                // handled by ChangeExternal.
            }
            catch (COMException)
            {
            }
        }

        static void ChangeExternal(Func<int, int> adjust, List<Screen> screens)
        {
            var handles = new List<IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data) { handles.Add(hMonitor); return true; },
                IntPtr.Zero);

            int number = 0;
            foreach (IntPtr hMonitor in handles)
            {
                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0) continue;
                var monitors = new PHYSICAL_MONITOR[count];
                if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors)) continue;
                try
                {
                    foreach (PHYSICAL_MONITOR monitor in monitors)
                    {
                        uint min, cur, max;
                        // Fails for laptop screens and for monitors without DDC/CI
                        if (!GetMonitorBrightness(monitor.hPhysicalMonitor, out min, out cur, out max) || max <= min || cur < min)
                            continue;

                        number++;
                        string name = "monitor " + number;
                        if (!string.IsNullOrEmpty(monitor.szPhysicalMonitorDescription))
                            name += " (" + monitor.szPhysicalMonitorDescription + ")";

                        int before = (int)Math.Round((cur - min) * 100.0 / (max - min));
                        var result = new Screen { Name = name, Before = before, After = Percent.Clamp(adjust(before)) };
                        uint raw = min + (uint)Math.Round(result.After * (max - min) / 100.0);
                        if (result.After != before && !SetMonitorBrightness(monitor.hPhysicalMonitor, raw))
                            result.Refused = true;
                        screens.Add(result);
                    }
                }
                finally
                {
                    DestroyPhysicalMonitors(count, monitors);
                }
            }
        }

        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("dxva2.dll", SetLastError = true)]
        static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

        [DllImport("dxva2.dll", SetLastError = true)]
        static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint min, out uint current, out uint max);

        [DllImport("dxva2.dll", SetLastError = true)]
        static extern bool SetMonitorBrightness(IntPtr hMonitor, uint brightness);
    }

    // Volume and mute of the default speakers or headphones (the ones the
    // taskbar volume icon controls), through Windows' Core Audio API
    static class Volume
    {
        public static string Change(Func<int, int> adjust)
        {
            return WithSpeakers(delegate(IAudioEndpointVolume speakers)
            {
                var context = Guid.Empty;
                float level;
                speakers.GetMasterVolumeLevelScalar(out level);
                int before = (int)Math.Round(level * 100);
                int after = Percent.Clamp(adjust(before));
                if (after != before) speakers.SetMasterVolumeLevelScalar(after / 100f, ref context);
                bool muted;
                speakers.GetMute(out muted);
                return "Volume: " + Percent.Levels(before, after) + (muted ? " (muted)" : "");
            });
        }

        // true mutes, false unmutes, null switches
        public static string SetMute(bool? mute)
        {
            return WithSpeakers(delegate(IAudioEndpointVolume speakers)
            {
                var context = Guid.Empty;
                bool muted;
                speakers.GetMute(out muted);
                bool want = mute.HasValue ? mute.Value : !muted;
                if (want != muted) speakers.SetMute(want, ref context);
                return want ? "Sound: muted" : "Sound: on";
            });
        }

        public static string Describe()
        {
            return Change(x => x);
        }

        static string WithSpeakers(Func<IAudioEndpointVolume, string> use)
        {
            IAudioEndpointVolume speakers;
            try
            {
                speakers = DefaultSpeakers();
            }
            catch (COMException)
            {
                throw new Problem("no speakers or headphones found.");
            }
            try
            {
                return use(speakers);
            }
            finally
            {
                Marshal.ReleaseComObject(speakers);
            }
        }

        static IAudioEndpointVolume DefaultSpeakers()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice device = null;
            try
            {
                enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device);
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object endpoint;
                device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out endpoint);
                return (IAudioEndpointVolume)endpoint;
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator
        {
        }

        // Only the methods up to the last one used are declared; the order
        // must match the COM interface exactly.
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            void Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                          [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            void RegisterControlChangeNotify(IntPtr notify);
            void UnregisterControlChangeNotify(IntPtr notify);
            void GetChannelCount(out uint count);
            void SetMasterVolumeLevel(float levelDb, ref Guid context);
            void SetMasterVolumeLevelScalar(float level, ref Guid context);
            void GetMasterVolumeLevel(out float levelDb);
            void GetMasterVolumeLevelScalar(out float level);
            void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
            void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            void GetChannelVolumeLevel(uint channel, out float levelDb);
            void GetChannelVolumeLevelScalar(uint channel, out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }

    // Presses the keyboard's media keys, which whatever is playing (Spotify,
    // a browser tab, Media Player) answers
    static class Media
    {
        public const byte Next = 0xB0;
        public const byte Previous = 0xB1;
        public const byte Stop = 0xB2;
        public const byte PlayPause = 0xB3;

        const uint KEYEVENTF_EXTENDEDKEY = 0x1;
        const uint KEYEVENTF_KEYUP = 0x2;

        public static string Press(byte key, string name)
        {
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
            return name + ": pressed";
        }

        [DllImport("user32.dll")]
        static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
    }

    // Windows' light / dark mode, the same switch as Settings > Personalization > Colors
    static class Theme
    {
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static string Describe()
        {
            return "Dark mode: " + (IsDark() ? "on" : "off");
        }

        public static string Toggle()
        {
            return Set(!IsDark());
        }

        public static string Set(bool dark)
        {
            int light = dark ? 0 : 1;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Personalize))
            {
                key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
                key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
            }
            // Tells open windows to redraw in the new colours
            IntPtr result;
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet",
                               SMTO_ABORTIFHUNG, 200, out result);
            return Describe();
        }

        static bool IsDark()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Personalize))
            {
                object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                return value is int && (int)value == 0;
            }
        }

        static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        const uint WM_SETTINGCHANGE = 0x001A;
        const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
                                                uint flags, uint timeout, out IntPtr result);
    }

    static class Desktop
    {
        // The same as Windows + D: hides every window, or brings them back
        public static string Toggle()
        {
            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                shellType.InvokeMember("ToggleDesktop", BindingFlags.InvokeMethod, null, shell, null);
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
            return "Show desktop / restore: done";
        }
    }

    static class Apps
    {
        public static string Open(string target, string name)
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return "Opened " + name;
        }
    }

    static class Power
    {
        public static bool KeepingAwake { get; private set; }

        // Stops Windows sleeping or turning the screen off for as long as
        // the window stays open
        public static string KeepAwake(Runner runner, bool on)
        {
            if (!runner.InWindow)
                throw new Problem("only works while the window is open. Run DigitControl.exe without codes, then type 51.");
            KeepingAwake = on;
            SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS);
            return DescribeKeepAwake();
        }

        public static string DescribeKeepAwake()
        {
            return KeepingAwake ? "Keep awake: on while this window is open" : "Keep awake: off";
        }

        public static string Lock()
        {
            if (!LockWorkStation()) throw new Problem("Windows refused to lock.");
            return "Locking the PC";
        }

        public static string ScreenOff(Runner runner)
        {
            runner.Later.Add(delegate
            {
                // A hidden window of our own to send the request through
                var window = new NativeWindow();
                window.CreateHandle(new CreateParams());
                try
                {
                    SendMessage(window.Handle, WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(2));
                }
                finally
                {
                    window.DestroyHandle();
                }
            });
            return "Turning the screen off. Move the mouse or press a key to wake it.";
        }

        public static string Sleep(Runner runner)
        {
            runner.Later.Add(delegate { Application.SetSuspendState(PowerState.Suspend, false, false); });
            return "Going to sleep";
        }

        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;
        const uint ES_DISPLAY_REQUIRED = 0x00000002;
        const uint WM_SYSCOMMAND = 0x0112;
        const int SC_MONITORPOWER = 0xF170;

        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);

        [DllImport("user32.dll")]
        static extern bool LockWorkStation();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

    class MainForm : Form
    {
        const string Title = "Digit Control";
        const int HotkeyId = 1;

        readonly TextBox input = new TextBox { Name = "input" };
        readonly Label status = new Label();
        readonly Label hint = new Label();
        readonly Timer later = new Timer { Interval = 1000 };
        readonly List<Action> pending = new List<Action>();

        public MainForm()
        {
            Text = Title;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }

            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14),
            };

            layout.Controls.Add(new Label
            {
                Text = "Type codes and press Enter, like 11 11 21 or 11x5. Click an option to run it now.",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8),
            });

            var groups = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0) };
            foreach (string group in Options.Groups)
                groups.Controls.Add(GroupPanel(group));
            layout.Controls.Add(groups);

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            input.Width = 360;
            input.Margin = new Padding(0, 1, 8, 0);
            row.Controls.Add(input);
            var apply = new Button { Text = "Apply", AutoSize = true, Margin = new Padding(0) };
            apply.Click += delegate { RunInput(); };
            row.Controls.Add(apply);
            layout.Controls.Add(row);

            status.AutoSize = true;
            status.MaximumSize = new Size(600, 0);
            status.Margin = new Padding(0, 12, 0, 0);
            layout.Controls.Add(status);

            hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText;
            hint.Margin = new Padding(0, 12, 0, 0);
            layout.Controls.Add(hint);

            Controls.Add(layout);
            AcceptButton = apply;

            later.Tick += delegate
            {
                later.Stop();
                var actions = new List<Action>(pending);
                pending.Clear();
                foreach (Action action in actions)
                {
                    try { action(); }
                    catch (Exception e) { ShowError(e.Message); }
                }
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) WindowState = FormWindowState.Minimized;
            };
            Shown += delegate
            {
                ShowResult(Engine.Describe());
                input.Focus();
            };
        }

        Control GroupPanel(string group)
        {
            var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 24, 10) };
            var header = new Label
            {
                Text = group,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4),
            };
            panel.Controls.Add(header);
            panel.SetColumnSpan(header, 2);

            foreach (Option option in Options.All)
            {
                if (option.Group != group) continue;
                panel.Controls.Add(new Label
                {
                    Text = option.Code.ToString(),
                    Font = new Font(FontFamily.GenericMonospace, Font.Size, FontStyle.Bold),
                    AutoSize = true,
                    Margin = new Padding(0, 2, 8, 2),
                });
                var link = new LinkLabel
                {
                    Text = option.Description,
                    AutoSize = true,
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    LinkColor = SystemColors.ControlText,
                    ActiveLinkColor = SystemColors.HotTrack,
                    Margin = new Padding(0, 2, 0, 2),
                };
                Option chosen = option;
                link.LinkClicked += delegate
                {
                    input.Text = chosen.Code.ToString();
                    Run(new List<Option> { chosen });
                    input.Focus();
                    input.SelectAll();
                };
                panel.Controls.Add(link);
            }
            return panel;
        }

        void RunInput()
        {
            string error;
            List<Option> steps = Parser.Parse(input.Text, out error);
            if (steps == null) ShowError(error);
            else Run(steps);

            // Selected, so Enter runs the same codes again and typing replaces them
            input.SelectAll();
            input.Focus();
        }

        void Run(List<Option> steps)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                Runner runner = Engine.Run(steps, true);
                ShowResult(runner);
                if (runner.Later.Count > 0)
                {
                    pending.AddRange(runner.Later);
                    later.Stop();
                    later.Start();
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        void ShowResult(Runner runner)
        {
            status.ForeColor = SystemColors.ControlText;
            status.Text = string.Join(Environment.NewLine, runner.Lines);
            Text = Power.KeepingAwake ? Title + " (keeping awake)" : Title;
        }

        void ShowError(string message)
        {
            status.ForeColor = Color.Firebrick;
            status.Text = message;
        }

        // Ctrl+Alt+D brings the window back from anywhere
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bool registered = RegisterHotKey(Handle, HotkeyId, MOD_ALT | MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.D);
            hint.Text = registered
                ? "Ctrl+Alt+D brings this window back from anywhere. Esc minimizes it."
                : "Esc minimizes this window. (Ctrl+Alt+D is taken by another app.)";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(Handle, HotkeyId);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                input.Focus();
                input.SelectAll();
            }
            base.WndProc(ref m);
        }

        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x1;
        const uint MOD_CONTROL = 0x2;
        const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0) return CommandLine(args);

            // Sharp text on scaled (high-DPI) screens
            if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        // DigitControl.exe 11x5 21 runs the codes without opening the window.
        // Exit code 0: all done, 1: something did not work, 2: bad codes.
        static int CommandLine(string[] args)
        {
            // A window app has no console of its own; borrow the one it was
            // started from, unless its output is already going to a file or pipe
            if (GetStdHandle(STD_OUTPUT_HANDLE) == IntPtr.Zero) AttachConsole(ATTACH_PARENT_PROCESS);
            Console.WriteLine();

            string first = args[0].ToLowerInvariant();
            if (first == "--list" || first == "--help" || first == "-h" || first == "/?")
            {
                Console.WriteLine("Digit Control: run Windows options by number.");
                Console.WriteLine();
                Console.WriteLine("  DigitControl.exe             open the window");
                Console.WriteLine("  DigitControl.exe 11x5 21     brightness up 5%, then volume up 1%");
                Console.WriteLine("  DigitControl.exe --list      this list");
                foreach (string group in Options.Groups)
                {
                    Console.WriteLine();
                    Console.WriteLine(group);
                    foreach (Option option in Options.All)
                        if (option.Group == group) Console.WriteLine("  " + option.Code + "  " + option.Description);
                }
                return 0;
            }

            string error;
            List<Option> steps = Parser.Parse(string.Join(" ", args), out error);
            if (steps == null)
            {
                Console.WriteLine(error);
                return 2;
            }

            Runner runner = Engine.Run(steps, false);
            foreach (string line in runner.Lines) Console.WriteLine(line);
            if (runner.Later.Count > 0)
            {
                System.Threading.Thread.Sleep(1000);
                foreach (Action action in runner.Later)
                {
                    try { action(); }
                    catch (Exception e) { Console.WriteLine(e.Message); runner.Failed = true; }
                }
            }
            return runner.Failed ? 1 : 0;
        }

        const int STD_OUTPUT_HANDLE = -11;
        const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int handle);
    }
}
