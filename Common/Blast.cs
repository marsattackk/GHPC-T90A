using System.Collections.Generic;
using GHPC.Audio;
using GHPC.Effects;
using UnityEngine;

namespace T90A.Common
{
	// GHPC looks impact effects up by AmmoType, hence a stand-in per size that's never fired.
	internal static class Blast
	{
		internal enum Size { EraBrick }

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
		};

		static readonly Dictionary<Size, AmmoType> _types = new Dictionary<Size, AmmoType>();

		internal static void Play(Vector3 at, Size size)
		{
			var k = Kinds[size];
			if (!_types.TryGetValue(size, out var t))
				_types[size] = t = new AmmoType { Name = "T90A.Common blast " + size, ImpactEffectDescriptor = k.d };
			var fx = ParticleEffectsManager.Instance;
			if (fx != null) fx.CreateImpactEffectOfType(t, ParticleEffectsManager.FusedStatus.Fuzed, ParticleEffectsManager.SurfaceMaterial.Steel, false, at);
			ImpactSFXManager.Instance?.PlaySimpleImpactAudio(k.audio, at);
		}
	}
}
