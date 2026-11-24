# QTM tools

QTM JSON exports of a take (File > Export > To JSON) go in `pro-mocap-data/`.
The converters turn one into a coach clip for GhostCoach, without Blender.

## `convert_qtm_clip.py` (use this one)

Needs numpy. Reads the skeleton, the racket rim markers (`Racket1`-`Racket5`),
the table corner markers (`TableL`, `TableR`) and the wrist markers, so the
racket's position and angle and the table's place are measured.

```
python3 Tools/qtm/convert_qtm_clip.py pro-mocap-data/backhand_drive0009.json \
    Assets/Resources/GhostCoach/clips/backhand_drive0009.json
```

The clip file name decides which stroke it is in the stroke panel: it must
start with `forehand`, `forehand_drive`, `backhand` or `backhand_drive`,
optionally followed by digits (for example `backhand_drive0009`).

## `qtm_json_to_clip.py` (provisional)

For an export with the skeleton only (no racket or table markers): the racket
is the hand plus an offset, and the table is placed in front of the player's
stance. The racket's face angle is assumed. Prefer `convert_qtm_clip.py` when
the markers are in the take.
