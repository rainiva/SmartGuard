from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
OUTPUT_DIR = ROOT / "dist" / "icon-previews" / "cursor-style-hd"

CANVAS = 256.0
FINAL_SIZE = 2048
OVERSAMPLE = 2
WORK_SIZE = FINAL_SIZE * OVERSAMPLE

BOARD_BG = (248, 251, 255, 255)
CARD_BG = (255, 255, 255, 255)
CARD_BORDER = (223, 232, 242, 255)
TEXT = (31, 43, 60, 255)
SUBTEXT = (94, 107, 128, 255)

NAVY = (27, 49, 84, 255)
SLATE = (46, 73, 111, 255)
WHITE = (246, 250, 253, 255)


def cubic_points(p0, p1, p2, p3, steps: int) -> list[tuple[float, float]]:
    points: list[tuple[float, float]] = []
    for index in range(steps + 1):
        t = index / steps
        mt = 1.0 - t
        x = (
            (mt**3) * p0[0]
            + 3 * (mt**2) * t * p1[0]
            + 3 * mt * (t**2) * p2[0]
            + (t**3) * p3[0]
        )
        y = (
            (mt**3) * p0[1]
            + 3 * (mt**2) * t * p1[1]
            + 3 * mt * (t**2) * p2[1]
            + (t**3) * p3[1]
        )
        points.append((x, y))
    return points


def scale_point(point: tuple[float, float]) -> tuple[float, float]:
    factor = WORK_SIZE / CANVAS
    return point[0] * factor, point[1] * factor


def scale_points(points: list[tuple[float, float]]) -> list[tuple[float, float]]:
    return [scale_point(point) for point in points]


def scale_box(box: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    left_top = scale_point((box[0], box[1]))
    right_bottom = scale_point((box[2], box[3]))
    return left_top[0], left_top[1], right_bottom[0], right_bottom[1]


def shield_outline() -> list[tuple[float, float]]:
    top_right = cubic_points((128, 10), (158, 21), (187, 35), (214, 50), 64)
    bottom_right = cubic_points((214, 149), (214, 171), (204, 197), (128, 242), 64)
    bottom_left = cubic_points((128, 242), (52, 197), (42, 171), (42, 149), 64)
    top_left = cubic_points((42, 50), (69, 35), (98, 21), (128, 10), 64)
    return (
        top_right[:-1]
        + [(214, 149)]
        + bottom_right[1:-1]
        + bottom_left[:-1]
        + [(42, 50)]
        + top_left[1:]
    )


def render_shield_base(fill_color: tuple[int, int, int, int]) -> Image.Image:
    image = Image.new("RGBA", (WORK_SIZE, WORK_SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.polygon(scale_points(shield_outline()), fill=fill_color)
    return image


def render_guardian_first() -> Image.Image:
    image = render_shield_base(NAVY)
    draw = ImageDraw.Draw(image)
    bolt = [
        (134, 58),
        (108, 124),
        (132, 124),
        (120, 188),
        (158, 108),
        (138, 108),
    ]
    draw.polygon(scale_points(bolt), fill=WHITE)
    return image.resize((FINAL_SIZE, FINAL_SIZE), Image.Resampling.LANCZOS)


def render_power_first() -> Image.Image:
    image = render_shield_base(SLATE)
    body = scale_box((100, 72, 156, 186))
    cap = scale_box((117, 60, 139, 72))
    body_radius = round(16 * WORK_SIZE / CANVAS)
    cap_radius = round(5 * WORK_SIZE / CANVAS)

    body_mask = Image.new("L", (WORK_SIZE, WORK_SIZE), 0)
    draw_mask = ImageDraw.Draw(body_mask)
    draw_mask.rounded_rectangle(body, radius=body_radius, fill=255)
    draw_mask.rounded_rectangle(cap, radius=cap_radius, fill=255)
    body_fill = Image.new("RGBA", (WORK_SIZE, WORK_SIZE), WHITE)
    body_fill.putalpha(body_mask)
    image.alpha_composite(body_fill)

    draw = ImageDraw.Draw(image)
    bolt = [
        (132, 88),
        (118, 120),
        (134, 120),
        (126, 151),
        (147, 112),
        (135, 112),
    ]
    draw.polygon(scale_points(bolt), fill=NAVY)
    return image.resize((FINAL_SIZE, FINAL_SIZE), Image.Resampling.LANCZOS)


def on_white(image: Image.Image) -> Image.Image:
    board = Image.new("RGBA", image.size, (255, 255, 255, 255))
    board.alpha_composite(image)
    return board


def save_png(path: Path, image: Image.Image) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG")


def render_board(guardian: Image.Image, power: Image.Image) -> Image.Image:
    board = Image.new("RGBA", (2600, 1480), BOARD_BG)
    draw = ImageDraw.Draw(board)
    font = ImageFont.load_default()

    cards = [
        (70, 80, "A. Guardian-first", "single shield face + white lightning"),
        (1360, 80, "B. Power-first", "single shield face + white battery + bolt cut"),
    ]
    icons = [guardian, power]

    for (x, y, title, subtitle), icon in zip(cards, icons):
        draw.rounded_rectangle((x, y, x + 1170, y + 1320), radius=42, fill=CARD_BG, outline=CARD_BORDER, width=3)
        draw.text((x + 36, y + 32), title, fill=TEXT, font=font)
        draw.text((x + 36, y + 68), subtitle, fill=SUBTEXT, font=font)
        scaled = icon.resize((900, 900), Image.Resampling.LANCZOS)
        board.alpha_composite(scaled, (x + 135, y + 170))

    return board


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    guardian = render_guardian_first()
    power = render_power_first()

    save_png(OUTPUT_DIR / "SmartGuard-guardian-first-2048-transparent.png", guardian)
    save_png(OUTPUT_DIR / "SmartGuard-guardian-first-2048-white.png", on_white(guardian))
    save_png(OUTPUT_DIR / "SmartGuard-power-first-2048-transparent.png", power)
    save_png(OUTPUT_DIR / "SmartGuard-power-first-2048-white.png", on_white(power))
    save_png(OUTPUT_DIR / "SmartGuard-cursor-style-hd-board.png", render_board(on_white(guardian), on_white(power)))

    print(OUTPUT_DIR / "SmartGuard-cursor-style-hd-board.png")


if __name__ == "__main__":
    main()
