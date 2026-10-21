using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Equipment.Optics;
using Reticle;
using UnityEngine;

namespace T90A
{
	internal class ReticleWidth : MonoBehaviour
	{
		const float Exponent = 0.6f;

		UsableOptic _optic;
		ReticleSO _reticle;
		List<ReticleTree.Line> _lines;
		float _narrowFov;
		float _applied = -1f;
		static readonly Dictionary<ReticleTree.Line, float> _base = new Dictionary<ReticleTree.Line, float>();
		static readonly FieldInfo Smr = typeof(ReticleMesh).GetField("SMR", BindingFlags.NonPublic | BindingFlags.Instance);

		internal static void Attach(UsableOptic optic)
		{
			if (optic.GetComponent<ReticleWidth>() != null) return;
			var w = optic.gameObject.AddComponent<ReticleWidth>();
			w._optic = optic;
			w._reticle = optic.reticleMesh.reticleSO;
			w._narrowFov = new[] { optic.slot.DefaultFov }.Concat(optic.slot.OtherFovs ?? new float[0]).Min();
			w._lines = AllLines(w._reticle).ToList();
			foreach (var line in w._lines) if (!_base.ContainsKey(line)) _base[line] = line.thickness.MRADS;
			optic.slot.ZoomChanged += w.Refresh;
			w.Refresh();
		}

		void OnDestroy()
		{
			if (_optic != null && _optic.slot != null) _optic.slot.ZoomChanged -= Refresh;
		}

		void Refresh()
		{
			if (_optic == null || _optic.reticleMesh.reticleSO != _reticle) return;
			float fov = _optic.slot.CurrentFov;
			float k = Mathf.Pow(Mathf.Max(1f, fov / Mathf.Max(0.1f, _narrowFov)), Exponent);
			if (Mathf.Abs(k - _applied) < 0.01f) return;
			foreach (var line in _lines) if (line.parent != null) line.thickness.MRADS = _base[line] * k;
			Smr.SetValue(_optic.reticleMesh, null);
			_optic.reticleMesh.Load();
			_applied = k;
		}

		static IEnumerable<ReticleTree.Line> AllLines(ReticleSO so)
		{
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var e = stack.Pop();
				if (e is ReticleTree.Line l) yield return l;
				if (e is ReticleTree.GroupBase g) foreach (var c in g.elements) stack.Push(c);
			}
		}
	}
}
