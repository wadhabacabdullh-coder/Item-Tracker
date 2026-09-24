using ShadowContract.Core;
using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Runs one mission: owns the <see cref="GameSession"/>, steps it at a fixed 60 Hz, gathers input,
    /// and turns simulation events into audio, effects and HUD feedback.
    /// </summary>
    public sealed class MissionRunner : MonoBehaviour
    {
        public GameSession Session { get; private set; }
        public WorldView View { get; private set; }
        public MissionDef Mission { get; private set; }
        public bool Paused { get; set; }
        public bool Ended { get; private set; }

        private readonly PlayerInput _input = new PlayerInput();
        private float _accum;
        private float _endTimer = -1f;
        private float _distractionTimer;
        private HUD _hud;
        private readonly System.Collections.Generic.Dictionary<int, float> _npcSteps = new System.Collections.Generic.Dictionary<int, float>();

        public void Begin(MissionDef mission, MapData map, LoadoutConfig loadout, SettingsData settings, HUD hud)
        {
            Mission = mission;
            _hud = hud;
            Session = new GameSession(map, mission, settings.Difficulty, loadout, Random.Range(1, 1 << 30));
            View = new GameObject("World").AddComponent<WorldView>();
            View.transform.SetParent(transform, false);
            View.Build(Session);
            View.Cones.Visible = settings.ShowVisionCones;
            CameraRig.I.Snap(new Vector2(Session.Player.Pos.x, Session.Player.Pos.y));
            AudioManager.I.Listener = CameraRig.I.transform;
            MusicDirector.I.StartMission(map.Theme);
            FxManager.I.Clear();
            _hud.Bind(this);
            _hud.Banner(mission.Name.ToUpperInvariant(), mission.Location, new Color(0.9f, 0.9f, 0.95f));
        }

        private void OnDestroy()
        {
            if (AudioManager.I != null) AudioManager.I.StopAll();
        }

        // ------------------------------------------------------------------ input

        private void GatherInput()
        {
            var s = Session;
            Vector2 move = Vector2.zero;
            if (GameInput.Held(GameAction.MoveUp)) move.y += 1;
            if (GameInput.Held(GameAction.MoveDown)) move.y -= 1;
            if (GameInput.Held(GameAction.MoveRight)) move.x += 1;
            if (GameInput.Held(GameAction.MoveLeft)) move.x -= 1;
            _input.Move = new Vec2(move.x, move.y);
            Vector2 aim = CameraRig.I.ScreenToWorld(GameInput.MousePosition);
            _input.Aim = new Vec2(aim.x, aim.y);
            _input.FireHeld = GameInput.Held(GameAction.Attack) && !_hud.PointerOverUi;
            _input.AltHeld = GameInput.Held(GameAction.Aim);
            _input.InteractHeld = GameInput.Held(GameAction.Interact);
            _input.SprintHeld = GameInput.Held(GameAction.Sprint);
            // Edges accumulate until a simulation tick consumes them (important at high frame rates).
            _input.FirePressed |= GameInput.Pressed(GameAction.Attack) && !_hud.PointerOverUi;
            _input.AltPressed |= GameInput.Pressed(GameAction.Aim);
            _input.ReloadPressed |= GameInput.Pressed(GameAction.Reload);
            _input.InteractPressed |= GameInput.Pressed(GameAction.Interact);
            _input.CrouchPressed |= GameInput.Pressed(GameAction.Crouch);
            _input.MedkitPressed |= GameInput.Pressed(GameAction.Medkit);
            if (GameInput.Pressed(GameAction.Slot1)) _input.SelectSlot = 0;
            if (GameInput.Pressed(GameAction.Slot2)) _input.SelectSlot = 1;
            if (GameInput.Pressed(GameAction.Slot3)) _input.SelectSlot = 2;
            if (GameInput.Pressed(GameAction.Slot4)) _input.SelectSlot = 3;
            if (GameInput.Pressed(GameAction.Slot5)) _input.SelectSlot = 4;
            float scroll = GameInput.Scroll;
            if (scroll > 0.01f) _input.Cycle = -1;
            else if (scroll < -0.01f) _input.Cycle = 1;
        }

        // ------------------------------------------------------------------ loop

        private void Update()
        {
            if (Session == null) return;
            float dt = Time.deltaTime;

            if (!Ended && !Paused && GameManager.I.UiToggleFrame != Time.frameCount)
            {
                if (GameInput.Pressed(GameAction.Pause)) { GameManager.I.PauseMission(); return; }
                if (GameInput.Pressed(GameAction.Inventory)) { GameManager.I.ToggleInventory(); return; }
            }

            if (!Paused && !Ended)
            {
                GatherInput();
                _accum += dt;
                int steps = 0;
                while (_accum >= GameSession.Tick && steps < 5)
                {
                    Session.Update(GameSession.Tick, _input);
                    _input.ClearEdges();
                    HandleEvents();
                    _accum -= GameSession.Tick;
                    steps++;
                    if (Session.State != MissionState.Playing) break;
                }
                if (steps == 5) _accum = 0f; // don't spiral after a hitch
                UpdateDistractionAudio(dt);
                UpdateNpcFootsteps();
            }

            if (View != null)
            {
                View.Sync(dt);
                var p = Session.Player;
                Vector2 aim = new Vector2(_input.Aim.x, _input.Aim.y);
                CameraRig.I.Follow(new Vector2(p.Pos.x, p.Pos.y), aim, p.Aiming, Time.unscaledDeltaTime);
                MusicDirector.I.SetMood(Session.Alert == AlertLevel.Combat ? MusicMood.Combat : Session.Alert >= AlertLevel.Alarmed ? MusicMood.Tension : MusicMood.Calm, Session.DetectionLevel);
                MusicDirector.I.SetHeartbeat(p.Health / p.MaxHealth);
            }

            // Mission end: short beat before the result screen.
            if (!Ended && Session.State != MissionState.Playing)
            {
                if (_endTimer < 0f)
                {
                    _endTimer = Session.State == MissionState.Complete ? 1.2f : 2.0f;
                    if (Session.State != MissionState.Complete) Time.timeScale = 0.35f;
                    if (Session.State == MissionState.Complete) _hud.Banner("CONTRACT COMPLETE", "Extraction successful", new Color(0.5f, 1f, 0.6f));
                    else _hud.Banner(Session.State == MissionState.Dead ? "YOU DIED" : "CONTRACT FAILED", Session.FailReason ?? "", new Color(1f, 0.35f, 0.35f));
                }
                _endTimer -= Time.unscaledDeltaTime;
                if (_endTimer <= 0f)
                {
                    Ended = true;
                    Time.timeScale = 1f;
                    GameManager.I.OnMissionEnded(this);
                }
            }
        }

        private void UpdateDistractionAudio(float dt)
        {
            _distractionTimer -= dt;
            if (_distractionTimer > 0f) return;
            _distractionTimer = 1.9f;
            foreach (var it in Session.Interactables)
                if (it.Kind == InteractKind.Distraction && it.Active)
                    AudioManager.I.PlayAt("distraction_" + it.Sound, V(it.Pos), 0.8f, 16f, 0f, 0f);
        }

        /// <summary>Guards' footsteps are audible nearby: a key stealth cue for approaching patrols.</summary>
        private void UpdateNpcFootsteps()
        {
            var p = Session.Player.Pos;
            foreach (var n in Session.Npcs)
            {
                if (n.Down || n.IsCamera) continue;
                if (Vec2.SqrDistance(n.Pos, p) > 12f * 12f) continue;
                _npcSteps.TryGetValue(n.Id, out float last);
                float stride = n.Speed > 3.5f ? 1.25f : 0.95f;
                if (n.MoveAnim - last < stride) continue;
                _npcSteps[n.Id] = n.MoveAnim;
                var t = Session.World.TileAt((int)System.Math.Floor(n.Pos.x), (int)System.Math.Floor(n.Pos.y));
                float loud = n.Speed > 3.5f ? 0.55f : n.IsHostile ? 0.32f : 0.22f;
                AudioManager.I.PlayAt("step_" + GameSession.SurfaceName(t), V(n.Pos), loud, 12f, 0.12f, 0f, true);
            }
        }

        private static Vector2 V(Vec2 v) => new Vector2(v.x, v.y);

        // ------------------------------------------------------------------ events -> presentation

        private void HandleEvents()
        {
            var fx = FxManager.I;
            var au = AudioManager.I;
            foreach (var e in Session.Events)
            {
                Vector2 pos = V(e.Pos);
                switch (e.Type)
                {
                    case EvType.Shot:
                    {
                        fx.MuzzleFlash(pos, e.Angle, e.Flag);
                        au.PlayAt(e.Sound, pos, e.Flag ? 0.7f : 1f, e.Flag ? 10f : 34f, 0.05f, 0.03f);
                        var cv = View.ViewFor(e.ActorId);
                        if (cv != null) cv.Recoil(e.Flag ? 0.05f : 0.09f);
                        if (e.ActorId == 0)
                        {
                            CameraRig.I.Kick(new Vector2(Mathf.Cos(e.Angle), Mathf.Sin(e.Angle)), 0.05f + e.Value * 0.2f);
                            if (e.Value > 0.2f) CameraRig.I.Shake(e.Value * 0.5f);
                        }
                        break;
                    }
                    case EvType.Tracer:
                        fx.Tracer(pos, V(e.Pos2), e.ActorId == 0);
                        break;
                    case EvType.Casing:
                        fx.Casing(pos, e.Angle, e.Sound == "shell");
                        break;
                    case EvType.Impact:
                    {
                        bool metal = e.Text == "Machine" || e.Text == "ServerRack" || e.Text == "Vending" || e.Text == "Cabinet" || e.Text == "Car" || e.Text == "Door";
                        fx.Impact(pos, V(e.Pos2), metal);
                        au.PlayAt(metal ? "impact_metal" : "impact", pos, 0.5f, 14f, 0.15f, 0.04f);
                        break;
                    }
                    case EvType.Hit:
                        fx.Hit(pos, e.Angle, e.Flag);
                        au.PlayAt("hit", pos, 0.7f, 16f, 0.1f, 0.03f);
                        View.ViewFor(e.ActorId)?.Flash();
                        break;
                    case EvType.Death:
                        if (e.Flag && e.ActorId > 0 && IsCamera(e.ActorId)) { fx.Sparks(pos, 10, new Color(1f, 0.9f, 0.5f)); au.PlayAt("impact_metal", pos); break; }
                        au.PlayAt("body_fall", pos, 0.7f, 14f);
                        if (!e.Flag) PlayVoice(e.ActorId, "pain", pos);
                        break;
                    case EvType.Melee:
                        View.ViewFor(e.ActorId)?.Swing();
                        au.PlayAt("knife", pos, 0.6f, 10f, 0.12f);
                        break;
                    case EvType.Takedown:
                        au.PlayAt("stab", pos, 0.8f, 10f);
                        CameraRig.I.Shake(0.12f);
                        break;
                    case EvType.Subdue:
                        au.PlayAt("body_fall", pos, 0.4f, 8f);
                        if (e.Text != null) _hud.Toast(e.Text, new Color(1f, 0.8f, 0.4f));
                        break;
                    case EvType.Throw:
                        au.PlayAt("throw", pos, 0.5f, 8f, 0.15f);
                        break;
                    case EvType.ReloadStart:
                        au.PlayAt(e.Text == "shell" || (e.Sound != null && e.Sound.StartsWith("shotgun")) ? "shell_insert" : "reload_start", pos, 0.6f, 10f);
                        break;
                    case EvType.ReloadDone:
                        au.PlayAt(e.Sound != null && e.Sound.StartsWith("shotgun") ? "pump" : "reload_done", pos, 0.6f, 10f);
                        break;
                    case EvType.DryFire:
                        au.PlayAt("dryfire", pos, 0.6f, 8f);
                        break;
                    case EvType.WeaponSwitch:
                        au.Play2D("switch", 0.5f);
                        break;
                    case EvType.DoorOpen:
                        au.PlayAt("door_open", pos, e.ActorId == 0 ? 0.6f : 0.45f, 14f, 0.1f, 0.1f);
                        break;
                    case EvType.DoorClose:
                        au.PlayAt("door_close", pos, 0.5f, 14f, 0.1f, 0.1f);
                        break;
                    case EvType.DoorLocked:
                        au.PlayAt("door_locked", pos, 0.6f, 10f, 0f, 0.5f);
                        break;
                    case EvType.DoorUnlock:
                        au.PlayAt("door_unlock", pos, 0.7f, 10f);
                        _hud.Toast($"Unlocked with the {e.Text} keycard", new Color(0.6f, 0.8f, 1f));
                        break;
                    case EvType.WindowBreak:
                        fx.GlassBurst(pos);
                        au.PlayAt("glass", pos, 0.8f, 24f);
                        break;
                    case EvType.Vault:
                        au.PlayAt("vault", pos, 0.6f, 10f);
                        break;
                    case EvType.Explosion:
                        fx.Explosion(pos, e.Value);
                        au.PlayAt("explosion", pos, 1f, 50f, 0.05f, 0.05f);
                        float d = Vector2.Distance(pos, V(Session.Player.Pos));
                        CameraRig.I.Shake(Mathf.Clamp01(1.1f - d / 14f));
                        break;
                    case EvType.Pickup:
                    {
                        string snd = e.Sound == "Cash" ? "cash" : e.Sound == "Keycard" ? "keycard" : e.Sound == "Medkit" ? "medkit" : "pickup";
                        au.Play2D(snd, 0.6f);
                        if (e.Text != null) _hud.Toast(e.Text, e.Sound == "Intel" || e.Sound == "Keycard" ? new Color(1f, 0.85f, 0.4f) : Color.white);
                        break;
                    }
                    case EvType.Bark:
                        _hud.Subtitle(e.ActorId, e.Text);
                        PlayBark(e.Sound, pos);
                        break;
                    case EvType.Footstep:
                        au.PlayAt("step_" + e.Text, pos, 0.25f + e.Value * 0.6f, 9f + e.Value * 6f, 0.12f, 0.05f, true);
                        break;
                    case EvType.Noise:
                        if (e.ActorId == 0 && e.Value >= 2.5f)
                        {
                            bool loud = e.Text == "Gunshot" || e.Text == "Explosion" || e.Text == "GlassBreak";
                            fx.NoiseRing(pos, e.Value, loud ? new Color(1f, 0.4f, 0.35f, 0.35f) : new Color(1f, 1f, 1f, 0.22f));
                        }
                        break;
                    case EvType.Objective:
                        au.Play2D("objective", 0.8f);
                        _hud.Banner("OBJECTIVE COMPLETE", e.Text, new Color(0.55f, 1f, 0.65f));
                        break;
                    case EvType.Message:
                        _hud.Toast(e.Text, new Color(0.85f, 0.9f, 1f));
                        break;
                    case EvType.PlayerHurt:
                        au.Play2D("player_hurt", 0.7f, Random.Range(0.9f, 1.1f));
                        _hud.Damage(e.Angle);
                        CameraRig.I.Shake(0.12f);
                        View.PlayerView.Flash();
                        break;
                    case EvType.PlayerDied:
                        au.Play2D("death", 0.9f);
                        MusicDirector.I.Sting("gameover", 0.8f);
                        break;
                    case EvType.MissionComplete:
                        MusicDirector.I.Sting("victory", 0.9f);
                        break;
                    case EvType.MissionFailed:
                        MusicDirector.I.Sting("gameover", 0.8f);
                        break;
                    case EvType.Lights:
                        au.PlayAt(e.Flag ? "power_off" : "power_on", pos, 0.8f, 30f);
                        break;
                    case EvType.Teleport:
                        _hud.Flash(0.25f);
                        CameraRig.I.Snap(V(e.Pos2));
                        au.PlayAt("step_hard", V(e.Pos2), 0.6f, 10f, 0.1f, 0f, true);
                        break;
                    case EvType.Hide:
                    case EvType.Unhide:
                    case EvType.BodyStash:
                        au.PlayAt("hide", pos, 0.6f, 10f);
                        break;
                    case EvType.BodyDrag:
                        au.PlayAt("drag", pos, 0.5f, 8f);
                        break;
                    case EvType.BodyDrop:
                        au.PlayAt("body_fall", pos, 0.35f, 8f);
                        break;
                    case EvType.Medkit:
                        au.Play2D("medkit", 0.7f);
                        break;
                    case EvType.BodyFound:
                        au.Play2D("body_found", 0.8f);
                        _hud.Warning("A BODY HAS BEEN FOUND");
                        break;
                    case EvType.Spotted:
                        au.Play2D("spotted", 0.7f);
                        _hud.Warning("SPOTTED");
                        break;
                    case EvType.CameraDisabled:
                    case EvType.Download:
                        au.PlayAt("terminal_done", pos, 0.8f, 12f);
                        break;
                    case EvType.KnifeStuck:
                        au.PlayAt("knife_hit", pos, 0.5f, 10f);
                        break;
                    case EvType.CoinLand:
                        au.PlayAt("coin", pos, 0.8f, 14f, 0.1f);
                        break;
                    case EvType.Radio:
                        if (e.Flag) { au.PlayAt("radio", pos, 0.8f, 18f); _hud.RadioStarted(e.ActorId, e.Value); }
                        else if (e.Text == "alarm") { au.Play2D("camera_alarm", 0.6f); _hud.Warning("ALARM RAISED"); _hud.RadioEnded(e.ActorId); }
                        else if (e.Text == "camera") { au.PlayAt("camera_alarm", pos, 0.9f, 30f); _hud.Warning("CAMERA ALERT"); }
                        else _hud.RadioEnded(e.ActorId);
                        break;
                    case EvType.SecretFound:
                        au.PlayAt("secret", pos, 0.8f, 12f);
                        break;
                    case EvType.Distraction:
                        if (e.Flag) au.PlayAt("distraction_" + e.Text, pos, 0.8f, 16f, 0f, 0f);
                        break;
                }
            }
        }

        private bool IsCamera(int id)
        {
            foreach (var n in Session.Npcs) if (n.Id == id) return n.IsCamera;
            return false;
        }

        private void PlayVoice(int actorId, string cat, Vector2 pos)
        {
            foreach (var n in Session.Npcs)
                if (n.Id == actorId)
                {
                    string voice = n.IsCivilian || (n.IsTarget && !n.Armed) ? "civ" : n.Kind == NpcKind.Elite ? "elite" : "guard";
                    AudioManager.I.PlayVoice(voice, cat, pos);
                    return;
                }
        }

        /// <summary>Bark keys ("guard_spotted") map onto a handful of synthesised voice moods.</summary>
        private static void PlayBark(string key, Vector2 pos)
        {
            if (string.IsNullOrEmpty(key)) return;
            int us = key.IndexOf('_');
            string voice = key.Substring(0, us), what = key.Substring(us + 1);
            string cat;
            switch (what)
            {
                case "spotted": case "gunshot": case "body": case "found": case "camera": cat = "alert"; break;
                case "suspicious": case "investigate": case "lost": case "body_seen": case "noise": case "lights": cat = "question"; break;
                case "hurt": cat = "pain"; break;
                case "panic": case "flee": cat = "panic"; break;
                case "radio": case "report": case "search": cat = "report"; break;
                default: cat = "calm"; break;
            }
            AudioManager.I.PlayVoice(voice, cat, pos);
        }
    }
}
