using ShadowContract.Core;
using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Visual for a character (player or NPC): walking legs, body pose, held weapon, melee swing, recoil,
    /// hit flash, crouch, state icon and (for targets) a marker. Pure presentation; reads the simulation each frame.
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        public Actor Actor;
        public Npc Npc;
        public bool IsPlayer;

        private SpriteRenderer _legs, _body, _weapon, _icon, _rim, _marker;
        private Sprite[] _sheet;
        private string _weaponId;
        private float _swing;       // melee swing timer
        private float _recoil;
        private float _flash;
        private Transform _pivot;
        private Sprite _alert, _question, _targetMarker;
        private float _iconPulse;

        public static CharacterView Create(Transform parent, Actor actor, string type, bool isPlayer)
        {
            var go = new GameObject(isPlayer ? "Player" : "NPC_" + ((actor as Npc)?.Key ?? "?"));
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<CharacterView>();
            v.Actor = actor;
            v.Npc = actor as Npc;
            v.IsPlayer = isPlayer;
            v._sheet = SpriteLibrary.Character(type);
            v._pivot = new GameObject("pivot").transform;
            v._pivot.SetParent(go.transform, false);
            v._legs = v.MakeRenderer("legs", Layers.Legs);
            v._weapon = v.MakeRenderer("weapon", Layers.Weapon);
            v._body = v.MakeRenderer("body", Layers.Char);
            v._icon = new GameObject("icon").AddComponent<SpriteRenderer>();
            v._icon.transform.SetParent(go.transform, false);
            v._icon.sortingOrder = Layers.WorldUi;
            v._alert = SpriteLibrary.Get("UI/alert");
            v._question = SpriteLibrary.Get("UI/question");
            v._targetMarker = SpriteLibrary.Get("UI/target_marker");
            if (isPlayer)
            {
                // A faint copy of the player drawn above the darkness so you never lose yourself in shadows.
                v._rim = v.MakeRenderer("rim", Layers.PlayerRim);
                v._rim.color = new Color(0.55f, 0.75f, 1f, 0.22f);
            }
            if (v.Npc != null && v.Npc.IsTarget)
            {
                v._marker = new GameObject("marker").AddComponent<SpriteRenderer>();
                v._marker.transform.SetParent(go.transform, false);
                v._marker.sprite = v._targetMarker;
                v._marker.sortingOrder = Layers.WorldUi;
            }
            return v;
        }

        private SpriteRenderer MakeRenderer(string name, int order)
        {
            var r = new GameObject(name).AddComponent<SpriteRenderer>();
            r.transform.SetParent(_pivot, false);
            r.sortingOrder = order;
            return r;
        }

        public void Swing() => _swing = 0.18f;
        public void Recoil(float amount) => _recoil = Mathf.Max(_recoil, amount);
        public void Flash() => _flash = 0.12f;

        private SpriteLibrary.Pose PoseFor(WeaponDef w)
        {
            if (w == null) return SpriteLibrary.Pose.Unarmed;
            if (w.IsMelee || w.IsThrown) return SpriteLibrary.Pose.Knife;
            if (w.Class == WeaponClass.Pistol) return SpriteLibrary.Pose.OneHand;
            return SpriteLibrary.Pose.TwoHand;
        }

        public void Sync(float dt, GameSession s)
        {
            var a = Actor;
            transform.position = new Vector3(a.Pos.x, a.Pos.y, 0f);
            bool down = Npc != null ? Npc.Down : !a.Alive;

            if (down)
            {
                // Bodies are drawn by BodyView.
                _legs.enabled = _body.enabled = _weapon.enabled = _icon.enabled = false;
                if (_marker != null) _marker.enabled = false;
                if (_rim != null) _rim.enabled = false;
                return;
            }

            bool hidden = IsPlayer && (s.Player.Hidden);
            if (Npc != null && Npc.IsCamera) return;

            // ---- weapon & pose
            WeaponDef w = IsPlayer ? s.Player.Inventory.CurrentWeapon?.Def : Npc.Weapon?.Def;
            var pose = PoseFor(w);
            string wid = w?.Id;
            if (Npc != null && !Npc.IsHostile && Npc.State != AIState.Attack) { pose = SpriteLibrary.Pose.Unarmed; wid = null; }
            // Calm guards carry their weapon lowered (unarmed pose, weapon hidden) until something is up.
            if (Npc != null && Npc.IsHostile && (Npc.State == AIState.Idle || Npc.State == AIState.Patrol || Npc.State == AIState.Follow || Npc.State == AIState.ReturnToPatrol) && s.Alert < AlertLevel.Alarmed)
            {
                pose = SpriteLibrary.Pose.Unarmed;
                wid = null;
            }
            _body.sprite = _sheet[(int)pose];
            if (wid != _weaponId)
            {
                _weaponId = wid;
                _weapon.sprite = wid != null ? SpriteLibrary.HeldWeapon(wid) : null;
            }
            _weapon.enabled = wid != null && !hidden;

            // ---- legs: walk cycle from distance travelled
            bool moving = IsPlayer ? s.Player.Moving : Npc.Speed > 0.15f;
            int frame = moving ? (int)(a.MoveAnim * 2.6f) % 4 : 0;
            _legs.sprite = _sheet[6 + frame];
            _legs.enabled = !hidden;
            _body.enabled = !hidden;

            // ---- rotation, crouch, swing & recoil
            float facingDeg = a.Facing * Mathf.Rad2Deg;
            float swingOffset = 0f;
            if (_swing > 0f)
            {
                _swing -= dt;
                float t = 1f - _swing / 0.18f;
                swingOffset = Mathf.Lerp(55f, -65f, t);
            }
            _pivot.localRotation = Quaternion.Euler(0, 0, facingDeg + swingOffset * 0.35f);
            _legs.transform.localRotation = Quaternion.Euler(0, 0, 0);
            bool crouched = IsPlayer && s.Player.Crouched;
            float scale = crouched ? 0.86f : 1f;
            float bob = moving && !crouched ? Mathf.Sin(a.MoveAnim * 5.2f) * 0.02f : 0f;
            _pivot.localScale = new Vector3(scale + bob, scale - bob, 1f);

            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 1.2f);
            _body.transform.localPosition = new Vector3(-_recoil, 0f, 0f);
            // weapon sits in the hands; swings with melee
            Vector3 hand = pose == SpriteLibrary.Pose.OneHand ? new Vector3(0.38f, 0f, 0f)
                         : pose == SpriteLibrary.Pose.TwoHand ? new Vector3(0.28f, -0.02f, 0f)
                         : new Vector3(0.36f, -0.26f, 0f);
            _weapon.transform.localPosition = hand + new Vector3(-_recoil * 1.5f, 0f, 0f);
            _weapon.transform.localRotation = Quaternion.Euler(0, 0, swingOffset);

            // ---- hit flash / damage
            _flash -= dt;
            Color tint = _flash > 0f ? new Color(1f, 0.55f, 0.55f) : Color.white;
            if (IsPlayer && s.Player.InVent) tint *= 0.6f;
            _body.color = tint;
            _legs.color = tint;
            _weapon.color = tint;

            if (_rim != null)
            {
                _rim.enabled = !hidden;
                _rim.sprite = _body.sprite;
                _rim.transform.localPosition = _body.transform.localPosition;
            }

            // ---- state icon above NPCs
            if (Npc != null)
            {
                _icon.transform.position = new Vector3(a.Pos.x, a.Pos.y + 0.95f, 0f);
                _icon.transform.rotation = Quaternion.identity;
                Sprite icon = null;
                Color ic = Color.white;
                switch (Npc.State)
                {
                    case AIState.Attack:
                    case AIState.Chase:
                    case AIState.Panic:
                    case AIState.Flee:
                        icon = _alert;
                        break;
                    case AIState.Suspicious:
                    case AIState.Investigate:
                    case AIState.Search:
                        icon = _question;
                        ic = Npc.State == AIState.Search ? new Color(1f, 0.6f, 0.3f) : Color.white;
                        break;
                    default:
                        if (Npc.Suspicion > 0.15f) { icon = _question; ic = new Color(1, 1, 1, Mathf.Clamp01(Npc.Suspicion * 2f)); }
                        break;
                }
                _icon.sprite = icon;
                _icon.enabled = icon != null;
                _iconPulse += dt * 8f;
                float sc = icon == _alert ? 1f + Mathf.Sin(_iconPulse) * 0.1f : 1f;
                _icon.transform.localScale = new Vector3(sc, sc, 1);
                _icon.color = ic;

                if (_marker != null)
                {
                    _marker.enabled = true;
                    float bobM = Mathf.Sin(Time.time * 3f) * 0.08f;
                    _marker.transform.position = new Vector3(a.Pos.x, a.Pos.y + (icon != null ? 1.55f : 1.0f) + bobM, 0f);
                    _marker.transform.rotation = Quaternion.identity;
                }
            }
            else _icon.enabled = false;
        }
    }
}
