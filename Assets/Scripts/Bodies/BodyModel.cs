using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Simple humanoid made of primitives. All 11 parts are direct children of the body root, each with its
/// origin at the joint it hangs from (so a ragdoll can be made by cloning and adding physics).
/// Arms use two-bone IK to hold the weapon; legs swing procedurally with movement speed.
/// </summary>
public class BodyModel : MonoBehaviour
{
    public enum Part { Pelvis, Chest, Head, UpperArmL, LowerArmL, UpperArmR, LowerArmR, UpperLegL, LowerLegL, UpperLegR, LowerLegR }
    public const int PartCount = 11;

    public struct PartSpec
    {
        public Part Parent;
        public bool HasParent;
        public float Length, Radius, Mass;
    }

    public static readonly PartSpec[] Specs =
    {
        new PartSpec { HasParent = false, Mass = 12f },                                            // Pelvis
        new PartSpec { HasParent = true, Parent = Part.Pelvis, Mass = 16f },                       // Chest
        new PartSpec { HasParent = true, Parent = Part.Chest, Mass = 5f },                         // Head
        new PartSpec { HasParent = true, Parent = Part.Chest, Length = 0.3f, Radius = 0.06f, Mass = 2.5f },
        new PartSpec { HasParent = true, Parent = Part.UpperArmL, Length = 0.28f, Radius = 0.05f, Mass = 1.8f },
        new PartSpec { HasParent = true, Parent = Part.Chest, Length = 0.3f, Radius = 0.06f, Mass = 2.5f },
        new PartSpec { HasParent = true, Parent = Part.UpperArmR, Length = 0.28f, Radius = 0.05f, Mass = 1.8f },
        new PartSpec { HasParent = true, Parent = Part.Pelvis, Length = 0.42f, Radius = 0.075f, Mass = 7f },
        new PartSpec { HasParent = true, Parent = Part.UpperLegL, Length = 0.4f, Radius = 0.065f, Mass = 4.5f },
        new PartSpec { HasParent = true, Parent = Part.Pelvis, Length = 0.42f, Radius = 0.075f, Mass = 7f },
        new PartSpec { HasParent = true, Parent = Part.UpperLegR, Length = 0.4f, Radius = 0.065f, Mass = 4.5f },
    };

    static readonly Vector3 PelvisPos = new(0f, 0.95f, 0f);
    static readonly Vector3 WaistPos = new(0f, 1.05f, 0f);
    static readonly Vector3 NeckPos = new(0f, 1.55f, 0f);
    static readonly Vector3 ShoulderL = new(-0.25f, 1.47f, 0f);
    static readonly Vector3 ShoulderR = new(0.25f, 1.47f, 0f);
    static readonly Vector3 HipL = new(-0.1f, 0.86f, 0f);
    static readonly Vector3 HipR = new(0.1f, 0.86f, 0f);

    public Transform[] parts = new Transform[PartCount];
    public Renderer[] armRenderers;
    public Renderer[] otherRenderers;

    [Header("Pose input")]
    public float aimPitch;
    public float lean;
    public float crouch, slide, prone; // 0..1 pose weights
    public float airborne;             // 0..1: legs tuck up while off the ground
    public Transform rightHandTarget;
    public Transform leftHandTarget;
    public bool animateLegs = true;

    Vector3 lastPos;
    float smoothedSpeed, phase;
    float legSign = 1f, smoothPitch, breathOffset = -1f;

    public Transform Get(Part p) => parts[(int)p];

    /// <summary>World-space skeleton as point pairs (ESP overlay), following the live, animated pose.</summary>
    public void GetBones(List<Vector3> pts)
    {
        pts.Clear();
        Vector3 P(Part p) => Get(p).position;
        Vector3 End(Part p) => Get(p).TransformPoint(Vector3.down * Specs[(int)p].Length);
        void Bone(Vector3 a, Vector3 b) { pts.Add(a); pts.Add(b); }
        Vector3 neck = P(Part.Head), top = Get(Part.Head).TransformPoint(new Vector3(0f, 0.3f, 0f));
        Vector3 chest = P(Part.Chest), pelvis = P(Part.Pelvis);
        Bone(top, neck);
        Bone(neck, chest);
        Bone(chest, pelvis);
        foreach (var (upper, lower) in new[] { (Part.UpperArmL, Part.LowerArmL), (Part.UpperArmR, Part.LowerArmR) })
        {
            Bone(neck, P(upper));
            Bone(P(upper), P(lower));
            Bone(P(lower), End(lower));
        }
        foreach (var (upper, lower) in new[] { (Part.UpperLegL, Part.LowerLegL), (Part.UpperLegR, Part.LowerLegR) })
        {
            Bone(pelvis, P(upper));
            Bone(P(upper), P(lower));
            Bone(P(lower), End(lower));
        }
    }

    // ------------------------------------------------------------------ construction

    /// <summary>Everyone wears the same white "FREEDOM" tee; `accent` (the player color) goes on the armbands.</summary>
    public static BodyModel Build(Transform parent, Color accent, bool mannequin = false, int skinId = 0)
    {
        var sk = Skins.Get(skinId);
        Color shirt = mannequin ? accent : sk.Shirt;
        var a = GameAssets.I;
        Material shirtMat = mannequin ? null : a.ShirtPatternMaterial(sk.Pattern);
        var rootGo = new GameObject("Body");
        rootGo.transform.SetParent(parent, false);
        var body = rootGo.AddComponent<BodyModel>();
        var root = rootGo.transform;

        Color pants = mannequin ? shirt : sk.Pants;
        Color boots = mannequin ? shirt * 0.9f : sk.Boots;
        Color skin = mannequin ? shirt : sk.SkinTone.a > 0f ? sk.SkinTone : new Color(0.86f, 0.66f, 0.52f);
        var arms = new System.Collections.Generic.List<Renderer>();
        var others = new System.Collections.Generic.List<Renderer>();

        Transform MakePart(Part p, Vector3 origin)
        {
            var t = new GameObject(p.ToString()).transform;
            t.SetParent(root, false);
            t.localPosition = origin;
            body.parts[(int)p] = t;
            return t;
        }

        Renderer Visual(Transform part, PrimitiveType type, Vector3 center, Vector3 scale, Color color, Material mat = null, Vector3 euler = default)
        {
            var go = Prims.Make(type);
            go.transform.SetParent(part, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat != null ? mat : (mannequin ? a.mannequin : a.playerBody);
            if (mat == null) Prims.Tint(r, color);
            return r;
        }

        Color hair = sk.Hair;

        // Hips: rounded (horizontal capsule) with a belt.
        var pelvis = MakePart(Part.Pelvis, PelvisPos);
        others.Add(Visual(pelvis, PrimitiveType.Capsule, new Vector3(0f, -0.01f, 0f), new Vector3(0.22f, 0.18f, 0.21f), pants, null, new Vector3(0f, 0f, 90f)));
        others.Add(Visual(pelvis, PrimitiveType.Capsule, new Vector3(0f, 0.075f, 0f), new Vector3(0.2f, 0.185f, 0.215f), new Color(0.13f, 0.1f, 0.08f), null, new Vector3(0f, 0f, 90f))); // belt

        // Torso: flat-fronted core (keeps the chest print flat) rounded off by side and shoulder capsules.
        var chest = MakePart(Part.Chest, WaistPos);
        others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.24f, 0f), new Vector3(0.3f, 0.44f, 0.24f), shirt, shirtMat));
        others.Add(Visual(chest, PrimitiveType.Capsule, new Vector3(-0.14f, 0.24f, 0f), new Vector3(0.22f, 0.25f, 0.235f), shirt, shirtMat));
        others.Add(Visual(chest, PrimitiveType.Capsule, new Vector3(0.14f, 0.24f, 0f), new Vector3(0.22f, 0.25f, 0.235f), shirt, shirtMat));
        others.Add(Visual(chest, PrimitiveType.Capsule, new Vector3(0f, 0.42f, -0.005f), new Vector3(0.2f, 0.25f, 0.21f), shirt, shirtMat, new Vector3(0f, 0f, 90f))); // shoulder line
        others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(0f, 0.3f, 0.035f), new Vector3(0.32f, 0.26f, 0.18f), shirt, shirtMat)); // pecs, rounds the front edges
        if (!mannequin)
        {
            // Crew neck collar.
            others.Add(Visual(chest, PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0f), new Vector3(0.16f, 0.015f, 0.15f), shirt * 0.92f, shirtMat));
            if (sk.FreedomPrint)
            {
                var print = Visual(chest, PrimitiveType.Quad, new Vector3(0f, 0.35f, 0.127f), new Vector3(0.3f, 0.047f, 1f), Color.white, a.freedomPrint, new Vector3(0f, 180f, 0f));
                print.shadowCastingMode = ShadowCastingMode.Off;
                others.Add(print);
            }
            if (sk.WehrmachtGear)
            {
                var green = new Color(0.2f, 0.27f, 0.2f);
                var leather = new Color(0.07f, 0.06f, 0.05f);
                var button = new Color(0.32f, 0.33f, 0.3f);
                // M36 tunic: bottle-green collar with collar tabs, shoulder boards, buttons and pocket flaps.
                others.Add(Visual(chest, PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0f), new Vector3(0.17f, 0.032f, 0.16f), green));
                foreach (float side in new[] { -1f, 1f })
                {
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.04f, 0.497f, 0.074f), new Vector3(0.026f, 0.022f, 0.006f), green * 0.8f, null, new Vector3(0f, side * 25f, 0f)));
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.04f, 0.497f, 0.077f), new Vector3(0.022f, 0.004f, 0.004f), new Color(0.7f, 0.72f, 0.68f), null, new Vector3(0f, side * 25f, 0f)));
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.165f, 0.475f, 0f), new Vector3(0.05f, 0.012f, 0.09f), green));
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.085f, 0.37f, 0.128f), new Vector3(0.075f, 0.022f, 0.006f), sk.Shirt * 0.85f)); // breast pocket flap
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.095f, 0.17f, 0.131f), new Vector3(0.085f, 0.024f, 0.006f), sk.Shirt * 0.85f)); // skirt pocket flap
                }
                for (int i = 0; i < 5; i++)
                    others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(0f, 0.44f - i * 0.075f, 0.13f), Vector3.one * 0.016f, button));
                // Black belt, plain steel buckle, Y-straps, three-cell K98 ammo pouches.
                others.Add(Visual(chest, PrimitiveType.Cylinder, new Vector3(0f, 0.06f, 0f), new Vector3(0.31f, 0.022f, 0.265f), leather));
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.136f), new Vector3(0.045f, 0.034f, 0.008f), new Color(0.5f, 0.52f, 0.5f), a.gunMetal));
                foreach (float side in new[] { -1f, 1f })
                {
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.08f, 0.27f, 0.128f), new Vector3(0.026f, 0.42f, 0.008f), leather, null, new Vector3(0f, 0f, side * -3f)));
                    for (int c = 0; c < 3; c++)
                        others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * (0.045f + c * 0.034f), 0.105f, 0.138f), new Vector3(0.03f, 0.052f, 0.026f), leather));
                }
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.27f, -0.124f), new Vector3(0.03f, 0.42f, 0.008f), leather)); // Y-strap back
                // Bread bag on the right hip, ribbed gas-mask canister on the back.
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0.16f, 0.0f, -0.05f), new Vector3(0.05f, 0.11f, 0.13f), new Color(0.42f, 0.4f, 0.3f)));
                var canister = new Color(0.33f, 0.36f, 0.3f);
                others.Add(Visual(chest, PrimitiveType.Cylinder, new Vector3(0.07f, 0.1f, -0.17f), new Vector3(0.085f, 0.13f, 0.085f), canister));
                for (int r = 0; r < 3; r++)
                    others.Add(Visual(chest, PrimitiveType.Cylinder, new Vector3(0.07f, 0.02f + r * 0.08f, -0.17f), new Vector3(0.09f, 0.006f, 0.09f), canister * 0.85f));
            }
            if (sk.FlagPrint)
            {
                var flag = Visual(chest, PrimitiveType.Quad, new Vector3(0f, 0.3f, 0.129f), new Vector3(0.26f, 0.165f, 1f), Color.white, a.rebelFlagPrint, new Vector3(0f, 180f, 0f));
                flag.shadowCastingMode = ShadowCastingMode.Off;
                others.Add(flag);
            }
            if (sk.TunicButtons)
            {
                var brassColor = new Color(0.85f, 0.68f, 0.3f);
                for (int i = 0; i < 6; i++)
                    others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(0f, 0.44f - i * 0.055f, 0.128f), Vector3.one * 0.018f, brassColor, a.brass));
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.065f, 0.13f), new Vector3(0.07f, 0.045f, 0.01f), brassColor, a.brass)); // belt plate
                others.Add(Visual(chest, PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0f), new Vector3(0.17f, 0.03f, 0.16f), sk.Shirt * 0.8f)); // stand collar
            }
            if (sk.HiVisStripes)
            {
                // Reflective bands around the torso.
                var band = new Color(0.78f, 0.8f, 0.82f);
                foreach (float by in new[] { 0.16f, 0.3f })
                {
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, by, 0.129f), new Vector3(0.36f, 0.035f, 0.006f), band));
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, by, -0.124f), new Vector3(0.36f, 0.035f, 0.006f), band));
                }
            }
            if (sk.ChefButtons)
                foreach (float bx in new[] { -0.05f, 0.05f })
                    for (int i = 0; i < 3; i++)
                        others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(bx, 0.36f - i * 0.09f, 0.128f), Vector3.one * 0.022f, new Color(0.85f, 0.85f, 0.83f)));
            if (sk.Backpack)
            {
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.27f, -0.19f), new Vector3(0.32f, 0.4f, 0.13f), new Color(0.88f, 0.88f, 0.9f)));
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.38f, 0.13f), new Vector3(0.14f, 0.09f, 0.03f), new Color(0.3f, 0.35f, 0.45f))); // chest control panel
                foreach (float bx in new[] { -0.08f, 0.08f })
                    others.Add(Visual(chest, PrimitiveType.Cylinder, new Vector3(bx, 0.27f, -0.27f), new Vector3(0.06f, 0.16f, 0.06f), new Color(0.75f, 0.75f, 0.78f)));
            }
            if (sk.Belly)
                others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(0f, 0.15f, 0.07f), new Vector3(0.36f, 0.3f, 0.3f), shirt, shirtMat));
            if (sk.JacketLapels)
            {
                // Suit: shirt showing in the V, lapels, two jacket buttons.
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.39f, 0.131f), new Vector3(0.07f, 0.2f, 0.006f), sk.UnderShirt));
                foreach (float side in new[] { -1f, 1f })
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.048f, 0.39f, 0.134f), new Vector3(0.035f, 0.21f, 0.006f), shirt * 0.8f, null, new Vector3(0f, 0f, side * 12f)));
                foreach (float by in new[] { 0.22f, 0.14f })
                    others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(0f, by, 0.132f), Vector3.one * 0.016f, new Color(0.05f, 0.05f, 0.05f)));
            }
            if (sk.Tie.HasValue)
            {
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.468f, 0.135f), new Vector3(0.032f, 0.026f, 0.012f), sk.Tie.Value));
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.37f, 0.137f), new Vector3(0.042f, 0.17f, 0.008f), sk.Tie.Value));
            }
            if (sk.Overalls)
            {
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.22f, 0.132f), new Vector3(0.2f, 0.2f, 0.008f), pants));
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.25f, 0.137f), new Vector3(0.08f, 0.06f, 0.006f), pants * 0.85f)); // bib pocket
                foreach (float side in new[] { -1f, 1f })
                {
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.075f, 0.4f, 0.13f), new Vector3(0.032f, 0.2f, 0.008f), pants));
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.075f, 0.3f, -0.125f), new Vector3(0.032f, 0.36f, 0.008f), pants));
                    others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(side * 0.075f, 0.31f, 0.137f), Vector3.one * 0.018f, new Color(0.85f, 0.68f, 0.3f), a.brass));
                }
            }
            if (sk.Hood)
            {
                others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(0f, 0.47f, -0.1f), new Vector3(0.24f, 0.13f, 0.15f), shirt * 0.9f));
                foreach (float side in new[] { -1f, 1f })
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(side * 0.03f, 0.4f, 0.132f), new Vector3(0.006f, 0.08f, 0.006f), new Color(0.92f, 0.92f, 0.9f)));
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.14f, 0.131f), new Vector3(0.18f, 0.08f, 0.006f), shirt * 0.88f)); // front pocket
            }
            if (sk.FurCollar.HasValue)
            {
                others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(0f, 0.46f, 0f), new Vector3(0.44f, 0.13f, 0.34f), sk.FurCollar.Value));
                foreach (float side in new[] { -1f, 1f })
                    others.Add(Visual(chest, PrimitiveType.Sphere, new Vector3(side * 0.14f, 0.43f, 0.06f), new Vector3(0.12f, 0.1f, 0.12f), sk.FurCollar.Value * 0.9f));
            }
            if (sk.PlateCarrier)
            {
                // Plates front and back with shoulder straps and pouches.
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.27f, 0.145f), new Vector3(0.32f, 0.34f, 0.05f), sk.GearColor));
                others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(0f, 0.27f, -0.14f), new Vector3(0.32f, 0.36f, 0.05f), sk.GearColor));
                foreach (float sx in new[] { -0.1f, 0.1f })
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(sx, 0.47f, 0f), new Vector3(0.06f, 0.03f, 0.3f), sk.GearColor));
                foreach (float sx in new[] { -0.09f, 0f, 0.09f })
                    others.Add(Visual(chest, PrimitiveType.Cube, new Vector3(sx, 0.16f, 0.18f), new Vector3(0.07f, 0.1f, 0.04f), sk.GearColor * 0.85f));
            }
        }

        // Head: neck, skull, rounded jaw, generic face, ears and short black hair.
        var head = MakePart(Part.Head, NeckPos);
        others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.035f, 0f), new Vector3(0.11f, 0.05f, 0.11f), skin)); // neck
        others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.16f, 0.01f), new Vector3(0.21f, 0.25f, 0.23f), skin)); // skull
        others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.09f, 0.03f), new Vector3(0.17f, 0.13f, 0.17f), skin)); // jaw / chin
        if (!mannequin)
        {
            Color lips = new Color(0.66f, 0.42f, 0.38f);
            bool masked = sk.Mask.HasValue;
            if (masked)
            {
                // Luchador mask over the whole head, with trimmed eye and mouth openings.
                var m = sk.Mask.Value;
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.163f, 0.01f), new Vector3(0.222f, 0.262f, 0.242f), m));
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.092f, 0.03f), new Vector3(0.178f, 0.138f, 0.178f), m));
                foreach (float side in new[] { -1f, 1f })
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.045f, 0.176f, 0.118f), new Vector3(0.07f, 0.05f, 0.025f), sk.MaskTrim, null, new Vector3(0f, 0f, side * -15f)));
                others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.25f, 0.105f), new Vector3(0.02f, 0.09f, 0.01f), sk.MaskTrim, null, new Vector3(-30f, 0f, 0f))); // forehead stripe
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.108f, 0.115f), new Vector3(0.07f, 0.03f, 0.02f), sk.MaskTrim)); // mouth opening rim
            }
            foreach (float side in new[] { -1f, 1f })
            {
                if (sk.AlienEyes)
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.047f, 0.172f, 0.115f), new Vector3(0.07f, 0.045f, 0.03f), new Color(0.02f, 0.02f, 0.03f), a.visor, new Vector3(0f, 0f, side * -22f)));
                else
                {
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.042f, 0.175f, 0.121f), new Vector3(0.032f, 0.024f, 0.022f), new Color(0.95f, 0.95f, 0.93f))); // eye white
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.042f, 0.175f, 0.129f), new Vector3(0.016f, 0.016f, 0.01f), new Color(0.12f, 0.1f, 0.08f)));   // iris
                    if (!masked && !sk.ClownHair)
                        others.Add(Visual(head, PrimitiveType.Cube, new Vector3(side * 0.043f, 0.203f, 0.119f), new Vector3(0.042f, 0.009f, 0.012f), sk.Bald && !sk.Beard.HasValue ? skin * 0.8f : hair, null, new Vector3(0f, 0f, side * -6f))); // brow
                }
                if (!masked)
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.106f, 0.16f, 0f), new Vector3(0.024f, 0.058f, 0.042f), skin)); // ear
            }
            if (!sk.AlienEyes)
                others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(0f, 0.15f, 0.123f), new Vector3(0.026f, 0.025f, 0.03f), masked ? sk.Mask.Value : skin)); // nose
            others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.106f, 0.114f), new Vector3(0.048f, 0.008f, 0.01f), sk.ClownHair ? new Color(0.85f, 0.1f, 0.12f) : lips)); // mouth

            // Hair (unless bald, masked or fully covered).
            if (sk.Afro && !masked)
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.245f, -0.02f), new Vector3(0.37f, 0.34f, 0.37f), hair));
            if (sk.Combover)
            {
                for (int i = 0; i < 4; i++)
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0.005f, 0.283f - i * 0.003f, -0.03f + i * 0.025f), new Vector3(0.16f, 0.005f, 0.008f), hair, null, new Vector3(0f, 0f, -6f)));
                foreach (float side in new[] { -1f, 1f })
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.1f, 0.17f, -0.045f), new Vector3(0.05f, 0.08f, 0.1f), hair));
            }
            if (sk.LongHair && !masked)
            {
                others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(0f, 0.1f, -0.085f), new Vector3(0.21f, 0.13f, 0.1f), hair));
                foreach (float side in new[] { -1f, 1f })
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.098f, 0.12f, -0.015f), new Vector3(0.05f, 0.15f, 0.08f), hair));
            }
            if (!sk.Bald && !masked && !sk.Afro)
            {
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.215f, -0.012f), new Vector3(0.226f, 0.2f, 0.246f), hair));
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.15f, -0.04f), new Vector3(0.2f, 0.16f, 0.18f), hair));
                if (sk.Hat == HatKind.None)
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.262f, 0.04f), new Vector3(0.17f, 0.07f, 0.15f), hair)); // swept front
            }
            if (sk.ClownHair)
                foreach (float side in new[] { -1f, 1f })
                    for (int i = 0; i < 3; i++)
                        others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * (0.115f + i * 0.02f), 0.21f - i * 0.04f, -0.02f - i * 0.02f), Vector3.one * (0.12f - i * 0.015f), hair));

            // Face extras.
            if (sk.Mustache.HasValue)
            {
                others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(-0.025f, 0.125f, 0.12f), new Vector3(0.018f, 0.03f, 0.016f), sk.Mustache.Value, null, new Vector3(0f, 0f, 70f)));
                others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(0.025f, 0.125f, 0.12f), new Vector3(0.018f, 0.03f, 0.016f), sk.Mustache.Value, null, new Vector3(0f, 0f, -70f)));
            }
            if (sk.Beard.HasValue)
            {
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.075f, 0.055f), new Vector3(0.16f, 0.13f, 0.15f), sk.Beard.Value));
                others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(0f, 0.122f, 0.118f), new Vector3(0.016f, 0.035f, 0.014f), sk.Beard.Value, null, new Vector3(0f, 0f, 90f)));
            }
            if (sk.Goatee.HasValue)
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.075f, 0.105f), new Vector3(0.05f, 0.055f, 0.035f), sk.Goatee.Value));
            if (sk.Glasses)
            {
                var frame = new Color(0.08f, 0.07f, 0.06f);
                foreach (float side in new[] { -1f, 1f })
                {
                    float cx = side * 0.043f;
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(cx, 0.193f, 0.134f), new Vector3(0.05f, 0.005f, 0.006f), frame));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(cx, 0.162f, 0.134f), new Vector3(0.05f, 0.005f, 0.006f), frame));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(cx - 0.025f, 0.1775f, 0.134f), new Vector3(0.005f, 0.031f, 0.006f), frame));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(cx + 0.025f, 0.1775f, 0.134f), new Vector3(0.005f, 0.031f, 0.006f), frame));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(side * 0.1f, 0.185f, 0.075f), new Vector3(0.005f, 0.005f, 0.12f), frame)); // temple arm
                }
                others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.183f, 0.135f), new Vector3(0.03f, 0.005f, 0.006f), frame)); // bridge
            }
            if (sk.FaceMask.HasValue)
            {
                others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.122f, 0.122f), new Vector3(0.11f, 0.075f, 0.02f), sk.FaceMask.Value));
                foreach (float side in new[] { -1f, 1f })
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(side * 0.08f, 0.135f, 0.07f), new Vector3(0.004f, 0.006f, 0.1f), sk.FaceMask.Value * 0.9f, null, new Vector3(0f, side * -20f, 0f)));
            }
            if (sk.RedNose)
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.15f, 0.138f), Vector3.one * 0.05f, new Color(0.9f, 0.05f, 0.08f), a.visor));
            if (sk.Sunglasses)
            {
                foreach (float side in new[] { -1f, 1f })
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(side * 0.043f, 0.177f, 0.133f), new Vector3(0.05f, 0.03f, 0.008f), Color.black, a.visor));
                others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.183f, 0.134f), new Vector3(0.03f, 0.006f, 0.006f), Color.black, a.visor));
            }
            if (sk.Eyepatch)
            {
                others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(-0.042f, 0.176f, 0.128f), new Vector3(0.045f, 0.038f, 0.012f), new Color(0.03f, 0.03f, 0.03f)));
                others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.205f, 0.07f), new Vector3(0.24f, 0.008f, 0.008f), new Color(0.03f, 0.03f, 0.03f), null, new Vector3(0f, 25f, 22f)));
            }
            if (sk.Antennae)
                foreach (float side in new[] { -1f, 1f })
                {
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(side * 0.05f, 0.33f, 0f), new Vector3(0.012f, 0.06f, 0.012f), skin, null, new Vector3(0f, 0f, side * -18f)));
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.088f, 0.39f, 0f), Vector3.one * 0.035f, new Color(0.7f, 1f, 0.3f), a.visor));
                }
            if (sk.Neckerchief.HasValue)
            {
                others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.135f, 0.025f, 0.135f), sk.Neckerchief.Value));
                others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.0f, 0.06f), new Vector3(0.06f, 0.06f, 0.012f), sk.Neckerchief.Value, null, new Vector3(0f, 0f, 45f)));
            }

            // Headwear.
            var hc = sk.HatColor;
            switch (sk.Hat)
            {
                case HatKind.HardHat:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.25f, 0f), new Vector3(0.27f, 0.19f, 0.29f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.235f, 0.01f), new Vector3(0.32f, 0.008f, 0.35f), hc));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.33f, 0f), new Vector3(0.03f, 0.03f, 0.22f), hc * 0.9f));
                    break;
                case HatKind.Cap:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.24f, -0.005f), new Vector3(0.24f, 0.16f, 0.26f), hc));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.222f, 0.15f), new Vector3(0.18f, 0.012f, 0.12f), hc, null, new Vector3(8f, 0f, 0f)));
                    break;
                case HatKind.Propeller:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.25f, -0.005f), new Vector3(0.245f, 0.17f, 0.265f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.215f, -0.005f), new Vector3(0.25f, 0.015f, 0.27f), new Color(1f, 0.85f, 0.15f)));
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.335f, 0f), Vector3.one * 0.03f, new Color(0.9f, 0.15f, 0.15f)));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.355f, 0f), new Vector3(0.012f, 0.02f, 0.012f), new Color(0.6f, 0.6f, 0.62f)));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.376f, 0f), new Vector3(0.22f, 0.006f, 0.035f), new Color(0.9f, 0.15f, 0.15f), null, new Vector3(0f, 20f, 8f)));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.376f, 0f), new Vector3(0.035f, 0.006f, 0.22f), new Color(0.15f, 0.7f, 0.25f), null, new Vector3(0f, 20f, -8f)));
                    break;
                case HatKind.Beret:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0.02f, 0.285f, -0.01f), new Vector3(0.27f, 0.07f, 0.27f), hc, null, new Vector3(0f, 0f, 10f)));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0.03f, 0.322f, -0.005f), new Vector3(0.015f, 0.01f, 0.015f), hc));
                    break;
                case HatKind.Beanie:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.24f, -0.01f), new Vector3(0.245f, 0.2f, 0.265f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.205f, -0.01f), new Vector3(0.252f, 0.03f, 0.272f), hc * 0.85f));
                    break;
                case HatKind.StrawHat:
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.255f, 0f), new Vector3(0.5f, 0.006f, 0.5f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.29f, 0f), new Vector3(0.22f, 0.04f, 0.23f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.265f, 0f), new Vector3(0.225f, 0.012f, 0.235f), new Color(0.45f, 0.2f, 0.12f)));
                    break;
                case HatKind.FireHelmet:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.26f, -0.01f), new Vector3(0.27f, 0.2f, 0.29f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.222f, -0.035f), new Vector3(0.36f, 0.01f, 0.42f), hc, null, new Vector3(-8f, 0f, 0f)));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.305f, 0.13f), new Vector3(0.07f, 0.08f, 0.01f), new Color(0.95f, 0.8f, 0.35f), a.gold, new Vector3(-20f, 0f, 0f)));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.355f, -0.01f), new Vector3(0.02f, 0.02f, 0.24f), hc * 0.85f));
                    break;
                case HatKind.VikingHelmet:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.255f, 0f), new Vector3(0.25f, 0.2f, 0.27f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.215f, 0f), new Vector3(0.255f, 0.02f, 0.275f), hc * 0.8f));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.16f, 0.132f), new Vector3(0.02f, 0.09f, 0.012f), hc * 0.8f));
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var horn = new Color(0.92f, 0.88f, 0.75f);
                        others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(side * 0.145f, 0.31f, 0f), new Vector3(0.04f, 0.065f, 0.04f), horn, null, new Vector3(0f, 0f, -side * 50f)));
                        others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(side * 0.19f, 0.385f, 0f), new Vector3(0.026f, 0.05f, 0.026f), horn, null, new Vector3(0f, 0f, -side * 15f)));
                    }
                    break;
                case HatKind.KnightHelm:
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.165f, 0.005f), new Vector3(0.285f, 0.17f, 0.295f), hc));
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.335f, 0.005f), new Vector3(0.285f, 0.1f, 0.295f), hc));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.18f, 0.146f), new Vector3(0.17f, 0.012f, 0.01f), new Color(0.02f, 0.02f, 0.02f)));
                    for (int i = -1; i <= 1; i++)
                        others.Add(Visual(head, PrimitiveType.Cube, new Vector3(i * 0.035f, 0.105f, 0.146f), new Vector3(0.008f, 0.025f, 0.01f), new Color(0.02f, 0.02f, 0.02f)));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.14f, 0.147f), new Vector3(0.02f, 0.11f, 0.006f), hc * 1.1f));
                    break;
                case HatKind.SurgicalCap:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.24f, -0.01f), new Vector3(0.238f, 0.15f, 0.258f), hc));
                    break;
                case HatKind.HazmatHood:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.17f, 0f), new Vector3(0.3f, 0.34f, 0.31f), hc));
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.175f, 0.1f), new Vector3(0.21f, 0.15f, 0.1f), new Color(0.3f, 0.35f, 0.38f), a.visor));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.26f, 0.03f, 0.26f), hc * 0.85f));
                    foreach (float side in new[] { -1f, 1f })
                        others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(side * 0.05f, 0.075f, 0.135f), new Vector3(0.045f, 0.02f, 0.045f), new Color(0.12f, 0.12f, 0.12f), null, new Vector3(90f, 0f, 0f)));
                    break;
                case HatKind.Stahlhelm:
                    // M35 helmet: deep dome, flared skirt dropping lower at the back and sides, short visor lip, vent lugs.
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.255f, -0.008f), new Vector3(0.262f, 0.21f, 0.286f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.218f, -0.025f), new Vector3(0.298f, 0.03f, 0.318f), hc * 0.97f, null, new Vector3(-11f, 0f, 0f))); // skirt drops at the back
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.245f, 0.13f), new Vector3(0.19f, 0.01f, 0.035f), hc, null, new Vector3(10f, 0f, 0f))); // short visor lip above the brow
                    foreach (float side in new[] { -1f, 1f })
                        others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(side * 0.127f, 0.27f, 0f), Vector3.one * 0.016f, hc * 0.8f));
                    break;
                case HatKind.Kepi:
                    // Soft-topped kepi tipped forward over the eyes, leather band and short visor.
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.258f, 0.005f), new Vector3(0.218f, 0.012f, 0.228f), new Color(0.12f, 0.1f, 0.08f)));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.3f, 0.02f), new Vector3(0.205f, 0.045f, 0.215f), hc, null, new Vector3(-12f, 0f, 0f)));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.345f, 0.04f), new Vector3(0.17f, 0.012f, 0.18f), hc * 0.95f, null, new Vector3(-16f, 0f, 0f)));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.245f, 0.125f), new Vector3(0.15f, 0.01f, 0.07f), new Color(0.1f, 0.08f, 0.06f), null, new Vector3(18f, 0f, 0f)));
                    break;
                case HatKind.Toque:
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.31f, 0f), new Vector3(0.22f, 0.08f, 0.22f), hc));
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.42f, 0f), new Vector3(0.3f, 0.18f, 0.3f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.245f, 0f), new Vector3(0.235f, 0.02f, 0.25f), hc * 0.95f));
                    break;
                case HatKind.Cowboy:
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.255f, 0f), new Vector3(0.44f, 0.008f, 0.4f), hc));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.31f, 0f), new Vector3(0.2f, 0.11f, 0.24f), hc));
                    others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.27f, 0f), new Vector3(0.205f, 0.025f, 0.245f), hc * 0.4f)); // band
                    break;
                case HatKind.Tricorn:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.27f, 0f), new Vector3(0.24f, 0.13f, 0.26f), hc));
                    for (int i = 0; i < 3; i++)
                    {
                        var dir = Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward;
                        others.Add(Visual(head, PrimitiveType.Cube, new Vector3(0f, 0.285f, 0f) + dir * 0.13f, new Vector3(0.2f, 0.08f, 0.012f), hc, null, new Vector3(-12f, i * 120f, 0f)));
                    }
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.25f, 0f), new Vector3(0.3f, 0.008f, 0.3f), hc));
                    break;
                case HatKind.Bucket:
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.27f, 0f), new Vector3(0.24f, 0.05f, 0.25f), hc));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.235f, 0f), new Vector3(0.34f, 0.01f, 0.35f), hc * 0.95f));
                    break;
                case HatKind.AstroHelmet:
                    others.Add(Visual(head, PrimitiveType.Sphere, new Vector3(0f, 0.17f, 0.01f), Vector3.one * 0.36f, Color.white, a.visor));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.0f, 0f), new Vector3(0.28f, 0.035f, 0.28f), new Color(0.85f, 0.85f, 0.88f))); // neck ring
                    break;
                case HatKind.BananaHood:
                    others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(0f, 0.2f, -0.04f), new Vector3(0.27f, 0.24f, 0.24f), hc));
                    others.Add(Visual(head, PrimitiveType.Capsule, new Vector3(0f, 0.43f, -0.07f), new Vector3(0.12f, 0.1f, 0.12f), hc, null, new Vector3(-20f, 0f, 0f)));
                    others.Add(Visual(head, PrimitiveType.Cylinder, new Vector3(0f, 0.53f, -0.11f), new Vector3(0.035f, 0.035f, 0.035f), new Color(0.35f, 0.24f, 0.1f), null, new Vector3(-30f, 0f, 0f))); // stem
                    break;
            }
        }

        void Limb(Part upper, Part lower, Vector3 origin, bool isArm)
        {
            var su = Specs[(int)upper];
            var sl = Specs[(int)lower];
            var u = MakePart(upper, origin);
            var l = MakePart(lower, origin + Vector3.down * su.Length);
            Color top = isArm ? shirt : pants;
            var list = isArm ? arms : others;
            if (isArm && !mannequin)
            {
                list.Add(Visual(u, PrimitiveType.Capsule, new Vector3(0f, -su.Length * 0.25f, 0f),
                    new Vector3(su.Radius * 2.4f, su.Length * 0.32f, su.Radius * 2.4f), sk.TankTop ? skin : top, sk.TankTop ? null : shirtMat)); // short sleeve (bare for a tank top)
                if (!sk.NoArmbands)
                    list.Add(Visual(u, PrimitiveType.Cylinder, new Vector3(0f, -su.Length * 0.38f, 0f),
                        new Vector3(su.Radius * 2.5f, 0.012f, su.Radius * 2.5f), accent)); // armband in the player's color
                if (sk.WehrmachtGear && origin.x < 0f)
                {
                    // Parody armband on the left arm: red band, white roundel with a black spider.
                    float bandR = su.Radius * 1.3f;
                    list.Add(Visual(u, PrimitiveType.Cylinder, new Vector3(0f, -su.Length * 0.27f, 0f),
                        new Vector3(bandR * 2f, 0.045f, bandR * 2f), new Color(0.72f, 0.08f, 0.1f)));
                    var roundel = Visual(u, PrimitiveType.Quad, new Vector3(-(bandR + 0.003f), -su.Length * 0.27f, 0f),
                        new Vector3(0.075f, 0.075f, 1f), Color.white, a.spiderEmblem, new Vector3(0f, 90f, 0f));
                    roundel.shadowCastingMode = ShadowCastingMode.Off;
                    list.Add(roundel);
                }
                list.Add(Visual(u, PrimitiveType.Capsule, new Vector3(0f, -su.Length * 0.62f, 0f),
                    new Vector3(su.Radius * 1.85f, su.Length * 0.34f, su.Radius * 1.85f), skin));
            }
            else
                list.Add(Visual(u, PrimitiveType.Capsule, new Vector3(0f, -su.Length * 0.5f, 0f),
                    new Vector3(su.Radius * 2f, su.Length * 0.5f + su.Radius, su.Radius * 2f), top));
            bool bareShin = !isArm && sk.Shorts && !mannequin;
            if (!isArm && sk.TallBoots && !mannequin) // knee-high boot shafts over the trousers
                list.Add(Visual(l, PrimitiveType.Capsule, new Vector3(0f, -sl.Length * 0.6f, 0f),
                    new Vector3(sl.Radius * 2.3f, sl.Length * 0.42f + sl.Radius, sl.Radius * 2.3f), boots));
            list.Add(Visual(l, PrimitiveType.Capsule, new Vector3(0f, -sl.Length * 0.5f, 0f),
                new Vector3(sl.Radius * 2f, sl.Length * 0.5f + sl.Radius, sl.Radius * 2f), isArm || bareShin ? skin : pants));
            if (bareShin)
                list.Add(Visual(l, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(sl.Radius * 2.6f, 0.03f, sl.Radius * 2.6f), pants)); // shorts hem
            if (isArm)
            {
                // Hand in the character's skin tone (or costume gloves): palm, four curled fingers and a thumb.
                Color handColor = mannequin ? shirt : sk.Gloves ?? skin;
                float side = Mathf.Sign(origin.x);
                float wrist = -sl.Length;
                list.Add(Visual(l, PrimitiveType.Sphere, new Vector3(0f, wrist - 0.035f, 0f), new Vector3(0.07f, 0.085f, 0.046f), handColor));
                for (int f = 0; f < 4; f++)
                {
                    float fx = -0.022f + f * 0.0147f;
                    list.Add(Visual(l, PrimitiveType.Capsule, new Vector3(fx, wrist - 0.078f, 0.008f), new Vector3(0.015f, 0.018f, 0.015f), handColor, null, new Vector3(-35f, 0f, 0f)));
                    list.Add(Visual(l, PrimitiveType.Capsule, new Vector3(fx, wrist - 0.092f, 0.024f), new Vector3(0.014f, 0.013f, 0.014f), handColor, null, new Vector3(-100f, 0f, 0f)));
                }
                list.Add(Visual(l, PrimitiveType.Capsule, new Vector3(-side * 0.034f, wrist - 0.045f, 0.018f), new Vector3(0.018f, 0.024f, 0.018f), handColor, null, new Vector3(-40f, 0f, side * 35f)));
            }
            else
            {
                // Rounded shoe: toe cap and heel.
                float shoe = sk.BigShoes && !mannequin ? 1.7f : 1f;
                list.Add(Visual(l, PrimitiveType.Capsule, new Vector3(0f, -sl.Length - 0.025f, 0.045f * shoe), new Vector3(0.11f * shoe, 0.13f * shoe, 0.085f * Mathf.Sqrt(shoe)), boots, null, new Vector3(90f, 0f, 0f)));
                list.Add(Visual(l, PrimitiveType.Cube, new Vector3(0f, -sl.Length - 0.055f, 0.045f * shoe), new Vector3(0.105f * shoe, 0.02f, 0.25f * shoe), boots * 0.7f)); // sole
            }
        }

        Limb(Part.UpperArmL, Part.LowerArmL, ShoulderL, true);
        Limb(Part.UpperArmR, Part.LowerArmR, ShoulderR, true);
        Limb(Part.UpperLegL, Part.LowerLegL, HipL, false);
        Limb(Part.UpperLegR, Part.LowerLegR, HipR, false);

        // Relaxed arms when not holding anything.
        body.SetLimb(Part.UpperArmL, Part.LowerArmL, ShoulderL, ShoulderL + new Vector3(-0.08f, -0.55f, 0.05f), new Vector3(-1f, 0f, -1f));
        body.SetLimb(Part.UpperArmR, Part.LowerArmR, ShoulderR, ShoulderR + new Vector3(0.08f, -0.55f, 0.05f), new Vector3(1f, 0f, -1f));

        body.armRenderers = arms.ToArray();
        body.otherRenderers = others.ToArray();
        body.lastPos = root.position;
        return body;
    }

    public void SetVisible(bool visible)
    {
        foreach (var r in armRenderers) r.enabled = visible;
        foreach (var r in otherRenderers) r.enabled = visible;
    }

    /// <summary>For the local player: torso/legs only cast shadows, arms render on the view-model layer.</summary>
    public void ConfigureFirstPerson()
    {
        foreach (var r in otherRenderers)
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        // First-person arms are drawn separately (FirstPersonArms); the body's arms only cast shadows.
        foreach (var r in armRenderers)
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
    }

    // ------------------------------------------------------------------ posing

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Prone: the whole body lies face down, head over the player's position and legs trailing behind.
        transform.localRotation = Quaternion.Euler(90f * prone, 0f, 0f);
        transform.localPosition = new Vector3(0f, 0.15f, -1.6f) * prone;

        // Crouch / slide: hips drop, torso pitches (forward when crouched, leaning back when sliding).
        Vector3 drop = Vector3.down * (0.42f * crouch + 0.6f * slide);
        if (breathOffset < 0f) breathOffset = Random.Range(0f, 6f);
        float breath = Mathf.Sin(Time.time * 1.7f + breathOffset) * 0.9f * (1f - prone);
        float torsoPitch = 14f * crouch - 50f * slide + breath;
        smoothPitch = Mathf.Lerp(smoothPitch, Mathf.Clamp(aimPitch, -60f, 60f), 1f - Mathf.Exp(-18f * dt));
        Vector3 waist = WaistPos + drop;
        var roll = Quaternion.Euler(0f, 0f, -lean * 32f) * Quaternion.Euler(torsoPitch, 0f, 0f);
        Vector3 Leaned(Vector3 p) => waist + roll * (p - WaistPos);
        Get(Part.Pelvis).localPosition = PelvisPos + drop;
        Get(Part.Pelvis).localRotation = Quaternion.Euler(torsoPitch * 0.4f, 0f, 0f);
        Get(Part.Chest).localPosition = waist;
        Get(Part.Chest).localRotation = roll;
        Get(Part.Head).localPosition = Leaned(NeckPos);
        Get(Part.Head).localRotation = roll * Quaternion.Euler(smoothPitch * 0.7f - breath * 0.5f, 0f, 0f);

        if (rightHandTarget != null)
            SetLimbWorld(Part.UpperArmR, Part.LowerArmR, Leaned(ShoulderR), rightHandTarget.position, roll * new Vector3(1f, -1.2f, -0.4f));
        if (leftHandTarget != null)
            SetLimbWorld(Part.UpperArmL, Part.LowerArmL, Leaned(ShoulderL), leftHandTarget.position, roll * new Vector3(-1f, -1.2f, -0.2f));
        else if (rightHandTarget != null) // one-handed weapon: the free arm hangs relaxed
            SetLimb(Part.UpperArmL, Part.LowerArmL, Leaned(ShoulderL), Leaned(ShoulderL) + roll * new Vector3(-0.06f, -0.56f, 0.08f), roll * new Vector3(0f, 0f, 1f));

        if (!animateLegs) return;
        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;
        delta.y = 0f;
        float speed = delta.magnitude / dt;
        if (speed > 20f) speed = 0f; // teleport
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-10f * dt));
        float k = Mathf.Clamp01(smoothedSpeed / 6f) * (1f - slide) * (1f - prone * 0.6f) * (1f - airborne);
        phase += smoothedSpeed * dt * 2.4f;
        // Swing direction relative to facing: walking backward reverses the cycle.
        float forwardDot = Vector3.Dot(delta.normalized, transform.forward);
        float sign = forwardDot < -0.3f ? -1f : 1f;
        legSign = Mathf.MoveTowards(legSign, sign, dt * 5f); // reverse the stride smoothly, legs pass together
        // Base leg pose per stance: crouch = knees forward and bent; slide = lead leg out front, other tucked.
        float thigh = 70f * crouch + 82f * slide;
        float kneeL = 110f * crouch + 8f * slide, kneeR = 110f * crouch + 75f * slide;
        // In the air: one knee drawn up, the other trailing.
        float thighL = thigh + 42f * airborne, thighR = thigh + 18f * airborne;
        kneeL += 70f * airborne;
        kneeR += 95f * airborne;
        float stride = 38f * (1f - crouch * 0.5f);
        PoseLeg(Part.UpperLegL, Part.LowerLegL, HipL + drop, thighL + Mathf.Sin(phase) * stride * k * legSign, kneeL + Mathf.Max(0f, Mathf.Sin(phase + 1.6f)) * 50f * k);
        PoseLeg(Part.UpperLegR, Part.LowerLegR, HipR + drop, thighR - Mathf.Sin(phase) * stride * k * legSign, kneeR + Mathf.Max(0f, -Mathf.Sin(phase + 1.6f)) * 50f * k);
    }

    void PoseLeg(Part upper, Part lower, Vector3 hip, float swing, float bend)
    {
        var u = Get(upper);
        var l = Get(lower);
        var r = Quaternion.Euler(-swing, 0f, 0f);
        u.localPosition = hip;
        u.localRotation = r;
        l.localPosition = hip + r * Vector3.down * Specs[(int)upper].Length;
        l.localRotation = r * Quaternion.Euler(bend, 0f, 0f);
    }

    void SetLimbWorld(Part upper, Part lower, Vector3 shoulderLocal, Vector3 targetWorld, Vector3 poleLocal)
        => SetLimb(upper, lower, shoulderLocal, transform.InverseTransformPoint(targetWorld), poleLocal);

    /// <summary>Two-bone IK in body-local space.</summary>
    void SetLimb(Part upper, Part lower, Vector3 shoulder, Vector3 target, Vector3 pole)
    {
        float a = Specs[(int)upper].Length, b = Specs[(int)lower].Length + 0.04f;
        Vector3 d = target - shoulder;
        float dist = Mathf.Clamp(d.magnitude, 0.05f, a + b - 0.001f);
        Vector3 dir = d.normalized;
        float x = (a * a - b * b + dist * dist) / (2f * dist);
        float h = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
        Vector3 poleDir = Vector3.ProjectOnPlane(pole, dir).normalized;
        Vector3 elbow = shoulder + dir * x + poleDir * h;
        Vector3 hand = shoulder + dir * dist;

        var u = Get(upper);
        var l = Get(lower);
        u.localPosition = shoulder;
        u.localRotation = Quaternion.FromToRotation(Vector3.down, elbow - shoulder);
        l.localPosition = elbow;
        l.localRotation = Quaternion.FromToRotation(Vector3.down, hand - elbow);
    }

    // ------------------------------------------------------------------ hit classification

    /// <summary>Stance-aware hit location: prone bodies lie along the forward axis, crouched ones are shorter.</summary>
    public static Part ClassifyHit(Transform playerRoot, Vector3 worldPoint, Stance stance, float shapeHeight)
    {
        if (stance == Stance.Prone)
        {
            Vector3 q = playerRoot.InverseTransformPoint(worldPoint);
            bool l = q.x < 0f;
            if (q.z > -0.2f) return Mathf.Abs(q.x) > 0.25f ? (l ? Part.LowerArmL : Part.LowerArmR) : Part.Head;
            if (q.z > -0.75f) return Mathf.Abs(q.x) > 0.25f ? (l ? Part.UpperArmL : Part.UpperArmR) : Part.Chest;
            if (q.z > -1.0f) return Part.Pelvis;
            return q.z > -1.4f ? (l ? Part.UpperLegL : Part.UpperLegR) : (l ? Part.LowerLegL : Part.LowerLegR);
        }
        // Stretch a shortened (crouch/slide) body back to standing proportions.
        Vector3 local = playerRoot.InverseTransformPoint(worldPoint);
        local.y *= 2f / Mathf.Max(0.8f, shapeHeight);
        return ClassifyHit(playerRoot, playerRoot.TransformPoint(local));
    }

    /// <summary>Which body part a hit at this world point most likely struck (player root = feet).</summary>
    public static Part ClassifyHit(Transform playerRoot, Vector3 worldPoint)
    {
        Vector3 p = playerRoot.InverseTransformPoint(worldPoint);
        bool left = p.x < 0f;
        float ax = Mathf.Abs(p.x);
        if (p.y > 1.55f) return Part.Head;
        if (p.y > 1.05f)
        {
            if (ax > 0.21f) return p.y > 1.27f ? (left ? Part.UpperArmL : Part.UpperArmR) : (left ? Part.LowerArmL : Part.LowerArmR);
            return Part.Chest;
        }
        if (p.y > 0.86f)
            return ax > 0.22f ? (left ? Part.LowerArmL : Part.LowerArmR) : Part.Pelvis;
        if (p.y > 0.46f) return left ? Part.UpperLegL : Part.UpperLegR;
        return left ? Part.LowerLegL : Part.LowerLegR;
    }

    public static float DamageMultiplier(Part part, WeaponDef def) => part switch
    {
        Part.Head => def.HeadMultiplier,
        Part.UpperArmL or Part.UpperArmR or Part.LowerArmL or Part.LowerArmR => 0.85f,
        Part.UpperLegL or Part.UpperLegR or Part.LowerLegL or Part.LowerLegR => 0.8f,
        _ => 1f,
    };

    public static bool IsLimb(Part p) => p >= Part.UpperArmL;
}
