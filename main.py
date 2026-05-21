import flet as ft
import os
import sys
import threading
import time
import pathlib
from api import APIClient
from utils import get_save_games_path, get_latest_local_save, get_file_hash, get_session_name

def _token_path():
    appdata = os.environ.get('APPDATA', os.path.expanduser('~'))
    p = pathlib.Path(appdata) / 'SFTracker'
    p.mkdir(parents=True, exist_ok=True)
    return p / 'token.txt'

def _load_token():
    try:
        return _token_path().read_text().strip() or None
    except Exception:
        return None

def _save_token(token):
    try:
        _token_path().write_text(token)
    except Exception:
        pass

def _clear_token():
    try:
        _token_path().unlink(missing_ok=True)
    except Exception:
        pass

VERSION = "1.1.0"

class SFTApp:
    def __init__(self, page: ft.Page):
        self.page = page
        self.api = APIClient()
        self.save_paths = get_save_games_path()
        self.save_path = self.save_paths[0] if self.save_paths else None
        
        self.local_save_info = None
        self.server_save_info = None
        self.auto_refresh_running = False

        self.init_ui()

    def check_for_updates(self):
        try:
            version_info = self.api.get_version()
            if not version_info:
                return

            remote_version = version_info.get("version")
            force_update = version_info.get("force_update", False)
            download_url = version_info.get("url")

            if remote_version != VERSION or force_update:
                if not download_url or not download_url.startswith("https://"):
                    return

                def close_app(e):
                    self.page.launch_url(download_url)
                    self.page.window_close()

                self.page.dialog = ft.AlertDialog(
                    title=ft.Text("Update Available", color=ft.Colors.BLUE_400),
                    content=ft.Text(f"A new version ({remote_version}) is available. Please update to continue."),
                    actions=[ft.TextButton("Update Now", on_click=close_app)],
                    modal=True
                )
                self.page.dialog.open = True
                self.page.update()
        except Exception:
            pass

    def init_ui(self):
        self.page.title = "Satisfactory Session Tracker"
        self.page.window_width = 500
        self.page.window_height = 750
        self.page.window_resizable = False
        self.page.theme_mode = ft.ThemeMode.DARK
        self.page.padding = 0
        self.page.fonts = {"RobotoMono": "https://github.com/google/fonts/raw/main/apache/robotomono/RobotoMono%5Bwght%5D.ttf"}

        self.check_for_updates()

        saved_token = _load_token()
        if saved_token:
            self.api.token = saved_token
            self.api.session.headers.update({"Authorization": f"Bearer {saved_token}"})
            self.show_main_view()
        else:
            self.show_login_view()

    def show_login_view(self):
        self.page.clean()
        self.page.add(
            ft.Container(
                content=ft.Column([
                    ft.Icon(ft.Icons.FACTORY_ROUNDED, size=80, color=ft.Colors.ORANGE_500),
                    ft.Text("Satisfactory Tracker", size=32, weight=ft.FontWeight.BOLD, text_align=ft.TextAlign.CENTER),
                    ft.Text("Windows Companion App", color=ft.Colors.GREY_400, size=16),
                    ft.Divider(height=60, color=ft.Colors.TRANSPARENT),
                    ft.Text("Please connect your account via Telegram bot", text_align=ft.TextAlign.CENTER),
                    ft.ElevatedButton(
                        "Open Telegram Bot", 
                        icon=ft.Icons.TELEGRAM,
                        color=ft.Colors.WHITE,
                        bgcolor=ft.Colors.BLUE_600,
                        on_click=lambda _: self.page.launch_url("https://t.me/SatisfactoryTrackerBot")
                    ),
                    ft.Divider(height=20, color=ft.Colors.TRANSPARENT),
                    ft.Text("Waiting for deeplink connection...", italic=True, size=12, color=ft.Colors.GREY_500)
                ], horizontal_alignment=ft.CrossAxisAlignment.CENTER, alignment=ft.MainAxisAlignment.CENTER),
                alignment=ft.alignment.center,
                expand=True,
                padding=40
            )
        )
        self.page.update()

    def show_main_view(self):
        me = self.api.get_me()
        if not me:
            self.show_login_view()
            return

        worlds = self.api.get_worlds()
        world_options = [ft.dropdown.Option(key=str(w["id"]), text=w["name"]) for w in worlds]
        
        self.world_dropdown = ft.Dropdown(
            label="Select World",
            options=world_options,
            width=400,
            border_color=ft.Colors.ORANGE_500,
            on_change=self.on_world_change
        )

        premium_status = "Premium" if me.get("active") else "Free"
        premium_color = ft.Colors.AMBER if me.get("active") else ft.Colors.BLUE_GREY_400

        # UI Elements for sync status
        self.local_status_card = self._build_status_card("Local Save", ft.Icons.COMPUTER)
        self.server_status_card = self._build_status_card("Server Save", ft.Icons.CLOUD)
        self.sync_message = ft.Text("", size=14, weight=ft.FontWeight.BOLD, text_align=ft.TextAlign.CENTER)

        self.btn_download = ft.ElevatedButton(
            "Download", icon=ft.Icons.DOWNLOAD, on_click=self.on_download, 
            style=ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.GREEN_600), disabled=True
        )
        self.btn_upload = ft.ElevatedButton(
            "Upload", icon=ft.Icons.UPLOAD, on_click=self.on_upload,
            style=ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.ORANGE_600), disabled=True
        )

        main_content = ft.Container(
            padding=20,
            content=ft.Column([
                ft.Row([
                    ft.Icon(ft.Icons.FACTORY_ROUNDED, color=ft.Colors.ORANGE_500, size=30),
                    ft.Text("SFT Companion", size=24, weight=ft.FontWeight.BOLD),
                    ft.Container(expand=True),
                    ft.Container(
                        content=ft.Text(f"{premium_status}", color=ft.Colors.WHITE, weight=ft.FontWeight.BOLD, size=12),
                        bgcolor=premium_color,
                        padding=ft.padding.symmetric(horizontal=10, vertical=4),
                        border_radius=15
                    )
                ], alignment=ft.MainAxisAlignment.SPACE_BETWEEN),
                ft.Divider(height=20, color=ft.Colors.GREY_800),
                
                ft.Container(
                    content=self.world_dropdown,
                    alignment=ft.alignment.center,
                    padding=ft.padding.only(bottom=10)
                ),
                
                ft.Row([self.local_status_card, self.server_status_card], alignment=ft.MainAxisAlignment.SPACE_BETWEEN),
                
                ft.Container(
                    content=self.sync_message,
                    alignment=ft.alignment.center,
                    padding=ft.padding.symmetric(vertical=15)
                ),

                ft.Row([self.btn_download, self.btn_upload], alignment=ft.MainAxisAlignment.CENTER, spacing=20),
                
                ft.Divider(height=30, color=ft.Colors.GREY_800),
                ft.Column(self._build_save_folder_ui(), spacing=10),
                
                ft.Container(expand=True),
                ft.Row([
                    ft.Text(f"v{VERSION}", size=10, color=ft.Colors.GREY_600),
                    ft.Container(expand=True),
                    ft.TextButton("Logout", on_click=self.logout, icon=ft.Icons.LOGOUT, icon_color=ft.Colors.RED_400, style=ft.ButtonStyle(color=ft.Colors.RED_400))
                ])
            ], expand=True)
        )

        self.page.clean()
        self.page.add(main_content)
        self.page.update()

        # Start auto-refresh
        if not self.auto_refresh_running:
            self.auto_refresh_running = True
            threading.Thread(target=self.auto_refresh_loop, daemon=True).start()

    def _build_status_card(self, title, icon):
        return ft.Container(
            width=210,
            padding=15,
            border_radius=10,
            bgcolor=ft.Colors.SURFACE_VARIANT,
            content=ft.Column([
                ft.Row([ft.Icon(icon, size=20, color=ft.Colors.BLUE_200), ft.Text(title, weight=ft.FontWeight.BOLD)]),
                ft.Text("Waiting...", size=12, color=ft.Colors.GREY_400, key="info"),
                ft.Text("-", size=11, color=ft.Colors.GREY_500, key="session")
            ])
        )

    def _update_card_ui(self, card, info_text, session_text, color=ft.Colors.GREY_400):
        card.content.controls[1].value = info_text
        card.content.controls[1].color = color
        card.content.controls[2].value = session_text

    def _build_save_folder_ui(self):
        ui = [
            ft.Row([
                ft.Text("Configuration", weight=ft.FontWeight.BOLD, size=16),
                ft.IconButton(ft.Icons.FOLDER_OPEN, on_click=self.on_open_folder, tooltip="Open Save Folder")
            ], alignment=ft.MainAxisAlignment.SPACE_BETWEEN)
        ]
        if len(self.save_paths) > 1:
            folder_options = [ft.dropdown.Option(key=p, text=os.path.basename(p)) for p in self.save_paths]
            self.folder_dropdown = ft.Dropdown(
                label="Steam/Epic Account ID",
                options=folder_options,
                value=self.save_path,
                width=400,
                on_change=self.on_folder_change,
                text_size=12
            )
            ui.append(self.folder_dropdown)
        else:
            ui.append(ft.Text(f"Save Path: {self.save_path or 'Not Found'}", size=11, color=ft.Colors.GREY_400, selectable=True))
        return ui

    def on_open_folder(self, e):
        if self.save_path and os.path.exists(self.save_path):
            os.startfile(self.save_path) if sys.platform == "win32" else os.system(f'xdg-open "{self.save_path}"')
        else:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Save folder not found!")))

    def on_folder_change(self, e):
        self.save_path = self.folder_dropdown.value
        self.refresh_sync_state()

    def on_world_change(self, e):
        self.refresh_sync_state()

    def auto_refresh_loop(self):
        while self.auto_refresh_running:
            time.sleep(5)
            if self.page.session_id:
                self.refresh_sync_state()

    def refresh_sync_state(self):
        if not self.world_dropdown or not self.world_dropdown.value:
            self._update_card_ui(self.server_status_card, "Select a world", "-")
            self.btn_download.disabled = True
            self.btn_upload.disabled = True
            try:
                self.page.update()
            except Exception:
                pass
            return

        # Local
        latest_local = get_latest_local_save(self.save_path)
        local_hash = None
        if latest_local:
            local_hash = get_file_hash(latest_local)
            session_name = get_session_name(latest_local)
            mtime = time.strftime('%Y-%m-%d %H:%M', time.localtime(os.path.getmtime(latest_local)))
            self._update_card_ui(self.local_status_card, f"Modified: {mtime}", f"Session: {session_name or 'Unknown'}", ft.Colors.WHITE)
        else:
            self._update_card_ui(self.local_status_card, "No saves found", "-", ft.Colors.RED_300)

        # Server
        meta = self.api.get_save_metadata(self.world_dropdown.value)
        server_hash = None
        if meta and meta.get("exists"):
            server_hash = meta.get("hash")
            session_name = meta.get("session_name", "Unknown")
            updated_at = meta.get("updated_at", "").replace("T", " ")[:16]
            self._update_card_ui(self.server_status_card, f"Updated: {updated_at}", f"Session: {session_name}", ft.Colors.WHITE)
        else:
            self._update_card_ui(self.server_status_card, "No saves on server", "-", ft.Colors.ORANGE_300)

        # Compare
        self.btn_download.disabled = False if server_hash else True
        self.btn_upload.disabled = False if local_hash else True

        if local_hash and server_hash:
            if local_hash == server_hash:
                self.sync_message.value = "✔️ Up to date"
                self.sync_message.color = ft.Colors.GREEN_400
            else:
                self.sync_message.value = "⚠️ Out of sync"
                self.sync_message.color = ft.Colors.ORANGE_400
        else:
            self.sync_message.value = "Ready to sync"
            self.sync_message.color = ft.Colors.BLUE_400

        try:
            self.page.update()
        except Exception:
            pass

    def on_download(self, e):
        if not self.world_dropdown.value or not self.save_path:
            return

        meta = self.api.get_save_metadata(self.world_dropdown.value)
        if not meta or not meta.get("exists"):
            self.page.show_snack_bar(ft.SnackBar(ft.Text("No saves found on server!")))
            return

        latest_local = get_latest_local_save(self.save_path)
        
        def do_download(e):
            self.page.dialog.open = False
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Downloading...")))
            self.page.update()
            
            result = self.api.download_save(self.world_dropdown.value, self.save_path)
            if result:
                self.page.show_snack_bar(ft.SnackBar(ft.Text(f"Downloaded: {os.path.basename(result)}", color=ft.Colors.GREEN_400)))
                self.refresh_sync_state()
            else:
                self.page.show_snack_bar(ft.SnackBar(ft.Text("Download failed!", color=ft.Colors.RED_400)))
            self.page.update()

        if latest_local:
            self.page.dialog = ft.AlertDialog(
                title=ft.Text("Confirm Download", color=ft.Colors.ORANGE_400),
                content=ft.Text("Your local save will be replaced by the server version. Continue?"),
                actions=[
                    ft.TextButton("Yes, Replace", on_click=do_download),
                    ft.TextButton("Cancel", on_click=lambda _: self.set_dialog(False)),
                ]
            )
            self.page.dialog.open = True
            self.page.update()
        else:
            do_download(None)

    def set_dialog(self, open_state):
        self.page.dialog.open = open_state
        self.page.update()

    def on_upload(self, e):
        if not self.world_dropdown.value:
            return

        latest_local = get_latest_local_save(self.save_path)
        if not latest_local:
            return

        local_hash = get_file_hash(latest_local)
        local_session = get_session_name(latest_local)
        meta = self.api.get_save_metadata(self.world_dropdown.value)
        
        if meta and meta.get("hash") == local_hash:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("This version is already on the server!")))
            return

        server_session = meta.get("session_name") if meta and meta.get("exists") else None
        warning_msg = None
        
        if server_session and local_session and server_session != local_session:
            warning_msg = f"⚠️ MISMATCH RISK\n\nServer Session: '{server_session}'\nLocal Session: '{local_session}'\n\nAre you sure you want to overwrite?"

        def do_upload(e):
            self.page.dialog.open = False
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Uploading...")))
            self.page.update()
            
            result = self.api.upload_save(self.world_dropdown.value, latest_local)
            if result and result.get("status") == "ok":
                diff = result.get("diff", {}).get("micro_summary", "")
                self.page.show_snack_bar(ft.SnackBar(ft.Text(f"Success! {diff}", color=ft.Colors.GREEN_400), duration=5000))
                self.refresh_sync_state()
            else:
                self.page.show_snack_bar(ft.SnackBar(ft.Text("Upload failed!", color=ft.Colors.RED_400)))
            self.page.update()

        if warning_msg:
            self.page.dialog = ft.AlertDialog(
                title=ft.Text("Cross-World Overwrite Risk", color=ft.Colors.RED_400),
                content=ft.Text(warning_msg),
                actions=[
                    ft.TextButton("Yes, Overwrite", on_click=do_upload, icon=ft.Icons.WARNING_AMBER_ROUNDED, style=ft.ButtonStyle(color=ft.Colors.RED_400)),
                    ft.TextButton("Cancel", on_click=lambda _: self.set_dialog(False)),
                ]
            )
            self.page.dialog.open = True
            self.page.update()
        elif meta and meta.get("exists"):
            self.page.dialog = ft.AlertDialog(
                title=ft.Text("Confirm Upload"),
                content=ft.Text("This will replace the latest save on the server. Continue?"),
                actions=[
                    ft.TextButton("Yes, Upload", on_click=do_upload),
                    ft.TextButton("Cancel", on_click=lambda _: self.set_dialog(False)),
                ]
            )
            self.page.dialog.open = True
            self.page.update()
        else:
            do_upload(None)

    def logout(self, e):
        self.auto_refresh_running = False
        _clear_token()
        self.api.token = None
        self.show_login_view()

    def handle_deeplink(self, token):
        if self.api.login(token):
            _save_token(self.api.token)
            self.show_main_view()
        else:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Login failed! Invalid token.", color=ft.Colors.RED_400)))

def main(page: ft.Page):
    import re
    app = SFTApp(page)

    for arg in sys.argv:
        if "sft://auth?token=" in arg:
            token = arg.split("token=")[-1].strip()
            if re.match(r"^[A-Za-z0-9\-_]{16,128}$", token):
                app.handle_deeplink(token)
            else:
                print("Invalid token format in deeplink.")

if __name__ == "__main__":
    ft.app(target=main)
