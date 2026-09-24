using System.Collections.Generic;
using ShadowContract.Core;
using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Builds and updates everything visible in a mission: map layers, doors, windows, barrels, cameras, pickups, bodies,
    /// projectiles, characters, lighting and vision cones.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public GameSession Session { get; private set; }
        public CharacterView PlayerView { get; private set; }
        public LightingView Lighting { get; private set; }
        public VisionConeView Cones { get; private set; }

        private readonly Dictionary<int, CharacterView> _npcViews = new Dictionary<int, CharacterView>();
        private readonly Dictionary<int, SpriteRenderer> _bodies = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _pickups = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _projectiles = new Dictionary<int, SpriteRenderer>();
        private readonly List<(DoorState door, Transform panel, SpriteRenderer r, bool secret)> _doors = new List<(DoorState, Transform, SpriteRenderer, bool)>();
        private readonly List<(Int2 cell, SpriteRenderer r)> _windows = new List<(Int2, SpriteRenderer)>();
        private readonly List<(Int2 cell, GameObject go)> _barrels = new List<(Int2, GameObject)>();
        private readonly List<(Npc cam, SpriteRenderer r)> _cameras = new List<(Npc, SpriteRenderer)>();
        private readonly List<(Interactable it, SpriteRenderer r)> _icons = new List<(Interactable, SpriteRenderer)>();
        private readonly List<SpriteRenderer> _extractLines = new List<SpriteRenderer>();
        private Transform _dyn;

        public CharacterView ViewFor(int actorId)
        {
            if (actorId == 0) return PlayerView;
            return _npcViews.TryGetValue(actorId, out var v) ? v : null;
        }

        public void Build(GameSession s)
        {
            Session = s;
            string id = s.Map.Id;

            // ---- static map layers
            AddLayer("MapBase", SpriteLibrary.Get("Maps/" + id + "_base", 0f, 0f), Layers.Map);
            AddLayer("MapOverlay", SpriteLibrary.Get("Maps/" + id + "_overlay", 0f, 0f), Layers.Overlay);
            _dyn = new GameObject("Dynamic").transform;
            _dyn.SetParent(transform, false);

            // ---- doors
            string theme = s.Map.Theme;
            foreach (var d in s.World.Doors)
            {
                bool secret = d.Type == DoorType.Secret;
                string spr = secret ? "Objects/secret_door"
                    : d.Type == DoorType.LockedBlue ? "Objects/door_blue"
                    : d.Type == DoorType.LockedRed ? "Objects/door_red"
                    : theme == "mansion" ? "Objects/door_wood" : theme == "office" ? "Objects/door_glass" : "Objects/door_metal";
                var go = new GameObject("Door");
                go.transform.SetParent(_dyn, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.sortingOrder = Layers.Door;
                if (secret)
                {
                    r.sprite = SpriteLibrary.Get(spr, 0f, 0f);
                    go.transform.position = new Vector3(d.Cell.x, d.Cell.y, 0);
                }
                else
                {
                    r.sprite = SpriteLibrary.Get(spr, 0f, 0.5f);
                    if (d.Horizontal) go.transform.position = new Vector3(d.Cell.x, d.Cell.y + 0.5f, 0);
                    else go.transform.position = new Vector3(d.Cell.x + 0.5f, d.Cell.y, 0);
                }
                _doors.Add((d, go.transform, r, secret));
            }

            // ---- windows & barrels
            for (int y = 0; y < s.World.Height; y++)
            for (int x = 0; x < s.World.Width; x++)
            {
                var t = s.World.TileAt(x, y);
                if (t.Kind == TileKind.Window)
                {
                    var go = new GameObject("Window");
                    go.transform.SetParent(_dyn, false);
                    go.transform.position = new Vector3(x + 0.5f, y + 0.5f, 0);
                    bool vertical = s.World.TileAt(x, y + 1).Kind == TileKind.Wall || s.World.TileAt(x, y - 1).Kind == TileKind.Wall;
                    bool horizontalWall = s.World.TileAt(x - 1, y).Kind == TileKind.Wall || s.World.TileAt(x + 1, y).Kind == TileKind.Wall;
                    if (vertical && !horizontalWall) go.transform.rotation = Quaternion.Euler(0, 0, 90);
                    var r = go.AddComponent<SpriteRenderer>();
                    r.sprite = SpriteLibrary.Get("Objects/window");
                    r.sortingOrder = Layers.Door;
                    _windows.Add((new Int2(x, y), r));
                }
                else if (t.Kind == TileKind.Furniture && t.Furniture == FurnitureType.Barrel)
                {
                    var go = new GameObject("Barrel");
                    go.transform.SetParent(_dyn, false);
                    go.transform.position = new Vector3(x + 0.5f, y + 0.5f, 0);
                    var r = go.AddComponent<SpriteRenderer>();
                    r.sprite = SpriteLibrary.Get("Objects/barrel");
                    r.sortingOrder = Layers.Door;
                    _barrels.Add((new Int2(x, y), go));
                }
            }

            // ---- cameras
            foreach (var n in s.Npcs)
            {
                if (!n.IsCamera) continue;
                var go = new GameObject("Camera");
                go.transform.SetParent(_dyn, false);
                go.transform.position = new Vector3(n.Pos.x, n.Pos.y, 0);
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = SpriteLibrary.Get("Objects/camera", 0.2f, 0.5f);
                r.sortingOrder = Layers.WorldUi - 2;
                _cameras.Add((n, r));
            }

            // ---- interactable icons
            foreach (var it in s.Interactables)
            {
                if (it.Secret) continue;
                string icon = it.Kind == InteractKind.PowerBox ? "Objects/icon_power"
                    : it.Kind == InteractKind.Distraction ? "Objects/icon_music"
                    : it.Kind == InteractKind.Stairs ? "Objects/icon_stairs" : "Objects/icon_terminal";
                var go = new GameObject("Icon");
                go.transform.SetParent(_dyn, false);
                go.transform.position = new Vector3(it.Pos.x, it.Pos.y + 0.55f, 0);
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = SpriteLibrary.Get(icon);
                r.sortingOrder = Layers.WorldUi - 3;
                _icons.Add((it, r));
            }

            // ---- extraction zones (dashed outline)
            var px = SpriteLibrary.Get("FX/pixel", 0f, 0f);
            foreach (var ez in s.Extracts)
            {
                var rc = ez.Rect;
                for (float x = rc.X; x < rc.X + rc.W; x += 0.5f)
                {
                    AddLine(px, new Vector2(x, rc.Y), new Vector2(0.3f, 0.06f));
                    AddLine(px, new Vector2(x, rc.Y + rc.H - 0.06f), new Vector2(0.3f, 0.06f));
                }
                for (float y = rc.Y; y < rc.Y + rc.H; y += 0.5f)
                {
                    AddLine(px, new Vector2(rc.X, y), new Vector2(0.06f, 0.3f));
                    AddLine(px, new Vector2(rc.X + rc.W - 0.06f, y), new Vector2(0.06f, 0.3f));
                }
            }

            // ---- characters
            PlayerView = CharacterView.Create(transform, s.Player, "player", true);
            foreach (var n in s.Npcs)
            {
                if (n.IsCamera) continue;
                _npcViews[n.Id] = CharacterView.Create(transform, n, SpriteLibrary.CharacterTypeFor(n), false);
            }

            Lighting = gameObject.AddComponent<LightingView>();
            Lighting.Build(s, transform);
            Cones = gameObject.AddComponent<VisionConeView>();
            Cones.Build(s, transform);
        }

        private void AddLayer(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = order;
        }

        private void AddLine(Sprite px, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("ExtractLine");
            go.transform.SetParent(_dyn, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0);
            go.transform.localScale = new Vector3(size.x * 4f, size.y * 4f, 1f);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = px;
            r.sortingOrder = Layers.WorldUi - 4;
            r.sharedMaterial = SpriteLibrary.Additive;
            _extractLines.Add(r);
        }

        // ------------------------------------------------------------------ per frame

        public void Sync(float dt)
        {
            var s = Session;
            PlayerView.Sync(dt, s);
            foreach (var kv in _npcViews) kv.Value.Sync(dt, s);

            foreach (var (door, panel, r, secret) in _doors)
            {
                if (secret)
                {
                    // Bookshelf slides into the wall.
                    float off = door.Anim * 0.9f;
                    panel.position = door.Horizontal ? new Vector3(door.Cell.x - off, door.Cell.y, 0) : new Vector3(door.Cell.x, door.Cell.y + off, 0);
                    continue;
                }
                float closed = door.Horizontal ? 0f : 90f;
                panel.rotation = Quaternion.Euler(0, 0, closed + door.Anim * 88f);
                r.color = door.Locked && door.Anim < 0.01f ? Color.white : new Color(0.92f, 0.92f, 0.92f);
            }

            foreach (var (cell, r) in _windows)
                if (s.World.IsWindowBroken(cell.x, cell.y) && r.sprite.name != "Objects/window_broken")
                    r.sprite = SpriteLibrary.Get("Objects/window_broken");

            foreach (var (cell, go) in _barrels)
            {
                var t = s.World.TileAt(cell.x, cell.y);
                if (go.activeSelf && !(t.Kind == TileKind.Furniture && t.Furniture == FurnitureType.Barrel)) go.SetActive(false);
            }

            foreach (var (cam, r) in _cameras)
            {
                bool off = cam.Down || cam.State == AIState.Disabled;
                r.sprite = SpriteLibrary.Get(off ? "Objects/camera_off" : "Objects/camera", 0.2f, 0.5f);
                r.transform.rotation = Quaternion.Euler(0, 0, cam.Facing * Mathf.Rad2Deg);
            }

            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
            foreach (var (it, r) in _icons)
            {
                float d = Vec2.Distance(it.Pos, s.Player.Pos);
                bool done = (it.Kind == InteractKind.CameraTerminal || it.Kind == InteractKind.DownloadTerminal) && it.Used;
                float a = done ? 0.25f : d < 6f ? pulse : 0.45f;
                r.color = new Color(1, 1, 1, a);
            }

            Color ext = s.RequiredDone ? new Color(0.4f, 1f, 0.5f, 0.55f + 0.3f * Mathf.Sin(Time.time * 5f)) : new Color(0.5f, 0.8f, 0.6f, 0.18f);
            foreach (var r in _extractLines) r.color = ext;

            SyncBodies();
            SyncPickups();
            SyncProjectiles();
        }

        private void SyncBodies()
        {
            foreach (var b in Session.Bodies)
            {
                if (!_bodies.TryGetValue(b.Id, out var r))
                {
                    var go = new GameObject("Body");
                    go.transform.SetParent(_dyn, false);
                    r = go.AddComponent<SpriteRenderer>();
                    string type = SpriteLibrary.CharacterTypeFor(b.Npc);
                    r.sprite = SpriteLibrary.Character(type)[(int)SpriteLibrary.Pose.Dead];
                    r.sortingOrder = Layers.Body;
                    _bodies[b.Id] = r;
                    if (b.Npc.Alive == false && FxManager.I != null) FxManager.I.Stain(new Vector2(b.Pos.x, b.Pos.y));
                }
                r.enabled = !b.Hidden;
                r.transform.position = new Vector3(b.Pos.x, b.Pos.y, 0);
                r.transform.rotation = Quaternion.Euler(0, 0, b.Facing * Mathf.Rad2Deg + 180f);
                r.color = b.Npc.Unconscious ? new Color(0.85f, 0.85f, 1f) : Color.white;
            }
        }

        private void SyncPickups()
        {
            float bob = Mathf.Sin(Time.time * 3f) * 0.05f;
            foreach (var p in Session.Pickups)
            {
                if (!_pickups.TryGetValue(p.Id, out var r))
                {
                    var go = new GameObject("Pickup");
                    go.transform.SetParent(_dyn, false);
                    r = go.AddComponent<SpriteRenderer>();
                    r.sprite = PickupSprite(p);
                    r.sortingOrder = Layers.Pickup;
                    float sc = p.Kind == PickupKind.Weapon ? 0.55f : p.Kind == PickupKind.Knives && p.Amount == 1 ? 0.5f : 0.6f;
                    go.transform.localScale = new Vector3(sc, sc, 1);
                    _pickups[p.Id] = r;
                }
                if (p.Taken) { if (r.enabled) r.enabled = false; continue; }
                r.enabled = true;
                r.transform.position = new Vector3(p.Pos.x, p.Pos.y + bob, 0);
            }
        }

        public static Sprite PickupSprite(Pickup p)
        {
            switch (p.Kind)
            {
                case PickupKind.Ammo: return SpriteLibrary.Icon("ammo_pistol");
                case PickupKind.Medkit: return SpriteLibrary.Icon("medkit");
                case PickupKind.Armor: return SpriteLibrary.Icon("armor_light");
                case PickupKind.Cash: return SpriteLibrary.Icon("cash");
                case PickupKind.Keycard: return SpriteLibrary.Icon(p.ItemId == "red" ? "keycard_red" : "keycard_blue");
                case PickupKind.Intel:
                    switch (p.ItemId)
                    {
                        case "ledger": return SpriteLibrary.Icon("ledger");
                        case "samples": return SpriteLibrary.Icon("samples");
                        case "prototype": return SpriteLibrary.Icon("prototype");
                        default: return SpriteLibrary.Icon("intel");
                    }
                case PickupKind.Weapon: return SpriteLibrary.WeaponIcon(p.ItemId);
                case PickupKind.Knives: return p.Amount == 1 ? SpriteLibrary.Get("FX/knife_proj") : SpriteLibrary.Icon("ammo_knives");
                case PickupKind.Coins: return SpriteLibrary.Icon("coin");
                default: return SpriteLibrary.Icon("intel");
            }
        }

        private void SyncProjectiles()
        {
            foreach (var pr in Session.Projectiles)
            {
                if (!_projectiles.TryGetValue(pr.Id, out var r))
                {
                    var go = new GameObject("Projectile");
                    go.transform.SetParent(_dyn, false);
                    r = go.AddComponent<SpriteRenderer>();
                    r.sprite = SpriteLibrary.Get(pr.Kind == ProjectileKind.Knife ? "FX/knife_proj" : "FX/coin_proj");
                    r.sortingOrder = Layers.Projectile;
                    _projectiles[pr.Id] = r;
                }
                r.transform.position = new Vector3(pr.Pos.x, pr.Pos.y, 0);
                r.transform.rotation = Quaternion.Euler(0, 0, pr.Angle * Mathf.Rad2Deg);
            }
            // remove finished projectiles
            List<int> dead = null;
            foreach (var kv in _projectiles)
            {
                bool alive = false;
                foreach (var pr in Session.Projectiles) if (pr.Id == kv.Key && !pr.Done) { alive = true; break; }
                if (!alive) (dead ?? (dead = new List<int>())).Add(kv.Key);
            }
            if (dead != null)
                foreach (int id in dead) { Destroy(_projectiles[id].gameObject); _projectiles.Remove(id); }
        }
    }
}
