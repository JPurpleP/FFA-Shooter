using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// View-model arms for the local player: forearms and jointed hands in the character's own skin tone (or their
/// costume gloves), closed round each weapon's grip and fore-end (sized by <see cref="HandFit"/> so nothing sinks into
/// the gun), wrists tapering into the forearms, elbows trailing off below the screen edge.
/// </summary>
public class FirstPersonArms : MonoBehaviour
{
    class Arm
    {
        public Transform root, forearm, forearmFront, sleeve, hand;
        public Transform target;
        public GripFit fit;
        public bool built;
        public Vector3 elbowOffset; // from the wrist, in head space
        public float side;
        public Color handColor, skin;
        public bool gloved;
    }

    Arm right, left;
    Transform head;

    public static FirstPersonArms Build(Transform head, SkinDef skin)
    {
        var go = new GameObject("FirstPersonArms");
        go.transform.SetParent(head, false);
        var arms = go.AddComponent<FirstPersonArms>();
        arms.head = head;
        Color skinTone = skin.SkinTone.a > 0f ? skin.SkinTone : new Color(0.86f, 0.66f, 0.52f);
        Color hand = skin.Gloves ?? skinTone;
        Color sleeve = skin.TankTop ? skinTone : skin.SleeveColor;
        arms.right = arms.MakeArm("Right", sleeve, skinTone, hand, skin.Gloves.HasValue, 1f, new Vector3(0.12f, -0.29f, -0.06f));
        arms.left = arms.MakeArm("Left", sleeve, skinTone, hand, skin.Gloves.HasValue, -1f, new Vector3(-0.15f, -0.29f, -0.02f));
        GameAssets.SetLayerRecursive(go, GameAssets.ViewModelLayer);
        return arms;
    }

    public void SetTargets(Transform rightHand, Transform leftHand) => SetTargets(rightHand, leftHand, GripFit.Pistol, GripFit.Pistol);

    public void SetTargets(Transform rightHand, Transform leftHand, GripFit rightFit, GripFit leftFit)
    {
        right.target = rightHand;
        left.target = leftHand;
        Refit(right, rightFit);
        Refit(left, leftFit);
    }

    void Refit(Arm arm, GripFit fit)
    {
        if (arm.built && arm.fit.Same(fit)) return;
        arm.fit = fit;
        arm.built = true;
        for (int i = arm.hand.childCount - 1; i >= 0; i--) Destroy(arm.hand.GetChild(i).gameObject);
        BuildHand(arm);
        GameAssets.SetLayerRecursive(arm.hand.gameObject, GameAssets.ViewModelLayer);
    }

    Arm MakeArm(string name, Color shirt, Color skin, Color handColor, bool gloved, float side, Vector3 elbowOffset)
    {
        var a = GameAssets.I;
        var arm = new Arm { side = side, elbowOffset = elbowOffset, handColor = handColor, skin = skin, gloved = gloved };
        arm.root = new GameObject(name + "Arm").transform;
        arm.root.SetParent(transform, false);
        // Bare forearms (short-sleeved tee): slimmer at the wrist, fuller toward the elbow; the sleeve only shows if
        // the elbow comes into view.
        arm.forearmFront = Part(arm.root, PrimitiveType.Capsule, "ForearmWrist", a.playerBody, gloved ? handColor * 0.9f : skin);
        arm.forearm = Part(arm.root, PrimitiveType.Capsule, "Forearm", a.playerBody, skin);
        arm.sleeve = Part(arm.root, PrimitiveType.Capsule, "Sleeve", a.playerBody, shirt);
        arm.hand = new GameObject("Hand").transform;
        arm.hand.SetParent(arm.root, false);
        return arm;
    }

    /// <summary>
    /// The hand closed round something W across (palm to fingers) and Hd deep (the way the fingers curl): rounded palm
    /// and heel, knuckles, three-segment fingers following the surface round the front, and a two-segment thumb.
    /// </summary>
    void BuildHand(Arm arm)
    {
        var a = GameAssets.I;
        float s = arm.side, w = arm.fit.W, hd = arm.fit.Hd;
        Color c = arm.handColor, knuckleColor = arm.handColor * 0.93f;
        const float fr = 0.0079f; // finger radius

        // Palm and heel just outside the grip on the palm side.
        var palm = Part(arm.hand, PrimitiveType.Sphere, "Palm", a.playerBody, c);
        palm.localPosition = new Vector3(s * (w + 0.015f), -0.012f, -0.004f);
        palm.localScale = new Vector3(0.03f, 0.09f, Mathf.Max(0.07f, hd * 2f + 0.02f));
        var heel = Part(arm.hand, PrimitiveType.Sphere, "PalmHeel", a.playerBody, c);
        heel.localPosition = new Vector3(s * (w + 0.013f), -0.038f, -hd * 0.55f - 0.006f);
        heel.localScale = new Vector3(0.034f, 0.04f, 0.042f);

        // Fingers: knuckle at the palm-side front corner, round the front, tips on the far side. Held from underneath
        // (Bar), the tips stop on the upper side rather than wrapping over the top.
        float tipWrap = arm.fit.Bar ? 0.55f : 1f;
        for (int i = 0; i < 4; i++)
        {
            float y = 0.024f - i * 0.0185f;
            float r = i == 3 ? 0.0068f : fr;
            float reach = i == 0 || i == 3 ? 0.92f : 1f;
            var p0 = new Vector3(s * (w + 0.009f), y, hd * 0.4f);
            var p1 = new Vector3(s * w * 0.55f, y, hd + r + 0.001f);
            var p2 = new Vector3(-s * w * 0.55f * reach, y - 0.001f, hd + r + 0.001f);
            var p3 = new Vector3(-s * (w + r + 0.001f) * tipWrap, y - 0.002f, Mathf.Lerp(hd + r, hd * 0.25f, tipWrap) * reach);
            var knuckle = Part(arm.hand, PrimitiveType.Sphere, "Knuckle", a.playerBody, knuckleColor);
            knuckle.localPosition = p0 + new Vector3(s * 0.002f, 0f, -0.003f);
            knuckle.localScale = Vector3.one * r * 2.3f;
            PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "FingerBase", a.playerBody, c), p0, p1, r);
            PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "FingerMid", a.playerBody, c), p1, p2, r * 0.95f);
            PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "FingerTip", a.playerBody, c), p2, p3, r * 0.88f);
        }

        // Thumb: on a pistol grip it comes round the back to the far side above the fingers; on a bar it lies along
        // the near side pointing forward.
        Vector3 t0, t1, t2;
        if (!arm.fit.Bar)
        {
            t0 = new Vector3(s * (w + 0.008f), 0.016f, -hd - 0.004f);
            t1 = new Vector3(-s * w * 0.2f, 0.028f, -hd - 0.011f);
            t2 = new Vector3(-s * (w + 0.011f), 0.034f, -hd * 0.1f);
        }
        else
        {
            t0 = new Vector3(s * (w + 0.006f), -0.016f, -hd - 0.004f);
            t1 = new Vector3(s * w * 0.2f, 0.004f, -hd - 0.009f);
            t2 = new Vector3(-s * w * 0.35f, 0.022f, -hd - 0.008f);
        }
        PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "ThumbBase", a.playerBody, c), t0, t1, 0.0098f);
        PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "ThumbTip", a.playerBody, c), t1, t2, 0.0085f);

        // Wrist: a short tapered piece from the heel of the palm back to where the forearm starts.
        PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "Wrist", a.playerBody, c),
            heel.localPosition + new Vector3(s * 0.002f, 0.004f, 0.004f), WristPoint(arm), 0.0215f);
        if (arm.gloved)
        {
            Vector3 wp = WristPoint(arm);
            PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "Cuff", a.playerBody, c * 0.85f), wp + new Vector3(0f, 0f, 0.012f), wp - new Vector3(0f, 0.004f, 0.02f), 0.028f);
        }
    }

    /// <summary>Where the wrist meets the forearm, in hand space: behind and below the palm.</summary>
    static Vector3 WristPoint(Arm arm) => new(arm.side * (arm.fit.W + 0.013f), -0.05f, -arm.fit.Hd - 0.045f);

    static Transform Part(Transform parent, PrimitiveType type, string name, Material mat, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        r.SetPropertyBlock(block);
        return go.transform;
    }

    void LateUpdate()
    {
        Pose(right);
        Pose(left);
    }

    void Pose(Arm arm)
    {
        bool show = arm.target != null && arm.target.gameObject.activeInHierarchy;
        arm.forearm.gameObject.SetActive(show);
        arm.forearmFront.gameObject.SetActive(show);
        arm.sleeve.gameObject.SetActive(show);
        arm.hand.gameObject.SetActive(show);
        if (!show) return;

        // Work in head space so the arm moves with aim, lean and weapon sway.
        Vector3 hand = head.InverseTransformPoint(arm.target.position);
        Quaternion handRot = Quaternion.Inverse(head.rotation) * arm.target.rotation;
        arm.hand.localPosition = hand;
        arm.hand.localRotation = handRot;
        Vector3 wrist = hand + handRot * WristPoint(arm);
        Vector3 elbow = wrist + arm.elbowOffset;
        Vector3 dir = (elbow - wrist).normalized;
        // Forearm: slim just behind the wrist, fuller toward the elbow.
        Vector3 mid = wrist + (elbow - wrist) * 0.35f;
        PlaceCapsule(arm.forearmFront, wrist - dir * 0.004f, mid, 0.025f);
        PlaceCapsule(arm.forearm, mid - dir * 0.01f, elbow + (elbow - wrist) * 0.3f, 0.031f);
        PlaceCapsule(arm.sleeve, elbow + (elbow - wrist) * 0.45f, elbow + (elbow - wrist) * 1.1f, 0.045f);
    }

    static void PlaceCapsule(Transform t, Vector3 a, Vector3 b, float radius)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        t.localPosition = (a + b) * 0.5f;
        t.localRotation = Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 1e-8f ? d : Vector3.up);
        t.localScale = new Vector3(radius * 2f, len * 0.5f + radius, radius * 2f);
    }
}
