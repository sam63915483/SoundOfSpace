import unittest

from audioscan import manifest


class ScopeTest(unittest.TestCase):
    def test_gameplay_scripts_are_in_scope(self):
        self.assertTrue(manifest.in_scope("3 - Scripts/Survival/OxygenManager.cs"))

    def test_separate_scenes_are_out_of_scope(self):
        for path in (
            "3 - Scripts/Dimensions/OrchardController.cs",
            "3 - Scripts/Poolrooms/DrowningController.cs",
            "3 - Scripts/Cutscenes/DeathCutsceneAudio.cs",
        ):
            self.assertFalse(manifest.in_scope(path), path)

    def test_third_party_packs_are_out_of_scope(self):
        self.assertFalse(manifest.in_scope("5 - External Imports/Foo/Bar.cs"))


class GroupTest(unittest.TestCase):
    def test_player_controller_is_movement(self):
        self.assertEqual(
            manifest.group_for(
                "3 - Scripts/Scripts/Game/Controllers/PlayerController.cs"
            ),
            "Movement",
        )

    def test_ship_is_ship(self):
        self.assertEqual(manifest.group_for("3 - Scripts/Ship/ShipReactor.cs"), "Ship")

    def test_smuggling_radio_lines_are_voice(self):
        self.assertEqual(
            manifest.group_for("3 - Scripts/Story/TevSmugglingMission.cs"), "Voice"
        )

    def test_an_unmapped_folder_falls_back_to_other(self):
        self.assertEqual(manifest.group_for("3 - Scripts/Weird/Thing.cs"), "Other")

    def test_a_more_specific_rule_beats_a_folder_rule(self):
        # Ship.cs lives under Game/Controllers/ but belongs to the Ship group.
        self.assertEqual(
            manifest.group_for("3 - Scripts/Scripts/Game/Controllers/Ship.cs"), "Ship"
        )


class KeyTest(unittest.TestCase):
    def test_key_is_snake_cased_class_and_field(self):
        self.assertEqual(
            manifest.make_key("PlayerController", "footstepWalkClipA"),
            "player_controller.footstep_walk_clip_a",
        )

    def test_leading_underscore_field_does_not_double_up(self):
        self.assertEqual(
            manifest.make_key("OxygenManager", "_alarmClip"),
            "oxygen_manager.alarm_clip",
        )

    def test_key_does_not_contain_the_group(self):
        # groups are editable; keys must be stable forever
        self.assertNotIn("movement", manifest.make_key("PlayerController", "x"))


class MergeTest(unittest.TestCase):
    def setUp(self):
        self.draft = [
            {
                "key": "player_controller.footstep_walk_clip_a",
                "label": "PlayerController.footstepWalkClipA",
                "group": "Movement",
                "clip": "transfer/audio/walk.mp3",
                "scanned_clip": "transfer/audio/walk.mp3",
                "volume": 1.0,
                "bus": "SFX",
                "note": "",
                "dead": False,
                "missing": False,
            }
        ]
        self.existing = {
            "version": 1,
            "sounds": [
                {
                    "key": "player_controller.footstep_walk_clip_a",
                    "label": "Walking - step A",
                    "group": "Movement",
                    "clip": "transfer/audio/walk.mp3",
                    "scanned_clip": "transfer/audio/walk.mp3",
                    "volume": 0.35,
                    "bus": "SFX",
                    "note": "too loud",
                    "dead": False,
                    "missing": False,
                }
            ],
        }

    def test_keeps_the_human_label(self):
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(out["sounds"][0]["label"], "Walking - step A")

    def test_keeps_the_tuned_volume(self):
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(out["sounds"][0]["volume"], 0.35)

    def test_keeps_the_note(self):
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(out["sounds"][0]["note"], "too loud")

    def test_takes_the_new_clip_when_the_scan_finds_a_different_one(self):
        # untouched by the user (clip == scanned_clip), so Unity's change wins
        self.draft[0]["clip"] = "Audio/Player/new_walk.wav"
        self.draft[0]["scanned_clip"] = "Audio/Player/new_walk.wav"
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(out["sounds"][0]["clip"], "Audio/Player/new_walk.wav")

    def test_a_clip_the_human_swapped_survives_a_rescan(self):
        # Sam swapped the clip in the browser; the scan still sees the old one
        # wired in the Editor. His choice must win, or the tool is useless.
        self.existing["sounds"][0]["clip"] = "Audio/Studio/Movement/better_walk.wav"
        # scanned_clip stays at the Editor's value - the divergence is the signal
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(
            out["sounds"][0]["clip"], "Audio/Studio/Movement/better_walk.wav"
        )

    def test_adds_a_newly_discovered_sound(self):
        self.draft.append(dict(self.draft[0], key="ship.thruster", label="x"))
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(len(out["sounds"]), 2)

    def test_marks_a_vanished_sound_missing_instead_of_deleting_it(self):
        out = manifest.merge([], self.existing)
        self.assertEqual(len(out["sounds"]), 1)
        self.assertTrue(out["sounds"][0]["missing"])

    def test_a_returning_sound_is_no_longer_missing(self):
        gone = manifest.merge([], self.existing)
        back = manifest.merge(self.draft, gone)
        self.assertFalse(back["sounds"][0]["missing"])

    def test_dead_flag_comes_from_the_scan_not_the_human(self):
        self.draft[0]["dead"] = True
        out = manifest.merge(self.draft, self.existing)
        self.assertTrue(out["sounds"][0]["dead"])

    def test_output_is_sorted_by_group_then_label(self):
        self.draft.append(dict(self.draft[0], key="a.b", group="Ship", label="Aaa"))
        out = manifest.merge(self.draft, {"version": 1, "sounds": []})
        self.assertEqual([s["group"] for s in out["sounds"]], ["Movement", "Ship"])

    def test_merging_into_nothing_works(self):
        out = manifest.merge(self.draft, None)
        self.assertEqual(len(out["sounds"]), 1)


if __name__ == "__main__":
    unittest.main()
