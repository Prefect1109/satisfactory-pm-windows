import flet as ft
import os
import sys
from api import APIClient
from utils import get_save_games_path, get_latest_local_save, get_file_hash

VERSION = "1.0.0"

class SFTApp:
    def __init__(self, page: ft.Page):
        self.page = page
        self.api = APIClient()
        self.save_paths = get_save_games_path()
        self.save_path = self.save_paths[0] if self.save_paths else None
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
                # Validate URL for security
                if not download_url or not download_url.startswith("https://"):
                    print("Invalid download URL. Must be HTTPS.")
                    return

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
                ft.Column(self._build_save_folder_ui(), spacing=5),
                ft.Container(expand=True),
                ft.TextButton("Logout", on_click=self.logout, font_family="monospace")
            ], expand=True)
        )
        self.page.update()

    def _build_save_folder_ui(self):
        ui = [ft.Text("Save Folder Status:", weight=ft.FontWeight.BOLD)]
        
        if len(self.save_paths) > 1:
            # Show a dropdown if multiple Steam IDs
            folder_options = [ft.dropdown.Option(key=p, text=os.path.basename(p)) for p in self.save_paths]
            self.folder_dropdown = ft.Dropdown(
                label="Select Account ID",
                options=folder_options,
                value=self.save_path,
                width=300,
                on_change=self.on_folder_change
            )
            ui.append(self.folder_dropdown)
            ui.append(ft.Text(f"Path: {os.path.dirname(self.save_path)}/...", size=11, color=ft.colors.GREY_400))
        else:
            ui.append(ft.Text(f"Path: {self.save_path or 'Not Found'}", size=11, color=ft.colors.GREY_400))
            
        ui.append(self.local_save_text)
        return ui

    def on_folder_change(self, e):
        self.save_path = self.folder_dropdown.value
        self.update_local_save_info()
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

        # Smart Sync: Check metadata
        meta = self.api.get_save_metadata(self.world_dropdown.value)
        if not meta or not meta.get("exists"):
            self.page.show_snack_bar(ft.SnackBar(ft.Text("No saves found on server!")))
            return

        latest_local = get_latest_local_save(self.save_path)
        local_hash = get_file_hash(latest_local) if latest_local else None
        
        if local_hash == meta.get("hash"):
            self.page.show_snack_bar(ft.SnackBar(ft.Text("You already have the latest version!")))
            return

        def do_download(e):
            self.page.dialog.open = False
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Downloading...")))
            result = self.api.download_save(self.world_dropdown.value, self.save_path)
            if result:
                self.page.show_snack_bar(ft.SnackBar(ft.Text(f"Downloaded: {os.path.basename(result)}")))
                self.update_local_save_info()
            else:
                self.page.show_snack_bar(ft.SnackBar(ft.Text("Download failed!")))
            self.page.update()

        # If we have local changes, ask for confirmation
        if latest_local:
            self.page.dialog = ft.AlertDialog(
                title=ft.Text("Confirm Download"),
                content=ft.Text("Your local save will be replaced. Continue?"),
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
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Select a world first!")))
            return

        latest_local = get_latest_local_save(self.save_path)
        if not latest_local:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("No local save to upload!")))
            return

        # Smart Sync: Check if same version already on server
        local_hash = get_file_hash(latest_local)
        meta = self.api.get_save_metadata(self.world_dropdown.value)
        if meta and meta.get("hash") == local_hash:
            self.page.show_snack_bar(ft.SnackBar(ft.Text("This version is already on the server!")))
            return

        def do_upload(e):
            self.page.dialog.open = False
            self.page.show_snack_bar(ft.SnackBar(ft.Text("Uploading...")))
            result = self.api.upload_save(self.world_dropdown.value, latest_local)
            if result and result.get("status") == "ok":
                diff_summary = result.get("diff", {}).get("micro_summary", "Upload successful!")
                self.page.show_snack_bar(ft.SnackBar(ft.Text(f"Success! {diff_summary}"), duration=5000))
            else:
                self.page.show_snack_bar(ft.SnackBar(ft.Text("Upload failed!")))
            self.page.update()

        # Confirm upload if server has a different version
        if meta and meta.get("exists"):
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
:
    ft.app(target=main)
