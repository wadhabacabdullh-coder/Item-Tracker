using System.Collections.Generic;
using ShadowContract.Core;
using UnityEngine;

namespace ShadowContract.Game
{
    /// <summary>
    /// Draws every NPC's field of view as one combined mesh (a single draw call). Rays stop at walls and doors,
    /// and the colour shows the NPC's state: pale = calm, yellow = suspicious, orange = searching, red = combat.
    /// </summary>
    public sealed class VisionConeView : MonoBehaviour
    {
        private const int Rays = 22;
        private GameSession _s;
        private Mesh _mesh;
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color32> _cols = new List<Color32>();
        private readonly List<int> _tris = new List<int>();
        private MeshRenderer _renderer;
        private int _frame;
        public bool Visible = true;

        public void Build(GameSession s, Transform parent)
        {
            _s = s;
            var go = new GameObject("VisionCones");
            go.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "Cones" };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = SpriteLibrary.VertexColor;
            _renderer.sortingOrder = Layers.Cone;
        }

        private static Color32 StateColor(Npc n, AlertLevel alert)
        {
            if (n.IsCamera) return n.State == AIState.Disabled ? new Color32(0, 0, 0, 0) : new Color32(255, 90, 90, 38);
            switch (n.State)
            {
                case AIState.Attack:
                case AIState.Chase:
                    return new Color32(255, 60, 60, 46);
                case AIState.Search:
                case AIState.Investigate:
                    return new Color32(255, 150, 60, 40);
                case AIState.Suspicious:
                    return new Color32(255, 220, 90, 40);
                case AIState.Panic:
                case AIState.Flee:
                case AIState.Cower:
                    return new Color32(0, 0, 0, 0);
            }
            if (n.Suspicion > 0.05f) return Color32.Lerp(new Color32(235, 240, 255, 26), new Color32(255, 220, 90, 40), n.Suspicion * 2f);
            return n.IsCivilian ? new Color32(200, 230, 255, 16) : new Color32(235, 240, 255, 26);
        }

        private void LateUpdate()
        {
            if (_s == null) return;
            _renderer.enabled = Visible;
            if (!Visible) return;
            // Cones are cheap but not free: rebuild every other frame.
            if ((++_frame & 1) == 1) return;
            _verts.Clear();
            _cols.Clear();
            _tris.Clear();
            var world = _s.World;
            foreach (var n in _s.Npcs)
            {
                if (n.Down || n.State == AIState.Disabled) continue;
                var col = StateColor(n, _s.Alert);
                if (col.a == 0) continue;
                // Only draw cones near the camera.
                if (CameraRig.I != null && Vector2.Distance(CameraRig.I.transform.position, new Vector2(n.Pos.x, n.Pos.y)) > 28f) continue;

                bool alerted = n.State == AIState.Attack || n.State == AIState.Chase || n.State == AIState.Search || n.State == AIState.Investigate;
                float fov = n.ViewAngle * (alerted && !n.IsCamera ? 1.45f : 1f);
                float range = n.ViewRange * (alerted ? 1.0f : 0.85f);
                int center = _verts.Count;
                _verts.Add(new Vector3(n.Pos.x, n.Pos.y, 0));
                _cols.Add(new Color32(col.r, col.g, col.b, (byte)Mathf.Min(255, col.a * 2)));
                for (int i = 0; i <= Rays; i++)
                {
                    float a = n.Facing - fov * 0.5f + fov * i / Rays;
                    var dir = Vec2.FromAngle(a);
                    var hit = world.Raycast(n.Pos, dir, range, RayMode.Sight);
                    Vec2 p = hit.Hit ? hit.Point : n.Pos + dir * range;
                    _verts.Add(new Vector3(p.x, p.y, 0));
                    float fall = hit.Hit ? 1f - hit.Distance / range * 0.6f : 0.35f;
                    _cols.Add(new Color32(col.r, col.g, col.b, (byte)(col.a * fall)));
                    if (i > 0)
                    {
                        _tris.Add(center);
                        _tris.Add(center + i);
                        _tris.Add(center + i + 1);
                    }
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_cols);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }
    }
}
