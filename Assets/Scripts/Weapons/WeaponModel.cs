using UnityEngine;

/// <summary>
/// Builds detailed-ish weapon models out of primitives, roughly to real-world scale (meters).
/// Weapon space: +Z toward the muzzle, +Y up, origin at the top of the pistol grip (where the firing hand sits).
/// </summary>
public class WeaponModel : MonoBehaviour
{
    public WeaponId id;
    public Transform muzzle;
    public Transform ejectionPort;
    public Transform slide;  // Glock slide, M4 bolt carrier etc. – kicks back on fire
    public Transform pump;   // Remington forend
    public Transform warhead; // RPG rocket sitting in the tube (hidden when empty)
    public Transform cylinder; // M32 / revolver cylinder: indexes one chamber per shot
    public Transform lever;    // lever action: swings down and back after each shot
    public Transform breakAction; // break-action barrel group, hinged at its pivot (tips open while reloading)
    public int chambers = 6;
    Vector3 slideRest, pumpRest;
    Quaternion leverRest;
    float slideT = 1f, pumpT = 1f, pumpDelay, leverT = 1f, leverDelay;

    public static WeaponModel Build(WeaponId id, Transform parent)
    {
        var root = new GameObject(id.ToString());
        root.transform.SetParent(parent, false);
        var m = root.AddComponent<WeaponModel>();
        m.id = id;
        var b = new Builder(root.transform);
        switch (id)
        {
            case WeaponId.Glock17: BuildGlock(m, b); break;
            case WeaponId.MP5: BuildMP5(m, b); break;
            case WeaponId.M4A1: BuildM4(m, b); break;
            case WeaponId.Remington870: BuildRemington(m, b); break;
            case WeaponId.BarrettM82: BuildBarrett(m, b); break;
            case WeaponId.GoldenDeagle: BuildDeagle(m, b); break;
            case WeaponId.M32: BuildM32(m, b); break;
            case WeaponId.RPG7: BuildRPG(m, b); break;
            case WeaponId.FragGrenade: BuildFrag(m, b); break;
            case WeaponId.SmokeGrenade: BuildSmoke(m, b); break;
            case WeaponId.RopeGrenade: BuildRopeGrenade(m, b); break;
            case WeaponId.Molotov: BuildMolotov(m, b); break;
            case WeaponId.AA12: BuildAA12(m, b); break;
            case WeaponId.Mosin: BuildMosin(m, b); break;
            case WeaponId.PPSh41: BuildPPSh(m, b); break;
            case WeaponId.M249: BuildM249(m, b); break;
            case WeaponId.Cattleman: BuildCattleman(m, b); break;
            case WeaponId.Magnum44: BuildMagnum(m, b); break;
            case WeaponId.LeverAction: BuildLeverAction(m, b); break;
            case WeaponId.M1911: BuildM1911(m, b); break;
            case WeaponId.GlockSwitch: BuildGlockSwitch(m, b); break;
            case WeaponId.GrappleHook: BuildGrappleHook(m, b); break;
            case WeaponId.KrissVector: BuildVector(m, b); break;
            case WeaponId.MP18: BuildMP18(m, b); break;
        }
        if (m.slide != null) m.slideRest = m.slide.localPosition;
        if (m.pump != null) m.pumpRest = m.pump.localPosition;
        if (m.lever != null) m.leverRest = m.lever.localRotation;
        // Canted grip (sideways Glock): roll the whole gun about its own sight line, so the sight point stays put and
        // still lands exactly on the screen centre when aiming.
        var def = WeaponDefs.Get(id);
        if (def != null && def.CantDegrees != 0f)
        {
            var roll = Quaternion.Euler(0f, 0f, def.CantDegrees);
            var line = new Vector3(0f, def.SightHeight, 0f);
            root.transform.localRotation = roll;
            root.transform.localPosition = line - roll * line;
        }
        return m;
    }

    /// <summary>Animate the action after a shot.</summary>
    public void OnFired()
    {
        if (cylinder != null) cylinder.Rotate(0f, 0f, 360f / chambers, Space.Self); // index to the next chamber
        if (lever != null) { leverT = 0f; leverDelay = 0.12f; }
        slideT = 0f;
        if (pump != null)
        {
            pumpT = 0f;
            pumpDelay = 0.18f;
        }
    }

    void Update()
    {
        if (lever != null && leverT < 1f)
        {
            if (leverDelay > 0f) leverDelay -= Time.deltaTime;
            else
            {
                leverT = Mathf.Min(1f, leverT + Time.deltaTime * 2.6f);
                lever.localRotation = leverRest * Quaternion.Euler(-55f * Mathf.Sin(leverT * Mathf.PI), 0f, 0f);
            }
        }
        if (slide != null && slideT < 1f)
        {
            slideT = Mathf.Min(1f, slideT + Time.deltaTime * 14f);
            float back = Mathf.Sin(slideT * Mathf.PI) * (id == WeaponId.Glock17 ? 0.03f : 0.02f);
            slide.localPosition = slideRest - Vector3.forward * back;
        }
        if (pump != null && pumpT < 1f)
        {
            if (pumpDelay > 0f) { pumpDelay -= Time.deltaTime; return; }
            pumpT = Mathf.Min(1f, pumpT + Time.deltaTime * 2.8f);
            float back = Mathf.Sin(pumpT * Mathf.PI) * (id == WeaponId.Mosin ? 0.075f : 0.085f);
            pump.localPosition = pumpRest - Vector3.forward * back;
        }
    }

    // ------------------------------------------------------------------ Glock 17 (186mm long, Gen 3)

    static void BuildGlock(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        // Slide (matte black steel) with rear serrations, ejection port and sights.
        var slide = b.Group("Slide", Vector3.zero);
        m.slide = slide;
        b.Box(slide, "SlideBody", new Vector3(0f, 0.034f, 0.036f), new Vector3(0.0255f, 0.029f, 0.186f), a.gunMetal);
        b.Box(slide, "SlideTopBevel", new Vector3(0f, 0.0485f, 0.036f), new Vector3(0.019f, 0.004f, 0.184f), a.gunMetal);
        b.Box(slide, "SlideNose", new Vector3(0f, 0.031f, 0.128f), new Vector3(0.023f, 0.024f, 0.006f), a.gunMetal);
        for (int i = 0; i < 7; i++)
        {
            float z = -0.052f + i * 0.0045f;
            b.Box(slide, "Serration", new Vector3(0f, 0.034f, z), new Vector3(0.0262f, 0.022f, 0.0018f), a.gunDark);
        }
        b.Box(slide, "EjectionPort", new Vector3(0.006f, 0.046f, 0.045f), new Vector3(0.013f, 0.006f, 0.032f), a.gunDark);
        b.Box(slide, "BarrelHood", new Vector3(0.0f, 0.0455f, 0.045f), new Vector3(0.011f, 0.005f, 0.03f), a.gunPolymer);
        b.Box(slide, "Extractor", new Vector3(0.0131f, 0.04f, 0.035f), new Vector3(0.001f, 0.004f, 0.012f), a.gunDark);
        b.Box(slide, "RearSightL", new Vector3(-0.0065f, 0.0575f, -0.048f), new Vector3(0.006f, 0.013f, 0.008f), a.gunDark);
        b.Box(slide, "RearSightR", new Vector3(0.0065f, 0.0575f, -0.048f), new Vector3(0.006f, 0.013f, 0.008f), a.gunDark);
        b.Box(slide, "RearSightBase", new Vector3(0f, 0.0508f, -0.048f), new Vector3(0.019f, 0.002f, 0.008f), a.gunDark);
        b.Box(slide, "FrontSight", new Vector3(0f, 0.0565f, 0.121f), new Vector3(0.0035f, 0.015f, 0.006f), a.gunDark);
        b.Box(slide, "FrontDot", new Vector3(0f, 0.0615f, 0.1182f), new Vector3(0.0025f, 0.0025f, 0.001f), a.sightWhite);
        b.Box(slide, "RearDotL", new Vector3(-0.0065f, 0.0615f, -0.0441f), new Vector3(0.0022f, 0.0022f, 0.001f), a.sightWhite);
        b.Box(slide, "RearDotR", new Vector3(0.0065f, 0.0615f, -0.0441f), new Vector3(0.0022f, 0.0022f, 0.001f), a.sightWhite);
        b.Cyl(slide, "Bore", new Vector3(0f, 0.032f, 0.1295f), 0.0047f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.012f, 0.046f, 0.045f));
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.032f, 0.135f));

        // Polymer frame with accessory rail and the classic squared trigger guard.
        b.Box(null, "Frame", new Vector3(0f, 0.012f, 0.03f), new Vector3(0.0275f, 0.016f, 0.158f), a.gunPolymer);
        b.Box(null, "DustCover", new Vector3(0f, 0.004f, 0.083f), new Vector3(0.0265f, 0.014f, 0.052f), a.gunPolymer);
        for (int i = 0; i < 2; i++)
            b.Box(null, "RailSlot", new Vector3(0f, -0.0035f, 0.07f + i * 0.018f), new Vector3(0.027f, 0.002f, 0.004f), a.gunDark);
        b.Box(null, "GuardFront", new Vector3(0f, -0.012f, 0.058f), new Vector3(0.009f, 0.03f, 0.006f), a.gunPolymer);
        b.Box(null, "GuardBottom", new Vector3(0f, -0.026f, 0.033f), new Vector3(0.009f, 0.005f, 0.054f), a.gunPolymer);
        b.Box(null, "Trigger", new Vector3(0f, -0.007f, 0.03f), new Vector3(0.006f, 0.02f, 0.005f), a.gunPolymer, new Vector3(-8f, 0f, 0f));
        b.Box(null, "TriggerSafety", new Vector3(0f, -0.006f, 0.0322f), new Vector3(0.002f, 0.012f, 0.002f), a.gunDark);
        b.Box(null, "SlideStop", new Vector3(-0.0142f, 0.02f, 0.02f), new Vector3(0.002f, 0.004f, 0.016f), a.gunDark);
        b.Box(null, "TakedownLever", new Vector3(0.0142f, 0.018f, 0.045f), new Vector3(0.002f, 0.003f, 0.006f), a.gunDark);

        // Grip, raked ~18°, with finger grooves, beavertail and magazine floorplate.
        var grip = b.Group("Grip", new Vector3(0f, 0.004f, -0.018f), new Vector3(18f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.052f, 0f), new Vector3(0.0295f, 0.104f, 0.046f), a.gunPolymer);
        b.Box(grip, "Backstrap", new Vector3(0f, -0.045f, -0.024f), new Vector3(0.026f, 0.09f, 0.004f), a.gunPolymer);
        b.Box(grip, "Beavertail", new Vector3(0f, 0.0f, -0.03f), new Vector3(0.026f, 0.01f, 0.014f), a.gunPolymer);
        for (int i = 0; i < 3; i++)
            b.Box(grip, "FingerGroove", new Vector3(0f, -0.03f - i * 0.022f, 0.0235f), new Vector3(0.028f, 0.006f, 0.004f), a.gunPolymer);
        for (int i = 0; i < 6; i++)
            b.Box(grip, "Texture", new Vector3(0.015f, -0.02f - i * 0.013f, 0f), new Vector3(0.0012f, 0.008f, 0.036f), a.gunDark);
        b.Box(grip, "MagFloorplate", new Vector3(0f, -0.107f, 0.002f), new Vector3(0.03f, 0.008f, 0.048f), a.gunPolymer);
        b.Box(grip, "MagRelease", new Vector3(0.0155f, -0.006f, 0.02f), new Vector3(0.002f, 0.009f, 0.008f), a.gunDark);
    }

    // ------------------------------------------------------------------ MP5A2 (fixed stock, ~680mm)

    static void BuildMP5(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        // Stamped round receiver with the cocking tube running forward over the barrel.
        b.Cyl(null, "Receiver", new Vector3(0f, 0.04f, 0.075f), 0.026f, 0.31f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "ReceiverTop", new Vector3(0f, 0.062f, 0.075f), new Vector3(0.018f, 0.008f, 0.3f), a.gunMetal);
        b.Box(null, "ClawMountRidge", new Vector3(0f, 0.068f, 0.05f), new Vector3(0.012f, 0.004f, 0.12f), a.gunDark);
        b.Box(null, "EjectionPort", new Vector3(0.024f, 0.048f, 0.11f), new Vector3(0.006f, 0.016f, 0.045f), a.gunDark);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.03f, 0.05f, 0.11f));
        b.Cyl(null, "CockingTube", new Vector3(0f, 0.058f, 0.29f), 0.0105f, 0.17f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "CockingHandle", new Vector3(-0.022f, 0.058f, 0.31f), new Vector3(0.03f, 0.008f, 0.01f), a.gunDark, new Vector3(0f, -25f, 0f));
        b.Box(null, "CockingSlot", new Vector3(-0.011f, 0.058f, 0.29f), new Vector3(0.002f, 0.004f, 0.11f), a.gunDark);

        // Slim polymer handguard and barrel with tri-lug and the hooded front sight.
        b.Box(null, "Handguard", new Vector3(0f, 0.022f, 0.285f), new Vector3(0.046f, 0.046f, 0.16f), a.gunPolymer);
        for (int i = 0; i < 5; i++)
            b.Box(null, "HandguardRib", new Vector3(0f, 0.0f, 0.225f + i * 0.03f), new Vector3(0.047f, 0.004f, 0.012f), a.gunDark);
        b.Box(null, "HandguardFlare", new Vector3(0f, 0.02f, 0.37f), new Vector3(0.05f, 0.05f, 0.01f), a.gunPolymer);
        b.Cyl(null, "Barrel", new Vector3(0f, 0.04f, 0.41f), 0.0095f, 0.07f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "TriLug", new Vector3(0f, 0.04f, 0.44f), 0.0125f, 0.028f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Bore", new Vector3(0f, 0.04f, 0.4545f), 0.005f, 0.002f, a.gunDark, Builder.Axis.Z);
        b.Box(null, "FrontSightBase", new Vector3(0f, 0.064f, 0.375f), new Vector3(0.026f, 0.014f, 0.022f), a.gunMetal);
        b.Tube(null, "FrontSightHood", new Vector3(0f, 0.086f, 0.375f), 0.012f, 0.0095f, 0.02f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "FrontPost", new Vector3(0f, 0.08f, 0.375f), new Vector3(0.0025f, 0.012f, 0.004f), a.gunMetal);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.04f, 0.46f));

        // Rear diopter drum sight.
        b.Box(null, "RearSightBase", new Vector3(0f, 0.07f, -0.055f), new Vector3(0.022f, 0.01f, 0.03f), a.gunMetal);
        b.Box(null, "RearSightPost", new Vector3(0f, 0.0755f, -0.055f), new Vector3(0.008f, 0.006f, 0.01f), a.gunMetal);
        b.Tube(null, "RearGhostRing", new Vector3(0f, 0.086f, -0.055f), 0.0125f, 0.0085f, 0.008f, a.gunMetal, Builder.Axis.Z);

        // Trigger housing, pistol grip and the curved 30-round magazine.
        b.Box(null, "TriggerHousing", new Vector3(0f, 0.004f, 0.005f), new Vector3(0.032f, 0.026f, 0.13f), a.gunPolymer);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.03f, 0.03f), new Vector3(0.012f, 0.005f, 0.06f), a.gunPolymer);
        b.Box(null, "GuardFront", new Vector3(0f, -0.016f, 0.058f), new Vector3(0.012f, 0.026f, 0.005f), a.gunPolymer);
        b.Box(null, "Trigger", new Vector3(0f, -0.012f, 0.032f), new Vector3(0.005f, 0.018f, 0.005f), a.gunMetal);
        b.Box(null, "SelectorL", new Vector3(-0.0165f, 0.004f, -0.02f), new Vector3(0.002f, 0.01f, 0.018f), a.gunDark);
        var grip = b.Group("Grip", new Vector3(0f, -0.01f, -0.02f), new Vector3(14f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.045f, 0f), new Vector3(0.032f, 0.095f, 0.042f), a.gunPolymer);
        for (int i = 0; i < 4; i++)
            b.Box(grip, "GripRib", new Vector3(0f, -0.02f - i * 0.02f, 0.0215f), new Vector3(0.03f, 0.008f, 0.003f), a.gunDark);
        b.Box(null, "MagWell", new Vector3(0f, 0.005f, 0.13f), new Vector3(0.028f, 0.03f, 0.04f), a.gunMetal);
        var mag = b.Group("Magazine", new Vector3(0f, -0.01f, 0.13f));
        b.Box(mag, "Mag1", new Vector3(0f, -0.03f, 0.002f), new Vector3(0.022f, 0.06f, 0.034f), a.gunMetal, new Vector3(-6f, 0f, 0f));
        b.Box(mag, "Mag2", new Vector3(0f, -0.085f, 0.012f), new Vector3(0.022f, 0.055f, 0.033f), a.gunMetal, new Vector3(-14f, 0f, 0f));
        b.Box(mag, "Mag3", new Vector3(0f, -0.135f, 0.03f), new Vector3(0.022f, 0.05f, 0.032f), a.gunMetal, new Vector3(-24f, 0f, 0f));
        b.Box(mag, "MagBase", new Vector3(0f, -0.158f, 0.045f), new Vector3(0.025f, 0.006f, 0.036f), a.gunDark, new Vector3(-28f, 0f, 0f));
        b.Box(null, "MagRelease", new Vector3(0f, -0.012f, 0.105f), new Vector3(0.03f, 0.01f, 0.008f), a.gunDark);

        // A2 fixed polymer stock.
        b.Box(null, "StockTop", new Vector3(0f, 0.038f, -0.2f), new Vector3(0.034f, 0.036f, 0.25f), a.gunPolymer);
        b.Box(null, "StockLower", new Vector3(0f, -0.018f, -0.27f), new Vector3(0.03f, 0.06f, 0.13f), a.gunPolymer, new Vector3(-12f, 0f, 0f));
        b.Box(null, "StockWeb", new Vector3(0f, 0.008f, -0.2f), new Vector3(0.02f, 0.03f, 0.12f), a.gunPolymer);
        b.Box(null, "ButtPlate", new Vector3(0f, 0.008f, -0.335f), new Vector3(0.042f, 0.125f, 0.02f), a.gunRubber);
    }

    // ------------------------------------------------------------------ M4A1 (14.5" barrel, flat-top, red dot)

    static void BuildM4(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        // Upper receiver with Picatinny flat-top.
        b.Box(null, "Upper", new Vector3(0f, 0.045f, 0.03f), new Vector3(0.03f, 0.044f, 0.185f), a.gunMetal);
        b.Box(null, "RailBase", new Vector3(0f, 0.071f, 0.03f), new Vector3(0.022f, 0.008f, 0.185f), a.gunMetal);
        for (int i = 0; i < 12; i++)
            b.Box(null, "RailTooth", new Vector3(0f, 0.0765f, -0.052f + i * 0.0152f), new Vector3(0.022f, 0.003f, 0.007f), a.gunMetal);
        b.Box(null, "EjectionCover", new Vector3(0.0153f, 0.042f, 0.045f), new Vector3(0.001f, 0.018f, 0.05f), a.gunDark);
        b.Box(null, "BrassDeflector", new Vector3(0.0165f, 0.055f, 0.0f), new Vector3(0.006f, 0.014f, 0.016f), a.gunMetal);
        b.Cyl(null, "ForwardAssist", new Vector3(0.019f, 0.05f, -0.025f), 0.0075f, 0.022f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "ChargingHandle", new Vector3(0f, 0.066f, -0.068f), new Vector3(0.042f, 0.007f, 0.012f), a.gunMetal);
        var carrier = b.Group("BoltCarrier", Vector3.zero);
        m.slide = carrier;
        b.Box(carrier, "Bolt", new Vector3(0.0145f, 0.042f, 0.045f), new Vector3(0.002f, 0.012f, 0.03f), a.brass);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.02f, 0.045f, 0.045f));

        // Lower receiver, flared magwell, STANAG magazine, pistol grip, trigger guard.
        b.Box(null, "Lower", new Vector3(0f, 0.008f, 0.015f), new Vector3(0.028f, 0.035f, 0.16f), a.gunMetal);
        b.Box(null, "MagWell", new Vector3(0f, -0.02f, 0.06f), new Vector3(0.031f, 0.04f, 0.06f), a.gunMetal);
        var mag = b.Group("Magazine", new Vector3(0f, -0.035f, 0.062f));
        b.Box(mag, "Mag1", new Vector3(0f, -0.04f, 0f), new Vector3(0.024f, 0.09f, 0.062f), a.gunDark, new Vector3(-5f, 0f, 0f));
        b.Box(mag, "Mag2", new Vector3(0f, -0.11f, 0.012f), new Vector3(0.024f, 0.06f, 0.06f), a.gunDark, new Vector3(-14f, 0f, 0f));
        b.Box(mag, "MagBase", new Vector3(0f, -0.142f, 0.022f), new Vector3(0.027f, 0.008f, 0.066f), a.gunDark, new Vector3(-16f, 0f, 0f));
        b.Box(null, "MagRelease", new Vector3(0.0155f, 0.0f, 0.035f), new Vector3(0.003f, 0.01f, 0.01f), a.gunDark);
        b.Box(null, "Selector", new Vector3(-0.0152f, 0.012f, -0.01f), new Vector3(0.002f, 0.004f, 0.016f), a.gunDark);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.032f, 0.0f), new Vector3(0.01f, 0.004f, 0.062f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.016f, 0.005f), new Vector3(0.005f, 0.018f, 0.005f), a.gunMetal);
        var grip = b.Group("Grip", new Vector3(0f, -0.005f, -0.03f), new Vector3(20f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.042f, 0f), new Vector3(0.029f, 0.085f, 0.037f), a.gunPolymer);
        for (int i = 0; i < 4; i++)
            b.Box(grip, "GripTexture", new Vector3(0.0148f, -0.02f - i * 0.018f, 0f), new Vector3(0.001f, 0.01f, 0.03f), a.gunDark);
        b.Box(grip, "FingerRidge", new Vector3(0f, -0.04f, 0.019f), new Vector3(0.027f, 0.008f, 0.004f), a.gunPolymer);

        // Buffer tube and collapsible stock.
        b.Cyl(null, "BufferTube", new Vector3(0f, 0.04f, -0.135f), 0.015f, 0.16f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "CastleNut", new Vector3(0f, 0.04f, -0.058f), new Vector3(0.036f, 0.036f, 0.008f), a.gunMetal);
        b.Box(null, "StockBody", new Vector3(0f, 0.025f, -0.2f), new Vector3(0.036f, 0.064f, 0.11f), a.gunPolymer);
        b.Box(null, "StockLever", new Vector3(0f, -0.012f, -0.18f), new Vector3(0.012f, 0.01f, 0.06f), a.gunPolymer);
        b.Box(null, "StockCheek", new Vector3(0f, 0.06f, -0.21f), new Vector3(0.032f, 0.008f, 0.09f), a.gunPolymer);
        b.Box(null, "ButtPad", new Vector3(0f, 0.018f, -0.26f), new Vector3(0.04f, 0.105f, 0.014f), a.gunRubber);
        b.Box(null, "SlingLoop", new Vector3(0f, -0.022f, -0.245f), new Vector3(0.006f, 0.012f, 0.02f), a.gunMetal);

        // Ribbed round handguard, delta ring, A-frame front sight, barrel and A2 birdcage.
        b.Cyl(null, "DeltaRing", new Vector3(0f, 0.045f, 0.128f), 0.028f, 0.012f, a.gunMetal, Builder.Axis.Z);
        // Smooth free-float handguard with a full-length top rail and a few M-LOK slots, low-profile gas block.
        b.Cyl(null, "Handguard", new Vector3(0f, 0.045f, 0.25f), 0.0255f, 0.235f, a.gunPolymer, Builder.Axis.Z);
        b.Box(null, "HandguardRail", new Vector3(0f, 0.0715f, 0.25f), new Vector3(0.02f, 0.007f, 0.235f), a.gunMetal);
        for (int i = 0; i < 3; i++)
            foreach (float side in new[] { -1f, 1f })
                b.Box(null, "MlokSlot", new Vector3(side * 0.0252f, 0.045f, 0.19f + i * 0.055f), new Vector3(0.002f, 0.007f, 0.028f), a.gunDark);
        b.Cyl(null, "HandguardCap", new Vector3(0f, 0.045f, 0.368f), 0.0235f, 0.006f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Barrel", new Vector3(0f, 0.045f, 0.44f), 0.0085f, 0.15f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "GasBlock", new Vector3(0f, 0.047f, 0.385f), 0.0125f, 0.016f, a.gunDark, Builder.Axis.Z);
        b.Cyl(null, "FlashHider", new Vector3(0f, 0.045f, 0.522f), 0.0115f, 0.045f, a.gunMetal, Builder.Axis.Z);
        for (int i = 0; i < 3; i++)
        {
            float ang = 30f + i * 60f;
            var dir = Quaternion.Euler(0f, 0f, ang) * Vector3.up * 0.0105f;
            b.Box(null, "HiderSlot", new Vector3(dir.x, 0.045f + dir.y, 0.528f), new Vector3(0.003f, 0.003f, 0.03f), a.gunDark, new Vector3(0f, 0f, ang));
        }
        b.Cyl(null, "Bore", new Vector3(0f, 0.045f, 0.5455f), 0.005f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.045f, 0.55f));

        // Folded rear sight and an Aimpoint-style red dot: a round, open tube on a QD mount.
        b.Box(null, "RearBUIS", new Vector3(0f, 0.0805f, -0.045f), new Vector3(0.02f, 0.006f, 0.022f), a.gunMetal);
        RedDot(b, new Vector3(0f, 0.112f, 0.045f), 0.085f, 0.075f);
    }

    // ------------------------------------------------------------------ Remington 870 (18" barrel, wood furniture)

    static void BuildRemington(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        // Steel receiver.
        b.Box(null, "Receiver", new Vector3(0f, 0.042f, 0.075f), new Vector3(0.036f, 0.066f, 0.2f), a.gunMetal);
        b.Box(null, "ReceiverTop", new Vector3(0f, 0.077f, 0.075f), new Vector3(0.026f, 0.006f, 0.2f), a.gunMetal);
        b.Box(null, "EjectionPort", new Vector3(0.0182f, 0.05f, 0.1f), new Vector3(0.001f, 0.026f, 0.07f), a.gunDark);
        b.Box(null, "LoadingPort", new Vector3(0f, 0.008f, 0.11f), new Vector3(0.024f, 0.002f, 0.08f), a.gunDark);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.025f, 0.05f, 0.1f));
        b.Box(null, "SafetyButton", new Vector3(0f, 0.012f, -0.012f), new Vector3(0.038f, 0.004f, 0.006f), a.gunMetal);
        b.Box(null, "TriggerPlate", new Vector3(0f, 0.004f, 0.03f), new Vector3(0.03f, 0.012f, 0.1f), a.gunMetal);
        b.Box(null, "GuardBottom", new Vector3(0f, -0.022f, 0.032f), new Vector3(0.012f, 0.005f, 0.07f), a.gunMetal);
        b.Box(null, "GuardFront", new Vector3(0f, -0.01f, 0.066f), new Vector3(0.012f, 0.026f, 0.005f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.006f, 0.036f), new Vector3(0.005f, 0.018f, 0.005f), a.gunMetal, new Vector3(-10f, 0f, 0f));
        b.Box(null, "ActionRelease", new Vector3(-0.0005f, -0.004f, 0.06f), new Vector3(0.03f, 0.004f, 0.004f), a.gunMetal);

        // 18" barrel with vent rib and brass bead, magazine tube with cap and barrel clamp.
        b.Cyl(null, "Barrel", new Vector3(0f, 0.058f, 0.425f), 0.0125f, 0.5f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "VentRib", new Vector3(0f, 0.0725f, 0.425f), new Vector3(0.008f, 0.003f, 0.5f), a.gunMetal);
        b.Box(null, "FrontBlade", new Vector3(0f, 0.087f, 0.66f), new Vector3(0.003f, 0.026f, 0.006f), a.gunMetal);
        b.Sphere(null, "Bead", new Vector3(0f, 0.0985f, 0.66f), 0.0025f, a.brass);
        b.Box(null, "RearSightBase", new Vector3(0f, 0.084f, 0f), new Vector3(0.02f, 0.008f, 0.012f), a.gunMetal);
        b.Tube(null, "RearGhostRing", new Vector3(0f, 0.1f, 0f), 0.0125f, 0.0085f, 0.008f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Bore", new Vector3(0f, 0.058f, 0.6755f), 0.0095f, 0.002f, a.gunDark, Builder.Axis.Z);
        b.Cyl(null, "MagTube", new Vector3(0f, 0.025f, 0.385f), 0.0135f, 0.43f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "MagCap", new Vector3(0f, 0.025f, 0.613f), 0.016f, 0.03f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "BarrelClamp", new Vector3(0f, 0.042f, 0.575f), new Vector3(0.026f, 0.048f, 0.014f), a.gunMetal);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.058f, 0.68f));

        // Ribbed walnut pump (animated) on the action bars.
        var pump = b.Group("Pump", new Vector3(0f, 0.025f, 0.33f));
        m.pump = pump;
        b.Box(pump, "Forend", Vector3.zero, new Vector3(0.046f, 0.044f, 0.2f), a.gunWood);
        b.Box(pump, "ForendTop", new Vector3(0f, 0.018f, 0f), new Vector3(0.036f, 0.014f, 0.2f), a.gunWood);
        for (int i = 0; i < 9; i++)
            b.Box(pump, "Groove", new Vector3(0f, -0.004f, -0.08f + i * 0.02f), new Vector3(0.0475f, 0.03f, 0.004f), a.gunDark);
        b.Box(pump, "ActionBarL", new Vector3(-0.012f, 0.02f, -0.15f), new Vector3(0.003f, 0.01f, 0.11f), a.gunMetal);
        b.Box(pump, "ActionBarR", new Vector3(0.012f, 0.02f, -0.15f), new Vector3(0.003f, 0.01f, 0.11f), a.gunMetal);

        // Walnut stock: wrist, buttstock dropping toward the rear, comb, black recoil pad.
        b.Box(null, "Wrist", new Vector3(0f, -0.004f, -0.065f), new Vector3(0.034f, 0.05f, 0.12f), a.gunWood, new Vector3(-10f, 0f, 0f));
        b.Box(null, "Butt", new Vector3(0f, -0.03f, -0.235f), new Vector3(0.04f, 0.1f, 0.24f), a.gunWood, new Vector3(-6f, 0f, 0f));
        b.Box(null, "Comb", new Vector3(0f, 0.02f, -0.23f), new Vector3(0.034f, 0.02f, 0.22f), a.gunWood, new Vector3(-3f, 0f, 0f));
        b.Box(null, "Toe", new Vector3(0f, -0.08f, -0.27f), new Vector3(0.036f, 0.03f, 0.16f), a.gunWood, new Vector3(-16f, 0f, 0f));
        b.Box(null, "RecoilPad", new Vector3(0f, -0.035f, -0.365f), new Vector3(0.045f, 0.14f, 0.024f), a.gunRubber, new Vector3(-6f, 0f, 0f));
        b.Box(null, "SwivelStud", new Vector3(0f, -0.1f, -0.28f), new Vector3(0.006f, 0.008f, 0.012f), a.gunMetal);
    }

    // ------------------------------------------------------------------ Barrett M82A1 (.50 BMG, 1.45m long)

    static void BuildBarrett(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        // Upper receiver: long sheet-steel box with cooling perforations toward the front.
        b.Box(null, "Upper", new Vector3(0f, 0.072f, 0.16f), new Vector3(0.066f, 0.078f, 0.62f), a.gunMetal);
        b.Box(null, "UpperTop", new Vector3(0f, 0.114f, 0.16f), new Vector3(0.05f, 0.008f, 0.62f), a.gunMetal);
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 5; i++)
                b.Cyl(null, "CoolingHole", new Vector3(side * 0.0335f, 0.075f, 0.3f + i * 0.04f), 0.011f, 0.002f, a.gunDark, Builder.Axis.X);
        b.Box(null, "EjectionPort", new Vector3(0.0335f, 0.07f, 0.0f), new Vector3(0.002f, 0.03f, 0.09f), a.gunDark);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.04f, 0.07f, 0.0f));
        b.Box(null, "ChargingHandle", new Vector3(0.042f, 0.08f, 0.06f), new Vector3(0.018f, 0.016f, 0.026f), a.gunDark);

        // Lower receiver, pistol grip, guard, 10-round box magazine.
        b.Box(null, "Lower", new Vector3(0f, 0.016f, 0.02f), new Vector3(0.06f, 0.04f, 0.42f), a.gunMetal);
        var grip = b.Group("Grip", new Vector3(0f, -0.005f, -0.025f), new Vector3(16f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.045f, 0f), new Vector3(0.032f, 0.09f, 0.044f), a.gunPolymer);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.024f, 0.04f), new Vector3(0.012f, 0.005f, 0.075f), a.gunMetal);
        b.Box(null, "GuardFront", new Vector3(0f, -0.01f, 0.076f), new Vector3(0.012f, 0.028f, 0.005f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.008f, 0.045f), new Vector3(0.005f, 0.02f, 0.006f), a.gunMetal);
        b.Box(null, "Magazine", new Vector3(0f, -0.04f, 0.15f), new Vector3(0.045f, 0.09f, 0.095f), a.gunDark);
        b.Box(null, "MagBase", new Vector3(0f, -0.087f, 0.15f), new Vector3(0.048f, 0.006f, 0.1f), a.gunMetal);
        b.Box(null, "MagRibs", new Vector3(0.0228f, -0.04f, 0.15f), new Vector3(0.002f, 0.07f, 0.06f), a.gunMetal);

        // Heavy fluted barrel and the iconic double-chamber arrowhead muzzle brake.
        b.Cyl(null, "Barrel", new Vector3(0f, 0.072f, 0.79f), 0.019f, 0.64f, a.gunMetal, Builder.Axis.Z);
        for (int i = 0; i < 4; i++)
        {
            float ang = i * 90f + 45f;
            var off = Quaternion.Euler(0f, 0f, ang) * Vector3.up * 0.0185f;
            b.Box(null, "Flute", new Vector3(off.x, 0.072f + off.y, 0.75f), new Vector3(0.004f, 0.002f, 0.48f), a.gunDark, new Vector3(0f, 0f, ang));
        }
        b.Box(null, "BrakeCore", new Vector3(0f, 0.072f, 1.15f), new Vector3(0.034f, 0.045f, 0.13f), a.gunMetal);
        b.Box(null, "BrakeChamber1", new Vector3(0f, 0.072f, 1.125f), new Vector3(0.095f, 0.048f, 0.03f), a.gunMetal);
        b.Box(null, "BrakeChamber2", new Vector3(0f, 0.072f, 1.172f), new Vector3(0.095f, 0.048f, 0.03f), a.gunMetal);
        b.Box(null, "BrakePortL", new Vector3(-0.048f, 0.072f, 1.148f), new Vector3(0.002f, 0.03f, 0.017f), a.gunDark);
        b.Box(null, "BrakePortR", new Vector3(0.048f, 0.072f, 1.148f), new Vector3(0.002f, 0.03f, 0.017f), a.gunDark);
        b.Box(null, "BrakeArrowL", new Vector3(-0.026f, 0.072f, 1.2f), new Vector3(0.055f, 0.046f, 0.014f), a.gunMetal, new Vector3(0f, -35f, 0f));
        b.Box(null, "BrakeArrowR", new Vector3(0.026f, 0.072f, 1.2f), new Vector3(0.055f, 0.046f, 0.014f), a.gunMetal, new Vector3(0f, 35f, 0f));
        b.Cyl(null, "Bore", new Vector3(0f, 0.072f, 1.218f), 0.0075f, 0.004f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.072f, 1.225f));

        // Carry handle and folded bipod.
        b.Box(null, "CarryHandle", new Vector3(0f, 0.152f, 0.36f), new Vector3(0.016f, 0.012f, 0.13f), a.gunDark);
        b.Box(null, "CarryPostF", new Vector3(0f, 0.133f, 0.42f), new Vector3(0.016f, 0.03f, 0.012f), a.gunDark);
        b.Box(null, "CarryPostR", new Vector3(0f, 0.133f, 0.3f), new Vector3(0.016f, 0.03f, 0.012f), a.gunDark);
        b.Box(null, "BipodYoke", new Vector3(0f, 0.045f, 0.5f), new Vector3(0.07f, 0.024f, 0.035f), a.gunMetal);
        for (int side = -1; side <= 1; side += 2)
        {
            b.Cyl(null, "BipodLeg", new Vector3(side * 0.03f, 0.044f, 0.66f), 0.0075f, 0.3f, a.gunMetal, Builder.Axis.Z);
            b.Box(null, "BipodFoot", new Vector3(side * 0.03f, 0.044f, 0.815f), new Vector3(0.028f, 0.012f, 0.022f), a.gunRubber);
        }

        // Buttstock with thick recoil pad, cheek rest and folded monopod.
        b.Box(null, "Stock", new Vector3(0f, 0.045f, -0.3f), new Vector3(0.05f, 0.095f, 0.32f), a.gunMetal);
        b.Box(null, "StockLower", new Vector3(0f, -0.012f, -0.33f), new Vector3(0.044f, 0.03f, 0.22f), a.gunMetal);
        b.Box(null, "CheekRest", new Vector3(0f, 0.1f, -0.3f), new Vector3(0.046f, 0.018f, 0.2f), a.gunPolymer);
        b.Box(null, "RecoilPad", new Vector3(0f, 0.04f, -0.48f), new Vector3(0.058f, 0.135f, 0.045f), a.gunRubber);
        b.Cyl(null, "Monopod", new Vector3(0f, -0.04f, -0.39f), 0.008f, 0.05f, a.gunMetal, Builder.Axis.Y);

        // Leupold-style riflescope on a rail with two rings.
        b.Box(null, "ScopeRail", new Vector3(0f, 0.122f, 0.03f), new Vector3(0.03f, 0.008f, 0.3f), a.gunMetal);
        b.Box(null, "RingRear", new Vector3(0f, 0.137f, -0.05f), new Vector3(0.036f, 0.026f, 0.022f), a.gunMetal);
        b.Box(null, "RingFront", new Vector3(0f, 0.137f, 0.1f), new Vector3(0.036f, 0.026f, 0.022f), a.gunMetal);
        b.Cyl(null, "ScopeTube", new Vector3(0f, 0.155f, 0.03f), 0.015f, 0.3f, a.gunDark, Builder.Axis.Z);
        b.Cyl(null, "ObjectiveBell", new Vector3(0f, 0.155f, 0.215f), 0.026f, 0.075f, a.gunDark, Builder.Axis.Z);
        b.Cyl(null, "ObjectiveTaper", new Vector3(0f, 0.155f, 0.172f), 0.02f, 0.02f, a.gunDark, Builder.Axis.Z);
        b.Cyl(null, "ObjectiveLens", new Vector3(0f, 0.155f, 0.2535f), 0.023f, 0.002f, a.scopeLens, Builder.Axis.Z);
        b.Cyl(null, "OcularBell", new Vector3(0f, 0.155f, -0.15f), 0.021f, 0.065f, a.gunDark, Builder.Axis.Z);
        b.Cyl(null, "OcularLens", new Vector3(0f, 0.155f, -0.1835f), 0.018f, 0.002f, a.scopeLens, Builder.Axis.Z);
        b.Cyl(null, "TurretTop", new Vector3(0f, 0.178f, 0.03f), 0.012f, 0.024f, a.gunDark, Builder.Axis.Y);
        b.Cyl(null, "TurretSide", new Vector3(0.023f, 0.155f, 0.03f), 0.012f, 0.024f, a.gunDark, Builder.Axis.X);
        b.Cyl(null, "PowerRing", new Vector3(0f, 0.155f, -0.105f), 0.0175f, 0.02f, a.gunMetal, Builder.Axis.Z);
    }

    // ------------------------------------------------------------------ Desert Eagle .50 AE, gold plated (6" barrel, ~270mm)

    static void BuildDeagle(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var gold = a.gold;
        // Fixed barrel on top with the triangular-profile rib, running to the muzzle.
        b.Box(null, "Barrel", new Vector3(0f, 0.056f, 0.118f), new Vector3(0.029f, 0.028f, 0.152f), gold);
        b.Box(null, "BarrelRib", new Vector3(0f, 0.073f, 0.118f), new Vector3(0.013f, 0.007f, 0.152f), gold);
        b.Box(null, "BarrelFlatL", new Vector3(-0.0146f, 0.052f, 0.118f), new Vector3(0.001f, 0.014f, 0.15f), a.gunDark);
        b.Box(null, "BarrelFlatR", new Vector3(0.0146f, 0.052f, 0.118f), new Vector3(0.001f, 0.014f, 0.15f), a.gunDark);
        b.Cyl(null, "Bore", new Vector3(0f, 0.055f, 0.1945f), 0.0065f, 0.002f, a.gunDark, Builder.Axis.Z);
        b.Box(null, "FrontSight", new Vector3(0f, 0.088f, 0.186f), new Vector3(0.004f, 0.014f, 0.012f), gold);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.055f, 0.2f));

        // Slide (animated) behind the barrel block: serrations, ejection port, rear sight, hammer.
        var slide = b.Group("Slide", Vector3.zero);
        m.slide = slide;
        b.Box(slide, "SlideBody", new Vector3(0f, 0.047f, -0.018f), new Vector3(0.033f, 0.048f, 0.13f), gold);
        for (int i = 0; i < 8; i++)
            b.Box(slide, "Serration", new Vector3(0f, 0.047f, -0.075f + i * 0.0045f), new Vector3(0.0338f, 0.034f, 0.0016f), a.brass);
        b.Box(slide, "EjectionPort", new Vector3(0.0167f, 0.058f, 0.02f), new Vector3(0.001f, 0.016f, 0.04f), a.gunDark);
        b.Box(slide, "RearSight", new Vector3(0f, 0.0755f, -0.07f), new Vector3(0.02f, 0.004f, 0.01f), gold);
        b.Box(slide, "RearSightL", new Vector3(-0.0068f, 0.0855f, -0.07f), new Vector3(0.0055f, 0.019f, 0.01f), gold);
        b.Box(slide, "RearSightR", new Vector3(0.0068f, 0.0855f, -0.07f), new Vector3(0.0055f, 0.019f, 0.01f), gold);
        b.Box(slide, "SafetyL", new Vector3(-0.0175f, 0.055f, -0.07f), new Vector3(0.003f, 0.008f, 0.014f), gold);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.02f, 0.058f, 0.02f));
        b.Box(null, "Hammer", new Vector3(0f, 0.06f, -0.088f), new Vector3(0.009f, 0.016f, 0.01f), gold, new Vector3(-20f, 0f, 0f));

        // Frame, dust cover, trigger guard.
        b.Box(null, "Frame", new Vector3(0f, 0.014f, 0.055f), new Vector3(0.031f, 0.024f, 0.21f), gold);
        b.Box(null, "GuardFront", new Vector3(0f, -0.016f, 0.082f), new Vector3(0.01f, 0.038f, 0.008f), gold, new Vector3(-10f, 0f, 0f));
        b.Box(null, "GuardBottom", new Vector3(0f, -0.033f, 0.05f), new Vector3(0.01f, 0.007f, 0.068f), gold);
        b.Box(null, "Trigger", new Vector3(0f, -0.01f, 0.045f), new Vector3(0.006f, 0.022f, 0.006f), gold, new Vector3(-10f, 0f, 0f));
        b.Box(null, "SlideStop", new Vector3(-0.0162f, 0.02f, 0.035f), new Vector3(0.002f, 0.005f, 0.018f), a.gunDark);

        // Grip with black rubber panels and gold magazine base.
        var grip = b.Group("Grip", new Vector3(0f, 0.004f, -0.025f), new Vector3(15f, 0f, 0f));
        b.Box(grip, "GripFrame", new Vector3(0f, -0.06f, 0f), new Vector3(0.031f, 0.12f, 0.052f), gold);
        b.Box(grip, "GripPanel", new Vector3(0f, -0.058f, 0.002f), new Vector3(0.0345f, 0.095f, 0.042f), a.gunRubber);
        for (int i = 0; i < 5; i++)
            b.Box(grip, "Stipple", new Vector3(0.0175f, -0.025f - i * 0.016f, 0.002f), new Vector3(0.0012f, 0.009f, 0.036f), a.gunDark);
        b.Box(grip, "MagBase", new Vector3(0f, -0.122f, 0.002f), new Vector3(0.033f, 0.008f, 0.055f), gold);
    }

    // ------------------------------------------------------------------ M32 MGL: six-shot 40mm revolver launcher

    static void BuildM32(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        // Revolving cylinder below the barrel line, with six chambers on its face.
        var cyl = b.Group("Cylinder", new Vector3(0f, 0.035f, 0.13f));
        m.cylinder = cyl;
        b.Cyl(cyl, "Drum", Vector3.zero, 0.078f, 0.17f, a.gunPolymer, Builder.Axis.Z);
        for (int i = 0; i < 6; i++)
        {
            float ang = i * 60f * Mathf.Deg2Rad;
            var off = new Vector3(Mathf.Sin(ang), Mathf.Cos(ang), 0f) * 0.048f;
            b.Cyl(cyl, "Chamber", off + new Vector3(0f, 0f, 0.0855f), 0.021f, 0.002f, a.gunDark, Builder.Axis.Z);
            b.Cyl(cyl, "Flute", new Vector3(off.x, off.y, 0f) * 1.62f, 0.012f, 0.15f, a.gunDark, Builder.Axis.Z);
        }
        // Barrel (40mm) on top, frame straps, sight rail and reflex sight.
        b.Cyl(null, "Barrel", new Vector3(0f, 0.083f, 0.36f), 0.03f, 0.3f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Bore", new Vector3(0f, 0.083f, 0.5105f), 0.022f, 0.002f, a.gunDark, Builder.Axis.Z);
        b.Box(null, "TopStrap", new Vector3(0f, 0.125f, 0.2f), new Vector3(0.03f, 0.018f, 0.36f), a.gunMetal);
        b.Box(null, "FrontFrame", new Vector3(0f, 0.04f, 0.225f), new Vector3(0.04f, 0.17f, 0.02f), a.gunMetal);
        b.Box(null, "RearFrame", new Vector3(0f, 0.04f, 0.035f), new Vector3(0.04f, 0.17f, 0.02f), a.gunMetal);
        for (int i = 0; i < 8; i++)
            b.Box(null, "RailTooth", new Vector3(0f, 0.137f, 0.08f + i * 0.02f), new Vector3(0.026f, 0.005f, 0.008f), a.gunMetal);
        RedDot(b, new Vector3(0f, 0.172f, 0.12f), 0.142f, 0.07f);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.083f, 0.52f));
        // Grips, trigger guard and folding stock.
        var grip = b.Group("Grip", new Vector3(0f, -0.02f, -0.01f), new Vector3(15f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.045f, 0f), new Vector3(0.032f, 0.09f, 0.042f), a.gunPolymer);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.05f, 0.03f), new Vector3(0.012f, 0.005f, 0.06f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.035f, 0.035f), new Vector3(0.005f, 0.02f, 0.005f), a.gunMetal);
        b.Box(null, "Foregrip", new Vector3(0f, -0.07f, 0.33f), new Vector3(0.03f, 0.1f, 0.035f), a.gunPolymer);
        b.Box(null, "ForegripMount", new Vector3(0f, -0.012f, 0.33f), new Vector3(0.035f, 0.02f, 0.06f), a.gunMetal);
        b.Box(null, "StockTube", new Vector3(0f, 0.05f, -0.15f), new Vector3(0.026f, 0.026f, 0.26f), a.gunMetal);
        b.Box(null, "StockLower", new Vector3(0f, -0.01f, -0.2f), new Vector3(0.02f, 0.02f, 0.16f), a.gunMetal, new Vector3(-20f, 0f, 0f));
        b.Box(null, "ButtPad", new Vector3(0f, 0.02f, -0.29f), new Vector3(0.042f, 0.13f, 0.025f), a.gunRubber);
        m.ejectionPort = null;
    }

    // ------------------------------------------------------------------ RPG-7 with a PG-7V rocket loaded

    static void BuildRPG(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        const float axis = 0.09f;
        b.Cyl(null, "Tube", new Vector3(0f, axis, 0.05f), 0.035f, 0.95f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "WoodGuard", new Vector3(0f, axis, -0.03f), 0.045f, 0.28f, a.gunWood, Builder.Axis.Z);
        b.Cyl(null, "Venturi", new Vector3(0f, axis, -0.47f), 0.05f, 0.12f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Bell", new Vector3(0f, axis, -0.545f), 0.062f, 0.04f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "BellBore", new Vector3(0f, axis, -0.566f), 0.05f, 0.002f, a.gunDark, Builder.Axis.Z);
        b.Cyl(null, "MuzzleRing", new Vector3(0f, axis, 0.52f), 0.039f, 0.02f, a.gunMetal, Builder.Axis.Z);
        // Grips under the tube, iron sights and the side-mounted optic.
        var grip = b.Group("Grip", new Vector3(0f, 0.045f, 0f), new Vector3(12f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.05f, 0f), new Vector3(0.03f, 0.1f, 0.04f), a.gunWood);
        b.Box(null, "TriggerHousing", new Vector3(0f, 0.045f, 0.04f), new Vector3(0.03f, 0.03f, 0.1f), a.gunMetal);
        b.Box(null, "TriggerGuard", new Vector3(0f, 0.005f, 0.045f), new Vector3(0.01f, 0.005f, 0.06f), a.gunMetal);
        b.Box(null, "FrontGrip", new Vector3(0f, -0.005f, 0.18f), new Vector3(0.03f, 0.1f, 0.035f), a.gunWood);
        b.Box(null, "FrontSight", new Vector3(0f, 0.137f, 0.38f), new Vector3(0.006f, 0.025f, 0.01f), a.gunMetal);
        b.Box(null, "RearSightBase", new Vector3(0f, 0.13f, 0.1f), new Vector3(0.026f, 0.01f, 0.01f), a.gunMetal);
        b.Box(null, "RearEarL", new Vector3(-0.0085f, 0.142f, 0.1f), new Vector3(0.007f, 0.015f, 0.008f), a.gunMetal);
        b.Box(null, "RearEarR", new Vector3(0.0085f, 0.142f, 0.1f), new Vector3(0.007f, 0.015f, 0.008f), a.gunMetal);
        b.Box(null, "OpticMount", new Vector3(-0.042f, 0.12f, 0.05f), new Vector3(0.03f, 0.02f, 0.05f), a.gunMetal);
        b.Box(null, "Optic", new Vector3(-0.058f, 0.15f, 0.05f), new Vector3(0.035f, 0.05f, 0.12f), a.olive);
        b.Cyl(null, "Eyepiece", new Vector3(-0.058f, 0.155f, -0.02f), 0.016f, 0.04f, a.gunRubber, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, axis, 0.53f));
        // The rocket: motor tube inside, olive warhead and nose out front.
        var rocket = b.Group("Warhead", Vector3.zero);
        m.warhead = rocket;
        b.Cyl(rocket, "RocketBody", new Vector3(0f, axis, 0.6f), 0.03f, 0.15f, a.olive, Builder.Axis.Z);
        b.Cyl(rocket, "Warhead", new Vector3(0f, axis, 0.77f), 0.0425f, 0.2f, a.olive, Builder.Axis.Z);
        b.Cyl(rocket, "Ogive1", new Vector3(0f, axis, 0.895f), 0.032f, 0.05f, a.olive, Builder.Axis.Z);
        b.Cyl(rocket, "Ogive2", new Vector3(0f, axis, 0.94f), 0.02f, 0.04f, a.olive, Builder.Axis.Z);
        b.Cyl(rocket, "Fuze", new Vector3(0f, axis, 0.975f), 0.009f, 0.03f, a.gunMetal, Builder.Axis.Z);
        m.ejectionPort = null;
    }


    // ------------------------------------------------------------------ shared: tube red-dot sight

    /// <summary>Round red-dot sight centred on `center` (the sight line), mounted on a rail whose top is at `railTop`.</summary>
    static void RedDot(Builder b, Vector3 center, float railTop, float length)
    {
        var a = GameAssets.I;
        const float outer = 0.0195f, inner = 0.0162f;
        float mountH = center.y - outer - railTop + 0.002f;
        b.Box(null, "RedDotMount", new Vector3(0f, railTop + mountH / 2f, center.z), new Vector3(0.024f, mountH, length * 0.6f), a.gunMetal);
        b.Box(null, "RedDotMountLever", new Vector3(0.0135f, railTop + mountH * 0.4f, center.z - 0.004f), new Vector3(0.004f, mountH * 0.6f, length * 0.4f), a.gunDark);
        b.Tube(null, "RedDotTube", center, outer, inner, length, a.gunMetal, Builder.Axis.Z);
        b.Tube(null, "RedDotFrontRing", center + Vector3.forward * (length / 2f - 0.003f), outer + 0.0015f, inner, 0.006f, a.gunDark, Builder.Axis.Z);
        b.Tube(null, "RedDotRearRing", center - Vector3.forward * (length / 2f - 0.003f), outer + 0.0015f, inner, 0.006f, a.gunDark, Builder.Axis.Z);
        b.Cyl(null, "RedDotTurret", center + new Vector3(outer + 0.003f, 0f, 0.004f), 0.0075f, 0.01f, a.gunMetal, Builder.Axis.X);
        b.Cyl(null, "RedDotTurretTop", center + new Vector3(0f, outer + 0.003f, 0.004f), 0.0075f, 0.008f, a.gunMetal, Builder.Axis.Y);
        Builder.Tint(b.Sphere(null, "RedDot", center + Vector3.forward * (length / 2f - 0.01f), 0.0009f, a.muzzleFlash), new Color(1f, 0.1f, 0.1f));
    }

    // ------------------------------------------------------------------ AA-12: select-fire 12 gauge with a 20-round drum

    static void BuildAA12(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        b.Box(null, "Receiver", new Vector3(0f, 0.045f, 0.12f), new Vector3(0.056f, 0.085f, 0.42f), a.gunMetal);
        b.Box(null, "ReceiverTop", new Vector3(0f, 0.092f, 0.12f), new Vector3(0.046f, 0.01f, 0.42f), a.gunMetal);
        for (int i = 0; i < 4; i++)
            b.Box(null, "ReceiverRib", new Vector3(0f, 0.045f, -0.03f + i * 0.1f), new Vector3(0.058f, 0.07f, 0.006f), a.gunDark);
        // Carry rail with peep and post sights.
        b.Box(null, "CarryRail", new Vector3(0f, 0.115f, 0.1f), new Vector3(0.03f, 0.01f, 0.3f), a.gunDark);
        b.Box(null, "CarryPostRear", new Vector3(0f, 0.103f, -0.03f), new Vector3(0.026f, 0.016f, 0.02f), a.gunDark);
        b.Box(null, "CarryPostFront", new Vector3(0f, 0.103f, 0.23f), new Vector3(0.026f, 0.016f, 0.02f), a.gunDark);
        RedDot(b, new Vector3(0f, 0.147f, 0.06f), 0.12f, 0.07f);
        // Vented barrel shroud and short barrel.
        b.Cyl(null, "Shroud", new Vector3(0f, 0.05f, 0.45f), 0.026f, 0.26f, a.gunMetal, Builder.Axis.Z);
        for (int i = 0; i < 6; i++)
            foreach (float side in new[] { -1f, 1f })
                b.Cyl(null, "ShroudVent", new Vector3(side * 0.0255f, 0.05f, 0.35f + i * 0.04f), 0.008f, 0.002f, a.gunDark, Builder.Axis.X);
        b.Cyl(null, "Barrel", new Vector3(0f, 0.05f, 0.61f), 0.015f, 0.06f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Bore", new Vector3(0f, 0.05f, 0.6405f), 0.0095f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.05f, 0.645f));
        // Bolt indicator (kicks back), ejection port, charging handle.
        var bolt = b.Group("Bolt", Vector3.zero);
        m.slide = bolt;
        b.Box(bolt, "BoltFace", new Vector3(0.0285f, 0.06f, 0.06f), new Vector3(0.002f, 0.016f, 0.04f), a.brass);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.035f, 0.06f, 0.06f));
        b.Box(null, "ChargingHandle", new Vector3(-0.034f, 0.07f, 0.22f), new Vector3(0.012f, 0.012f, 0.03f), a.gunDark);
        // 20-round drum, magwell, grip, guard and straight stock.
        b.Box(null, "MagWell", new Vector3(0f, -0.005f, 0.13f), new Vector3(0.04f, 0.03f, 0.08f), a.gunMetal);
        b.Cyl(null, "Drum", new Vector3(0f, -0.075f, 0.13f), 0.085f, 0.07f, a.gunPolymer, Builder.Axis.X);
        b.Cyl(null, "DrumCapL", new Vector3(-0.036f, -0.075f, 0.13f), 0.06f, 0.006f, a.gunDark, Builder.Axis.X);
        b.Cyl(null, "DrumCapR", new Vector3(0.036f, -0.075f, 0.13f), 0.06f, 0.006f, a.gunDark, Builder.Axis.X);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.03f, 0.02f), new Vector3(0.012f, 0.005f, 0.06f), a.gunPolymer);
        b.Box(null, "Trigger", new Vector3(0f, -0.014f, 0.025f), new Vector3(0.005f, 0.018f, 0.005f), a.gunMetal);
        var grip = b.Group("Grip", new Vector3(0f, -0.005f, -0.025f), new Vector3(16f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.045f, 0f), new Vector3(0.032f, 0.09f, 0.042f), a.gunPolymer);
        b.Box(null, "Stock", new Vector3(0f, 0.045f, -0.17f), new Vector3(0.046f, 0.075f, 0.26f), a.gunPolymer);
        b.Box(null, "StockLower", new Vector3(0f, 0.0f, -0.2f), new Vector3(0.04f, 0.04f, 0.2f), a.gunPolymer, new Vector3(-8f, 0f, 0f));
        b.Box(null, "ButtPad", new Vector3(0f, 0.035f, -0.305f), new Vector3(0.05f, 0.12f, 0.02f), a.gunRubber);
    }

    // ------------------------------------------------------------------ Mosin-Nagant 91/30: bolt action, iron sights

    static void BuildMosin(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var wood = a.gunWood;
        const float bore = 0.05f;
        // Full-length wooden stock with steel bands.
        b.Box(null, "Wrist", new Vector3(0f, 0.012f, -0.05f), new Vector3(0.032f, 0.045f, 0.12f), wood, new Vector3(-8f, 0f, 0f));
        b.Box(null, "Butt", new Vector3(0f, -0.02f, -0.27f), new Vector3(0.042f, 0.11f, 0.34f), wood, new Vector3(-5f, 0f, 0f));
        b.Box(null, "Comb", new Vector3(0f, 0.034f, -0.25f), new Vector3(0.036f, 0.02f, 0.3f), wood, new Vector3(-2f, 0f, 0f));
        b.Box(null, "Buttplate", new Vector3(0f, -0.032f, -0.44f), new Vector3(0.044f, 0.13f, 0.012f), a.gunMetal, new Vector3(-5f, 0f, 0f));
        b.Box(null, "Forend", new Vector3(0f, 0.03f, 0.4f), new Vector3(0.04f, 0.045f, 0.62f), wood);
        b.Box(null, "HandguardTop", new Vector3(0f, 0.064f, 0.46f), new Vector3(0.026f, 0.012f, 0.38f), wood);
        foreach (float z in new[] { 0.3f, 0.66f })
            b.Box(null, "Band", new Vector3(0f, 0.042f, z), new Vector3(0.044f, 0.062f, 0.012f), a.gunMetal);
        // Receiver, magazine box, guard.
        b.Cyl(null, "Receiver", new Vector3(0f, bore, 0.08f), 0.016f, 0.2f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "Magazine", new Vector3(0f, 0.0f, 0.1f), new Vector3(0.034f, 0.05f, 0.09f), a.gunMetal);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.03f, 0.04f), new Vector3(0.01f, 0.005f, 0.07f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.014f, 0.04f), new Vector3(0.004f, 0.02f, 0.005f), a.gunMetal);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.02f, 0.06f, 0.06f));
        // Bolt: straight handle out to the right, works back after each shot.
        var bolt = b.Group("Bolt", Vector3.zero);
        m.pump = bolt;
        b.Cyl(bolt, "BoltBody", new Vector3(0f, bore, -0.005f), 0.0095f, 0.1f, a.gunMetal, Builder.Axis.Z);
        b.Box(bolt, "BoltHandle", new Vector3(0.035f, bore - 0.004f, -0.02f), new Vector3(0.058f, 0.008f, 0.008f), a.gunMetal);
        b.Sphere(bolt, "BoltKnob", new Vector3(0.066f, bore - 0.004f, -0.02f), 0.011f, a.gunMetal);
        b.Cyl(bolt, "CockingKnob", new Vector3(0f, bore, -0.06f), 0.011f, 0.02f, a.gunMetal, Builder.Axis.Z);
        // Long barrel and crown.
        b.Cyl(null, "Barrel", new Vector3(0f, bore, 0.55f), 0.011f, 0.74f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Crown", new Vector3(0f, bore, 0.925f), 0.0125f, 0.012f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Bore", new Vector3(0f, bore, 0.9315f), 0.004f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, bore, 0.935f));
        // Iron sights: tangent rear leaf with a notch, hooded front post (sight line at y = 0.08).
        b.Box(null, "RearSightBase", new Vector3(0f, 0.064f, 0.22f), new Vector3(0.024f, 0.012f, 0.06f), a.gunMetal);
        b.Box(null, "RearLeaf", new Vector3(0f, 0.072f, 0.2f), new Vector3(0.018f, 0.004f, 0.05f), a.gunMetal);
        b.Box(null, "RearNotchL", new Vector3(-0.006f, 0.076f, 0.18f), new Vector3(0.005f, 0.008f, 0.004f), a.gunMetal);
        b.Box(null, "RearNotchR", new Vector3(0.006f, 0.076f, 0.18f), new Vector3(0.005f, 0.008f, 0.004f), a.gunMetal);
        b.Box(null, "FrontSightBase", new Vector3(0f, 0.064f, 0.9f), new Vector3(0.014f, 0.012f, 0.02f), a.gunMetal);
        b.Box(null, "FrontPost", new Vector3(0f, 0.0735f, 0.9f), new Vector3(0.0025f, 0.013f, 0.003f), a.gunMetal);
        b.Tube(null, "FrontHood", new Vector3(0f, 0.077f, 0.9f), 0.012f, 0.0105f, 0.018f, a.gunMetal, Builder.Axis.Z);
    }

    // ------------------------------------------------------------------ PPSh-41: 71-round drum, perforated jacket

    static void BuildPPSh(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var wood = a.gunWood;
        const float bore = 0.052f;
        // Wooden stock and wrist (no pistol grip).
        b.Box(null, "Wrist", new Vector3(0f, 0.012f, -0.04f), new Vector3(0.034f, 0.05f, 0.1f), wood, new Vector3(-6f, 0f, 0f));
        b.Box(null, "Butt", new Vector3(0f, -0.015f, -0.2f), new Vector3(0.04f, 0.1f, 0.26f), wood, new Vector3(-6f, 0f, 0f));
        b.Box(null, "Buttplate", new Vector3(0f, -0.025f, -0.33f), new Vector3(0.042f, 0.11f, 0.01f), a.gunMetal, new Vector3(-6f, 0f, 0f));
        b.Box(null, "StockBody", new Vector3(0f, 0.022f, 0.06f), new Vector3(0.04f, 0.05f, 0.17f), wood);
        // Receiver, perforated barrel jacket and the slanted compensator nose.
        b.Cyl(null, "Receiver", new Vector3(0f, bore, 0.08f), 0.02f, 0.2f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Jacket", new Vector3(0f, bore, 0.31f), 0.0175f, 0.26f, a.gunMetal, Builder.Axis.Z);
        for (int i = 0; i < 7; i++)
            foreach (float side in new[] { -1f, 1f })
                b.Box(null, "JacketSlot", new Vector3(side * 0.0172f, bore, 0.21f + i * 0.03f + (side > 0 ? 0.015f : 0f)), new Vector3(0.002f, 0.009f, 0.016f), a.gunDark);
        b.Box(null, "Compensator", new Vector3(0f, bore + 0.006f, 0.442f), new Vector3(0.036f, 0.044f, 0.012f), a.gunMetal, new Vector3(25f, 0f, 0f));
        b.Cyl(null, "Bore", new Vector3(0f, bore, 0.436f), 0.004f, 0.004f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, bore, 0.45f));
        // Sights: flip rear notch on the receiver, hooded front post on the jacket (sight line y = 0.082).
        b.Box(null, "RearSight", new Vector3(0f, 0.0745f, 0.02f), new Vector3(0.016f, 0.007f, 0.012f), a.gunMetal);
        b.Box(null, "RearNotchL", new Vector3(-0.005f, 0.086f, 0.02f), new Vector3(0.004f, 0.016f, 0.004f), a.gunMetal);
        b.Box(null, "RearNotchR", new Vector3(0.005f, 0.086f, 0.02f), new Vector3(0.004f, 0.016f, 0.004f), a.gunMetal);
        b.Box(null, "FrontPost", new Vector3(0f, 0.08f, 0.41f), new Vector3(0.0025f, 0.028f, 0.003f), a.gunMetal);
        b.Tube(null, "FrontHood", new Vector3(0f, 0.091f, 0.41f), 0.01f, 0.0085f, 0.016f, a.gunMetal, Builder.Axis.Z);
        // Bolt handle in its slot (kicks back), ejection port on top.
        var bolt = b.Group("Bolt", Vector3.zero);
        m.slide = bolt;
        b.Box(bolt, "BoltHandle", new Vector3(0.024f, bore + 0.006f, 0.05f), new Vector3(0.014f, 0.01f, 0.012f), a.gunMetal);
        b.Box(null, "EjectionPort", new Vector3(0f, bore + 0.019f, 0.09f), new Vector3(0.012f, 0.002f, 0.03f), a.gunDark);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.008f, bore + 0.025f, 0.09f));
        // 71-round drum ahead of the trigger guard.
        b.Box(null, "MagWell", new Vector3(0f, 0.01f, 0.14f), new Vector3(0.03f, 0.03f, 0.04f), a.gunMetal);
        b.Cyl(null, "Drum", new Vector3(0f, -0.055f, 0.15f), 0.08f, 0.055f, a.gunMetal, Builder.Axis.X);
        b.Cyl(null, "DrumFace", new Vector3(0.028f, -0.055f, 0.15f), 0.065f, 0.004f, a.gunDark, Builder.Axis.X);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.022f, 0.04f), new Vector3(0.01f, 0.005f, 0.06f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.008f, 0.045f), new Vector3(0.004f, 0.018f, 0.005f), a.gunMetal);
    }

    // ------------------------------------------------------------------ M249 SAW: belt-fed, 100-round box

    static void BuildM249(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        const float bore = 0.055f;
        b.Box(null, "Receiver", new Vector3(0f, 0.05f, 0.1f), new Vector3(0.06f, 0.08f, 0.3f), a.gunMetal);
        b.Box(null, "FeedCover", new Vector3(0f, 0.098f, 0.12f), new Vector3(0.056f, 0.016f, 0.18f), a.gunMetal);
        b.Box(null, "FeedCoverLatch", new Vector3(0f, 0.1f, 0.025f), new Vector3(0.03f, 0.012f, 0.014f), a.gunDark);
        // Rear sight on the feed cover, front post on the barrel (sight line y = 0.118).
        b.Box(null, "RearSight", new Vector3(0f, 0.113f, 0.04f), new Vector3(0.02f, 0.014f, 0.014f), a.gunDark);
        b.Box(null, "RearNotchL", new Vector3(-0.006f, 0.124f, 0.04f), new Vector3(0.005f, 0.008f, 0.006f), a.gunDark);
        b.Box(null, "RearNotchR", new Vector3(0.006f, 0.124f, 0.04f), new Vector3(0.005f, 0.008f, 0.006f), a.gunDark);
        b.Box(null, "FrontSightBase", new Vector3(0f, 0.072f, 0.7f), new Vector3(0.014f, 0.02f, 0.016f), a.gunMetal);
        b.Box(null, "FrontPost", new Vector3(0f, 0.105f, 0.7f), new Vector3(0.003f, 0.046f, 0.004f), a.gunMetal);
        b.Box(null, "FrontEarL", new Vector3(-0.007f, 0.105f, 0.7f), new Vector3(0.003f, 0.04f, 0.01f), a.gunMetal);
        b.Box(null, "FrontEarR", new Vector3(0.007f, 0.105f, 0.7f), new Vector3(0.003f, 0.04f, 0.01f), a.gunMetal);
        // Barrel with heat shield, gas tube, carry handle, flash hider, folded bipod.
        b.Cyl(null, "Barrel", new Vector3(0f, bore, 0.5f), 0.013f, 0.5f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "HeatShield", new Vector3(0f, 0.075f, 0.43f), new Vector3(0.03f, 0.006f, 0.25f), a.gunDark);
        b.Cyl(null, "GasTube", new Vector3(0f, 0.03f, 0.42f), 0.009f, 0.3f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "CarryHandle", new Vector3(-0.034f, 0.1f, 0.33f), new Vector3(0.014f, 0.012f, 0.12f), a.gunPolymer);
        b.Box(null, "CarryPostF", new Vector3(-0.024f, 0.083f, 0.38f), new Vector3(0.02f, 0.028f, 0.012f), a.gunPolymer);
        b.Box(null, "CarryPostR", new Vector3(-0.024f, 0.083f, 0.28f), new Vector3(0.02f, 0.028f, 0.012f), a.gunPolymer);
        b.Cyl(null, "FlashHider", new Vector3(0f, bore, 0.77f), 0.0145f, 0.04f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Bore", new Vector3(0f, bore, 0.7905f), 0.005f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, bore, 0.795f));
        foreach (float side in new[] { -1f, 1f })
        {
            b.Cyl(null, "BipodLeg", new Vector3(side * 0.013f, 0.022f, 0.6f), 0.006f, 0.28f, a.gunMetal, Builder.Axis.Z);
            b.Box(null, "BipodFoot", new Vector3(side * 0.013f, 0.022f, 0.74f), new Vector3(0.016f, 0.01f, 0.014f), a.gunRubber);
        }
        // Handguard, 100-round ammo box with the belt feeding in, grip, guard, stock.
        b.Box(null, "Handguard", new Vector3(0f, 0.02f, 0.32f), new Vector3(0.05f, 0.042f, 0.14f), a.gunPolymer);
        b.Box(null, "AmmoBox", new Vector3(0f, -0.045f, 0.13f), new Vector3(0.075f, 0.11f, 0.11f), a.olive);
        b.Box(null, "AmmoBoxLid", new Vector3(0f, 0.012f, 0.13f), new Vector3(0.077f, 0.008f, 0.112f), a.olive);
        for (int i = 0; i < 4; i++)
            b.Cyl(null, "BeltRound", new Vector3(-0.04f + i * 0.004f, 0.03f + i * 0.012f, 0.12f + i * 0.005f), 0.004f, 0.03f, a.brass, Builder.Axis.Z);
        var bolt = b.Group("ChargingHandle", Vector3.zero);
        m.slide = bolt;
        b.Box(bolt, "Handle", new Vector3(0.034f, 0.06f, 0.16f), new Vector3(0.012f, 0.012f, 0.02f), a.gunDark);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.02f, 0.0f, 0.05f));
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.025f, -0.01f), new Vector3(0.012f, 0.005f, 0.06f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.01f, -0.005f), new Vector3(0.005f, 0.018f, 0.005f), a.gunMetal);
        var grip = b.Group("Grip", new Vector3(0f, -0.005f, -0.04f), new Vector3(16f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.045f, 0f), new Vector3(0.032f, 0.09f, 0.042f), a.gunPolymer);
        b.Box(null, "Stock", new Vector3(0f, 0.045f, -0.17f), new Vector3(0.04f, 0.07f, 0.24f), a.gunPolymer);
        b.Box(null, "StockLower", new Vector3(0f, 0.0f, -0.2f), new Vector3(0.03f, 0.03f, 0.18f), a.gunPolymer, new Vector3(-10f, 0f, 0f));
        b.Box(null, "ButtPad", new Vector3(0f, 0.035f, -0.295f), new Vector3(0.046f, 0.11f, 0.02f), a.gunRubber);
    }


    // ------------------------------------------------------------------ Cattleman: Colt Single Action Army, 5.5"

    static void BuildCattleman(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        // Frame and top strap with the rear-sight groove (two short ears, tops at the sight line 0.066).
        b.Box(null, "Frame", new Vector3(0f, 0.03f, 0.04f), new Vector3(0.03f, 0.045f, 0.09f), a.gunMetal);
        b.Box(null, "TopStrap", new Vector3(0f, 0.057f, 0.045f), new Vector3(0.014f, 0.008f, 0.07f), a.gunMetal);
        b.Box(null, "RearEarL", new Vector3(-0.0048f, 0.063f, 0.014f), new Vector3(0.0042f, 0.006f, 0.008f), a.gunMetal);
        b.Box(null, "RearEarR", new Vector3(0.0048f, 0.063f, 0.014f), new Vector3(0.0042f, 0.006f, 0.008f), a.gunMetal);
        // Unfluted six-shot cylinder (indexes each shot), loading gate.
        var cyl = b.Group("Cylinder", new Vector3(0f, 0.034f, 0.045f));
        m.cylinder = cyl;
        b.Cyl(cyl, "Drum", Vector3.zero, 0.019f, 0.042f, a.gunMetal, Builder.Axis.Z);
        for (int i = 0; i < 6; i++)
        {
            float ang = i * 60f * Mathf.Deg2Rad;
            b.Cyl(cyl, "Chamber", new Vector3(Mathf.Sin(ang), Mathf.Cos(ang), 0f) * 0.012f + new Vector3(0f, 0f, 0.0212f), 0.0045f, 0.001f, a.gunDark, Builder.Axis.Z);
        }
        b.Box(null, "LoadingGate", new Vector3(0.0158f, 0.03f, 0.03f), new Vector3(0.002f, 0.014f, 0.012f), a.gunMetal);
        // Barrel, ejector rod housing, blade front sight (top at 0.066).
        b.Cyl(null, "Barrel", new Vector3(0f, 0.046f, 0.155f), 0.0075f, 0.14f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "EjectorHousing", new Vector3(0.0065f, 0.034f, 0.135f), 0.0045f, 0.1f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "FrontBlade", new Vector3(0f, 0.06f, 0.218f), new Vector3(0.003f, 0.012f, 0.007f), a.gunMetal);
        b.Cyl(null, "Bore", new Vector3(0f, 0.046f, 0.2255f), 0.0045f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.046f, 0.228f));
        m.ejectionPort = null;
        // Hammer (kicks on each shot), brass guard, trigger, plow-handle grip.
        var hammer = b.Group("Hammer", Vector3.zero);
        m.slide = hammer;
        b.Box(hammer, "HammerBody", new Vector3(0f, 0.052f, -0.01f), new Vector3(0.007f, 0.022f, 0.011f), a.gunMetal, new Vector3(-25f, 0f, 0f));
        b.Box(hammer, "HammerSpur", new Vector3(0f, 0.063f, -0.021f), new Vector3(0.009f, 0.004f, 0.014f), a.gunMetal, new Vector3(-10f, 0f, 0f));
        b.Box(null, "GuardBottom", new Vector3(0f, -0.014f, 0.032f), new Vector3(0.008f, 0.004f, 0.04f), a.brass);
        b.Box(null, "GuardFront", new Vector3(0f, -0.002f, 0.052f), new Vector3(0.008f, 0.024f, 0.004f), a.brass, new Vector3(-15f, 0f, 0f));
        b.Box(null, "Trigger", new Vector3(0f, -0.001f, 0.032f), new Vector3(0.004f, 0.018f, 0.004f), a.gunMetal, new Vector3(-12f, 0f, 0f));
        var grip = b.Group("Grip", new Vector3(0f, 0.008f, -0.012f), new Vector3(15f, 0f, 0f));
        b.Box(grip, "Backstrap", new Vector3(0f, -0.04f, -0.014f), new Vector3(0.026f, 0.09f, 0.007f), a.brass);
        b.Box(grip, "GripWood", new Vector3(0f, -0.044f, 0.004f), new Vector3(0.031f, 0.086f, 0.034f), a.gunWood);
        b.Box(grip, "ButtCap", new Vector3(0f, -0.089f, 0.0f), new Vector3(0.03f, 0.006f, 0.04f), a.brass);
    }

    // ------------------------------------------------------------------ .44 Magnum: S&W Model 29, 6.5"

    static void BuildMagnum(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        b.Box(null, "Frame", new Vector3(0f, 0.03f, 0.04f), new Vector3(0.032f, 0.05f, 0.1f), a.gunMetal);
        b.Box(null, "TopStrap", new Vector3(0f, 0.061f, 0.05f), new Vector3(0.016f, 0.008f, 0.07f), a.gunMetal);
        // Adjustable rear sight: open notch between two posts, tops at the sight line 0.076.
        b.Box(null, "RearSightBase", new Vector3(0f, 0.067f, 0.01f), new Vector3(0.016f, 0.004f, 0.018f), a.gunDark);
        b.Box(null, "RearEarL", new Vector3(-0.0052f, 0.0725f, 0.004f), new Vector3(0.0056f, 0.007f, 0.006f), a.gunDark);
        b.Box(null, "RearEarR", new Vector3(0.0052f, 0.0725f, 0.004f), new Vector3(0.0056f, 0.007f, 0.006f), a.gunDark);
        // Fluted cylinder (indexes each shot) with the crane.
        var cyl = b.Group("Cylinder", new Vector3(0f, 0.034f, 0.05f));
        m.cylinder = cyl;
        b.Cyl(cyl, "Drum", Vector3.zero, 0.021f, 0.044f, a.gunMetal, Builder.Axis.Z);
        for (int i = 0; i < 6; i++)
        {
            float ang = i * 60f;
            var off = Quaternion.Euler(0f, 0f, ang + 30f) * Vector3.up * 0.0205f;
            b.Box(cyl, "Flute", off, new Vector3(0.006f, 0.003f, 0.03f), a.gunDark, new Vector3(0f, 0f, ang + 30f));
            float r = ang * Mathf.Deg2Rad;
            b.Cyl(cyl, "Chamber", new Vector3(Mathf.Sin(r), Mathf.Cos(r), 0f) * 0.0125f + new Vector3(0f, 0f, 0.0222f), 0.0048f, 0.001f, a.gunDark, Builder.Axis.Z);
        }
        // Heavy barrel: rib on top, full-length underlug, ramp front blade (top at 0.076).
        b.Cyl(null, "Barrel", new Vector3(0f, 0.047f, 0.185f), 0.009f, 0.17f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "Rib", new Vector3(0f, 0.0575f, 0.185f), new Vector3(0.009f, 0.004f, 0.17f), a.gunMetal);
        b.Box(null, "Underlug", new Vector3(0f, 0.035f, 0.185f), new Vector3(0.012f, 0.012f, 0.17f), a.gunMetal);
        b.Box(null, "FrontBlade", new Vector3(0f, 0.06775f, 0.262f), new Vector3(0.003f, 0.0165f, 0.012f), a.gunMetal);
        b.Cyl(null, "Bore", new Vector3(0f, 0.047f, 0.2705f), 0.0055f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.047f, 0.273f));
        m.ejectionPort = null;
        var hammer = b.Group("Hammer", Vector3.zero);
        m.slide = hammer;
        b.Box(hammer, "HammerSpur", new Vector3(0f, 0.058f, -0.012f), new Vector3(0.008f, 0.016f, 0.012f), a.gunMetal, new Vector3(-25f, 0f, 0f));
        b.Box(null, "GuardBottom", new Vector3(0f, -0.016f, 0.035f), new Vector3(0.009f, 0.005f, 0.05f), a.gunMetal);
        b.Box(null, "GuardFront", new Vector3(0f, -0.002f, 0.06f), new Vector3(0.009f, 0.028f, 0.005f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.002f, 0.036f), new Vector3(0.006f, 0.02f, 0.006f), a.gunMetal);
        var grip = b.Group("Grip", new Vector3(0f, 0.006f, -0.014f), new Vector3(15f, 0f, 0f));
        b.Box(grip, "GripWood", new Vector3(0f, -0.048f, 0.004f), new Vector3(0.034f, 0.098f, 0.044f), a.gunWood);
        for (int i = 0; i < 4; i++)
            b.Box(grip, "Checkering", new Vector3(0.0172f, -0.025f - i * 0.016f, 0.004f), new Vector3(0.001f, 0.009f, 0.03f), a.gunDark);
    }

    // ------------------------------------------------------------------ Lever action: Winchester 1894, .30-30

    static void BuildLeverAction(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var wood = a.gunWood;
        const float bore = 0.05f;
        b.Box(null, "Receiver", new Vector3(0f, 0.03f, 0.07f), new Vector3(0.034f, 0.06f, 0.16f), a.gunMetal);
        b.Box(null, "LoadingGate", new Vector3(0.0172f, 0.02f, 0.1f), new Vector3(0.002f, 0.014f, 0.03f), a.brass);
        b.Tube(null, "SaddleRing", new Vector3(-0.0185f, 0.035f, 0.01f), 0.009f, 0.007f, 0.003f, a.gunMetal, Builder.Axis.X);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0f, 0.066f, 0.08f));
        var hammer = b.Group("Hammer", Vector3.zero);
        m.slide = hammer;
        b.Box(hammer, "HammerBody", new Vector3(0f, 0.058f, -0.008f), new Vector3(0.006f, 0.016f, 0.01f), a.gunMetal, new Vector3(-20f, 0f, 0f));
        // Barrel, tube magazine, forend and bands.
        b.Cyl(null, "Barrel", new Vector3(0f, bore, 0.48f), 0.0095f, 0.66f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "MagTube", new Vector3(0f, 0.033f, 0.42f), 0.008f, 0.54f, a.gunMetal, Builder.Axis.Z);
        b.Box(null, "Forend", new Vector3(0f, 0.035f, 0.3f), new Vector3(0.034f, 0.034f, 0.24f), wood);
        b.Box(null, "ForendBand", new Vector3(0f, 0.042f, 0.42f), new Vector3(0.036f, 0.036f, 0.01f), a.gunMetal);
        b.Box(null, "MuzzleBand", new Vector3(0f, 0.041f, 0.775f), new Vector3(0.026f, 0.03f, 0.01f), a.gunMetal);
        b.Cyl(null, "Bore", new Vector3(0f, bore, 0.8105f), 0.004f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, bore, 0.813f));
        // Open rear notch on the barrel and a blade-and-bead front (tops at the sight line 0.0755).
        b.Box(null, "RearSightBase", new Vector3(0f, 0.0625f, 0.2f), new Vector3(0.022f, 0.006f, 0.012f), a.gunMetal);
        b.Box(null, "RearEarL", new Vector3(-0.0065f, 0.0705f, 0.2f), new Vector3(0.006f, 0.01f, 0.006f), a.gunMetal);
        b.Box(null, "RearEarR", new Vector3(0.0065f, 0.0705f, 0.2f), new Vector3(0.006f, 0.01f, 0.006f), a.gunMetal);
        b.Box(null, "FrontBlade", new Vector3(0f, 0.0675f, 0.79f), new Vector3(0.003f, 0.016f, 0.008f), a.gunMetal);
        b.Sphere(null, "FrontBead", new Vector3(0f, 0.0735f, 0.7858f), 0.0018f, a.brass);
        // Stock.
        b.Box(null, "Wrist", new Vector3(0f, 0.015f, -0.06f), new Vector3(0.03f, 0.042f, 0.12f), wood, new Vector3(-6f, 0f, 0f));
        b.Box(null, "Butt", new Vector3(0f, -0.02f, -0.26f), new Vector3(0.04f, 0.105f, 0.3f), wood, new Vector3(-5f, 0f, 0f));
        b.Box(null, "Buttplate", new Vector3(0f, -0.032f, -0.412f), new Vector3(0.042f, 0.12f, 0.01f), a.gunMetal, new Vector3(-5f, 0f, 0f));
        // The lever: pivots under the front of the receiver, finger loop behind the trigger.
        var lever = b.Group("Lever", new Vector3(0f, 0.0f, 0.12f));
        m.lever = lever;
        b.Box(lever, "LeverBar", new Vector3(0f, -0.004f, -0.06f), new Vector3(0.008f, 0.007f, 0.12f), a.gunMetal);
        b.Box(lever, "LoopFront", new Vector3(0f, -0.034f, -0.035f), new Vector3(0.008f, 0.055f, 0.007f), a.gunMetal);
        b.Box(lever, "LoopBottom", new Vector3(0f, -0.062f, -0.075f), new Vector3(0.008f, 0.007f, 0.085f), a.gunMetal);
        b.Box(lever, "LoopRear", new Vector3(0f, -0.034f, -0.117f), new Vector3(0.008f, 0.06f, 0.007f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.012f, 0.03f), new Vector3(0.004f, 0.018f, 0.004f), a.gunMetal, new Vector3(-10f, 0f, 0f));
    }

    // ------------------------------------------------------------------ M1911A1 (.45 ACP, 5" Government)

    static void BuildM1911(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var slide = b.Group("Slide", Vector3.zero);
        m.slide = slide;
        b.Box(slide, "SlideBody", new Vector3(0f, 0.033f, 0.04f), new Vector3(0.024f, 0.028f, 0.216f), a.gunMetal);
        for (int i = 0; i < 9; i++)
            b.Box(slide, "Serration", new Vector3(0f, 0.033f, -0.062f + i * 0.004f), new Vector3(0.0245f, 0.022f, 0.0016f), a.gunDark);
        b.Box(slide, "EjectionPort", new Vector3(0.0121f, 0.04f, 0.045f), new Vector3(0.001f, 0.012f, 0.03f), a.gunDark);
        b.Cyl(slide, "Bushing", new Vector3(0f, 0.031f, 0.149f), 0.009f, 0.006f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(slide, "Bore", new Vector3(0f, 0.031f, 0.1525f), 0.0055f, 0.002f, a.gunDark, Builder.Axis.Z);
        // Tall sights: front blade and two-post rear, tops at the sight line 0.064.
        b.Box(slide, "FrontBlade", new Vector3(0f, 0.0555f, 0.138f), new Vector3(0.0035f, 0.017f, 0.007f), a.gunDark);
        b.Box(slide, "FrontDot", new Vector3(0f, 0.0615f, 0.1343f), new Vector3(0.0025f, 0.0025f, 0.001f), a.sightWhite);
        b.Box(slide, "RearSightBase", new Vector3(0f, 0.0495f, -0.06f), new Vector3(0.02f, 0.005f, 0.008f), a.gunDark);
        b.Box(slide, "RearEarL", new Vector3(-0.0065f, 0.0575f, -0.06f), new Vector3(0.006f, 0.013f, 0.008f), a.gunDark);
        b.Box(slide, "RearEarR", new Vector3(0.0065f, 0.0575f, -0.06f), new Vector3(0.006f, 0.013f, 0.008f), a.gunDark);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.014f, 0.04f, 0.045f));
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.031f, 0.155f));
        // Frame: dust cover, guard, flat trigger, slide stop, thumb safety, hammer, grip safety.
        b.Box(null, "Frame", new Vector3(0f, 0.011f, 0.03f), new Vector3(0.025f, 0.016f, 0.17f), a.gunMetal);
        b.Box(null, "GuardBottom", new Vector3(0f, -0.022f, 0.033f), new Vector3(0.008f, 0.004f, 0.048f), a.gunMetal);
        b.Box(null, "GuardFront", new Vector3(0f, -0.01f, 0.058f), new Vector3(0.008f, 0.026f, 0.005f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.006f, 0.028f), new Vector3(0.006f, 0.016f, 0.004f), a.gunMetal);
        b.Box(null, "SlideStop", new Vector3(-0.0135f, 0.018f, 0.02f), new Vector3(0.002f, 0.005f, 0.022f), a.gunDark);
        b.Box(null, "ThumbSafety", new Vector3(-0.0135f, 0.022f, -0.04f), new Vector3(0.002f, 0.004f, 0.016f), a.gunDark);
        b.Box(null, "Hammer", new Vector3(0f, 0.038f, -0.074f), new Vector3(0.008f, 0.016f, 0.01f), a.gunMetal, new Vector3(-25f, 0f, 0f));
        var grip = b.Group("Grip", new Vector3(0f, 0.003f, -0.02f), new Vector3(17f, 0f, 0f));
        b.Box(grip, "GripFrame", new Vector3(0f, -0.055f, 0f), new Vector3(0.026f, 0.11f, 0.05f), a.gunMetal);
        b.Box(grip, "GripSafety", new Vector3(0f, -0.005f, -0.03f), new Vector3(0.022f, 0.022f, 0.012f), a.gunMetal);
        b.Box(grip, "PanelL", new Vector3(-0.0138f, -0.055f, 0.003f), new Vector3(0.003f, 0.085f, 0.04f), a.gunWood);
        b.Box(grip, "PanelR", new Vector3(0.0138f, -0.055f, 0.003f), new Vector3(0.003f, 0.085f, 0.04f), a.gunWood);
        b.Box(grip, "MagBase", new Vector3(0f, -0.112f, 0.002f), new Vector3(0.024f, 0.006f, 0.046f), a.gunDark);
    }

    // ------------------------------------------------------------------ Glock with a switch (auto sear) and a 33-round mag

    static void BuildGlockSwitch(WeaponModel m, Builder b)
    {
        BuildGlock(m, b);
        var a = GameAssets.I;
        var slide = m.transform.Find("Slide");
        var grip = m.transform.Find("Grip");
        // The switch: a little selector block on the back of the slide.
        b.Box(slide, "Switch", new Vector3(0f, 0.034f, -0.0565f), new Vector3(0.02f, 0.02f, 0.007f), a.gunDark);
        b.Box(slide, "SwitchLever", new Vector3(0.011f, 0.04f, -0.0565f), new Vector3(0.004f, 0.004f, 0.012f), a.gunMetal);
        // Extended 33-round magazine sticking out of the grip.
        b.Box(grip, "ExtendedMag", new Vector3(0f, -0.15f, 0.004f), new Vector3(0.024f, 0.085f, 0.036f), a.gunPolymer);
        b.Box(grip, "ExtendedMagBase", new Vector3(0f, -0.195f, 0.004f), new Vector3(0.028f, 0.007f, 0.042f), a.gunPolymer);
    }

    // ------------------------------------------------------------------ KRISS Vector (.45 ACP, Super V, red dot)

    static void BuildVector(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        // Upper receiver with a full-length top rail.
        b.Box(null, "Upper", new Vector3(0f, 0.05f, 0.07f), new Vector3(0.034f, 0.045f, 0.24f), a.gunPolymer);
        b.Box(null, "TopRail", new Vector3(0f, 0.078f, 0.06f), new Vector3(0.022f, 0.008f, 0.26f), a.gunMetal);
        for (int i = 0; i < 14; i++)
            b.Box(null, "RailTooth", new Vector3(0f, 0.0835f, -0.06f + i * 0.0175f), new Vector3(0.022f, 0.003f, 0.007f), a.gunMetal);
        // The Vector's angular lower: the Super V housing sweeps down in front of the trigger.
        b.Box(null, "Lower", new Vector3(0f, 0.016f, 0.07f), new Vector3(0.032f, 0.04f, 0.2f), a.gunPolymer);
        b.Box(null, "SuperVHousing", new Vector3(0f, -0.015f, 0.165f), new Vector3(0.03f, 0.055f, 0.085f), a.gunPolymer, new Vector3(25f, 0f, 0f));
        b.Box(null, "SuperVFace", new Vector3(0f, -0.035f, 0.2f), new Vector3(0.031f, 0.04f, 0.012f), a.gunDark, new Vector3(25f, 0f, 0f));
        // Magazine forward of the grip (Glock 21 style, 25 rounds).
        var mag = b.Group("Magazine", new Vector3(0f, -0.004f, 0.105f), new Vector3(-6f, 0f, 0f));
        b.Box(mag, "Mag", new Vector3(0f, -0.06f, 0f), new Vector3(0.022f, 0.115f, 0.03f), a.gunDark);
        b.Box(mag, "MagBase", new Vector3(0f, -0.119f, 0f), new Vector3(0.026f, 0.007f, 0.034f), a.gunPolymer);
        // Grip, big angular trigger guard, trigger.
        var grip = b.Group("Grip", new Vector3(0f, -0.004f, -0.02f), new Vector3(18f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.045f, 0f), new Vector3(0.03f, 0.09f, 0.04f), a.gunPolymer);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.034f, 0.025f), new Vector3(0.012f, 0.005f, 0.065f), a.gunPolymer);
        b.Box(null, "Trigger", new Vector3(0f, -0.016f, 0.03f), new Vector3(0.005f, 0.018f, 0.005f), a.gunMetal);
        // Barrel shroud and short barrel.
        b.Box(null, "Shroud", new Vector3(0f, 0.042f, 0.24f), new Vector3(0.03f, 0.03f, 0.1f), a.gunPolymer);
        b.Cyl(null, "Barrel", new Vector3(0f, 0.042f, 0.31f), 0.008f, 0.05f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(null, "Bore", new Vector3(0f, 0.042f, 0.3355f), 0.0055f, 0.002f, a.gunDark, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.042f, 0.338f));
        // Charging handle (kicks back), ejection port, folding stock.
        var ch = b.Group("ChargingHandle", Vector3.zero);
        m.slide = ch;
        b.Box(ch, "Handle", new Vector3(-0.02f, 0.056f, 0.1f), new Vector3(0.008f, 0.01f, 0.02f), a.gunDark);
        b.Box(null, "EjectionPort", new Vector3(0.0172f, 0.05f, 0.05f), new Vector3(0.001f, 0.014f, 0.03f), a.gunDark);
        m.ejectionPort = b.Group("EjectPoint", new Vector3(0.022f, 0.05f, 0.05f));
        b.Box(null, "StockArm", new Vector3(0f, 0.05f, -0.14f), new Vector3(0.022f, 0.03f, 0.18f), a.gunPolymer);
        b.Box(null, "ButtPad", new Vector3(0f, 0.035f, -0.235f), new Vector3(0.03f, 0.09f, 0.016f), a.gunRubber);
        // Folded-flat backup irons (below the optic) and the red dot on the rail.
        b.Box(null, "RearBUISFolded", new Vector3(0f, 0.0855f, -0.04f), new Vector3(0.016f, 0.004f, 0.012f), a.gunDark);
        b.Box(null, "FrontBUISFolded", new Vector3(0f, 0.0855f, 0.175f), new Vector3(0.012f, 0.004f, 0.012f), a.gunDark);
        RedDot(b, new Vector3(0f, 0.108f, 0.05f), 0.082f, 0.07f);
    }

    // ------------------------------------------------------------------ MP-18: single-shot break-action rifle (.308)

    static void BuildMP18(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var wood = a.gunWood;
        // Action block, exposed hammer (kicks on the shot), guard with the opening lever.
        b.Box(null, "Action", new Vector3(0f, 0.03f, 0.03f), new Vector3(0.036f, 0.055f, 0.09f), a.gunMetal);
        var hammer = b.Group("Hammer", Vector3.zero);
        m.slide = hammer;
        b.Box(hammer, "HammerSpur", new Vector3(0f, 0.056f, -0.01f), new Vector3(0.007f, 0.012f, 0.012f), a.gunMetal, new Vector3(-25f, 0f, 0f));
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.022f, 0.03f), new Vector3(0.01f, 0.005f, 0.06f), a.gunMetal);
        b.Box(null, "OpeningLever", new Vector3(0f, -0.026f, 0.064f), new Vector3(0.006f, 0.012f, 0.012f), a.gunMetal);
        b.Box(null, "Trigger", new Vector3(0f, -0.008f, 0.03f), new Vector3(0.004f, 0.018f, 0.004f), a.gunMetal, new Vector3(-10f, 0f, 0f));
        // Barrel, forend and sights, all on the hinged group (pivot at the front-bottom of the action).
        var brk = b.Group("BreakAction", new Vector3(0f, 0.012f, 0.075f));
        m.breakAction = brk;
        b.Cyl(brk, "Barrel", new Vector3(0f, 0.034f, 0.33f), 0.011f, 0.66f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(brk, "Crown", new Vector3(0f, 0.034f, 0.662f), 0.012f, 0.006f, a.gunMetal, Builder.Axis.Z);
        b.Cyl(brk, "Bore", new Vector3(0f, 0.034f, 0.6655f), 0.0045f, 0.002f, a.gunDark, Builder.Axis.Z);
        b.Box(brk, "Lug", new Vector3(0f, 0.002f, 0.012f), new Vector3(0.02f, 0.02f, 0.03f), a.gunMetal);
        b.Box(brk, "Forend", new Vector3(0f, 0.012f, 0.17f), new Vector3(0.036f, 0.034f, 0.22f), wood);
        // Open notch rear and hooded post front, tops at the sight line (0.072 in weapon space).
        b.Box(brk, "RearSightBase", new Vector3(0f, 0.0485f, 0.125f), new Vector3(0.022f, 0.007f, 0.012f), a.gunMetal);
        b.Box(brk, "RearEarL", new Vector3(-0.0065f, 0.055f, 0.125f), new Vector3(0.006f, 0.01f, 0.006f), a.gunMetal);
        b.Box(brk, "RearEarR", new Vector3(0.0065f, 0.055f, 0.125f), new Vector3(0.006f, 0.01f, 0.006f), a.gunMetal);
        b.Box(brk, "FrontPost", new Vector3(0f, 0.0525f, 0.645f), new Vector3(0.003f, 0.015f, 0.006f), a.gunMetal);
        b.Tube(brk, "FrontHood", new Vector3(0f, 0.06f, 0.645f), 0.011f, 0.0095f, 0.016f, a.gunMetal, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.046f, 0.745f));
        m.ejectionPort = null; // the empty comes out when you break it open to reload
        // Monte Carlo stock with a recoil pad.
        b.Box(null, "Wrist", new Vector3(0f, 0.012f, -0.06f), new Vector3(0.03f, 0.044f, 0.12f), wood, new Vector3(-8f, 0f, 0f));
        b.Box(null, "Butt", new Vector3(0f, -0.02f, -0.27f), new Vector3(0.042f, 0.11f, 0.32f), wood, new Vector3(-5f, 0f, 0f));
        b.Box(null, "Comb", new Vector3(0f, 0.04f, -0.25f), new Vector3(0.036f, 0.02f, 0.26f), wood, new Vector3(-1f, 0f, 0f));
        b.Box(null, "RecoilPad", new Vector3(0f, -0.032f, -0.432f), new Vector3(0.044f, 0.125f, 0.02f), a.gunRubber, new Vector3(-5f, 0f, 0f));
    }

    // ------------------------------------------------------------------ Grappling hook launcher (pickup)

    static void BuildGrappleHook(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var grip = b.Group("Grip", new Vector3(0f, 0f, 0f), new Vector3(15f, 0f, 0f));
        b.Box(grip, "GripBody", new Vector3(0f, -0.045f, 0f), new Vector3(0.03f, 0.09f, 0.04f), a.gunPolymer);
        b.Cyl(null, "Barrel", new Vector3(0f, 0.035f, 0.1f), 0.022f, 0.22f, a.olive, Builder.Axis.Z);
        b.Cyl(null, "Spool", new Vector3(0f, -0.01f, 0.1f), 0.03f, 0.05f, a.gunMetal, Builder.Axis.X);
        b.Cyl(null, "RopeCoil", new Vector3(0f, -0.01f, 0.1f), 0.032f, 0.03f, a.sightWhite, Builder.Axis.X);
        b.Box(null, "TriggerGuard", new Vector3(0f, -0.02f, 0.02f), new Vector3(0.01f, 0.005f, 0.05f), a.gunMetal);
        // Three-prong hook sitting in the muzzle.
        b.Cyl(null, "HookShank", new Vector3(0f, 0.035f, 0.24f), 0.008f, 0.06f, a.gunMetal, Builder.Axis.Z);
        for (int i = 0; i < 3; i++)
        {
            var rot = Quaternion.Euler(0f, 0f, i * 120f);
            b.Box(null, "Prong", new Vector3(0f, 0.035f, 0.265f) + rot * new Vector3(0f, 0.018f, 0f), new Vector3(0.006f, 0.032f, 0.006f), a.gunMetal, new Vector3(-35f, 0f, i * 120f));
        }
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.035f, 0.28f));
    }

    // ------------------------------------------------------------------ M67 fragmentation grenade

    static void BuildFrag(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        b.Sphere(null, "Body", Vector3.zero, 0.032f, a.olive);
        b.Cyl(null, "Fuze", new Vector3(0f, 0.036f, 0f), 0.011f, 0.024f, a.gunMetal, Builder.Axis.Y);
        b.Box(null, "Spoon", new Vector3(0.012f, 0.02f, 0f), new Vector3(0.006f, 0.05f, 0.014f), a.gunMetal, new Vector3(0f, 0f, -12f));
        b.Cyl(null, "PinRing", new Vector3(-0.016f, 0.05f, 0f), 0.012f, 0.002f, a.gunMetal, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", Vector3.zero);
    }

    static void BuildSmoke(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var can = b.Cyl(null, "Canister", Vector3.zero, 0.03f, 0.11f, a.olive, Builder.Axis.Y);
        Builder.Tint(can, new Color(0.55f, 0.58f, 0.6f));
        var band = b.Cyl(null, "Band", new Vector3(0f, 0.035f, 0f), 0.031f, 0.014f, a.sightWhite, Builder.Axis.Y);
        Builder.Tint(band, new Color(0.3f, 0.75f, 0.35f));
        b.Cyl(null, "Fuze", new Vector3(0f, 0.065f, 0f), 0.011f, 0.022f, a.gunMetal, Builder.Axis.Y);
        b.Box(null, "Spoon", new Vector3(0.016f, 0.04f, 0f), new Vector3(0.006f, 0.06f, 0.014f), a.gunMetal, new Vector3(0f, 0f, -8f));
        b.Cyl(null, "PinRing", new Vector3(-0.016f, 0.078f, 0f), 0.012f, 0.002f, a.gunMetal, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", Vector3.zero);
    }

    static void BuildRopeGrenade(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var body = b.Cyl(null, "Body", Vector3.zero, 0.032f, 0.09f, a.gunMetal, Builder.Axis.Y);
        Builder.Tint(body, new Color(0.25f, 0.22f, 0.2f));
        // Coils of rope wound around it.
        for (int i = 0; i < 5; i++)
        {
            var coil = b.Cyl(null, "Coil", new Vector3(0f, -0.032f + i * 0.016f, 0f), 0.037f, 0.011f, a.gunWood, Builder.Axis.Y);
            Builder.Tint(coil, new Color(0.78f, 0.62f, 0.38f));
        }
        b.Cyl(null, "Fuze", new Vector3(0f, 0.055f, 0f), 0.011f, 0.022f, a.gunMetal, Builder.Axis.Y);
        b.Cyl(null, "PinRing", new Vector3(-0.016f, 0.068f, 0f), 0.012f, 0.002f, a.gunMetal, Builder.Axis.Z);
        m.muzzle = b.Group("Muzzle", Vector3.zero);
    }

    static void BuildMolotov(WeaponModel m, Builder b)
    {
        var a = GameAssets.I;
        var glass = new Color(0.25f, 0.45f, 0.2f);
        Builder.Tint(b.Cyl(null, "Bottle", Vector3.zero, 0.036f, 0.13f, a.gunPolymer, Builder.Axis.Y), glass);
        Builder.Tint(b.Sphere(null, "Shoulder", new Vector3(0f, 0.065f, 0f), 0.036f, a.gunPolymer), glass);
        Builder.Tint(b.Cyl(null, "Neck", new Vector3(0f, 0.11f, 0f), 0.013f, 0.06f, a.gunPolymer, Builder.Axis.Y), glass);
        // Petrol inside, and a rag stuffed in the neck.
        Builder.Tint(b.Cyl(null, "Petrol", new Vector3(0f, -0.02f, 0f), 0.033f, 0.08f, a.sightWhite, Builder.Axis.Y), new Color(0.8f, 0.6f, 0.2f));
        Builder.Tint(b.Box(null, "Rag", new Vector3(0f, 0.15f, 0f), new Vector3(0.02f, 0.05f, 0.02f), a.sightWhite, new Vector3(0f, 0f, 12f)), new Color(0.85f, 0.8f, 0.68f));
        m.muzzle = b.Group("Muzzle", new Vector3(0f, 0.17f, 0f));
    }

    // ------------------------------------------------------------------ primitive helpers

    public class Builder
    {
        public enum Axis { X, Y, Z }
        readonly Transform root;

        public Builder(Transform root) => this.root = root;

        public static void Tint(Transform t, Color c)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", c);
            t.GetComponent<Renderer>().SetPropertyBlock(block);
        }

        public Transform Group(string name, Vector3 pos, Vector3 euler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }

        public Transform Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, Vector3 euler = default)
            => Make(PrimitiveType.Cube, parent, name, center, size, Quaternion.Euler(euler), mat);

        public Transform Sphere(Transform parent, string name, Vector3 center, float radius, Material mat)
            => Make(PrimitiveType.Sphere, parent, name, center, Vector3.one * radius * 2f, Quaternion.identity, mat);

        public Transform Cyl(Transform parent, string name, Vector3 center, float radius, float length, Material mat, Axis axis)
        {
            var rot = axis switch
            {
                Axis.X => Quaternion.Euler(0f, 0f, 90f),
                Axis.Z => Quaternion.Euler(90f, 0f, 0f),
                _ => Quaternion.identity,
            };
            return Make(PrimitiveType.Cylinder, parent, name, center, new Vector3(radius * 2f, length * 0.5f, radius * 2f), rot, mat);
        }

        /// <summary>Open-ended round tube (hollow cylinder) with the given outer and inner radius.</summary>
        public Transform Tube(Transform parent, string name, Vector3 center, float outerRadius, float innerRadius, float length, Material mat, Axis axis)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = TubeMesh(innerRadius / outerRadius);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var t = go.transform;
            t.SetParent(parent != null ? parent : root, false);
            t.localPosition = center;
            t.localRotation = axis switch
            {
                Axis.X => Quaternion.Euler(0f, 0f, 90f),
                Axis.Z => Quaternion.Euler(90f, 0f, 0f),
                _ => Quaternion.identity,
            };
            t.localScale = new Vector3(outerRadius * 2f, length, outerRadius * 2f);
            return t;
        }

        static readonly System.Collections.Generic.Dictionary<int, Mesh> tubes = new();

        /// <summary>Unit tube along Y (length 1, outer radius 0.5): outer and inner walls plus both end rings.</summary>
        static Mesh TubeMesh(float innerRatio)
        {
            int key = Mathf.RoundToInt(innerRatio * 200f);
            if (tubes.TryGetValue(key, out var cached) && cached != null) return cached;
            const int n = 28;
            float ro = 0.5f, ri = 0.5f * innerRatio;
            var v = new System.Collections.Generic.List<Vector3>();
            var tri = new System.Collections.Generic.List<int>();
            Vector3 P(int k, float r, float y) { float ang = (k % n) / (float)n * Mathf.PI * 2f; return new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r); }
            int Ring(float r, float y) { int s = v.Count; for (int k = 0; k < n; k++) v.Add(P(k, r, y)); return s; }
            // Walls (vertices shared around the ring so they shade smoothly).
            int ob = Ring(ro, -0.5f), ot = Ring(ro, 0.5f), ib = Ring(ri, -0.5f), it = Ring(ri, 0.5f);
            // End rings get their own vertices (flat normals).
            int tb = Ring(ro, 0.5f), ti = Ring(ri, 0.5f), bb = Ring(ro, -0.5f), bi = Ring(ri, -0.5f);
            for (int k = 0; k < n; k++)
            {
                int k1 = (k + 1) % n;
                tri.AddRange(new[] { ob + k, ot + k, ob + k1, ob + k1, ot + k, ot + k1 });   // outer wall, facing out
                tri.AddRange(new[] { ib + k, ib + k1, it + k, ib + k1, it + k1, it + k });   // inner wall, facing in
                tri.AddRange(new[] { ti + k, tb + k1, tb + k, ti + k, ti + k1, tb + k1 });   // top ring
                tri.AddRange(new[] { bi + k, bb + k, bb + k1, bi + k, bb + k1, bi + k1 });   // bottom ring
            }
            var mesh = new Mesh { name = "Tube" };
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            tubes[key] = mesh;
            return mesh;
        }

        Transform Make(PrimitiveType type, Transform parent, string name, Vector3 center, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            var t = go.transform;
            t.SetParent(parent != null ? parent : root, false);
            t.localPosition = center;
            t.localRotation = rot;
            t.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }
    }
}
