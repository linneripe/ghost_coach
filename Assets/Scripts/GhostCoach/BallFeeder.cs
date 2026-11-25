using UnityEngine;

// Ball-feeding game mode: spawns a ball above the far end of the table every
// `feed_interval` seconds and sends it over the net so it bounces once on the
// player's half, in line with the player, and then rises to a height where it
// is easy to hit, with little spin.  Runs on a fixed timer regardless of
// whether or how the player returns the ball.
public class BallFeeder : MonoBehaviour {

    public enum TargetSide { Player, Forehand, Backhand }

    public static readonly float[] speed_presets = { 3.8f, 5.0f, 6.5f };
    public static readonly string[] speed_labels = { "Slow", "Medium", "Fast" };

    public Play play;
    public Table table;

    public float feed_interval = 3.0f;     // Seconds between feeds.
    public float feed_speed = 5.0f;        // Horizontal speed (m/s).
    public float feed_topspin = 1.0f;      // Mild topspin (m/s at ball surface).
    public TargetSide target_side = TargetSide.Player;
    public float forehand_x = 0.45f;       // Sideways bounce offset for forehand/backhand drills.
    int current_speed_level = 1;
    public int speed_level { get { return current_speed_level; } }

    // Coordinates relative to the full table center (at the net; y relative to top_y).
    public Vector3 spawn_offset = new Vector3(0.0f, 0.35f, 1.45f);
    public float bounce_z = -1.05f;        // Where the ball lands on the player's half, m from the net.
    public float bounce_x_limit = 0.55f;   // Landing spot stays this far from the table's side edges.

    public bool feeding { get; private set; }
    public int fed_count { get; private set; }
    public Vector3 last_spawn_position { get; private set; }
    public Vector3 last_launch_velocity { get; private set; }

    float timer = 0f;

    public void apply_speed_level(int level) {
        current_speed_level = Mathf.Clamp(level, 0, speed_presets.Length - 1);
        feed_speed = speed_presets[current_speed_level];
    }

    void Update() {
        if (!feeding)
            return;

        timer -= Time.deltaTime;
        if (timer <= 0f) {
            feed_ball();
            timer += feed_interval;
        }
    }

    public void start_feeding() {
        start_feeding(true);
    }

    public void start_feeding(bool hide_ghost) {
        if (feeding)
            return;
        feeding = true;
        timer = 0f;

        if (play != null) {
            play.robot_active = false;
            if (play.robot != null)
                play.robot.auto_serve = false;
        }

        // Standalone trigger mode hides GhostCoach visuals; dashboard Drill mode keeps them.
        GhostCoach coach = GetComponent<GhostCoach>();
        if (hide_ghost && coach != null) {
            if (coach.coach_ghost != null)
                coach.coach_ghost.hide();
            if (coach.path_guide != null)
                coach.path_guide.hide();
        }

        show_message("Ball Feed: ON (every " + feed_interval.ToString("F1") + " s)");
        if (coach != null && coach.dashboard != null)
            coach.dashboard.refresh();
    }

    public void stop_feeding() {
        if (!feeding)
            return;
        feeding = false;
        show_message("Ball Feed: OFF");
        GhostCoach coach = GetComponent<GhostCoach>();
        if (coach != null && coach.dashboard != null)
            coach.dashboard.refresh();
    }

    public void toggle_feeding() {
        if (feeding)
            stop_feeding();
        else
            start_feeding(true);
    }

    public void OnToggleBallFeed() {
        if (!ButtonDedup.accept("ToggleBallFeed"))
            return;
        toggle_feeding();
    }

    public Ball feed_ball() {
        if (play == null || play.balls == null || table == null)
            return null;

        Ball b = play.balls.new_ball();
        if (b == null)
            return null;

        float top_y;
        Vector3 center = TableSpace.center(table, out top_y);
        // TableSpace.center() returns the center of table_top (the near half);
        // shift to the middle of the full table (the net) so far_bounce lands on the far half.
        center.z += 0.5f * table.table_top.transform.lossyScale.z;
        Vector3 spawn = TableSpace.to_world(table.transform,
                                            new Vector3(center.x + spawn_offset.x,
                                                        top_y + spawn_offset.y,
                                                        center.z + spawn_offset.z));
        // Land on the configured drill side, or in line with the player.
        float target_x = compute_target_x(center.x);
        float bounce_x = Mathf.Clamp(target_x, -bounce_x_limit, bounce_x_limit);
        Vector3 bounce = TableSpace.to_world(table.transform,
                                             new Vector3(center.x + bounce_x,
                                                         top_y + b.radius,
                                                         center.z + bounce_z));

        Vector3 line = bounce - spawn;
        Vector3 plane_move = new Vector3(line.x, 0f, line.z);
        float range = plane_move.magnitude;
        Vector3 velocity;
        Vector3 angular_velocity;

        if (range > 0.001f) {
            float vy = b.vertical_speed_for_range(feed_speed, feed_topspin, -line.y, range);
            velocity = feed_speed * plane_move.normalized + vy * Vector3.up;
            Vector3 topspin_axis = Vector3.Cross(velocity, Vector3.up).normalized;
            angular_velocity = (-feed_topspin * topspin_axis) / b.radius;
        } else {
            velocity = Vector3.zero;
            angular_velocity = Vector3.zero;
        }

        last_spawn_position = spawn;
        last_launch_velocity = velocity;

        b.set_motion(new BallState(b, Time.time, spawn, velocity, angular_velocity));
        play.start_ball_feed(b);
        fed_count += 1;
        return b;
    }

    // The player's sideways place in table coordinates: the head if the
    // headset or desktop view is there, else the table's centre line.
    float player_x() {
        Camera head = (play != null && play.passthrough_camera != null ? play.passthrough_camera : Camera.main);
        return (head != null ? TableSpace.to_table(table.transform, head.transform.position).x : 0f);
    }

    float compute_target_x(float center_x) {
        if (target_side == TargetSide.Player)
            return player_x() - center_x;
        bool left = (play != null && play.paddle_hand != null && play.paddle_hand.wand != null && play.paddle_hand.wand.left);
        float fh = (left ? -forehand_x : forehand_x);
        return (target_side == TargetSide.Forehand ? fh : -fh);
    }

    void show_message(string text) {
        Debug.Log("BallFeeder: " + text);
        if (play != null && play.billboard != null)
            play.billboard.text = text;
    }
}
