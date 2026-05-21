import os
import platform
import glob

def get_save_games_path():
    if platform.system() != "Windows":
        # For testing/dev on Linux/Mac if needed, but primarily Windows
        return None
    
    local_app_data = os.getenv("LOCALAPPDATA")
    if not local_app_data:
        return None
    
    base_path = os.path.join(local_app_data, "FactoryGame", "Saved", "SaveGames")
    
    if not os.path.exists(base_path):
        return None
    
    # Satisfactory stores saves in subfolders named by SteamID or EpicID
    # We look for folders containing .sav files
    potential_folders = [f.path for f in os.scandir(base_path) if f.is_dir()]
    
    for folder in potential_folders:
        if glob.glob(os.path.join(folder, "*.sav")):
            return folder
            
    return None

def get_latest_local_save(save_path):
    if not save_path or not os.path.exists(save_path):
        return None
    
    files = glob.glob(os.path.join(save_path, "*.sav"))
    if not files:
        return None
    
    return max(files, key=os.path.getmtime)
