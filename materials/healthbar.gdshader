// Floating health bar (M16, Chunk 79). One billboarded quad drawn as a rounded toon "pill":
// a dark rounded frame, a team-coloured fill that drains right->left, and a soft top
// highlight so it reads glossy/cartoony rather than a flat rectangle. The fill fraction +
// colours are uniforms, so one shared unit-quad mesh + this shader covers every unit cheaply.
//
// The bar geometry is a 1x1 quad sized by the node's scale; `aspect` (width/height) lets the
// rounding stay circular instead of stretching into an ellipse. Unshaded + depth-test-off so
// the bar always reads clearly over the bodies; manual billboard (keeps node scale) in vertex.
shader_type spatial;
render_mode unshaded, depth_test_disabled, cull_disabled, shadows_disabled, depth_draw_never;

uniform float fill : hint_range(0.0, 1.0) = 1.0;
uniform float aspect = 9.0;                                   // bar width / height
uniform vec4 fill_color : source_color = vec4(0.3, 0.8, 0.35, 1.0);
uniform vec4 bg_color : source_color = vec4(0.10, 0.11, 0.14, 1.0);
uniform vec4 border_color : source_color = vec4(0.04, 0.04, 0.06, 1.0);

// Signed distance to a rounded box centred at origin with half-extents b and corner radius r.
float rounded_box(vec2 p, vec2 b, float r) {
	vec2 q = abs(p) - b + r;
	return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - r;
}

void vertex() {
	// Billboard toward the camera while preserving the node's (non-uniform) scale.
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
		INV_VIEW_MATRIX[0] * length(MODEL_MATRIX[0].xyz),
		INV_VIEW_MATRIX[1] * length(MODEL_MATRIX[1].xyz),
		INV_VIEW_MATRIX[2] * length(MODEL_MATRIX[2].xyz),
		MODEL_MATRIX[3]);
}

void fragment() {
	// Work in "height units": x scaled by aspect so corners round as circles, not ovals.
	vec2 p = vec2((UV.x - 0.5) * aspect, UV.y - 0.5);
	vec2 half_outer = vec2(0.5 * aspect, 0.5);
	float r = 0.5;                          // full-height rounding -> pill ends

	float d_outer = rounded_box(p, half_outer, r);
	if (d_outer > 0.0)
		discard;                            // outside the pill

	float border = 0.10;                    // frame thickness in height units
	float d_inner = rounded_box(p, half_outer - vec2(border), r);

	vec3 col;
	float inner = step(d_inner, 0.0);       // 1 inside the fillable area
	if (inner < 0.5) {
		col = border_color.rgb;             // dark frame ring
	} else {
		// Left-anchored fill: UV.x below the fraction is "full".
		float filled = step(UV.x, fill);
		col = mix(bg_color.rgb, fill_color.rgb, filled);
		// Soft toon highlight across the top third + a touch of shade at the bottom.
		float shine = smoothstep(0.0, -0.5, p.y) * 0.35;        // brighter near top (p.y<0)
		float shade = smoothstep(0.0, 0.5, p.y) * 0.25;         // darker near bottom
		col += (shine - shade) * filled;
	}

	ALBEDO = clamp(col, 0.0, 1.0);
}
