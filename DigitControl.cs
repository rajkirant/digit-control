// Digit Control: a small Windows window with one textbox. Type digits and
// press Enter; each digit is one option, and they are applied together:
//
//   1  brightness up 1%        0  brightness down 1%
//   2  volume up 1%            3  volume down 1%
//   4  mute / unmute           5  play / pause music or video
//   6  dark mode on / off      7  keep the PC awake on / off
//   8  turn the screen off     9  lock the PC
//
// Steps add up ("111" is +3%), on / off options flip each time they appear
// ("44" leaves the sound as it was), and 8 and 9 happen once however often
// they are typed.
//
// Build with build.bat, which uses the C# compiler that ships with Windows.
// That compiler only knows C# 5, so this file avoids anything newer
// (no $"..." strings, no ?., no => members).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DigitControl
{
    // What all the digits of one input add up to, applied once at the end
    class Changes
    {
        public int BrightnessDelta;
        public int VolumeDelta;
        public bool ToggleMute;
        public bool PlayPause;
        public bool ToggleDarkMode;
        public bool ToggleKeepAwake;
        public bool ScreenOff;
        public bool Lock;
    }

    class Option
    {
        public readonly char Key;
        public readonly string Description;
        public readonly Action<Changes> Apply;

        public Option(char key, string description, Action<Changes> apply)
        {
            Key = key;
            Description = description;
            Apply = apply;
        }
    }

    static class Options
    {
        // To add an option, add a line here, a field to Changes and a step to
        // MainForm.Apply. The window lists this table two options to a row.
        public static readonly Option[] All =
        {
            new Option('1', "Brightness up 1%", c => c.BrightnessDelta += 1),
            new Option('0', "Brightness down 1%", c => c.BrightnessDelta -= 1),
            new Option('2', "Volume up 1%", c => c.VolumeDelta += 1),
            new Option('3', "Volume down 1%", c => c.VolumeDelta -= 1),
            new Option('4', "Mute / unmute", c => c.ToggleMute = !c.ToggleMute),
            new Option('5', "Play / pause", c => c.PlayPause = !c.PlayPause),
            new Option('6', "Dark mode on / off", c => c.ToggleDarkMode = !c.ToggleDarkMode),
            new Option('7', "Keep awake on / off", c => c.ToggleKeepAwake = !c.ToggleKeepAwake),
            new Option('8', "Turn the screen off", c => c.ScreenOff = true),
            new Option('9', "Lock the PC", c => c.Lock = true),
        };

        public static Option Find(char key)
        {
            foreach (Option option in All)
                if (option.Key == key) return option;
            return null;
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

        // Moves every screen that allows it by delta percent, each from its own
        // current level, and returns one line per screen. A delta of 0 only reads.
        public static List<string> Change(int delta)
        {
            var screens = new List<Screen>();
            ChangeBuiltIn(delta, screens);
            ChangeExternal(delta, screens);

            var lines = new List<string>();
            if (screens.Count == 0)
                lines.Add("Brightness: no screen here lets software change it. External " +
                          "monitors need DDC/CI turned on in their own menu.");
            foreach (Screen screen in screens)
            {
                string label = screens.Count == 1 ? "Brightness" : "Brightness, " + screen.Name;
                lines.Add(screen.Refused
                    ? label + ": " + screen.Before + "% (the screen refused the change)"
                    : label + ": " + Percent.Levels(screen.Before, screen.After));
            }
            return lines;
        }

        static void ChangeBuiltIn(int delta, List<Screen> screens)
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
                        var result = new Screen { Name = "built-in screen", Before = before, After = Percent.Clamp(before + delta) };
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

        static void ChangeExternal(int delta, List<Screen> screens)
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
                        if (!GetMonitorBrightness(monitor.hPhysicalMonitor, out min, out cur, out max) || max <= min)
                            continue;

                        number++;
                        string name = "monitor " + number;
                        if (!string.IsNullOrEmpty(monitor.szPhysicalMonitorDescription))
                            name += " (" + monitor.szPhysicalMonitorDescription + ")";

                        int before = (int)Math.Round((cur - min) * 100.0 / (max - min));
                        var result = new Screen { Name = name, Before = before, After = Percent.Clamp(before + delta) };
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

    // Volume and mute of the default speakers or headphones, through Windows'
    // Core Audio API
    static class Volume
    {
        public static string Change(int delta, bool toggleMute)
        {
            IAudioEndpointVolume speakers;
            try
            {
                speakers = DefaultSpeakers();
            }
            catch (COMException)
            {
                return "Volume: no speakers or headphones found.";
            }

            try
            {
                var context = Guid.Empty;
                float level;
                speakers.GetMasterVolumeLevelScalar(out level);
                int before = (int)Math.Round(level * 100);
                int after = Percent.Clamp(before + delta);
                if (after != before) speakers.SetMasterVolumeLevelScalar(after / 100f, ref context);

                bool muted;
                speakers.GetMute(out muted);
                if (toggleMute)
                {
                    muted = !muted;
                    speakers.SetMute(muted, ref context);
                }

                string line = "Volume: " + Percent.Levels(before, after);
                if (toggleMute) return line + (muted ? ", now muted" : ", now unmuted");
                return muted ? line + " (muted)" : line;
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

    static class Media
    {
        const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1;
        const uint KEYEVENTF_KEYUP = 0x2;

        // Presses the keyboard's play / pause media key, which whatever is
        // playing (Spotify, a browser tab, Media Player) answers
        public static string PlayPause()
        {
            keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
            return "Play / pause: pressed";
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
            int light = IsDark() ? 1 : 0;
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

    static class Power
    {
        public static bool KeepingAwake { get; private set; }

        // Stops Windows sleeping or turning the screen off, for as long as
        // this window is open
        public static string ToggleKeepAwake()
        {
            KeepingAwake = !KeepingAwake;
            SetThreadExecutionState(KeepingAwake
                ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
                : ES_CONTINUOUS);
            return DescribeKeepAwake();
        }

        public static string DescribeKeepAwake()
        {
            return KeepingAwake ? "Keep awake: on while this window is open" : "Keep awake: off";
        }

        public static void Lock()
        {
            LockWorkStation();
        }

        public static void ScreenOff(IntPtr window)
        {
            SendMessage(window, WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(2));
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

        readonly TextBox input = new TextBox();
        readonly Label status = new Label();

        // The screen goes off a moment after Enter, so letting go of the key
        // does not wake it straight back up
        readonly Timer screenOffDelay = new Timer { Interval = 1000 };

        public MainForm()
        {
            Text = Title;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;

            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
            };

            layout.Controls.Add(new Label
            {
                Text = "Type digits, then press Enter. Each digit is one option:",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            });

            var table = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Margin = new Padding(0) };
            foreach (Option option in Options.All)
            {
                table.Controls.Add(new Label
                {
                    Text = option.Key.ToString(),
                    Font = new Font(Font, FontStyle.Bold),
                    AutoSize = true,
                    Margin = new Padding(8, 3, 8, 3),
                });
                table.Controls.Add(new Label { Text = option.Description, AutoSize = true, Margin = new Padding(0, 3, 16, 3) });
            }
            layout.Controls.Add(table);

            layout.Controls.Add(new Label
            {
                Text = "Steps add up: 111 is +3%. On / off options flip each time: 44 changes nothing.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 6, 0, 0),
            });

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            input.Width = 300;
            input.Margin = new Padding(0, 1, 8, 0);
            row.Controls.Add(input);
            var apply = new Button { Text = "Apply", AutoSize = true, Margin = new Padding(0) };
            apply.Click += delegate { ApplyInput(); };
            row.Controls.Add(apply);
            layout.Controls.Add(row);

            status.AutoSize = true;
            status.MaximumSize = new Size(520, 0);
            status.Margin = new Padding(0, 12, 0, 0);
            layout.Controls.Add(status);

            Controls.Add(layout);
            AcceptButton = apply;

            screenOffDelay.Tick += delegate
            {
                screenOffDelay.Stop();
                Power.ScreenOff(Handle);
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += delegate
            {
                ShowLines(Apply(new Changes(), true));
                input.Focus();
            };
        }

        void ApplyInput()
        {
            var changes = new Changes();
            var unknown = new List<string>();
            foreach (char c in input.Text)
            {
                if (char.IsWhiteSpace(c)) continue;
                Option option = Options.Find(c);
                if (option == null)
                {
                    if (!unknown.Contains(c.ToString())) unknown.Add(c.ToString());
                    continue;
                }
                option.Apply(changes);
            }

            if (unknown.Count > 0)
                ShowError("Not an option: " + string.Join(" ", unknown) + ". Use only the digits 0 to 9. Nothing was changed.");
            else if (input.Text.Trim().Length == 0)
                ShowError("Type at least one digit first.");
            else
                ShowLines(Apply(changes, false));

            // Selected, so Enter applies the same digits again and typing replaces them
            input.SelectAll();
            input.Focus();
        }

        // Carries out the changes and returns one line for each thing touched.
        // With everything set it also lists the current state of what was not
        // touched; that is the view the window opens with.
        List<string> Apply(Changes c, bool everything)
        {
            var lines = new List<string>();
            Cursor = Cursors.WaitCursor;
            try
            {
                if (everything || c.BrightnessDelta != 0)
                    Run(lines, "Brightness", delegate { lines.AddRange(Brightness.Change(c.BrightnessDelta)); });
                if (everything || c.VolumeDelta != 0 || c.ToggleMute)
                    Run(lines, "Volume", delegate { lines.Add(Volume.Change(c.VolumeDelta, c.ToggleMute)); });
                if (c.PlayPause)
                    Run(lines, "Play / pause", delegate { lines.Add(Media.PlayPause()); });
                if (everything || c.ToggleDarkMode)
                    Run(lines, "Dark mode", delegate { lines.Add(c.ToggleDarkMode ? Theme.Toggle() : Theme.Describe()); });
                if (everything || c.ToggleKeepAwake)
                    lines.Add(c.ToggleKeepAwake ? Power.ToggleKeepAwake() : Power.DescribeKeepAwake());
                if (c.Lock)
                {
                    Power.Lock();
                    lines.Add("Locking the PC");
                }
                if (c.ScreenOff)
                {
                    screenOffDelay.Start();
                    lines.Add("Turning the screen off. Move the mouse or press a key to wake it.");
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Text = Power.KeepingAwake ? Title + " (keeping awake)" : Title;
            return lines;
        }

        // One option failing never stops the others
        static void Run(List<string> lines, string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                lines.Add(name + ": did not work (" + e.Message.Trim() + ")");
            }
        }

        void ShowLines(List<string> lines)
        {
            status.ForeColor = SystemColors.ControlText;
            status.Text = string.Join(Environment.NewLine, lines);
        }

        void ShowError(string message)
        {
            status.ForeColor = Color.Firebrick;
            status.Text = message;
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            // Sharp text on scaled (high-DPI) screens
            if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
