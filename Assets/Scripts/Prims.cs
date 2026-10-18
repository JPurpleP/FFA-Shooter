using UnityEngine;

/// <summary>
/// Visual-only primitives. GameObject.CreatePrimitive also builds a physics collider, which most callers deleted
/// straight away; with ~100 parts per body or gun (killcam stand-ins, corpses, pickups) that create-and-destroy cost
/// hundreds of milliseconds in browsers. These share the built-in meshes and never make a collider.
/// </summary>
public static class Prims
{
    static readonly Mesh[] meshes = new Mesh[6];

    /// <summary>
    /// The exact mesh CreatePrimitive uses, taken from one throwaway primitive per shape. (The built-in "Sphere.fbx" /
    /// "Capsule.fbx" / "Cylinder.fbx" resources are different, twice-the-size meshes: using them made every body fat.)
    /// </summary>
    public static Mesh MeshOf(PrimitiveType type)
    {
        int i = (int)type; // Sphere, Capsule, Cylinder, Cube, Plane, Quad
        if (meshes[i] != null) return meshes[i];
        var probe = GameObject.CreatePrimitive(type);
        meshes[i] = probe.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Object.Destroy(probe); else Object.DestroyImmediate(probe);
        return meshes[i];
    }

    public static GameObject Make(PrimitiveType type, string name = null)
    {
        var go = new GameObject(name ?? type.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
        go.GetComponent<MeshFilter>().sharedMesh = MeshOf(type);
        return go;
    }

    static MaterialPropertyBlock block;

    /// <summary>Tint a renderer with one shared property block (SetPropertyBlock copies the values).</summary>
    public static void Tint(Renderer r, Color color)
    {
        block ??= new MaterialPropertyBlock();
        block.Clear();
        block.SetColor("_Color", color);
        r.SetPropertyBlock(block);
    }
}
