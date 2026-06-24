// Shared toon ground shader (M16, Chunk 78).
//
// GL-Compatibility-safe: a world-space two-tone CHECKERBOARD plus soft GRID lines
// turn the flat solid-colour plane into a stylised toon surface, and a gentle 2-band
// light() keeps it cohesive with the cel-shaded units without harsh banding across a
// big plane. Per-level tint lives in each scene's ShaderMaterial (base/alt/grid), so
// one shader dresses grass arenas, dirt pens, and stone floors alike.
shader_type spatial;
render_mode cull_back, specular_disabled, diffuse_lambert;

uniform vec4 base_color : source_color = vec4(0.26, 0.34, 0.24, 1.0);
uniform vec4 alt_color : source_color = vec4(0.30, 0.39, 0.27, 1.0);
uniform vec4 grid_color : source_color = vec4(0.16, 0.21, 0.15, 1.0);
uniform float cell_size = 4.0;          // metres per checker cell
uniform float line_width : hint_range(0.0, 0.3) = 0.05;   // fraction of a cell
uniform float grid_strength : hint_range(0.0, 1.0) = 0.5;
uniform float checker_strength : hint_range(0.0, 1.0) = 0.5;

// Gentle 2-band cel ramp so the ground shares the units' stepped look.
uniform float band_floor : hint_range(0.0, 1.0) = 0.55;
uniform float edge : hint_range(0.0, 1.0) = 0.5;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 uv = world_pos.xz / cell_size;

	// Two-tone checkerboard.
	vec2 cell = floor(uv);
	float checker = mod(cell.x + cell.y, 2.0);
	vec3 col = mix(base_color.rgb, alt_color.rgb, checker * checker_strength);

	// Soft grid lines at cell boundaries.
	vec2 f = fract(uv);
	vec2 d = min(f, 1.0 - f);
	float line = 1.0 - smoothstep(0.0, line_width, min(d.x, d.y));
	col = mix(col, grid_color.rgb, line * grid_strength);

	ALBEDO = col;
	ROUGHNESS = 1.0;
}

void light() {
	float ndl = clamp(dot(normalize(NORMAL), normalize(LIGHT)), 0.0, 1.0);
	float ramp = ndl < edge ? band_floor : 1.0;
	DIFFUSE_LIGHT += ATTENUATION * LIGHT_COLOR.rgb * ALBEDO * ramp;
}
