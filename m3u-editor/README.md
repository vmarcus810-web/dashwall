# Editor M3U

Junta várias listas M3U, separa **Canais**, **Filmes** e **Séries** e exporta uma lista única só com o que você marcar.

Requer apenas Python 3.10+ (o Tkinter já vem junto no Windows e no macOS; no Linux instale `python3-tk`).

```
python editor_m3u.py                     # abre vazio
python editor_m3u.py lista1.m3u lista2.m3u
```

- **Canais**: agrupados pelo grupo (`group-title`).
- **Filmes**: agrupados pelo título; o mesmo filme vindo de listas diferentes (HD, 4K, dublado…) fica junto.
- **Séries**: agrupadas por título → temporada → episódio (reconhece `S01E02`, `T01E02`, `1x02`, `Temporada 1 Episódio 2`).

A classificação usa a URL (`/movie/`, `/series/`), o nome e o grupo. Se algo cair na aba errada,
marque e use **Mover para Canais/Filmes/Séries**.
