import unittest

from audioscan import yaml_refs

PREFAB = """%YAML 1.1
--- !u!1 &6759884
GameObject:
  m_Name: Player
--- !u!114 &6759885
MonoBehaviour:
  m_GameObject: {fileID: 6759884}
  m_Script: {fileID: 11500000, guid: 0aa13226b151d40ae952589d37a6dd9e, type: 3}
  m_Name:
  footstepWalkClipA: {fileID: 0}
  footstepWalkClipB: {fileID: 0}
  landClip: {fileID: 8300000, guid: 6c9d80c6033e14f469c4646a523c6bbb, type: 3}
  footstepVolume: 0.35
--- !u!114 &6759886
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: ffffffffffffffffffffffffffffffff, type: 3}
  someClip: {fileID: 8300000, guid: 0d5b074c61d987a47a93936925947666, type: 3}
"""

SCENE = """%YAML 1.1
--- !u!1001 &111222333
PrefabInstance:
  m_Modification:
    m_Modifications:
    - target: {fileID: 6759885, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
      propertyPath: footstepWalkClipA
      value:
      objectReference: {fileID: 8300000, guid: 6c9d80c6033e14f469c4646a523c6bbb, type: 3}
    - target: {fileID: 6759885, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
      propertyPath: footstepWalkClipB
      value:
      objectReference: {fileID: 8300000, guid: 0d5b074c61d987a47a93936925947666, type: 3}
    - target: {fileID: 6759885, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
      propertyPath: m_Name
      value: Player
      objectReference: {fileID: 0}
    m_SourcePrefab: {fileID: 100100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
"""

# fileID 6759885 inside the prefab is the PlayerController MonoBehaviour.
PREFAB_COMPONENT_SCRIPTS = {
    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa": {6759885: "0aa13226b151d40ae952589d37a6dd9e"}
}


class DocumentSplitTest(unittest.TestCase):
    def test_splits_on_unity_document_markers(self):
        docs = yaml_refs.split_documents(PREFAB)
        self.assertEqual([d.file_id for d in docs], [6759884, 6759885, 6759886])

    def test_captures_the_script_guid_of_a_monobehaviour(self):
        docs = {d.file_id: d for d in yaml_refs.split_documents(PREFAB)}
        self.assertEqual(
            docs[6759885].script_guid, "0aa13226b151d40ae952589d37a6dd9e"
        )

    def test_a_gameobject_has_no_script_guid(self):
        docs = {d.file_id: d for d in yaml_refs.split_documents(PREFAB)}
        self.assertIsNone(docs[6759884].script_guid)


class AssignmentTest(unittest.TestCase):
    def setUp(self):
        self.found = yaml_refs.scan_components(PREFAB, "NetworkPlayer.prefab")
        self.by_field = {a.field: a for a in self.found}

    def test_reads_an_assigned_clip(self):
        a = self.by_field["landClip"]
        self.assertEqual(a.clip_guid, "6c9d80c6033e14f469c4646a523c6bbb")
        self.assertEqual(a.script_guid, "0aa13226b151d40ae952589d37a6dd9e")

    def test_an_empty_slot_is_reported_with_no_clip(self):
        self.assertIsNone(self.by_field["footstepWalkClipA"].clip_guid)

    def test_empty_slots_are_still_reported_so_gaps_are_visible(self):
        self.assertIn("footstepWalkClipB", self.by_field)

    def test_ignores_non_reference_fields(self):
        self.assertNotIn("footstepVolume", self.by_field)

    def test_ignores_unity_internal_fields(self):
        self.assertNotIn("m_Script", self.by_field)
        self.assertNotIn("m_GameObject", self.by_field)

    def test_records_where_it_was_found(self):
        self.assertEqual(self.by_field["landClip"].source, "NetworkPlayer.prefab")


class OverrideTest(unittest.TestCase):
    def setUp(self):
        self.found = yaml_refs.scan_overrides(
            SCENE, "1.6.7.7.7.unity", PREFAB_COMPONENT_SCRIPTS
        )
        self.by_field = {a.field: a for a in self.found}

    def test_finds_the_clip_the_scene_overrides_in(self):
        a = self.by_field["footstepWalkClipA"]
        self.assertEqual(a.clip_guid, "6c9d80c6033e14f469c4646a523c6bbb")

    def test_attributes_the_override_to_the_right_script(self):
        self.assertEqual(
            self.by_field["footstepWalkClipA"].script_guid,
            "0aa13226b151d40ae952589d37a6dd9e",
        )

    def test_finds_every_overridden_clip(self):
        self.assertEqual(
            sorted(self.by_field), ["footstepWalkClipA", "footstepWalkClipB"]
        )

    def test_ignores_overrides_that_are_not_object_references(self):
        self.assertNotIn("m_Name", self.by_field)

    def test_records_the_scene_as_the_source(self):
        self.assertEqual(
            self.by_field["footstepWalkClipB"].source, "1.6.7.7.7.unity"
        )

    def test_unknown_prefab_is_skipped_rather_than_crashing(self):
        self.assertEqual(yaml_refs.scan_overrides(SCENE, "s.unity", {}), [])


class ComponentScriptMapTest(unittest.TestCase):
    def test_maps_component_file_ids_to_their_script_guids(self):
        self.assertEqual(
            yaml_refs.component_script_map(PREFAB),
            {
                6759885: "0aa13226b151d40ae952589d37a6dd9e",
                6759886: "ffffffffffffffffffffffffffffffff",
            },
        )


if __name__ == "__main__":
    unittest.main()
