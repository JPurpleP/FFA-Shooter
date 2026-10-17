using Unity.Netcode;
using UnityEngine;

/// <summary>Everything clients need to reproduce a kill's ragdoll and gore identically.</summary>
public struct DeathInfo : INetworkSerializable
{
    public Vector3 Point;
    public Vector3 Direction;
    public byte Weapon;
    public byte Part;
    public float Distance;
    public int Seed;
    public bool Fell;
    public bool Train;              // run over by the Dust Ridge train
    public bool Explosive;          // blast kill: always total dismemberment, thrown away from ExplosionCenter
    public Vector3 ExplosionCenter;

    public WeaponId WeaponId => (WeaponId)Weapon;
    public BodyModel.Part HitPart => (BodyModel.Part)Part;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Point);
        s.SerializeValue(ref Direction);
        s.SerializeValue(ref Weapon);
        s.SerializeValue(ref Part);
        s.SerializeValue(ref Distance);
        s.SerializeValue(ref Seed);
        s.SerializeValue(ref Fell);
        s.SerializeValue(ref Train);
        s.SerializeValue(ref Explosive);
        if (Explosive) s.SerializeValue(ref ExplosionCenter);
    }
}

/// <summary>One bullet/pellet result for remote shot effects.</summary>
public struct ShotImpact : INetworkSerializable
{
    public Vector3 Point;
    public Vector3 Normal;
    public byte Kind; // 0 = nothing hit, 1 = world, 2 = flesh

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Point);
        s.SerializeValue(ref Normal);
        s.SerializeValue(ref Kind);
    }
}

/// <summary>Deterministic description of how a body comes apart.</summary>
public struct GorePlan
{
    public int SeverMask;     // bit per BodyModel.Part: that part's joint is torn off (Head = head explodes, Chest = cut in half)
    public bool ChestBurst;   // torso blown open
    public bool HeadOff;      // head torn off whole instead of bursting
    public bool Obliterate;   // total dismemberment: every joint fails, everything is thrown
    public int Intensity;     // 0..4 scales blood volume

    public bool Severed(BodyModel.Part p) => (SeverMask & (1 << (int)p)) != 0;
    public bool HeadExploded => Severed(BodyModel.Part.Head) && !HeadOff;
    public bool CutInHalf => Severed(BodyModel.Part.Chest);
    public bool AnyGibs => SeverMask != 0 || ChestBurst || Obliterate;

    const int AllLimbs = (1 << (int)BodyModel.Part.UpperArmL) | (1 << (int)BodyModel.Part.LowerArmL) | (1 << (int)BodyModel.Part.UpperArmR) |
                         (1 << (int)BodyModel.Part.LowerArmR) | (1 << (int)BodyModel.Part.UpperLegL) | (1 << (int)BodyModel.Part.LowerLegL) |
                         (1 << (int)BodyModel.Part.UpperLegR) | (1 << (int)BodyModel.Part.LowerLegR);

    public static GorePlan For(DeathInfo info)
    {
        var plan = new GorePlan();
        var rng = new System.Random(info.Seed);
        void Sever(BodyModel.Part p) => plan.SeverMask |= 1 << (int)p;
        BodyModel.Part RandomLimb() => (BodyModel.Part)rng.Next((int)BodyModel.Part.UpperArmL, BodyModel.PartCount);
        void Head(double explodeChance)
        {
            Sever(BodyModel.Part.Head);
            plan.HeadOff = rng.NextDouble() >= explodeChance;
        }
        void Total()
        {
            plan.Obliterate = true;
            plan.ChestBurst = true;
            plan.SeverMask |= AllLimbs | (1 << (int)BodyModel.Part.Chest);
            Head(0.5);
            plan.Intensity = 4;
        }

        if (info.Explosive)
        {
            Total();
            return plan;
        }
        var def = WeaponDefs.Get(info.WeaponId);
        if (def == null || info.Fell)
        {
            plan.Intensity = 1;
            return plan;
        }
        var part = info.HitPart;
        float close = 1f - Mathf.Clamp01((info.Distance - 2f) / 14f);
        bool limb = BodyModel.IsLimb(part);
        bool torso = part == BodyModel.Part.Chest || part == BodyModel.Part.Pelvis;
        plan.Intensity = def.DismemberPower;

        switch (def.DismemberPower)
        {
            case 0: // pistols, SMG
                if (part == BodyModel.Part.Head && rng.NextDouble() < 0.45) Head(0.85);
                if (limb && rng.NextDouble() < 0.25) Sever(part);
                break;
            case 1: // rifle
                if (part == BodyModel.Part.Head) Head(0.75);
                if (limb && rng.NextDouble() < 0.6) Sever(part);
                if (torso && info.Distance < 10f && rng.NextDouble() < 0.3) Sever(rng.NextDouble() < 0.5 ? BodyModel.Part.UpperArmL : BodyModel.Part.UpperArmR);
                break;
            case 2: // shotgun, Deagle
                if (close > 0.3f)
                {
                    if (part == BodyModel.Part.Head) Head(0.7);
                    if (limb) Sever(part);
                    int extra = rng.Next(1, 2 + Mathf.RoundToInt(close * 3f));
                    for (int i = 0; i < extra; i++) Sever(RandomLimb());
                    if (torso && close > 0.6f)
                    {
                        plan.ChestBurst = true;
                        Sever(BodyModel.Part.UpperArmL);
                        Sever(BodyModel.Part.UpperArmR);
                        if (rng.NextDouble() < 0.5) Head(0.6);
                        if (close > 0.85f && rng.NextDouble() < 0.4) Sever(BodyModel.Part.Chest);
                    }
                }
                else
                {
                    if (limb && rng.NextDouble() < 0.7) Sever(part);
                    if (part == BodyModel.Part.Head && rng.NextDouble() < 0.6) Head(0.8);
                }
                break;
            default: // Barrett
                if (torso)
                {
                    Sever(BodyModel.Part.Chest);
                    plan.ChestBurst = true;
                    if (rng.NextDouble() < 0.7) Sever(BodyModel.Part.UpperArmL);
                    if (rng.NextDouble() < 0.7) Sever(BodyModel.Part.UpperArmR);
                    if (part == BodyModel.Part.Pelvis) { Sever(BodyModel.Part.UpperLegL); Sever(BodyModel.Part.UpperLegR); }
                }
                else if (part == BodyModel.Part.Head)
                {
                    Head(0.85);
                    if (rng.NextDouble() < 0.3) Sever(RandomLimb());
                }
                else
                {
                    Sever(part);
                    int extra = rng.Next(1, 3);
                    for (int i = 0; i < extra; i++) Sever(RandomLimb());
                }
                if (rng.NextDouble() < 0.3) Total();
                break;
        }
        return plan;
    }

    public string Describe()
    {
        if (Obliterate) return "OBLITERATED";
        if (CutInHalf) return "CUT IN HALF";
        if (Severed(BodyModel.Part.Head)) return HeadOff ? "DECAPITATED" : "HEAD EXPLODED";
        if (ChestBurst) return "SPLATTERED";
        if (SeverMask != 0) return "DISMEMBERED";
        return null;
    }
}
