import os
import unittest

from audioscan import meta_index

HERE = os.path.dirname(os.path.abspath(__file__))
FIXTURE = os.path.join(HERE, "fixtures", "meta")


class MetaIndexTest(unittest.TestCase):
    def setUp(self):
        self.index = meta_index.build_index(FIXTURE)

    def test_resolves_an_audio_guid_to_its_path(self):
        self.assertEqual(
            self.index.path_for("6c9d80c6033e14f469c4646a523c6bbb"),
            "Audio/walk.mp3",
        )

    def test_resolves_a_script_guid_to_its_path(self):
        self.assertEqual(
            self.index.path_for("0aa13226b151d40ae952589d37a6dd9e"),
            "Scripts/PlayerController.cs",
        )

    def test_unknown_guid_returns_none_rather_than_raising(self):
        self.assertIsNone(self.index.path_for("deadbeef" * 4))

    def test_guid_lookup_is_reversible(self):
        self.assertEqual(
            self.index.guid_for("Audio/walk.mp3"),
            "6c9d80c6033e14f469c4646a523c6bbb",
        )

    def test_script_guids_maps_only_cs_files(self):
        self.assertEqual(
            self.index.script_guids(),
            {"0aa13226b151d40ae952589d37a6dd9e": "Scripts/PlayerController.cs"},
        )

    def test_audio_guids_maps_only_audio_files(self):
        self.assertEqual(
            self.index.audio_guids(),
            {"6c9d80c6033e14f469c4646a523c6bbb": "Audio/walk.mp3"},
        )

    def test_meta_without_its_asset_is_ignored(self):
        orphan = os.path.join(FIXTURE, "Audio", "ghost.mp3.meta")
        with open(orphan, "w", encoding="utf-8") as f:
            f.write("fileFormatVersion: 2\nguid: " + ("ab" * 16) + "\n")
        try:
            index = meta_index.build_index(FIXTURE)
            self.assertIsNone(index.path_for("ab" * 16))
        finally:
            os.remove(orphan)


if __name__ == "__main__":
    unittest.main()
