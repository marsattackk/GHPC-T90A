using System.Collections.Generic;
using GHPC.Audio;
using GHPC.Effects;
using UnityEngine;

namespace T90A.Common
{
	// Explosion effect and sound for ERA bricks and the APS. GHPC looks impact effects up by AmmoType, so there's a
	// stand-in round per size that never gets fired.
	internal static class Blast
	{
		internal enum Size { EraBrick, ApsLaunch, ApsBurst }

		static readonly Dictionary<Size, (ParticleEffectsManager.ImpactEffectDescriptor d, ImpactAudioType audio)> Kinds =
			new Dictionary<Size, (ParticleEffectsManager.ImpactEffectDescriptor, ImpactAudioType)>
			{
				[Size.EraBrick] = (new ParticleEffectsManager.ImpactEffectDescriptor
				{
					HasImpactEffect = true,
					ImpactCategory = ParticleEffectsManager.Category.HighExplosive,
					EffectSize = ParticleEffectsManager.EffectSize.Autocannon,
					RicochetType = ParticleEffectsManager.RicochetType.None,
					Flags = ParticleEffectsManager.ImpactModifierFlags.Large,
					MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low,
				}, ImpactAudioType.MainGunHeat),
				[Size.ApsLaunch] = (new ParticleEffectsManager.ImpactEffectDescriptor
				{
					HasImpactEffect = true,
					ImpactCategory = ParticleEffectsManager.Category.HighExplosive,
					EffectSize = ParticleEffectsManager.EffectSize.Autocannon,
					RicochetType = ParticleEffectsManager.RicochetType.None,
					Flags = ParticleEffectsManager.ImpactModifierFlags.None,
					MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low,
				}, ImpactAudioType.AutocannonExplosive),
				[Size.ApsBurst] = (new ParticleEffectsManager.ImpactEffectDescriptor
				{
					HasImpactEffect = true,
					ImpactCategory = ParticleEffectsManager.Category.HighExplosive,
					EffectSize = ParticleEffectsManager.EffectSize.Mortar,
					RicochetType = ParticleEffectsManager.RicochetType.None,
					Flags = ParticleEffectsManager.ImpactModifierFlags.None,
					MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low,
				}, ImpactAudioType.Mortar),
			};

		static readonly Dictionary<Size, AmmoType> _types = new Dictionary<Size, AmmoType>();

		internal static void Play(Vector3 at, Size size) => Play(at, size, true);

		internal static void Play(Vector3 at, Size size, bool sound)
		{
			var kind = Kinds[size];
			if (!_types.TryGetValue(size, out var ammoType))
				_types[size] = ammoType = new AmmoType { Name = "T90A.Common blast " + size, ImpactEffectDescriptor = kind.d };
			var effects = ParticleEffectsManager.Instance;
			GameObject made = effects != null
				? effects.CreateImpactEffectOfType(
					ammoType,
					ParticleEffectsManager.FusedStatus.Fuzed,
					ParticleEffectsManager.SurfaceMaterial.Steel,
					false,
					at)
				: null;
			if (sound) ImpactSFXManager.Instance?.PlaySimpleImpactAudio(kind.audio, at);
		}
	}
}
