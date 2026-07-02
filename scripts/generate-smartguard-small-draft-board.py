from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
OUTPUT_DIR = ROOT / "dist" / "icon-previews" / "aggressive-small-drafts"

OUTER_CYAN = (84, 211, 242, 255)
PLATE_CYAN = (72, 162, 210, 255)
NAVY = (23, 53, 91, 255)
NAVY_SOFT = (36, 72, 112, 255)
WHITE = (244, 249, 252, 255)
LIME = (230, 255, 114, 255)
BOARD_BG = (250, 252, 255, 255)
CARD_BG = (255, 255, 255, 255)
CARD_BORDER = (223, 232, 242, 255)
TEXT = (33, 44, 64, 255)
SUBTEXT = (93, 107, 128, 255)

SIZES = (16, 24, 32)


def scale_small(value: float, size: int) -> int:
    return int(round(value * size / 32.0))


def scale_points(points: list[tuple[float, float]], size: int) -> list[tuple[int, int]]:
    return [(scale_small(x, size), scale_small(y, size)) for x, y in points]


def scale_box(box: tuple[float, float, float, float], size: int) -> tuple[int, int, int, int]:
    return tuple(scale_small(value, size) for value in box)


SHIELD_OUTER = [(16, 2), (24, 5), (27, 11), (27, 20), (23, 25), (16, 30), (9, 25), (5, 20), (5, 11), (8, 5)]
SHIELD_INNER = [(16, 4), (22, 7), (24, 11), (24, 19), (21, 23), (16, 27), (11, 23), (8, 19), (8, 11), (10, 7)]


def draw_route_1(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.polygon(scale_points(SHIELD_OUTER, size), fill=OUTER_CYAN)

    battery = scale_box((10, 8, 22, 25), size)
    cap = scale_box((13, 6, 19, 8), size)
    body_radius = max(1, scale_small(4, size))
    cap_radius = max(1, scale_small(1, size))
    draw.rounded_rectangle(battery, radius=body_radius, fill=WHITE)
    draw.rounded_rectangle(cap, radius=cap_radius, fill=WHITE)

    bolt = scale_points([(17, 13), (14, 17), (17, 17), (15, 22), (20, 16), (17, 16)], size)
    draw.polygon(bolt, fill=LIME)
    return image


def draw_route_2(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.polygon(scale_points(SHIELD_OUTER, size), fill=PLATE_CYAN)

    battery = scale_box((8, 7, 24, 26), size)
    cap = scale_box((13, 5, 19, 8), size)
    cell = scale_box((11, 10, 21, 23), size)
    body_radius = max(1, scale_small(5, size))
    cap_radius = max(1, scale_small(1, size))
    cell_radius = max(1, scale_small(3, size))
    draw.rounded_rectangle(battery, radius=body_radius, fill=WHITE)
    draw.rounded_rectangle(cap, radius=cap_radius, fill=WHITE)
    draw.rounded_rectangle(cell, radius=cell_radius, fill=NAVY_SOFT)

    bolt = scale_points([(17, 13), (14, 18), (17, 18), (15, 22), (20, 16), (17, 16)], size)
    draw.polygon(bolt, fill=LIME)
    return image


def draw_route_3(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.polygon(scale_points(SHIELD_OUTER, size), fill=NAVY)

    core = scale_box((10, 8, 22, 24), size)
    cap = scale_box((13, 6, 19, 8), size)
    core_radius = max(1, scale_small(4, size))
    cap_radius = max(1, scale_small(1, size))
    draw.rounded_rectangle(core, radius=core_radius, fill=PLATE_CYAN)
    draw.rounded_rectangle(cap, radius=cap_radius, fill=WHITE)

    bolt = scale_points([(16.5, 9), (13.5, 18), (17, 18), (14.5, 25), (20.5, 15.5), (17, 15.5)], size)
    draw.polygon(bolt, fill=LIME)
    return image


ROUTES = [
    ("route-1-shield-cutout", "1. Shield + Battery Knockout", "shield-first, battery hollow feel", draw_route_1),
    ("route-2-battery-dominant", "2. Battery Dominant", "battery-first, shield recedes", draw_route_2),
    ("route-3-bolt-dominant", "3. Shield + Bolt Dominant", "bolt-first, battery weak cue", draw_route_3),
]


def save_png(path: Path, image: Image.Image) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG")


def make_slot(size: int, icon: Image.Image, slot_size: int = 72) -> Image.Image:
    slot = Image.new("RGBA", (slot_size, slot_size), CARD_BG)
    draw = ImageDraw.Draw(slot)
    draw.rounded_rectangle((1, 1, slot_size - 2, slot_size - 2), radius=16, outline=CARD_BORDER, width=1)
    x = (slot_size - size) // 2
    y = (slot_size - size) // 2
    slot.alpha_composite(icon, (x, y))
    return slot


def render_board() -> Path:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    font = ImageFont.load_default()

    card_width = 360
    card_height = 430
    gap = 28
    padding = 28
    board = Image.new("RGBA", (padding * 2 + card_width * 3 + gap * 2, padding * 2 + card_height), BOARD_BG)
    draw = ImageDraw.Draw(board)

    for index, (slug, title, subtitle, renderer) in enumerate(ROUTES):
        x = padding + index * (card_width + gap)
        y = padding
        draw.rounded_rectangle((x, y, x + card_width, y + card_height), radius=26, fill=CARD_BG, outline=CARD_BORDER, width=2)

        title_y = y + 22
        draw.text((x + 22, title_y), title, fill=TEXT, font=font)
        draw.text((x + 22, title_y + 22), subtitle, fill=SUBTEXT, font=font)

        preview = renderer(32).resize((224, 224), Image.Resampling.NEAREST)
        preview_box = (x + 68, y + 76)
        board.alpha_composite(preview, preview_box)

        labels_y = y + 328
        for small_index, size in enumerate((32, 24, 16)):
            icon = renderer(size)
            slot = make_slot(size, icon)
            slot_x = x + 30 + small_index * 102
            slot_y = labels_y
            board.alpha_composite(slot, (slot_x, slot_y))
            draw.text((slot_x + 26, slot_y + 82), f"{size}px", fill=SUBTEXT, font=font)

            save_png(OUTPUT_DIR / slug / f"SmartGuard-{size}.png", icon)

    board_path = OUTPUT_DIR / "SmartGuard-aggressive-small-drafts-board.png"
    save_png(board_path, board)
    return board_path


if __name__ == "__main__":
    path = render_board()
    print(path)
