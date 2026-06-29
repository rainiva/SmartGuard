from __future__ import annotations

import argparse
import math
import struct
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
ASSETS_DIR = ROOT / "assets" / "icon"
PREVIEW_DIR = ROOT / "dist" / "icon-previews"
CANDIDATE_DIR = PREVIEW_DIR / "face-system-v2"
FINAL_SET_DIR = PREVIEW_DIR / "final-set"
LIB_DIR = ROOT / "lib"

CANVAS = 256.0

OUTER_SHIELD = (84, 211, 242, 255)
INNER_TOP = (74, 155, 198, 255)
INNER_MID = (57, 140, 184, 255)
INNER_BOTTOM = (41, 112, 152, 255)
SMALL_INNER = (28, 60, 96, 255)
BATTERY_WHITE = (241, 247, 251, 255)
CELL_BLUE = (93, 153, 197, 255)
BOLT_LIME = (230, 255, 114, 255)
WHITE_BG = (255, 255, 255, 255)

LARGE_SIZES = (48, 64, 128, 256)
SMALL_SIZES = (16, 20, 24, 32, 40)
ALL_SIZES = tuple(sorted((*SMALL_SIZES, *LARGE_SIZES)))

OVERSAMPLE = {
    48: 6,
    64: 6,
    128: 4,
    256: 4,
    2048: 2,
}


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


def scale_point(point: tuple[float, float], size: int, oversample: int = 1) -> tuple[float, float]:
    factor = size / CANVAS * oversample
    return point[0] * factor, point[1] * factor


def scale_points(points: list[tuple[float, float]], size: int, oversample: int = 1) -> list[tuple[float, float]]:
    return [scale_point(point, size, oversample) for point in points]


def around_center_scale_point(point: tuple[float, float], factor: float, center: float = 128.0) -> tuple[float, float]:
    return center + (point[0] - center) * factor, center + (point[1] - center) * factor


def around_center_scale_box(box: tuple[float, float, float, float], factor: float, center: float = 128.0) -> tuple[float, float, float, float]:
    left, top = around_center_scale_point((box[0], box[1]), factor, center)
    right, bottom = around_center_scale_point((box[2], box[3]), factor, center)
    return left, top, right, bottom


def make_gradient(width: int, height: int, top: tuple[int, int, int, int], mid: tuple[int, int, int, int], bottom: tuple[int, int, int, int]) -> Image.Image:
    gradient = Image.new("RGBA", (width, height))
    pixels = gradient.load()
    split = 0.58
    for y in range(height):
        offset = 0 if height == 1 else y / (height - 1)
        if offset <= split:
            local = 0 if split == 0 else offset / split
            start = top
            end = mid
        else:
            local = 0 if split == 1 else (offset - split) / (1 - split)
            start = mid
            end = bottom
        color = tuple(
            int(round(start[channel] + (end[channel] - start[channel]) * local))
            for channel in range(4)
        )
        for x in range(width):
            pixels[x, y] = color
    return gradient


def fill_mask(base: Image.Image, mask: Image.Image, fill_image: Image.Image) -> None:
    layer = fill_image.copy()
    layer.putalpha(mask)
    base.alpha_composite(layer)


def draw_union_mask(size: tuple[int, int], drawers: list[tuple[str, tuple, int]]) -> Image.Image:
    mask = Image.new("L", size, 0)
    draw = ImageDraw.Draw(mask)
    for shape, payload, radius in drawers:
        if shape == "rounded_rect":
            draw.rounded_rectangle(payload, radius=radius, fill=255)
        elif shape == "rect":
            draw.rectangle(payload, fill=255)
        elif shape == "polygon":
            draw.polygon(payload, fill=255)
    return mask


def shield_paths(shoulder_inner_y: float = 56.0) -> tuple[list[tuple[float, float]], list[tuple[float, float]]]:
    outer_top_right = cubic_points((128, 8), (159, 19), (191, 35), (223, 50), 48)
    outer_bottom_right = cubic_points((223, 149), (223, 173), (213, 201), (128, 249), 48)
    outer_bottom_left = cubic_points((128, 249), (43, 201), (33, 173), (33, 149), 48)
    outer_top_left = cubic_points((33, 50), (65, 35), (97, 19), (128, 8), 48)
    outer = (
        outer_top_right[:-1]
        + [(223, 149)]
        + outer_bottom_right[1:-1]
        + outer_bottom_left[:-1]
        + [(33, 50)]
        + outer_top_left[1:]
    )

    inner_top_right = cubic_points((128, 20), (156, 30), (185, 45), (212, shoulder_inner_y), 48)
    inner_bottom_right = cubic_points((212, 147), (212, 168), (203, 191), (128, 237), 48)
    inner_bottom_left = cubic_points((128, 237), (53, 191), (44, 168), (44, 147), 48)
    inner_top_left = cubic_points((44, shoulder_inner_y), (71, 45), (100, 30), (128, 20), 48)
    inner = (
        inner_top_right[:-1]
        + [(212, 147)]
        + inner_bottom_right[1:-1]
        + inner_bottom_left[:-1]
        + [(44, shoulder_inner_y)]
        + inner_top_left[1:]
    )
    return outer, inner


def battery_shell_shapes(scale: float, size: int, oversample: int) -> list[tuple[str, tuple, int]]:
    body_box = around_center_scale_box((98, 64, 158, 202), scale)
    cap_box = around_center_scale_box((115, 53, 141, 64), scale)
    body_radius = int(round(13 * scale * size / CANVAS * oversample))
    cap_radius = max(1, int(round(4 * scale * size / CANVAS * oversample)))
    return [
        ("rounded_rect", tuple(scale_box(body_box, size, oversample)), body_radius),
        ("rounded_rect", tuple(scale_box(cap_box, size, oversample)), cap_radius),
    ]


def scale_box(box: tuple[float, float, float, float], size: int, oversample: int = 1) -> tuple[float, float, float, float]:
    left_top = scale_point((box[0], box[1]), size, oversample)
    right_bottom = scale_point((box[2], box[3]), size, oversample)
    return left_top[0], left_top[1], right_bottom[0], right_bottom[1]


def large_battery_group(canvas_size: int) -> Image.Image:
    oversample = OVERSAMPLE.get(canvas_size, 4)
    image = Image.new("RGBA", (canvas_size * oversample, canvas_size * oversample), (0, 0, 0, 0))

    shell_mask = draw_union_mask(
        image.size,
        battery_shell_shapes(0.92, canvas_size, oversample),
    )
    shell_fill = Image.new("RGBA", image.size, BATTERY_WHITE)
    fill_mask(image, shell_mask, shell_fill)

    cell_box = around_center_scale_box((106, 79, 150, 195), 0.92)
    cell_radius = max(1, int(round(9 * 0.92 * canvas_size / CANVAS * oversample)))
    cell_mask = draw_union_mask(
        image.size,
        [("rounded_rect", tuple(scale_box(cell_box, canvas_size, oversample)), cell_radius)],
    )
    cell_fill = make_gradient(
        image.width,
        image.height,
        INNER_TOP,
        INNER_MID,
        INNER_BOTTOM,
    )
    fill_mask(image, cell_mask, cell_fill)

    bolt_points = [
        (129, 108),
        (118, 138),
        (128, 138),
        (124, 157),
        (138, 133),
        (130, 133),
    ]
    transformed_bolt = [
        around_center_scale_point(point, 0.92)
        for point in bolt_points
    ]
    draw = ImageDraw.Draw(image)
    draw.polygon(scale_points(transformed_bolt, canvas_size, oversample), fill=BOLT_LIME)
    if oversample > 1:
        return image.resize((canvas_size, canvas_size), Image.Resampling.LANCZOS)
    return image


def render_large_icon(size: int) -> Image.Image:
    oversample = OVERSAMPLE.get(size, 4)
    canvas = Image.new("RGBA", (size * oversample, size * oversample), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    outer_points, inner_points = shield_paths(shoulder_inner_y=56.0)
    draw.polygon(scale_points(outer_points, size, oversample), fill=OUTER_SHIELD)

    inner_mask = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(inner_mask).polygon(scale_points(inner_points, size, oversample), fill=255)
    shield_fill = make_gradient(canvas.width, canvas.height, INNER_TOP, INNER_MID, INNER_BOTTOM)
    fill_mask(canvas, inner_mask, shield_fill)

    battery = large_battery_group(size)
    if oversample > 1:
        battery = battery.resize((size * oversample, size * oversample), Image.Resampling.NEAREST)
    canvas.alpha_composite(battery)

    if oversample > 1:
        return canvas.resize((size, size), Image.Resampling.LANCZOS)
    return canvas


SMALL_BASE = {
    "outer": [(16, 1), (24, 5), (28, 12), (28, 20), (24, 26), (16, 31), (8, 26), (4, 20), (4, 12), (8, 5)],
    "inner": [(16, 3), (23, 7), (25, 12), (25, 19), (21, 24), (16, 28), (11, 24), (7, 19), (7, 12), (9, 7)],
    "battery_body": (10, 8, 22, 25),
    "battery_radius": 4,
    "battery_cap": (13, 6, 19, 8),
    "cap_radius": 1,
    "cell": (11, 10, 21, 24),
    "cell_radius": 2,
    "bolt": [(17, 14), (15, 17), (18, 17), (16, 20), (19, 17), (17, 17)],
}


def scale_small(value: float, size: int) -> int:
    return int(round(value * size / 32.0))


def scale_small_points(points: list[tuple[float, float]], size: int) -> list[tuple[int, int]]:
    return [(scale_small(x, size), scale_small(y, size)) for x, y in points]


def scale_small_box(box: tuple[float, float, float, float], size: int) -> tuple[int, int, int, int]:
    return tuple(scale_small(value, size) for value in box)


def render_small_icon(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    draw.polygon(scale_small_points(SMALL_BASE["outer"], size), fill=OUTER_SHIELD)
    draw.polygon(scale_small_points(SMALL_BASE["inner"], size), fill=SMALL_INNER)

    body = scale_small_box(SMALL_BASE["battery_body"], size)
    body_radius = max(1, scale_small(SMALL_BASE["battery_radius"], size))
    cap = scale_small_box(SMALL_BASE["battery_cap"], size)
    cap_radius = max(1, scale_small(SMALL_BASE["cap_radius"], size))
    cell = scale_small_box(SMALL_BASE["cell"], size)
    cell_radius = max(1, scale_small(SMALL_BASE["cell_radius"], size))

    draw.rounded_rectangle(body, radius=body_radius, fill=BATTERY_WHITE)
    draw.rounded_rectangle(cap, radius=cap_radius, fill=BATTERY_WHITE)
    draw.rounded_rectangle(cell, radius=cell_radius, fill=CELL_BLUE)
    draw.polygon(scale_small_points(SMALL_BASE["bolt"], size), fill=BOLT_LIME)
    return image


def render_icon(size: int) -> Image.Image:
    if size in SMALL_SIZES:
        return render_small_icon(size)
    return render_large_icon(size)


def save_png(path: Path, image: Image.Image) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG")


def save_ico(path: Path, images: dict[int, Image.Image]) -> None:
    blobs: list[tuple[int, bytes]] = []
    for size in sorted(images):
        buffer = BytesIO()
        images[size].save(buffer, format="PNG")
        blobs.append((size, buffer.getvalue()))

    header = struct.pack("<HHH", 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    entries: list[bytes] = []
    payloads: list[bytes] = []
    for size, data in blobs:
        width = 0 if size >= 256 else size
        height = 0 if size >= 256 else size
        entries.append(
            struct.pack(
                "<BBBBHHII",
                width,
                height,
                0,
                0,
                1,
                32,
                len(data),
                offset,
            )
        )
        payloads.append(data)
        offset += len(data)

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(header + b"".join(entries) + b"".join(payloads))


def white_preview(image: Image.Image) -> Image.Image:
    preview = Image.new("RGBA", image.size, WHITE_BG)
    preview.alpha_composite(image)
    return preview


def render_all() -> dict[int, Image.Image]:
    return {size: render_icon(size) for size in ALL_SIZES}


def write_candidate(images: dict[int, Image.Image]) -> None:
    CANDIDATE_DIR.mkdir(parents=True, exist_ok=True)
    for size, image in images.items():
        save_png(CANDIDATE_DIR / f"SmartGuard-{size}.png", image)
    save_ico(CANDIDATE_DIR / "SmartGuard.ico", images)

    preview_2048 = render_large_icon(2048)
    save_png(PREVIEW_DIR / "SmartGuard-face-system-v2-2048-white.png", white_preview(preview_2048))
    save_png(PREVIEW_DIR / "SmartGuard-face-system-v2-2048-transparent.png", preview_2048)


def promote(images: dict[int, Image.Image]) -> None:
    for size, image in images.items():
        save_png(FINAL_SET_DIR / f"SmartGuard-{size}.png", image)
    save_ico(FINAL_SET_DIR / "SmartGuard.ico", images)
    save_ico(FINAL_SET_DIR / "SmartGuard-test.ico", images)
    save_ico(LIB_DIR / "SmartGuard.ico", images)
    save_png(ASSETS_DIR / "SmartGuard-32.png", images[32])

    preview_2048 = render_large_icon(2048)
    save_png(PREVIEW_DIR / "SmartGuard-master-official-2048-white.png", white_preview(preview_2048))


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate SmartGuard icon previews and promoted assets.")
    parser.add_argument(
        "--promote",
        action="store_true",
        help="Copy the generated icon set into final-set, lib/SmartGuard.ico, and assets/icon/SmartGuard-32.png.",
    )
    args = parser.parse_args()

    images = render_all()
    write_candidate(images)
    if args.promote:
        promote(images)


if __name__ == "__main__":
    main()
