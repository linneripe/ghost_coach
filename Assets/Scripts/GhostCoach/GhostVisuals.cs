using UnityEngine;

// Shared helpers for building the semi-transparent ghost visuals used in
// both training phases.
public static class GhostVisuals {

    // Material from Assets/Resources/GhostCoach (see GhostCoachAssets).
    public static Material material(string name) {
        return Resources.Load<Material>("GhostCoach/" + name);
    }

    public static Transform primitive(PrimitiveType type, string name, Material m, Transform parent) {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Object.Destroy(g.GetComponent<Collider>());   // Must not touch the ball or menus.
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        g.transform.SetParent(parent, false);
        return g.transform;
    }

    // Flat green disc on the floor showing the player where to stand.
    public static Transform stand_marker(string name, Transform parent) {
        Transform t = primitive(PrimitiveType.Cylinder, name, material("stand_marker"), parent);
        t.localScale = new Vector3(0.5f, 0.003f, 0.5f);
        return t;
    }

    // Place an object on the floor at a table coordinates position.
    public static void place_on_floor(Transform t, Transform table, Vector3 table_position) {
        table_position.y = 0.005f;              // Table origin is on the floor.
        t.position = TableSpace.to_world(table, table_position);
        t.rotation = table.rotation;
    }

    // Average coach head position and facing over a time range of a clip,
    // in table coordinates.  Right is the coach's right hand side.
    public static Vector3 coach_stance(MotionClip clip, float start, float end,
                                       out Vector3 forward, out Vector3 right) {
        Vector3 sum = Vector3.zero, forward_sum = Vector3.zero;
        int n = 0;
        foreach (MotionFrame f in clip.frames) {
            if (f.t < start || f.t > end)
                continue;
            sum += f.head_position;
            forward_sum += f.head_rotation * Vector3.forward;
            n += 1;
        }
        forward = Vector3.ProjectOnPlane(forward_sum, Vector3.up).normalized;
        if (forward == Vector3.zero)
            forward = Vector3.forward;
        right = Vector3.Cross(Vector3.up, forward);
        return (n > 0 ? sum / n : Vector3.zero);
    }

    // Copy only the visible meshes of a paddle, not its colliders or
    // scripts, so the ghost cannot hit the ball.
    public static Transform copy_meshes(Transform source, string name, Material m, Transform parent) {
        Transform copy = new GameObject(name).transform;
        copy.SetParent(parent, false);
        Quaternion inverse = Quaternion.Inverse(source.rotation);
        foreach (MeshRenderer r in source.GetComponentsInChildren<MeshRenderer>()) {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (!r.enabled || mf == null || mf.sharedMesh == null)
                continue;
            GameObject g = new GameObject(r.name);
            g.transform.SetParent(copy, false);
            g.transform.localPosition = inverse * (r.transform.position - source.position);
            g.transform.localRotation = inverse * r.transform.rotation;
            g.transform.localScale = r.transform.lossyScale;    // Copy root is not scaled.
            g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = m;
        }
        return copy;
    }

    // Thin line through points, e.g. a paddle path.
    public static LineRenderer line(string name, Material m, float width, Transform parent) {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        LineRenderer lr = g.AddComponent<LineRenderer>();
        lr.sharedMaterial = m;
        lr.widthMultiplier = width;
        lr.useWorldSpace = true;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = 0;
        return lr;
    }
}
