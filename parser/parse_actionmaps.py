import xml.etree.ElementTree as ET
import json
import logging
import os
import re
import winreg

# Constants
OUTPUT_JSON_PATH = "mapping.json"
LOG_FILE = "parser.log"

# Setup logging
logging.basicConfig(filename=LOG_FILE, level=logging.INFO, 
                    format='%(asctime)s - %(levelname)s - %(message)s')

def get_sc_path():
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Roberts Space Industries\RSI Launcher") as key:
            install_dir, _ = winreg.QueryValueEx(key, "InstallDir")
            path = os.path.join(install_dir, r"StarCitizen\LIVE\USER\Client\0\Profiles\default\actionmaps.xml")
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

def main():
    logging.info("Starting parser...")
    xml_path = get_sc_path()
    logging.info(f"Reading from: {xml_path}")
    print(f"Reading from: {xml_path}")

    bindings = parse_actionmaps(xml_path)

    if bindings:
        try:
            with open(OUTPUT_JSON_PATH, 'w', encoding='utf-8') as f:
                json.dump(bindings, f, indent=4)
            logging.info(f"Successfully wrote {len(bindings)} bindings to {OUTPUT_JSON_PATH}")
            print(f"Successfully wrote {len(bindings)} bindings to {OUTPUT_JSON_PATH}")
        except Exception as e:
            logging.error(f"Failed to write JSON: {e}")
            print(f"Error writing JSON: {e}")
    else:
        logging.warning("No bindings found or error occurred.")
        print("No bindings found or error occurred.")

if __name__ == "__main__":
    main()