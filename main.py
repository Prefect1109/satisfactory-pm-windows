import flet as ft
import os
import sys
import threading
import time
import pathlib
import webbrowser
import datetime
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

def _save_last_world(world_id):
    try:
        (_token_path().parent / 'last_world.txt').write_text(str(world_id))
    except Exception:
        pass

def _load_last_world():
    try:
        return (_token_path().parent / 'last_world.txt').read_text().strip() or None
    except Exception:
        return None

VERSION = "1.1.9"

class SFTApp:
    def __init__(self, page: ft.Page):
        self.page = page
        self.api = APIClient()
        self.save_paths = get_save_games_path()
        self.save_path = self.save_paths[0] if self.save_paths else None
        
        self.local_save_info = None
        self.server_save_info = None
        self.auto_refresh_running = False
        self._sync_direction = None
        self._cached_meta = None
        self._local_hash_cache = {}

        self.init_ui()

    def _get_local_hash(self, filepath):
        if not filepath or not os.path.exists(filepath):
            return None
        mtime = os.path.getmtime(filepath)
        cached = self._local_hash_cache.get(filepath)
        if cached and cached[0] == mtime:
            return cached[1]
        h = get_file_hash(filepath)
        self._local_hash_cache[filepath] = (mtime, h)
        return h

    def check_for_updates(self):
        try:
            version_info = self.api.get_version()
            if not version_info:
                return

            remote_version = version_info.get("version", "0.0.0")
            force_update = version_info.get("force_update", False)
            download_url = version_info.get("url", "")

            def _ver(v):
                try:
                    return tuple(int(x) for x in str(v).split("."))
                except Exception:
                    return (0,)

            is_newer = _ver(remote_version) > _ver(VERSION)
            if not is_newer and not force_update:
                return
            if not download_url or not download_url.startswith("https://"):
                return

            def do_update(e):
                webbrowser.open(download_url)
                try:
                    self.page.window.close()
                except AttributeError:
                    pass

            actions = [ft.TextButton("Update Now", on_click=do_update)]
            if not force_update:
                actions.append(ft.TextButton("Later", on_click=lambda _: self._close_dialog()))

            self._open_dialog(ft.AlertDialog(
                title=ft.Text("Update Available", color=ft.Colors.BLUE_400),
                content=ft.Text(f"Version {remote_version} is available (you have {VERSION})."),
                actions=actions,
                modal=force_update,
            ))
        except Exception:
            pass

    def _snack(self, text, color=None):
        sb = ft.SnackBar(ft.Text(text, color=color) if color else ft.Text(text))
        self.page.overlay.append(sb)
        sb.open = True
        self.page.update()

    def _open_dialog(self, dialog):
        self.page.show_dialog(dialog)

    def _close_dialog(self, dialog=None):
        self.page.pop_dialog()

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

        # Show loading screen immediately — no blocking API calls here
        self.page.add(ft.Container(
            content=ft.Column([
                ft.Icon(ft.Icons.FACTORY_ROUNDED, size=64, color=ft.Colors.ORANGE_500),
                ft.ProgressRing(color=ft.Colors.ORANGE_500, width=36, height=36, stroke_width=3),
                ft.Text("Loading...", color=ft.Colors.GREY_400, size=14),
            ], horizontal_alignment=ft.CrossAxisAlignment.CENTER,
               alignment=ft.MainAxisAlignment.CENTER, spacing=18),
            alignment=ft.Alignment(0, 0), expand=True,
        ))
        self.page.update()

        threading.Thread(target=self.check_for_updates, daemon=True).start()
        threading.Thread(target=self._load_initial_data, daemon=True).start()

    def _load_initial_data(self):
        from concurrent.futures import ThreadPoolExecutor
        saved_token = _load_token()
        if not saved_token:
            self.show_login_view()
            return
        self.api.token = saved_token
        self.api.session.headers.update({"Authorization": f"Bearer {saved_token}"})
        try:
            with ThreadPoolExecutor(max_workers=2) as ex:
                me_f = ex.submit(self.api.get_me)
                worlds_f = ex.submit(self.api.get_worlds)
                me = me_f.result(timeout=8)
                worlds = worlds_f.result(timeout=8)
        except Exception:
            self._open_dialog(ft.AlertDialog(
                title=ft.Text("No Connection", color=ft.Colors.RED_400),
                content=ft.Text("Cannot reach the server. Check your internet connection."),
                actions=[ft.TextButton("OK", on_click=lambda _: self._close_dialog())],
            ))
            self.show_login_view()
            return
        if not me:
            self.show_login_view()
            return
        self.show_main_view(me, worlds or [])

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

    def show_main_view(self, me=None, worlds=None):
        if me is None:
            me = self.api.get_me()
        if not me:
            self.show_login_view()
            return
        if worlds is None:
            worlds = self.api.get_worlds() or []
        world_options = [ft.dropdown.Option(key=str(w["id"]), text=w["name"]) for w in worlds]

        last_world = _load_last_world()
        valid_ids = {str(w["id"]) for w in worlds}
        preselect = last_world if last_world in valid_ids else (str(worlds[0]["id"]) if worlds else None)

        self.world_dropdown = ft.Dropdown(
            label="Select World",
            options=world_options,
            value=preselect,
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

        self._sync_direction = None
        self.btn_sync = ft.ElevatedButton(
            "Sync", icon=ft.Icons.SYNC, on_click=self.on_sync,
            style=ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.ORANGE_600),
            disabled=True, width=220,
        )
        self.sync_progress = ft.ProgressBar(visible=False, color=ft.Colors.ORANGE_500, width=400)
        self.sync_error = ft.Text("", color=ft.Colors.RED_400, size=12, text_align=ft.TextAlign.CENTER)
        self.world_loader = ft.ProgressRing(width=20, height=20, stroke_width=2, visible=False, color=ft.Colors.ORANGE_500)

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
                        padding=ft.Padding(left=10, right=10, top=4, bottom=4),
                        border_radius=15
                    )
                ], alignment=ft.MainAxisAlignment.SPACE_BETWEEN),
                ft.Divider(height=20, color=ft.Colors.GREY_800),
                
                ft.Container(
                    content=ft.Row(
                        [self.world_dropdown, self.world_loader],
                        alignment=ft.MainAxisAlignment.CENTER,
                        vertical_alignment=ft.CrossAxisAlignment.CENTER,
                    ),
                    alignment=ft.Alignment(0, 0),
                    padding=ft.Padding(bottom=10)
                ),
                
                ft.Row([self.local_status_card, self.server_status_card], alignment=ft.MainAxisAlignment.SPACE_BETWEEN),
                
                ft.Container(
                    content=self.sync_message,
                    alignment=ft.Alignment(0, 0),
                    padding=ft.Padding(top=15, bottom=15)
                ),

                ft.Column([
                    ft.Row([self.btn_sync], alignment=ft.MainAxisAlignment.CENTER),
                    ft.Row([self.sync_progress], alignment=ft.MainAxisAlignment.CENTER),
                    ft.Row([self.sync_error], alignment=ft.MainAxisAlignment.CENTER),
                ], horizontal_alignment=ft.CrossAxisAlignment.CENTER, spacing=6),
                
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

        # Auto-select last used world
        last = _load_last_world()
        if last and any(o.key == last for o in world_options):
            self.world_dropdown.value = last
            self.page.update()
            threading.Thread(target=self._trigger_world_load, daemon=True).start()
        elif not self.auto_refresh_running:
            self.auto_refresh_running = True
            threading.Thread(target=self.auto_refresh_loop, daemon=True).start()

    def _trigger_world_load(self):
        self.world_loader.visible = True
        self._update_card_ui(self.local_status_card, "Loading...", "-", ft.Colors.GREY_400)
        self._update_card_ui(self.server_status_card, "Loading...", "-", ft.Colors.GREY_400)
        try:
            self.page.update()
        except Exception:
            pass
        self.refresh_sync_state()
        if not self.auto_refresh_running:
            self.auto_refresh_running = True
            threading.Thread(target=self.auto_refresh_loop, daemon=True).start()

    def _build_status_card(self, title, icon):
        return ft.Container(
            width=210,
            padding=15,
            border_radius=10,
            bgcolor=ft.Colors.SURFACE_CONTAINER,
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
        self.refresh_sync_state()

    def on_world_change(self, e):
        _save_last_world(self.world_dropdown.value)
        self.world_loader.visible = True
        self._update_card_ui(self.local_status_card, "Loading...", "-", ft.Colors.GREY_400)
        self._update_card_ui(self.server_status_card, "Loading...", "-", ft.Colors.GREY_400)
        self.btn_sync.disabled = True
        self.sync_error.value = ""
        try:
            self.page.update()
        except Exception:
            pass
        threading.Thread(target=self.refresh_sync_state, daemon=True).start()

    def auto_refresh_loop(self):
        while self.auto_refresh_running:
            try:
                self.refresh_sync_state()
            except Exception:
                break
            time.sleep(5)

    def refresh_sync_state(self):
        if not self.world_dropdown or not self.world_dropdown.value:
            self._update_card_ui(self.server_status_card, "Select a world", "-")
            self.btn_sync.disabled = True
            try:
                self.page.update()
            except Exception:
                pass
            return

        # Local
        latest_local = get_latest_local_save(self.save_path)
        local_hash = None
        if latest_local:
            local_hash = self._get_local_hash(latest_local)
            session_name = get_session_name(latest_local)
            mtime = time.strftime('%Y-%m-%d %H:%M', time.localtime(os.path.getmtime(latest_local)))
            self._update_card_ui(self.local_status_card, f"Modified: {mtime}", f"Session: {session_name or 'Unknown'}", ft.Colors.WHITE)
        else:
            self._update_card_ui(self.local_status_card, "No saves found", "-", ft.Colors.RED_300)

        # Server
        meta = self.api.get_save_metadata(self.world_dropdown.value)
        self._cached_meta = meta
        server_hash = None
        if meta and meta.get("exists"):
            server_hash = meta.get("hash")
            session_name = meta.get("session_name", "Unknown")
            updated_at = (meta.get("updated_at") or meta.get("created_at", "")).replace("T", " ")[:16]
            self._update_card_ui(self.server_status_card, f"Updated: {updated_at}", f"Session: {session_name}", ft.Colors.WHITE)
        else:
            self._update_card_ui(self.server_status_card, "No saves on server", "-", ft.Colors.ORANGE_300)

        # Determine sync direction and update Sync button
        self._sync_direction = None
        self.world_loader.visible = False
        if local_hash and server_hash:
            if local_hash == server_hash:
                self.btn_sync.disabled = True
                self.btn_sync.text = "Synced"
                self.btn_sync.icon = ft.Icons.CHECK_CIRCLE_OUTLINE
                self.btn_sync.style = ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.GREEN_800)
                self.sync_message.value = "✔ Up to date"
                self.sync_message.color = ft.Colors.GREEN_400
            else:
                local_session = get_session_name(latest_local) if latest_local else ""
                server_session = meta.get("session_name", "") if meta else ""
                sessions_differ = local_session and server_session and local_session != server_session

                if sessions_differ:
                    # Different sessions — user must choose direction
                    self._sync_direction = "ask"
                    self.btn_sync.text = "Sync  ↕ Choose"
                    self.btn_sync.icon = ft.Icons.SWAP_VERT
                    self.btn_sync.style = ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.BLUE_600)
                else:
                    local_mtime = os.path.getmtime(latest_local) if latest_local else 0
                    server_time_str = meta.get("updated_at") or meta.get("created_at", "")
                    try:
                        server_mtime = datetime.datetime.strptime(server_time_str, "%Y-%m-%d %H:%M:%S").timestamp()
                    except Exception:
                        server_mtime = 0
                    if local_mtime >= server_mtime:
                        self._sync_direction = "upload"
                        self.btn_sync.text = "Sync  ↑ Upload"
                        self.btn_sync.icon = ft.Icons.UPLOAD
                        self.btn_sync.style = ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.ORANGE_600)
                    else:
                        self._sync_direction = "download"
                        self.btn_sync.text = "Sync  ↓ Download"
                        self.btn_sync.icon = ft.Icons.DOWNLOAD
                        self.btn_sync.style = ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.GREEN_600)
                self.btn_sync.disabled = False
                self.sync_message.value = "⚠ Out of sync"
                self.sync_message.color = ft.Colors.ORANGE_400
        elif local_hash:
            self._sync_direction = "upload"
            self.btn_sync.text = "Sync  ↑ Upload"
            self.btn_sync.icon = ft.Icons.UPLOAD
            self.btn_sync.style = ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.ORANGE_600)
            self.btn_sync.disabled = False
            self.sync_message.value = "No server save yet"
            self.sync_message.color = ft.Colors.BLUE_400
        elif server_hash:
            self._sync_direction = "download"
            self.btn_sync.text = "Sync  ↓ Download"
            self.btn_sync.icon = ft.Icons.DOWNLOAD
            self.btn_sync.style = ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.GREEN_600)
            self.btn_sync.disabled = False
            self.sync_message.value = "No local save"
            self.sync_message.color = ft.Colors.BLUE_400
        else:
            self.btn_sync.disabled = True
            self.btn_sync.text = "Sync"
            self.btn_sync.icon = ft.Icons.SYNC
            self.btn_sync.style = ft.ButtonStyle(color=ft.Colors.WHITE, bgcolor=ft.Colors.BLUE_GREY_600)
            self.sync_message.value = "No saves found"
            self.sync_message.color = ft.Colors.GREY_500

        try:
            self.page.update()
        except Exception:
            pass

    def on_sync(self, e):
        if self._sync_direction == "upload":
            self._confirm_and_upload()
        elif self._sync_direction == "download":
            self._confirm_and_download()
        elif self._sync_direction == "ask":
            self._ask_sync_direction()

    def _ask_sync_direction(self):
        meta = self._cached_meta
        latest_local = get_latest_local_save(self.save_path)
        local_session = get_session_name(latest_local) if latest_local else "?"
        server_session = meta.get("session_name", "?") if meta else "?"
        dlg = ft.AlertDialog(
            title=ft.Text("Choose Sync Direction"),
            content=ft.Text(
                f"Sessions are different:\n"
                f"  Local:  {local_session}\n"
                f"  Server: {server_session}\n\n"
                f"What do you want to do?"
            ),
            actions=[
                ft.TextButton(
                    f"↓ Get server save",
                    on_click=lambda _: (self._close_dialog(), self._confirm_and_download()),
                    style=ft.ButtonStyle(color=ft.Colors.GREEN_400),
                ),
                ft.TextButton(
                    f"↑ Push local save",
                    on_click=lambda _: (self._close_dialog(), self._confirm_and_upload()),
                    style=ft.ButtonStyle(color=ft.Colors.ORANGE_400),
                ),
                ft.TextButton("Cancel", on_click=lambda _: self._close_dialog()),
            ],
        )
        self._open_dialog(dlg)

    def _confirm_and_download(self):
        if not self.world_dropdown.value or not self.save_path:
            return
        self.sync_progress.visible = True
        self.btn_sync.disabled = True
        self.sync_error.value = ""
        try:
            self.page.update()
        except Exception:
            pass
        def _run():
            result = self.api.download_save(self.world_dropdown.value, self.save_path)
            self.sync_progress.visible = False
            if result:
                self._snack(f"Downloaded: {os.path.basename(result)}", ft.Colors.GREEN_400)
                self.refresh_sync_state()
            else:
                self.sync_error.value = "Download failed! Check connection."
                self.btn_sync.disabled = False
            try:
                self.page.update()
            except Exception:
                pass
        threading.Thread(target=_run, daemon=True).start()

    def _confirm_and_upload(self):
        if not self.world_dropdown.value:
            return
        latest_local = get_latest_local_save(self.save_path)
        if not latest_local:
            return
        local_hash = self._get_local_hash(latest_local)
        local_session = get_session_name(latest_local)
        meta = self._cached_meta
        server_session = meta.get("session_name") if meta and meta.get("exists") else None

        def do_upload():
            self.sync_progress.visible = True
            self.btn_sync.disabled = True
            self.sync_error.value = ""
            try:
                self.page.update()
            except Exception:
                pass
            def _run():
                result = self.api.upload_save(self.world_dropdown.value, latest_local)
                self.sync_progress.visible = False
                if result and result.get("status") == "ok":
                    diff = result.get("diff", {}).get("micro_summary", "")
                    self._snack(f"Uploaded! {diff}".strip(), ft.Colors.GREEN_400)
                    self.refresh_sync_state()
                else:
                    self.sync_error.value = "Upload failed! Check connection."
                    self.btn_sync.disabled = False
                try:
                    self.page.update()
                except Exception:
                    pass
            threading.Thread(target=_run, daemon=True).start()

        if meta and meta.get("exists"):
            dlg = ft.AlertDialog(
                title=ft.Text("Confirm Upload"),
                content=ft.Text("This will replace the save on the server. Continue?"),
                actions=[
                    ft.TextButton("Yes, Upload", on_click=lambda _: (self._close_dialog(dlg), do_upload())),
                    ft.TextButton("Cancel", on_click=lambda _: self._close_dialog(dlg)),
                ]
            )
            self._open_dialog(dlg)
        else:
            do_upload()

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
