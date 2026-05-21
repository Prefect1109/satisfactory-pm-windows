import flet as ft
import os
import sys
from api import APIClient
from utils import get_save_games_path, get_latest_local_save

VERSION = "1.0.0"

class SFTApp:
    def __init__(self, page: ft.Page):
        self.page = page
        self.api = APIClient()
        self.save_path = get_save_games_path()
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
                # Show update dialog
                def close_app(e):
                    import subprocess
                    # Download and run installer
                    # This is simplified, in real life we would download it first
                    self.page.launch_url(download_url)
                    self.page.window_close()

                self.page.dialog = ft.AlertDialog(
                    title=ft.Text("Update Available"),
                    content=ft.Text(f"A new version ({remote_version}) is available. Please update to continue."),
                    actions=[
                        ft.TextButton("Update Now", on_click=close_app),
                    ],
                    modal=True
                )
                self.page.dialog.open = True
                self.page.update()
        except Exception as e:
            print(f"Update check failed: {e}")

    def init_ui(self):
        self.page.title = "Satisfactory Session Tracker"
        self.page.window_width = 450
        self.page.window_height = 650
        self.page.window_resizable = False
        self.page.theme_mode = ft.ThemeMode.DARK
        self.page.padding = 20

        # Check for updates first
        self.check_for_updates()

        # Load token if exists
        saved_token = self.page.client_storage.get("auth_token")
        if saved_token:
            self.api.token = saved_token
            self.api.session.headers.update({"Authorization": f"Bearer {saved_token}"})
            self.show_main_view()
        else:
            self.show_login_view()

    def show_login_view(self):
        self.page.clean()
        self.page.add(
            ft.Column([
                ft.Text("Satisfactory Tracker", size=32, weight=ft.FontWeight.BOLD),
                ft.Text("Windows Companion App", color=ft.colors.GREY_400),
                ft.Divider(height=40),
                ft.Text("Please connect your account via Telegram bot"),
                ft.ElevatedButton(
                    "Open Telegram Bot", 
                    icon=ft.icons.TELEGRAM, 
                    on_click=lambda _: self.page.launch_url("https://t.me/SatisfactoryTrackerBot")
                ),
                ft.Text("Waiting for connection...", italic=True, size=12, color=ft.colors.GREY_500)
            ], horizontal_alignment=ft.CrossAxisAlignment.CENTER, spacing=20)
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
            width=300,
            on_change=self.on_world_change
        )

        premium_status = "Premium" if me.get("active") else "Free"
        premium_color = ft.colors.AMBER if me.get("active") else ft.colors.BLUE_GREY_400

        self.status_text = ft.Text(f"Account: {premium_status}", color=premium_color, weight=ft.FontWeight.BOLD)
        
        self.local_save_text = ft.Text("Detecting local saves...", size=12, color=ft.colors.GREY_400)
        self.update_local_save_info()

        self.page.clean()
        self.page.add(
            ft.Column([
                ft.Row([
                    ft.Text("SFT Companion", size=24, weight=ft.FontWeight.BOLD),
                    ft.Container(expand=True),
                    self.status_text
                ]),
                ft.Divider(),
                self.world_dropdown,
                ft.Divider(height=20),
                ft.Row([
                    ft.ElevatedButton(
                        "Download Latest", 
                        icon=ft.icons.DOWNLOAD, 
                        on_click=self.on_download,
                        color=ft.colors.GREEN_400
                    ),
                    ft.ElevatedButton(
                        "Upload New", 
                        icon=ft.icons.UPLOAD, 
                        on_click=self.on_upload,
                        color=ft.colors.ORANGE_400
                    ),
                ], alignment=ft.MainAxisAlignment.CENTER),
                ft.Divider(height=40),
                ft.Column([
                    ft.Text("Save Folder Status:", weight=ft.FontWeight.BOLD),
                    ft.Text(f"Path: {self.save_path or 'Not Found'}", size=11, color=ft.colors.GREY_400),
                    self.local_save_text
                ], spacing=5),
                ft.Container(expand=True),
                ft.TextButton("Logout", on_click=self.logout, font_family="monospace")
            ], expand=True)
        )
        self.page.update()

    def update_local_save_info(self):
        latest = get_latest_local_save(self.save_path)
        if latest:
            self.local_save_text.value = f"Latest local: {os.path.basename(latest)}"
        else:
            self.local_save_text.value = "No local .sav files found"

    def on_world_change(self, e):
        pass

    def on_download(self, e):
        if not self.world_dropdown.value:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Select a world first!")))
            return
        
        if not self.save_path:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Save path not found!")))
            return

        self.page.show_snack_bar(ft.SnackBar(ft.Text("Downloading...")))
        result = self.api.download_save(self.world_dropdown.value, self.save_path)
        if result:
            self.page.show_snack_bar(ft.SnackBar(ft.Text(f"Downloaded: {os.path.basename(result)}")))
            self.update_local_save_info()
            self.page.update()
        else:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Download failed!")))

    def on_upload(self, e):
        if not self.world_dropdown.value:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Select a world first!")))
            return

        latest = get_latest_local_save(self.save_path)
        if not latest:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("No local save to upload!")))
            return

        self.page.show_snack_bar(ft.SnackBar(ft.Text("Uploading...")))
        result = self.api.upload_save(self.world_dropdown.value, latest)
        if result:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Upload successful!")))
        else:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Upload failed!")))

    def logout(self, e):
        self.page.client_storage.remove("auth_token")
        self.api.token = None
        self.show_login_view()

    def handle_deeplink(self, token):
        if self.api.login(token):
            self.page.client_storage.set("auth_token", self.api.token)
            self.show_main_view()
        else:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Login failed! Invalid token.")))

def main(page: ft.Page):
    app = SFTApp(page)

    # Check for token in command line args (deeplink)
    # sft://auth?token=XXX -> sys.argv might contain this or just part of it
    for arg in sys.argv:
        if "sft://auth?token=" in arg:
            token = arg.split("token=")[-1]
            app.handle_deeplink(token)

if __name__ == "__main__":
    ft.app(target=main)
