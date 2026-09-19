using System;
using System.Runtime.CompilerServices;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DimraethMinimap
{
    /// <summary>Writes a read-only snapshot of cameras / layers / players to the BepInEx log for bug reports.</summary>
    internal static class Diagnostics
    {
        public static void Dump(Camera minimapCam, RectTransform mapHolder)
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== Dimraeth Minimap diagnostics =====");
            sb.AppendLine($"plugin {Plugin.Version}, unity {Application.unityVersion}, game {Application.version}, screen {Screen.width}x{Screen.height}");

            sb.AppendLine(FrameStats.Summary());
            Section(sb, "scenes", Scenes);
            Section(sb, "layers", Layers);
            Section(sb, "cameras", Cameras);
            Section(sb, "game minimap leftovers", GameMinimap);
            Section(sb, "players", Players);
            Section(sb, "game map", GameMap.Describe);
            if (mapHolder != null)
                sb.AppendLine($"minimap map holder: active={mapHolder.gameObject.activeSelf} scale={mapHolder.localScale.x} anchored={mapHolder.anchoredPosition} children={mapHolder.childCount}");
            if (minimapCam != null)
                sb.AppendLine($"minimap cam: pos={minimapCam.transform.position} size={minimapCam.orthographicSize} mask=0x{minimapCam.cullingMask:X8}");

            Plugin.Logger.LogInfo(sb.ToString());
            FrameStats.SkipNext(3);
        }

        private static void Section(StringBuilder sb, string title, Action<StringBuilder> body)
        {
            sb.AppendLine($"--- {title}");
            try { body(sb); }
            catch (Exception e) { sb.AppendLine($"  (failed: {e.GetType().Name}: {e.Message})"); }
        }

        private static void Scenes(StringBuilder sb)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                sb.AppendLine($"  [{i}] {s.name} loaded={s.isLoaded} roots={s.rootCount}");
            }
            sb.AppendLine($"  active: {SceneManager.GetActiveScene().name}");
        }

        private static void Layers(StringBuilder sb)
        {
            for (int i = 0; i < 32; i++)
            {
                string n = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(n)) sb.AppendLine($"  {i,2}: {n}");
            }
        }

        private static void Cameras(StringBuilder sb)
        {
            var found = UnityEngine.Object.FindObjectsByType(Il2CppType.Of<Camera>(), FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var o in found)
            {
                var c = o.TryCast<Camera>();
                if (c == null) continue;
                sb.AppendLine($"  {Path(c.transform)} enabled={c.enabled} active={c.gameObject.activeInHierarchy} tag={c.tag} ortho={c.orthographic} size={c.orthographicSize} depth={c.depth} mask=0x{c.cullingMask:X8} rt={(c.targetTexture != null ? c.targetTexture.name : "-")} pos={c.transform.position} rot={c.transform.eulerAngles}");
                foreach (var comp in c.gameObject.GetComponents<Component>())
                    if (comp != null) sb.AppendLine($"      + {comp.GetIl2CppType().FullName}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void GameMinimap(StringBuilder sb)
        {
            var ctrls = Resources.FindObjectsOfTypeAll(Il2CppType.Of<MiniMapCameraController>());
            sb.AppendLine($"  MiniMapCameraController instances: {ctrls.Length}");
            foreach (var o in ctrls)
            {
                var c = o.TryCast<MiniMapCameraController>();
                if (c != null) sb.AppendLine($"    {Path(c.transform)} active={c.gameObject.activeInHierarchy} cam={(c.minimapCamera != null ? c.minimapCamera.name : "null")}");
            }

            var mobs = UnityEngine.Object.FindObjectsByType(Il2CppType.Of<MonsterBehaviourLibrary>(), FindObjectsSortMode.None);
            sb.AppendLine($"  monsters in scene: {mobs.Length}");
            int shown = 0;
            foreach (var o in mobs)
            {
                var m = o.TryCast<MonsterBehaviourLibrary>();
                var head = m?._miniMapHead;
                if (head == null) continue;
                var sr = head.GetComponent<SpriteRenderer>();
                sb.AppendLine($"    {m.name}: head layer={head.layer}({LayerMask.LayerToName(head.layer)}) active={head.activeInHierarchy} sprite={(sr != null && sr.sprite != null ? sr.sprite.name : "-")} scale={head.transform.lossyScale}");
                if (++shown >= 5) break;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Players(StringBuilder sb)
        {
            var found = UnityEngine.Object.FindObjectsByType(Il2CppType.Of<Player>(), FindObjectsSortMode.None);
            sb.AppendLine($"  players: {found.Length}");
            foreach (var o in found)
            {
                var p = o.TryCast<Player>();
                if (p == null) continue;
                sb.AppendLine($"    {p.name} owner={p.IsOwner} spawned={p.IsSpawned} scene={p.gameObject.scene.name} layer={p.gameObject.layer} pos={p.transform.position}");
            }
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
