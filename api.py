import requests
import os

BASE_URL = "https://api.satisfactory.kaffka.tech"

class APIClient:
    def __init__(self, token=None):
        self.token = token
        self.session = requests.Session()
        if token:
            self.session.headers.update({"Authorization": f"Bearer {token}"})

    def login(self, connect_token):
        response = self.session.post(f"{BASE_URL}/auth/exchange", json={"token": connect_token})
        if response.status_code == 200:
            data = response.json()
            self.token = data["access_token"]
            self.session.headers.update({"Authorization": f"Bearer {self.token}"})
            return True
        return False

    def get_me(self):
        response = self.session.get(f"{BASE_URL}/me/premium")
        if response.status_code == 200:
            return response.json()
        return None

    def get_worlds(self):
        response = self.session.get(f"{BASE_URL}/worlds")
        if response.status_code == 200:
            return response.json()
        return []

    def get_save_metadata(self, world_id):
        response = self.session.get(f"{BASE_URL}/worlds/{world_id}/save/metadata")
        if response.status_code == 200:
            return response.json()
        return None

    def download_save(self, world_id, target_path):
        response = self.session.get(f"{BASE_URL}/worlds/{world_id}/save/latest", stream=True)
        if response.status_code == 200:
            # Get filename from headers or default
            filename = response.headers.get("Content-Disposition", "").split("filename=")[-1].strip('"')
            if not filename:
                filename = f"world_{world_id}_latest.sav"
            
            full_path = os.path.join(target_path, filename)
            with open(full_path, "wb") as f:
                for chunk in response.iter_content(chunk_size=8192):
                    f.write(chunk)
            return full_path
        return None

    def upload_save(self, world_id, file_path):
        with open(file_path, "rb") as f:
            files = {"file": f}
            response = self.session.post(f"{BASE_URL}/worlds/{world_id}/save", files=files)
            if response.status_code == 200:
                return response.json()
        return None

    def get_version(self):
        response = requests.get(f"{BASE_URL}/client/version")
        if response.status_code == 200:
            return response.json()
        return None
