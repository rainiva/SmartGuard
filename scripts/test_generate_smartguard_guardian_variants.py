import importlib.util
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
SCRIPT_PATH = ROOT / "scripts" / "generate-smartguard-guardian-variants.py"


def load_generator_module():
    spec = importlib.util.spec_from_file_location("sg_variants", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class InnerStrokeOccupancyTests(unittest.TestCase):
    def test_rendered_master_fills_canvas_like_desktop_app_icon(self):
        generator = load_generator_module()

        image = generator.render_inner_stroke()
        alpha = image.getchannel("A")
        bbox = alpha.getbbox()

        self.assertIsNotNone(bbox)
        width = bbox[2] - bbox[0]
        height = bbox[3] - bbox[1]

        self.assertGreaterEqual(
            width / image.width,
            0.72,
            "master icon should occupy enough horizontal space to avoid looking smaller than neighboring app icons",
        )
        self.assertGreaterEqual(
            height / image.height,
            0.90,
            "master icon should remain visually full-height in the square canvas",
        )

    def test_rendered_master_uses_balanced_top_and_bottom_padding(self):
        generator = load_generator_module()

        image = generator.render_inner_stroke()
        alpha = image.getchannel("A")
        bbox = alpha.getbbox()

        self.assertIsNotNone(bbox)
        top_padding = bbox[1]
        bottom_padding = image.height - bbox[3]

        self.assertLessEqual(
            abs(top_padding - bottom_padding),
            1,
            "master icon should be vertically centered so top and bottom padding feel equal",
        )


if __name__ == "__main__":
    unittest.main()
