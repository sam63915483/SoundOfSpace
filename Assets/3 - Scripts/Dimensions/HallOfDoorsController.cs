using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D7 "Hall of Doors" (v2): rooms of three colored doors. Look at a door within reach
/// and press F to open it — it reveals the next room, and closes behind you once you
/// step through. The true path (an alien in the world gives the clue) is:
/// RED, RED, WHITE, BLACK, BLACK, BLACK → exit arch to the next dimension.
/// Any wrong color silently derails you into endless rooms of random doors.
/// </summary>
public class HallOfDoorsController : MonoBehaviour
{
    enum DoorColor { Red, Yellow, Blue, White, Black, Green, Purple, Orange }

    static readonly DoorColor[] Sequence = { DoorColor.Red, DoorColor.Red, DoorColor.White, DoorColor.Black, DoorColor.Black, DoorColor.Black };

    class Door
    {
        public Transform panel;          // the sliding colored slab
        public Transform promptAnchor;
        public DoorColor color;
        public Vector3 outDir;           // world direction this door leads
        public bool interactive;
    }

    class RoomShell
    {
        public Transform root;
        public Door[] doors = new Door[4];   // +x, -x, +z, -z
        public Material[] panelMats = new Material[4];
    }

    const float RoomSize = 9f;           // interior span
    const float WallHalf = RoomSize * 0.5f;

    RoomShell _shellA, _shellB;
    RoomShell _current, _other;
    int _progress;                       // index into Sequence while on the true path
    bool _onPath = true;
    bool _finished;
    Door _openDoor;                      // door currently sliding/standing open
    float _openT;                        // 0 closed → 1 open
    bool _transiting;                    // player crossing into the other shell
    Vector3 _pendingRoomCenter;
    GameObject _promptOwner;             // door panel currently owning the shared interact prompt
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;
    Material _carpetMat, _wallMat, _ceilMat, _frameMat, _lampMat;

    static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

    void Awake()
    {
        _carpetMat = DimensionSceneUtil.Mat(new Color(0.30f, 0.12f, 0.12f), 0.05f);
        _wallMat   = DimensionSceneUtil.Mat(new Color(0.52f, 0.46f, 0.36f), 0.1f);
        _ceilMat   = DimensionSceneUtil.Mat(new Color(0.28f, 0.26f, 0.24f), 0.1f);
        _frameMat  = DimensionSceneUtil.Mat(new Color(0.20f, 0.13f, 0.08f), 0.15f);
        _lampMat   = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.6f), 1.4f);

        _shellA = BuildShell("RoomA");
        _shellB = BuildShell("RoomB");
        _current = _shellA;
        _other = _shellB;
        _other.root.gameObject.SetActive(false);

        _current.root.position = Vector3.zero;
        // Room 0 is always Red / Yellow / Blue on three walls; the fourth is sealed.
        DressRoom(_current, new[] { DoorColor.Red, DoorColor.Yellow, DoorColor.Blue }, sealedWall: 3);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(110f, 2f, 0.04f), 300f, 1f);
    }

    // One reusable room: carpet, ceiling, lamp, and four walls each split around a
    // central doorway with a slidable panel. Doors not in use are sealed by their panel.
    RoomShell BuildShell(string name)
    {
        var shell = new RoomShell();
        var root = new GameObject(name);
        root.transform.SetParent(transform, false);
        shell.root = root.transform;

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Carpet", new Vector3(0f, -0.15f, 0f), new Vector3(RoomSize + 1f, 0.3f, RoomSize + 1f), _carpetMat, shell.root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceil", new Vector3(0f, 3.9f, 0f), new Vector3(RoomSize + 1f, 0.3f, RoomSize + 1f), _ceilMat, shell.root);
        var lamp = DimensionSceneUtil.Block(PrimitiveType.Cube, "Lamp", new Vector3(0f, 3.7f, 0f), new Vector3(1f, 0.1f, 1f), _lampMat, shell.root);
        Destroy(lamp.GetComponent<Collider>());
        var lightGo = new GameObject("LampLight");
        lightGo.transform.SetParent(shell.root, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.6f, 0f);
        var pl = lightGo.AddComponent<Light>();
        pl.type = LightType.Point; pl.range = 12f; pl.intensity = 1.2f;
        pl.color = new Color(1f, 0.88f, 0.65f);

        for (int w = 0; w < 4; w++)
        {
            Vector3 dir = Dirs[w];
            Vector3 side = Vector3.Cross(Vector3.up, dir);            // along the wall
            Vector3 wallCenter = dir * WallHalf;
            // Two wall segments flanking a 1.6m doorway + lintel above it.
            float segLen = (RoomSize - 1.6f) * 0.5f;
            Vector3 segScale = new Vector3(Mathf.Abs(side.x) * segLen + Mathf.Abs(dir.x) * 0.3f, 3.8f,
                                           Mathf.Abs(side.z) * segLen + Mathf.Abs(dir.z) * 0.3f);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "WallSegA" + w,
                wallCenter + side * (0.8f + segLen * 0.5f) + Vector3.up * 1.9f, segScale, _wallMat, shell.root);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "WallSegB" + w,
                wallCenter - side * (0.8f + segLen * 0.5f) + Vector3.up * 1.9f, segScale, _wallMat, shell.root);
            Vector3 lintelScale = new Vector3(Mathf.Abs(side.x) * 1.6f + Mathf.Abs(dir.x) * 0.3f, 1f,
                                              Mathf.Abs(side.z) * 1.6f + Mathf.Abs(dir.z) * 0.3f);
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel" + w,
                wallCenter + Vector3.up * 3.3f, lintelScale, _wallMat, shell.root);
            // Threshold under the doorway (bridges the gap between butted rooms).
            DimensionSceneUtil.Block(PrimitiveType.Cube, "Threshold" + w,
                wallCenter + Vector3.up * -0.15f, new Vector3(Mathf.Abs(side.x) * 1.6f + Mathf.Abs(dir.x) * 1.2f, 0.3f,
                                                              Mathf.Abs(side.z) * 1.6f + Mathf.Abs(dir.z) * 1.2f), _carpetMat, shell.root);

            // The sliding colored panel (its own material instance per wall). Deep
            // (0.45m) on purpose: thin panels let the player controller tunnel through.
            shell.panelMats[w] = DimensionSceneUtil.Mat(Color.grey, 0.25f);
            Vector3 panelScale = new Vector3(Mathf.Abs(side.x) * 1.55f + Mathf.Abs(dir.x) * 0.45f, 2.75f,
                                             Mathf.Abs(side.z) * 1.55f + Mathf.Abs(dir.z) * 0.45f);
            var panel = DimensionSceneUtil.Block(PrimitiveType.Cube, "Panel" + w,
                wallCenter + Vector3.up * 1.4f, panelScale, shell.panelMats[w], shell.root);

            var anchor = new GameObject("PromptAnchor" + w);
            anchor.transform.SetParent(shell.root, false);
            anchor.transform.localPosition = wallCenter + Vector3.up * 3.0f - dir * 0.6f;

            shell.doors[w] = new Door { panel = panel.transform, promptAnchor = anchor.transform, outDir = dir };
        }
        return shell;
    }

    // Assign colors: three interactive doors, one sealed wall (the one you came from).
    void DressRoom(RoomShell shell, DoorColor[] colors, int sealedWall)
    {
        int c = 0;
        for (int w = 0; w < 4; w++)
        {
            var door = shell.doors[w];
            door.panel.gameObject.SetActive(true);
            SetPanelClosed(shell, w);
            if (w == sealedWall)
            {
                door.interactive = false;
                shell.panelMats[w].color = new Color(0.25f, 0.22f, 0.2f);   // inert, unremarkable
            }
            else
            {
                door.interactive = true;
                door.color = colors[c++];
                ApplyDoorColor(shell.panelMats[w], door.color);
            }
        }
    }

    void ApplyDoorColor(Material m, DoorColor c)
    {
        Color col = c switch
        {
            DoorColor.Red => new Color(0.75f, 0.1f, 0.1f),
            DoorColor.Yellow => new Color(0.85f, 0.75f, 0.15f),
            DoorColor.Blue => new Color(0.15f, 0.3f, 0.8f),
            DoorColor.White => new Color(0.92f, 0.92f, 0.9f),
            DoorColor.Black => new Color(0.04f, 0.04f, 0.05f),
            DoorColor.Green => new Color(0.15f, 0.6f, 0.2f),
            DoorColor.Purple => new Color(0.5f, 0.15f, 0.65f),
            _ => new Color(0.85f, 0.45f, 0.1f),
        };
        m.color = col;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", col * 0.35f);          // readable in dim light
    }

    void SetPanelClosed(RoomShell shell, int w)
    {
        var p = shell.doors[w].panel;
        Vector3 lp = p.localPosition;
        p.localPosition = new Vector3(lp.x, 1.4f, lp.z);
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.17f, 0.15f, 0.12f),
                fog: new Color(0.05f, 0.04f, 0.035f), fogDensity: 0.03f,
                background: new Color(0.03f, 0.025f, 0.02f));
            _atmosApplied = true;
        }
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        Vector3 playerPos = _player.Rigidbody.position;

        // Animate the open door panel sliding into the floor. (Runs even after the
        // final door — gating this on _finished left the last door shut forever.)
        if (_openDoor != null)
        {
            _openT = Mathf.MoveTowards(_openT, 1f, Time.deltaTime / 0.45f);
            Vector3 lp = _openDoor.panel.localPosition;
            _openDoor.panel.localPosition = new Vector3(lp.x, Mathf.Lerp(1.4f, -1.6f, _openT), lp.z);
        }

        // Transit: once the player is inside the newly revealed room, shut the door
        // behind them and recycle the old shell.
        if (_transiting)
        {
            Vector3 flat = playerPos - _pendingRoomCenter; flat.y = 0f;
            if (Mathf.Abs(flat.x) < WallHalf - 1f && Mathf.Abs(flat.z) < WallHalf - 1f)
                CompleteTransit();
            ClearPrompt();
            return;                                          // no interactions mid-transit
        }
        if (_finished) { ClearPrompt(); return; }            // exit room has no doors to open

        // Gaze + proximity door prompt — the game's standard interact pill.
        Door target = null;
        foreach (var d in _current.doors)
        {
            if (!d.interactive) continue;
            Vector3 doorWorld = d.panel.position;
            if (Vector3.Distance(playerPos, doorWorld) > interactDistance) continue;
            Vector3 to = doorWorld - cam.transform.position;
            if (Vector3.Angle(cam.transform.forward, to) > 22f) continue;
            target = d;
            break;
        }

        if (target != null)
        {
            var ownerGo = target.panel.gameObject;
            if (_promptOwner != ownerGo) ClearPrompt();
            _promptOwner = ownerGo;
            InteractPromptUI.Show(ownerGo, "Press F to open");
            bool pressed = Input.GetKeyDown(KeyCode.F) || TutorialGate.PadPressed(TutorialGate.PadButton.X);
            if (pressed) OpenDoor(target);
        }
        else
            ClearPrompt();
    }

    void ClearPrompt()
    {
        if (_promptOwner == null) return;
        InteractPromptUI.Clear(_promptOwner);
        _promptOwner = null;
    }

    void OpenDoor(Door door)
    {
        ClearPrompt();

        bool correct = _onPath && door.color == Sequence[Mathf.Min(_progress, Sequence.Length - 1)];
        if (correct) _progress++;
        else _onPath = false;                                // silently derailed — endless rooms

        // Place the other shell beyond the opened door and dress it for what comes next.
        Vector3 nextCenter = _current.root.position + door.outDir * (RoomSize + 0.6f);
        _other.root.position = nextCenter;
        _other.root.gameObject.SetActive(true);

        int entryWall = System.Array.IndexOf(Dirs, -door.outDir);
        if (_onPath && _progress >= Sequence.Length)
            DressExitRoom(_other, entryWall);
        else
            DressRoom(_other, NextColors(), entryWall);

        // Open BOTH aligned panels: this room's door and the next room's entry panel.
        _openDoor = door;
        _openT = 0f;
        var entryPanel = _other.doors[entryWall].panel;
        entryPanel.gameObject.SetActive(false);              // entry side simply stands open
        _pendingRoomCenter = nextCenter;
        _transiting = true;
    }

    void CompleteTransit()
    {
        _transiting = false;
        // Shut the door behind the player: restore both panels, seal the entry wall.
        if (_openDoor != null) { SetPanelClosed(_current, System.Array.IndexOf(_current.doors, _openDoor)); }
        _openDoor = null;
        foreach (var d in _other.doors) d.panel.gameObject.SetActive(true);

        var old = _current;
        _current = _other;
        _other = old;
        _other.root.gameObject.SetActive(false);             // old room vanishes behind the shut door
    }

    // Colors for the next room: on the path, the required color plus two distinct
    // decoys; off the path, three distinct randoms forever.
    DoorColor[] NextColors()
    {
        var all = (DoorColor[])System.Enum.GetValues(typeof(DoorColor));
        var picks = new List<DoorColor>();
        if (_onPath) picks.Add(Sequence[_progress]);
        while (picks.Count < 3)
        {
            var c = all[Random.Range(0, all.Length)];
            if (!picks.Contains(c)) picks.Add(c);
        }
        // Shuffle so the required color isn't always on the same wall.
        for (int i = 0; i < picks.Count; i++)
        {
            int j = Random.Range(i, picks.Count);
            (picks[i], picks[j]) = (picks[j], picks[i]);
        }
        return picks.ToArray();
    }

    // The final room: a glowing arch straight ahead — walk through to leave.
    void DressExitRoom(RoomShell shell, int entryWall)
    {
        _finished = true;
        for (int w = 0; w < 4; w++)
        {
            shell.doors[w].interactive = false;
            shell.panelMats[w].color = new Color(0.25f, 0.22f, 0.2f);
            shell.doors[w].panel.gameObject.SetActive(true);
            SetPanelClosed(shell, w);
        }
        var glow = DimensionSceneUtil.EmissiveMat(new Color(0.75f, 0.95f, 1f), 2.5f);
        var arch = DimensionSceneUtil.Block(PrimitiveType.Cube, "ExitArch",
            shell.root.position + new Vector3(0f, 1.5f, 0f), new Vector3(1.6f, 3f, 0.1f), glow, shell.root);
        Destroy(arch.GetComponent<Collider>());
        DimensionSceneUtil.CreatePortal("ToNext", shell.root.position + new Vector3(0f, 1.5f, 0f),
            new Vector3(1.6f, 3f, 0.8f), LevelPortal.PortalAction.EnterInterior, nextScene, shell.root);
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Interaction")]
    [Tooltip("How close you must be to a door for the open prompt.")]
    public float interactDistance = 2.2f;

    [Header("Exit")]
    [Tooltip("Scene the completed sequence leads to.")]
    public string nextScene = "D8_Procession";
}
