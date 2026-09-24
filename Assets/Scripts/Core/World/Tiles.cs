using System;

namespace ShadowContract.Core
{
    public enum TileKind : byte
    {
        Void,       // outside the playable map
        Floor,
        Wall,
        Window,     // solid, see-through, vaultable, breakable by bullets
        Door,       // dynamic: see DoorState
        Furniture,
        Water,      // solid, see-through
        Vent,       // crawlspace: only a crouched player can pass; hides the player
        Stairs,     // floor that links to another stairs marker
    }

    public enum FloorStyle : byte { None, A, B, C, D, Grass, Path }

    public enum FurnitureType : byte
    {
        None, Table, Desk, Bed, Sofa, Bookshelf, Crate, ServerRack, Counter, Toilet, Sink,
        Piano, Machine, Cabinet, Plant, Closet, Tree, Bush, Chair, Barrel, Bathtub, Pillar,
        Car, Vending, Console, Partition,
    }

    public enum DoorType : byte { Normal, LockedBlue, LockedRed, Secret }

    /// <summary>How a tile interacts with movement, sight and bullets.</summary>
    [Flags]
    public enum TileFlags : byte
    {
        None = 0,
        Solid = 1,          // blocks movement
        BlocksSight = 2,    // blocks vision completely
        LowCover = 4,       // blocks vision to crouched characters only
        BlocksBullets = 8,
        Concealment = 16,   // bushes: reduce visibility of anyone standing inside
        HidingSpot = 32,    // closets: player can hide inside
    }

    public struct Tile
    {
        public TileKind Kind;
        public FloorStyle Floor;
        public FurnitureType Furniture;
        public TileFlags Flags;
        public char Symbol;

        public bool Is(TileFlags f) => (Flags & f) != 0;
    }

    /// <summary>The single source of truth for what each map character means. Mirrored in tools/maplib.py.</summary>
    public static class TileLegend
    {
        public static Tile FromChar(char c)
        {
            var t = new Tile { Symbol = c, Floor = FloorStyle.A };
            switch (c)
            {
                case ' ': t.Kind = TileKind.Void; t.Floor = FloorStyle.None; t.Flags = TileFlags.Solid | TileFlags.BlocksSight | TileFlags.BlocksBullets; break;
                case '#': t.Kind = TileKind.Wall; t.Floor = FloorStyle.None; t.Flags = TileFlags.Solid | TileFlags.BlocksSight | TileFlags.BlocksBullets; break;
                case 'W': t.Kind = TileKind.Window; t.Flags = TileFlags.Solid | TileFlags.BlocksBullets; break;
                case 'D': case 'L': case 'M': t.Kind = TileKind.Door; break;
                case 'S': t.Kind = TileKind.Door; t.Floor = FloorStyle.B; break;
                case '.': t.Kind = TileKind.Floor; t.Floor = FloorStyle.A; break;
                case ':': t.Kind = TileKind.Floor; t.Floor = FloorStyle.B; break;
                case ';': t.Kind = TileKind.Floor; t.Floor = FloorStyle.C; break;
                case '_': t.Kind = TileKind.Floor; t.Floor = FloorStyle.D; break;
                case ',': t.Kind = TileKind.Floor; t.Floor = FloorStyle.Grass; break;
                case '"': t.Kind = TileKind.Floor; t.Floor = FloorStyle.Path; break;
                case '~': t.Kind = TileKind.Water; t.Floor = FloorStyle.None; t.Flags = TileFlags.Solid; break;
                case '=': t.Kind = TileKind.Stairs; break;
                case 'v': t.Kind = TileKind.Vent; t.Floor = FloorStyle.None; t.Flags = TileFlags.BlocksSight | TileFlags.BlocksBullets; break;
                default:
                    var f = FurnitureFromChar(c);
                    if (f == FurnitureType.None)
                        throw new FormatException($"Unknown map tile character '{c}'");
                    t.Kind = TileKind.Furniture;
                    t.Furniture = f;
                    t.Flags = FurnitureFlags(f);
                    break;
            }
            return t;
        }

        public static FurnitureType FurnitureFromChar(char c)
        {
            switch (c)
            {
                case 't': return FurnitureType.Table;
                case 'k': return FurnitureType.Desk;
                case 'b': return FurnitureType.Bed;
                case 's': return FurnitureType.Sofa;
                case 'h': return FurnitureType.Bookshelf;
                case 'x': return FurnitureType.Crate;
                case 'r': return FurnitureType.ServerRack;
                case 'u': return FurnitureType.Counter;
                case 'o': return FurnitureType.Toilet;
                case 'n': return FurnitureType.Sink;
                case 'P': return FurnitureType.Piano;
                case 'm': return FurnitureType.Machine;
                case 'f': return FurnitureType.Cabinet;
                case 'p': return FurnitureType.Plant;
                case 'C': return FurnitureType.Closet;
                case 'T': return FurnitureType.Tree;
                case 'B': return FurnitureType.Bush;
                case 'c': return FurnitureType.Chair;
                case 'e': return FurnitureType.Barrel;
                case 'y': return FurnitureType.Bathtub;
                case 'i': return FurnitureType.Pillar;
                case 'g': return FurnitureType.Car;
                case 'j': return FurnitureType.Vending;
                case 'q': return FurnitureType.Console;
                case 'w': return FurnitureType.Partition;
                default: return FurnitureType.None;
            }
        }

        public static TileFlags FurnitureFlags(FurnitureType f)
        {
            const TileFlags tall = TileFlags.Solid | TileFlags.BlocksSight | TileFlags.BlocksBullets;
            const TileFlags low = TileFlags.Solid | TileFlags.LowCover;
            switch (f)
            {
                case FurnitureType.Bookshelf:
                case FurnitureType.Crate:
                case FurnitureType.ServerRack:
                case FurnitureType.Machine:
                case FurnitureType.Cabinet:
                case FurnitureType.Tree:
                case FurnitureType.Pillar:
                case FurnitureType.Vending:
                    return tall;
                case FurnitureType.Closet:
                    return tall | TileFlags.HidingSpot;
                case FurnitureType.Bush:
                    return TileFlags.Concealment | TileFlags.LowCover;
                case FurnitureType.Chair:
                    return TileFlags.None;
                case FurnitureType.Plant:
                    return TileFlags.Solid;
                case FurnitureType.Barrel:
                    return TileFlags.Solid | TileFlags.LowCover | TileFlags.BlocksBullets;
                case FurnitureType.Car:
                    return low | TileFlags.BlocksBullets;
                default:
                    return low;
            }
        }

        public static DoorType DoorTypeFromChar(char c)
        {
            switch (c)
            {
                case 'L': return DoorType.LockedBlue;
                case 'M': return DoorType.LockedRed;
                case 'S': return DoorType.Secret;
                default: return DoorType.Normal;
            }
        }
    }
}
