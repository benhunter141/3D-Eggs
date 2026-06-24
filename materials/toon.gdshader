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

// Cel banding + dark-side floor (so shadowed sides keep the tint, never crush to black).
uniform float bands : hint_range(1.0, 6.0) = 3.0;
uniform float band_floor : hint_range(0.0, 1.0) = 0.30;

// Fresnel rim light.
uniform vec4 rim_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float rim_strength : hint_range(0.0, 2.0) = 0.45;
uniform float rim_power : hint_range(0.5, 8.0) = 3.0;

void fragment() {
	ALBEDO = mix(base_color.rgb, flash_color.rgb, flash * 0.85);
	ROUGHNESS = 1.0;

	// Fresnel rim: bright where the surface turns away from the camera.
	float fres = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), rim_power);
	// Rim plus an additive white burst while flashing.
	EMISSION = rim_color.rgb * fres * rim_strength + flash_color.rgb * flash * 1.5;
}

void light() {
	// Quantize the sun's lambert term into hard bands for the cel look.
	float ndl = clamp(dot(normalize(NORMAL), normalize(LIGHT)), 0.0, 1.0);
	float stepped = floor(ndl * bands) / bands;
	stepped = mix(band_floor, 1.0, stepped);   // lift the darkest band so the tint reads
	DIFFUSE_LIGHT += ATTENUATION * LIGHT_COLOR.rgb * ALBEDO * stepped;
}
