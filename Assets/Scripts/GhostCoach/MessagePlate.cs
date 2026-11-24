using UnityEngine;

// A dark glass plate behind the floating message text (the billboard), the
// same look as the stroke panel.  It follows the text's size, and is hidden
// when the text is empty.  The edge can be tinted, e.g. by the score.
public class MessagePlate : MonoBehaviour {

    public bool show_plate = false;          // Off: the black text stands on its own.
    TextMesh text;
    PixelText pixels;
    Renderer edge, body;
    Color edge_color = GlassStyle.glass_edge;
    int shown_w = -1, shown_h = -1;
    float shown_dot = -1f;
    // In dots of the text, so the plate keeps its look at any size of the billboard.
    const float padding_dots = 4f, step_dots = 0.5f, radius_dots = 3.5f, line_dots = 0.35f;

    public static MessagePlate attach(TextMesh t) {
        MessagePlate p = t.gameObject.GetComponent<MessagePlate>();
        if (p == null) {
            p = t.gameObject.AddComponent<MessagePlate>();
            p.build(t);
        }
        return p;
    }

    void build(TextMesh t) {
        text = t;
        pixels = PixelText.attach(t);        // The text is drawn as dots, after the plate.
        Material source = GhostVisuals.material("ghost");
        edge = GlassStyle.quad("message edge", transform, source, null, GlassStyle.glass_edge, 3000);
        body = GlassStyle.quad("message plate", transform, source, null, GlassStyle.glass, 3001);
    }

    public void tint(Color? c) {
        edge_color = (c.HasValue ? c.Value : GlassStyle.glass_edge);
        if (edge != null)
            edge.material.color = edge_color;
    }

    void LateUpdate() {
        bool show = pixels.has_text && text.gameObject.activeInHierarchy;
        edge.enabled = body.enabled = show && show_plate;
        if (!show || !show_plate)
            return;
        Vector3 s = transform.lossyScale;
        if (s.x < 1e-5f || s.y < 1e-5f)
            return;
        float dot = pixels.cell_world;
        float padding = padding_dots * dot, step = step_dots * dot, radius = radius_dots * dot, line = line_dots * dot;
        // World size, in steps of a centimeter so the texture is not redrawn all the time.
        int w = Mathf.CeilToInt((pixels.world_size.x + 2f * padding) / step);
        int h = Mathf.CeilToInt((pixels.world_size.y + 2f * padding) / step);
        if (w != shown_w || h != shown_h || dot != shown_dot) {
            shown_w = w;
            shown_h = h;
            shown_dot = dot;
            edge.material.mainTexture = GlassStyle.rounded(w * step, h * step, radius + line, 0f);
            body.material.mainTexture = GlassStyle.rounded(w * step - 2f * line, h * step - 2f * line, radius, 0f);
        }
        Vector3 centre = pixels.local_center + new Vector3(0f, 0f, 0.6f * dot / s.z);
        edge.transform.localPosition = centre + new Vector3(0f, 0f, 0.1f * dot / s.z);
        body.transform.localPosition = centre;
        edge.transform.localScale = new Vector3(w * step / s.x, h * step / s.y, 1f);
        body.transform.localScale = new Vector3((w * step - 2f * line) / s.x, (h * step - 2f * line) / s.y, 1f);
    }
}
