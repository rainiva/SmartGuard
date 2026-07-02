from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
OUTPUT_DIR = ROOT / "dist" / "icon-previews" / "cursor-style-drafts"

BOARD_BG = (249, 251, 255, 255)
CARD_BG = (255, 255, 255, 255)
CARD_BORDER = (224, 232, 243, 255)
TEXT = (30, 42, 61, 255)
SUBTEXT = (97, 111, 131, 255)

SHIELD_NAVY = (23, 47, 82, 255)
SHIELD_CYAN = (87, 203, 239, 255)
SHIELD_CYAN_DARK = (71, 153, 198, 255)
WHITE = (246, 250, 253, 255)

SIZES = (16, 24, 32)


def scale(value: float, size: int) -> int:
    return int(round(value * size / 32.0))


def scale_points(points: list[tuple[float, float]], size: int) -> list[tuple[int, int]]:
    return [(scale(x, size), scale(y, size)) for x, y in points]


def scale_box(box: tuple[float, float, float, float], size: int) -> tuple[int, int, int, int]:
    return tuple(scale(value, size) for value in box)


SHIELD_OUTER = [(16, 2), (25, 6), (28, 13), (28, 21), (22, 27), (16, 30), (10, 27), (4, 21), (4, 13), (7, 6)]
TOP_FACET = [(16, 2), (25, 6), (21, 9), (11, 9), (7, 6)]


def draw_guardian_variant(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    draw.polygon(scale_points(SHIELD_OUTER, size), fill=SHIELD_NAVY)
    draw.polygon(scale_points(TOP_FACET, size), fill=SHIELD_CYAN)

    bolt = [(17, 9), (13, 18), (17, 18), (14, 25), (21, 14), (17, 14)]
    draw.polygon(scale_points(bolt, size), fill=WHITE)
    return image


def draw_power_variant(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    draw.polygon(scale_points(SHIELD_OUTER, size), fill=SHIELD_NAVY)
    draw.polygon(scale_points(TOP_FACET, size), fill=SHIELD_CYAN_DARK)

    body = scale_box((10, 9, 22, 24), size)
    cap = scale_box((13, 7, 19, 9), size)
    body_radius = max(1, scale(3, size))
    cap_radius = max(1, scale(1, size))

    draw.rounded_rectangle(body, radius=body_radius, fill=WHITE)
    draw.rounded_rectangle(cap, radius=cap_radius, fill=WHITE)

    bolt = [(17, 12), (15, 16), (17, 16), (15, 21), (19, 15), (17, 15)]
    draw.polygon(scale_points(bolt, size), fill=SHIELD_NAVY)
    return image


def save_png(path: Path, image: Image.Image) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG")


def make_slot(icon: Image.Image, size: int) -> Image.Image:
    slot = Image.new("RGBA", (76, 76), CARD_BG)
    draw = ImageDraw.Draw(slot)
    draw.rounded_rectangle((1, 1, 74, 74), radius=16, outline=CARD_BORDER, width=1)
    x = (76 - size) // 2
    y = (76 - size) // 2
    slot.alpha_composite(icon, (x, y))
    return slot


def route_card(
    board: Image.Image,
    x: int,
    y: int,
    title: str,
    subtitle: str,
    slug: str,
    renderer,
    font,
) -> None:
    draw = ImageDraw.Draw(board)
    card_width = 460
    card_height = 460
    draw.rounded_rectangle((x, y, x + card_width, y + card_height), radius=28, fill=CARD_BG, outline=CARD_BORDER, width=2)

    draw.text((x + 26, y + 24), title, fill=TEXT, font=font)
    draw.text((x + 26, y + 48), subtitle, fill=SUBTEXT, font=font)

    preview = renderer(32).resize((240, 240), Image.Resampling.NEAREST)
    board.alpha_composite(preview, (x + 110, y + 88))

    labels = ["32px", "24px", "16px"]
    for idx, size in enumerate((32, 24, 16)):
        icon = renderer(size)
        save_png(OUTPUT_DIR / slug / f"SmartGuard-{size}.png", icon)
        slot = make_slot(icon, size)
        slot_x = x + 56 + idx * 122
        slot_y = y + 346
        board.alpha_composite(slot, (slot_x, slot_y))
        draw.text((slot_x + 22, slot_y + 84), labels[idx], fill=SUBTEXT, font=font)


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    font = ImageFont.load_default()

    board = Image.new("RGBA", (980, 520), BOARD_BG)
    route_card(
        board,
        26,
        26,
        "A. Guardian-first",
        "solid shield + minimal white lightning cut",
        "guardian-first",
        draw_guardian_variant,
        font,
    )
    route_card(
        board,
        494,
        26,
        "B. Power-first",
        "solid shield + white battery glyph + bolt cut",
        "power-first",
        draw_power_variant,
        font,
    )

    board_path = OUTPUT_DIR / "SmartGuard-cursor-style-board.png"
    save_png(board_path, board)
    print(board_path)


if __name__ == "__main__":
    main()
