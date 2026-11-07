using System;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using Reticle;
using UnityEngine;

namespace T90A.Common
{
	// Has to run after UsableOptic.LateUpdate.
	[DefaultExecutionOrder(20)]
	internal sealed class ReticleRangeLine : MonoBehaviour
	{
		UsableOptic _optic;
		FireControlSystem _fcs;
		Func<AmmoType> _ammo;

		internal void Init(UsableOptic optic, FireControlSystem fcs, Func<AmmoType> ammo) { _optic = optic; _fcs = fcs; _ammo = ammo; }

		void LateUpdate()
		{
			if (_optic?.reticleMesh == null || _fcs == null || _ammo == null) return;
			var a = _ammo();
			float se = a != null && _fcs.CurrentRange > 0f ? BallisticComputerRepository.Instance.GetSuperelevation(a, _fcs.CurrentRange) : 0f;
			_optic.reticleMesh.SetAlignment(ReticleTree.GroupBase.Alignment.Impact, new AngularVector2(0f, se, AngularLength.AngularUnit.DEG));
		}
	}
}
