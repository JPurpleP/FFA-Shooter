using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// View-model arms for the local player: forearms and jointed hands in the character's own skin tone (or their
/// costume gloves), gripping the weapon's grip and support points, elbows trailing off below the screen edge.
/// </summary>
public class FirstPersonArms : MonoBehaviour
{
    class Arm
    {
        public Transform forearm, sleeve, hand;
        public Transform target;
        public Vector3 elbowOffset; // from the hand, in head space
        public float side;
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
        arms.right = arms.MakeArm("Right", sleeve, skinTone, hand, skin.Gloves.HasValue, 1f, new Vector3(0.13f, -0.3f, -0.1f));
        arms.left = arms.MakeArm("Left", sleeve, skinTone, hand, skin.Gloves.HasValue, -1f, new Vector3(-0.17f, -0.3f, -0.06f));
        GameAssets.SetLayerRecursive(go, GameAssets.ViewModelLayer);
        return arms;
    }

    public void SetTargets(Transform rightHand, Transform leftHand)
    {
        right.target = rightHand;
        left.target = leftHand;
    }

    Arm MakeArm(string name, Color shirt, Color skin, Color handColor, bool gloved, float side, Vector3 elbowOffset)
    {
        var a = GameAssets.I;
        var arm = new Arm { side = side, elbowOffset = elbowOffset };
        var root = new GameObject(name + "Arm").transform;
        root.SetParent(transform, false);

        // Bare forearms (short-sleeved tee); the sleeve only shows if the elbow comes into view.
        arm.forearm = Part(root, PrimitiveType.Capsule, "Forearm", a.playerBody, skin);
        arm.sleeve = Part(root, PrimitiveType.Capsule, "Sleeve", a.playerBody, shirt);

        // Hand wrapped around a grip: rounded palm, knuckles, three-segment fingers curling around the front of the
        // grip, and a two-segment thumb over the top. Hand space: +z toward the muzzle, the grip runs through the origin.
        arm.hand = new GameObject("Hand").transform;
        arm.hand.SetParent(root, false);
        var palm = Part(arm.hand, PrimitiveType.Sphere, "Palm", a.playerBody, handColor);
        palm.localPosition = new Vector3(side * 0.017f, -0.01f, -0.008f);
        palm.localScale = new Vector3(0.034f, 0.092f, 0.074f);
        var heel = Part(arm.hand, PrimitiveType.Sphere, "PalmHeel", a.playerBody, handColor);
        heel.localPosition = new Vector3(side * 0.008f, -0.036f, -0.026f);
        heel.localScale = new Vector3(0.036f, 0.04f, 0.04f);
        Color knuckleColor = handColor * 0.93f;
        for (int i = 0; i < 4; i++)
        {
            float y = 0.024f - i * 0.0185f;
            float r = i == 3 ? 0.0068f : 0.0079f;              // pinky is thinner
            float reach = i == 0 || i == 3 ? 0.9f : 1f;         // index and pinky a touch shorter
            var p0 = new Vector3(side * 0.02f, y, 0.02f);
            var p1 = new Vector3(side * 0.004f, y, 0.035f * reach + 0.002f);
            var p2 = new Vector3(-side * 0.012f, y - 0.001f, 0.027f * reach);
            var p3 = new Vector3(-side * 0.016f, y - 0.002f, 0.013f);
            var knuckle = Part(arm.hand, PrimitiveType.Sphere, "Knuckle", a.playerBody, knuckleColor);
            knuckle.localPosition = p0;
            knuckle.localScale = Vector3.one * r * 2.3f;
            PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "FingerBase", a.playerBody, handColor), p0, p1, r);
            PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "FingerMid", a.playerBody, handColor), p1, p2, r * 0.95f);
            PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "FingerTip", a.playerBody, handColor), p2, p3, r * 0.88f);
        }
        var t0 = new Vector3(side * 0.008f, 0.022f, -0.022f);
        var t1 = new Vector3(-side * 0.004f, 0.034f, -0.002f);
        var t2 = new Vector3(-side * 0.013f, 0.037f, 0.016f);
        PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "ThumbBase", a.playerBody, handColor), t0, t1, 0.0098f);
        PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "ThumbTip", a.playerBody, handColor), t1, t2, 0.0085f);
        if (gloved)
            PlaceCapsule(Part(arm.hand, PrimitiveType.Capsule, "Cuff", a.playerBody, handColor * 0.9f),
                new Vector3(side * 0.012f, -0.012f, -0.05f), new Vector3(side * 0.012f, -0.012f, -0.075f), 0.03f);
        return arm;
    }

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

    static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
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
        arm.sleeve.gameObject.SetActive(show);
        arm.hand.gameObject.SetActive(show);
        if (!show) return;

        // Work in head space so the arm moves with aim, lean and weapon sway.
        Vector3 hand = head.InverseTransformPoint(arm.target.position);
        Quaternion handRot = Quaternion.Inverse(head.rotation) * arm.target.rotation;
        Vector3 wrist = hand + handRot * new Vector3(arm.side * 0.012f, -0.01f, -0.06f);
        Vector3 elbow = wrist + arm.elbowOffset;

        arm.hand.localPosition = hand;
        arm.hand.localRotation = handRot;
        PlaceCapsule(arm.forearm, wrist, elbow + (elbow - wrist) * 0.3f, 0.029f);
        PlaceCapsule(arm.sleeve, elbow + (elbow - wrist) * 0.45f, elbow + (elbow - wrist) * 1.1f, 0.045f);
    }

    static void PlaceCapsule(Transform t, Vector3 a, Vector3 b, float radius)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        t.localPosition = (a + b) * 0.5f;
        t.localRotation = Quaternion.FromToRotation(Vector3.up, d);
        t.localScale = new Vector3(radius * 2f, len * 0.5f + radius, radius * 2f);
    }
}
