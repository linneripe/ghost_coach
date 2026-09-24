using System.Collections.Generic;
using UnityEngine;

// Phase 1: replays a recorded coach clip as a semi-transparent ghost at the
// table, where the coach stood, so it is in the hitting position.  The
// recorded ball and robot paddle are replayed too, so the coach always
// meets the ball.  A marker on the floor shows the player where to stand:
// beside the coach, facing the same way.
//
// The ghost body is a simple stick figure (head, torso, paddle arm) built
// from the head and paddle poses, since Quest recordings have no body.
// A full body can replace it once Qualisys recordings are available.
public class CoachGhost : MonoBehaviour {

    public Play play;
    public Transform table;
    public float speed = 0.5f;              // Playback speed, 1 = real time.
    public float lead_time = 1.2f;          // Seconds shown before first contact.
    public float follow_time = 0.8f;        // Seconds shown after last contact.
    public float stand_offset = 0.9f;       // Meters beside the coach for the player.

    MotionClip clip;
    float playback_time, loop_start, loop_end;

    Transform ghost_root, ghost_paddle, ghost_robot_paddle;
    Transform head, torso, arm, ball, stand_marker;

    public bool showing {
        get { return ghost_root != null && ghost_root.gameObject.activeSelf; }
    }

    public float time {
        get { return playback_time; }
    }

    public void show(MotionClip c) {
        if (c == null || c.frames.Count == 0) {
            hide();
            return;
        }
        if (ghost_root == null)
            build_ghost();
        clip = c;
        set_loop();
        playback_time = loop_start;
        place_stand_marker();
        ghost_root.gameObject.SetActive(true);
        Update();
    }

    public void hide() {
        if (ghost_root != null)
            ghost_root.gameObject.SetActive(false);
    }

    // Cycle playback speed 100%, 50%, 25%.  Returns the new speed.
    public float next_speed() {
        speed = (speed > 0.75f ? 0.5f : (speed > 0.375f ? 0.25f : 1f));
        return speed;
    }

    // Loop over the strokes, from a little before the first ball contact to
    // a little after the last one, or the whole clip if it has no contacts.
    void set_loop() {
        List<float> contacts = clip.contact_times();
        loop_start = 0f;
        loop_end = clip.duration;
        if (contacts.Count > 0) {
            loop_start = Mathf.Max(0f, contacts[0] - lead_time);
            loop_end = Mathf.Min(clip.duration, contacts[contacts.Count-1] + follow_time);
        }
    }

    void Update() {
        if (clip == null || !showing)
            return;
        playback_time += speed * Time.deltaTime;
        if (playback_time > loop_end || playback_time < loop_start)
            playback_time = loop_start;
        pose_ghost(clip.sample(playback_time));
    }

    void pose_ghost(MotionFrame f) {
        Vector3 paddle_position = TableSpace.to_world(table, f.paddle_position);
        Quaternion paddle_rotation = TableSpace.to_world(table, f.paddle_rotation);
        ghost_paddle.SetPositionAndRotation(paddle_position, paddle_rotation);
        ghost_robot_paddle.SetPositionAndRotation(TableSpace.to_world(table, f.robot_paddle_position),
                                                  TableSpace.to_world(table, f.robot_paddle_rotation));

        // Stick figure body facing where the head faces, kept upright.
        Vector3 head_position = TableSpace.to_world(table, f.head_position);
        Quaternion head_rotation = TableSpace.to_world(table, f.head_rotation);
        Vector3 forward = Vector3.ProjectOnPlane(head_rotation * Vector3.forward, Vector3.up).normalized;
        if (forward == Vector3.zero)
            forward = table.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        head.SetPositionAndRotation(head_position, head_rotation);

        Vector3 neck = head_position - 0.15f * Vector3.up;
        Vector3 hips = head_position - 0.75f * Vector3.up;
        place_limb(torso, neck, hips, 0.3f);

        // Straight arm from the shoulder on the paddle side to the handle.
        float side = (clip.left_handed ? -1f : 1f);
        Vector3 shoulder = neck - 0.05f * Vector3.up + 0.2f * side * right;
        Vector3 hand = paddle_position - 0.12f * (paddle_rotation * Vector3.up);
        place_limb(arm, shoulder, hand, 0.08f);

        ball.gameObject.SetActive(f.ball_in_play);
        ball.position = TableSpace.to_world(table, f.ball_position);
    }

    // Stretch a unit-height primitive between two points.
    static void place_limb(Transform limb, Vector3 from, Vector3 to, float width) {
        Vector3 d = to - from;
        limb.position = 0.5f * (from + to);
        if (d.sqrMagnitude > 1e-6f)
            limb.rotation = Quaternion.FromToRotation(Vector3.up, d);
        limb.localScale = new Vector3(width, 0.5f * d.magnitude, width);  // Primitive height is 2.
    }

    void place_stand_marker() {
        // Average coach head position over the loop, on the floor.
        Vector3 sum = Vector3.zero, forward_sum = Vector3.zero;
        int n = 0;
        foreach (MotionFrame f in clip.frames) {
            if (f.t < loop_start || f.t > loop_end)
                continue;
            sum += f.head_position;
            forward_sum += f.head_rotation * Vector3.forward;
            n += 1;
        }
        if (n == 0)
            return;
        Vector3 coach = sum / n;
        Vector3 forward = Vector3.ProjectOnPlane(forward_sum, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        // Stand on the side away from the paddle arm.
        float side = (clip.left_handed ? 1f : -1f);
        Vector3 spot = coach + stand_offset * side * right;
        spot.y = 0.005f;                      // Table origin is on the floor.
        stand_marker.position = TableSpace.to_world(table, spot);
        stand_marker.rotation = table.rotation;
    }

    void build_ghost() {
        Material ghost = Resources.Load<Material>("GhostCoach/ghost");
        Material ghost_ball = Resources.Load<Material>("GhostCoach/ghost_ball");
        Material marker = Resources.Load<Material>("GhostCoach/stand_marker");

        ghost_root = new GameObject("CoachGhost visuals").transform;
        ghost_paddle = copy_meshes(play.paddle_hand.held_paddle.transform, "ghost paddle", ghost);
        ghost_robot_paddle = copy_meshes(play.robot.paddle.transform, "ghost robot paddle", ghost);
        head = primitive(PrimitiveType.Sphere, "ghost head", ghost);
        head.localScale = 0.2f * Vector3.one;
        torso = primitive(PrimitiveType.Capsule, "ghost torso", ghost);
        arm = primitive(PrimitiveType.Cylinder, "ghost arm", ghost);
        ball = primitive(PrimitiveType.Sphere, "ghost ball", ghost_ball);
        float ball_radius = (play.ball_in_play != null ? play.ball_in_play.radius : 0.02f);
        ball.localScale = 2f * ball_radius * Vector3.one;
        stand_marker = primitive(PrimitiveType.Cylinder, "stand here marker", marker);
        stand_marker.localScale = new Vector3(0.5f, 0.003f, 0.5f);
    }

    Transform primitive(PrimitiveType type, string name, Material m) {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Destroy(g.GetComponent<Collider>());   // Must not touch the ball or menus.
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        g.transform.SetParent(ghost_root, false);
        return g.transform;
    }

    // Copy only the visible meshes of a paddle, not its colliders or
    // scripts, so the ghost cannot hit the ball.
    Transform copy_meshes(Transform source, string name, Material m) {
        Transform copy = new GameObject(name).transform;
        copy.SetParent(ghost_root, false);
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
}
