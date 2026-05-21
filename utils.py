import os
import platform
import glob

def get_save_games_path():
    if platform.system() != "Windows":
        # For testing/dev on Linux/Mac if needed, but primarily Windows
        return []
    
    local_app_data = os.getenv("LOCALAPPDATA")
    if not local_app_data:
        return []
    
    base_path = os.path.join(local_app_data, "FactoryGame", "Saved", "SaveGames")
    
    if not os.path.exists(base_path):
        return []
    
    # Satisfactory stores saves in subfolders named by SteamID or EpicID
    # We look for folders containing .sav files
    potential_folders = [f.path for f in os.scandir(base_path) if f.is_dir()]
    valid_folders = []
    
    for folder in potential_folders:
        if glob.glob(os.path.join(folder, "*.sav")):
            valid_folders.append(folder)
            
    return valid_folders

def get_latest_local_save(save_path):
    if not save_path or not os.path.exists(save_path):
        return None
    
    files = glob.glob(os.path.join(save_path, "*.sav"))
    if not files:
        return None
    
    return max(files, key=os.path.getmtime)

def get_file_hash(filepath):
    import hashlib
    if not filepath or not os.path.exists(filepath):
        return None
    hash_md5 = hashlib.md5()
    with open(filepath, "rb") as f:
        for chunk in iter(lambda: f.read(4096), b""):
            hash_md5.update(chunk)
    return hash_md5.hexdigest()

def get_session_name(filepath):
    import struct
    try:
        with open(filepath, "rb") as f:
            raw = f.read(2048)
        off = 0
        _save_hdr_ver = struct.unpack_from("<i", raw, off)[0]; off += 4
        save_ver = struct.unpack_from("<i", raw, off)[0]; off += 4
        build_ver = struct.unpack_from("<i", raw, off)[0]; off += 4
        
        def _read_fstring(d: bytes, off: int):
            length, off = struct.unpack_from("<i", d, off)[0], off + 4
            if length == 0:
                return "", off
            if length > 0:
                s = d[off:off + length - 1].decode("utf-8", errors="replace")
                return s, off + length
            byte_len = (-length) * 2
            s = d[off:off + byte_len - 2].decode("utf-16-le", errors="replace")
            return s, off + byte_len

        map_name, off = _read_fstring(raw, off)
        _map_opts, off = _read_fstring(raw, off)
        session_name, off = _read_fstring(raw, off)
        return session_name
    except Exception:
        return ""
