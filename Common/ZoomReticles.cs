using System.Linq;
using System.Reflection;
using GHPC.Equipment.Optics;
using Reticle;
using UnityEngine;

namespace T90A.Common
{
	// GHPC draws every reticle in the first focal plane, so a mark that keeps its size on screen needs one per zoom step.
	internal sealed class ZoomReticles : MonoBehaviour
	{
		static readonly FieldInfo Smr = Reflect.Field(typeof(ReticleMesh), "SMR");

		UsableOptic _optic;
		ReticleSO[] _reticles;
		GameObject[][] _overlays;
		GameObject[] _all;
		int _applied = -1;

		internal static void Attach(UsableOptic optic, ReticleSO[] reticles, GameObject[][] overlays)
		{
			var z = optic.gameObject.GetComponent<ZoomReticles>() ?? optic.gameObject.AddComponent<ZoomReticles>();
			z._optic = optic;
			z._reticles = reticles;
			z._overlays = overlays;
			z._all = overlays.Where(o => o != null).SelectMany(o => o).Where(o => o != null).Distinct().ToArray();
			z._applied = -1;
			optic.slot.ZoomChanged += z.Refresh;
			z.Refresh();
		}

		void OnDestroy()
		{
			if (_optic != null && _optic.slot != null) _optic.slot.ZoomChanged -= Refresh;
		}

		internal int Step => _optic != null && _optic.slot != null ? Mathf.Clamp(_optic.slot.FovIndex, 0, _overlays.Length - 1) : 0;

		void Refresh()
		{
			if (_optic == null) return;
			int i = Step;
			var so = _reticles != null && i < _reticles.Length ? _reticles[i] : null;
			if (i != _applied && so != null && _optic.reticleMesh != null && _optic.reticleMesh.reticleSO != so)
			{
				_optic.reticleMesh.reticleSO = so;
				Smr.SetValue(_optic.reticleMesh, null);
				_optic.reticleMesh.Load();
			}
			var shown = _overlays[i] ?? new GameObject[0];
			foreach (var o in _all) if (!shown.Contains(o) && o.activeSelf) o.SetActive(false);
			foreach (var o in shown) if (o != null && !o.activeSelf) o.SetActive(true);
			_applied = i;
		}
	}
}
