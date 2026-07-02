from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
OUTPUT_DIR = ROOT / "dist" / "icon-previews" / "guardian-variants-hd"

CANVAS = 256.0
FINAL_SIZE = 2048
OVERSAMPLE = 2
WORK_SIZE = FINAL_SIZE * OVERSAMPLE

BOARD_BG = (248, 251, 255, 255)
CARD_BG = (255, 255, 255, 255)
CARD_BORDER = (223, 232, 242, 255)
TEXT = (31, 43, 60, 255)
SUBTEXT = (94, 107, 128, 255)

OUTER_BRIGHT = (84, 211, 242, 255)
NAVY = (74, 155, 198, 255)
NAVY_DEEP = (41, 112, 152, 255)
SLIT = (34, 96, 136, 255)
WHITE = (246, 250, 253, 255)
SMALL_EDGE = (62, 132, 198, 255)
SMALL_FACE = (137, 208, 250, 255)
EXPORT_SIZES = (16, 20, 24, 32, 40)
PREVIEW_SIZES = (32, 24, 16)

INNER_STROKE_RING = (114, 188, 239, 255)
INNER_STROKE_FACE = (82, 156, 228, 255)
INNER_STROKE_SEPARATOR = (255, 255, 255, 255)
INNER_STROKE_MASTER_SCALE = 1.08
INNER_STROKE_MASTER_CENTER = (128.0, 132.0)
INNER_STROKE_FACE_SCALE = 0.915
INNER_STROKE_SEPARATOR_SCALE = 0.948
INNER_STROKE_SEPARATOR_STRENGTH = 0.70


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


def scale_point_for_size(point: tuple[float, float], size: int, oversample: int = 1) -> tuple[float, float]:
    factor = size / CANVAS * oversample
    return point[0] * factor, point[1] * factor


def scale_points_for_size(points: list[tuple[float, float]], size: int, oversample: int = 1) -> list[tuple[float, float]]:
    return [scale_point_for_size(point, size, oversample) for point in points]


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


def scale_shape(points: list[tuple[float, float]], factor: float, center: tuple[float, float] = (128.0, 128.0)) -> list[tuple[float, float]]:
    cx, cy = center
    return [((x - cx) * factor + cx, (y - cy) * factor + cy) for x, y in points]


def polygon_mask(points: list[tuple[float, float]]) -> Image.Image:
    mask = Image.new("L", (WORK_SIZE, WORK_SIZE), 0)
    draw = ImageDraw.Draw(mask)
    draw.polygon(scale_points(points), fill=255)
    return mask


def apply_mask_fill(image: Image.Image, mask: Image.Image, fill: tuple[int, int, int, int]) -> None:
    layer = Image.new("RGBA", (WORK_SIZE, WORK_SIZE), fill)
    layer.putalpha(mask)
    image.alpha_composite(layer)


def apply_mask_fill_for_size(
    image: Image.Image,
    mask: Image.Image,
    fill: tuple[int, int, int, int],
) -> None:
    layer = Image.new("RGBA", image.size, fill)
    layer.putalpha(mask)
    image.alpha_composite(layer)


def center_alpha_bounds_vertically(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        return image

    top = bbox[1]
    bottom = bbox[3]
    content_height = bottom - top
    target_top = (image.height - content_height) // 2

    if top == target_top:
        return image

    band = image.crop((0, top, image.width, bottom))
    centered = Image.new("RGBA", image.size, (0, 0, 0, 0))
    centered.alpha_composite(band, (0, target_top))
    return centered


def draw_mask_at_size(
    points: list[tuple[float, float]],
    size: int,
    oversample: int,
) -> Image.Image:
    work = Image.new("L", (size * oversample, size * oversample), 0)
    ImageDraw.Draw(work).polygon(scale_points_for_size(points, size, oversample), fill=255)
    return work.resize((size, size), Image.Resampling.LANCZOS)


def render_base_shield() -> tuple[Image.Image, Image.Image]:
    outline = shield_outline()
    shield_mask = polygon_mask(outline)
    image = Image.new("RGBA", (WORK_SIZE, WORK_SIZE), (0, 0, 0, 0))
    apply_mask_fill(image, shield_mask, NAVY)
    return image, shield_mask


def draw_bolt(draw: ImageDraw.ImageDraw, fill: tuple[int, int, int, int]) -> None:
    bolt = BOLT_POINTS
    draw.polygon(scale_points(bolt), fill=fill)


BOLT_POINTS = [
    (134, 58),
    (108, 124),
    (132, 124),
    (120, 188),
    (158, 108),
    (138, 108),
]


def draw_transformed_bolt(
    draw: ImageDraw.ImageDraw,
    fill: tuple[int, int, int, int],
    factor: float,
    center: tuple[float, float],
) -> None:
    draw.polygon(scale_points(scale_shape(BOLT_POINTS, factor, center=center)), fill=fill)


def scale_small(value: float, size: int) -> int:
    return int(round(value * size / 32.0))


def scale_small_points(points: list[tuple[float, float]], size: int) -> list[tuple[int, int]]:
    return [(scale_small(x, size), scale_small(y, size)) for x, y in points]


SMALL_SHIELD_OUTER = [(16, 2), (24, 5), (28, 12), (28, 21), (22, 27), (16, 30), (10, 27), (4, 21), (4, 12), (8, 5)]
SMALL_SHIELD_INNER = [(16, 4), (22, 7), (25, 12), (25, 20), (21, 25), (16, 28), (11, 25), (7, 20), (7, 12), (10, 7)]
SMALL_BOLT = [(17, 9), (13, 18), (17, 18), (14, 25), (21, 14), (17, 14)]
SMALL_VECTOR_BOLT = [
    (130, 68),
    (110, 126),
    (132, 126),
    (122, 186),
    (154, 112),
    (136, 112),
]

PIXEL_FIT_INNER_STROKE = {
    16: {
        "outer": [(8, 1), (12, 3), (14, 6), (14, 11), (12, 14), (8, 15), (4, 14), (2, 11), (2, 6), (4, 3)],
        "face": [(8, 3), (11, 4), (12, 6), (12, 10), (10, 12), (8, 13), (6, 12), (4, 10), (4, 6), (5, 4)],
        "bolt": [(9, 5), (7, 9), (9, 9), (8, 12), (11, 7), (9, 7)],
    },
    20: {
        "outer": [(10, 1), (15, 4), (18, 7), (18, 13), (15, 17), (10, 19), (5, 17), (2, 13), (2, 7), (5, 4)],
        "face": [(10, 3), (14, 5), (16, 8), (16, 13), (14, 16), (10, 18), (6, 16), (4, 13), (4, 8), (6, 5)],
        "bolt": [(11, 6), (8, 11), (11, 11), (10, 15), (14, 8), (12, 8)],
    },
    24: {
        "outer": [(12, 1), (18, 4), (21, 8), (21, 16), (18, 20), (12, 23), (6, 20), (3, 16), (3, 8), (6, 4)],
        "face": [(12, 4), (16, 6), (18, 9), (18, 14), (15, 18), (12, 20), (9, 18), (6, 14), (6, 9), (8, 6)],
        "bolt": [(13, 7), (10, 13), (13, 13), (11, 17), (16, 10), (13, 10)],
    },
    32: {
        "outer": [(16, 1), (24, 5), (27, 9), (27, 18), (16, 30), (5, 18), (5, 9), (8, 5)],
        "face": [(16, 4), (21, 7), (24, 10), (24, 17), (16, 27), (8, 17), (8, 10), (11, 7)],
        "bolt": [(17, 9), (14, 15), (17, 15), (15, 22), (20, 14), (17, 14)],
    },
    40: {
        "outer": [(20, 2), (30, 6), (35, 12), (35, 22), (30, 29), (20, 37), (10, 29), (5, 22), (5, 12), (10, 6)],
        "face": [(20, 5), (27, 9), (30, 14), (30, 24), (25, 30), (20, 34), (15, 30), (10, 24), (10, 14), (13, 9)],
        "bolt": [(21, 10), (16, 21), (21, 21), (18, 29), (25, 16), (21, 16)],
    },
}

VECTOR_FIT_INNER_STROKE = {
    32: {
        "outer": [
            (128, 14),
            (192, 42),
            (220, 92),
            (220, 166),
            (176, 216),
            (128, 244),
            (80, 216),
            (36, 166),
            (36, 92),
            (64, 42),
        ],
        "face": [
            (128, 36),
            (176, 60),
            (196, 98),
            (196, 158),
            (164, 198),
            (128, 220),
            (92, 198),
            (60, 158),
            (60, 98),
            (80, 60),
        ],
        "bolt": [
            (136, 70),
            (108, 130),
            (134, 130),
            (122, 186),
            (160, 112),
            (138, 112),
        ],
    },
}


def make_slot(icon: Image.Image, size: int) -> Image.Image:
    slot = Image.new("RGBA", (78, 78), CARD_BG)
    draw = ImageDraw.Draw(slot)
    draw.rounded_rectangle((1, 1, 76, 76), radius=16, outline=CARD_BORDER, width=1)
    x = (78 - size) // 2
    y = (78 - size) // 2
    slot.alpha_composite(icon, (x, y))
    return slot


def render_small_inner_stroke(size: int) -> Image.Image:
    if size == 32:
        image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        draw = ImageDraw.Draw(image)
        geometry = PIXEL_FIT_INNER_STROKE[32]
        draw.polygon(geometry["outer"], fill=SMALL_EDGE)
        draw.polygon(geometry["face"], fill=SMALL_FACE)
        draw.polygon(geometry["bolt"], fill=WHITE)
        return image

    if size >= 20:
        oversample = 16 if size == 32 else 14
        canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        geometry = VECTOR_FIT_INNER_STROKE.get(size)
        outline = geometry["outer"] if geometry is not None else shield_outline()
        outer_mask = draw_mask_at_size(outline, size, oversample)
        apply_mask_fill_for_size(canvas, outer_mask, SMALL_EDGE)

        face_points = geometry["face"] if geometry is not None else scale_shape(outline, 0.89)
        face_mask = draw_mask_at_size(face_points, size, oversample)
        apply_mask_fill_for_size(canvas, face_mask, SMALL_FACE)

        bolt_points = geometry["bolt"] if geometry is not None else SMALL_VECTOR_BOLT
        bolt_mask = draw_mask_at_size(bolt_points, size, oversample)
        apply_mask_fill_for_size(canvas, bolt_mask, WHITE)
        return canvas

    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    geometry = PIXEL_FIT_INNER_STROKE[size]
    draw.polygon(geometry["outer"], fill=SMALL_EDGE)
    draw.polygon(geometry["face"], fill=SMALL_FACE)
    draw.polygon(geometry["bolt"], fill=WHITE)
    return image


def render_small_bottom_facet(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.polygon(scale_small_points(SMALL_SHIELD_OUTER, size), fill=NAVY)
    facet = [(0, 19), (10, 20), (16, 22), (22, 20), (32, 19), (32, 32), (0, 32)]
    draw.polygon(scale_small_points(facet, size), fill=NAVY_DEEP)
    draw.polygon(scale_small_points(SMALL_BOLT, size), fill=WHITE)
    return image


def render_small_bolt_detail(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.polygon(scale_small_points(SMALL_SHIELD_OUTER, size), fill=NAVY)
    draw.polygon(scale_small_points(SMALL_BOLT, size), fill=WHITE)
    slit = [(16.6, 11.0), (17.7, 11.2), (16.0, 16.5), (14.8, 16.3)]
    draw.polygon(scale_small_points(slit, size), fill=SLIT)
    return image


def render_inner_stroke() -> Image.Image:
    image = Image.new("RGBA", (WORK_SIZE, WORK_SIZE), (0, 0, 0, 0))
    outline = scale_shape(
        shield_outline(),
        INNER_STROKE_MASTER_SCALE,
        center=INNER_STROKE_MASTER_CENTER,
    )
    apply_mask_fill(image, polygon_mask(outline), INNER_STROKE_RING)

    face_mask = polygon_mask(
        scale_shape(
            shield_outline(),
            INNER_STROKE_FACE_SCALE * INNER_STROKE_MASTER_SCALE,
            center=INNER_STROKE_MASTER_CENTER,
        )
    )
    apply_mask_fill(image, face_mask, INNER_STROKE_FACE)

    separator_points = scale_shape(
        shield_outline(),
        INNER_STROKE_SEPARATOR_SCALE * INNER_STROKE_MASTER_SCALE,
        center=INNER_STROKE_MASTER_CENTER,
    )
    separator_mask = polygon_mask(separator_points)
    separator_ring = ImageChops.subtract(separator_mask, face_mask)
    separator_ring = separator_ring.point(lambda alpha: int(alpha * INNER_STROKE_SEPARATOR_STRENGTH))
    apply_mask_fill(image, separator_ring, INNER_STROKE_SEPARATOR)

    draw = ImageDraw.Draw(image)
    draw_transformed_bolt(
        draw,
        WHITE,
        INNER_STROKE_MASTER_SCALE,
        center=INNER_STROKE_MASTER_CENTER,
    )
    final_image = image.resize((FINAL_SIZE, FINAL_SIZE), Image.Resampling.LANCZOS)
    return center_alpha_bounds_vertically(final_image)


def render_bottom_facet() -> Image.Image:
    image, shield_mask = render_base_shield()
    facet_top = [
        (-24, 151),
        (96, 165),
        (128, 172),
        (160, 165),
        (280, 151),
        (280, 256),
        (-24, 256),
    ]
    facet_mask = ImageChops.multiply(shield_mask, polygon_mask(facet_top))
    apply_mask_fill(image, facet_mask, NAVY_DEEP)
    draw = ImageDraw.Draw(image)
    draw_bolt(draw, WHITE)
    return image.resize((FINAL_SIZE, FINAL_SIZE), Image.Resampling.LANCZOS)


def render_bolt_detail() -> Image.Image:
    image, _ = render_base_shield()
    draw = ImageDraw.Draw(image)
    draw_bolt(draw, WHITE)
    seam = [
        (132.3, 90.0),
        (135.2, 90.6),
        (128.7, 118.0),
        (125.7, 117.3),
    ]
    draw.polygon(scale_points(seam), fill=SLIT)
    return image.resize((FINAL_SIZE, FINAL_SIZE), Image.Resampling.LANCZOS)


def on_white(image: Image.Image) -> Image.Image:
    board = Image.new("RGBA", image.size, (255, 255, 255, 255))
    board.alpha_composite(image)
    return board


def with_square_export_anchors(image: Image.Image) -> Image.Image:
    anchored = image.copy()
    pixels = anchored.load()
    width, height = anchored.size
    # Use fully opaque 1px corner anchors so export tools that trim by visible
    # alpha bounds still preserve the square canvas.
    anchor = (255, 255, 255, 255)
    pixels[0, 0] = anchor
    pixels[width - 1, 0] = anchor
    pixels[0, height - 1] = anchor
    pixels[width - 1, height - 1] = anchor
    return anchored


def save_png(path: Path, image: Image.Image) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG")


def render_board(images: list[Image.Image]) -> Image.Image:
    board = Image.new("RGBA", (3840, 1520), BOARD_BG)
    draw = ImageDraw.Draw(board)
    font = ImageFont.load_default()

    cards = [
        (60, 80, "1. Inner stroke", "thin inset ring, still shield + bolt"),
        (1300, 80, "2. Bottom facet", "single dark facet only on lower shield"),
        (2540, 80, "3. Bolt detail", "single split detail inside lightning"),
    ]
    small_renderers = [render_small_inner_stroke, render_small_bottom_facet, render_small_bolt_detail]

    for (x, y, title, subtitle), icon, small_renderer in zip(cards, images, small_renderers):
        draw.rounded_rectangle((x, y, x + 1160, y + 1320), radius=42, fill=CARD_BG, outline=CARD_BORDER, width=3)
        draw.text((x + 36, y + 32), title, fill=TEXT, font=font)
        draw.text((x + 36, y + 68), subtitle, fill=SUBTEXT, font=font)
        scaled = icon.resize((860, 860), Image.Resampling.LANCZOS)
        board.alpha_composite(scaled, (x + 150, y + 190))

        labels = ["32px", "24px", "16px"]
        for size in EXPORT_SIZES:
            small_icon = small_renderer(size)
            save_png(OUTPUT_DIR / title.split(". ", 1)[1].lower().replace(" ", "-") / f"SmartGuard-{size}.png", small_icon)

        for index, size in enumerate(PREVIEW_SIZES):
            small_icon = small_renderer(size)
            slot = make_slot(small_icon, size)
            slot_x = x + 154 + index * 240
            slot_y = y + 1088
            board.alpha_composite(slot, (slot_x, slot_y))
            draw.text((slot_x + 22, slot_y + 86), labels[index], fill=SUBTEXT, font=font)

    return board


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    inner_stroke = render_inner_stroke()
    bottom_facet = render_bottom_facet()
    bolt_detail = render_bolt_detail()

    variants = [
        ("inner-stroke", inner_stroke),
        ("bottom-facet", bottom_facet),
        ("bolt-detail", bolt_detail),
    ]

    for slug, image in variants:
        save_png(OUTPUT_DIR / f"SmartGuard-{slug}-2048-transparent.png", image)
        save_png(OUTPUT_DIR / f"SmartGuard-{slug}-2048-transparent-square-master.png", with_square_export_anchors(image))
        save_png(OUTPUT_DIR / f"SmartGuard-{slug}-2048-white.png", on_white(image))

    board = render_board([on_white(inner_stroke), on_white(bottom_facet), on_white(bolt_detail)])
    save_png(OUTPUT_DIR / "SmartGuard-guardian-variants-board.png", board)
    print(OUTPUT_DIR / "SmartGuard-guardian-variants-board.png")


if __name__ == "__main__":
    main()
