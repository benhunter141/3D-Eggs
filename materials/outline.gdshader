// Inverted-hull outline for the egg bodies (M16, Chunk 77).
//
// Rendered on a SECOND copy of the egg mesh that shows only its BACK faces
// (cull_front), grown outward along the normals and drawn flat dark + unshaded.
// Because the hull is pushed out past the real egg only at the silhouette, it
// reads as a crisp dark line ringing each unit — the classic toon outline, and
// dirt cheap on the GL-Compatibility renderer (no post-process / edge detect).
shader_type spatial;
render_mode cull_front, unshaded, shadows_disabled, depth_draw_opaque;

uniform float outline_width : hint_range(0.0, 0.2) = 0.045;
uniform vec4 outline_color : source_color = vec4(0.06, 0.05, 0.08, 1.0);

void vertex() {
	VERTEX += normalize(NORMAL) * outline_width;
}

void fragment() {
	ALBEDO = outline_color.rgb;
}
