# Gameplay

This file describes the gameplay, training modes, and features implemented in the project.

## Core Table Tennis Features

- **Physics-Based VR Ping Pong:** Targets standalone Meta Quest headsets with support for both **Mixed Reality (Passthrough)** and **Virtual Room (VR)** modes.
- **Robot Opponent:** Serves and returns balls across the table (supports single manual serves and continuous auto-serve).
- **Ball Handling:** Players can spawn/hold a ball in their free hand and toss it to serve.
- **Ergonomics & Setup:**
  - **Right/Left-Handed Support:** Switching handedness automatically mirrors controller bindings and coach demonstrations.
  - **Move Table & Adjust Grip:** Reposition the table in physical space and fine-tune how the paddle sits in the hand.
  - **Hide Hand Visuals:** Toggle visibility of the virtual racket and ball in hand (persisted between sessions, useful in passthrough).
- **Desktop Mode (`DesktopRig`):** Automatically activates in the Unity Editor when no VR headset is connected, allowing full play and testing with mouse and keyboard.

## Ghost Coach Training Loop

Ghost Coach is a two-phase training system built on top of the table tennis simulation:

### Phase 1 — Watch the Coach
1. A semi-transparent ghost coach stands at the table's center line and demonstrates the selected stroke in a continuous loop (using bundled mocap skeletons, Quest-recorded clips, or a built-in demo forehand).
2. The player stands beside the coach to observe technique from the same perspective.
3. Playback speed can be cycled between **100%**, **50%**, and **25%** to study fast motions in detail.
4. The player can cycle between strokes in the loaded clip (e.g., forehand, backhand) or switch to Phase 2 at any time.

### Phase 2 — Play with Path Guide & Stroke Feedback
1. The player steps into the hitting position and plays against the robot.
2. The coach's reference paddle path and contact pose are displayed in 3D space at the player's hitting position.
3. After each ball contact, the player's actual swing path is drawn next to the coach's path for visual comparison.
4. **Automatic Stroke Scoring (`StrokeCompare`):** Each stroke is scored from **0–100** based on:
   - **Path shape** (50%): RMS difference between the player's and coach's paddle trajectories around contact.
   - **Paddle face angle** (30%): Angle difference at ball contact.
   - **Swing speed** (20%): Ratio of player paddle speed to coach speed at contact.
5. **Coaching Feedback:** Displays the score and a single focused tip (e.g., paddle angle, swing speed, or swing direction) on the billboard, paired with optional haptic feedback on the free hand (one long pulse for a good stroke, two short pulses when a tip is given).

## Additional Training & Research Features

- **Ball Feeder Mode (`BallFeeder`):** Standalone timed ball-feeding mode that launches balls with mild topspin onto the player's half of the table at a fixed interval (default 3.0s) in line with the player's position, independent of Ghost Coach visuals.
- **Custom Coach Recording (`MotionRecorder`):** When `allow_recording` is enabled in `experiment.json`, players/coaches can record their own motion clips directly in VR.
- **Experiment & Telemetry System (`Experiment`, `ExperimentConfig`):** Supports configurable A/B variant blocks (comparing feedback channels, coach viewing positions, and MR/VR), automatic participant counterbalancing, and per-stroke CSV session logging.
