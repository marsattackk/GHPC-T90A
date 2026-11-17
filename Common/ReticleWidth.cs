using System.Collections.Generic;
using System.Linq;
using GHPC.Equipment.Optics;
using Reticle;
using UnityEngine;

namespace T90A.Common
{
	// First focal plane: stroke widths are angles, so at the wide fields they all but vanish. This scales them with the
	// field of view.
	internal class ReticleWidth : MonoBehaviour
	{
		const float DefaultExponent = 0.6f;

		UsableOptic _optic;
		float _exponent;
		ReticleSO _reticle;
		List<ReticleTree.Line> _lines;
		float _narrowFov;
		float _applied = -1f;
		static readonly Dictionary<ReticleTree.Line, float> _base = new Dictionary<ReticleTree.Line, float>();
		// The asset is shared and every rebuild reads its current widths, so it goes back to the optic in view.
		static readonly Dictionary<ReticleSO, float> Held = new Dictionary<ReticleSO, float>();
		static readonly List<ReticleWidth> All = new List<ReticleWidth>();

		internal static void Attach(UsableOptic optic, float exponent = DefaultExponent)
		{
			if (optic.GetComponent<ReticleWidth>() != null) return;
			All.RemoveAll(i => i == null);
			UnityKeys.Prune(Held);
			var reticleWidth = optic.gameObject.AddComponent<ReticleWidth>();
			All.Add(reticleWidth);
			reticleWidth._optic = optic;
			reticleWidth._exponent = exponent;
			reticleWidth._narrowFov = new[] { optic.slot.DefaultFov }.Concat(optic.slot.OtherFovs ?? new float[0]).Min();
			reticleWidth.Bind(optic.reticleMesh.reticleSO);
			optic.slot.ZoomChanged += reticleWidth.Refresh;
			optic.slot.ActiveChanged += reticleWidth.OnActive;
			reticleWidth.Refresh();
		}

		void OnDestroy()
		{
			All.Remove(this);
			if (_optic != null && _optic.slot != null)
			{
				_optic.slot.ZoomChanged -= Refresh;
				_optic.slot.ActiveChanged -= OnActive;
			}
		}

		void Bind(ReticleSO reticle)
		{
			_reticle = reticle;
			_lines = AllLines(reticle).ToList();
			foreach (var line in _lines) if (!_base.ContainsKey(line)) _base[line] = line.thickness.MRADS;
			_applied = -1f;
		}

		void OnActive(bool now) { if (now) Refresh(); }

		internal void Refresh()
		{
			if (_optic == null || _optic.reticleMesh == null || _optic.reticleMesh.reticleSO == null) return;
			if (_optic.reticleMesh.reticleSO != _reticle) Bind(_optic.reticleMesh.reticleSO);
			float fov = _optic.slot.CurrentFov;
			float k = Mathf.Pow(Mathf.Max(1f, fov / Mathf.Max(0.1f, _narrowFov)), _exponent);
			if (Mathf.Abs(k - _applied) >= 0.01f && ReticleMeshes.Smr != null)
			{
				Hold(_reticle, _lines, k);
				ReticleMeshes.ClearSmr(_optic.reticleMesh);
				_optic.reticleMesh.Load();
				_applied = k;
			}
			var shown = All.FirstOrDefault(i => i != null
				&& i._reticle == _reticle
				&& i._applied > 0f
				&& i._optic != null
				&& i._optic.slot != null
				&& i._optic.slot.IsActive);
			if (shown != null) Hold(_reticle, shown._lines, shown._applied);
		}

		static void Hold(ReticleSO so, List<ReticleTree.Line> lines, float k)
		{
			if (Held.TryGetValue(so, out float now) && Mathf.Abs(now - k) < 0.001f) return;
			foreach (var line in lines) if (line.parent != null) line.thickness.MRADS = _base[line] * k;
			Held[so] = k;
		}

		static IEnumerable<ReticleTree.Line> AllLines(ReticleSO so)
		{
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var element = stack.Pop();
				if (element is ReticleTree.Line line) yield return line;
				if (element is ReticleTree.GroupBase container) foreach (var child in container.elements) stack.Push(child);
			}
		}
	}
}
