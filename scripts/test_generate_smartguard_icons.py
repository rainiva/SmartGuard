import importlib.util
import tempfile
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
SCRIPT_PATH = ROOT / "scripts" / "generate-smartguard-icons.py"


def load_generator_module():
    spec = importlib.util.spec_from_file_location("sg_icons", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class SmallFootprintTests(unittest.TestCase):
    def test_rendered_small_icons_fill_taskbar_canvas(self):
        generator = load_generator_module()

        for size, min_width_ratio in ((16, 0.90), (24, 0.88), (32, 0.88)):
            with self.subTest(size=size):
                image = generator.render_small_icon(size)
                bbox = image.getchannel("A").getbbox()
                self.assertIsNotNone(bbox)
                width_ratio = (bbox[2] - bbox[0]) / image.width
                self.assertGreaterEqual(
                    width_ratio,
                    min_width_ratio,
                    f"{size}px icon should not look smaller than neighboring taskbar icons",
                )


class PromoteOutputTests(unittest.TestCase):
    def test_promote_does_not_emit_retired_lib_icon(self):
        generator = load_generator_module()

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_root = Path(temp_dir)
            generator.FINAL_SET_DIR = temp_root / "final-set"
            generator.ASSETS_DIR = temp_root / "assets"
            generator.PREVIEW_DIR = temp_root / "preview"
            lib_dir = temp_root / "lib"

            images = {
                size: Image.new("RGBA", (size, size), (0, 0, 0, 0))
                for size in generator.ALL_SIZES
            }

            generator.promote(images)

            self.assertTrue((generator.FINAL_SET_DIR / "SmartGuard.ico").exists())
            self.assertFalse((lib_dir / "SmartGuard.ico").exists())


if __name__ == "__main__":
    unittest.main()
