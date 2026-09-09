import unittest

from audioscan import cs_parser

SAMPLE = """
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] AudioClip footstepWalkClipA;
    [SerializeField] AudioClip footstepWalkClipB;
    public AudioClip landClip;
    [SerializeField] AudioClip jumpClip;
    [SerializeField] float footstepVolume = 0.35f;
    AudioClip _cachedHum;              // private, no attribute: NOT serialized
    AudioSource sfxSource;

    void Start()
    {
        sfxSource = gameObject.AddComponent<AudioSource>();
        var hum = Resources.Load<AudioClip>("DomeFX/dome_hum");
        sfxSource.PlayOneShot(landClip, landVolume);
    }

    void Step()
    {
        // A LOCAL, not a serialized slot. Reporting this as a sound would put
        // a row in Sam's mixer that corresponds to nothing he can change.
        AudioClip target = (index == 0) ? footstepWalkClipA : footstepWalkClipB;
        footstepsSource.clip = target;
    }

    void Jump()
    {
        // The serialized `jumpClip` is deliberately NO LONGER read: it pointed
        // at the wrong file. The real dead-marker lives here, in a method body,
        // nowhere near the declaration.
        PlayerSuitAudio.Instance?.PlayJump();
    }
}
"""

NAMESPACED = """
namespace Game.Audio
{
    public class Nested : MonoBehaviour
    {
        [SerializeField] AudioClip humClip;

        void Go()
        {
            AudioClip scratch = humClip;
        }
    }
}
"""


class ClipFieldTest(unittest.TestCase):
    def setUp(self):
        self.fields = {f.name: f for f in cs_parser.find_clip_fields(SAMPLE)}

    def test_finds_every_audioclip_field(self):
        # Runtime caches are found too, and flagged via `serialized` rather
        # than dropped here -- scan.py decides what becomes a mixer row.
        self.assertEqual(
            sorted(self.fields),
            [
                "_cachedHum",
                "footstepWalkClipA",
                "footstepWalkClipB",
                "jumpClip",
                "landClip",
            ],
        )

    def test_does_not_mistake_a_float_for_a_clip(self):
        self.assertNotIn("footstepVolume", self.fields)

    def test_marks_a_field_dead_when_a_comment_says_it_is_not_read(self):
        self.assertTrue(self.fields["jumpClip"].dead)

    def test_a_normal_field_is_not_dead(self):
        self.assertFalse(self.fields["landClip"].dead)

    def test_a_local_variable_is_not_a_field(self):
        # `AudioClip target = ...` inside a method body is not a mixable slot.
        self.assertNotIn("target", self.fields)

    def test_dead_marker_is_found_anywhere_in_the_file(self):
        # PlayerController's real marker sits ~960 lines below the declaration.
        self.assertTrue(self.fields["jumpClip"].dead)

    def test_public_field_is_serialized(self):
        self.assertTrue(self.fields["landClip"].serialized)

    def test_serializefield_attribute_is_serialized(self):
        self.assertTrue(self.fields["footstepWalkClipA"].serialized)

    def test_plain_private_field_is_not_serialized(self):
        # Unity does not serialize a private field without [SerializeField],
        # so it is a runtime cache, not a slot anyone can mix.
        self.assertIn("_cachedHum", self.fields)
        self.assertFalse(self.fields["_cachedHum"].serialized)


class NamespacedClassTest(unittest.TestCase):
    def setUp(self):
        self.fields = {f.name: f for f in cs_parser.find_clip_fields(NAMESPACED)}

    def test_finds_fields_inside_a_namespaced_class(self):
        self.assertIn("humClip", self.fields)

    def test_still_excludes_locals_when_nested_a_level_deeper(self):
        self.assertNotIn("scratch", self.fields)


class OtherSignalsTest(unittest.TestCase):
    def test_finds_resources_load_paths(self):
        self.assertEqual(cs_parser.find_resource_loads(SAMPLE), ["DomeFX/dome_hum"])

    def test_finds_the_class_name(self):
        self.assertEqual(cs_parser.class_name(SAMPLE), "PlayerController")

    def test_counts_sources_created_in_code(self):
        self.assertEqual(cs_parser.count_code_sources(SAMPLE), 1)

    def test_a_file_with_no_audio_yields_nothing(self):
        self.assertEqual(cs_parser.find_clip_fields("class Empty {}"), [])


if __name__ == "__main__":
    unittest.main()
