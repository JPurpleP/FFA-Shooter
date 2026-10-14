using UnityEngine;

public enum ShirtPattern { Plain, Rainbow, Polka, Plaid, Sailor, Hawaiian }
public enum HatKind
{
    None, HardHat, Cap, Toque, Cowboy, Tricorn, Bucket, AstroHelmet, BananaHood, Kepi, Stahlhelm,
    Propeller, Beret, Beanie, StrawHat, FireHelmet, VikingHelmet, KnightHelm, SurgicalCap, HazmatHood,
}

/// <summary>One character outfit: colors, a shirt pattern, headwear and a few optional pieces of costume.</summary>
public class SkinDef
{
    public string Name;
    public string Description;
    public Color Shirt = Color.white;
    public ShirtPattern Pattern;
    public Color Pants = new(0.2f, 0.22f, 0.26f);
    public Color Hair = new(0.05f, 0.045f, 0.045f);
    public Color Boots = new(0.1f, 0.09f, 0.08f);
    public Color SkinTone;              // alpha 0 = default skin
    public Color? Gloves;               // null = default tan gloves
    public Color Swatch;                // list swatch for patterned shirts (alpha 0 = use Shirt)

    public HatKind Hat;
    public Color HatColor = new(0.95f, 0.78f, 0.1f);
    public bool Bald;
    public bool FreedomPrint, HiVisStripes, PlateCarrier, ChefButtons, Backpack;
    public bool TunicButtons;           // single column of brass buttons and a brass belt plate
    public bool FlagPrint;              // big Confederate flag printed on the chest
    public bool WehrmachtGear;          // M36 tunic details, Y-straps, belt, K98 pouches, bread bag, gas-mask canister
    public bool TallBoots;              // knee-high marching boots
    public Color GearColor = new(0.22f, 0.25f, 0.16f);
    public Color? Mustache, Beard, Neckerchief;
    public bool Sunglasses, Eyepatch, RedNose, ClownHair, AlienEyes, Antennae;
    public Color? Mask;                 // luchador mask color
    public Color MaskTrim = new(1f, 0.82f, 0.2f);
    public bool Shorts, BigShoes;
    // Hair, face and body extras
    public bool Afro, Combover, LongHair, Glasses;
    public Color? Goatee, FaceMask;
    public bool TankTop, Belly, Hood, Overalls, JacketLapels;
    public Color? Tie, FurCollar;
    public Color UnderShirt = Color.white; // shirt showing between suit lapels

    public bool RainbowShirt => Pattern == ShirtPattern.Rainbow;
    public Color SleeveColor => Swatch.a > 0f ? Swatch : Shirt;
}

public static class Skins
{
    public static readonly SkinDef[] All =
    {
        new SkinDef
        {
            Name = "Original", Description = "Plain white tee, dark jeans, short black hair.",
            Shirt = new Color(0.95f, 0.95f, 0.94f),
        },
        new SkinDef
        {
            Name = "Rainbow", Description = "Rainbow-striped tee, light denim and bright blue hair.",
            Pattern = ShirtPattern.Rainbow, Swatch = new Color(0.9f, 0.3f, 0.6f),
            Pants = new Color(0.38f, 0.48f, 0.66f), Hair = new Color(0.12f, 0.35f, 0.95f),
            Boots = new Color(0.92f, 0.92f, 0.9f),
        },
        new SkinDef
        {
            Name = "Hi-Vis Worker", Description = "High-visibility shirt with reflective bands and a hard hat.",
            Shirt = new Color(0.82f, 0.95f, 0.12f), HiVisStripes = true, Hat = HatKind.HardHat,
            Pants = new Color(0.27f, 0.26f, 0.24f), Hair = new Color(0.25f, 0.16f, 0.08f),
            Boots = new Color(0.32f, 0.2f, 0.1f),
        },
        new SkinDef
        {
            Name = "Tactical", Description = "Olive shirt under a plate carrier, black ball cap.",
            Shirt = new Color(0.33f, 0.37f, 0.25f), PlateCarrier = true, Hat = HatKind.Cap,
            Pants = new Color(0.24f, 0.26f, 0.2f), HatColor = new Color(0.07f, 0.07f, 0.08f),
        },
        new SkinDef
        {
            Name = "Chef", Description = "Double-breasted whites, a towering toque and a magnificent mustache.",
            Shirt = new Color(0.96f, 0.96f, 0.95f), ChefButtons = true, Hat = HatKind.Toque, HatColor = new Color(0.98f, 0.98f, 0.97f),
            Pants = new Color(0.22f, 0.22f, 0.24f), Mustache = new Color(0.08f, 0.06f, 0.05f), Neckerchief = new Color(0.85f, 0.12f, 0.12f),
        },
        new SkinDef
        {
            Name = "Clown", Description = "Polka dots, a honking red nose, puffy hair and shoes for days.",
            Pattern = ShirtPattern.Polka, Swatch = new Color(0.95f, 0.3f, 0.3f),
            Pants = new Color(0.55f, 0.2f, 0.75f), SkinTone = new Color(0.97f, 0.95f, 0.93f),
            RedNose = true, ClownHair = true, Bald = true, Hair = new Color(1f, 0.45f, 0.05f),
            BigShoes = true, Boots = new Color(0.88f, 0.1f, 0.1f), Gloves = new Color(0.97f, 0.97f, 0.97f),
        },
        new SkinDef
        {
            Name = "Banana Suit", Description = "A full-body banana costume. Your face pokes out. Dignity does not.",
            Shirt = new Color(1f, 0.88f, 0.2f), Pants = new Color(1f, 0.88f, 0.2f), Hat = HatKind.BananaHood,
            HatColor = new Color(1f, 0.88f, 0.2f), Bald = true, Boots = new Color(0.35f, 0.24f, 0.1f),
            Gloves = new Color(1f, 0.88f, 0.2f),
        },
        new SkinDef
        {
            Name = "Astronaut", Description = "White pressure suit, mirrored bubble helmet and a life-support pack.",
            Shirt = new Color(0.93f, 0.93f, 0.95f), Pants = new Color(0.93f, 0.93f, 0.95f), Hat = HatKind.AstroHelmet,
            Bald = true, Backpack = true, Boots = new Color(0.8f, 0.8f, 0.82f), Gloves = new Color(0.95f, 0.95f, 0.96f),
        },
        new SkinDef
        {
            Name = "Cowboy", Description = "Plaid shirt, bandana, wide-brim hat and a handlebar mustache.",
            Pattern = ShirtPattern.Plaid, Swatch = new Color(0.75f, 0.12f, 0.1f),
            Pants = new Color(0.25f, 0.32f, 0.5f), Hat = HatKind.Cowboy, HatColor = new Color(0.45f, 0.3f, 0.15f),
            Mustache = new Color(0.35f, 0.2f, 0.08f), Neckerchief = new Color(0.8f, 0.12f, 0.1f),
            Hair = new Color(0.35f, 0.2f, 0.08f), Boots = new Color(0.4f, 0.24f, 0.1f),
        },
        new SkinDef
        {
            Name = "Pirate", Description = "Sailor stripes, a tricorn hat, an eyepatch and a scraggly beard.",
            Pattern = ShirtPattern.Sailor, Swatch = new Color(0.12f, 0.18f, 0.45f),
            Pants = new Color(0.08f, 0.08f, 0.09f), Hat = HatKind.Tricorn, HatColor = new Color(0.08f, 0.07f, 0.07f),
            Eyepatch = true, Beard = new Color(0.22f, 0.13f, 0.06f), Hair = new Color(0.22f, 0.13f, 0.06f),
        },
        new SkinDef
        {
            Name = "Alien", Description = "Green skin, enormous black eyes, antennae and a silver jumpsuit.",
            Shirt = new Color(0.75f, 0.78f, 0.82f), Pants = new Color(0.68f, 0.7f, 0.75f),
            SkinTone = new Color(0.45f, 0.8f, 0.35f), AlienEyes = true, Antennae = true, Bald = true,
            Boots = new Color(0.55f, 0.58f, 0.62f), Gloves = new Color(0.45f, 0.8f, 0.35f),
        },
        new SkinDef
        {
            Name = "Luchador", Description = "Masked wrestler in red tights, ready to fly off the top rope.",
            Shirt = new Color(0.85f, 0.1f, 0.15f), Pants = new Color(0.85f, 0.1f, 0.15f), Mask = new Color(0.12f, 0.28f, 0.85f),
            Bald = true, Boots = new Color(0.95f, 0.95f, 0.95f), Gloves = new Color(0.12f, 0.28f, 0.85f),
        },
        new SkinDef
        {
            Name = "Tourist", Description = "Loud Hawaiian shirt, cargo shorts, shades, bucket hat and a fresh sunburn.",
            Pattern = ShirtPattern.Hawaiian, Swatch = new Color(0.1f, 0.6f, 0.62f),
            Pants = new Color(0.76f, 0.68f, 0.5f), Shorts = true, Sunglasses = true, Hat = HatKind.Bucket,
            HatColor = new Color(0.78f, 0.72f, 0.55f), Hair = new Color(0.6f, 0.6f, 0.6f),
            SkinTone = new Color(0.95f, 0.62f, 0.52f), Boots = new Color(0.45f, 0.3f, 0.18f),
        },
        new SkinDef
        {
            Name = "Confederate Soldier", Description = "Grey shell jacket with brass buttons, a kepi and a scraggly beard.",
            Shirt = new Color(0.55f, 0.56f, 0.58f), TunicButtons = true, Hat = HatKind.Kepi, HatColor = new Color(0.5f, 0.51f, 0.53f),
            Pants = new Color(0.46f, 0.5f, 0.56f), Boots = new Color(0.12f, 0.1f, 0.08f),
            Hair = new Color(0.3f, 0.2f, 0.1f), Beard = new Color(0.3f, 0.2f, 0.1f),
        },
        new SkinDef
        {
            Name = "Redneck", Description = "Confederate flag tee, jeans, trucker cap, a mustache and a farmer's sunburn.",
            Shirt = new Color(0.92f, 0.9f, 0.85f), FlagPrint = true, Hat = HatKind.Cap, HatColor = new Color(0.82f, 0.16f, 0.13f),
            Pants = new Color(0.25f, 0.35f, 0.55f), Boots = new Color(0.4f, 0.26f, 0.14f),
            Hair = new Color(0.42f, 0.28f, 0.14f), Mustache = new Color(0.42f, 0.28f, 0.14f), SkinTone = new Color(0.95f, 0.66f, 0.55f),
        },
        new SkinDef
        {
            Name = "Propeller Kid", Description = "Propeller beanie, bright tee, shorts and sneakers.",
            Shirt = new Color(0.95f, 0.75f, 0.15f), Pants = new Color(0.25f, 0.4f, 0.7f), Shorts = true,
            Hat = HatKind.Propeller, HatColor = new Color(0.2f, 0.45f, 0.9f), Hair = new Color(0.5f, 0.3f, 0.12f), Boots = new Color(0.9f, 0.9f, 0.9f),
        },
        new SkinDef
        {
            Name = "Office Dad", Description = "Short-sleeve dress shirt, clip-on tie, a brave comb-over and a donut-fed belly.",
            Shirt = new Color(0.96f, 0.96f, 0.94f), Tie = new Color(0.25f, 0.35f, 0.7f), Pants = new Color(0.22f, 0.28f, 0.5f),
            Bald = true, Combover = true, Belly = true, Hair = new Color(0.35f, 0.3f, 0.25f), Boots = new Color(0.15f, 0.1f, 0.07f),
        },
        new SkinDef
        {
            Name = "Suburban Dad", Description = "Cosy sweater, khakis, glasses, a proud mustache and a dad belly.",
            Shirt = new Color(0.6f, 0.25f, 0.25f), Pants = new Color(0.62f, 0.56f, 0.44f), Glasses = true, Belly = true,
            Mustache = new Color(0.08f, 0.06f, 0.05f), SkinTone = new Color(0.47f, 0.31f, 0.21f), Hair = new Color(0.06f, 0.05f, 0.05f),
            Boots = new Color(0.3f, 0.2f, 0.12f),
        },
        new SkinDef
        {
            Name = "Hitman", Description = "Black suit, black shirt, long hair, a beard and leather gloves. Very focused.",
            Shirt = new Color(0.08f, 0.08f, 0.09f), JacketLapels = true, UnderShirt = new Color(0.13f, 0.13f, 0.14f),
            Pants = new Color(0.07f, 0.07f, 0.08f), LongHair = true, Hair = new Color(0.07f, 0.06f, 0.05f), Beard = new Color(0.07f, 0.06f, 0.05f),
            Gloves = new Color(0.05f, 0.05f, 0.05f), Boots = new Color(0.04f, 0.04f, 0.04f),
        },
        new SkinDef
        {
            Name = "Street OG", Description = "White tank top, blue jeans, fresh sneakers, shaved head and a goatee.",
            Shirt = new Color(0.96f, 0.96f, 0.96f), TankTop = true, Pants = new Color(0.3f, 0.4f, 0.6f), Bald = true,
            Goatee = new Color(0.05f, 0.04f, 0.04f), Mustache = new Color(0.05f, 0.04f, 0.04f), SkinTone = new Color(0.38f, 0.25f, 0.17f),
            Boots = new Color(0.92f, 0.92f, 0.92f),
        },
        new SkinDef
        {
            Name = "Disco King", Description = "A magnificent afro, a 70s shirt, shades, bell-bottoms and a mustache.",
            Afro = true, Hair = new Color(0.06f, 0.05f, 0.05f), Shirt = new Color(0.9f, 0.45f, 0.12f), Pants = new Color(0.4f, 0.25f, 0.15f),
            Sunglasses = true, Mustache = new Color(0.06f, 0.05f, 0.05f), SkinTone = new Color(0.45f, 0.3f, 0.2f), Boots = new Color(0.85f, 0.85f, 0.8f),
        },
        new SkinDef
        {
            Name = "Ninja", Description = "All black, hooded mask with an eye slit, soft-soled boots.",
            Shirt = new Color(0.07f, 0.07f, 0.08f), Pants = new Color(0.07f, 0.07f, 0.08f), Mask = new Color(0.07f, 0.07f, 0.08f),
            MaskTrim = new Color(0.55f, 0.43f, 0.34f), Bald = true, Gloves = new Color(0.06f, 0.06f, 0.07f), Boots = new Color(0.05f, 0.05f, 0.05f),
        },
        new SkinDef
        {
            Name = "Viking", Description = "Horned helmet, fur mantle, braided ginger beard.",
            Shirt = new Color(0.45f, 0.32f, 0.2f), FurCollar = new Color(0.36f, 0.27f, 0.18f), Hat = HatKind.VikingHelmet, HatColor = new Color(0.6f, 0.6f, 0.62f),
            Beard = new Color(0.75f, 0.4f, 0.15f), Hair = new Color(0.75f, 0.4f, 0.15f), Pants = new Color(0.3f, 0.3f, 0.32f), Boots = new Color(0.35f, 0.24f, 0.14f),
        },
        new SkinDef
        {
            Name = "Knight", Description = "Head-to-toe steel: a great helm with an eye slit and armoured gauntlets.",
            Shirt = new Color(0.66f, 0.67f, 0.7f), Pants = new Color(0.6f, 0.61f, 0.64f), Hat = HatKind.KnightHelm, HatColor = new Color(0.63f, 0.64f, 0.68f),
            Bald = true, Gloves = new Color(0.55f, 0.56f, 0.6f), Boots = new Color(0.5f, 0.51f, 0.55f),
        },
        new SkinDef
        {
            Name = "Mime", Description = "Striped shirt, beret, white face and white gloves. Says nothing.",
            Pattern = ShirtPattern.Sailor, Swatch = new Color(0.12f, 0.18f, 0.45f), SkinTone = new Color(0.97f, 0.97f, 0.96f),
            Hat = HatKind.Beret, HatColor = new Color(0.08f, 0.08f, 0.08f), Gloves = new Color(0.97f, 0.97f, 0.97f), Pants = new Color(0.07f, 0.07f, 0.07f),
        },
        new SkinDef
        {
            Name = "Skater", Description = "Grey hoodie, red beanie, shorts and beat-up skate shoes.",
            Shirt = new Color(0.45f, 0.47f, 0.5f), Hood = true, Hat = HatKind.Beanie, HatColor = new Color(0.75f, 0.15f, 0.15f),
            Shorts = true, Pants = new Color(0.25f, 0.28f, 0.32f), Hair = new Color(0.85f, 0.7f, 0.35f), Boots = new Color(0.1f, 0.1f, 0.1f),
        },
        new SkinDef
        {
            Name = "Farmer", Description = "Denim overalls over a plaid shirt, straw hat, grey mustache.",
            Pattern = ShirtPattern.Plaid, Swatch = new Color(0.75f, 0.12f, 0.1f), Overalls = true, Pants = new Color(0.25f, 0.38f, 0.6f),
            Hat = HatKind.StrawHat, HatColor = new Color(0.88f, 0.78f, 0.48f), Mustache = new Color(0.62f, 0.62f, 0.6f), Hair = new Color(0.62f, 0.62f, 0.6f),
            SkinTone = new Color(0.9f, 0.62f, 0.5f), Boots = new Color(0.35f, 0.22f, 0.12f),
        },
        new SkinDef
        {
            Name = "Lumberjack", Description = "Plaid flannel, green beanie, big beard, work boots.",
            Pattern = ShirtPattern.Plaid, Swatch = new Color(0.75f, 0.12f, 0.1f), Hat = HatKind.Beanie, HatColor = new Color(0.15f, 0.35f, 0.2f),
            Beard = new Color(0.3f, 0.18f, 0.08f), Hair = new Color(0.3f, 0.18f, 0.08f), Pants = new Color(0.25f, 0.32f, 0.48f), Boots = new Color(0.4f, 0.26f, 0.12f),
        },
        new SkinDef
        {
            Name = "Surgeon", Description = "Teal scrubs, surgical cap and mask, blue gloves.",
            Shirt = new Color(0.3f, 0.6f, 0.62f), Pants = new Color(0.3f, 0.6f, 0.62f), Hat = HatKind.SurgicalCap, HatColor = new Color(0.3f, 0.6f, 0.62f),
            FaceMask = new Color(0.62f, 0.82f, 0.95f), Gloves = new Color(0.6f, 0.8f, 0.95f), Boots = new Color(0.85f, 0.85f, 0.85f),
        },
        new SkinDef
        {
            Name = "Firefighter", Description = "Turnout coat with reflective bands, red helmet, heavy gloves.",
            Shirt = new Color(0.78f, 0.68f, 0.35f), HiVisStripes = true, Pants = new Color(0.78f, 0.68f, 0.35f),
            Hat = HatKind.FireHelmet, HatColor = new Color(0.8f, 0.12f, 0.1f), Mustache = new Color(0.3f, 0.2f, 0.1f),
            Gloves = new Color(0.25f, 0.22f, 0.18f), Boots = new Color(0.06f, 0.06f, 0.06f),
        },
        new SkinDef
        {
            Name = "Hazmat", Description = "Sealed yellow suit, hood with a face visor, black rubber gloves and boots.",
            Shirt = new Color(0.95f, 0.85f, 0.12f), Pants = new Color(0.95f, 0.85f, 0.12f), Hat = HatKind.HazmatHood, HatColor = new Color(0.95f, 0.85f, 0.12f),
            Bald = true, Gloves = new Color(0.08f, 0.08f, 0.08f), Boots = new Color(0.08f, 0.08f, 0.08f),
        },
        new SkinDef
        {
            Name = "WW2 German Soldier", Description = "Field-grey M36 tunic, M35 helmet, jackboots, full webbing and a parody spider armband.",
            Shirt = new Color(0.42f, 0.44f, 0.38f), WehrmachtGear = true, TallBoots = true,
            Hat = HatKind.Stahlhelm, HatColor = new Color(0.34f, 0.36f, 0.31f),
            Pants = new Color(0.4f, 0.41f, 0.38f), Boots = new Color(0.08f, 0.07f, 0.07f),
            Hair = new Color(0.45f, 0.35f, 0.2f),
        },
    };

    public static int Count => All.Length;
    public static SkinDef Get(int id) => All[Mathf.Clamp(id, 0, All.Length - 1)];
}
