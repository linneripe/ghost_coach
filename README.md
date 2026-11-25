# PingPang
Virtual reality ping pong aiming for maximum physics fidelity.

Uses Unity XR and targets standalone Meta Quest headsets.

https://github.com/user-attachments/assets/65bbd361-80c7-4f36-8441-1f453e02a7d6

## How to Use Ghost Coach

Ghost Coach is a two-phase VR table tennis training mode:

1. **Phase 1 — Watch the Coach:** Stand beside the ghost coach and watch its stroke form.
2. **Phase 2 — Play with Path Guide:** Step into the hitting position and play against the robot while following the coach's paddle path. After each stroke, you receive a score and a short tip (plus optional haptic feedback on your free hand).

### VR Controller Controls (Right-Handed)
*Left-handed players have the left and right hand bindings swapped automatically, except the Menu button which stays on the left controller.*

**Paddle Hand (Right):**
- **A**: Switch between **Phase 1 (Watch Coach)** and **Phase 2 (Play with Path Guide)**
- **B**: Cycle to the next stroke (or start/stop recording a custom coach clip when `allow_recording` is enabled in `experiment.json`)
- **Thumbstick Click**: Cycle coach playback speed (`100%` / `50%` / `25%`)
- **Trigger**: Toggle automatic ball feed

**Free Hand (Left):**
- **Trigger**: Robot serve
- **Grip** or **Y**: Hold a ball in hand
- **X**: Show/hide the racket and ball in hand
- **Menu**: Open settings menu
- **Hold Thumbstick (1.5s)**: Start a new participant/experiment session

### Desktop Controls (No Headset)
When running in the Unity Editor without a VR headset, `DesktopRig` activates automatically (press **H** to toggle the on-screen help):
- **Mouse / Scroll**: Move paddle / adjust paddle distance
- **Right Drag + Arrow Keys (Q/E)**: Look around and move
- **P**: Switch between Coach and Path phases
- **K**: Next stroke (or record if enabled)
- **L**: Cycle coach speed (`100%` / `50%` / `25%`)
- **R** or **S**: Robot serve (**F** toggles automatic ball feed)
- **G**: Show/hide racket and ball in hand
- **V**: Switch right/left-handed mode
- **N**: Start next test participant session
- **Tab**: Open settings menu (**Left Click** to press buttons)
- **Backspace**: Reset phase, speed, and view

## Problems

- Inaccurate hand controller tracking.  This has been the biggest obstacle.  The tracking during fast motions is frequently wrong by more than 10 cm, while the typical table tennis paddle face radius is about 8 cm.  So a motion that should strike the ball in the middle of the paddle completely misses the ball in the VR simulation.

- Hand controller acceleration limit.  Hand controllers on all headsets I have tried prior to Quest 3 have an inertial measurement unit with maximum acceleration of 16g.  It is quite easy to exceed that acceleration in which case tracking is lost producing the wrong paddle motion.  Testing shows that all of these hand controllers have this severe limitation: Meta Quest Pro, Meta Quest 2, Valve Index, Oculus Rift S, Vive Pro, Vive, Oculus Rift.  It appears the Meta Quest 3 is the first system I have tried that has inertial measurement unit with a higher lijmit of 32g from the IMU data sheet.  Testing with this ping pong applications appears to show much more faithful tracking at high accelerations.

- Limited field of view.  Surprisingly the 100 degree field of view of many VR headsets is a very noticeable limitation playing ping pong.  Looking at photos of professional table tennis players serving shows they often are looking at the ball with eye pupils far from the center of the eye.  That makes sense since fast head motions to track the ball would be impractical.  The Quest 3 has binocular overlap of just 80 degrees, while human eyes achieve about 120 degrees.  So in the headset when the ball is outside 40 degrees of the headset pointing direction at most one eye sees the ball.

- Limited frame rate.  The ping pong ball often travels faster than 10 meters per second.  At the typical 72 - 144 Hz refresh rate of VR headsets the ball moves about 10 cm between frames when moving at 10 m/s.  The ball is only 4 cm in diameter, so the motion has the ball jumping with gaps of 6 cm each frame.  This is not visually too apparent, because the brain adapts to it.  But I think it creates subconcious discomfort, possibly because the eye retina no longer perceives a continuous motion and higher level brain processing makes it appear smooth.

Developed since 2016.
