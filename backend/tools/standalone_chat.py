import json
import os
import sys
import threading
from dataclasses import replace
from pathlib import Path
from uuid import uuid4

BACKEND_ROOT = Path(__file__).resolve().parents[1]
os.chdir(BACKEND_ROOT)
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

import tkinter as tk
from tkinter import messagebox, ttk
from tkinter.scrolledtext import ScrolledText

from stardew_backend.agent import StardewAgent
from stardew_backend.config import Settings


class AbigailTestChat:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("Stardew Agent Framework - Abigail Test Chat")
        self.root.geometry("1320x820")
        self.root.minsize(1060, 680)

        base_settings = Settings.from_env()
        test_data = BACKEND_ROOT / "data" / "test_runs"
        self.settings = replace(
            base_settings,
            memory_path=test_data / "standalone_memory.json",
            trace_path=test_data / "standalone_trace.jsonl",
        )
        self.agent = StardewAgent(self.settings)
        self.history: list[dict[str, str]] = []
        self.busy = False

        self.session_var = tk.StringVar(value="standalone:abigail")
        self.player_var = tk.StringVar(value="TestFarmer")
        self.hearts_var = tk.IntVar(value=4)
        self.relationship_var = tk.StringVar(value="friends")
        self.day_var = tk.IntVar(value=10)
        self.time_var = tk.IntVar(value=1200)
        self.season_var = tk.StringVar(value="spring")
        self.weather_var = tk.StringVar(value="sunny")
        self.location_var = tk.StringVar(value="Town")
        self.held_item_var = tk.StringVar(value="")
        self.status_var = tk.StringVar(value=self._status_text("就绪"))

        self._configure_style()
        self._build_ui()
        self._append_chat(
            "系统",
            "这是独立测试环境。它使用与游戏相同的 Agent、Lore、Memory 和 Policy，"
            "但记忆与 trace 写入 backend/data/test_runs，不会污染正式游戏数据。",
            "system",
        )

    def _configure_style(self) -> None:
        style = ttk.Style()
        if "vista" in style.theme_names():
            style.theme_use("vista")
        style.configure("Title.TLabel", font=("Segoe UI", 14, "bold"))
        style.configure("Subtle.TLabel", foreground="#5d6570")
        style.configure("Primary.TButton", font=("Segoe UI", 10, "bold"))

    def _build_ui(self) -> None:
        shell = ttk.Frame(self.root, padding=12)
        shell.pack(fill=tk.BOTH, expand=True)

        header = ttk.Frame(shell)
        header.pack(fill=tk.X, pady=(0, 10))
        ttk.Label(header, text="Abigail 独立对话测试台", style="Title.TLabel").pack(side=tk.LEFT)
        ttk.Button(header, text="新测试会话", command=self.new_session).pack(side=tk.RIGHT)
        ttk.Button(header, text="清空屏幕", command=self.clear_chat).pack(side=tk.RIGHT, padx=(0, 8))

        pane = ttk.Panedwindow(shell, orient=tk.HORIZONTAL)
        pane.pack(fill=tk.BOTH, expand=True)

        chat_frame = ttk.Frame(pane, padding=(0, 0, 10, 0))
        side_frame = ttk.Frame(pane)
        pane.add(chat_frame, weight=3)
        pane.add(side_frame, weight=2)

        self.chat_view = ScrolledText(
            chat_frame,
            wrap=tk.WORD,
            state=tk.DISABLED,
            font=("Microsoft YaHei UI", 11),
            padx=12,
            pady=10,
            background="#fbfbfa",
        )
        self.chat_view.pack(fill=tk.BOTH, expand=True)
        self.chat_view.tag_configure("player_name", foreground="#2563a6", font=("Microsoft YaHei UI", 10, "bold"))
        self.chat_view.tag_configure("npc_name", foreground="#7b3fa1", font=("Microsoft YaHei UI", 10, "bold"))
        self.chat_view.tag_configure("system_name", foreground="#68717c", font=("Microsoft YaHei UI", 10, "bold"))
        self.chat_view.tag_configure("body", spacing3=10)

        input_label = ttk.Label(chat_frame, text="玩家输入（Ctrl+Enter 发送）")
        input_label.pack(anchor=tk.W, pady=(10, 4))
        self.input_box = tk.Text(
            chat_frame,
            height=5,
            wrap=tk.WORD,
            font=("Microsoft YaHei UI", 11),
            padx=8,
            pady=8,
        )
        self.input_box.pack(fill=tk.X)
        self.input_box.bind("<Control-Return>", self._send_shortcut)

        input_actions = ttk.Frame(chat_frame)
        input_actions.pack(fill=tk.X, pady=(8, 0))
        ttk.Label(input_actions, textvariable=self.status_var, style="Subtle.TLabel").pack(side=tk.LEFT)
        self.send_button = ttk.Button(
            input_actions,
            text="发送",
            style="Primary.TButton",
            command=self.send,
        )
        self.send_button.pack(side=tk.RIGHT)

        notebook = ttk.Notebook(side_frame)
        notebook.pack(fill=tk.BOTH, expand=True)
        context_tab = ttk.Frame(notebook, padding=12)
        diagnostics_tab = ttk.Frame(notebook, padding=8)
        notebook.add(context_tab, text="模拟上下文")
        notebook.add(diagnostics_tab, text="本轮诊断")
        self._build_context_form(context_tab)

        self.diagnostics = ScrolledText(
            diagnostics_tab,
            wrap=tk.WORD,
            state=tk.DISABLED,
            font=("Consolas", 9),
            padx=8,
            pady=8,
        )
        self.diagnostics.pack(fill=tk.BOTH, expand=True)

    def _build_context_form(self, parent: ttk.Frame) -> None:
        fields = [
            ("测试 Session", self.session_var, None),
            ("玩家名", self.player_var, None),
            ("关系状态", self.relationship_var, ("friends", "dating", "engaged", "married")),
            ("心数", self.hearts_var, None),
            ("游戏总日", self.day_var, None),
            ("时间", self.time_var, None),
            ("季节", self.season_var, ("spring", "summer", "fall", "winter")),
            ("天气", self.weather_var, ("sunny", "rainy")),
            ("地点", self.location_var, ("Town", "Mountain", "Mine", "SeedShop", "Farm")),
            ("手持物品", self.held_item_var, None),
        ]
        for row, (label, variable, choices) in enumerate(fields):
            ttk.Label(parent, text=label).grid(row=row, column=0, sticky=tk.W, pady=5)
            if choices:
                widget = ttk.Combobox(parent, textvariable=variable, values=choices, state="readonly")
            elif isinstance(variable, tk.IntVar):
                widget = ttk.Spinbox(parent, textvariable=variable, from_=0, to=99999)
            else:
                widget = ttk.Entry(parent, textvariable=variable)
            widget.grid(row=row, column=1, sticky=tk.EW, padx=(12, 0), pady=5)

        parent.columnconfigure(1, weight=1)
        separator = ttk.Separator(parent)
        separator.grid(row=len(fields), column=0, columnspan=2, sticky=tk.EW, pady=14)
        ttk.Label(
            parent,
            text=(
                "修改这里可以模拟不同关系、天气和地点。新测试会话只更换 session，"
                "不会删除旧测试数据；重新使用同一 session 可以验证跨重启记忆。"
            ),
            wraplength=360,
            justify=tk.LEFT,
            style="Subtle.TLabel",
        ).grid(row=len(fields) + 1, column=0, columnspan=2, sticky=tk.W)
        ttk.Button(
            parent,
            text="打开测试数据目录",
            command=lambda: os.startfile(BACKEND_ROOT / "data" / "test_runs"),
        ).grid(row=len(fields) + 2, column=0, columnspan=2, sticky=tk.EW, pady=(16, 0))

    def _status_text(self, state: str) -> str:
        backend = self.settings.llm_backend if hasattr(self, "settings") else "loading"
        model = self.settings.llm_model if hasattr(self, "settings") else ""
        return f"{state} | {backend} | {model}"

    def _game_state(self) -> dict:
        game_day = max(1, int(self.day_var.get()))
        zero_based = game_day - 1
        season_index = (zero_based % 112) // 28
        day_of_month = zero_based % 28 + 1
        year = zero_based // 112 + 1
        selected_season = self.season_var.get() or ["spring", "summer", "fall", "winter"][season_index]
        is_raining = self.weather_var.get() == "rainy"
        hearts = max(0, int(self.hearts_var.get()))
        return {
            "source": "standalone_test_harness",
            "npc": {"name": "Abigail", "display_name": "Abigail"},
            "npc_perception": {
                "schemaVersion": "npc-perception-0.1",
                "time": {
                    "year": year,
                    "season": selected_season,
                    "dayOfMonth": day_of_month,
                    "timeOfDay": max(0, int(self.time_var.get())),
                },
                "weather": {"isRaining": is_raining},
                "location": {"name": self.location_var.get() or "Town"},
                "player": {
                    "name": self.player_var.get() or "TestFarmer",
                    "hearts": hearts,
                    "friendshipPoints": hearts * 250,
                    "relationshipStatus": self.relationship_var.get() or "friends",
                    "heldItem": self.held_item_var.get(),
                },
                "nearby": {"npcs": [], "objects": [], "crops": [], "monsters": []},
            },
        }

    def _send_shortcut(self, _event: tk.Event) -> str:
        self.send()
        return "break"

    def send(self) -> None:
        if self.busy:
            return
        player_input = self.input_box.get("1.0", tk.END).strip()
        if not player_input:
            return
        session_id = self.session_var.get().strip()
        if not session_id:
            messagebox.showwarning("缺少 Session", "请填写测试 Session。")
            return

        self.input_box.delete("1.0", tk.END)
        self._append_chat(self.player_var.get() or "玩家", player_input, "player")
        self.busy = True
        self.send_button.configure(state=tk.DISABLED)
        self.status_var.set(self._status_text("正在请求"))
        state = self._game_state()
        history = list(self.history)
        threading.Thread(
            target=self._chat_worker,
            args=(player_input, session_id, state, history),
            daemon=True,
        ).start()

    def _chat_worker(self, player_input: str, session_id: str, state: dict, history: list[dict[str, str]]) -> None:
        try:
            result = self.agent.chat(
                player_input=player_input,
                game_state=state,
                session_id=session_id,
                conversation_history=history,
                debug=True,
            )
            self.root.after(0, lambda: self._finish_chat(player_input, state, result))
        except Exception as exc:
            self.root.after(0, lambda: self._finish_error(exc))

    def _finish_chat(self, player_input: str, state: dict, result: dict) -> None:
        reply = str(result.get("reply", ""))
        emotion = str(result.get("emotion", "neutral"))
        self._append_chat(f"Abigail [{emotion}]", reply, "npc")
        self.history.extend([
            {"role": "user", "content": player_input},
            {"role": "assistant", "content": reply},
        ])
        self.history = self.history[-12:]
        diagnostic = {
            "turn_id": result.get("turn_id"),
            "context": state,
            "retrieved_lore": result.get("retrieved_lore", []),
            "retrieved_memory": result.get("retrieved_memory", []),
            "saved_memories": result.get("saved_memories", []),
            "relationship_effect": result.get("relationship_effect", {}),
            "action_proposal": result.get("action_proposal"),
            "debug_prompt": result.get("debug_prompt"),
        }
        self._set_diagnostics(diagnostic)
        self.busy = False
        self.send_button.configure(state=tk.NORMAL)
        self.status_var.set(self._status_text("完成"))
        self.input_box.focus_set()

    def _finish_error(self, exc: Exception) -> None:
        self.busy = False
        self.send_button.configure(state=tk.NORMAL)
        self.status_var.set(self._status_text("失败"))
        messagebox.showerror("对话请求失败", str(exc))

    def _append_chat(self, name: str, body: str, kind: str) -> None:
        self.chat_view.configure(state=tk.NORMAL)
        tag = f"{kind}_name" if kind in {"player", "npc", "system"} else "system_name"
        self.chat_view.insert(tk.END, f"{name}\n", tag)
        self.chat_view.insert(tk.END, f"{body}\n\n", "body")
        self.chat_view.configure(state=tk.DISABLED)
        self.chat_view.see(tk.END)

    def _set_diagnostics(self, value: dict) -> None:
        self.diagnostics.configure(state=tk.NORMAL)
        self.diagnostics.delete("1.0", tk.END)
        self.diagnostics.insert(tk.END, json.dumps(value, ensure_ascii=False, indent=2))
        self.diagnostics.configure(state=tk.DISABLED)

    def clear_chat(self) -> None:
        self.history.clear()
        self.chat_view.configure(state=tk.NORMAL)
        self.chat_view.delete("1.0", tk.END)
        self.chat_view.configure(state=tk.DISABLED)
        self._set_diagnostics({})

    def new_session(self) -> None:
        self.clear_chat()
        self.session_var.set(f"standalone:{uuid4().hex[:8]}:Abigail")
        self._append_chat("系统", "已创建新的隔离测试会话。", "system")


def main() -> int:
    root = tk.Tk()
    AbigailTestChat(root)
    root.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
