using System.Collections.Generic;

namespace ShadowContract.Core
{
    public enum Difficulty { Easy, Normal, Hard }

    public sealed class DifficultySettings
    {
        public float Detection = 1f;       // suspicion gain multiplier
        public float EnemyDamage = 1f;
        public float EnemySpread = 1f;     // >1 = less accurate
        public float Reaction = 0.45f;     // seconds before a guard opens fire
        public float PlayerHealth = 100f;

        public static DifficultySettings For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return new DifficultySettings { Detection = 0.7f, EnemyDamage = 0.55f, EnemySpread = 1.5f, Reaction = 0.75f, PlayerHealth = 130f };
                case Difficulty.Hard: return new DifficultySettings { Detection = 1.35f, EnemyDamage = 1.35f, EnemySpread = 0.8f, Reaction = 0.3f, PlayerHealth = 100f };
                default: return new DifficultySettings();
            }
        }
    }

    /// <summary>What the player brings into a mission (built from the save file by the game layer).</summary>
    public sealed class LoadoutConfig
    {
        public string[] SlotWeapons = { "knife", "pistol", null, null, "coin" };
        public HashSet<string> Upgrades = new HashSet<string>();
        public Dictionary<AmmoType, int> Ammo = new Dictionary<AmmoType, int>();
        public int Medkits;
        public float Armor;
        public float SpeedMult = 1f;
    }

    public sealed class PlayerInput
    {
        public Vec2 Move;               // -1..1 per axis
        public Vec2 Aim;                // world position of the cursor
        public bool FireHeld, FirePressed;
        public bool AltHeld, AltPressed;
        public bool ReloadPressed;
        public bool InteractPressed, InteractHeld;
        public bool SprintHeld;
        public bool CrouchPressed;
        public bool MedkitPressed;
        public int SelectSlot = -1;     // 0..4
        public int Cycle;               // mouse wheel -1 / +1

        public void ClearEdges()
        {
            FirePressed = AltPressed = ReloadPressed = InteractPressed = CrouchPressed = MedkitPressed = false;
            SelectSlot = -1;
            Cycle = 0;
        }
    }

    public enum AlertLevel { Calm, Suspicious, Alarmed, Combat }
    public enum MissionState { Playing, Complete, Failed, Dead }

    public enum NoiseKind { Footstep, Door, Distraction, Takedown, BodyFall, SuppressedShot, Gunshot, GlassBreak, Explosion, Shout }

    public enum EvType
    {
        Shot, Tracer, Impact, Hit, Death, Melee, Throw, ReloadStart, ReloadDone, DryFire, WeaponSwitch,
        DoorOpen, DoorClose, DoorLocked, DoorUnlock, WindowBreak, Vault, Explosion, Pickup, Bark, Footstep, Noise,
        Objective, Message, PlayerHurt, PlayerDied, MissionComplete, MissionFailed, Lights, Teleport, Casing,
        Hide, Unhide, Medkit, Alert, BodyFound, Takedown, Subdue, Spotted, CameraDisabled, Download, Distraction,
        KnifeStuck, CoinLand, Radio, BodyDrag, BodyDrop, BodyStash, SecretFound,
    }

    /// <summary>Something the presentation layer should show or play. Emitted by the simulation each tick.</summary>
    public struct GameEvent
    {
        public EvType Type;
        public Vec2 Pos;
        public Vec2 Pos2;
        public float Angle;
        public float Value;
        public string Text;
        public string Sound;
        public int ActorId;             // npc id, 0 = player, -1 = none
        public bool Flag;

        public override string ToString() => $"{Type} {Text} {Pos}";
    }

    public sealed class MissionResult
    {
        public string MissionId;
        public bool Success;
        public string FailReason;
        public float Time;
        public int Kills;
        public int TargetsKilled;
        public int CiviliansKilled;
        public int NonTargetKills;
        public int Subdued;
        public int BodiesFound;
        public bool Spotted;
        public int BaseReward;
        public int ObjectiveBonus;
        public int ChallengeBonus;
        public int CashFound;
        public int Penalty;
        public int Total;
        public string Rating;
        public readonly List<ChallengeDef> ChallengesCompleted = new List<ChallengeDef>();
        public readonly List<string> ObjectivesCompleted = new List<string>();
        public readonly Dictionary<AmmoType, int> AmmoLeft = new Dictionary<AmmoType, int>();
        public int MedkitsLeft;
    }
}
