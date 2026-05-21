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

VERSION = "1.2.0"

# --- Token Helpers ---
def _token_path():
    appdata = os.environ.get('APPDATA', os.path.expanduser('~'))
    p = pathlib.Path(appdata) / 'SFTracker'
    p.mkdir(parents=True, exist_ok=True)
    return p / 'token.txt'

def _load_token():
    try: return _token_path().read_text().strip() or None
    except Exception: return None

def _save_token(token):
    try: _token_path().write_text(token)
    except Exception: pass

def _clear_token():
    try: _token_path().unlink(missing_ok=True)
    except Exception: pass

def _save_last_world(world_id):
    try: (_token_path().parent / 'last_world.txt').write_text(str(world_id))
    except Exception: pass

def _load_last_world():
    try: return (_token_path().parent / 'last_world.txt').read_text().strip() or None
    except Exception: return None

class SFTApp:
    def __init__(self, page: ft.Page):
        self.page = page
        self.api = APIClient()
        self.save_paths = get_save_games_path()
        self.save_path = self.save_paths[0] if self.save_paths else None
        
        self.auto_refresh_running = False
        self._sync_direction = None
        self._cached_meta = None
        self._local_hash_cache = {}
        
        self.init_page_config()
        self.show_loading()
        
        # Background tasks
        threading.Thread(target=self.check_for_updates, daemon=True).start()
        threading.Thread(target=self._load_initial_data, daemon=True).start()

    def init_page_config(self):
        self.page.title = "Satisfactory Session Tracker"
        self.page.window_width = 460
        self.page.window_height = 700
        self.page.window_resizable = False
        self.page.window_title_bar_hidden = True
        self.page.window_title_bar_buttons_hidden = True
        self.page.bgcolor = ft.colors.TRANSPARENT
        self.page.window_bgcolor = ft.colors.TRANSPARENT
        self.page.padding = 0
        self.page.spacing = 0
        self.page.theme_mode = ft.ThemeMode.DARK
        self.page.update()

    def _build_glass_container(self, content, padding=20, expand=True):
        return ft.Container(
            content=content,
            padding=padding,
            expand=expand,
            border_radius=20,
            bgcolor="#1AFFFFFF", # Very transparent white
            blur=ft.Blur(15, 15, ft.BlurStyle.OUTER),
            border=ft.border.all(1, "#33FFFFFF"),
        )

    def show_loading(self):
        self.page.clean()
        self.page.add(
            ft.Stack([
                ft.Container(
                    expand=True,
                    gradient=ft.LinearGradient(
                        begin=ft.alignment.top_left,
                        end=ft.alignment.bottom_right,
                        colors=["#0f172a", "#1e293b", "#334155"]
                    )
                ),
                ft.Container(
                    content=ft.Column([
                        ft.Icon(ft.Icons.FACTORY_ROUNDED, size=64, color=ft.colors.ORANGE_400),
                        ft.ProgressRing(width=40, height=40, stroke_width=3, color=ft.colors.ORANGE_500),
                        ft.Text("INITIALIZING SYSTEM", size=10, letter_spacing=2, weight=ft.FontWeight.W_300, color=ft.colors.BLUE_GREY_300),
                    ], horizontal_alignment=ft.CrossAxisAlignment.CENTER, alignment=ft.MainAxisAlignment.CENTER, spacing=30),
                    alignment=ft.alignment.center,
                    expand=True
                )
            ], expand=True)
        )
        self.page.update()

    def _load_initial_data(self):
        saved_token = _load_token()
        if not saved_token:
            time.sleep(1) # Dramatic effect
            self.show_login_view()
            return
        
        self.api.token = saved_token
        self.api.session.headers.update({"Authorization": f"Bearer {saved_token}"})
        
        try:
            me = self.api.get_me()
            worlds = self.api.get_worlds()
            if not me:
                self.show_login_view()
                return
            self.show_main_view(me, worlds or [])
        except Exception:
            self.show_login_view()

    def _build_title_bar(self, title="SFT COMPANION"):
        return ft.Row([
            ft.WindowDragHandler(
                content=ft.Container(
                    content=ft.Row([
                        ft.Icon(ft.Icons.FACTORY_ROUNDED, size=18, color=ft.colors.ORANGE_500),
                        ft.Text(title, size=12, weight=ft.FontWeight.BOLD, color=ft.colors.BLUE_GREY_100),
                    ], spacing=10),
                    padding=ft.padding.only(left=20),
                    expand=True
                )
            ),
            ft.Row([
                ft.IconButton(ft.Icons.MINIMIZE_ROUNDED, icon_size=16, icon_color=ft.colors.BLUE_GREY_400, on_click=lambda _: self.page.window_minimize()),
                ft.IconButton(ft.Icons.CLOSE_ROUNDED, icon_size=16, icon_color=ft.colors.RED_400, on_click=lambda _: self.page.window_close()),
            ], spacing=0)
        ], height=40)

    def show_login_view(self):
        self.page.clean()
        
        token_field = ft.TextField(
            label="Connect Token",
            hint_text="Paste from /connect",
            border_radius=15,
            border_color="#44FFFFFF",
            bgcolor="#11FFFFFF",
            color=ft.colors.WHITE,
            text_size=14,
            width=300,
        )

        def do_login(e):
            token = token_field.value.strip() if token_field.value else ""
            if not token: return
            if self.api.login(token):
                _save_token(self.api.token)
                self._load_initial_data()
            else:
                self._snack("Invalid token", ft.colors.RED_400)

        login_card = self._build_glass_container(
            ft.Column([
                ft.Icon(ft.Icons.TELEGRAM, size=60, color=ft.colors.BLUE_400),
                ft.Text("LINK ACCOUNT", size=24, weight=ft.FontWeight.BOLD, color=ft.colors.WHITE),
                ft.Text("Use /connect command in Satisfactory Bot\nto obtain your secret access token.", 
                        text_align=ft.TextAlign.CENTER, color=ft.colors.BLUE_GREY_200, size=12),
                ft.Divider(height=20, color=ft.colors.TRANSPARENT),
                token_field,
                ft.ElevatedButton(
                    "AUTHORIZE",
                    icon=ft.Icons.LOCK_OPEN_ROUNDED,
                    color=ft.colors.WHITE,
                    bgcolor=ft.colors.ORANGE_700,
                    width=300,
                    height=50,
                    on_click=do_login,
                    style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=15))
                ),
                ft.TextButton("Need help?", on_click=lambda _: webbrowser.open("https://t.me/SatisfactoryTrackerBot")),
            ], horizontal_alignment=ft.CrossAxisAlignment.CENTER, alignment=ft.MainAxisAlignment.CENTER, spacing=20),
            expand=False, padding=40
        )

        self.page.add(
            ft.Stack([
                ft.Container(expand=True, gradient=ft.LinearGradient(colors=["#0f172a", "#1e293b"])),
                ft.Column([
                    self._build_title_bar("AUTHENTICATION"),
                    ft.Container(content=login_card, alignment=ft.alignment.center, expand=True)
                ], expand=True)
            ])
        )
        self.page.update()

    def show_main_view(self, me, worlds):
        self.page.clean()
        
        self.nav_rail = ft.NavigationRail(
            selected_index=0,
            label_type=ft.NavigationRailLabelType.ALL,
            min_width=100,
            min_extended_width=200,
            group_alignment=-0.9,
            bgcolor=ft.colors.TRANSPARENT,
            destinations=[
                ft.NavigationRailDestination(icon=ft.Icons.SYNC_ROUNDED, selected_icon=ft.Icons.SYNC_ROUNDED, label="Sync"),
                ft.NavigationRailDestination(icon=ft.Icons.SETTINGS_ROUNDED, selected_icon=ft.Icons.SETTINGS_ROUNDED, label="Config"),
            ],
            on_change=self.on_nav_change,
        )

        # Dashboard View
        self.world_dropdown = ft.Dropdown(
            label="ACTIVE WORLD",
            options=[ft.dropdown.Option(key=str(w["id"]), text=w["name"]) for w in worlds],
            value=_load_last_world() or (str(worlds[0]["id"]) if worlds else None),
            border_radius=15,
            border_color="#44FFFFFF",
            bgcolor="#11FFFFFF",
            on_change=self.on_world_change,
        )

        self.btn_sync = ft.Container(
            content=ft.Row([
                ft.Icon(ft.Icons.SYNC_ROUNDED, color=ft.colors.WHITE),
                ft.Text("SYNC NOW", weight=ft.FontWeight.BOLD, color=ft.colors.WHITE)
            ], alignment=ft.MainAxisAlignment.CENTER),
            width=200,
            height=55,
            border_radius=20,
            gradient=ft.LinearGradient(colors=[ft.colors.ORANGE_600, ft.colors.ORANGE_800]),
            on_click=self.on_sync,
            animate=ft.animation.Animation(300, ft.AnimationCurve.EASE_OUT),
            disabled=True,
            opacity=0.5
        )

        self.sync_msg = ft.Text("Checking state...", color=ft.colors.BLUE_GREY_400, size=12)
        self.local_card = self._build_stat_tile("LOCAL", "computer")
        self.server_card = self._build_stat_tile("SERVER", "cloud")

        self.dashboard_view = ft.Column([
            ft.Text("SYSTEM STATUS", size=10, letter_spacing=2, color=ft.colors.BLUE_GREY_400),
            ft.Divider(height=10, color=ft.colors.TRANSPARENT),
            self.world_dropdown,
            ft.Divider(height=20, color=ft.colors.TRANSPARENT),
            ft.Row([self.local_card, self.server_card], spacing=15),
            ft.Container(height=40),
            ft.Column([
                self.btn_sync,
                self.sync_msg,
            ], horizontal_alignment=ft.CrossAxisAlignment.CENTER, spacing=10),
        ], horizontal_alignment=ft.CrossAxisAlignment.CENTER)

        # Config View
        self.config_view = ft.Column([
            ft.Text("APPLICATION CONFIG", size=10, letter_spacing=2, color=ft.colors.BLUE_GREY_400),
            ft.Divider(height=10, color=ft.colors.TRANSPARENT),
            ft.ListTile(
                leading=ft.Icon(ft.Icons.FOLDER_OPEN_ROUNDED, color=ft.colors.BLUE_400),
                title=ft.Text("Save Folder", size=14),
                subtitle=ft.Text(self.save_path, size=10, color=ft.colors.BLUE_GREY_400),
                on_click=self.on_open_folder
            ),
            ft.ListTile(
                leading=ft.Icon(ft.Icons.LOGOUT_ROUNDED, color=ft.colors.RED_400),
                title=ft.Text("Logout", size=14, color=ft.colors.RED_400),
                on_click=self.logout
            ),
            ft.Container(expand=True),
            ft.Text(f"Client v{VERSION}", size=10, color=ft.colors.BLUE_GREY_600)
        ], visible=False)

        self.content_area = ft.Container(
            content=self.dashboard_view,
            expand=True,
            padding=ft.padding.only(top=20, left=20, right=20, bottom=40)
        )

        main_panel = self._build_glass_container(
            ft.Row([
                self.nav_rail,
                ft.VerticalDivider(width=1, color="#22FFFFFF"),
                self.content_area
            ], spacing=0)
        )

        self.page.add(
            ft.Stack([
                ft.Container(
                    expand=True, 
                    gradient=ft.LinearGradient(
                        begin=ft.alignment.top_left,
                        end=ft.alignment.bottom_right,
                        colors=["#0f172a", "#111827", "#1e1b4b"]
                    )
                ),
                ft.Column([
                    self._build_title_bar(),
                    ft.Container(content=main_panel, padding=20, expand=True)
                ], expand=True)
            ])
        )
        self.page.update()
        
        self.auto_refresh_running = True
        threading.Thread(target=self.auto_refresh_loop, daemon=True).start()

    def _build_stat_tile(self, label, icon_name):
        return ft.Container(
            content=ft.Column([
                ft.Row([ft.Icon(getattr(ft.Icons, f"{icon_name.upper()}_ROUNDED"), size=14, color=ft.colors.BLUE_300), 
                        ft.Text(label, size=9, weight=ft.FontWeight.BOLD, color=ft.colors.BLUE_GREY_300)]),
                ft.Text("-", size=11, color=ft.colors.WHITE, weight=ft.FontWeight.W_500, key="info"),
                ft.Text("NO DATA", size=9, color=ft.colors.BLUE_GREY_500, key="session")
            ], spacing=4),
            bgcolor="#0FFFFFFF",
            padding=15,
            border_radius=15,
            width=150,
            border=ft.border.all(1, "#1FFFFFFF")
        )

    def on_nav_change(self, e):
        idx = e.control.selected_index
        self.dashboard_view.visible = (idx == 0)
        self.config_view.visible = (idx == 1)
        if idx == 1 and self.config_view not in self.content_area.content.controls:
             # Need to add it if it's the first time
             self.content_area.content = ft.Stack([self.dashboard_view, self.config_view])
        self.page.update()

    def on_world_change(self, e):
        _save_last_world(self.world_dropdown.value)
        self.refresh_sync_state()

    def auto_refresh_loop(self):
        while self.auto_refresh_running:
            try: self.refresh_sync_state()
            except Exception: break
            time.sleep(5)

    def refresh_sync_state(self):
        if not self.world_dropdown.value: return
        
        # Local
        latest_local = get_latest_local_save(self.save_path)
        local_hash = None
        if latest_local:
            local_hash = get_file_hash(latest_local)
            session_name = get_session_name(latest_local)
            mtime = time.strftime('%H:%M', time.localtime(os.path.getmtime(latest_local)))
            self.local_card.content.controls[1].value = f"Updated {mtime}"
            self.local_card.content.controls[2].value = session_name[:18] or "Unknown"
        
        # Server
        meta = self.api.get_save_metadata(self.world_dropdown.value)
        self._cached_meta = meta
        server_hash = None
        if meta and meta.get("exists"):
            server_hash = meta.get("hash")
            updated_at = (meta.get("updated_at") or meta.get("created_at", "")).split()[-1][:5]
            self.server_card.content.controls[1].value = f"Cloud {updated_at}"
            self.server_card.content.controls[2].value = meta.get("session_name", "Unknown")[:18]

        # Button logic
        self.btn_sync.disabled = False
        self.btn_sync.opacity = 1.0
        
        if local_hash == server_hash and local_hash:
            self.sync_msg.value = "ALL SYSTEMS SYNCED"
            self.sync_msg.color = ft.colors.GREEN_400
            self.btn_sync.gradient = ft.LinearGradient(colors=["#065f46", "#064e3b"])
            self._sync_direction = "synced"
        elif local_hash and not server_hash:
            self.sync_msg.value = "READY TO INITIAL UPLOAD"
            self.btn_sync.gradient = ft.LinearGradient(colors=[ft.colors.ORANGE_600, ft.colors.ORANGE_800])
            self._sync_direction = "upload"
        else:
            self.sync_msg.value = "DATA MISMATCH DETECTED"
            self.sync_msg.color = ft.colors.AMBER_400
            self.btn_sync.gradient = ft.LinearGradient(colors=[ft.colors.BLUE_600, ft.colors.BLUE_800])
            self._sync_direction = "diff"

        self.page.update()

    def on_sync(self, e):
        if self._sync_direction == "synced":
            self._snack("Already up to date!")
            return
        
        # Simple directional logic for now: newer mtime wins, or ask if diff
        latest_local = get_latest_local_save(self.save_path)
        if not latest_local:
             self._confirm_and_download()
             return
             
        self._confirm_and_upload() # For now default to upload for user safety

    def _confirm_and_upload(self):
        latest_local = get_latest_local_save(self.save_path)
        self.btn_sync.disabled = True
        self.sync_msg.value = "UPLOADING..."
        self.page.update()
        
        res = self.api.upload_save(self.world_dropdown.value, latest_local)
        if res and res.get("status") == "ok":
            self._snack("Upload Success", ft.colors.GREEN_400)
        else:
            self._snack("Upload Failed", ft.colors.RED_400)
        self.refresh_sync_state()

    def _snack(self, text, color=ft.colors.WHITE):
        sb = ft.SnackBar(ft.Text(text, color=color), bgcolor="#33000000", blur=10)
        self.page.overlay.append(sb)
        sb.open = True
        self.page.update()

    def on_open_folder(self, e):
        if self.save_path: webbrowser.open(f"file://{self.save_path}")

    def logout(self, e):
        _clear_token()
        self.auto_refresh_running = False
        self.show_login_view()

    def check_for_updates(self):
        # Implementation from previous version
        pass

def main(page: ft.Page):
    app = SFTApp(page)

if __name__ == "__main__":
    ft.app(target=main)
