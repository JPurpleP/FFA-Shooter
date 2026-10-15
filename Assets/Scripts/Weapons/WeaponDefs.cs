using UnityEngine;

public enum WeaponId
{
    None = -1,
    Glock17 = 0,
    MP5 = 1,
    M4A1 = 2,
    Remington870 = 3,
    BarrettM82 = 4,
    GoldenDeagle = 5,
    M32 = 6,
    RPG7 = 7,
    FragGrenade = 8, // throwable (slot 3)
    AA12 = 9,
    Mosin = 10,
    PPSh41 = 11,
    M249 = 12,
    Cattleman = 13,
    Magnum44 = 14,
    LeverAction = 15,
    M1911 = 16,
    GlockSwitch = 17, // full-auto Glock with a switch, held sideways
    KrissVector = 18,
    MP18 = 19,        // single-shot break-action rifle
    Fists = 20,
    GrappleHook = 21, // pickup only: gives the grappling hook (kept until you die)
    SmokeGrenade = 22, // throwable (slot 3): thick smoke cloud
    RopeGrenade = 23,  // throwable (slot 3): snares everyone nearby in ropes
    Molotov = 24,      // throwable (slot 3): bursts into a pool of fire on impact
}

/// <summary>Static stats for one weapon type. Distances in meters, angles in degrees, times in seconds.</summary>
public class WeaponDef
{
    public WeaponId Id;
    public string Name;
    public string Caliber;

    // Damage
    public int Damage;                 // per bullet / pellet
    public int Pellets = 1;
    public float HeadMultiplier = 2f;
    public float FalloffStart = 1000f; // full damage until here...
    public float FalloffEnd = 1000f;   // ...then linearly down to MinDamageScale here
    public float MinDamageScale = 1f;
    public float CloseBonus = 1f;      // extra damage multiplier at point blank...
    public float CloseRange = 0f;      // ...up to this distance, easing back to 1 by FalloffStart
    public float Range = 300f;

    // Handling
    public bool Automatic;
    public float FireInterval;
    public int MagSize;
    public float ReloadTime;           // full reload, or per round when ReloadPerRound
    public bool ReloadPerRound;
    public float HipSpread;
    public float AdsSpread;
    public float BloomPerShot = 0.3f;
    public float RecoilKick;           // degrees of pitch per shot
    public float AdsFov = 55f;
    public bool HasScope;
    public float MoveSpeedScale = 1f;

    // Gore: how violently a kill with this weapon throws and tears the body apart
    public float RagdollImpulse;
    public float RagdollLift;
    public float RagdollSpin;
    public int DismemberPower;         // 0 none, 1 headshots can pop heads, 2 limbs fly at close range, 3 everything goes

    // View model
    public Vector3 HipOffset;          // weapon origin (grip) relative to the head
    // ADS rules (every gun, including new ones): SightHeight is the exact aim point at x = 0 — front post / blade tip,
    // bead centre, red-dot tube centre or scope axis — so it lands on the screen centre where shots go. Rear notch tops
    // sit level with the post tip, rings are centred on the line, nothing crosses the line, and sights are open
    // (U-notch, two-post, ghost ring) or an optic so the target stays visible. Verify with -ffaaim -ffacenterdot.
    public float SightHeight;          // sight line height above the grip origin
    public float AdsDistance;          // how far in front of the eye the grip sits when aiming
    public Vector3 LeftHand;           // support hand position in weapon space
    public bool OneHanded;             // no support hand (the sideways switch Glock)
    public float CantDegrees;          // weapon rolled about the sight line (sideways grip); sights stay on the centre
    public Color PickupGlow;
    public float SoundPitch = 1f;
    public float TracerSpeed = 400f;   // visual speed of the round (m/s), slowed from real so you can see it fly

    // Explosives
    public ProjectileKind Projectile;
    public float ProjectileSpeed;
    public float ProjectileGravity = 1f;
    public float ExplosionRadius;
    public float ExplosionDamage;
    public bool IsThrowable;

    public int SpareMags = 3;
    public int SpareAmmo => MagSize * SpareMags;

    public float DamageScaleAt(float distance)
    {
        if (distance <= CloseRange) return CloseBonus;
        if (distance < FalloffStart)
            return CloseRange > 0f ? Mathf.Lerp(CloseBonus, 1f, (distance - CloseRange) / (FalloffStart - CloseRange)) : 1f;
        if (distance >= FalloffEnd) return MinDamageScale;
        return Mathf.Lerp(1f, MinDamageScale, (distance - FalloffStart) / (FalloffEnd - FalloffStart));
    }
}

public static class WeaponDefs
{
    /// <summary>Number of real weapons (ids 0..Count-1). Fists come after and are not a pickup.</summary>
    public const int Count = 20;
    public const float FragFuse = 3.2f;

    // Throwables, carried in slot 3. Index = throwable type in the inventory.
    public static readonly WeaponId[] Throwables = { WeaponId.FragGrenade, WeaponId.SmokeGrenade, WeaponId.RopeGrenade, WeaponId.Molotov };
    public static readonly int[] MaxCarry = { 4, 2, 2, 2 };
    public static int ThrowableIndex(WeaponId id) => System.Array.IndexOf(Throwables, id);
    public const float SmokeFuse = 1.6f, SmokeSeconds = 16f, SmokeRadius = 5.5f;
    public const float RopeFuse = 1.3f, RopeRadius = 15f, SnareSeconds = 10f;
    public const float MolotovMaxFlight = 6f, FireRadius = 3.6f, FireSeconds = 8f, FireDps = 24f;

    public static readonly WeaponDef[] All =
    {
        new WeaponDef
        {
            Id = WeaponId.Glock17, Name = "Glock 17", Caliber = "9×19mm",
            Damage = 22, FalloffStart = 15f, FalloffEnd = 40f, MinDamageScale = 0.7f,
            FireInterval = 0.13f, MagSize = 17, ReloadTime = 1.5f,
            HipSpread = 1.4f, AdsSpread = 0.4f, BloomPerShot = 0.35f, RecoilKick = 0.35f, AdsFov = 62f,
            MoveSpeedScale = 1.05f,
            RagdollImpulse = 70f, RagdollLift = 0.15f, RagdollSpin = 12f, DismemberPower = 0,
            HipOffset = new Vector3(0.13f, -0.14f, 0.42f), SightHeight = 0.064f, AdsDistance = 0.42f,
            LeftHand = new Vector3(-0.015f, -0.03f, 0.0f), PickupGlow = new Color(0.9f, 0.9f, 0.9f), SoundPitch = 1.15f, TracerSpeed = 300f,
        },
        new WeaponDef
        {
            Id = WeaponId.MP5, Name = "MP5", Caliber = "9×19mm",
            Damage = 20, FalloffStart = 18f, FalloffEnd = 50f, MinDamageScale = 0.6f,
            Automatic = true, FireInterval = 0.075f, MagSize = 30, ReloadTime = 2.2f,
            HipSpread = 2.2f, AdsSpread = 0.7f, BloomPerShot = 0.22f, RecoilKick = 0.25f, AdsFov = 58f,
            RagdollImpulse = 85f, RagdollLift = 0.15f, RagdollSpin = 15f, DismemberPower = 0,
            HipOffset = new Vector3(0.17f, -0.2f, 0.3f), SightHeight = 0.086f, AdsDistance = 0.22f,
            LeftHand = new Vector3(0f, 0.005f, 0.29f), PickupGlow = new Color(0.3f, 1f, 0.4f), SoundPitch = 1.25f, TracerSpeed = 320f,
        },
        new WeaponDef
        {
            Id = WeaponId.M4A1, Name = "M4A1", Caliber = "5.56×45mm",
            Damage = 28, FalloffStart = 40f, FalloffEnd = 90f, MinDamageScale = 0.8f,
            Automatic = true, FireInterval = 0.085f, MagSize = 30, ReloadTime = 2.3f,
            HipSpread = 2.4f, AdsSpread = 0.3f, BloomPerShot = 0.25f, RecoilKick = 0.35f, AdsFov = 50f,
            RagdollImpulse = 140f, RagdollLift = 0.2f, RagdollSpin = 25f, DismemberPower = 1,
            HipOffset = new Vector3(0.17f, -0.2f, 0.26f), SightHeight = 0.112f, AdsDistance = 0.16f,
            LeftHand = new Vector3(0f, 0.02f, 0.26f), PickupGlow = new Color(0.3f, 0.6f, 1f), SoundPitch = 1f, TracerSpeed = 520f,
        },
        new WeaponDef
        {
            Id = WeaponId.Remington870, Name = "Remington 870", Caliber = "12 gauge",
            Damage = 19, Pellets = 9, HeadMultiplier = 1.5f, CloseBonus = 1.6f, CloseRange = 4f,
            FalloffStart = 9f, FalloffEnd = 30f, MinDamageScale = 0.3f, Range = 60f,
            FireInterval = 0.9f, MagSize = 6, ReloadTime = 0.5f, ReloadPerRound = true,
            HipSpread = 4.5f, AdsSpread = 3.8f, BloomPerShot = 0f, RecoilKick = 9f, AdsFov = 60f,
            RagdollImpulse = 380f, RagdollLift = 0.45f, RagdollSpin = 70f, DismemberPower = 2,
            HipOffset = new Vector3(0.17f, -0.2f, 0.24f), SightHeight = 0.1f, AdsDistance = 0.2f,
            LeftHand = new Vector3(0f, 0.01f, 0.33f), PickupGlow = new Color(1f, 0.55f, 0.15f), SoundPitch = 0.8f, TracerSpeed = 260f,
        },
        new WeaponDef
        {
            Id = WeaponId.BarrettM82, Name = "Barrett M82A1", Caliber = ".50 BMG",
            Damage = 180, HeadMultiplier = 2f, Range = 500f,
            FireInterval = 0.6f, MagSize = 10, ReloadTime = 3.5f,
            HipSpread = 7f, AdsSpread = 0f, BloomPerShot = 0f, RecoilKick = 18f, AdsFov = 12f, HasScope = true,
            MoveSpeedScale = 0.8f,
            RagdollImpulse = 700f, RagdollLift = 0.5f, RagdollSpin = 140f, DismemberPower = 3,
            HipOffset = new Vector3(0.16f, -0.2f, 0.12f), SightHeight = 0.155f, AdsDistance = 0.12f,
            LeftHand = new Vector3(0f, -0.04f, -0.2f), PickupGlow = new Color(1f, 0.25f, 0.9f), SoundPitch = 0.55f, TracerSpeed = 800f,
        },
        new WeaponDef
        {
            Id = WeaponId.GoldenDeagle, Name = "Golden Deagle", Caliber = ".50 AE",
            Damage = 70, HeadMultiplier = 2f, FalloffStart = 30f, FalloffEnd = 70f, MinDamageScale = 0.75f,
            FireInterval = 0.38f, MagSize = 7, SpareMags = 1, ReloadTime = 1.9f,
            HipSpread = 1.6f, AdsSpread = 0.2f, BloomPerShot = 0.8f, RecoilKick = 11f, AdsFov = 58f,
            RagdollImpulse = 320f, RagdollLift = 0.35f, RagdollSpin = 60f, DismemberPower = 2,
            HipOffset = new Vector3(0.13f, -0.15f, 0.4f), SightHeight = 0.095f, AdsDistance = 0.42f,
            LeftHand = new Vector3(-0.015f, -0.035f, 0.0f), PickupGlow = new Color(1f, 0.82f, 0.25f), SoundPitch = 0.9f, TracerSpeed = 380f,
        },
        new WeaponDef
        {
            Id = WeaponId.M32, Name = "M32 MGL", Caliber = "40mm",
            Damage = 110, Projectile = ProjectileKind.Grenade40, ProjectileSpeed = 42f, ProjectileGravity = 1f,
            ExplosionRadius = 5f, ExplosionDamage = 120f,
            FireInterval = 0.55f, MagSize = 6, SpareMags = 1, ReloadTime = 0.7f, ReloadPerRound = true,
            HipSpread = 1.5f, AdsSpread = 0.4f, BloomPerShot = 0f, RecoilKick = 7f, AdsFov = 62f, MoveSpeedScale = 0.9f,
            HipOffset = new Vector3(0.2f, -0.27f, 0.34f), SightHeight = 0.172f, AdsDistance = 0.26f,
            LeftHand = new Vector3(0f, -0.06f, 0.33f), PickupGlow = new Color(1f, 0.4f, 0.1f), SoundPitch = 1f,
        },
        new WeaponDef
        {
            Id = WeaponId.RPG7, Name = "RPG-7", Caliber = "PG-7V",
            Damage = 200, Projectile = ProjectileKind.Rocket, ProjectileSpeed = 55f, ProjectileGravity = 0.12f,
            ExplosionRadius = 6.5f, ExplosionDamage = 210f,
            FireInterval = 1f, MagSize = 1, SpareMags = 3, ReloadTime = 2.8f,
            HipSpread = 2f, AdsSpread = 0.3f, BloomPerShot = 0f, RecoilKick = 5f, AdsFov = 55f, MoveSpeedScale = 0.85f,
            HipOffset = new Vector3(0.24f, -0.23f, 0.24f), SightHeight = 0.1495f, AdsDistance = 0.16f,
            LeftHand = new Vector3(0f, -0.06f, 0.18f), PickupGlow = new Color(1f, 0.15f, 0.1f), SoundPitch = 1f,
        },
        new WeaponDef
        {
            Id = WeaponId.FragGrenade, Name = "Frag Grenade", Caliber = "M67",
            Damage = 160, Projectile = ProjectileKind.Frag, IsThrowable = true,
            ExplosionRadius = 7f, ExplosionDamage = 170f,
            MagSize = 2, SpareMags = 0, OneHanded = true, HipOffset = new Vector3(0.2f, -0.2f, 0.36f),
            PickupGlow = new Color(0.55f, 0.75f, 0.3f),
        },
        new WeaponDef
        {
            Id = WeaponId.AA12, Name = "AA-12", Caliber = "12 gauge",
            Damage = 13, Pellets = 8, HeadMultiplier = 1.5f, CloseBonus = 1.4f, CloseRange = 3f,
            FalloffStart = 8f, FalloffEnd = 25f, MinDamageScale = 0.3f, Range = 50f,
            Automatic = true, FireInterval = 0.2f, MagSize = 20, SpareMags = 2, ReloadTime = 3f,
            HipSpread = 5f, AdsSpread = 4.2f, BloomPerShot = 0.3f, RecoilKick = 5f, AdsFov = 62f, MoveSpeedScale = 0.92f,
            RagdollImpulse = 330f, RagdollLift = 0.4f, RagdollSpin = 60f, DismemberPower = 2,
            HipOffset = new Vector3(0.18f, -0.22f, 0.24f), SightHeight = 0.147f, AdsDistance = 0.2f,
            LeftHand = new Vector3(0f, 0.022f, 0.38f), PickupGlow = new Color(1f, 0.35f, 0.35f), SoundPitch = 0.9f, TracerSpeed = 250f,
        },
        new WeaponDef
        {
            Id = WeaponId.Mosin, Name = "Mosin-Nagant", Caliber = "7.62×54mmR",
            Damage = 95, HeadMultiplier = 2.5f, Range = 450f,
            FireInterval = 1.25f, MagSize = 5, SpareMags = 4, ReloadTime = 0.55f, ReloadPerRound = true,
            HipSpread = 4f, AdsSpread = 0.05f, BloomPerShot = 0f, RecoilKick = 11f, AdsFov = 48f, MoveSpeedScale = 0.95f,
            RagdollImpulse = 480f, RagdollLift = 0.45f, RagdollSpin = 90f, DismemberPower = 2,
            HipOffset = new Vector3(0.16f, -0.2f, 0.18f), SightHeight = 0.08f, AdsDistance = 0.2f,
            LeftHand = new Vector3(0f, 0.008f, 0.42f), PickupGlow = new Color(0.8f, 0.55f, 0.3f), SoundPitch = 0.7f, TracerSpeed = 700f,
        },
        new WeaponDef
        {
            Id = WeaponId.PPSh41, Name = "PPSh-41", Caliber = "7.62×25mm",
            Damage = 17, FalloffStart = 15f, FalloffEnd = 45f, MinDamageScale = 0.6f,
            Automatic = true, FireInterval = 0.055f, MagSize = 71, SpareMags = 2, ReloadTime = 3.2f,
            HipSpread = 2.6f, AdsSpread = 1f, BloomPerShot = 0.18f, RecoilKick = 0.25f, AdsFov = 60f,
            RagdollImpulse = 90f, RagdollLift = 0.15f, RagdollSpin = 18f, DismemberPower = 0,
            HipOffset = new Vector3(0.17f, -0.2f, 0.26f), SightHeight = 0.094f, AdsDistance = 0.18f,
            LeftHand = new Vector3(0f, 0.02f, 0.2f), PickupGlow = new Color(0.95f, 0.6f, 0.2f), SoundPitch = 1.3f, TracerSpeed = 330f,
        },
        new WeaponDef
        {
            Id = WeaponId.M249, Name = "M249 SAW", Caliber = "5.56×45mm",
            Damage = 26, FalloffStart = 45f, FalloffEnd = 100f, MinDamageScale = 0.8f,
            Automatic = true, FireInterval = 0.075f, MagSize = 100, SpareMags = 1, ReloadTime = 5.5f,
            HipSpread = 3.2f, AdsSpread = 0.6f, BloomPerShot = 0.2f, RecoilKick = 0.4f, AdsFov = 52f, MoveSpeedScale = 0.8f,
            RagdollImpulse = 150f, RagdollLift = 0.2f, RagdollSpin = 28f, DismemberPower = 1,
            HipOffset = new Vector3(0.2f, -0.24f, 0.22f), SightHeight = 0.128f, AdsDistance = 0.12f,
            LeftHand = new Vector3(0f, 0f, 0.32f), PickupGlow = new Color(0.5f, 0.7f, 1f), SoundPitch = 0.95f, TracerSpeed = 520f,
        },
        new WeaponDef
        {
            Id = WeaponId.Cattleman, Name = "Cattleman", Caliber = ".45 Colt",
            Damage = 55, HeadMultiplier = 2.2f, FalloffStart = 25f, FalloffEnd = 60f, MinDamageScale = 0.7f, Range = 150f,
            FireInterval = 0.5f, MagSize = 6, SpareMags = 4, ReloadTime = 0.45f, ReloadPerRound = true,
            HipSpread = 1.6f, AdsSpread = 0.3f, BloomPerShot = 0.6f, RecoilKick = 8f, AdsFov = 60f, MoveSpeedScale = 1.03f,
            RagdollImpulse = 260f, RagdollLift = 0.3f, RagdollSpin = 50f, DismemberPower = 1,
            HipOffset = new Vector3(0.13f, -0.15f, 0.4f), SightHeight = 0.066f, AdsDistance = 0.4f,
            LeftHand = new Vector3(-0.015f, -0.035f, 0.0f), PickupGlow = new Color(0.85f, 0.65f, 0.4f), SoundPitch = 0.85f, TracerSpeed = 330f,
        },
        new WeaponDef
        {
            Id = WeaponId.Magnum44, Name = ".44 Magnum", Caliber = ".44 Magnum",
            Damage = 62, HeadMultiplier = 2.2f, FalloffStart = 30f, FalloffEnd = 70f, MinDamageScale = 0.7f, Range = 180f,
            FireInterval = 0.42f, MagSize = 6, SpareMags = 4, ReloadTime = 2.4f,
            HipSpread = 1.8f, AdsSpread = 0.25f, BloomPerShot = 0.8f, RecoilKick = 12f, AdsFov = 58f,
            RagdollImpulse = 330f, RagdollLift = 0.35f, RagdollSpin = 60f, DismemberPower = 2,
            HipOffset = new Vector3(0.13f, -0.15f, 0.4f), SightHeight = 0.076f, AdsDistance = 0.42f,
            LeftHand = new Vector3(-0.015f, -0.035f, 0.0f), PickupGlow = new Color(0.7f, 0.75f, 0.85f), SoundPitch = 0.8f, TracerSpeed = 360f,
        },
        new WeaponDef
        {
            Id = WeaponId.LeverAction, Name = "Lever Action", Caliber = ".30-30 Win",
            Damage = 70, HeadMultiplier = 2.3f, Range = 300f,
            FireInterval = 0.75f, MagSize = 7, SpareMags = 4, ReloadTime = 0.5f, ReloadPerRound = true,
            HipSpread = 3.5f, AdsSpread = 0.15f, BloomPerShot = 0f, RecoilKick = 8.5f, AdsFov = 52f, MoveSpeedScale = 0.97f,
            RagdollImpulse = 380f, RagdollLift = 0.4f, RagdollSpin = 70f, DismemberPower = 2,
            HipOffset = new Vector3(0.16f, -0.2f, 0.2f), SightHeight = 0.0755f, AdsDistance = 0.16f,
            LeftHand = new Vector3(0f, 0.02f, 0.33f), PickupGlow = new Color(0.9f, 0.7f, 0.45f), SoundPitch = 0.8f, TracerSpeed = 600f,
        },
        new WeaponDef
        {
            Id = WeaponId.M1911, Name = "M1911", Caliber = ".45 ACP",
            Damage = 30, FalloffStart = 15f, FalloffEnd = 40f, MinDamageScale = 0.7f,
            FireInterval = 0.14f, MagSize = 7, ReloadTime = 1.6f,
            HipSpread = 1.4f, AdsSpread = 0.4f, BloomPerShot = 0.45f, RecoilKick = 1f, AdsFov = 62f, MoveSpeedScale = 1.05f,
            RagdollImpulse = 110f, RagdollLift = 0.2f, RagdollSpin = 18f, DismemberPower = 0,
            HipOffset = new Vector3(0.13f, -0.14f, 0.42f), SightHeight = 0.064f, AdsDistance = 0.42f,
            LeftHand = new Vector3(-0.015f, -0.03f, 0.0f), PickupGlow = new Color(0.75f, 0.75f, 0.7f), SoundPitch = 0.95f, TracerSpeed = 300f,
        },
        new WeaponDef
        {
            Id = WeaponId.GlockSwitch, Name = "Glock (switch)", Caliber = "9×19mm full auto",
            Damage = 18, FalloffStart = 12f, FalloffEnd = 35f, MinDamageScale = 0.6f,
            Automatic = true, FireInterval = 0.05f, MagSize = 33, SpareMags = 2, ReloadTime = 2f,
            HipSpread = 3.5f, AdsSpread = 2.2f, BloomPerShot = 0.45f, RecoilKick = 0.4f, AdsFov = 64f, MoveSpeedScale = 1.05f,
            RagdollImpulse = 75f, RagdollLift = 0.15f, RagdollSpin = 14f, DismemberPower = 0,
            HipOffset = new Vector3(0.1f, -0.13f, 0.42f), SightHeight = 0.064f, AdsDistance = 0.42f,
            OneHanded = true, CantDegrees = 90f,
            PickupGlow = new Color(0.95f, 0.3f, 0.6f), SoundPitch = 1.15f, TracerSpeed = 300f,
        },
        new WeaponDef
        {
            Id = WeaponId.KrissVector, Name = "KRISS Vector", Caliber = ".45 ACP",
            Damage = 24, FalloffStart = 12f, FalloffEnd = 35f, MinDamageScale = 0.65f,
            Automatic = true, FireInterval = 0.05f, MagSize = 25, SpareMags = 3, ReloadTime = 2.1f,
            HipSpread = 2.4f, AdsSpread = 0.6f, BloomPerShot = 0.15f, RecoilKick = 0.5f, AdsFov = 58f,
            RagdollImpulse = 110f, RagdollLift = 0.2f, RagdollSpin = 20f, DismemberPower = 0,
            HipOffset = new Vector3(0.16f, -0.19f, 0.28f), SightHeight = 0.108f, AdsDistance = 0.17f,
            LeftHand = new Vector3(0f, -0.03f, 0.17f), PickupGlow = new Color(0.55f, 0.55f, 0.6f), SoundPitch = 1.1f, TracerSpeed = 320f,
        },
        new WeaponDef
        {
            Id = WeaponId.MP18, Name = "MP-18", Caliber = ".308 Win",
            Damage = 110, HeadMultiplier = 2.5f, Range = 450f,
            FireInterval = 0.4f, MagSize = 1, SpareMags = 15, ReloadTime = 1.4f,
            HipSpread = 4f, AdsSpread = 0.05f, BloomPerShot = 0f, RecoilKick = 10f, AdsFov = 48f, MoveSpeedScale = 0.98f,
            RagdollImpulse = 520f, RagdollLift = 0.45f, RagdollSpin = 95f, DismemberPower = 2,
            HipOffset = new Vector3(0.16f, -0.2f, 0.18f), SightHeight = 0.072f, AdsDistance = 0.16f,
            LeftHand = new Vector3(0f, 0.022f, 0.44f), PickupGlow = new Color(0.8f, 0.6f, 0.4f), SoundPitch = 0.72f, TracerSpeed = 750f,
        },
        new WeaponDef
        {
            Id = WeaponId.Fists, Name = "Fists", Caliber = "bare knuckles",
            Damage = 20, Range = 2f, FireInterval = 0.42f, HipSpread = 1f, AdsSpread = 1f, BloomPerShot = 0f,
            RagdollImpulse = 220f, RagdollLift = 0.35f, RagdollSpin = 40f, DismemberPower = 0,
            PickupGlow = Color.white,
        },
        new WeaponDef
        {
            Id = WeaponId.GrappleHook, Name = "Grappling Hook", Caliber = "30 m line",
            MagSize = 0, SpareMags = 0, PickupGlow = new Color(0.3f, 0.9f, 1f),
        },
        new WeaponDef
        {
            Id = WeaponId.SmokeGrenade, Name = "Smoke Grenade", Caliber = "M18 smoke",
            Projectile = ProjectileKind.Smoke, IsThrowable = true, MagSize = 1, SpareMags = 0,
            OneHanded = true, HipOffset = new Vector3(0.2f, -0.2f, 0.36f), PickupGlow = new Color(0.85f, 0.85f, 0.9f),
        },
        new WeaponDef
        {
            Id = WeaponId.RopeGrenade, Name = "Rope Grenade", Caliber = "15 m snare",
            Projectile = ProjectileKind.Rope, IsThrowable = true, MagSize = 1, SpareMags = 0,
            OneHanded = true, HipOffset = new Vector3(0.2f, -0.2f, 0.36f), PickupGlow = new Color(0.95f, 0.7f, 0.35f),
        },
        new WeaponDef
        {
            Id = WeaponId.Molotov, Name = "Molotov", Caliber = "petrol bottle",
            Projectile = ProjectileKind.Molotov, IsThrowable = true, MagSize = 1, SpareMags = 0,
            OneHanded = true, HipOffset = new Vector3(0.2f, -0.24f, 0.34f), PickupGlow = new Color(1f, 0.45f, 0.1f),
            RagdollImpulse = 60f, RagdollLift = 0.2f,
        },
    };

    public static WeaponDef Get(WeaponId id) => id == WeaponId.None ? null : All[(int)id];
    public static WeaponDef Get(int id) => id < 0 || id >= All.Length ? null : All[id];

    /// <summary>
    /// Deterministic pellet directions so the shooter's predicted tracers match what the server simulates.
    /// </summary>
    public static Vector3[] PelletDirections(Vector3 forward, int pellets, float spreadDegrees, int seed)
    {
        var rng = new System.Random(seed);
        var result = new Vector3[pellets];
        var rot = Quaternion.LookRotation(forward);
        for (int i = 0; i < pellets; i++)
        {
            // Uniform-ish disc distribution inside the spread cone.
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            float radius = Mathf.Sqrt((float)rng.NextDouble()) * spreadDegrees;
            var offset = Quaternion.Euler(Mathf.Sin((float)angle) * radius, Mathf.Cos((float)angle) * radius, 0f);
            result[i] = rot * offset * Vector3.forward;
        }
        return result;
    }
}
