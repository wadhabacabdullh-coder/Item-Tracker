using System.Collections.Generic;

namespace ShadowContract.Core
{
    public abstract class Actor
    {
        public int Id;
        public Vec2 Pos;
        public Vec2 Velocity;
        public float Facing;            // radians
        public float Radius = 0.32f;
        public float Health = 100f;
        public float MaxHealth = 100f;
        public bool Alive = true;
        public float MoveAnim;          // accumulates distance walked (for walk cycles / footsteps)

        public Vec2 Forward => Vec2.FromAngle(Facing);
    }

    public sealed class Player : Actor
    {
        public float Armor;
        public float MaxArmor;
        public bool Crouched;
        public bool Sprinting;
        public bool Aiming;
        public bool Moving;
        public bool Hidden;             // inside a closet
        public Int2 HiddenIn;           // closet cell
        public Vec2 HideExitPos;
        public bool InVent;
        public int DraggingBody = -1;   // body id
        public readonly Inventory Inventory = new Inventory();
        public float ActionLock;        // seconds the player can't act (takedown, vault)
        public string ActionLabel;
        public float InteractProgress;  // hold-to-use progress 0..1
        public object InteractTarget;
        public float FootstepDist;
        public float LastShotNoise;     // time since the player last made a loud noise
        public float DamageFlash;
        public float SpeedMult = 1f;    // heavy armour etc.
        public bool GodMode;            // debug cheat: no damage
        public bool Ghost;              // debug cheat: NPCs cannot perceive the player
    }

    public enum NpcKind { Guard, Elite, Civilian, Target, Camera }

    public enum AIState
    {
        Idle, Patrol, Suspicious, Investigate, Search, Chase, Attack, ReturnToPatrol,
        Follow, Panic, Flee, Cower, Fixing, Dead, Unconscious, Disabled,
    }

    public sealed class Npc : Actor
    {
        public string Key;                  // id from the map file
        public string DisplayName;
        public NpcKind Kind;
        public string Type;                 // skin: guard/elite/staff/scientist/worker/guest/target
        public AIState State = AIState.Idle;
        public float StateTime;
        public float Suspicion;             // 0..1 towards the player
        public float SuspicionDecayDelay;
        public bool SeesPlayer;
        public float SeeTime;               // continuous seconds seeing the player
        public Vec2 LastKnownPlayer;
        public float LastSeenAgo = 999f;
        public Vec2 InvestigatePos;
        public bool InvestigateIsLoud;
        public int InvestigateBody = -1;
        public object InvestigateObject;     // distraction / power box to switch off
        public WeaponState Weapon;
        public string CarriedKey;
        public List<Waypoint> Route = new List<Waypoint>();
        public int RouteIndex;
        public float WaitTimer;
        public float[] LookAngles;
        public int LookIndex;
        public float LookTimer;
        public Vec2 HomePos;
        public float HomeFacing;
        public List<PathNode> Path;
        public int PathIndex;
        public Vec2 PathGoal;
        public float RepathTimer;
        public float PerceptionTimer;       // staggered perception ticks
        public float ReactionTimer;         // delay before first shot
        public float BurstTimer;
        public float RadioTimer = -1f;      // > 0 while radioing an alarm
        public bool HasRadioed;
        public float BarkCooldown;
        public string FollowKey;            // bodyguard: key of the target to follow
        public Npc FollowTarget;
        public Vec2 EscapePos;
        public bool Escaped;
        public string Activity;             // shown next to targets ("Phone call")
        public float SearchTimer;
        public int SearchPointsLeft;
        public float Awareness = 1f;        // grows after incidents: faster detection
        public float Speed;                 // current speed, for animation
        public float TurnSpeed = 7f;
        public float ViewRange = 9f;
        public float ViewAngle = 100f * MathUtil.Deg2Rad;
        // cameras
        public float SweepCenter, SweepHalf, SweepPhase;
        public string Group;
        public bool Unconscious;
        public Npc ReportTo;                // civilians running to a guard
        public float DesiredFacing = float.NaN;
        public float StuckTimer;
        public Vec2 StuckPos;
        public Vec2 SearchPoint;
        public bool SearchMoving;
        public float CameraCooldown;
        public bool Armed => Weapon != null;
        public bool IsTarget => Kind == NpcKind.Target;
        public bool IsCivilian => Kind == NpcKind.Civilian;
        public bool IsCamera => Kind == NpcKind.Camera;
        public bool IsHostile => Kind == NpcKind.Guard || Kind == NpcKind.Elite || (Kind == NpcKind.Target && Armed);
        public bool Down => !Alive || Unconscious;
    }

    public sealed class Body
    {
        public int Id;
        public Npc Npc;
        public Vec2 Pos;
        public float Facing;
        public bool Discovered;
        public bool Hidden;                 // stashed in a closet
        public bool Looted;
        public bool Dragged;
        public int KnivesInside;            // thrown knives that can be recovered
    }

    public enum PickupKind { Ammo, Medkit, Armor, Cash, Keycard, Intel, Weapon, Knives, Coins }

    public sealed class Pickup
    {
        public int Id;
        public PickupKind Kind;
        public string ItemId;               // keycard colour / intel id / weapon id
        public string Name;
        public int Amount;
        public AmmoType AmmoType;
        public Vec2 Pos;
        public bool Taken;
        public bool RequiresInteract => Kind == PickupKind.Weapon;
    }

    public enum ProjectileKind { Knife, Coin }

    public sealed class Projectile
    {
        public int Id;
        public ProjectileKind Kind;
        public Vec2 Pos;
        public Vec2 Vel;
        public float Angle;
        public float Life;
        public float Damage;
        public bool FromPlayer;
        public bool Done;
    }

    public enum InteractKind { PowerBox, CameraTerminal, DownloadTerminal, Distraction, Stairs }

    public sealed class Interactable
    {
        public int Id;
        public InteractKind Kind;
        public string Key;                  // terminal id
        public string Label;
        public string Group;                // light / camera group
        public Vec2 Pos;
        public Vec2 Pos2;                   // stairs destination
        public string Lock;                 // stairs that need a keycard
        public float Duration;              // hold-to-use seconds
        public bool Used;                   // terminals: done
        public bool Active;                 // distraction running / lights off
        public float Timer;
        public string Sound;                // distraction kind
        public bool Secret;
    }

    public sealed class Zone
    {
        public string Name;
        public Rect Rect;
    }

    public sealed class ExtractZone
    {
        public string Label;
        public Rect Rect;
    }
}
