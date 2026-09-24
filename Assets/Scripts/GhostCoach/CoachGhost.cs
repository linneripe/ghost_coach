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
        // Stand on the side away from the paddle arm.
        Vector3 forward, right;
        Vector3 coach = GhostVisuals.coach_stance(clip, loop_start, loop_end, out forward, out right);
        float side = (clip.left_handed ? 1f : -1f);
        GhostVisuals.place_on_floor(stand_marker, table, coach + stand_offset * side * right);
    }

    void build_ghost() {
        Material ghost = GhostVisuals.material("ghost");
        Material ghost_ball = GhostVisuals.material("ghost_ball");

        ghost_root = new GameObject("CoachGhost visuals").transform;
        ghost_paddle = GhostVisuals.copy_meshes(play.paddle_hand.held_paddle.transform, "ghost paddle", ghost, ghost_root);
        ghost_robot_paddle = GhostVisuals.copy_meshes(play.robot.paddle.transform, "ghost robot paddle", ghost, ghost_root);
        head = GhostVisuals.primitive(PrimitiveType.Sphere, "ghost head", ghost, ghost_root);
        head.localScale = 0.2f * Vector3.one;
        torso = GhostVisuals.primitive(PrimitiveType.Capsule, "ghost torso", ghost, ghost_root);
        arm = GhostVisuals.primitive(PrimitiveType.Cylinder, "ghost arm", ghost, ghost_root);
        ball = GhostVisuals.primitive(PrimitiveType.Sphere, "ghost ball", ghost_ball, ghost_root);
        float ball_radius = (play.ball_in_play != null ? play.ball_in_play.radius : 0.02f);
        ball.localScale = 2f * ball_radius * Vector3.one;
        stand_marker = GhostVisuals.stand_marker("stand here marker", ghost_root);
    }
}
