// RocketTrail.cs
// The smoke trail behind a player rocket / missile / thermo shot (RocketGun::setRadar 0x18b2b0 / RocketGun::update
// 0x18b600; ParticleSystemMesh::emitTrail 0x1b6678 / setQuadEdge). The original gives each of the player's bullets its
// own mesh particle system on Level+0x80 (the particles.png manager, like the exhaust): a quad-strip trail whose next
// section starts every 'spacing' units flown (the record's +0x28 with flag 0x10), the sections in a ring of 'pool' (the
// oldest reused, so the trail is at most pool x spacing long), each point fading from its start to its end colour over
// the record's lifetime; two ribbons crossing at 45 deg (flags 0x1000 | 0x2000 | 0x20000), edges at +-size.
// ParticleSettings records (ParticleSettings::ParticleSettings, stride 0x9c):
//   39  rockets (sort 4) and missiles (5): size 100, every 125 units, 29 sections, 3000 ms, white -> transparent,
//       particles.png (0.752, 0.002)-(0.998, 0.498): the white smoke strip
//   25-27  thermo guns 28 / 29 / 30 (25 also the cluster missiles, sort 40): size 50 / 100 / 150, every 50 units, 25
//       sections, 1000 ms, the gold / red / purple strips (0, 0.625)-(0.125, 0.875) + 0.125 per record
//   (28, SunFire o50: another manager, Level+0x98, not built.)
//   12  SET_MISSILE_TRAIL, every other RocketGun sort (EMP bombs built here): not a ribbon but one sprite system on
//       Level+0x84 (sprite_fire, additive) at the bullet, flags 0x2000021 like record 42: 60/s, 1250 ms, size 250..299
//       +250/s, local velocity (0, 0, -6000), 700 behind the bullet, emitting while it lives (MissileTrail).
// A launch resets the system; the bullet's death stops the emission and (sorts 4 / 5 / 40) the trail is drawn 2000 ms more
// (RocketGun+0xd4). NPC guns never get one (setRadar is the player's). Remake: one camera-facing ribbon instead of the
// crossed pair (the same from every side), each section showing the whole strip (mirrored every other section).

using UnityEngine;

namespace GoF2Remake.Flight
{
    public sealed class RocketTrail
    {
        const float M = 0.05f;

        public sealed class Record
        {
            public float size, spacing, lifeMs, afterDeathMs;
            public int pool;
            public Rect uv;   // particles.png, GL (bottom-left) UVs
        }

        static readonly Record Rocket = new Record
            { size = 100f, spacing = 125f, pool = 29, lifeMs = 3000f, afterDeathMs = 2000f, uv = Rect.MinMaxRect(0.752f, 0.002f, 0.998f, 0.498f) };

        static Record Thermo(int k, float size, bool cluster) => new Record
            { size = size, spacing = 50f, pool = 25, lifeMs = 1000f, afterDeathMs = cluster ? 2000f : 1000f,
              uv = Rect.MinMaxRect(0.002f + 0.125f * k, 0.625f, 0.125f + 0.125f * k, 0.875f) };

        /// <summary>The record for a player gun, null = no trail (RocketGun::setRadar's cases).</summary>
        public static Record For(Gun gun)
        {
            if (gun == null) return null;
            if (gun.kind == Gun.Kind.Rocket || gun.kind == Gun.Kind.Missile) return Rocket;
            if (gun.kind == Gun.Kind.ClusterMissile) return Thermo(0, 50f, true);
            if (gun.kind == Gun.Kind.Thermo)
                return gun.itemIndex switch { 28 => Thermo(0, 50f, false), 29 => Thermo(1, 100f, false), 30 => Thermo(2, 150f, false), _ => null };
            return null;
        }

        /// <summary>Record 12 for an EMP bomb (setRadar's last case, addSystem(Level+0x84, bullet, 0xc)); null otherwise.
        /// Emission off: Play / Stop per launch, the pose set each frame.</summary>
        public static ParticleSystem MissileTrail(Gun gun, Transform parent)
        {
            var mat = gun.kind == Gun.Kind.EmpBomb ? CombatAssets.Load()?.fireMaterial : null;
            if (mat == null) return null;
            var ps = ShipSmoke.Create(parent, "Missile trail", mat, 1.25f, 250f, 299f, 76, 0f, Vector3.zero, -700f, 250f, 1);
            var main = ps.main;
            main.startSpeed = -6000f * M;   // +0x6c, along the bullet's -Z (the box shape emits along +Z)
            var em = ps.emission;
            em.rateOverTime = 60f;
            return ps;
        }

        readonly Record rec;
        readonly GameObject go;
        readonly Mesh mesh;
        readonly Vector3[] points;     // ring, oldest at 'first'
        readonly float[] born;         // ms
        int first, count;
        float travelled, clock, deadFor = -1f;
        Vector3 last;
        Vector3[] verts;
        Vector2[] uvs;
        Color32[] cols;
        int[] tris;

        public RocketTrail(Record record, Transform parent, Material material)
        {
            rec = record;
            points = new Vector3[rec.pool];
            born = new float[rec.pool];
            go = new GameObject("Rocket trail");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            mesh = new Mesh { name = "Rocket trail" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            int n = rec.pool + 1;   // the sections plus the rocket itself
            verts = new Vector3[n * 2];
            uvs = new Vector2[n * 2];
            cols = new Color32[n * 2];
            tris = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int v = i * 2, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            go.SetActive(false);
        }

        /// <summary>A launch at 'position' (resetSystem + enableSystemEmit).</summary>
        public void Restart(Vector3 position)
        {
            first = count = 0;
            travelled = 0f;
            deadFor = -1f;
            last = position;
            Push(position);
            go.SetActive(true);
        }

        /// <summary>The bullet gone: no new sections, drawn for the record's after-death time.</summary>
        public void Stop() { if (go.activeSelf && deadFor < 0f) deadFor = 0f; }

        public void Clear() { count = 0; deadFor = -1f; go.SetActive(false); }

        void Push(Vector3 p)
        {
            if (count == rec.pool) { first = (first + 1) % rec.pool; count--; }   // the oldest section reused
            int i = (first + count) % rec.pool;
            points[i] = p;
            born[i] = clock;
            count++;
        }

        /// <summary>Per frame: 'alive' with the bullet at 'position'; the ribbon faces 'cam'.</summary>
        public void Tick(float dtMs, bool alive, Vector3 position, Camera cam)
        {
            if (!go.activeSelf) return;
            clock += dtMs;
            if (deadFor >= 0f)
            {
                deadFor += dtMs;
                if (deadFor >= rec.afterDeathMs) { Clear(); return; }
            }
            else if (alive)
            {
                // flag 0x10: one section per 'spacing' units flown
                float d = Vector3.Distance(position, last) / M;
                if (d > 1e-3f)
                {
                    // the crossings in flight order, 'spacing' apart along this frame's path
                    for (float s = rec.spacing - travelled; s <= d; s += rec.spacing) Push(Vector3.Lerp(last, position, s / d));
                    travelled = Mathf.Repeat(travelled + d, rec.spacing);
                }
                last = position;
            }
            while (count > 0 && clock - born[first] >= rec.lifeMs) { first = (first + 1) % rec.pool; count--; }
            Build(deadFor < 0f && alive ? position : (Vector3?)null, cam);
        }

        void Build(Vector3? head, Camera cam)
        {
            int n = count + (head.HasValue ? 1 : 0);
            if (n < 2 || cam == null) { mesh.Clear(); return; }
            var camPos = cam.transform.position;
            float half = rec.size * M;
            Vector3 P(int k) => k < count ? points[(first + k) % rec.pool] : head.Value;
            float Age(int k) => k < count ? clock - born[(first + k) % rec.pool] : 0f;
            for (int k = 0; k < n; k++)
            {
                var p = P(k);
                var t = P(Mathf.Min(k + 1, n - 1)) - P(Mathf.Max(k - 1, 0));
                var side = Vector3.Cross(t, camPos - p);
                side = side.sqrMagnitude > 1e-8f ? side.normalized * half : Vector3.zero;
                verts[k * 2] = p - side;
                verts[k * 2 + 1] = p + side;
                // the whole strip per section, mirrored every other one; u across, v along
                float v = (k & 1) == 0 ? rec.uv.yMin : rec.uv.yMax;
                uvs[k * 2] = new Vector2(rec.uv.xMin, v);
                uvs[k * 2 + 1] = new Vector2(rec.uv.xMax, v);
                // FFFFFFFF -> 00000000 over the life (additive: the RGB fades too)
                byte c = (byte)(255f * Mathf.Clamp01(1f - Age(k) / rec.lifeMs));
                cols[k * 2] = cols[k * 2 + 1] = new Color32(c, c, c, c);
            }
            for (int k = n; k < verts.Length / 2; k++) verts[k * 2] = verts[k * 2 + 1] = verts[(n - 1) * 2];
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.SetTriangles(tris, 0, (n - 1) * 6, 0);
            mesh.RecalculateBounds();
        }

        public void Destroy()
        {
            if (go != null) Object.Destroy(go);
            if (mesh != null) Object.Destroy(mesh);
        }
    }
}
