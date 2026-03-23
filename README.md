# Star Citizen Overlay (SCOverlay)

**SCOverlay** is a lightweight, external, and transparent overlay for [Star Citizen](https://robertsspaceindustries.com/). It displays the names of in-game actions (e.g., "Landing Gear", "Quantum Drive") on the screen whenever you press the assigned keys on your keyboard, joystick, or gamepad.

**The main feature of this project is its strict adherence to safety (Anti-Cheat Safe).** The application operates entirely at the OS level: it does not inject DLLs into the game process and avoids any WinAPI/DirectX function hooking, completely eliminating the risk of Easy Anti-Cheat (EAC) bans.

## 🚀 Key Features

- **EAC Safe:** Zero injection or hooking. Fully relies on native Windows Raw Input API.
- **Universal Device Support:** Works seamlessly with keyboards and HID devices (Joysticks, HOSAS, HOTAS, Gamepads) via robust `hid.dll` native parsing.
- **Unintrusive UI:** Displays text unobtrusively in the bottom-right corner with a transparent, Top-Most window that never steals game focus.
- **Jitter-Free Input:** Advanced state tracking ensures your overlay won't flash or spam text due to analog axis noise from your HOTAS/HOSAS.
- **Automated Mapping:** A Python script automatically extracts and maps your personal bindings directly from the game's `actionmaps.xml`.
- **Debug Mode:** Optional logging for troubleshooting unmapped buttons, with spam-reduction to keep I/O operations lightweight.

- 
![SCR1](https://github.com/user-attachments/assets/5c0b73bd-4458-4f37-b301-88f022043b2f)

## 📁 Project Structure

The project is divided into two standalone components:

- `parser/` — A Python script (`parse_actionmaps.py`) that parses your Star Citizen control profile (`actionmaps.xml`) and generates a universal `mapping.json` dictionary.
- `overlay/` — A C# (.NET/WinForms) desktop application (`OverlayApp.exe`) that runs in the background, asynchronously reads your physical inputs, and draws the corresponding action names on-screen.

## 🛠 Requirements

- **OS:** Windows 10 / 11
- **Parser:** Python 3.x
- **Overlay:** .NET Framework 4.0 or higher (pre-installed on modern Windows)

## 📖 Setup and Usage Instructions

### Step 1: Prepare the Mapping (Parser)
First, you need to generate the `mapping.json` file, which links physical hardware buttons to the in-game actions from your personal profile.

1. Open a terminal or command prompt and navigate to the parser directory:
   ```bash
   cd parser
   ```
2. Open `parse_actionmaps.py` in any text editor and ensure the path variable (e.g., `REAL_XML_PATH`) correctly points to your Star Citizen profile file (usually located at `USER/Client/0/Profiles/default/actionmaps.xml`).
3. Run the parser:
   ```bash
   python parse_actionmaps.py
   ```
   A `mapping.json` file will be successfully generated in the `parser` directory.

### Step 2: Run the Overlay
1. Navigate to the overlay directory:
   ```bash
   cd ../overlay
   ```
2. Launch `OverlayApp.exe`.
   - The overlay will automatically load `mapping.json`.
   - The application runs hidden in a "Top-Most" state without activating or stealing focus. You can find its icon in the System Tray to exit the app (Right Click -> Exit).

## 🐛 Troubleshooting

- **Overlay Visualization:** By default, the window may have a slight opacity to help you confirm it is rendering over the game.
- **Logging (`overlay.log`):** The application records input events into `overlay.log` inside the `overlay/` folder.
  - Standard keys that aren't mapped are ignored to prevent log flooding.
  - If a specific joystick button doesn't trigger the text, check the log to see its registered hardware ID and ensure it matches the generated Python JSON.

## 👨‍💻 Compiling from Source

The project uses the default C# compiler (`csc.exe`) bundled with Windows. You do not need Visual Studio to build it.

To compile the executable from source, run this in PowerShell:
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /out:"OverlayApp.exe" "OverlayApp.cs" /reference:System.Windows.Forms.dll,System.Drawing.dll,System.Web.Extensions.dll
```

---
*Disclaimer: This project is not affiliated with Cloud Imperium Games (CIG). Star Citizen is a registered trademark of Cloud Imperium Rights LLC and Cloud Imperium Rights Ltd.*
