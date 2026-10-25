"""Editor M3U: junte várias listas, escolha canais, filmes e episódios e exporte uma lista única.

Uso: python editor_m3u.py [lista1.m3u lista2.m3u ...]
"""
from __future__ import annotations

import sys
import tkinter as tk
import urllib.request
from pathlib import Path
from tkinter import filedialog, messagebox, simpledialog, ttk

import m3u
from m3u import CHANNEL, MOVIE, SERIES

TABS = [(CHANNEL, "Canais"), (MOVIE, "Filmes"), (SERIES, "Séries")]
MARK = {True: "☑", False: "☐", None: "◩"}


class Node:
    """Um nó da árvore (grupo, título, temporada ou entrada)."""

    def __init__(self, label: str, parent: str = "", entry: int | None = None):
        self.label = label
        self.parent = parent
        self.entry = entry
        self.entries: list[int] = []   # todas as entradas abaixo deste nó
        self.children: list[str] = []
        self.loaded = False


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Editor M3U")
        self.geometry("1150x720")
        self.entries: dict[int, m3u.Entry] = {}
        self.sources: list[dict] = []  # {"name", "epg"}
        self.nodes: dict[str, Node] = {}
        self.next_id = 1
        self._search_job = None
        self._build_ui()

    # ---------- interface ----------
    def _build_ui(self):
        top = ttk.Frame(self, padding=6)
        top.pack(fill="x")
        ttk.Button(top, text="📂 Abrir arquivos", command=self.open_files).pack(side="left")
        self.url_var = tk.StringVar()
        ttk.Entry(top, textvariable=self.url_var, width=45).pack(side="left", padx=(8, 2))
        ttk.Button(top, text="Carregar URL", command=self.load_url).pack(side="left")
        ttk.Button(top, text="⬇ Exportar lista unificada", command=self.export).pack(side="right")
        self.count_var = tk.StringVar()
        ttk.Label(top, textvariable=self.count_var).pack(side="right", padx=10)

        body = ttk.PanedWindow(self, orient="horizontal")
        body.pack(fill="both", expand=True, padx=6, pady=(0, 6))

        side = ttk.Frame(body, padding=4)
        body.add(side, weight=0)
        ttk.Label(side, text="Listas carregadas").pack(anchor="w")
        self.src_list = tk.Listbox(side, width=32, height=10, activestyle="none")
        self.src_list.pack(fill="x")
        ttk.Button(side, text="Remover lista", command=self.remove_source).pack(fill="x", pady=2)

        ttk.Label(side, text="Exportação").pack(anchor="w", pady=(14, 0))
        self.dedupe = tk.BooleanVar(value=True)
        self.series_by_title = tk.BooleanVar(value=False)
        ttk.Checkbutton(side, text="Remover URLs duplicadas", variable=self.dedupe).pack(anchor="w")
        ttk.Checkbutton(side, text="Grupo das séries = título da série", variable=self.series_by_title).pack(anchor="w")

        ttk.Label(side, text="Selecionados").pack(anchor="w", pady=(14, 0))
        ttk.Button(side, text="Renomear grupo…", command=self.rename_group).pack(fill="x", pady=1)
        for kind, label in TABS:
            ttk.Button(side, text=f"Mover para {label}", command=lambda k=kind: self.move_kind(k)).pack(fill="x", pady=1)
        ttk.Label(side, wraplength=220, foreground="gray", justify="left", text=(
            "Clique no nome para marcar/desmarcar (marcar um grupo, título ou temporada marca tudo dentro). "
            "Espaço marca as linhas selecionadas. Duplo clique numa entrada edita o nome.")).pack(anchor="w", pady=(14, 0))

        main = ttk.Frame(body)
        body.add(main, weight=1)
        bar = ttk.Frame(main)
        bar.pack(fill="x", pady=(0, 4))
        ttk.Label(bar, text="Buscar:").pack(side="left")
        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", lambda *_: self._schedule_rebuild())
        ttk.Entry(bar, textvariable=self.search_var).pack(side="left", fill="x", expand=True, padx=4)
        self.only_sel = tk.BooleanVar()
        ttk.Checkbutton(bar, text="Só selecionados", variable=self.only_sel, command=self.rebuild).pack(side="left")
        ttk.Button(bar, text="Marcar tudo da aba", command=lambda: self.mark_tab(True)).pack(side="left", padx=2)
        ttk.Button(bar, text="Desmarcar tudo da aba", command=lambda: self.mark_tab(False)).pack(side="left")

        self.tabs = ttk.Notebook(main)
        self.tabs.pack(fill="both", expand=True)
        self.trees: dict[str, ttk.Treeview] = {}
        for kind, label in TABS:
            frame = ttk.Frame(self.tabs)
            tree = ttk.Treeview(frame, columns=("source", "group"), selectmode="extended")
            tree.heading("#0", text="Nome")
            tree.heading("source", text="Lista de origem")
            tree.heading("group", text="Grupo original")
            tree.column("#0", width=480)
            tree.column("source", width=160)
            tree.column("group", width=180)
            scroll = ttk.Scrollbar(frame, orient="vertical", command=tree.yview)
            tree.configure(yscrollcommand=scroll.set)
            tree.pack(side="left", fill="both", expand=True)
            scroll.pack(side="right", fill="y")
            tree.bind("<<TreeviewOpen>>", lambda ev, t=tree: self._load_children(t, t.focus()))
            tree.bind("<Button-1>", lambda ev, t=tree: self._on_click(t, ev))
            tree.bind("<space>", lambda ev, t=tree: self._toggle_many(t, t.selection()))
            tree.bind("<Double-1>", lambda ev, t=tree: self._edit_name(t, ev))
            self.tabs.add(frame, text=label)
            self.trees[kind] = tree

    # ---------- carregar ----------
    def add_text(self, text: str, name: str):
        base, n = name, 2
        while any(s["name"] == name for s in self.sources):  # nomes únicos: a origem identifica a lista
            name, n = f"{base} ({n})", n + 1
        epg, entries = m3u.parse(text, name, self.next_id)
        self.next_id += len(entries) + 1
        self.sources.append({"name": name, "epg": epg})
        self.entries.update((e.id, e) for e in entries)
        self.refresh_sources()
        self.rebuild()

    def open_files(self):
        for path in filedialog.askopenfilenames(filetypes=[("Listas M3U", "*.m3u *.m3u8"), ("Todos", "*.*")]):
            self.load_path(path)

    def load_path(self, path: str):
        try:
            self.add_text(Path(path).read_text(encoding="utf-8", errors="replace"), Path(path).name)
        except OSError as exc:
            messagebox.showerror("Erro", f"Não foi possível abrir {path}:\n{exc}")

    def load_url(self):
        url = self.url_var.get().strip()
        if not url:
            return
        self.config(cursor="watch")
        self.update()
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "VLC/3.0"})
            with urllib.request.urlopen(req, timeout=60) as resp:
                text = resp.read().decode("utf-8", errors="replace")
            self.add_text(text, url.rstrip("/").rsplit("/", 1)[-1][:60] or url)
            self.url_var.set("")
        except Exception as exc:  # noqa: BLE001 - mostrar qualquer falha de rede
            messagebox.showerror("Erro", f"Não foi possível baixar a lista:\n{exc}")
        finally:
            self.config(cursor="")

    def refresh_sources(self):
        self.src_list.delete(0, "end")
        for s in self.sources:
            n = sum(1 for e in self.entries.values() if e.source == s["name"])
            self.src_list.insert("end", f"{s['name']} ({n})")

    def remove_source(self):
        sel = self.src_list.curselection()
        if not sel:
            return
        name = self.sources.pop(sel[0])["name"]
        self.entries = {i: e for i, e in self.entries.items() if e.source != name}
        self.refresh_sources()
        self.rebuild()

    # ---------- árvore ----------
    def _schedule_rebuild(self):
        if self._search_job:
            self.after_cancel(self._search_job)
        self._search_job = self.after(300, self.rebuild)

    def _path(self, e: m3u.Entry) -> list[tuple[str, str]]:
        """Caminho (chave, rótulo) do topo da árvore até a entrada."""
        if e.kind == CHANNEL:
            return [(e.group, e.group)]
        if e.kind == MOVIE:
            return [(m3u.title_key(e.title), e.title)]
        season = f"Temporada {e.season:02d}" if e.season is not None else "Sem temporada"
        return [(m3u.title_key(e.title), e.title), (season, season)]

    def _leaf_label(self, e: m3u.Entry) -> str:
        if e.kind == SERIES and e.episode is not None:
            return f"E{e.episode:02d} · {e.name}"
        return e.name

    def rebuild(self):
        q = m3u.normalize(self.search_var.get().strip())
        only_sel = self.only_sel.get()
        self.nodes.clear()
        tops: dict[str, list[str]] = {k: [] for k, _ in TABS}
        visible = [e for e in self.entries.values()
                   if (not q or q in e.search_text) and (not only_sel or e.selected)]
        visible.sort(key=lambda e: (m3u.sort_key(e), e.name.lower() if e.kind != CHANNEL else ""))
        for e in visible:
            parent = ""
            for key, label in self._path(e):
                iid = f"{parent}\x1f{key}" if parent else f"{e.kind}\x1f{key}"
                node = self.nodes.get(iid)
                if node is None:
                    node = self.nodes[iid] = Node(label, parent)
                    (self.nodes[parent].children if parent else tops[e.kind]).append(iid)
                node.entries.append(e.id)
                parent = iid
            leaf = f"e{e.id}"
            self.nodes[leaf] = Node(self._leaf_label(e), parent, e.id)
            self.nodes[leaf].entries.append(e.id)
            self.nodes[parent].children.append(leaf)

        for kind, label in TABS:
            tree = self.trees[kind]
            tree.delete(*tree.get_children())
            for iid in tops[kind]:
                self._insert(tree, "", iid)
            n = sum(len(self.nodes[i].entries) for i in tops[kind])
            self.tabs.tab(self.trees[kind].master, text=f"{label} ({len(tops[kind])} · {n})")
        self.update_count()

    def _insert(self, tree: ttk.Treeview, parent: str, iid: str):
        node = self.nodes[iid]
        values = ("", "")
        if node.entry is not None:
            e = self.entries[node.entry]
            values = (e.source, e.group)
        tree.insert(parent, "end", iid=iid, text=self._text(iid), values=values)
        if node.children:
            tree.insert(iid, "end", iid=iid + "\x1e")  # filho fictício para mostrar a seta

    def _load_children(self, tree: ttk.Treeview, iid: str):
        node = self.nodes.get(iid)
        if not node or node.loaded:
            return
        node.loaded = True
        tree.delete(iid + "\x1e")
        for child in node.children:
            self._insert(tree, iid, child)

    def _state(self, iid: str) -> bool | None:
        ids = self.nodes[iid].entries
        n = sum(1 for i in ids if self.entries[i].selected)
        return True if n == len(ids) else False if n == 0 else None

    def _text(self, iid: str) -> str:
        node = self.nodes[iid]
        suffix = "" if node.entry is not None else f"   ({len(node.entries)})"
        return f"{MARK[self._state(iid)]} {node.label}{suffix}"

    def _refresh(self, tree: ttk.Treeview, iids):
        """Atualiza a marca dos nós alterados, dos filhos já carregados e dos pais."""
        todo, seen = list(iids), set()
        while todo:
            iid = todo.pop()
            if iid in seen or not tree.exists(iid):
                continue
            seen.add(iid)
            tree.item(iid, text=self._text(iid))
            if self.nodes[iid].loaded:
                todo.extend(self.nodes[iid].children)
        for iid in iids:
            parent = self.nodes[iid].parent
            while parent:
                tree.item(parent, text=self._text(parent))
                parent = self.nodes[parent].parent
        self.update_count()

    def _on_click(self, tree: ttk.Treeview, ev):
        iid = tree.identify_row(ev.y)
        element = tree.identify_element(ev.x, ev.y)
        if iid in self.nodes and tree.identify_column(ev.x) == "#0" and "indicator" not in element:
            self._toggle_many(tree, (iid,))
            return "break"

    def _toggle_many(self, tree: ttk.Treeview, iids):
        iids = [i for i in iids if i in self.nodes]
        if not iids:
            return "break"
        ids = {e for i in iids for e in self.nodes[i].entries}
        value = not all(self.entries[i].selected for i in ids)
        for i in ids:
            self.entries[i].selected = value
        self._refresh(tree, iids)
        return "break"

    def _edit_name(self, tree: ttk.Treeview, ev):
        iid = tree.identify_row(ev.y)
        node = self.nodes.get(iid)
        if not node or node.entry is None:
            return
        e = self.entries[node.entry]
        name = simpledialog.askstring("Editar nome", "Nome da entrada:", initialvalue=e.name, parent=self)
        if name and name.strip():
            e.name = name.strip()
            node.label = self._leaf_label(e)
            tree.item(iid, text=self._text(iid))

    def mark_tab(self, value: bool):
        kind = TABS[self.tabs.index("current")][0]
        tree = self.trees[kind]
        tops = tree.get_children()
        for iid in tops:
            for i in self.nodes[iid].entries:
                self.entries[i].selected = value
        self._refresh(tree, tops)

    def update_count(self):
        sel = sum(1 for e in self.entries.values() if e.selected)
        self.count_var.set(f"{sel} selecionados de {len(self.entries)}")

    # ---------- ações ----------
    def _selected(self) -> list[m3u.Entry]:
        return [e for e in self.entries.values() if e.selected]

    def rename_group(self):
        sel = self._selected()
        if not sel:
            return messagebox.showinfo("Renomear grupo", "Marque ao menos uma entrada.")
        group = simpledialog.askstring("Renomear grupo", f"Novo grupo para {len(sel)} entradas:", parent=self)
        if group and group.strip():
            for e in sel:
                e.group = group.strip()
            self.rebuild()

    def move_kind(self, kind: str):
        sel = self._selected()
        if not sel:
            return messagebox.showinfo("Mover", "Marque ao menos uma entrada.")
        for e in sel:
            e.kind = kind
            if kind == CHANNEL:
                e.title = e.name
            elif not e.title or e.title == e.name:
                e.title = m3u.clean_title(e.name)
        self.rebuild()

    def export(self):
        sel = self._selected()
        if not sel:
            return messagebox.showinfo("Exportar", "Marque ao menos uma entrada.")
        path = filedialog.asksaveasfilename(defaultextension=".m3u", initialfile="lista-unificada.m3u",
                                            filetypes=[("Lista M3U", "*.m3u"), ("Lista M3U8", "*.m3u8")])
        if not path:
            return
        used = {e.source for e in sel}
        epgs = list(dict.fromkeys(s["epg"] for s in self.sources if s["name"] in used and s["epg"]))
        text = m3u.write(sel, epgs, self.dedupe.get(), self.series_by_title.get())
        Path(path).write_text(text, encoding="utf-8")
        total = text.count("#EXTINF")
        messagebox.showinfo("Exportar", f"Lista salva com {total} entradas:\n{path}")


if __name__ == "__main__":
    app = App()
    for arg in sys.argv[1:]:
        app.load_path(arg)
    app.mainloop()
