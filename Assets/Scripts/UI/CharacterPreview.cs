using UnityEngine;

/// <summary>
/// Off-map "photo booth" for the Customize tab: a lit turntable with the selected skin, rendered into a texture.
/// </summary>
public class CharacterPreview : MonoBehaviour
{
    public RenderTexture Texture { get; private set; }

    Camera cam;
    Transform turntable;
    BodyModel body;
    int builtSkin = -1;
    float angle = 200f;

    public static CharacterPreview Create()
    {
        var go = new GameObject("Character Preview");
        go.transform.position = new Vector3(1000f, 0f, 1000f); // far outside the map, under open sky
        var p = go.AddComponent<CharacterPreview>();
        p.Setup();
        return p;
    }

    void Setup()
    {
        Texture = new RenderTexture(600, 760, 24) { antiAliasing = 4, name = "PreviewRT" };
        var camGo = new GameObject("Preview Camera");
        camGo.transform.SetParent(transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.3f, 3.8f); // far enough back to fit tall hats
        camGo.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.06f, -1f));
        cam = camGo.AddComponent<Camera>();
        cam.targetTexture = Texture;
        cam.fieldOfView = 34f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.09f, 0.1f, 0.13f, 1f);
        cam.cullingMask = ~(1 << GameAssets.ViewModelLayer);
        cam.farClipPlane = 20f;
        cam.enabled = false;

        // Key and rim lights.
        var key = new GameObject("Key").AddComponent<Light>();
        key.transform.SetParent(transform, false);
        key.transform.localPosition = new Vector3(1.6f, 2.4f, 2.2f);
        key.type = LightType.Point;
        key.range = 8f;
        key.intensity = 2.2f;
        key.color = new Color(1f, 0.95f, 0.88f);
        var rim = new GameObject("Rim").AddComponent<Light>();
        rim.transform.SetParent(transform, false);
        rim.transform.localPosition = new Vector3(-1.8f, 2f, -1.5f);
        rim.type = LightType.Point;
        rim.range = 7f;
        rim.intensity = 2.6f;
        rim.color = new Color(1f, 0.6f, 0.3f);

        // Platform.
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(transform, false);
        disc.transform.localPosition = new Vector3(0f, -0.04f, 0f);
        disc.transform.localScale = new Vector3(1.4f, 0.04f, 1.4f);
        disc.GetComponent<Renderer>().sharedMaterial = GameAssets.I.gunMetal;

        turntable = new GameObject("Turntable").transform;
        turntable.SetParent(transform, false);
    }

    public void Show(int skin, bool visible)
    {
        if (cam == null) return;
        cam.enabled = visible;
        if (!visible) return;
        if (skin != builtSkin)
        {
            builtSkin = skin;
            if (body != null) Destroy(body.gameObject);
            body = BodyModel.Build(turntable, new Color(1f, 0.55f, 0.15f), false, skin);
            body.animateLegs = false;
        }
    }

    void Update()
    {
        if (cam == null || !cam.enabled) return;
        angle += Time.unscaledDeltaTime * 30f;
        turntable.localRotation = Quaternion.Euler(0f, angle, 0f);
    }
}
