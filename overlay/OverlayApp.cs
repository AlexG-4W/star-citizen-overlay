using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace SCOverlay
{
    public class OverlayWindow : Form
    {
        private string _currentText = "";
        private Timer _clearTimer;
        private Dictionary<string, string> _mappings = new Dictionary<string, string>();
        private Dictionary<IntPtr, string> _deviceToJsMap = new Dictionary<IntPtr, string>();
        private Dictionary<string, bool> _activeButtons = new Dictionary<string, bool>();
        // Cache preparsed HID data per device — avoids AllocHGlobal on every raw input event
        private Dictionary<IntPtr, IntPtr> _preparsedDataCache = new Dictionary<IntPtr, IntPtr>();
        
        // Anti-Spam state for axes
        private Dictionary<string, uint> _lastAxisValues = new Dictionary<string, uint>();
        private Dictionary<string, DateTime> _axisLastTrigger = new Dictionary<string, DateTime>();
        
        private System.Threading.Timer _fswDebounceTimer;
        private string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay.log");
        private NotifyIcon _trayIcon;
        private Timer _topMostTimer;
        private Font _displayFont;
        // GDI Caches
        private Brush _textBrush;
        private Brush _shadowBrush;
        private FileSystemWatcher _watcher;
        private const long MAX_LOG_SIZE = 5 * 1024 * 1024;
        
        // Async Logging
        private System.Collections.Concurrent.ConcurrentQueue<string> _logQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private System.Threading.Timer _logFlushTimer;

        // Config-driven UI settings with defaults
        private string _cfgFontName = "Arial";
        private int _cfgFontSize = 28;
        private string _cfgPosition = "bottom-right"; // "bottom-right" or "center"
        private int _cfgDisplayDurationMs = 2000;
        private Color _cfgTextColor = Color.LimeGreen;
        private bool _cfgShowUnmappedInputs = false;

        // Tray menu item references for dynamic updates
        private MenuItem _statusMenuItem;

        // WinAPI Constants
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOPMOST = 0x8;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        // SetWindowPos Constants
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        // Raw Input Constants
        private const int WM_INPUT = 0x00FF;
        private const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
        private const int RID_INPUT = 0x10000003;
        private const int RID_DEVICENAME = 0x20000007;
        private const int RID_PREPARSEDDATA = 0x20000005;
        private const int RIM_TYPEKEYBOARD = 1;
        private const int RIM_TYPEHID = 2;
        private const int RIDEV_INPUTSINK = 0x00000100;
        private const int RIDEV_DEVNOTIFY = 0x00002000;

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct HIDP_VALUE_CAPS
        {
            [FieldOffset(0)] public ushort UsagePage;
            [FieldOffset(2)] public byte ReportID;
            [FieldOffset(3), MarshalAs(UnmanagedType.U1)] public bool IsAlias;
            [FieldOffset(4)] public ushort BitField;
            [FieldOffset(6)] public ushort LinkCollection;
            [FieldOffset(8)] public ushort LinkUsage;
            [FieldOffset(10)] public ushort LinkUsagePage;
            [FieldOffset(12), MarshalAs(UnmanagedType.U1)] public bool IsRange;
            [FieldOffset(13), MarshalAs(UnmanagedType.U1)] public bool IsStringRange;
            [FieldOffset(14), MarshalAs(UnmanagedType.U1)] public bool IsDesignatorRange;
            [FieldOffset(15), MarshalAs(UnmanagedType.U1)] public bool IsAbsolute;
            [FieldOffset(16), MarshalAs(UnmanagedType.U1)] public bool HasNull;
            [FieldOffset(17)] public byte Reserved;
            [FieldOffset(18)] public ushort BitSize;
            [FieldOffset(20)] public ushort ReportCount;
            // Reserved2 is 5 ushorts (10 bytes) offset 22
            [FieldOffset(22)] public ushort Reserved2_0;
            [FieldOffset(24)] public ushort Reserved2_1;
            [FieldOffset(26)] public ushort Reserved2_2;
            [FieldOffset(28)] public ushort Reserved2_3;
            [FieldOffset(30)] public ushort Reserved2_4;
            [FieldOffset(32)] public uint UnitsExp;
            [FieldOffset(36)] public uint Units;
            [FieldOffset(40)] public int LogicalMin;
            [FieldOffset(44)] public int LogicalMax;
            [FieldOffset(48)] public int PhysicalMin;
            [FieldOffset(52)] public int PhysicalMax;
            // Range (Start of Union)
            [FieldOffset(56)] public ushort UsageMin;
            [FieldOffset(58)] public ushort UsageMax;
            [FieldOffset(60)] public ushort StringMin;
            [FieldOffset(62)] public ushort StringMax;
            [FieldOffset(64)] public ushort DesignatorMin;
            [FieldOffset(66)] public ushort DesignatorMax;
            [FieldOffset(68)] public ushort DataIndexMin;
            [FieldOffset(70)] public ushort DataIndexMax;
            // NotRange (Overlapping Union)
            [FieldOffset(56)] public ushort Usage;
            [FieldOffset(58)] public ushort Reserved1_0;
            [FieldOffset(60)] public ushort StringIndex;
            [FieldOffset(62)] public ushort Reserved2_A;
            [FieldOffset(64)] public ushort DesignatorIndex;
            [FieldOffset(66)] public ushort Reserved3_0;
            [FieldOffset(68)] public ushort DataIndex;
            [FieldOffset(70)] public ushort Reserved4_0;
        }

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetUsages(int ReportType, ushort UsagePage, ushort LinkCollection, [In, Out] ushort[] UsageList, ref uint UsageLength, IntPtr PreparsedData, IntPtr Report, uint ReportLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetValueCaps(int ReportType, [In, Out] HIDP_VALUE_CAPS[] ValueCaps, ref ushort ValueCapsLength, IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetUsageValue(int ReportType, ushort UsagePage, ushort LinkCollection, ushort Usage, out uint UsageValue, IntPtr PreparsedData, IntPtr Report, uint ReportLength);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public OverlayWindow()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            // Production chroma-key transparency: near-black (1,1,1) is the "hole"
            this.BackColor = Color.FromArgb(1, 1, 1);
            this.TransparencyKey = Color.FromArgb(1, 1, 1);
            this.Opacity = 1.0;   // Must be 1.0 when using TransparencyKey
            this.StartPosition = FormStartPosition.Manual;
            this.DoubleBuffered = true;

            // Load config first so font/color/position are set before first paint
            LoadConfig();

            _displayFont = new Font(_cfgFontName, _cfgFontSize, FontStyle.Bold);

            _topMostTimer = new Timer();
            _topMostTimer.Interval = 1000;
            _topMostTimer.Tick += (s, e) => { 
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); 
            };
            _topMostTimer.Start();

            _clearTimer = new Timer();
            _clearTimer.Interval = _cfgDisplayDurationMs;
            _clearTimer.Tick += (s, e) => {
                _currentText = "";
                this.Invalidate();
                _clearTimer.Stop();
            };

            _shadowBrush = new SolidBrush(Color.Black);
            _textBrush = new SolidBrush(_cfgTextColor);

            _fswDebounceTimer = new System.Threading.Timer(FswDebounceTimerCallback, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _logFlushTimer = new System.Threading.Timer(LogFlushCallback, null, 100, 100);

            SetupTrayIcon();
            LoadMappings();
            SetupWatcher();
            RegisterDevices();
            Log("Overlay initialized (64-bit alignment fix + Click-through + Jitter Fix).");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_displayFont != null) _displayFont.Dispose();
                if (_watcher != null) _watcher.Dispose();
                if (_textBrush != null) _textBrush.Dispose();
                if (_shadowBrush != null) _shadowBrush.Dispose();
                if (_fswDebounceTimer != null) _fswDebounceTimer.Dispose();
                if (_logFlushTimer != null)
                {
                    _logFlushTimer.Dispose();
                    FlushLogQueue();
                }
            }
            base.Dispose(disposing);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        // -------------------------------------------------------------------
        //  Config loading (config.json)
        // -------------------------------------------------------------------

        private string GetParserDir()
        {
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "parser"));
        }

        private void LoadConfig()
        {
            try
            {
                string configPath = Path.Combine(GetParserDir(), "config.json");
                if (!File.Exists(configPath))
                {
                    Log("config.json not found, using defaults.");
                    return;
                }

                string json;
                using (var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    json = sr.ReadToEnd();
                }

                var serializer = new JavaScriptSerializer();
                var cfg = serializer.Deserialize<Dictionary<string, object>>(json);
                if (cfg == null) return;

                object val;
                if (cfg.TryGetValue("font_name", out val) && val is string)
                    _cfgFontName = (string)val;

                if (cfg.TryGetValue("font_size", out val))
                {
                    try
                    {
                        int parsed = Convert.ToInt32(val);
                        if (parsed >= 8 && parsed <= 200) _cfgFontSize = parsed;
                    }
                    catch { Log("Warning: invalid font_size in config.json, using default."); }
                }

                if (cfg.TryGetValue("position", out val) && val is string)
                    _cfgPosition = ((string)val).ToLower();

                if (cfg.TryGetValue("display_duration_ms", out val) && val is int)
                    _cfgDisplayDurationMs = (int)val;

                if (cfg.TryGetValue("text_color", out val) && val is string)
                {
                    try
                    {
                        string hex = ((string)val).Trim();
                        if (hex.StartsWith("#")) hex = hex.Substring(1);
                        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                        _cfgTextColor = Color.FromArgb(r, g, b);
                    }
                    catch { Log("Warning: invalid text_color in config.json, using default."); }
                }

                if (cfg.TryGetValue("show_unmapped_inputs", out val) && val is bool)
                    _cfgShowUnmappedInputs = (bool)val;

                Log(string.Format("Config loaded: font={0} {1}pt, position={2}, duration={3}ms, color={4}, unmapped={5}",
                    _cfgFontName, _cfgFontSize, _cfgPosition, _cfgDisplayDurationMs, _cfgTextColor.Name, _cfgShowUnmappedInputs));
            }
            catch (Exception ex) { Log("Config Load Error: " + ex.Message); }
        }

        private void ApplyConfig()
        {
            LoadConfig();

            // Rebuild font
            if (_displayFont != null) _displayFont.Dispose();
            _displayFont = new Font(_cfgFontName, _cfgFontSize, FontStyle.Bold);
            
            if (_textBrush != null) _textBrush.Dispose();
            _textBrush = new SolidBrush(_cfgTextColor);

            // Update clear timer interval
            _clearTimer.Interval = _cfgDisplayDurationMs;

            this.Invalidate();
            Log("Config applied.");
        }

        // -------------------------------------------------------------------
        //  Tray icon with enhanced menu
        // -------------------------------------------------------------------

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.Text = "SC Key Overlay";
            _trayIcon.Visible = true;

            _statusMenuItem = new MenuItem(string.Format("Loaded: {0} bindings", _mappings.Count));
            _statusMenuItem.Enabled = false;

            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add(_statusMenuItem);
            menu.MenuItems.Add("-"); // separator
            menu.MenuItems.Add("Reload Mappings && Config", (s, e) => {
                LoadMappings();
                ApplyConfig();
                Log("Manual reload triggered from tray menu.");
            });
            menu.MenuItems.Add("Open Log", (s, e) => {
                try
                {
                    string logFullPath = Path.GetFullPath(_logPath);
                    if (File.Exists(logFullPath))
                    {
                        System.Diagnostics.Process.Start("notepad.exe", logFullPath);
                    }
                    else
                    {
                        Log("Log file does not exist yet: " + logFullPath);
                    }
                }
                catch (Exception ex) { Log("Failed to open log: " + ex.Message); }
            });
            menu.MenuItems.Add("-"); // separator
            menu.MenuItems.Add("Exit", (s, e) => {
                _trayIcon.Visible = false;
                Application.Exit();
            });
            _trayIcon.ContextMenu = menu;
        }

        private void UpdateStatusMenuItem()
        {
            if (_statusMenuItem != null)
            {
                // Count only real bindings, not __device_ entries
                int count = 0;
                foreach (var kvp in _mappings)
                {
                    if (!kvp.Key.StartsWith("__device_")) count++;
                }
                _statusMenuItem.Text = string.Format("Loaded: {0} bindings", count);
            }
        }

        // -------------------------------------------------------------------
        //  FileSystemWatcher — watches mapping.json AND config.json
        // -------------------------------------------------------------------

        private void SetupWatcher()
        {
            try
            {
                string dir = GetParserDir();

                if (Directory.Exists(dir))
                {
                    _watcher = new FileSystemWatcher(dir, "*.json");
                    _watcher.NotifyFilter = NotifyFilters.LastWrite;
                    _watcher.Changed += (s, e) => {
                        // Restart debounce timer on any change
                        // Wait 500ms after the LAST change event
                        if (_fswDebounceTimer != null) _fswDebounceTimer.Change(500, System.Threading.Timeout.Infinite);
                    };
                    _watcher.EnableRaisingEvents = true;
                }
                else
                {
                    Log("Watcher setup failed: Directory does not exist - " + dir);
                }
            }
            catch (Exception ex) { Log("Error setting up FileSystemWatcher: " + ex.Message); }
        }

        private void FswDebounceTimerCallback(object state)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            this.BeginInvoke(new Action(() => {
                Log("File changed. Reloading mappings and config...");
                LoadMappings();
                ApplyConfig();
            }));
        }

        // -------------------------------------------------------------------
        //  Mapping loading
        // -------------------------------------------------------------------

        private void LoadMappings()
        {
            try
            {
                string jsonPath = Path.Combine(GetParserDir(), "mapping.json");
                if (File.Exists(jsonPath))
                {
                    string json;
                    using (var fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        json = sr.ReadToEnd();
                    }
                    var serializer = new JavaScriptSerializer();
                    _mappings = serializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                    Log(string.Format("Successfully loaded {0} bindings from mapping.json.", _mappings.Count));
                    UpdateStatusMenuItem();
                }
                else
                {
                    Log("Warning: mapping.json not found at " + jsonPath);
                }
            }
            catch (Exception ex) { Log("JSON Load Error: " + ex.Message); }
        }

        // -------------------------------------------------------------------
        //  Raw Input device registration
        // -------------------------------------------------------------------

        private void RegisterDevices()
        {
            // Register keyboard + joystick + gamepad + multi-axis, with device-change notifications
            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[4];
            uint flags = (uint)(RIDEV_INPUTSINK | RIDEV_DEVNOTIFY);
            rid[0].usUsagePage = 0x01; rid[0].usUsage = 0x06; rid[0].dwFlags = flags; rid[0].hwndTarget = this.Handle; // Keyboard
            rid[1].usUsagePage = 0x01; rid[1].usUsage = 0x04; rid[1].dwFlags = flags; rid[1].hwndTarget = this.Handle; // Joystick
            rid[2].usUsagePage = 0x01; rid[2].usUsage = 0x05; rid[2].dwFlags = flags; rid[2].hwndTarget = this.Handle; // Gamepad
            rid[3].usUsagePage = 0x01; rid[3].usUsage = 0x08; rid[3].dwFlags = flags; rid[3].hwndTarget = this.Handle; // Multi-axis

            if (!RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            {
                Log("Failed to register raw input devices.");
            }
        }

        private string GetDeviceName(IntPtr hDevice)
        {
            uint pcbSize = 0;
            GetRawInputDeviceInfo(hDevice, RID_DEVICENAME, IntPtr.Zero, ref pcbSize);
            if (pcbSize <= 0) return "";

            IntPtr pData = Marshal.AllocHGlobal((int)pcbSize * 2);
            try
            {
                GetRawInputDeviceInfo(hDevice, RID_DEVICENAME, pData, ref pcbSize);
                return Marshal.PtrToStringUni(pData);
            }
            finally { Marshal.FreeHGlobal(pData); }
        }

        private string ResolveJsInstance(IntPtr hDevice)
        {
            if (_deviceToJsMap.ContainsKey(hDevice)) return _deviceToJsMap[hDevice];

            string devicePath = GetDeviceName(hDevice).ToUpper();
            
            foreach (var mapping in _mappings)
            {
                if (mapping.Key.StartsWith("__device_js"))
                {
                    string jsInstance = mapping.Key.Replace("__device_", ""); // e.g. js3
                    string productInfo = mapping.Value.ToUpper();
                    
                    var match = Regex.Match(productInfo, @"\{([0-9A-F]{4})([0-9A-F]{4})-[0-9A-F]{4}-");
                    if (match.Success)
                    {
                        string searchStr = string.Format("VID_{0}&PID_{1}", match.Groups[2].Value, match.Groups[1].Value);
                        if (devicePath.Contains(searchStr))
                        {
                            _deviceToJsMap[hDevice] = jsInstance;
                            Log(string.Format("Matched {0} to {1} (Path: {2})", jsInstance, productInfo, devicePath));
                            return jsInstance;
                        }
                    }
                }
            }
            return null;
        }

        // -------------------------------------------------------------------
        //  WndProc / Raw Input processing
        // -------------------------------------------------------------------

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                ProcessRawInput(m.LParam);
            }
            else if (m.Msg == WM_INPUT_DEVICE_CHANGE)
            {
                if (m.WParam.ToInt32() == 2) // GIDC_REMOVAL
                {
                    // Device disconnected — evict its cached entries so stale handles don't linger
                    IntPtr hDevice = m.LParam;
                    IntPtr pPreparsed;
                    if (_preparsedDataCache.TryGetValue(hDevice, out pPreparsed))
                    {
                        Marshal.FreeHGlobal(pPreparsed);
                        _preparsedDataCache.Remove(hDevice);
                    }
                    _deviceToJsMap.Remove(hDevice);
                    Log(string.Format("Device disconnected/changed: 0x{0:X}", hDevice.ToInt64()));
                }
            }
            base.WndProc(ref m);
        }

        private const int HIDP_STATUS_SUCCESS = 0x110000;

        private void ProcessRawInput(IntPtr hRawInput)
        {
            try
            {
                uint dwSize = 0;
                GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                if (dwSize == 0) return;

                IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
                try
                {
                    if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == dwSize)
                    {
                        RAWINPUT raw = (RAWINPUT)Marshal.PtrToStructure(buffer, typeof(RAWINPUT));
                        List<string> triggeredActions = new List<string>();

                        if (raw.header.dwType == RIM_TYPEKEYBOARD)
                        {
                            bool isKeyDown = (raw.keyboard.Flags & 1) == 0;
                            bool isE0 = (raw.keyboard.Flags & 2) == 2;
                            Keys vKey = (Keys)raw.keyboard.VKey;
                            string scKeyName = GetStarCitizenKeyName(vKey, isE0);
                            string currentKey = "keyboard_" + scKeyName;

                            if (isKeyDown)
                            {
                                if (!_activeButtons.ContainsKey(currentKey) || !_activeButtons[currentKey])
                                {
                                    _activeButtons[currentKey] = true;
                                    if (_mappings.ContainsKey(currentKey))
                                    {
                                        triggeredActions.Add(_mappings[currentKey]);
                                    }
                                    else if (currentKey.Length > 10)
                                    {
                                        if (_cfgShowUnmappedInputs)
                                        {
                                            triggeredActions.Add("Keyboard " + currentKey.Replace("keyboard_", "").ToUpper());
                                        }
                                        Log(string.Format("DEBUG: Lookup failed for [{0}]", currentKey));
                                    }
                                }
                            }
                            else
                            {
                                _activeButtons[currentKey] = false;
                            }
                        }
                        else if (raw.header.dwType == RIM_TYPEHID)
                        {
                            string js = ResolveJsInstance(raw.header.hDevice);
                            if (js != null)
                            {
                                List<int> pressedButtons = new List<int>();
                                try
                                {
                                    // Use cached preparsed data — avoids AllocHGlobal on every HID event
                                    IntPtr pPreparsedData;
                                    if (!_preparsedDataCache.TryGetValue(raw.header.hDevice, out pPreparsedData))
                                    {
                                        uint pcbSize = 0;
                                        GetRawInputDeviceInfo(raw.header.hDevice, RID_PREPARSEDDATA, IntPtr.Zero, ref pcbSize);
                                        if (pcbSize > 0)
                                        {
                                            pPreparsedData = Marshal.AllocHGlobal((int)pcbSize);
                                            if (GetRawInputDeviceInfo(raw.header.hDevice, RID_PREPARSEDDATA, pPreparsedData, ref pcbSize) == pcbSize)
                                            {
                                                _preparsedDataCache[raw.header.hDevice] = pPreparsedData;
                                            }
                                            else
                                            {
                                                Marshal.FreeHGlobal(pPreparsedData);
                                                pPreparsedData = IntPtr.Zero;
                                            }
                                        }
                                        else { pPreparsedData = IntPtr.Zero; }
                                    }

                                    if (pPreparsedData != IntPtr.Zero)
                                    {
                                        HIDP_CAPS caps;
                                        int capsStatus = HidP_GetCaps(pPreparsedData, out caps);
                                        if (capsStatus == HIDP_STATUS_SUCCESS)
                                        {
                                            ushort[] usages = new ushort[256];
                                            uint numUsages = 256;

                                            int hidOffset = Marshal.OffsetOf(typeof(RAWINPUT), "hid").ToInt32() + Marshal.OffsetOf(typeof(RAWHID), "bRawData").ToInt32();
                                            IntPtr pReport = new IntPtr(buffer.ToInt64() + hidOffset);
                                            uint reportLength = raw.hid.dwSizeHid;

                                            int usagesStatus = HidP_GetUsages(0, 0x09, 0, usages, ref numUsages, pPreparsedData, pReport, reportLength);
                                            if (usagesStatus == HIDP_STATUS_SUCCESS)
                                            {
                                                for (int i = 0; i < numUsages; i++)
                                                    pressedButtons.Add(usages[i]);
                                            }
                                            else if (usagesStatus != -1072627702) // HIDP_STATUS_INCOMPATIBLE_REPORT_ID
                                            {
                                                Log("HID Parsing Error (HidP_GetUsages): " + usagesStatus);
                                            }
                                            
                                            // Process Analog Axes (UsagePage 0x01)
                                            if (caps.NumberInputValueCaps > 0)
                                            {
                                                ushort numValueCaps = caps.NumberInputValueCaps;
                                                HIDP_VALUE_CAPS[] valueCaps = new HIDP_VALUE_CAPS[numValueCaps];
                                                if (HidP_GetValueCaps(0, valueCaps, ref numValueCaps, pPreparsedData) == HIDP_STATUS_SUCCESS)
                                                {
                                                    for (int i = 0; i < numValueCaps; i++)
                                                    {
                                                        var vc = valueCaps[i];
                                                        if (vc.UsagePage == 0x01 && !vc.IsRange)
                                                        {
                                                            uint currentValue;
                                                            if (HidP_GetUsageValue(0, vc.UsagePage, 0, vc.Usage, out currentValue, pPreparsedData, pReport, reportLength) == HIDP_STATUS_SUCCESS)
                                                            {
                                                                string axisSuffix = null;
                                                                switch (vc.Usage)
                                                                {
                                                                    case 0x30: axisSuffix = "x"; break;
                                                                    case 0x31: axisSuffix = "y"; break;
                                                                    case 0x32: axisSuffix = "z"; break;
                                                                    case 0x33: axisSuffix = "rotx"; break; // rx
                                                                    case 0x34: axisSuffix = "roty"; break; // ry
                                                                    case 0x35: axisSuffix = "rotz"; break; // rz
                                                                    case 0x36: axisSuffix = "slider"; break;
                                                                }

                                                                if (axisSuffix != null)
                                                                {
                                                                    string axisKey = string.Format("joystick_{0}_{1}", js, axisSuffix);
                                                                    
                                                                    // Determine delta & deadzone
                                                                    long range = (long)vc.LogicalMax - (long)vc.LogicalMin;
                                                                    if (range <= 0) range = 65535; // safe fallback
                                                                    
                                                                    uint lastVal = 0;
                                                                    if (_lastAxisValues.ContainsKey(axisKey)) lastVal = _lastAxisValues[axisKey];
                                                                    
                                                                    long delta = Math.Abs((long)currentValue - (long)lastVal);
                                                                    bool significantChange = delta > (range * 0.05); // 5% delta required
                                                                    
                                                                    long center = ((long)vc.LogicalMax + (long)vc.LogicalMin) / 2;
                                                                    long distFromCenter = Math.Abs((long)currentValue - center);
                                                                    bool outsideDeadzone = distFromCenter > (range * 0.10); // 10% deadzone

                                                                    if (significantChange && outsideDeadzone)
                                                                    {
                                                                        _lastAxisValues[axisKey] = currentValue;
                                                                        
                                                                        // Check debounce (1.5 seconds)
                                                                        DateTime now = DateTime.UtcNow;
                                                                        bool canTrigger = true;
                                                                        if (_axisLastTrigger.ContainsKey(axisKey))
                                                                        {
                                                                            if ((now - _axisLastTrigger[axisKey]).TotalSeconds < 1.5)
                                                                            {
                                                                                canTrigger = false;
                                                                            }
                                                                        }
                                                                        
                                                                        if (canTrigger)
                                                                        {
                                                                            _axisLastTrigger[axisKey] = now;
                                                                            if (_mappings.ContainsKey(axisKey))
                                                                            {
                                                                                Log(string.Format("DEBUG HID Axis: {0} Triggered! Value: {1}", axisKey, currentValue));
                                                                                triggeredActions.Add(_mappings[axisKey]);
                                                                            }
                                                                            else if (_cfgShowUnmappedInputs)
                                                                            {
                                                                                string[] parts = axisKey.Split('_');
                                                                                if (parts.Length >= 3) triggeredActions.Add(parts[1].ToUpper() + " " + parts[2].ToUpper());
                                                                                else triggeredActions.Add(axisKey);
                                                                            }
                                                                        }
                                                                    }
                                                                    else if (!outsideDeadzone)
                                                                    {
                                                                        // Reset if it returns to center
                                                                        _lastAxisValues[axisKey] = (uint)center;
                                                                        if (_axisLastTrigger.ContainsKey(axisKey)) _axisLastTrigger.Remove(axisKey);
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    Log("HID Parsing Error (HidP_GetValueCaps failed)");
                                                }
                                            }
                                        }
                                        else { Log("HID Parsing Error (HidP_GetCaps): " + capsStatus); }
                                    }
                                }
                                catch (Exception hidEx)
                                {
                                    Log("HID Processing Exception: " + hidEx.Message);
                                }
                                
                                List<string> keysToClear = new List<string>();
                                foreach (var kvp in _activeButtons)
                                {
                                    if (kvp.Key.StartsWith("joystick_" + js + "_button") && kvp.Value)
                                    {
                                        int bIdx;
                                        if (int.TryParse(kvp.Key.Replace("joystick_" + js + "_button", ""), out bIdx))
                                        {
                                            if (!pressedButtons.Contains(bIdx))
                                            {
                                                keysToClear.Add(kvp.Key);
                                            }
                                        }
                                    }
                                }
                                foreach (var k in keysToClear) _activeButtons[k] = false;

                                foreach (int buttonIdx in pressedButtons)
                                {
                                    string jsKey = string.Format("joystick_{0}_button{1}", js, buttonIdx);
                                    Log(string.Format("DEBUG HID: Device {0}, Button {1}, Constructed Key: [{2}], Found in Map: {3}", js, buttonIdx, jsKey, _mappings.ContainsKey(jsKey)));
                                    
                                    if (!_activeButtons.ContainsKey(jsKey) || !_activeButtons[jsKey])
                                    {
                                        _activeButtons[jsKey] = true;
                                        if (_mappings.ContainsKey(jsKey))
                                        {
                                            triggeredActions.Add(_mappings[jsKey]);
                                        }
                                        else if (_cfgShowUnmappedInputs)
                                        {
                                            string[] parts = jsKey.Split('_');
                                            if (parts.Length >= 3) triggeredActions.Add(parts[1].ToUpper() + " " + parts[2].ToUpper());
                                            else triggeredActions.Add(jsKey);
                                        }
                                    }
                                }
                            }
                        }

                        if (triggeredActions.Count > 0)
                        {
                            ShowText(string.Join(" | ", triggeredActions));
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch (Exception ex) { Log("Input error: " + ex.Message); }
        }

        // -------------------------------------------------------------------
        //  Key name mapping
        // -------------------------------------------------------------------

        private string GetStarCitizenKeyName(Keys key, bool isE0)
        {
            switch (key)
            {
                case Keys.PageUp: return "pgup"; case Keys.PageDown: return "pgdn";
                case Keys.Back: return "backspace"; case Keys.Delete: return "delete";
                case Keys.Insert: return "insert"; case Keys.Home: return "home";
                case Keys.End: return "end"; case Keys.Space: return "space";
                case Keys.Return: return isE0 ? "np_enter" : "enter";
                case Keys.Multiply: return "np_multiply";
                case Keys.OemQuotes: return "apostrophe";
                case Keys.NumPad0: return "np_0"; case Keys.NumPad1: return "np_1";
                case Keys.NumPad2: return "np_2"; case Keys.NumPad3: return "np_3";
                case Keys.NumPad4: return "np_4"; case Keys.NumPad5: return "np_5";
                case Keys.NumPad6: return "np_6"; case Keys.NumPad7: return "np_7";
                case Keys.NumPad8: return "np_8"; case Keys.NumPad9: return "np_9";
                case Keys.Add: return "np_add"; case Keys.Subtract: return "np_subtract";
                case Keys.Divide: return "np_divide"; case Keys.Decimal: return "np_period";
                case Keys.LShiftKey: return "lshift"; case Keys.RShiftKey: return "rshift";
                case Keys.LControlKey: return "lctrl"; case Keys.RControlKey: return "rctrl";
                case Keys.LMenu: return "lalt"; case Keys.RMenu: return "ralt";
                case Keys.F1: return "f1"; case Keys.F2: return "f2";
                case Keys.F3: return "f3"; case Keys.F4: return "f4";
                case Keys.F5: return "f5"; case Keys.F6: return "f6";
                case Keys.F7: return "f7"; case Keys.F8: return "f8";
                case Keys.F9: return "f9"; case Keys.F10: return "f10";
                case Keys.F11: return "f11"; case Keys.F12: return "f12";
                case Keys.ShiftKey: return isE0 ? "rshift" : "lshift";
                case Keys.ControlKey: return isE0 ? "rctrl" : "lctrl";
                case Keys.Menu: return isE0 ? "ralt" : "lalt";
                default: return key.ToString().ToLower();
            }
        }

        // -------------------------------------------------------------------
        //  Display
        // -------------------------------------------------------------------

        private void ShowText(string text)
        {
            _currentText = text;
            this.Invalidate();
            _clearTimer.Stop(); _clearTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentText))
            {
                SizeF size = e.Graphics.MeasureString(_currentText, _displayFont);
                float x, y;

                if (_cfgPosition == "center")
                {
                    x = (this.Width - size.Width) / 2;
                    y = (this.Height - size.Height) / 2;
                }
                else // "bottom-right" (default)
                {
                    x = this.Width - size.Width - 50;
                    y = this.Height - size.Height - 50;
                }

                // Shadow
                e.Graphics.DrawString(_currentText, _displayFont, _shadowBrush, x + 2, y + 2);
                // Text
                e.Graphics.DrawString(_currentText, _displayFont, _textBrush, x, y);
            }
        }

        // -------------------------------------------------------------------
        //  Logging
        // -------------------------------------------------------------------

        private void Log(string message) 
        { 
            _logQueue.Enqueue(DateTime.Now + ": " + message + "\r\n"); 
        }

        private void LogFlushCallback(object state)
        {
            FlushLogQueue();
        }

        private void FlushLogQueue()
        {
            if (_logQueue.IsEmpty) return;

            try 
            {
                StringBuilder sb = new StringBuilder();
                string msg;
                while (_logQueue.TryDequeue(out msg))
                {
                    sb.Append(msg);
                }

                if (sb.Length > 0)
                {
                    if (File.Exists(_logPath))
                    {
                        long length = new FileInfo(_logPath).Length;
                        if (length > MAX_LOG_SIZE)
                        {
                            string bakPath = _logPath + ".bak";
                            if (File.Exists(bakPath)) File.Delete(bakPath);
                            File.Move(_logPath, bakPath);
                        }
                    }
                    File.AppendAllText(_logPath, sb.ToString());
                }
            } 
            catch { } 
        }

        [STAThread] public static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new OverlayWindow()); }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWINPUTHEADER
    {
        [FieldOffset(0)] public uint dwType;
        [FieldOffset(4)] public uint dwSize;
        [FieldOffset(8)] public IntPtr hDevice;
        [FieldOffset(16)] public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWINPUT
    {
        [FieldOffset(0)] public RAWINPUTHEADER header;
        [FieldOffset(24)] public RAWKEYBOARD keyboard;
        [FieldOffset(24)] public RAWMOUSE mouse;
        [FieldOffset(24)] public RAWHID hid;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWKEYBOARD
    {
        [FieldOffset(0)] public ushort MakeCode;
        [FieldOffset(2)] public ushort Flags;
        [FieldOffset(4)] public ushort Reserved;
        [FieldOffset(6)] public ushort VKey;
        [FieldOffset(8)] public uint Message;
        [FieldOffset(12)] public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort usFlags;
        [FieldOffset(4)] public uint ulButtons;
        [FieldOffset(8)] public uint ulRawButtons;
        [FieldOffset(12)] public int lLastX;
        [FieldOffset(16)] public int lLastY;
        [FieldOffset(20)] public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWHID
    {
        [FieldOffset(0)] public uint dwSizeHid;
        [FieldOffset(4)] public uint dwCount;
        [FieldOffset(8)] public byte bRawData;
    }

}