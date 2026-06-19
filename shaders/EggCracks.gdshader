// Health-as-cracks overlay for the egg shell (driven by Unit.cs).
//
// Rendered as a next_pass over a unit's body material, so the base colour + the
// hit-flash keep working untouched and this pass only paints dark crack lines on
// top. `damage` is 0 at full health (pristine shell, nothing drawn) and rises to 1
// near death (an extreme crack network).
//
// The shell is broken into voronoi "plates"; every plate boundary is a potential
// crack. Each plate gets a random APPEARANCE THRESHOLD, so a crack only shows once
// `damage` climbs past that plate's threshold — at 90% HP just the lowest-threshold
// few hairlines show, and the network spreads + thickens as HP drains. A finer
// second layer of sub-cracks fades in past the halfway mark for the shattered look.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, unshaded;

// 0 = pristine, 1 = about to die. Set per-frame-of-damage by Unit.cs.
uniform float damage : hint_range(0.0, 1.0) = 0.0;
// Per-unit random offset so no two eggs crack the same way.
uniform vec2 seed = vec2(0.0);
// How many shell plates span the egg's UV (more = finer plates).
uniform float plate_scale = 5.0;
// Crack colour (a dark recessed groove in the shell).
uniform vec3 crack_color : source_color = vec3(0.03, 0.02, 0.02);

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}

// Voronoi edge distance F2-F1 (≈0 on a plate boundary). Also returns a stable
// random value for the nearest plate so each plate can have its own crack threshold.
float voronoi_edge(vec2 uv, out float cell_rand) {
	vec2 g = floor(uv);
	vec2 f = fract(uv);
	float f1 = 8.0;
	float f2 = 8.0;
	vec2 cell = g;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 p = o + hash2(g + o) - f;
			float d = dot(p, p);
			if (d < f1) {
				f2 = f1;
				f1 = d;
				cell = g + o;
			} else if (d < f2) {
				f2 = d;
			}
		}
	}
	cell_rand = hash2(cell + 17.0).x;
	return sqrt(f2) - sqrt(f1);
}

void fragment() {
	vec2 uv = UV * plate_scale + seed;

	// Primary cracks: plate boundaries, each gated by its own random threshold and
	// thickening as damage rises.
	float cell_rand;
	float edge = voronoi_edge(uv, cell_rand);
	float appear = smoothstep(cell_rand - 0.06, cell_rand + 0.06, damage);
	float width = mix(0.012, 0.06, damage);
	float crack = (1.0 - smoothstep(0.0, width, edge)) * appear;

	// Secondary sub-cracks: a finer network that only fades in past half damage,
	// turning a few clean splits into a shattered shell near death.
	float sub_rand;
	float edge_b = voronoi_edge(uv * 2.4 + vec2(7.3, 1.9), sub_rand);
	float appear_b = smoothstep(sub_rand - 0.06, sub_rand + 0.06, (damage - 0.5) * 2.0);
	float width_b = mix(0.008, 0.035, damage);
	float crack_b = (1.0 - smoothstep(0.0, width_b, edge_b)) * appear_b * smoothstep(0.45, 0.6, damage);

	crack = max(crack, crack_b);
	// Hard gate so a full-health shell is perfectly clean.
	crack *= smoothstep(0.0, 0.04, damage);

	ALBEDO = crack_color;
	ALPHA = clamp(crack, 0.0, 1.0);
}
