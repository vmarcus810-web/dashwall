"""Leitura, classificação (canal / filme / série) e gravação de listas M3U."""
from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass, field

CHANNEL, MOVIE, SERIES = "channel", "movie", "series"

ATTR_RE = re.compile(r'([\w-]+)="([^"]*)"')
_SEP = r"[\s._\-–|:]*"
EPISODE_PATTERNS = [
    # Nome S01E02 / Nome T01E02 / Nome S01 EP02
    re.compile(rf"^(?P<title>.*?){_SEP}\b[ST](?P<season>\d{{1,2}})\s*[._\-]?\s*EP?(?P<episode>\d{{1,4}})\b", re.I),
    # Nome 1x02
    re.compile(rf"^(?P<title>.*?){_SEP}\b(?P<season>\d{{1,2}})x(?P<episode>\d{{1,4}})\b", re.I),
    # Nome Temporada 1 Episódio 2
    re.compile(rf"^(?P<title>.*?){_SEP}\bTemporada\s*(?P<season>\d{{1,2}})\D{{0,8}}Epis[oó]dio\s*(?P<episode>\d{{1,4}})", re.I),
]
# Marcas de qualidade/idioma que não fazem parte do título
TAG_RE = re.compile(
    r"\[[^\]]*\]|\((?:dub|leg|dual|4k|uhd|fhd|hd|sd)[^)]*\)"
    r"|\b(?:4k|uhd|fhd|hd|sd|h\.?265|hevc|dublado|legendado|dual|dub|leg|nacional)\b",
    re.I,
)
SERIES_WORDS = ("serie", "novela", "temporada", "anime", "dorama")
MOVIE_WORDS = ("filme", "movie", "vod", "cinema", "lancamento")
VIDEO_EXT = (".mp4", ".mkv", ".avi", ".mov", ".wmv")


def normalize(text: str) -> str:
    text = unicodedata.normalize("NFKD", text)
    return "".join(c for c in text if not unicodedata.combining(c)).lower()


def title_key(title: str) -> str:
    """Chave para juntar o mesmo título vindo de listas diferentes."""
    return re.sub(r"[^a-z0-9]+", "", normalize(title))


def clean_title(name: str) -> str:
    title = TAG_RE.sub(" ", name)
    title = re.sub(r"\s+", " ", title)
    return re.sub(r"^[\s._\-–|:]+|[\s._\-–|:]+$", "", title) or name.strip()


@dataclass
class Entry:
    id: int
    source: str
    name: str
    group: str
    url: str
    duration: str = "-1"
    attrs: dict = field(default_factory=dict)
    extra: list = field(default_factory=list)  # #EXTVLCOPT, #KODIPROP...
    kind: str = CHANNEL
    title: str = ""
    season: int | None = None
    episode: int | None = None
    selected: bool = False

    @property
    def search_text(self) -> str:
        return normalize(f"{self.name} {self.group} {self.title} {self.url}")


def classify(e: Entry) -> None:
    """Define kind, title, season e episode a partir da URL, do nome e do grupo."""
    url = e.url.lower().split("?")[0]
    group = normalize(e.group)
    for pattern in EPISODE_PATTERNS:
        m = pattern.match(e.name)
        if m and m.group("title").strip():
            e.kind = SERIES
            e.title = clean_title(m.group("title"))
            e.season, e.episode = int(m.group("season")), int(m.group("episode"))
            return
    if "/series/" in url or any(w in group for w in SERIES_WORDS):
        e.kind = SERIES
    elif "/movie/" in url or any(w in group for w in MOVIE_WORDS) or url.endswith(VIDEO_EXT):
        e.kind = MOVIE
    else:
        e.kind = CHANNEL
    e.title = clean_title(e.name) if e.kind != CHANNEL else e.name


def parse(text: str, source: str, start_id: int = 1) -> tuple[str, list[Entry]]:
    """Retorna (url do EPG, entradas)."""
    epg = ""
    entries: list[Entry] = []
    info: dict | None = None
    extra: list[str] = []
    for raw in text.lstrip("﻿").splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith("#EXTM3U"):
            attrs = dict(ATTR_RE.findall(line))
            epg = attrs.get("url-tvg") or attrs.get("x-tvg-url") or ""
        elif line.startswith("#EXTINF"):
            body = line.split(":", 1)[1] if ":" in line else ""
            # A vírgula do nome fica depois do último atributo entre aspas
            comma = body.find(",", body.rfind('"') + 1)
            head = body[:comma] if comma >= 0 else body
            attrs = dict(ATTR_RE.findall(head))
            m = re.match(r"\s*(-?\d+)", head)
            info = {
                "duration": m.group(1) if m else "-1",
                "attrs": attrs,
                "name": (body[comma + 1:].strip() if comma >= 0 else "") or attrs.get("tvg-name", "") or "Sem nome",
                "group": attrs.get("group-title", ""),
            }
        elif line.startswith("#EXTGRP:"):
            if info is not None and not info["group"]:
                info["group"] = line[8:].strip()
        elif line.startswith("#"):
            extra.append(line)
        else:
            info = info or {"duration": "-1", "attrs": {}, "name": line.rsplit("/", 1)[-1], "group": ""}
            e = Entry(id=start_id + len(entries), source=source, url=line, extra=extra,
                      name=info["name"], group=info["group"] or "Sem grupo",
                      duration=info["duration"], attrs=info["attrs"])
            classify(e)
            entries.append(e)
            info, extra = None, []
    return epg, entries


def sort_key(e: Entry):
    order = {CHANNEL: 0, MOVIE: 1, SERIES: 2}[e.kind]
    if e.kind == CHANNEL:
        return (order, "", 0, 0)
    return (order, title_key(e.title), e.season or 0, e.episode or 0)


def write(entries: list[Entry], epgs: list[str], dedupe: bool = True, series_group_by_title: bool = False) -> str:
    """Gera o texto M3U: canais (ordem original dos grupos), filmes e séries por título/temporada/episódio."""
    if dedupe:
        seen: set[str] = set()
        entries = [e for e in entries if not (e.url in seen or seen.add(e.url))]
    group_order: dict[str, int] = {}
    for e in entries:
        group_order.setdefault(e.group, len(group_order))
    entries = sorted(entries, key=lambda e: (sort_key(e), group_order[e.group] if e.kind == CHANNEL else 0))

    lines = [f'#EXTM3U url-tvg="{",".join(epgs)}"' if epgs else "#EXTM3U"]
    for e in entries:
        attrs = dict(e.attrs)
        attrs["group-title"] = e.title if (e.kind == SERIES and series_group_by_title) else e.group
        attrs.setdefault("tvg-name", e.name)
        attr_text = " ".join(f'{k}="{str(v).replace(chr(34), chr(39))}"' for k, v in attrs.items())
        lines.append(f"#EXTINF:{e.duration} {attr_text},{e.name}")
        lines.extend(e.extra)
        lines.append(e.url)
    return "\n".join(lines) + "\n"
