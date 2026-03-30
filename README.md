# Star Citizen Overlay (SCOverlay)

**SCOverlay** is a lightweight, external, and transparent overlay for [Star Citizen](https://robertsspaceindustries.com/). It displays the names of in-game actions (e.g., "Landing Gear", "Quantum Drive") when you press the mapped buttons on your keyboard, joystick, or gamepad.

**The main feature of this project is its strict Anti-Cheat Safety.** The program operates entirely at the OS-level: it does not inject DLLs into the game process and does not hook DirectX or WinAPI functions, entirely eliminating the risk of Easy Anti-Cheat (EAC) bans.





![scr1](https://github.com/user-attachments/assets/0747fbcb-8f33-45ac-a294-99429cdd5653)






## 🚀 Key Features

*   **100% EAC Safe:** No memory injection or hooking. Uses the native Windows Raw Input API.
*   **Universal Device Support:** Fully supports keyboards, mice, and HID devices (joysticks, gamepads, HOTAS/HOSAS setups).
*   **Analog Axes Supported:** Reads both digital buttons and analog axes. Advanced deadzone and anti-spam debouncing algorithms prevent UI flickering during throttle or stick movements.
*   **Non-intrusive UI:** The transparent window stays "always on top". Text appears smoothly upon keypress and vanishes automatically.
*   **Automatic Parsing:** A Python script automatically extracts your personal bindings directly from the game's `actionmaps.xml` (supports LIVE, PTU, EPTU, and TECH-PREVIEW channels).
*   **Custom Profiles:** Ability to manually load exported `.xml` control profiles from Star Citizen.
*   **Unmapped Inputs Mode:** Optional debug UI setting to display buttons that are pressed but have no registered binding.
*   **Hot-Reloading & Live Config:** Features a `config.json` configuration file to customize font, size, color, duration, and display modes. Both the config and key mappings hot-reload dynamically without requiring an app restart.
*   **Performance Optimized:** Uses native GDI caching, asynchronous threaded file logging, and debounced file system watchers for minimal performance impact.

## 📁 Project Structure

The project is split into two independent components:

*   `parser/`: A Python script (`parse_actionmaps.py`) that parses your Star Citizen control profile (`actionmaps.xml`) and generates a universal `mapping.json` dictionary. It can also generate an HTML Cheat Sheet.
*   `overlay/`: A C# background application (.NET/WinForms) (`OverlayApp.exe`) that listens to raw user input asynchronously and renders the text on screen.

## 🛠 Requirements

*   **OS:** Windows 10 / 11
*   **Parser:** Python 3.x
*   **Overlay:** .NET Framework 4.0 or higher (pre-installed on modern Windows versions)

## 📖 Installation and Usage

### Step 1: Prepare the Mapping (Parser)
First, you need to create the `mapping.json` file that links physical buttons to in-game actions according to your game profile.

1. Open your terminal (or Command Prompt) and navigate to the parser directory:
   ```bash
   cd SCOverlay/parser
   ```
2. Run the parser script. It will automatically attempt to find your `actionmaps.xml` from the registry across all game channels (LIVE, PTU, EPTU, etc.).
   ```bash
   python parse_actionmaps.py
   ```
   **Optional:**
   * `--watch`: Automatically re-parse whenever you change bindings in-game.
   * `--export-html`: Generate a secure, beautiful HTML cheat-sheet inside the directory.
   * `--profile "C:\path\to\layout.xml"`: Point the parser at an exported Star Citizen control profile instead of the default user folder.
3. The `mapping.json` file will be created in the `parser` folder.

### Step 2: Configure & Launch the Overlay
1. Open `config.json` inside the `parser` directory to tweak visual settings:
   ```json
   {
       "font_name": "Arial",
       "font_size": 28,
       "position": "bottom-right",
       "display_duration_ms": 2000,
       "text_color": "39FF14",
       "show_unmapped_inputs": false 
   }
   ```
   *Tip:* Set `"show_unmapped_inputs": true` if you want the overlay to display raw button presses (e.g. "Keyboard W", "JS1 BUTTON5") even if they lack an in-game binding.
2. Navigate to the overlay directory:
   ```bash
   cd ../overlay
   ```
3. Launch `OverlayApp.exe`.
   * The overlay will automatically load `mapping.json` and `config.json`.
   * The app runs in hidden mode "always on top". You can interact with it via its System Tray icon (Right Click -> Status / Reload / View Log / Exit).

## 🐛 Troubleshooting & Debugging

*   **Overlay Visibility:** By default, the window uses chroma-key transparency to stay completely invisible. If you don't see anything, check if you are playing in Borderless Windowed or Windowed mode. Exclusive Fullscreen might hide the overlay.
*   **Logs (`overlay.log`):** The app records captured inputs to an asynchronous log queue. If an action isn't displayed:
    * *Success example:* `DEBUG: RawInput received. WinForms Key: OemQuotes, Converted String: keyboard_apostrophe`
    * *Error example:* `DEBUG: Lookup failed for [keyboard_apostrophe]` (Enable `show_unmapped_inputs` or ensure the parser mapped it).
*   **Missing Device Axes/Keys:** Ensure your device is correctly recognized by looking at the logs. Device disconnections and hot-plugging are handled safely without leaving lingering memory blocks.

## 👨‍💻 Building from Source

The project relies on the basic C# compiler (`csc.exe`) bundled with Windows. You don't need Visual Studio to build it.

To compile the `.exe` file, run this inside PowerShell:
```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:"OverlayApp.exe" "OverlayApp.cs"
```

---
*Disclaimer: This project is not affiliated with Cloud Imperium Games (CIG). Star Citizen is a registered trademark of Cloud Imperium Rights LLC and Cloud Imperium Rights Ltd.*
