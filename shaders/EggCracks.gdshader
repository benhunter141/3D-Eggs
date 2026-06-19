// Health-as-cracks overlay for the egg shell (driven by Unit.cs).
//
// Rendered as a next_pass over a unit's body material, so the base colour + the
// hit-flash keep working untouched and this pass only paints dark crack lines on
// top. `damage` is 0 at full health (pristine shell, nothing drawn) and rises to 1
// near death (an extreme crack network).
//
// `style` selects the LOOK (we're trying a few — see the plan):
//   2 = Cartoon stages  : hand-authored bold cracks that snap in at HP stages (the default).
//   1 = Bold branching  : a few thick jagged lines that fork from a point (kept for compare).
//   0 = Voronoi         : the original dense cellular net (kept for compare).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, unshaded;

uniform int style = 2;
// 0 = pristine, 1 = about to die. Set per-frame-of-damage by Unit.cs.
uniform float damage : hint_range(0.0, 1.0) = 0.0;
// Per-unit random offset so no two eggs crack the same way.
uniform vec2 seed = vec2(0.0);
// Crack colour (a dark recessed groove in the shell).
uniform vec3 crack_color : source_color = vec3(0.03, 0.02, 0.02);
// Voronoi-only: how many shell plates span the egg's UV.
uniform float plate_scale = 5.0;

// --- Bold-branching tuning knobs (live-tunable from the CrackTest harness) ---
uniform float crack_thickness = 0.018;   // line half-width in UV (bigger = chunkier)
uniform float crack_jitter = 0.9;        // heading wobble per step (bigger = more jagged)
uniform float crack_seg_len = 0.05;      // step length per segment (bigger = longer strides)
uniform int crack_count = 4;             // how many cracks at full death (1..MAX_CRACKS)
uniform float crack_grow = 5.0;          // max segments a crack grows out to near death
uniform bool crack_branches = true;      // whether cracks fork a side branch

const int MAX_CRACKS = 6;
const int MAX_SEG = 6;

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

	int count = clamp(crack_count, 1, MAX_CRACKS);
	for (int i = 0; i < MAX_CRACKS; i++) {
		if (i >= count) {
			break;
		}
		float fi = float(i);
		// This crack only appears once damage passes its staggered threshold.
		float ti = fi / float(count) * 0.8;
		float appear = smoothstep(ti - 0.06, ti + 0.06, dmg);
		if (appear <= 0.0) {
			continue;
		}
		// How far this crack has grown out (1 segment at its threshold -> crack_grow near death).
		float grow = mix(1.0, crack_grow, clamp((dmg - ti) / max(1.0 - ti, 0.001), 0.0, 1.0));

		vec2 h = hash2(seed + vec2(fi * 7.31, 3.7));
		vec2 origin = vec2(0.18 + 0.64 * h.x, 0.12 + 0.62 * h.y);  // front/upper-ish, off the seam
		float dir = hash2(seed + vec2(fi * 5.1, 9.2)).x * 6.2831853;
		float thick = crack_thickness * mix(0.8, 1.2, dmg);

		vec2 p = origin;
		float dmin = 1e9;
		for (int k = 0; k < MAX_SEG; k++) {
			vec2 hk = hash2(seed + vec2(fi * 13.0 + float(k), 2.0));
			dir += (hk.x - 0.5) * crack_jitter;                    // jitter heading -> jagged
			vec2 q = p + vec2(cos(dir), sin(dir)) * crack_seg_len;
			float w = clamp(grow - float(k), 0.0, 1.0);            // segment grows in over k..k+1
			if (w > 0.0) {
				dmin = min(dmin, seg_dist(uv, p, mix(p, q, w)));   // growing tip
			}
			// Fork a short branch off the middle segment once the crack is long enough.
			if (crack_branches && k == 2 && grow > 2.5) {
				float bdir = dir + (hk.y > 0.5 ? 1.0 : -1.0) * 0.9;
				vec2 b = p;
				for (int j = 0; j < 2; j++) {
					vec2 hb = hash2(seed + vec2(fi * 17.0 + float(j), 5.0));
					bdir += (hb.x - 0.5) * 0.8;
					vec2 bn = b + vec2(cos(bdir), sin(bdir)) * crack_seg_len * 0.8;
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

// ---- Style 2: Cartoon crack stages -------------------------------------------------
// HAND-AUTHORED bold cracks (not procedural noise) that SNAP IN at HP thresholds, like a
// comic-book egg breaking step by step. A few deliberate jagged polylines on the front of
// the shell, each gated to a damage stage: ~90% HP a small crack, then more + a central
// shatter-star near death. Drawn thick + crisp (fwidth AA) so it reads as intentional.
// A seed-driven left/right mirror gives two variants so a crowd isn't all identical.
float cartoon_cracks(vec2 uv_in, float dmg) {
	// Mirror half the units so the same authored shapes don't repeat across a crowd.
	vec2 uv = uv_in;
	if (hash2(seed + vec2(1.7, 4.2)).x > 0.5) {
		uv.x = 1.0 - uv.x;
	}

	float aa = fwidth(uv.x) * 1.2 + 0.001;
	float thick = 0.020;     // bold, constant width
	float d = 1e9;

	// Stage 1 — appears ~90% HP (dmg >= 0.10): one small zig-zag crack, top-centre.
	if (dmg >= 0.10) {
		d = min(d, seg_dist(uv, vec2(0.50, 0.20), vec2(0.46, 0.28)));
		d = min(d, seg_dist(uv, vec2(0.46, 0.28), vec2(0.53, 0.34)));
		d = min(d, seg_dist(uv, vec2(0.53, 0.34), vec2(0.48, 0.43)));
	}
	// Stage 2 — ~65% HP (dmg >= 0.35): a second crack with a fork, to the right.
	if (dmg >= 0.35) {
		d = min(d, seg_dist(uv, vec2(0.62, 0.30), vec2(0.58, 0.39)));
		d = min(d, seg_dist(uv, vec2(0.58, 0.39), vec2(0.65, 0.47)));
		d = min(d, seg_dist(uv, vec2(0.65, 0.47), vec2(0.60, 0.56)));
		d = min(d, seg_dist(uv, vec2(0.58, 0.39), vec2(0.51, 0.45)));   // fork
	}
	// Stage 3 — ~40% HP (dmg >= 0.60): a longer crack down the left.
	if (dmg >= 0.60) {
		d = min(d, seg_dist(uv, vec2(0.38, 0.24), vec2(0.44, 0.33)));
		d = min(d, seg_dist(uv, vec2(0.44, 0.33), vec2(0.36, 0.41)));
		d = min(d, seg_dist(uv, vec2(0.36, 0.41), vec2(0.43, 0.50)));
		d = min(d, seg_dist(uv, vec2(0.43, 0.50), vec2(0.37, 0.60)));
	}
	// Stage 4 — ~15% HP (dmg >= 0.85): a central shatter-star (impact burst).
	if (dmg >= 0.85) {
		vec2 c = vec2(0.50, 0.44);
		d = min(d, seg_dist(uv, c, vec2(0.50, 0.28)));
		d = min(d, seg_dist(uv, c, vec2(0.40, 0.40)));
		d = min(d, seg_dist(uv, c, vec2(0.61, 0.39)));
		d = min(d, seg_dist(uv, c, vec2(0.45, 0.58)));
		d = min(d, seg_dist(uv, c, vec2(0.57, 0.59)));
	}

	return 1.0 - smoothstep(thick - aa, thick + aa, d);
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
	float crack =
		(style == 0) ? voronoi_cracks(UV, damage) :
		(style == 1) ? branching_cracks(UV, damage) :
		cartoon_cracks(UV, damage);
	// Hard gate so a full-health shell is perfectly clean.
	crack *= smoothstep(0.0, 0.04, damage);

	ALBEDO = crack_color;
	ALPHA = clamp(crack, 0.0, 1.0);
}
