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
        private string _logPath = "overlay.log";
        private NotifyIcon _trayIcon;
        private Timer _topMostTimer;

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
        private const int RID_INPUT = 0x10000003;
        private const int RID_DEVICENAME = 0x20000007;
        private const int RID_PREPARSEDDATA = 0x20000005;
        private const int RIM_TYPEKEYBOARD = 1;
        private const int RIM_TYPEHID = 2;
        private const int RIDEV_INPUTSINK = 0x00000100;

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

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetUsages(int ReportType, ushort UsagePage, ushort LinkCollection, [In, Out] ushort[] UsageList, ref uint UsageLength, IntPtr PreparsedData, IntPtr Report, uint ReportLength);

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
            this.BackColor = Color.Black;
            this.Opacity = 0.2;
            this.StartPosition = FormStartPosition.Manual;

            // TransparencyKey is removed for this debug phase to ensure BackColor renders
            this.TransparencyKey = Color.Empty;

            _topMostTimer = new Timer();
            _topMostTimer.Interval = 1000;
            _topMostTimer.Tick += (s, e) => { 
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); 
            };
            _topMostTimer.Start();

            _clearTimer = new Timer();
            _clearTimer.Interval = 2000;
            _clearTimer.Tick += (s, e) => {
                _currentText = "";
                this.Invalidate();
                _clearTimer.Stop();
            };

            SetupTrayIcon();
            LoadMappings();
            RegisterDevices();
            Log("Overlay initialized (64-bit alignment fix + Click-through).");
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

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.Text = "SC Key Overlay";
            _trayIcon.Visible = true;

            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("Exit", (s, e) => {
                _trayIcon.Visible = false;
                Application.Exit();
            });
            _trayIcon.ContextMenu = menu;
        }

        private void LoadMappings()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "parser", "mapping.json");
                if (File.Exists(jsonPath))
                {
                    string json = File.ReadAllText(jsonPath);
                    var serializer = new JavaScriptSerializer();
                    _mappings = serializer.Deserialize<Dictionary<string, string>>(json);
                    Log(string.Format("DEBUG: Successfully loaded {0} bindings from mapping.json.", _mappings.Count));
                }
            }
            catch (Exception ex) { Log("Error loading mappings: " + ex.Message); }
        }

        private void RegisterDevices()
        {
            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[2];
            rid[0].usUsagePage = 0x01; rid[0].usUsage = 0x06; rid[0].dwFlags = RIDEV_INPUTSINK; rid[0].hwndTarget = this.Handle;
            rid[1].usUsagePage = 0x01; rid[1].usUsage = 0x04; rid[1].dwFlags = RIDEV_INPUTSINK; rid[1].hwndTarget = this.Handle;

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
                    
                    var match = Regex.Match(productInfo, @"\{([0-9A-F]{4})([0-9A-F]{4})-");
                    if (match.Success)
                    {
                        string pid = match.Groups[1].Value;
                        string vid = match.Groups[2].Value;
                        
                        if (devicePath.Contains(string.Format("VID_{0}&PID_{1}", vid, pid)) ||
                            devicePath.Contains(string.Format("VID_{0}&PID_{1}", pid, vid)) ||
                            devicePath.Contains(string.Format("VID_{1}&PID_{0}", pid, vid)))
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

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) ProcessRawInput(m.LParam);
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
                        string key = "";

                        if (raw.header.dwType == RIM_TYPEKEYBOARD)
                        {
                            bool isKeyDown = (raw.keyboard.Flags & 1) == 0;
                            Keys vKey = (Keys)raw.keyboard.VKey;
                            string scKeyName = GetStarCitizenKeyName(vKey);
                            string currentKey = "keyboard_" + scKeyName;

                            if (isKeyDown)
                            {
                                if (!_activeButtons.ContainsKey(currentKey) || !_activeButtons[currentKey])
                                {
                                    _activeButtons[currentKey] = true;
                                    key = currentKey;
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
                                uint pcbSize = 0;
                                GetRawInputDeviceInfo(raw.header.hDevice, RID_PREPARSEDDATA, IntPtr.Zero, ref pcbSize);

                                if (pcbSize > 0)
                                {
                                    IntPtr pPreparsedData = Marshal.AllocHGlobal((int)pcbSize);
                                    try
                                    {
                                        if (GetRawInputDeviceInfo(raw.header.hDevice, RID_PREPARSEDDATA, pPreparsedData, ref pcbSize) == pcbSize)
                                        {
                                            HIDP_CAPS caps;
                                            if (HidP_GetCaps(pPreparsedData, out caps) == HIDP_STATUS_SUCCESS)
                                            {
                                                ushort[] usages = new ushort[128];
                                                uint numUsages = 128;
                                                IntPtr pReport = new IntPtr(buffer.ToInt64() + 32);
                                                uint reportLength = dwSize - 32;

                                                if (HidP_GetUsages(0, 0x09, 0, usages, ref numUsages, pPreparsedData, pReport, reportLength) == HIDP_STATUS_SUCCESS)
                                                {
                                                    for (int i = 0; i < numUsages; i++)
                                                    {
                                                        pressedButtons.Add(usages[i]);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        Marshal.FreeHGlobal(pPreparsedData);
                                    }
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
                                    if (!_activeButtons.ContainsKey(jsKey) || !_activeButtons[jsKey])
                                    {
                                        _activeButtons[jsKey] = true;
                                        key = jsKey;
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(key))
                        {
                            if (_mappings.ContainsKey(key))
                            {
                                ShowText(_mappings[key]);
                            }
                            else if (raw.header.dwType == RIM_TYPEKEYBOARD)
                            {
                                if (key.Length > 10)
                                {
                                    Log(string.Format("DEBUG: Lookup failed for [{0}]", key));
                                }
                            }
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch (Exception ex) { Log("Input error: " + ex.Message); }
        }

        private string GetStarCitizenKeyName(Keys key)
        {
            switch (key)
            {
                case Keys.PageUp: return "pgup"; case Keys.PageDown: return "pgdn";
                case Keys.Back: return "backspace"; case Keys.Delete: return "delete";
                case Keys.Insert: return "insert"; case Keys.Home: return "home";
                case Keys.End: return "end"; case Keys.Space: return "space";
                case Keys.Return: return "enter";
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
                case Keys.ShiftKey: return "lshift"; // fallback
                case Keys.ControlKey: return "lctrl"; // fallback
                case Keys.Menu: return "lalt"; // fallback
                default: return key.ToString().ToLower();
            }
        }

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
                using (Font font = new Font("Arial", 28, FontStyle.Bold))
                {
                    SizeF size = e.Graphics.MeasureString(_currentText, font);
                    float x = this.Width - size.Width - 50;
                    float y = this.Height - size.Height - 50;
                    e.Graphics.DrawString(_currentText, font, Brushes.Black, x + 2, y + 2);
                    e.Graphics.DrawString(_currentText, font, Brushes.LimeGreen, x, y);
                }
            }
        }

        private void Log(string message) { try { File.AppendAllText(_logPath, DateTime.Now + ": " + message + "\r\n"); } catch { } }

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