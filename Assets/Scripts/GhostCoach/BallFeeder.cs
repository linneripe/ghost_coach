using UnityEngine;

// Ball-feeding game mode: spawns a ball above the far end of the table every
// `feed_interval` seconds and sends it over the net so it bounces once on the
// player's half, in line with the player, and then rises to a height where it
// is easy to hit, with little spin.  Runs on a fixed timer regardless of
// whether or how the player returns the ball.
public class BallFeeder : MonoBehaviour {

    public Play play;
    public Table table;

    public float feed_interval = 3.0f;     // Seconds between feeds.
    public float feed_speed = 5.0f;        // Horizontal speed (m/s).
    public float feed_topspin = 1.0f;      // Mild topspin (m/s at ball surface).

    // Coordinates relative to the full table center (at the net; y relative to top_y).
    public Vector3 spawn_offset = new Vector3(0.0f, 0.35f, 1.45f);
    public float bounce_z = -1.05f;        // Where the ball lands on the player's half, m from the net.
    public float bounce_x_limit = 0.60f;   // Landing spot stays this far from the table's centre line.
    // The ball comes in at an angle that follows the coach's stroke: toward
    // the paddle side for a forehand (+1), toward the other side for a
    // backhand (-1), straight for 0.
    public float stroke_side = 0f;
    public float stroke_shift = 0.30f;     // Meters the landing spot moves to that side.
    public float stroke_angle = 0.45f;     // Meters the ball starts to the other side of the landing spot.

    public bool feeding { get; private set; }
    public int fed_count { get; private set; }
    public Vector3 last_spawn_position { get; private set; }
    public Vector3 last_launch_velocity { get; private set; }

    float timer = 0f;

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
        if (feeding)
            return;
        feeding = true;
        timer = 0f;

        if (play != null) {
            play.robot_active = false;
            if (play.robot != null)
                play.robot.auto_serve = false;
        }

        // The coach stays beside the player; only the robot rests.
        GhostCoach coach = GetComponent<GhostCoach>();
        if (coach != null)
            coach.update_robot();

        show_message("Ball Feed: ON (every " + feed_interval.ToString("F1") + " s)");
    }

    public void stop_feeding() {
        if (!feeding)
            return;
        feeding = false;
        GhostCoach coach = GetComponent<GhostCoach>();
        if (coach != null)
            coach.update_robot();
        show_message("Ball Feed: OFF");
    }

    public void toggle_feeding() {
        if (feeding)
            stop_feeding();
        else
            start_feeding();
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
        // Land near the player, so the ball comes to where they stand, moved to
        // the side of the stroke, and start on the other side: a diagonal ball.
        float paddle_x = stroke_side * (play != null && play.paddle_hand.wand.left ? -1f : 1f);
        float near_player = Mathf.Clamp(player_x() - center.x, -0.35f, 0.35f);
        float bounce_x = Mathf.Clamp(near_player + paddle_x * stroke_shift, -bounce_x_limit, bounce_x_limit);
        float spawn_x = bounce_x - paddle_x * stroke_angle;
        Vector3 spawn = TableSpace.to_world(table.transform,
                                            new Vector3(center.x + spawn_offset.x + spawn_x,
                                                        top_y + spawn_offset.y,
                                                        center.z + spawn_offset.z));
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

    void show_message(string text) {
        Debug.Log("BallFeeder: " + text);
        if (play != null && play.billboard != null)
            play.billboard.text = text;
    }
}
