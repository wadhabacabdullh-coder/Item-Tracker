using NUnit.Framework;
using ShadowContract.Core;
using UnityEngine;

namespace ShadowContract.Tests
{
    /// <summary>
    /// Smoke tests runnable inside Unity (Window > General > Test Runner > EditMode).
    /// The full suite (55+ tests incl. scripted playthroughs) lives in tests/Core.Tests and runs with `dotnet test`.
    /// </summary>
    public class CoreEditModeTests
    {
        [TestCase("mansion")]
        [TestCase("facility")]
        [TestCase("office")]
        public void MapLoadsFromResources(string id)
        {
            var text = Resources.Load<TextAsset>("Maps/" + id);
            Assert.IsNotNull(text, "map text asset");
            var map = MapData.Parse(text.text);
            Assert.AreEqual(id, map.Id);
            Assert.IsNotNull(Resources.Load<Texture2D>("Sprites/Maps/" + id + "_base"), "pre-rendered map art");
        }

        [Test]
        public void EveryMissionSimulatesWithoutErrors()
        {
            foreach (var mission in MissionCatalog.All)
            {
                var map = MapData.Parse(Resources.Load<TextAsset>("Maps/" + mission.MapId).text);
                var progress = new ProgressData();
                var s = new GameSession(map, mission, Difficulty.Normal, progress.BuildLoadout(), 42);
                s.Player.GodMode = true;
                var input = new PlayerInput();
                for (int i = 0; i < 60 * 20; i++)
                {
                    input.ClearEdges();
                    input.Move = new Vec2(Mathf.Sin(i * 0.01f), Mathf.Cos(i * 0.013f));
                    input.Aim = s.Player.Pos + new Vec2(1, 0);
                    s.Update(GameSession.Tick, input);
                }
                Assert.AreNotEqual(MissionState.Dead, s.State, mission.Id);
            }
        }

        [Test]
        public void AllReferencedSpritesAndSoundsExist()
        {
            foreach (var w in WeaponCatalog.All.Values)
            {
                Assert.IsNotNull(Resources.Load<Texture2D>("Sprites/Icons/w_" + w.Id), "icon " + w.Id);
                Assert.IsNotNull(Resources.Load<Texture2D>("Sprites/Weapons/" + w.Id), "held " + w.Id);
                if (w.IsGun) Assert.IsNotNull(Resources.Load<AudioClip>("Audio/SFX/" + w.Sound), "sound " + w.Sound);
            }
            foreach (var type in new[] { "player", "guard", "elite", "staff", "guest", "guest2", "scientist", "worker", "target" })
                Assert.IsNotNull(Resources.Load<Texture2D>("Sprites/Characters/" + type), type);
            foreach (var clip in new[] { "Music/calm", "Music/tension", "Music/combat", "Music/menu", "Music/victory", "Music/gameover" })
                Assert.IsNotNull(Resources.Load<AudioClip>("Audio/" + clip), clip);
        }
    }
}
