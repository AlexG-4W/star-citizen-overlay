import xml.etree.ElementTree as ET
import json
import logging
import os
import re
import sys
import shutil
import tempfile
import time
import argparse
import winreg
import html

# Resolve paths relative to this script's directory, not CWD.
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_JSON_PATH = os.path.join(_SCRIPT_DIR, "mapping.json")
LOG_FILE = os.path.join(_SCRIPT_DIR, "parser.log")
HTML_OUTPUT_PATH = os.path.join(_SCRIPT_DIR, "bindings_cheatsheet.html")

# Setup logging
logging.basicConfig(filename=LOG_FILE, level=logging.INFO,
                    format='%(asctime)s - %(levelname)s - %(message)s')


def get_sc_path():
    """Attempts to find actionmaps.xml via registry, falls back to manual input."""
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Roberts Space Industries\RSI Launcher") as key:
            install_dir, _ = winreg.QueryValueEx(key, "InstallDir")
            channels = ["LIVE", "PTU", "EPTU", "TECH-PREVIEW"]
            for channel in channels:
                path = os.path.join(install_dir, f"StarCitizen\\{channel}\\USER\\Client\\0\\Profiles\\default\\actionmaps.xml")
                if os.path.exists(path):
                    return path
    except Exception as e:
        logging.warning(f"Failed to read registry: {e}")

    # Fallback to manual input
    path = input("Could not automatically detect Star Citizen actionmaps.xml path.\nPlease enter the full path to actionmaps.xml: ").strip()
    return path


def parse_actionmaps(xml_path):
    """Parses actionmaps.xml and returns a dictionary of bindings."""
    if not os.path.exists(xml_path):
        logging.error(f"File not found: {xml_path}")
        print(f"Error: File not found: {xml_path}")
        return {}

    try:
        tree = ET.parse(xml_path)
        root = tree.getroot()
        bindings = {}

        # 1. Map joystick instances (js1, js2...) to product strings/GUIDs
        for options in root.findall(".//options"):
            otype = options.get("type")
            instance = options.get("instance")
            product = options.get("Product")
            if otype == "joystick" and instance and product:
                # Key: __device_js1, Value: Product Name with GUID
                bindings[f"__device_js{instance}"] = product
                logging.info(f"Detected Device: js{instance} -> {product}")

        # 2. Map actions
        for actionmap in root.findall(".//actionmap"):
            actionmap_name = actionmap.get("name")
            for action in actionmap.findall(".//action"):
                action_name = action.get("name")
                for bind in action.findall(".//rebind") + action.findall(".//addbind"):
                    input_val = bind.get("input")
                    if input_val:
                        # Normalize key names
                        key = input_val
                        if key.startswith("kb1_"):
                            key = "keyboard_" + key[4:]
                        elif key.startswith("js"):
                            key = "joystick_" + key
                        elif key.startswith("gp"):
                            key = "gamepad_" + key
                        elif key.startswith("mo"):
                            key = "mouse_" + key

                        suffix = key.split("_", 2)[-1] if "_" in key else key
                        if not suffix.strip():
                            continue

                        if key in bindings:
                            if action_name not in bindings[key].split(" / "):
                                bindings[key] += f" / {action_name}"
                        else:
                            bindings[key] = action_name
                            logging.info(f"Mapped {key} -> {action_name} (Context: {actionmap_name})")

        return bindings

    except ET.ParseError as e:
        logging.error(f"XML Parse Error: {e}")
        print(f"Error parsing XML: {e}")
        return {}
    except Exception as e:
        logging.error(f"Unexpected Error: {e}")
        print(f"Unexpected error: {e}")
        return {}


def write_bindings(bindings):
    """Atomically writes bindings dict to OUTPUT_JSON_PATH (temp-file + rename)."""
    try:
        dir_ = os.path.dirname(os.path.abspath(OUTPUT_JSON_PATH))
        with tempfile.NamedTemporaryFile('w', dir=dir_, delete=False,
                                         suffix='.tmp', encoding='utf-8') as tf:
            json.dump(bindings, tf, indent=4)
            tmp_path = tf.name
        shutil.move(tmp_path, OUTPUT_JSON_PATH)
        logging.info(f"Successfully wrote {len(bindings)} bindings to {OUTPUT_JSON_PATH}")
        print(f"Successfully wrote {len(bindings)} bindings to {OUTPUT_JSON_PATH}")
    except Exception as e:
        logging.error(f"Failed to write JSON: {e}")
        print(f"Error writing JSON: {e}")


def export_html(bindings):
    """Generates a static HTML cheat sheet from bindings."""
    # Separate bindings by device prefix, skip __device_ metadata entries.
    categories = {
        "Keyboard": {},
        "Joystick": {},
        "Gamepad": {},
        "Mouse": {},
        "Other": {},
    }
    for key, action in sorted(bindings.items()):
        if key.startswith("__device_"):
            continue
        if key.startswith("keyboard_"):
            categories["Keyboard"][key] = action
        elif key.startswith("joystick_"):
            categories["Joystick"][key] = action
        elif key.startswith("gamepad_"):
            categories["Gamepad"][key] = action
        elif key.startswith("mouse_"):
            categories["Mouse"][key] = action
        else:
            categories["Other"][key] = action

    rows = []
    for cat_name, cat_bindings in categories.items():
        if not cat_bindings:
            continue
        rows.append(f'<tr class="cat-header"><td colspan="2">{cat_name}</td></tr>')
        for key, action in sorted(cat_bindings.items()):
            rows.append(f'<tr><td class="key">{html.escape(key)}</td><td>{html.escape(action)}</td></tr>')

    total = sum(len(v) for v in categories.values())
    html_content = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Star Citizen Bindings Cheat Sheet</title>
<style>
  body {{ font-family: 'Segoe UI', Arial, sans-serif; background: #1a1a2e; color: #e0e0e0; margin: 0; padding: 20px; }}
  h1 {{ color: #39FF14; text-align: center; margin-bottom: 5px; }}
  .subtitle {{ text-align: center; color: #888; margin-bottom: 25px; font-size: 0.9em; }}
  table {{ width: 100%; max-width: 900px; margin: 0 auto; border-collapse: collapse; }}
  th {{ background: #16213e; color: #39FF14; padding: 10px; text-align: left; font-size: 0.95em; }}
  td {{ padding: 7px 10px; border-bottom: 1px solid #2a2a4a; font-size: 0.9em; }}
  tr:hover td {{ background: #16213e; }}
  .cat-header td {{ background: #0f3460; color: #39FF14; font-weight: bold; font-size: 1em; padding: 10px; }}
  .key {{ font-family: 'Consolas', monospace; color: #00d2ff; }}
</style>
</head>
<body>
<h1>Star Citizen Bindings</h1>
<p class="subtitle">Total bindings: {total} &mdash; Generated by SCOverlay Parser</p>
<table>
<tr><th>Input Key</th><th>Action</th></tr>
{''.join(rows)}
</table>
</body>
</html>"""

    try:
        with open(HTML_OUTPUT_PATH, 'w', encoding='utf-8') as f:
            f.write(html_content)
        print(f"Cheat sheet exported to {HTML_OUTPUT_PATH}")
        logging.info(f"Exported HTML cheat sheet ({total} bindings) to {HTML_OUTPUT_PATH}")
    except Exception as e:
        logging.error(f"Failed to write HTML: {e}")
        print(f"Error writing HTML: {e}")


def run_watch(xml_path, export_html_flag=False):
    """Polls actionmaps.xml and re-parses on change."""
    print(f"Watching {xml_path} for changes (Ctrl+C to stop)...")
    logging.info(f"Watch mode started for: {xml_path}")
    last_mtime = 0
    try:
        while True:
            try:
                mtime = os.path.getmtime(xml_path)
            except OSError:
                time.sleep(2)
                continue
            if mtime != last_mtime:
                if last_mtime != 0:
                    print(f"Change detected, re-parsing...")
                    logging.info("Watch: file change detected, re-parsing.")
                bindings = parse_actionmaps(xml_path)
                if bindings:
                    write_bindings(bindings)
                    if export_html_flag:
                        export_html(bindings)
                last_mtime = mtime
            time.sleep(2)
    except KeyboardInterrupt:
        print("\nWatch mode stopped.")
        logging.info("Watch mode stopped by user.")


def main():
    """Entry point with CLI argument handling."""
    parser = argparse.ArgumentParser(
        description="SCOverlay Parser — extracts Star Citizen keybindings from actionmaps.xml"
    )
    parser.add_argument(
        "--watch", action="store_true",
        help="Watch actionmaps.xml for changes and automatically re-parse."
    )
    parser.add_argument(
        "--export-html", action="store_true",
        help="Generate a static HTML cheat sheet (bindings_cheatsheet.html)."
    )
    parser.add_argument(
        "--profile", type=str,
        help="Path to an exported Star Citizen control profile XML file to parse instead of the standard actionmaps.xml."
    )
    args = parser.parse_args()

    logging.info("Starting parser...")
    if args.profile:
        xml_path = os.path.abspath(args.profile)
    else:
        xml_path = get_sc_path()
    logging.info(f"Reading from: {xml_path}")
    print(f"Reading from: {xml_path}")

    bindings = parse_actionmaps(xml_path)

    if bindings:
        write_bindings(bindings)
        if args.export_html:
            export_html(bindings)
    else:
        logging.warning("No bindings found or error occurred.")
        print("No bindings found or error occurred.")

    if args.watch:
        run_watch(xml_path, args.export_html)


if __name__ == "__main__":
    main()