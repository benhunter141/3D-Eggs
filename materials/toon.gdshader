// Shared toon / cel shader for every unit's egg body (M16, Chunk 76).
//
// GL-Compatibility-safe (no Vulkan-only features): a custom light() quantizes the
// sun's N·L into a few hard BANDS for the stepped cel look, and fragment() adds a
// fresnel RIM so each egg pops against the ground and its neighbours. The team tint
// is a per-instance `base_color` uniform (set in each unit scene), so one shared
// shader dresses the whole roster while every archetype keeps its colour.
//
// Unit.cs duplicates this material per instance at runtime and drives `flash` on a
// hit (white pop) and hangs the EggCracks shell-damage shader off it as a next_pass —
// both keep working because flash is a uniform here, not StandardMaterial emission.
shader_type spatial;
render_mode cull_back, specular_disabled, diffuse_lambert;

// Team colour, set per-unit in the scene (falls back to a neutral egg tone).
uniform vec4 base_color : source_color = vec4(0.82, 0.78, 0.62, 1.0);

// Hit-flash, driven 0..1 by Unit.cs; pops the body toward flash_color.
uniform float flash : hint_range(0.0, 1.0) = 0.0;
uniform vec4 flash_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

// Cel banding: a hard 3-step ramp. band_floor lifts the darkest band so shadowed
// sides keep the tint; shadow_tint cools/darkens the unlit band so the light/shadow
// split reads even under ambient fill.
uniform float band_floor : hint_range(0.0, 1.0) = 0.22;
uniform float band_mid : hint_range(0.0, 1.0) = 0.6;
uniform float edge1 : hint_range(0.0, 1.0) = 0.27;   // shadow -> mid threshold
uniform float edge2 : hint_range(0.0, 1.0) = 0.62;   // mid -> lit threshold
uniform vec3 shadow_tint = vec3(0.78, 0.82, 1.0);    // cool the shadow band

// Fresnel rim light.
uniform vec4 rim_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float rim_strength : hint_range(0.0, 2.0) = 0.7;
uniform float rim_power : hint_range(0.5, 8.0) = 2.5;

void fragment() {
	ALBEDO = mix(base_color.rgb, flash_color.rgb, flash * 0.85);
	ROUGHNESS = 1.0;

	// Fresnel rim: bright where the surface turns away from the camera.
	float fres = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), rim_power);
	// Rim plus an additive white burst while flashing.
	EMISSION = rim_color.rgb * fres * rim_strength + flash_color.rgb * flash * 1.5;
}

void light() {
	// Hard 3-step toon ramp off the sun's lambert term — crisp light/mid/shadow split.
	float ndl = clamp(dot(normalize(NORMAL), normalize(LIGHT)), 0.0, 1.0);
	float ramp = ndl < edge1 ? band_floor : (ndl < edge2 ? band_mid : 1.0);
	// Cool + darken the darkest band so the shadow side reads even with ambient fill.
	vec3 tint = mix(shadow_tint, vec3(1.0), smoothstep(edge1, edge2, ndl));
	DIFFUSE_LIGHT += ATTENUATION * LIGHT_COLOR.rgb * ALBEDO * tint * ramp;
}
