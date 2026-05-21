import flet as ft
import os
import sys
import threading
import time
import pathlib
import webbrowser
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
        self.save_paths = []
        self.save_path = None

        self.local_save_info = None
        self.server_save_info = None
        self.auto_refresh_running = False

        self._server_meta_cache = {}   # world_id → (meta, timestamp)
        self._SERVER_TTL = 30          # seconds before re-fetching server meta

        self.init_ui()

    def check_for_updates(self):
        # ... rest of check_for_updates ...
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
                    webbrowser.open(download_url)
                    try:
                        self.page.window.close()
                    except AttributeError:
                        self.page.window_close()

                self._open_dialog(ft.AlertDialog(
                    title=ft.Text("Update Available", color=ft.Colors.BLUE_400),
                    content=ft.Text(f"A new version ({remote_version}) is available. Please update to continue."),
                    actions=[ft.TextButton("Update Now", on_click=close_app)],
                    modal=True
                ))
        except Exception:
            pass

    def _snack(self, text, color=None):
        sb = ft.SnackBar(ft.Text(text, color=color) if color else ft.Text(text))
        try:
            self.page.open(sb)
        except Exception:
            self.page.show_snack_bar(sb)  # noqa: fallback for older flet

    def _open_dialog(self, dialog):
        try:
            self.page.open(dialog)
        except Exception:
            self.page.dialog = dialog
            dialog.open = True
            self.page.update()

    def _close_dialog(self, dialog=None):
        try:
            self.page.close(dialog or self.page.dialog)
        except Exception:
            try:
                self.page.dialog.open = False
            except Exception:
                pass
            self.page.update()

    def init_ui(self):
        self.page.title = "Satisfactory Session Tracker"
        try:
            self.page.window.width = 500
            self.page.window.height = 750
            self.page.window.resizable = False
        except Exception:
            try:
                self.page.window_width = 500
                self.page.window_height = 750
                self.page.window_resizable = False
            except Exception:
                pass
        try:
            self.page.theme_mode = ft.ThemeMode.DARK
        except Exception:
            pass

        # Async check for updates
        threading.Thread(target=self.check_for_updates, daemon=True).start()

        saved_token = _load_token()
        if saved_token:
            self.api.token = saved_token
            self.api.session.headers.update({"Authorization": f"Bearer {saved_token}"})
            # Show loader then load main view
            self.show_loading_view()
            threading.Thread(target=self.show_main_view, daemon=True).start()
        else:
            self.show_login_view()

    def show_loading_view(self, text="Loading data..."):
        self.page.clean()
        self.page.add(
            ft.Container(
                content=ft.Column([
                    ft.ProgressRing(color=ft.Colors.ORANGE_500, width=50, height=50),
                    ft.Text(text, color=ft.Colors.GREY_400)
                ], horizontal_alignment=ft.CrossAxisAlignment.CENTER, alignment=ft.MainAxisAlignment.CENTER, spacing=20),
                expand=True,
                alignment=ft.Alignment(0, 0)
            )
        )
        self.page.update()

    def show_login_view(self):
        self.page.clean()

        token_field = ft.TextField(
            label="Connect Token",
            hint_text="Paste token from /connect in bot",
            border_color=ft.Colors.ORANGE_500,
            width=340,
            password=False,
        )
        error_text = ft.Text("", color=ft.Colors.RED_400, size=12)

        def do_login(e):
            token = token_field.value.strip() if token_field.value else ""
            if not token:
                error_text.value = "Enter a token"
                self.page.update()
                return
            error_text.value = ""
            self.page.update()
            self.handle_deeplink(token)

        token_field.on_submit = do_login

        self.page.add(
            ft.Container(
                content=ft.Column([
                    ft.Icon(ft.Icons.FACTORY_ROUNDED, size=80, color=ft.Colors.ORANGE_500),
                    ft.Text("Satisfactory Tracker", size=32, weight=ft.FontWeight.BOLD, text_align=ft.TextAlign.CENTER),
                    ft.Text("Windows Companion App", color=ft.Colors.GREY_400, size=16),
                    ft.Divider(height=40, color=ft.Colors.TRANSPARENT),
                    ft.ElevatedButton(
                        "Open Telegram Bot",
                        icon=ft.Icons.TELEGRAM,
                        color=ft.Colors.WHITE,
                        bgcolor=ft.Colors.BLUE_600,
                        on_click=lambda _: webbrowser.open("https://t.me/satisfactory_pm_bot")
                    ),
                    ft.Text("Use /connect in the bot to get your token", color=ft.Colors.GREY_500, size=12),
                    ft.Divider(height=20, color=ft.Colors.TRANSPARENT),
                    token_field,
                    error_text,
                    ft.ElevatedButton(
                        "Connect",
                        icon=ft.Icons.LOGIN,
                        color=ft.Colors.WHITE,
                        bgcolor=ft.Colors.ORANGE_600,
                        width=340,
                        on_click=do_login,
                    ),
                ], horizontal_alignment=ft.CrossAxisAlignment.CENTER, alignment=ft.MainAxisAlignment.CENTER),
                alignment=ft.Alignment(0, 0),
                expand=True,
                padding=40
            )
        )
        self.page.update()

    def show_main_view(self):
        from concurrent.futures import ThreadPoolExecutor
        try:
            with ThreadPoolExecutor(max_workers=3) as ex:
                f_me = ex.submit(self.api.get_me)
                f_worlds = ex.submit(self.api.get_worlds)
                f_paths = ex.submit(get_save_games_path)
                me = f_me.result()
                worlds = f_worlds.result()
                self.save_paths = f_paths.result()

            if not me:
                self.show_login_view()
                return

            if not self.save_path and self.save_paths:
                self.save_path = self.save_paths[0]
        except Exception as e:
            print(f"Error loading initial data: {e}")
            self.show_login_view()
            return

        world_options = [ft.dropdown.Option(key=str(w["id"]), text=w["name"]) for w in worlds]
        
        # Build UI controls (this must be fast or we can move it to main thread if needed)
        # But Flet controls are fine to create in threads as long as we add them to page safely
        
        self.world_dropdown = ft.Dropdown(
            label="Select World",
            options=world_options,
            width=400,
            border_color=ft.Colors.ORANGE_500,
        )
        self.world_dropdown.on_change = self.on_world_change

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
                    alignment=ft.Alignment(0, 0),
                    padding=ft.padding.only(bottom=10)
                ),
                
                ft.Row([self.local_status_card, self.server_status_card], alignment=ft.MainAxisAlignment.SPACE_BETWEEN),
                
                ft.Container(
                    content=self.sync_message,
                    alignment=ft.Alignment(0, 0),
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
                text_size=12,
            )
            self.folder_dropdown.on_change = self.on_folder_change
            ui.append(self.folder_dropdown)
        else:
            ui.append(ft.Text(f"Save Path: {self.save_path or 'Not Found'}", size=11, color=ft.Colors.GREY_400, selectable=True))
        return ui

    def on_open_folder(self, e):
        if self.save_path and os.path.exists(self.save_path):
            os.startfile(self.save_path) if sys.platform == "win32" else os.system(f'xdg-open "{self.save_path}"')
        else:
            self._snack("Save folder not found!")

    def on_folder_change(self, e):
        self.save_path = self.folder_dropdown.value
        self.refresh_sync_state(force_server=True)

    def on_world_change(self, e):
        self.refresh_sync_state(force_server=True)

    def auto_refresh_loop(self):
        server_tick = 0
        while self.auto_refresh_running:
            time.sleep(3)
            try:
                force_server = server_tick <= 0
                self._refresh_local()
                if force_server:
                    self._refresh_server()
                    server_tick = self._SERVER_TTL // 3
                else:
                    server_tick -= 1
                self._update_sync_ui()
            except Exception:
                break

    def _get_server_meta(self, world_id, force=False):
        cached = self._server_meta_cache.get(world_id)
        if cached and not force:
            meta, ts = cached
            if time.time() - ts < self._SERVER_TTL:
                return meta
        meta = self.api.get_save_metadata(world_id)
        self._server_meta_cache[world_id] = (meta, time.time())
        return meta

    def _refresh_local(self):
        if not getattr(self, 'world_dropdown', None):
            return
        latest_local = get_latest_local_save(self.save_path)
        if latest_local:
            self._local_hash = get_file_hash(latest_local)
            self._local_path = latest_local
            session_name = get_session_name(latest_local)
            mtime = time.strftime('%Y-%m-%d %H:%M', time.localtime(os.path.getmtime(latest_local)))
            self._update_card_ui(self.local_status_card, f"Modified: {mtime}", f"Session: {session_name or 'Unknown'}", ft.Colors.WHITE)
        else:
            self._local_hash = None
            self._local_path = None
            self._update_card_ui(self.local_status_card, "No saves found", "-", ft.Colors.RED_300)

    def _refresh_server(self, force=False):
        if not getattr(self, 'world_dropdown', None) or not self.world_dropdown.value:
            self._server_hash = None
            self._update_card_ui(self.server_status_card, "Select a world", "-")
            return
        meta = self._get_server_meta(self.world_dropdown.value, force=force)
        if meta and meta.get("exists"):
            self._server_hash = meta.get("hash")
            session_name = meta.get("session_name", "Unknown")
            updated_at = meta.get("updated_at", "").replace("T", " ")[:16]
            self._update_card_ui(self.server_status_card, f"Updated: {updated_at}", f"Session: {session_name}", ft.Colors.WHITE)
        else:
            self._server_hash = None
            self._update_card_ui(self.server_status_card, "No saves on server", "-", ft.Colors.ORANGE_300)

    def _update_sync_ui(self):
        local_hash = getattr(self, '_local_hash', None)
        server_hash = getattr(self, '_server_hash', None)
        if not getattr(self, 'btn_download', None):
            return
        self.btn_download.disabled = not bool(server_hash)
        self.btn_upload.disabled = not bool(local_hash)
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

    def refresh_sync_state(self, force_server=False):
        self._refresh_local()
        self._refresh_server(force=force_server)
        self._update_sync_ui()

    def on_download(self, e):
        if not self.world_dropdown.value or not self.save_path:
            return

        meta = self.api.get_save_metadata(self.world_dropdown.value)
        if not meta or not meta.get("exists"):
            self._snack("No saves found on server!")
            return

        latest_local = get_latest_local_save(self.save_path)
        dlg = None

        def do_download(e):
            self._close_dialog(dlg)
            self._snack("Downloading...")
            result = self.api.download_save(self.world_dropdown.value, self.save_path)
            if result:
                self._snack(f"Downloaded: {os.path.basename(result)}", ft.Colors.GREEN_400)
                self.refresh_sync_state(force_server=True)
            else:
                self._snack("Download failed!", ft.Colors.RED_400)

        if latest_local:
            dlg = ft.AlertDialog(
                title=ft.Text("Confirm Download", color=ft.Colors.ORANGE_400),
                content=ft.Text("Your local save will be replaced by the server version. Continue?"),
                actions=[
                    ft.TextButton("Yes, Replace", on_click=do_download),
                    ft.TextButton("Cancel", on_click=lambda _: self._close_dialog(dlg)),
                ]
            )
            self._open_dialog(dlg)
        else:
            do_download(None)

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
            self._snack("This version is already on the server!")
            return

        server_session = meta.get("session_name") if meta and meta.get("exists") else None
        warning_msg = None

        if server_session and local_session and server_session != local_session:
            warning_msg = f"⚠️ MISMATCH RISK\n\nServer Session: '{server_session}'\nLocal Session: '{local_session}'\n\nAre you sure you want to overwrite?"

        dlg = None

        def do_upload(e):
            self._close_dialog(dlg)
            self._snack("Uploading...")
            result = self.api.upload_save(self.world_dropdown.value, latest_local)
            if result and result.get("status") == "ok":
                diff = result.get("diff", {}).get("micro_summary", "")
                self._snack(f"Success! {diff}", ft.Colors.GREEN_400)
                self.refresh_sync_state(force_server=True)
            else:
                self._snack("Upload failed!", ft.Colors.RED_400)

        if warning_msg:
            dlg = ft.AlertDialog(
                title=ft.Text("Cross-World Overwrite Risk", color=ft.Colors.RED_400),
                content=ft.Text(warning_msg),
                actions=[
                    ft.TextButton("Yes, Overwrite", on_click=do_upload, style=ft.ButtonStyle(color=ft.Colors.RED_400)),
                    ft.TextButton("Cancel", on_click=lambda _: self._close_dialog(dlg)),
                ]
            )
            self._open_dialog(dlg)
        elif meta and meta.get("exists"):
            dlg = ft.AlertDialog(
                title=ft.Text("Confirm Upload"),
                content=ft.Text("This will replace the latest save on the server. Continue?"),
                actions=[
                    ft.TextButton("Yes, Upload", on_click=do_upload),
                    ft.TextButton("Cancel", on_click=lambda _: self._close_dialog(dlg)),
                ]
            )
            self._open_dialog(dlg)
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
            self._snack("Login failed! Invalid token.", ft.Colors.RED_400)

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
