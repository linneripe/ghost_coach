using System;
using System.IO;
using UnityEngine;

// The coach as the character mesh of the mocap rig (the same figure as in
// Blender), skinned to the rig's bones and drawn with the ghost material.
// The mesh file NAME.body.bytes is written by Tools/blender/export_coach_clip.py
// (format described there) and the bone poses are in the clip.  Bones are
// separate transforms whose world poses are set every frame from the clip:
//
//   skinned point = rotation change * (rest point - rest bone position)
//                   + posed bone position                (table coordinates)
//
// which is what Blender computes, and which Unity's skinning does with
// bind poses that turn the mesh from table to bone coordinates.  A mirrored
// clip (left handed player) drives each side's bones with the poses of the
// other side, mirrored: the mirror of the right arm is the left arm.
public class CoachBody {

    public string resource;                 // File name in Resources/GhostCoach/clips.
    public Transform root;
    public SkinnedMeshRenderer renderer;
    public string[] bone_names;
    public Transform[] bones;
    public Vector3[] rest;                  // Rest bone positions, table coordinates.
    public Vector3[] rest_vertices;
    public BoneWeight[] weights;

    MotionClip mapped_clip;
    int[] source;                           // Clip bone to use for each mesh bone.

    // Null if the file is missing or cannot be read; the caller then draws
    // the simple figure instead.
    public static CoachBody load(string resource, Transform table, Transform parent, Material material) {
        TextAsset asset = Resources.Load<TextAsset>("GhostCoach/clips/" + resource);
        if (asset == null)
            return null;
        try {
            return new CoachBody(resource, asset.bytes, table, parent, material);
        } catch (Exception e) {
            Debug.LogWarning("GhostCoach: could not read character mesh " + resource + ": " + e.Message);
            return null;
        }
    }

    CoachBody(string resource, byte[] data, Transform table, Transform parent, Material material) {
        this.resource = resource;
        using (BinaryReader r = new BinaryReader(new MemoryStream(data))) {
            if (System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)) != "GCB1")
                throw new InvalidDataException("not a GCB1 file");
            int bone_count = r.ReadInt32(), vertex_count = r.ReadInt32(), submesh_count = r.ReadInt32();
            r.ReadInt32();                                   // Reserved.
            int[] index_counts = new int[submesh_count];
            for (int i = 0 ; i < submesh_count ; ++i)
                index_counts[i] = r.ReadInt32();
            bone_names = new string[bone_count];
            for (int i = 0 ; i < bone_count ; ++i)
                bone_names[i] = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
            rest = read_vectors(r, bone_count);
            rest_vertices = read_vectors(r, vertex_count);
            Vector3[] normals = read_vectors(r, vertex_count);
            weights = new BoneWeight[vertex_count];
            for (int i = 0 ; i < vertex_count ; ++i) {
                BoneWeight w = new BoneWeight();
                w.boneIndex0 = r.ReadByte(); w.boneIndex1 = r.ReadByte();
                w.boneIndex2 = r.ReadByte(); w.boneIndex3 = r.ReadByte();
                weights[i] = w;
            }
            for (int i = 0 ; i < vertex_count ; ++i) {
                BoneWeight w = weights[i];
                w.weight0 = r.ReadSingle(); w.weight1 = r.ReadSingle();
                w.weight2 = r.ReadSingle(); w.weight3 = r.ReadSingle();
                weights[i] = w;
            }
            Mesh mesh = new Mesh();
            mesh.name = resource;
            mesh.indexFormat = (vertex_count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32
                                                     : UnityEngine.Rendering.IndexFormat.UInt16);
            mesh.vertices = rest_vertices;
            mesh.normals = normals;
            mesh.boneWeights = weights;
            // All parts (skin, joints) go in one submesh, so the depth pass of the
            // ghost shader covers the whole figure before any color is drawn.
            int total = 0;
            foreach (int count in index_counts)
                total += count;
            int[] all_indices = new int[total];
            for (int i = 0 ; i < total ; ++i)
                all_indices[i] = r.ReadInt32();
            mesh.subMeshCount = 1;
            mesh.SetTriangles(all_indices, 0);
            root = new GameObject("ghost body").transform;
            root.SetParent(parent, false);

            // The mesh is in table coordinates, so the mesh object sits at the
            // table.  Bones start in the rest pose, and the bind poses are the
            // inverses of those rest transforms.
            Transform mesh_object = new GameObject("ghost body mesh").transform;
            mesh_object.SetParent(root, false);
            mesh_object.SetPositionAndRotation(table.position, table.rotation);
            bones = new Transform[bone_count];
            Matrix4x4[] bind = new Matrix4x4[bone_count];
            for (int i = 0 ; i < bone_count ; ++i) {
                bones[i] = new GameObject(bone_names[i]).transform;
                bones[i].SetParent(root, false);
                bones[i].SetPositionAndRotation(TableSpace.to_world(table, rest[i]), table.rotation);
                bind[i] = bones[i].worldToLocalMatrix * mesh_object.localToWorldMatrix;
            }
            mesh.bindposes = bind;
            renderer = mesh_object.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.sharedMaterial = material;
            renderer.updateWhenOffscreen = true;      // Bounds follow the bones.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    static Vector3[] read_vectors(BinaryReader r, int count) {
        Vector3[] v = new Vector3[count];
        for (int i = 0 ; i < count ; ++i)
            v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        return v;
    }

    // Which clip bone drives each mesh bone: the same name, or the other
    // side's for a mirrored clip.
    void map_bones(MotionClip clip) {
        mapped_clip = clip;
        source = new int[bones.Length];
        for (int i = 0 ; i < bones.Length ; ++i) {
            string name = bone_names[i];
            if (clip.bones_mirrored)
                name = other_side(name);
            int c = clip.bone_names.IndexOf(name);
            source[i] = (c >= 0 ? c : clip.bone_names.IndexOf(bone_names[i]));
        }
    }

    static string other_side(string name) {
        if (name.Contains("Left"))
            return name.Replace("Left", "Right");
        if (name.Contains("Right"))
            return name.Replace("Right", "Left");
        return name;
    }

    // Pose the bones at a clip time.
    public void pose(MotionClip clip, float t, Transform table) {
        if (clip != mapped_clip)
            map_bones(clip);
        float[] d = clip.bone_floats();
        int stride = 7 * clip.bone_names.Count;
        float x = (t - clip.bone_t0) * clip.bone_rate;
        int i0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, clip.bone_frames - 1);
        int i1 = Mathf.Min(i0 + 1, clip.bone_frames - 1);
        float u = Mathf.Clamp01(x - i0);
        for (int i = 0 ; i < bones.Length ; ++i) {
            int c = source[i];
            if (c < 0)
                continue;
            int o0 = i0 * stride + 7 * c, o1 = i1 * stride + 7 * c;
            Vector3 p = Vector3.Lerp(new Vector3(d[o0], d[o0+1], d[o0+2]), new Vector3(d[o1], d[o1+1], d[o1+2]), u);
            Quaternion q = Quaternion.Slerp(new Quaternion(d[o0+3], d[o0+4], d[o0+5], d[o0+6]),
                                            new Quaternion(d[o1+3], d[o1+4], d[o1+5], d[o1+6]), u);
            if (clip.bones_mirrored) {
                p.x = 2f * clip.bones_mirror_x - p.x;
                q = new Quaternion(q.x, -q.y, -q.z, q.w);   // Rotation seen in a mirror across x.
            }
            bones[i].SetPositionAndRotation(TableSpace.to_world(table, p), table.rotation * q);
        }
    }

    public Transform bone(string name) {
        int i = Array.IndexOf(bone_names, name);
        return (i >= 0 ? bones[i] : null);
    }
}
