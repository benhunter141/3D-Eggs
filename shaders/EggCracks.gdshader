// Health-as-cracks overlay for the egg shell (driven by Unit.cs).
//
// Rendered as a next_pass over a unit's body material, so the base colour + the
// hit-flash keep working untouched and this pass only paints dark crack lines on
// top. `damage` is 0 at full health (pristine shell, nothing drawn) and rises to 1
// near death (an extreme crack network).
//
// `style` selects the LOOK (we're trying a few — see the plan):
//   1 = Bold branching  : a FEW thick jagged lines that fork outward from a point,
//                         growing in count + length as HP drops (the default).
//   0 = Voronoi         : the original dense cellular net (kept for comparison).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, unshaded;

uniform int style = 1;
// 0 = pristine, 1 = about to die. Set per-frame-of-damage by Unit.cs.
uniform float damage : hint_range(0.0, 1.0) = 0.0;
// Per-unit random offset so no two eggs crack the same way.
uniform vec2 seed = vec2(0.0);
// Crack colour (a dark recessed groove in the shell).
uniform vec3 crack_color : source_color = vec3(0.03, 0.02, 0.02);
// Voronoi-only: how many shell plates span the egg's UV.
uniform float plate_scale = 5.0;

const int MAX_CRACKS = 4;

// Sin-free hash (Dave Hoskins) — stable at the Compatibility renderer's float precision,
// unlike fract(sin(p)*big) which quantised into blocky steps on older GPUs.
vec2 hash2(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

// Distance from point p to segment a-b.
float seg_dist(vec2 p, vec2 a, vec2 b) {
	vec2 pa = p - a;
	vec2 ba = b - a;
	float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
	return length(pa - ba * h);
}

// ---- Style 1: Bold branching cracks ------------------------------------------------
// A handful of jagged polylines, each a short random walk radiating from a per-unit
// seeded origin (with one forked branch), drawn as a min-distance field. Cracks pop in
// progressively (each has its own appearance threshold) and grow segment-by-segment as
// damage rises, so ~90% HP shows one short crack and near death shows 3-4 long forks.
float branching_cracks(vec2 uv, float dmg) {
	float aa = fwidth(uv.x) * 1.5 + 0.0012;
	float result = 0.0;

	for (int i = 0; i < MAX_CRACKS; i++) {
		float fi = float(i);
		// This crack only appears once damage passes its staggered threshold.
		float ti = fi / float(MAX_CRACKS) * 0.8;
		float appear = smoothstep(ti - 0.06, ti + 0.06, dmg);
		if (appear <= 0.0) {
			continue;
		}
		// How far this crack has grown out (1 segment at its threshold -> 5 near death).
		float grow = mix(1.0, 5.0, clamp((dmg - ti) / max(1.0 - ti, 0.001), 0.0, 1.0));

		vec2 h = hash2(seed + vec2(fi * 7.31, 3.7));
		vec2 origin = vec2(0.18 + 0.64 * h.x, 0.12 + 0.62 * h.y);  // front/upper-ish, off the seam
		float dir = hash2(seed + vec2(fi * 5.1, 9.2)).x * 6.2831853;
		float seg_len = 0.05;
		float thick = mix(0.012, 0.024, dmg);

		vec2 p = origin;
		float dmin = 1e9;
		for (int k = 0; k < 5; k++) {
			vec2 hk = hash2(seed + vec2(fi * 13.0 + float(k), 2.0));
			dir += (hk.x - 0.5) * 0.9;                              // jitter heading -> jagged
			vec2 q = p + vec2(cos(dir), sin(dir)) * seg_len;
			float w = clamp(grow - float(k), 0.0, 1.0);            // segment grows in over k..k+1
			if (w > 0.0) {
				dmin = min(dmin, seg_dist(uv, p, mix(p, q, w)));   // growing tip
			}
			// Fork a short branch off the middle segment once the crack is long enough.
			if (k == 2 && grow > 2.5) {
				float bdir = dir + (hk.y > 0.5 ? 1.0 : -1.0) * 0.9;
				vec2 b = p;
				for (int j = 0; j < 2; j++) {
					vec2 hb = hash2(seed + vec2(fi * 17.0 + float(j), 5.0));
					bdir += (hb.x - 0.5) * 0.8;
					vec2 bn = b + vec2(cos(bdir), sin(bdir)) * seg_len * 0.8;
					float bw = clamp(grow - 2.5 - float(j), 0.0, 1.0);
					if (bw > 0.0) {
						dmin = min(dmin, seg_dist(uv, b, mix(b, bn, bw)));
					}
					b = bn;
				}
			}
			p = q;
		}

		float line = (1.0 - smoothstep(thick - aa, thick + aa, dmin)) * appear;
		result = max(result, line);
	}
	return result;
}

// ---- Style 0: Voronoi net (legacy, kept for comparison) ----------------------------
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

float crack_line(float edge, float width) {
	float aa = fwidth(edge) * 1.25 + 0.0008;
	return 1.0 - smoothstep(width - aa, width + aa, edge);
}

float voronoi_cracks(vec2 uv_in, float dmg) {
	vec2 uv = uv_in * plate_scale + seed;
	float cell_rand;
	float edge = voronoi_edge(uv, cell_rand);
	float appear = smoothstep(cell_rand - 0.06, cell_rand + 0.06, dmg);
	float width = mix(0.03, 0.11, dmg);
	float crack = crack_line(edge, width) * appear;

	float sub_rand;
	float edge_b = voronoi_edge(uv * 2.4 + vec2(7.3, 1.9), sub_rand);
	float appear_b = smoothstep(sub_rand - 0.06, sub_rand + 0.06, (dmg - 0.5) * 2.0);
	float crack_b = crack_line(edge_b, mix(0.02, 0.07, dmg)) * appear_b * smoothstep(0.45, 0.6, dmg);
	return max(crack, crack_b);
}

void fragment() {
	float crack = (style == 0) ? voronoi_cracks(UV, damage) : branching_cracks(UV, damage);
	// Hard gate so a full-health shell is perfectly clean.
	crack *= smoothstep(0.0, 0.04, damage);

	ALBEDO = crack_color;
	ALPHA = clamp(crack, 0.0, 1.0);
}
