using UnityEngine;

// A dark glass plate behind the floating message text (the billboard), the
// same look as the stroke panel.  It follows the text's size, and is hidden
// when the text is empty.  The edge can be tinted, e.g. by the score.
public class MessagePlate : MonoBehaviour {

    TextMesh text;
    MeshRenderer text_renderer;
    Renderer edge, body;
    Color edge_color = GlassStyle.glass_edge;
    int shown_w = -1, shown_h = -1;
    const float padding = 0.05f, step = 0.01f, radius = 0.045f, line = 0.003f;

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
        text_renderer = t.GetComponent<MeshRenderer>();
        text.color = Color.white;
        // The text is drawn after the plate.
        text_renderer.material.renderQueue = 3020;
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
        bool show = !string.IsNullOrEmpty(text.text) && text_renderer.enabled && text.gameObject.activeInHierarchy;
        edge.enabled = body.enabled = show;
        if (!show)
            return;
        Bounds b = text_renderer.localBounds;
        Vector3 s = transform.lossyScale;
        if (s.x < 1e-5f || s.y < 1e-5f || b.size.x < 1e-6f)
            return;
        // World size, in steps of a centimeter so the texture is not redrawn all the time.
        int w = Mathf.CeilToInt((b.size.x * s.x + 2f * padding) / step);
        int h = Mathf.CeilToInt((b.size.y * s.y + 2f * padding) / step);
        if (w != shown_w || h != shown_h) {
            shown_w = w;
            shown_h = h;
            edge.material.mainTexture = GlassStyle.rounded(w * step, h * step, radius + line, 0f);
            body.material.mainTexture = GlassStyle.rounded(w * step - 2f * line, h * step - 2f * line, radius, 0f);
        }
        Vector3 centre = b.center + new Vector3(0f, 0f, 0.003f / s.z);
        edge.transform.localPosition = centre + new Vector3(0f, 0f, 0.0004f / s.z);
        body.transform.localPosition = centre;
        edge.transform.localScale = new Vector3(w * step / s.x, h * step / s.y, 1f);
        body.transform.localScale = new Vector3((w * step - 2f * line) / s.x, (h * step - 2f * line) / s.y, 1f);
    }
}
