using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Features.WaterEffects
{
    /// <summary>
    /// Simplifies a boat made of many mesh objects into a few numbers and two 2D outlines
    /// (boat-local XZ, counter-clockwise, convex): the waterline cross-section and the rim.
    /// Used by BoatFoamEmitter (foam / ripple size) and HullWaterMask (water-free hull interior).
    ///
    /// Only the hull meshes are measured: renderers named in <c>hullNames</c> (e.g. "Boat") if any
    /// exist, otherwise every renderer except those whose name contains an ignore word
    /// (sail, mast, rope...). The top-level child of the boat (e.g. "SailBoat" prefab root) is
    /// never tested against the ignore words.
    ///
    /// Vertex-accurate outlines need the model's Read/Write option enabled in its import settings;
    /// otherwise a tapered outline is built from the mesh bounds.
    /// </summary>
    public sealed class HullOutline
    {
        public Vector2[] Waterline;   // convex, CCW, boat-local XZ
        public Vector2[] Rim;         // convex, CCW, boat-local XZ
        public float MinY, MaxY, WaterlineY, RimY;
        public Vector3 Center;        // boat-local bounds centre
        public Vector3 HalfSize;      // boat-local half extents
        public bool FromVertices;
        public int MeshCount;

        public static HullOutline Measure(
            Transform boat, string[] hullNames, string[] ignoreWords,
            Vector3 sizeOverride, float waterlineFraction)
        {
            HullOutline result = new HullOutline();
            List<MeshFilter> filters = SelectMeshes(boat, hullNames, ignoreWords);
            result.MeshCount = filters.Count;

            Matrix4x4 worldToBoat = boat.worldToLocalMatrix;
            bool any = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;

            if (sizeOverride.x > 0.01f && sizeOverride.z > 0.01f)
            {
                min = -sizeOverride * 0.5f;
                max = sizeOverride * 0.5f;
                any = true;
                filters.Clear();
            }

            // Bounds of every selected mesh in boat space (always available).
            for (int i = 0; i < filters.Count; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                Matrix4x4 m = worldToBoat * filters[i].transform.localToWorldMatrix;
                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                    Encapsulate(ref any, ref min, ref max, m.MultiplyPoint3x4(corner));
                }
            }

            // Fallback: solid colliders.
            if (!any)
            {
                Collider[] colliders = boat.GetComponentsInChildren<Collider>(false);
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i].isTrigger || colliders[i].GetComponentInParent<PlayerController>() != null)
                        continue;
                    Bounds wb = colliders[i].bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = wb.center + Vector3.Scale(wb.extents,
                            new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                        Encapsulate(ref any, ref min, ref max, boat.InverseTransformPoint(corner));
                    }
                }
            }

            if (!any)
            {
                min = new Vector3(-0.7f, -0.4f, -1.6f);
                max = new Vector3(0.7f, 0.5f, 1.6f);
            }

            result.MinY = min.y;
            result.MaxY = max.y;
            float height = Mathf.Max(0.1f, max.y - min.y);
            result.WaterlineY = min.y + Mathf.Clamp01(waterlineFraction) * height;
            result.RimY = max.y;
            result.Center = (min + max) * 0.5f;
            result.HalfSize = Vector3.Max((max - min) * 0.5f, new Vector3(0.2f, 0.05f, 0.3f));

            // Vertex pass: collect points near the waterline and near the rim.
            List<Vector2> waterPoints = new List<Vector2>(2048);
            List<Vector2> rimPoints = new List<Vector2>(2048);
            float band = 0.12f * height;
            List<Vector3> verts = new List<Vector3>(4096);
            for (int i = 0; i < filters.Count; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (!mesh.isReadable)
                    continue;
                verts.Clear();
                mesh.GetVertices(verts);
                Matrix4x4 m = worldToBoat * filters[i].transform.localToWorldMatrix;
                for (int v = 0; v < verts.Count; v++)
                {
                    Vector3 p = m.MultiplyPoint3x4(verts[v]);
                    if (Mathf.Abs(p.y - result.WaterlineY) <= band)
                        waterPoints.Add(new Vector2(p.x, p.z));
                    if (p.y >= result.RimY - 0.2f * height)
                        rimPoints.Add(new Vector2(p.x, p.z));
                }
            }

            Vector2[] waterHull = waterPoints.Count >= 3 ? ConvexHull(waterPoints) : null;
            Vector2[] rimHull = rimPoints.Count >= 3 ? ConvexHull(rimPoints) : null;
            result.FromVertices = waterHull != null && waterHull.Length >= 3 && rimHull != null && rimHull.Length >= 3;

            result.Waterline = (waterHull != null && waterHull.Length >= 3)
                ? waterHull : TaperedOutline(result.Center, result.HalfSize, 0.85f, 20);
            result.Rim = (rimHull != null && rimHull.Length >= 3)
                ? rimHull : TaperedOutline(result.Center, result.HalfSize, 1f, 20);
            return result;
        }

        // ------------------------------------------------------------------ selection

        private static List<MeshFilter> SelectMeshes(Transform boat, string[] hullNames, string[] ignoreWords)
        {
            MeshFilter[] all = boat.GetComponentsInChildren<MeshFilter>(false);
            List<MeshFilter> named = new List<MeshFilter>();
            List<MeshFilter> general = new List<MeshFilter>();

            for (int i = 0; i < all.Length; i++)
            {
                MeshFilter f = all[i];
                if (f.sharedMesh == null || f.GetComponent<MeshRenderer>() == null ||
                    f.GetComponentInParent<PlayerController>() != null)
                    continue;

                if (hullNames != null)
                    for (int n = 0; n < hullNames.Length; n++)
                        if (!string.IsNullOrEmpty(hullNames[n]) &&
                            string.Equals(f.name.Trim(), hullNames[n].Trim(), System.StringComparison.OrdinalIgnoreCase))
                        {
                            named.Add(f);
                            break;
                        }

                if (!IsIgnored(f.transform, boat, ignoreWords))
                    general.Add(f);
            }

            return named.Count > 0 ? named : general;
        }

        private static bool IsIgnored(Transform t, Transform boat, string[] ignoreWords)
        {
            if (ignoreWords == null)
                return false;
            for (Transform node = t; node != null && node != boat; node = node.parent)
            {
                if (node.parent == boat)
                    break; // top-level prefab root such as "SailBoat" is never tested
                string lower = node.name.ToLowerInvariant();
                for (int i = 0; i < ignoreWords.Length; i++)
                    if (!string.IsNullOrEmpty(ignoreWords[i]) && lower.Contains(ignoreWords[i].ToLowerInvariant()))
                        return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ geometry

        private static void Encapsulate(ref bool any, ref Vector3 min, ref Vector3 max, Vector3 p)
        {
            if (!any) { min = max = p; any = true; return; }
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        /// <summary>Pointed-bow / pointed-stern outline used when mesh vertices are not readable.</summary>
        public static Vector2[] TaperedOutline(Vector3 center, Vector3 half, float scale, int count)
        {
            Vector2[] pts = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                float s = Mathf.Sin(a), c = Mathf.Cos(a);
                pts[i] = new Vector2(
                    center.x + half.x * scale * c * (1f - 0.45f * s * s),
                    center.z + half.z * scale * s);
            }
            return pts;
        }

        /// <summary>Andrew's monotone chain; returns counter-clockwise hull.</summary>
        public static Vector2[] ConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            int n = points.Count;
            Vector2[] hull = new Vector2[2 * n];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                while (k >= 2 && Cross(hull[k - 2], hull[k - 1], points[i]) <= 0f) k--;
                hull[k++] = points[i];
            }
            for (int i = n - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Cross(hull[k - 2], hull[k - 1], points[i]) <= 0f) k--;
                hull[k++] = points[i];
            }
            Vector2[] result = new Vector2[Mathf.Max(0, k - 1)];
            System.Array.Copy(hull, result, result.Length);
            return result;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        /// <summary>
        /// Evenly spaced points around a CCW polygon with outward unit normals.
        /// </summary>
        public static void Resample(Vector2[] polygon, int count, Vector2[] points, Vector2[] normals)
        {
            int n = polygon.Length;
            float total = 0f;
            for (int i = 0; i < n; i++)
                total += Vector2.Distance(polygon[i], polygon[(i + 1) % n]);

            float step = total / count;
            float target = 0f, walked = 0f;
            int edge = 0;
            for (int p = 0; p < count; p++)
            {
                target = p * step;
                while (edge < n - 1 && walked + Vector2.Distance(polygon[edge], polygon[(edge + 1) % n]) < target)
                {
                    walked += Vector2.Distance(polygon[edge], polygon[(edge + 1) % n]);
                    edge++;
                }
                Vector2 a = polygon[edge];
                Vector2 b = polygon[(edge + 1) % n];
                float len = Mathf.Max(1e-4f, Vector2.Distance(a, b));
                float t = Mathf.Clamp01((target - walked) / len);
                points[p] = Vector2.Lerp(a, b, t);
                Vector2 dir = (b - a) / len;
                normals[p] = new Vector2(dir.y, -dir.x); // outward for CCW polygons
            }
        }
    }
}
