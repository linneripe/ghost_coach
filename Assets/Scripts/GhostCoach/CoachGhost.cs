using System.Collections.Generic;
using UnityEngine;

// Replays a recorded coach clip as a semi-transparent ghost at the table.
// The coach stands on the table's centre line and swings in the air (there
// is no ball).  The player stands beside the coach, facing the same way.
//
// The ghost body is a simple figure: capsules between the recorded joints
// when the clip has them (mocap), else a stick figure (head, torso, paddle
// arm) built from the head and paddle poses (Quest recordings).
public class CoachGhost : MonoBehaviour {

    public Play play;
    public Transform table;
    public float speed = 0.5f;              // Playback speed, 1 = real time.
    public float lead_time = 1.2f;          // Seconds shown before first contact.
    public float follow_time = 0.8f;        // Seconds shown after last contact.
    public float stand_offset = 0.9f;       // Meters beside the coach for the player.
    // Which side of the coach the player watches from.  On the free hand
    // side the coach's body can hide the paddle arm; on the paddle side
    // the arm is in view.  Standing a little behind helps see both.
    public bool stand_on_paddle_side = false;
    public float stand_behind = 0f;         // Meters behind the coach.
    public int stroke = 0;                  // Which stroke of the clip is repeated.
    // The coach is drawn on the table's centre line, a little larger than life,
    // scaled around its own feet.  The player stands beside it, so the paddle
    // path in phase 2 is moved by player_shift to where the player's racket goes.
    public float coach_scale = 1.1f;
    Vector3 shift = Vector3.zero;           // Table coordinates: from recorded place to the centre line.
    public Vector3 player_shift { get; private set; }   // From recorded place to the player's place.
    Vector3 scale_origin = Vector3.zero;    // Table coordinates, on the floor at the coach's feet.
    Transform frame_transform;

    MotionClip clip;
    float playback_time, loop_start, loop_end;

    Transform ghost_root, ghost_paddle, ghost_robot_paddle;
    Transform head, torso, arm, stand_marker;

    // Body drawn from recorded joints when the clip has them (mocap).
    // Pairs of Mixamo joint names and the limb width in meters.
    static readonly string[,] bones = {
        {"Hips", "Neck"}, {"LeftArm", "RightArm"}, {"LeftUpLeg", "RightUpLeg"},
        {"LeftArm", "LeftForeArm"}, {"LeftForeArm", "LeftHand"},
        {"RightArm", "RightForeArm"}, {"RightForeArm", "RightHand"},
        {"LeftUpLeg", "LeftLeg"}, {"LeftLeg", "LeftFoot"}, {"LeftFoot", "LeftToeBase"},
        {"RightUpLeg", "RightLeg"}, {"RightLeg", "RightFoot"}, {"RightFoot", "RightToeBase"},
    };
    static readonly float[] bone_widths = { 0.26f, 0.1f, 0.14f, 0.09f, 0.07f, 0.09f, 0.07f,
                                            0.13f, 0.1f, 0.08f, 0.13f, 0.1f, 0.08f };
    Transform[] limbs;
    int[] limb_from, limb_to;
    int neck_joint = -1;                    // The head sits on the neck joint.

    public bool showing {
        get { return ghost_root != null && ghost_root.gameObject.activeSelf; }
    }

    public float time {
        get { return playback_time; }
    }

    // Where the coach is now, world coordinates.
    public Vector3 coach_position {
        get { return head.position; }
    }

    public bool has_spot {
        get { return stand_marker != null; }
    }

    // Where the player should stand, beside the coach, on the floor, world coordinates.
    public Vector3 player_spot {
        get { return stand_marker.position; }
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
        map_joints();
        ghost_root.gameObject.SetActive(true);
        ghost_robot_paddle.gameObject.SetActive(clip.has_opponent);
        Update();
    }

    public void hide() {
        if (ghost_root != null)
            ghost_root.gameObject.SetActive(false);
    }

    // Jump to a time in the clip, e.g. to look at a ball contact.
    public void seek(float t) {
        playback_time = Mathf.Clamp(t, 0f, clip.duration);
        pose_ghost(clip.sample(playback_time), playback_time);
    }

    // Cycle playback speed 100%, 50%, 25%.  Returns the new speed.
    public float next_speed() {
        speed = (speed > 0.75f ? 0.5f : (speed > 0.375f ? 0.25f : 1f));
        return speed;
    }

    // Repeat the chosen stroke, from a little before its ball contact to a
    // little after, or the whole clip if it has no strokes.
    void set_loop() {
        List<MotionEvent> strokes = clip.strokes();
        loop_start = 0f;
        loop_end = clip.duration;
        if (strokes.Count > 0) {
            float contact = strokes[stroke % strokes.Count].t;
            loop_start = Mathf.Max(0f, contact - lead_time);
            loop_end = Mathf.Min(clip.duration, contact + follow_time);
        }
    }

    void Update() {
        if (clip == null || !showing)
            return;
        playback_time += speed * Time.deltaTime;
        if (playback_time > loop_end || playback_time < loop_start)
            playback_time = loop_start;
        pose_ghost(clip.sample(playback_time), playback_time);
    }

    // Table frame moved to where the coach stands beside the player.
    Transform frame {
        get {
            if (frame_transform == null)
                frame_transform = new GameObject("coach frame").transform;
            frame_transform.SetPositionAndRotation(table.position + table.rotation * shift, table.rotation);
            return frame_transform;
        }
    }

    // A recorded table coordinates position as drawn: scaled around the
    // coach's feet and moved beside the player.
    public Vector3 drawn(Vector3 p) {
        return TableSpace.to_world(frame, scale_origin + coach_scale * (p - scale_origin));
    }

    void pose_ghost(MotionFrame f, float t) {
        Vector3 paddle_position = drawn(f.paddle_position);
        Quaternion paddle_rotation = TableSpace.to_world(frame, f.paddle_rotation);
        ghost_paddle.SetPositionAndRotation(paddle_position, paddle_rotation);
        ghost_robot_paddle.SetPositionAndRotation(drawn(f.robot_paddle_position),
                                                  TableSpace.to_world(frame, f.robot_paddle_rotation));

        // Stick figure body facing where the head faces, kept upright.
        Vector3 head_position = drawn(f.head_position);
        Quaternion head_rotation = TableSpace.to_world(frame, f.head_rotation);
        Vector3 forward = Vector3.ProjectOnPlane(head_rotation * Vector3.forward, Vector3.up).normalized;
        if (forward == Vector3.zero)
            forward = table.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        head.SetPositionAndRotation(head_position, head_rotation);

        if (limb_from != null && f.joints != null) {
            // Real joints from mocap.
            for (int i = 0 ; i < limbs.Length ; ++i)
                place_limb(limbs[i], drawn(f.joints[limb_from[i]]),
                           drawn(f.joints[limb_to[i]]), bone_widths[i]);
            // No neck: the head sphere rests on the top of the torso.
            if (neck_joint >= 0)
                head.position = drawn(f.joints[neck_joint]) + 0.12f * coach_scale * Vector3.up;
        } else {
            Vector3 neck = head_position - 0.15f * Vector3.up;
            Vector3 hips = head_position - 0.75f * Vector3.up;
            place_limb(torso, neck, hips, 0.3f);

            // Straight arm from the shoulder on the paddle side to the handle.
            float side = (clip.left_handed ? -1f : 1f);
            Vector3 shoulder = neck - 0.05f * Vector3.up + 0.2f * side * right;
            Vector3 hand = paddle_position - 0.12f * (paddle_rotation * Vector3.up);
            place_limb(arm, shoulder, hand, 0.08f);
        }
    }

    // Stretch a unit-height primitive between two points.
    static void place_limb(Transform limb, Vector3 from, Vector3 to, float width) {
        Vector3 d = to - from;
        limb.position = 0.5f * (from + to);
        if (d.sqrMagnitude > 1e-6f)
            limb.rotation = Quaternion.FromToRotation(Vector3.up, d);
        limb.localScale = new Vector3(width, 0.5f * d.magnitude, width);  // Primitive height is 2.
    }

    // Use the clip's recorded joints for the body if it has them, otherwise
    // the torso and arm estimated from the head and paddle.
    void map_joints() {
        int n = bone_widths.Length;
        limb_from = limb_to = null;
        if (clip.joint_names != null && clip.joint_names.Count > 0) {
            int[] from = new int[n], to = new int[n];
            bool all = true;
            for (int i = 0 ; i < n ; ++i) {
                from[i] = clip.joint_names.IndexOf(bones[i, 0]);
                to[i] = clip.joint_names.IndexOf(bones[i, 1]);
                all = all && from[i] >= 0 && to[i] >= 0;
            }
            if (all) {
                limb_from = from;
                limb_to = to;
            }
        }
        bool mocap = (limb_from != null);
        neck_joint = (mocap ? clip.joint_names.IndexOf("Neck") : -1);
        if (mocap && limbs == null) {
            limbs = new Transform[n];
            Material ghost = GhostVisuals.material("ghost");
            for (int i = 0 ; i < n ; ++i)
                limbs[i] = GhostVisuals.primitive(PrimitiveType.Capsule, "ghost " + bones[i, 0] + " " + bones[i, 1],
                                                  ghost, ghost_root);
        }
        if (limbs != null)
            foreach (Transform limb in limbs)
                limb.gameObject.SetActive(mocap);
        torso.gameObject.SetActive(!mocap);
        arm.gameObject.SetActive(!mocap);
    }

    void place_stand_marker() {
        Vector3 forward, right;
        Vector3 coach = GhostVisuals.coach_stance(clip, loop_start, loop_end, out forward, out right);
        // Stand by the hips, which sway less than the head, when the clip has them.
        int hips = (clip.joint_names != null ? clip.joint_names.IndexOf("Hips") : -1);
        if (hips >= 0) {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (MotionFrame f in clip.frames)
                if (f.t >= loop_start && f.t <= loop_end && f.joints != null) {
                    sum += f.joints[hips];
                    n += 1;
                }
            if (n > 0) {
                coach.x = sum.x / n;
                coach.z = sum.z / n;
            }
        }
        float paddle_side = (clip.left_handed ? -1f : 1f);
        float side = (stand_on_paddle_side ? paddle_side : -paddle_side);
        // The coach moves sideways onto the table's centre line, and the
        // player stands beside it.
        Table table_component = table.GetComponent<Table>();
        float top_y;
        float centre_x = (table_component != null ? TableSpace.center(table_component, out top_y).x : 0f);
        Vector3 beside = stand_offset * side * right - stand_behind * forward;
        shift = new Vector3(centre_x - coach.x, 0f, 0f);
        player_shift = shift + new Vector3(beside.x, 0f, beside.z);
        scale_origin = new Vector3(coach.x, 0f, coach.z);
        GhostVisuals.place_on_floor(stand_marker, table, new Vector3(coach.x, 0f, coach.z) + player_shift);
    }

    void build_ghost() {
        Material ghost = GhostVisuals.material("ghost");

        ghost_root = new GameObject("CoachGhost visuals").transform;
        ghost_paddle = GhostVisuals.copy_meshes(play.paddle_hand.held_paddle.transform, "ghost paddle", ghost, ghost_root);
        ghost_robot_paddle = GhostVisuals.copy_meshes(play.robot.paddle.transform, "ghost robot paddle", ghost, ghost_root);
        head = GhostVisuals.primitive(PrimitiveType.Sphere, "ghost head", ghost, ghost_root);
        head.localScale = 0.2f * Vector3.one;
        torso = GhostVisuals.primitive(PrimitiveType.Capsule, "ghost torso", ghost, ghost_root);
        arm = GhostVisuals.primitive(PrimitiveType.Cylinder, "ghost arm", ghost, ghost_root);
        stand_marker = GhostVisuals.stand_marker("stand here marker", ghost_root);
    }
}
