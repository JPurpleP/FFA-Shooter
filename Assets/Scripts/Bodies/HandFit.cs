using System.Collections.Generic;
using UnityEngine;

/// <summary>How a hand closes around what it holds: the size of the thing, and whether it's held like a pistol grip
/// (fingers wrap round the front, thumb round the back) or a bar from underneath (fore-ends, stock wrists).</summary>
public struct GripFit
{
    public float W;   // half-thickness from palm to fingertips' side
    public float Hd;  // half-depth across the direction the fingers curl
    public bool Bar;  // held from underneath / alongside rather than as a pistol grip

    public static GripFit Pistol => new() { W = 0.016f, Hd = 0.023f };
    public static GripFit Fist => new() { W = 0.004f, Hd = 0.009f };
    public bool Same(GripFit o) => Mathf.Approximately(W, o.W) && Mathf.Approximately(Hd, o.Hd) && Bar == o.Bar;
}

/// <summary>
/// Places the firing and support hand targets on a built weapon by measuring its parts, so the hands sit on the
/// surface of each grip and fore-end instead of inside it (and stay right when a model changes).
/// Hand space: +y runs along what's held, +z is the way the fingers curl round its front, the grip axis passes the origin.
/// </summary>
public static class HandFit
{
    const float Gap = 0.0015f; // skin clearance
    const float SupportRoll = 40f; // degrees the support hand is rolled round the fore-end, palm toward the left

    public static Transform FiringHand(WeaponModel model, out GripFit fit)
    {
        var t = new GameObject("RightHand").transform;
        fit = GripFit.Pistol;
        var body = FindPart(model.transform, "GripBody") ?? FindPart(model.transform, "GripWood") ?? FindPart(model.transform, "GripFrame");
        if (body != null && body.parent != null)
        {
            // Pistol grip: follow its rake. The index finger sits just under the frame, the rest wrap round the front.
            var size = PartSize(body);
            t.SetParent(body.parent, false);
            float top = body.localPosition.y + size.y * 0.5f;
            t.localPosition = new Vector3(body.localPosition.x, top - 0.03f, body.localPosition.z);
            t.localRotation = body.localRotation;
            fit = new GripFit { W = size.x * 0.5f + Gap, Hd = size.z * 0.5f + Gap };
            return t;
        }
        var wrist = FindPart(model.transform, "Wrist");
        if (wrist != null)
        {
            // Straight stock: the hand wraps the wrist from the right, fingers under it.
            var size = PartSize(wrist);
            t.SetParent(wrist.parent, false);
            t.localPosition = wrist.localPosition + wrist.localRotation * new Vector3(0f, -size.y * 0.15f, size.z * 0.15f);
            t.localRotation = wrist.localRotation * Quaternion.LookRotation(Vector3.down, Vector3.forward);
            fit = new GripFit { W = size.x * 0.5f + Gap, Hd = size.y * 0.5f + Gap, Bar = true };
            return t;
        }
        t.SetParent(model.transform, false);
        t.localPosition = new Vector3(0f, -0.035f, -0.01f);
        return t;
    }

    /// <summary>The support hand at the weapon's LeftHand point, fitted to whatever is there.</summary>
    public static Transform SupportHand(WeaponModel model, WeaponDef def, Transform firing, GripFit firingFit, out GripFit fit)
    {
        var t = new GameObject("LeftHand").transform;
        var root = model.transform;
        Vector3 p = def.LeftHand;

        // Vertical foregrip near the point: hold it like a pistol grip.
        foreach (var name in new[] { "Foregrip", "FrontGrip" })
        {
            var g = FindPart(root, name);
            if (g == null) continue;
            var gs = PartSize(g);
            Vector3 gp = root.InverseTransformPoint(g.position);
            if (Mathf.Abs(gp.z - p.z) > 0.09f) continue;
            t.SetParent(g.parent, false);
            t.localPosition = g.localPosition + new Vector3(0f, gs.y * 0.5f - 0.032f, 0f);
            t.localRotation = g.localRotation;
            fit = new GripFit { W = gs.x * 0.5f + Gap, Hd = gs.z * 0.5f + Gap };
            return t;
        }

        // Two-handed pistol: the support hand wraps over the firing hand's fingers.
        if (p.z < 0.06f && p.y < 0f && !firingFit.Bar && firing != null)
        {
            t.SetParent(firing.parent, false);
            t.localPosition = firing.localPosition + firing.localRotation * new Vector3(-0.004f, -0.012f, 0.004f);
            t.localRotation = firing.localRotation;
            fit = new GripFit { W = firingFit.W + 0.0175f, Hd = firingFit.Hd + 0.016f };
            return t;
        }

        // Fore-end: measure the cross-section the hand closes round (parts touching the point within a hand's width).
        var parts = new List<(Renderer r, Bounds b)>();
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is not MeshRenderer) continue;
            var b = LocalBounds(root, r);
            if (b.max.z < p.z - 0.04f || b.min.z > p.z + 0.04f) continue;
            float dx = Mathf.Max(0f, Mathf.Max(b.min.x - p.x, p.x - b.max.x)), dy = Mathf.Max(0f, Mathf.Max(b.min.y - p.y, p.y - b.max.y));
            if (dx * dx + dy * dy > 0.008f * 0.008f) continue;
            if (b.extents.x > 0.04f || b.extents.y > 0.04f) continue; // drums, scopes, bipods: not what the hand closes round
            parts.Add((r, b));
        }
        Bounds cross;
        if (parts.Count > 0)
        {
            cross = parts[0].b;
            foreach (var (_, b) in parts) cross.Encapsulate(b);
        }
        else cross = new Bounds(p + Vector3.up * 0.022f, new Vector3(0.045f, 0.045f, 0.1f));
        // Ride along with a pump or a break-action barrel when that's what's held.
        Transform parent = root;
        foreach (var (r, _) in parts)
        {
            if (model.pump != null && r.transform.IsChildOf(model.pump)) { parent = model.pump; break; }
            if (model.breakAction != null && r.transform.IsChildOf(model.breakAction)) { parent = model.breakAction; break; }
        }
        // Cradled from the lower left: palm under and to the left, fingers round the bottom and up the right side,
        // thumb along the top-left. Rolled off-axis, so it's sized for the cross-section's diagonal.
        Vector3 local = new Vector3(cross.center.x, cross.center.y, p.z);
        t.SetParent(root, false);
        t.localPosition = local;
        t.localRotation = Quaternion.AngleAxis(-SupportRoll, Vector3.forward) * Quaternion.LookRotation(Vector3.right, Vector3.forward);
        if (parent != root) t.SetParent(parent, true);
        float big = Mathf.Max(cross.extents.x, cross.extents.y);
        float diag = Mathf.Lerp(big, new Vector2(cross.extents.x, cross.extents.y).magnitude, 0.8f);
        fit = new GripFit { W = diag + Gap, Hd = diag + Gap, Bar = true };
        return t;
    }

    static Transform FindPart(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>())
            if (t.name == name && t.GetComponent<MeshFilter>() != null) return t;
        return null;
    }

    /// <summary>Box parts are unit cubes scaled to size; cylinders are 2 units tall.</summary>
    static Vector3 PartSize(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        var s = t.localScale;
        if (mf == null || mf.sharedMesh == null) return s;
        var m = mf.sharedMesh.bounds.size;
        return new Vector3(m.x * s.x, m.y * s.y, m.z * s.z);
    }

    /// <summary>A renderer's bounds in the weapon's own space (exact corners, so rotated parts are covered).</summary>
    static Bounds LocalBounds(Transform root, Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        var mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
        Bounds b = default;
        bool first = true;
        for (int i = 0; i < 8; i++)
        {
            var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var w = m.MultiplyPoint3x4(c);
            if (first) { b = new Bounds(w, Vector3.zero); first = false; }
            else b.Encapsulate(w);
        }
        return b;
    }
}
