using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Equipment.Optics;
using Reticle;
using UnityEngine;

namespace T90A.Common
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
		static readonly FieldInfo Smr = Reflect.Field(typeof(ReticleMesh), "SMR");
		static readonly Dictionary<ReticleSO, float> Held = new Dictionary<ReticleSO, float>();
		static readonly List<ReticleWidth> All = new List<ReticleWidth>();

		internal static void Attach(UsableOptic optic)
		{
			if (optic.GetComponent<ReticleWidth>() != null) return;
			All.RemoveAll(x => x == null);
			foreach (var dead in Held.Keys.Where(k => k == null).ToList()) Held.Remove(dead);
			var w = optic.gameObject.AddComponent<ReticleWidth>();
			All.Add(w);
			w._optic = optic;
			w._reticle = optic.reticleMesh.reticleSO;
			w._narrowFov = new[] { optic.slot.DefaultFov }.Concat(optic.slot.OtherFovs ?? new float[0]).Min();
			w._lines = AllLines(w._reticle).ToList();
			foreach (var line in w._lines) if (!_base.ContainsKey(line)) _base[line] = line.thickness.MRADS;
			optic.slot.ZoomChanged += w.Refresh;
			optic.slot.ActiveChanged += w.OnActive;
			w.Refresh();
		}

		void OnDestroy()
		{
			All.Remove(this);
			if (_optic != null && _optic.slot != null) { _optic.slot.ZoomChanged -= Refresh; _optic.slot.ActiveChanged -= OnActive; }
		}

		void OnActive(bool now) { if (now) Refresh(); }

		void Refresh()
		{
			if (_optic == null || _optic.reticleMesh.reticleSO != _reticle) return;
			float fov = _optic.slot.CurrentFov;
			float k = Mathf.Pow(Mathf.Max(1f, fov / Mathf.Max(0.1f, _narrowFov)), Exponent);
			if (Mathf.Abs(k - _applied) >= 0.01f && Smr != null)
			{
				Hold(_reticle, _lines, k);
				Smr.SetValue(_optic.reticleMesh, null);
				_optic.reticleMesh.Load();
				_applied = k;
			}
			var shown = All.FirstOrDefault(x => x != null && x._reticle == _reticle && x._applied > 0f && x._optic != null && x._optic.slot != null && x._optic.slot.IsActive);
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
				var e = stack.Pop();
				if (e is ReticleTree.Line l) yield return l;
				if (e is ReticleTree.GroupBase g) foreach (var c in g.elements) stack.Push(c);
			}
		}
	}
}
