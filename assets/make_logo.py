"""Gera o logo do VideoWall (PNG e ICO) a partir das mesmas formas do logo.svg.

Uso: python assets/make_logo.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).parent
S = 1024  # tamanho de trabalho (super-amostragem)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def draw_logo(size: int) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Fundo: quadrado arredondado com gradiente diagonal
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    top, bottom = (30, 41, 59), (15, 23, 42)
    for y in range(S):
        gd.line([(0, y), (S, y)], fill=lerp(top, bottom, y / S) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=220, fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    cyan, indigo, violet = (34, 211, 238), (99, 102, 241), (167, 139, 250)

    # Grade 2x2 de monitores: linha de cima mesclada (um dashboard em 2 telas)
    m, g = 150, 36
    cell = (S - 2 * m - g) // 2
    big = [m, m, S - m, m + cell]
    left = [m, m + cell + g, m + cell, S - m]
    right = [m + cell + g, m + cell + g, S - m, S - m]

    for box, color in ((big, cyan), (left, indigo), (right, violet)):
        d.rounded_rectangle(box, radius=48, fill=(37, 48, 68, 255), outline=color + (255,), width=22)

    # Barras no painel grande
    bx, by = big[0] + 80, big[3] - 60
    heights = [90, 160, 120, 210, 150, 240]
    bw = 58
    for i, h in enumerate(heights):
        x = bx + i * (bw + 30)
        d.rounded_rectangle([x, by - h, x + bw, by], radius=14, fill=cyan + (255,))

    # Linha no painel esquerdo
    pts = [(left[0] + 60, left[3] - 70), (left[0] + 130, left[3] - 150),
           (left[0] + 190, left[3] - 110), (left[2] - 60, left[1] + 70)]
    d.line(pts, fill=indigo + (255,), width=26, joint="curve")

    # Ponto "ao vivo" no painel direito
    cx, cy = (right[0] + right[2]) // 2, (right[1] + right[3]) // 2
    d.ellipse([cx - 70, cy - 70, cx + 70, cy + 70], outline=violet + (255,), width=22)
    d.ellipse([cx - 30, cy - 30, cx + 30, cy + 30], fill=violet + (255,))

    return img.resize((size, size), Image.LANCZOS)


def main():
    logo = draw_logo(512)
    logo.save(HERE / "logo.png")
    draw_logo(256).save(
        HERE / "logo.ico",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print("logo.png e logo.ico gerados em", HERE)


if __name__ == "__main__":
    main()
