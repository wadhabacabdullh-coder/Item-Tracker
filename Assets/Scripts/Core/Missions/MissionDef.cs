using System.Collections.Generic;

namespace ShadowContract.Core
{
    public enum ObjectiveType { Eliminate, Retrieve, Download, Reach, Extract }

    public sealed class ObjectiveDef
    {
        public string Id;
        public ObjectiveType Type;
        public string Text;
        public List<string> TargetIds = new List<string>();  // Eliminate
        public string ItemId;                                // Retrieve / Download terminal id
        public string ZoneName;                              // Reach
        public bool Optional;
        public int Bonus;                                    // optional objective payout
    }

    public enum ChallengeType { SilentAssassin, NoCivilianCasualties, Professional, NoBodiesFound, Speed }

    public sealed class ChallengeDef
    {
        public ChallengeType Type;
        public string Name;
        public string Description;
        public int Bonus;
        public float ParTime;           // Speed only
    }

    public sealed class MissionDef
    {
        public string Id;
        public string MapId;
        public string Name;
        public string Location;
        public string Briefing;
        public int Reward;
        public int Order;
        public int Difficulty;          // 1..3 stars shown in the menu
        public float ParTime = 480f;
        public List<ObjectiveDef> Objectives = new List<ObjectiveDef>();
        public List<ChallengeDef> Challenges = new List<ChallengeDef>();
        public string UnlockedBy;       // previous mission
    }

    public static class MissionCatalog
    {
        public static readonly List<MissionDef> All = new List<MissionDef>();

        public static MissionDef Get(string id) => All.Find(m => m.Id == id);

        static MissionCatalog()
        {
            All.Add(new MissionDef
            {
                Id = "mansion_host", MapId = "mansion", Order = 1, Difficulty = 1, Name = "The Host", Location = "Villa Moretti, Lake Como",
                Reward = 3500, ParTime = 420f,
                Briefing = "Victor Moretti launders money for half the cartels in Europe and tonight he is throwing a party. " +
                           "He moves between his guests, his study and his bedroom, always shadowed by a bodyguard. " +
                           "Get in through the gardens, eliminate Moretti and get back to the car. Guests and staff are not the contract.",
                Objectives =
                {
                    new ObjectiveDef { Id = "kill", Type = ObjectiveType.Eliminate, Text = "Eliminate Victor Moretti", TargetIds = { "moretti" } },
                    new ObjectiveDef { Id = "extract", Type = ObjectiveType.Extract, Text = "Escape to the getaway car" },
                },
            });
            All.Add(new MissionDef
            {
                Id = "facility_voss", MapId = "facility", Order = 2, Difficulty = 2, Name = "Patient Zero", Location = "Helix Labs, Rotterdam",
                Reward = 5000, ParTime = 540f, UnlockedBy = "mansion_host",
                Briefing = "Dr. Helena Voss engineered a pathogen and is about to sell it. She works the biolab, the servers and visits the director. " +
                           "The checkpoint needs a blue keycard - or crawl the vents from the dock storage. Cameras can be shut down from security control. " +
                           "Eliminate Voss. Her samples would be a welcome bonus.",
                Objectives =
                {
                    new ObjectiveDef { Id = "kill", Type = ObjectiveType.Eliminate, Text = "Eliminate Dr. Helena Voss", TargetIds = { "voss" } },
                    new ObjectiveDef { Id = "samples", Type = ObjectiveType.Retrieve, Text = "Optional: Steal the pathogen samples", ItemId = "samples", Optional = true, Bonus = 1500 },
                    new ObjectiveDef { Id = "extract", Type = ObjectiveType.Extract, Text = "Extract via the loading dock or freight elevator" },
                },
            });
            All.Add(new MissionDef
            {
                Id = "office_cfo", MapId = "office", Order = 3, Difficulty = 2, Name = "Hostile Takeover", Location = "Kessler Tower, Frankfurt",
                Reward = 6500, ParTime = 540f, UnlockedBy = "facility_voss",
                Briefing = "Marcus Kessler cooks the books for a private army. He works from the executive floor. " +
                           "The elevator needs a blue access card; the security desk guard carries one and another was left in the cafe. " +
                           "Find a way up, eliminate Kessler, and leave by the street or the rooftop helipad.",
                Objectives =
                {
                    new ObjectiveDef { Id = "card", Type = ObjectiveType.Retrieve, Text = "Find a blue access card", ItemId = "keycard_blue", Optional = true, Bonus = 500 },
                    new ObjectiveDef { Id = "kill", Type = ObjectiveType.Eliminate, Text = "Eliminate Marcus Kessler", TargetIds = { "kessler" } },
                    new ObjectiveDef { Id = "extract", Type = ObjectiveType.Extract, Text = "Escape via the street or the helipad" },
                },
            });
            All.Add(new MissionDef
            {
                Id = "mansion_ledger", MapId = "mansion", Order = 4, Difficulty = 3, Name = "Family Secrets", Location = "Villa Moretti, Lake Como",
                Reward = 7500, ParTime = 600f, UnlockedBy = "office_cfo",
                Briefing = "Moretti's accountant Paolo Greco keeps the family ledger in the cellar vault. The vault door needs the security chief's red keycard... " +
                           "or you could look for another way in. Rumour has it the old study hides more than books. Retrieve the ledger and silence Greco.",
                Objectives =
                {
                    new ObjectiveDef { Id = "ledger", Type = ObjectiveType.Retrieve, Text = "Retrieve the Moretti ledger from the vault", ItemId = "ledger" },
                    new ObjectiveDef { Id = "kill", Type = ObjectiveType.Eliminate, Text = "Eliminate Paolo Greco", TargetIds = { "accountant" } },
                    new ObjectiveDef { Id = "extract", Type = ObjectiveType.Extract, Text = "Escape to the getaway car" },
                },
            });
            All.Add(new MissionDef
            {
                Id = "facility_prototype", MapId = "facility", Order = 5, Difficulty = 3, Name = "Blackout", Location = "Helix Labs, Rotterdam",
                Reward = 9000, ParTime = 660f, UnlockedBy = "mansion_ledger",
                Briefing = "Helix rebuilt. Security chief Kovac and Director Hale now guard the finished prototype in the containment lab behind a red door. " +
                           "Kovac carries a red keycard; the director keeps a spare in her office. Eliminate both and steal the prototype.",
                Objectives =
                {
                    new ObjectiveDef { Id = "kill", Type = ObjectiveType.Eliminate, Text = "Eliminate Kovac and Director Hale", TargetIds = { "kovac", "director" } },
                    new ObjectiveDef { Id = "proto", Type = ObjectiveType.Retrieve, Text = "Steal the Helix prototype", ItemId = "prototype" },
                    new ObjectiveDef { Id = "extract", Type = ObjectiveType.Extract, Text = "Extract via the loading dock or freight elevator" },
                },
            });
            All.Add(new MissionDef
            {
                Id = "office_breach", MapId = "office", Order = 6, Difficulty = 3, Name = "Data Breach", Location = "Kessler Tower, Frankfurt",
                Reward = 12000, ParTime = 720f, UnlockedBy = "office_cfo",
                Briefing = "Kessler's successors are holding a board meeting. Download the mainframe's contents from the server room, " +
                           "then eliminate both executives - Dana Whitmore and Victor Hale. They will run for the exits the moment an alarm goes off.",
                Objectives =
                {
                    new ObjectiveDef { Id = "data", Type = ObjectiveType.Download, Text = "Download the mainframe data", ItemId = "mainframe" },
                    new ObjectiveDef { Id = "kill", Type = ObjectiveType.Eliminate, Text = "Eliminate Whitmore and Hale", TargetIds = { "exec1", "exec2" } },
                    new ObjectiveDef { Id = "extract", Type = ObjectiveType.Extract, Text = "Escape via the street or the helipad" },
                },
            });

            foreach (var m in All)
            {
                m.Challenges.Add(new ChallengeDef { Type = ChallengeType.SilentAssassin, Name = "Silent Assassin", Description = "Never be spotted.", Bonus = m.Reward / 2 });
                m.Challenges.Add(new ChallengeDef { Type = ChallengeType.NoCivilianCasualties, Name = "Clean Hands", Description = "No civilian casualties.", Bonus = 800 });
                m.Challenges.Add(new ChallengeDef { Type = ChallengeType.Professional, Name = "Professional", Description = "Kill no one but the targets.", Bonus = 1500 });
                m.Challenges.Add(new ChallengeDef { Type = ChallengeType.NoBodiesFound, Name = "Ghost", Description = "No bodies found.", Bonus = 700 });
                m.Challenges.Add(new ChallengeDef { Type = ChallengeType.Speed, Name = "Swift", Description = $"Finish in under {(int)(m.ParTime / 60)} minutes.", Bonus = 600, ParTime = m.ParTime });
            }
        }
    }
}
