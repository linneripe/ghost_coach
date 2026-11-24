using UnityEngine;
using UnityEngine.XR;

// Puts the table in front of the player when the headset starts: the room
// is turned and moved (like the "move table" mode does) so the player
// stands at their place beside the coach, facing the table, wherever they
// are in the room (1.5 m from the table centre if there is no coach).
// Runs once, after the headset reports a head position.  Not in desktop mode.
public class TableSpawn : MonoBehaviour {

    public Play play;
    public CoachGhost coach;
    public float distance = 1.5f;       // Meters from the player to the table centre.
    public float settle_time = 1.0f;    // Seconds to wait for tracking to start.

    bool done = false;
    float waited = 0f;

    void Update() {
        if (done)
            return;
        if (DesktopRig.active != null || !XRSettings.isDeviceActive) {
            done = true;
            return;
        }
        waited += Time.deltaTime;
        Camera head = (play.passthrough_camera != null ? play.passthrough_camera : Camera.main);
        Transform rig = play.vr_camera.transform;
        if (head == null || waited < settle_time || head.transform.position.y < 0.3f)
            return;

        // Turn the rig around the head so the player looks along the table (+z).
        Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
            return;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        rig.RotateAround(head.transform.position, Vector3.up, -yaw);
        // Then put the head at the player's place (the table origin is at 0, 0).
        Vector3 p = head.transform.position;
        Vector3 target = (coach != null && coach.has_spot ? coach.player_spot : new Vector3(0f, 0f, -distance));
        rig.position += new Vector3(target.x - p.x, 0f, target.z - p.z);
        done = true;
        Debug.Log("TableSpawn: table placed " + distance + " m in front of the player");
    }
}
