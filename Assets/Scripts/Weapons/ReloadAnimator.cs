using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// First-person reloads with the hands (local player's view model only).
/// Magazine guns: the gun tips to show the magwell, the support hand pulls the magazine, takes it away, comes back
/// with a fresh one and seats it, and racks the slide / charging handle if the gun was empty.
/// One round at a time (pump, lever, bolt, revolvers): the support hand fetches each round and pushes it in at the
/// loading port. Launchers: a fresh rocket goes in at the muzzle. The firing hand stays on the grip throughout.
/// </summary>
public class ReloadAnimator
{
    enum Kind { Magazine, PerRound, Muzzle, Generic }

    readonly WeaponModel model;
    readonly WeaponDef def;
    readonly Transform left;
    readonly Vector3 leftRestLocal;
    readonly Quaternion leftRestLocalRot;
    readonly Kind kind;

    bool pistol;                 // magazine in the grip (pistols): slide-release reloads
    Vector3 boltRelease;         // where a rifle's bolt release is slapped (weapon space)
    Transform magRig;            // the magazine's parts, re-parented so they can come out as one piece
    Vector3 magRest, magDown;    // rig rest position and the way it slides out (weapon space)
    float magLength;
    Vector3 loadPoint;           // where rounds go in (weapon space)
    Transform round;             // the cartridge / shell / rocket in the support hand
    float weight;                // 0 = hands at rest, 1 = fully into the reload pose
    Vector3 slideRest;

    public ReloadAnimator(WeaponModel model, WeaponDef def, Transform leftTarget)
    {
        this.model = model;
        this.def = def;
        left = leftTarget;
        if (left != null)
        {
            leftRestLocal = left.localPosition;
            leftRestLocalRot = left.localRotation;
        }
        if (model.slide != null) slideRest = model.slide.localPosition;

        if (def.Projectile == ProjectileKind.Rocket) kind = Kind.Muzzle;
        else if (def.ReloadPerRound) kind = Kind.PerRound;
        else kind = BuildMagRig() ? Kind.Magazine : Kind.Generic;

        // Where single rounds go in.
        var root = model.transform;
        if (model.cylinder != null) loadPoint = root.InverseTransformPoint(model.cylinder.position) + new Vector3(-0.045f, 0f, 0f);
        else if (def.Id == WeaponId.Remington870) loadPoint = new Vector3(0f, -0.012f, 0.075f);
        else if (FindPart(root, "LoadingGate") is { } gate) loadPoint = root.InverseTransformPoint(gate.position) + new Vector3(0.02f, 0f, 0f);
        else if (model.ejectionPort != null) loadPoint = root.InverseTransformPoint(model.ejectionPort.position) + new Vector3(0f, 0.03f, 0f);
        else loadPoint = new Vector3(0f, 0.05f, 0.05f);
        if (kind == Kind.Muzzle && model.muzzle != null) loadPoint = root.InverseTransformPoint(model.muzzle.position) + new Vector3(0f, 0f, 0.06f);

        if (left != null)
        {
            round = Prims.Make(PrimitiveType.Capsule).transform;
            round.name = "Reload Round";
            round.SetParent(model.transform, false);
            var r = round.GetComponent<Renderer>();
            r.sharedMaterial = GameAssets.I.gunMetal;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            bool shell = def.Id == WeaponId.Remington870 || def.Id == WeaponId.AA12;
            block.SetColor("_Color", kind == Kind.Muzzle ? new Color(0.35f, 0.4f, 0.3f) : shell ? new Color(0.65f, 0.12f, 0.1f) : new Color(0.85f, 0.65f, 0.25f));
            r.SetPropertyBlock(block);
            float len = kind == Kind.Muzzle ? 0.28f : shell ? 0.07f : 0.045f, rad = kind == Kind.Muzzle ? 0.04f : shell ? 0.0105f : 0.006f;
            round.localScale = new Vector3(rad * 2f, len * 0.5f, rad * 2f);
            round.gameObject.layer = model.gameObject.layer;
            round.gameObject.SetActive(false);
        }
    }

    /// <summary>Collect the magazine's parts under one transform. False if this gun has no detachable magazine.</summary>
    bool BuildMagRig()
    {
        var root = model.transform;
        var names = new HashSet<string> { "Magazine", "Mag", "MagBase", "MagFloorplate", "MagRibs", "Drum", "DrumFace", "DrumCapL", "DrumCapR" };
        var parts = new List<Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>())
            if (names.Contains(t.name) && t.GetComponent<MeshRenderer>() != null) parts.Add(t);
        if (parts.Count == 0) return false;

        Bounds b = HandFit.LocalBounds(root, parts[0].GetComponent<Renderer>());
        foreach (var p in parts) b.Encapsulate(HandFit.LocalBounds(root, p.GetComponent<Renderer>()));
        // Pistols: the magazine lives inside the grip and comes out along its rake.
        var grip = parts[0].parent != null && parts[0].parent.name == "Grip" ? parts[0].parent : null;
        magDown = grip != null ? (Quaternion.Inverse(root.rotation) * grip.rotation * Vector3.down).normalized : Vector3.down;
        magLength = Mathf.Max(0.06f, b.size.y);

        pistol = grip != null;
        boltRelease = b.center + new Vector3(-0.03f, b.extents.y + 0.03f, -0.01f);
        magRig = new GameObject("Magazine Rig").transform;
        magRig.SetParent(root, false);
        magRig.localPosition = b.center;
        magRest = b.center;
        foreach (var p in parts) p.SetParent(magRig, true);
        if (grip != null && parts.Count <= 2)
        {
            // Only the floorplate shows on a pistol: give the rig a real magazine body hidden inside the grip.
            var body = Prims.Make(PrimitiveType.Cube).transform;
            body.name = "MagBody";
            body.SetParent(magRig, false);
            body.localRotation = Quaternion.Inverse(root.rotation) * grip.rotation;
            body.localPosition = -magDown * 0.05f;
            body.localScale = new Vector3(0.022f, 0.1f, 0.03f);
            var r = body.GetComponent<Renderer>();
            r.sharedMaterial = GameAssets.I.gunDark;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.gameObject.layer = model.gameObject.layer;
            magLength = 0.11f;
        }
        return true;
    }

    static Transform FindPart(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
        return null;
    }

    static float Ease(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

    /// <summary>
    /// Pose the support hand, magazine and round for this frame; returns extra gun offset / rotation (view space)
    /// so the gun tips toward you to show what's happening.
    /// </summary>
    public void Update(bool reloading, float t, bool fromEmpty, out Vector3 gunOffset, out Vector3 gunEuler)
    {
        gunOffset = Vector3.zero;
        gunEuler = Vector3.zero;
        weight = Mathf.MoveTowards(weight, reloading ? 1f : 0f, Time.deltaTime * (reloading ? 6f : 4f));
        if (weight <= 0f)
        {
            if (left != null) { left.localPosition = leftRestLocal; left.localRotation = leftRestLocalRot; }
            if (magRig != null) magRig.localPosition = magRest;
            if (round != null && round.gameObject.activeSelf) round.gameObject.SetActive(false);
            if (magRig != null && !magRig.gameObject.activeSelf) magRig.gameObject.SetActive(true);
            return;
        }
        if (!reloading) t = 1f;
        var root = model.transform;
        Vector3 rest = root.InverseTransformPoint(left != null ? left.parent.TransformPoint(leftRestLocal) : Vector3.zero);
        Vector3 hand = rest;
        bool showRound = false;
        Vector3 roundPos = Vector3.zero;
        float tip;

        switch (kind)
        {
            case Kind.Magazine:
            {
                Vector3 bottom = magRest + magDown * magLength * 0.5f;
                Vector3 pouch = bottom + magDown * 0.3f + new Vector3(-0.12f, 0f, -0.08f);
                Vector3 below = bottom + magDown * 0.07f;
                Vector3 magOff = Vector3.zero;
                bool magShown = true;
                if (t < 0.15f) hand = Vector3.Lerp(rest, bottom, Ease(0f, 0.15f, t));
                else if (t < 0.3f) { float k = Ease(0.15f, 0.3f, t); magOff = magDown * magLength * 1.1f * k; hand = bottom + magOff; }
                else if (t < 0.45f) { float k = Ease(0.3f, 0.45f, t); hand = Vector3.Lerp(bottom + magDown * magLength * 1.1f, pouch, k); magOff = hand - bottom; magShown = k < 0.85f; }
                else if (t < 0.62f) { float k = Ease(0.45f, 0.62f, t); hand = Vector3.Lerp(pouch, below, k); magOff = hand - bottom; magShown = k > 0.15f; }
                else if (t < 0.7f) { float k = Ease(0.62f, 0.7f, t); hand = Vector3.Lerp(below, bottom, k); magOff = hand - bottom; if (k > 0.9f) gunOffset += Vector3.up * 0.004f; }
                else if (fromEmpty && !pistol && t < 0.88f)
                {
                    // Rifles: slap the bolt release on the left of the receiver, just above the magwell.
                    float k = Ease(0.7f, 0.8f, t), back = Ease(0.8f, 0.88f, t);
                    hand = Vector3.Lerp(Vector3.Lerp(bottom, boltRelease, k), boltRelease + Vector3.down * 0.02f, back);
                }
                else hand = Vector3.Lerp(fromEmpty && !pistol ? boltRelease : bottom, rest, Ease(fromEmpty && !pistol ? 0.88f : 0.7f, 1f, t));
                // Empty: the slide / bolt sits locked back until the new magazine is in (pistols) or the release is hit.
                if (model.slide != null && fromEmpty)
                    model.slide.localPosition = slideRest - Vector3.forward * (pistol ? 0.028f : 0.05f) * (t < (pistol ? 0.68f : 0.8f) ? 1f : 0f);
                if (magRig != null)
                {
                    magRig.localPosition = magRest + magOff;
                    magRig.gameObject.SetActive(magShown);
                }
                // Lift and roll the gun in toward the middle so the magwell and the magazine stay in view.
                tip = Mathf.Sin(Ease(0f, 1f, t) * Mathf.PI);
                gunEuler = (pistol ? new Vector3(-12f, -12f, -30f) : new Vector3(-14f, -20f, -38f)) * tip; // underside toward you
                gunOffset += (pistol ? new Vector3(-0.06f, 0.08f, -0.03f) : new Vector3(-0.1f, 0.13f, 0.03f)) * tip;
                break;
            }
            case Kind.PerRound:
            case Kind.Muzzle:
            {
                // Each round: down to the belt / pouch, up with a round, push it in, back for the next.
                bool muzzle = kind == Kind.Muzzle;
                Vector3 pouch = loadPoint + new Vector3(-0.1f, -0.22f, muzzle ? -0.15f : -0.06f);
                Vector3 inside = loadPoint + (muzzle ? Vector3.back * 0.12f : model.cylinder != null ? Vector3.right * 0.02f : def.Id == WeaponId.Remington870 ? new Vector3(0f, 0.02f, -0.02f) : Vector3.down * 0.025f);
                if (t < 0.35f) hand = Vector3.Lerp(loadPoint, pouch, Ease(0f, 0.35f, t));
                else if (t < 0.72f) { hand = Vector3.Lerp(pouch, loadPoint, Ease(0.35f, 0.72f, t)); showRound = t > 0.42f; }
                else if (t < 0.88f) { hand = Vector3.Lerp(loadPoint, inside, Ease(0.72f, 0.88f, t)); showRound = true; }
                else hand = Vector3.Lerp(inside, loadPoint, Ease(0.88f, 1f, t));
                roundPos = hand + new Vector3(0f, 0.025f, muzzle ? 0.1f : 0.01f);
                // From the fore-end into the loading rhythm and back out again.
                hand = Vector3.Lerp(rest, hand, weight);
                tip = 1f;
                // Turn the loading point toward you: the 870's port is underneath, a revolver's cylinder opens to the
                // left, lever gates and bolt actions load from the right / top.
                float roll = def.Id == WeaponId.Remington870 ? -36f : model.cylinder != null ? -28f : 24f;
                gunEuler = muzzle ? new Vector3(-6f, 0f, 0f) : new Vector3(-12f, -16f, roll);
                gunOffset = muzzle ? new Vector3(0f, -0.05f, -0.06f) : new Vector3(-0.07f, 0.1f, 0.0f);
                break;
            }
            default:
            {
                // No magazine to swap (belt-fed etc.): hand to the action and back.
                Vector3 top = loadPoint;
                if (t < 0.3f) hand = Vector3.Lerp(rest, top, Ease(0f, 0.3f, t));
                else if (t < 0.7f) hand = top + new Vector3(0f, 0.015f * Mathf.Sin((t - 0.3f) * 25f), 0f);
                else hand = Vector3.Lerp(top, rest, Ease(0.7f, 1f, t));
                tip = Mathf.Sin(Ease(0f, 1f, t) * Mathf.PI);
                gunEuler = new Vector3(-6f, -6f, 18f) * tip;
                gunOffset = new Vector3(-0.02f, 0.02f, -0.02f) * tip;
                break;
            }
        }

        if (left != null) left.position = root.TransformPoint(Vector3.Lerp(rest, hand, weight));
        if (round != null)
        {
            round.gameObject.SetActive(showRound && weight > 0.5f);
            if (showRound)
            {
                round.localPosition = roundPos;
                round.localRotation = Quaternion.Euler(kind == Kind.Muzzle ? 90f : 75f, 0f, 0f);
            }
        }
        gunOffset *= weight;
        gunEuler *= weight;
    }
}
