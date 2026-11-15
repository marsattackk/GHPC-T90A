using System.Linq;
using GHPC.Equipment.Optics;
using Reticle;
using UnityEngine;

namespace T90A.Common
{
	// GHPC draws every reticle in the first focal plane, so a mark that keeps its size on screen needs an overlay per
	// zoom step.
	internal sealed class ZoomReticles : MonoBehaviour
	{
		UsableOptic _optic;
		ReticleSO[] _reticles;
		GameObject[][] _overlays;
		GameObject[] _all;
		int _applied = -1;

		internal static ZoomReticles Attach(UsableOptic optic, ReticleSO[] reticles, GameObject[][] overlays)
		{
			var zoom = optic.gameObject.GetComponent<ZoomReticles>() ?? optic.gameObject.AddComponent<ZoomReticles>();
			zoom._optic = optic;
			zoom._reticles = reticles;
			zoom._overlays = overlays;
			zoom._all = overlays
				.Where(o => o != null)
				.SelectMany(o => o)
				.Where(o => o != null)
				.Distinct()
				.ToArray();
			zoom._applied = -1;
			optic.slot.ZoomChanged += zoom.Refresh;
			zoom.Refresh();
			return zoom;
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
			var reticle = _reticles != null && i < _reticles.Length ? _reticles[i] : null;
			if (i != _applied && reticle != null && _optic.reticleMesh != null && _optic.reticleMesh.reticleSO != reticle)
			{
				_optic.reticleMesh.reticleSO = reticle;
				ReticleMeshes.ClearSmr(_optic.reticleMesh);
				_optic.reticleMesh.Load();
			}
			var shown = _overlays[i] ?? new GameObject[0];
			foreach (var overlay in _all) if (!shown.Contains(overlay) && overlay.activeSelf) overlay.SetActive(false);
			foreach (var overlay in shown) if (overlay != null && !overlay.activeSelf) overlay.SetActive(true);
			_applied = i;
		}
	}
}
