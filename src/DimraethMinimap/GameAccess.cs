using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace DimraethMinimap
{
    /// <summary>
    /// The only place that touches Dimraeth's own classes. Everything else uses plain Unity API,
    /// so when a game update renames or removes something, only this file breaks - and it
    /// breaks softly: the minimap falls back to following the game camera.
    /// Read-only: nothing here writes game state or sends anything over the network.
    /// </summary>
    internal static class GameAccess
    {
        private const float RescanInterval = 1f;

        private static bool _broken;
        private static float _nextScan;
        private static Transform _local;
        private static readonly List<Transform> _others = new List<Transform>();

        public static bool Broken => _broken;

        /// <summary>The local player component, untyped so callers do not depend on game classes.</summary>
        public static Component LocalComponent { get; private set; }

        /// <summary>Local player's position, if we are in a world.</summary>
        public static bool TryGetLocal(out Vector3 position)
        {
            position = default;
            Rescan();
            if (!IsAlive(_local)) return false;
            position = _local.position;
            return true;
        }

        /// <summary>Positions of the other players that are in the same scene as us.</summary>
        public static void GetOthers(List<Vector3> into)
        {
            into.Clear();
            for (int i = 0; i < _others.Count; i++)
            {
                var t = _others[i];
                if (IsAlive(t)) into.Add(t.position);
            }
        }

        private static bool IsAlive(Transform t)
        {
            try { return t != null && t.gameObject.activeInHierarchy; }
            catch { return false; }
        }

        private static void Rescan()
        {
            if (_broken) return;
            float now = Time.unscaledTime;
            if (now < _nextScan) return;
            _nextScan = now + RescanInterval;

            try
            {
                ScanPlayers();
            }
            catch (Exception e)
            {
                // Missing type / member after a game update lands here (thrown when ScanPlayers is JIT-compiled).
                _broken = true;
                _local = null;
                _others.Clear();
                Plugin.Logger.LogWarning("Could not read players from the game (game update?). Following the camera instead; teammate dots disabled. " + e.GetType().Name + ": " + e.Message);
            }
        }

        // IMPORTANT: never search the scene here (FindObjectsByType and friends). Dimraeth keeps its whole world
        // loaded, and a scene-wide search stalls the game for 50-300 ms - measured, and felt as a hitch every
        // few steps. Everything below is dictionary / list lookups on data the game already keeps.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ScanPlayers()
        {
            _local = null;
            LocalComponent = null;
            _others.Clear();

            var net = Unity.Netcode.NetworkManager.Singleton;
            if (net == null || !net.IsListening) return;
            var spawns = net.SpawnManager;
            if (spawns == null) return;

            var localObject = spawns.GetLocalPlayerObject();
            if (localObject == null) return;
            var local = localObject.GetComponent<Player>();
            if (local == null) return;
            _local = local.transform;
            LocalComponent = local;

            if (!_othersBroken)
            {
                try { ScanOthers(spawns, localObject.NetworkObjectId); }
                catch (Exception e)
                {
                    _othersBroken = true;
                    _others.Clear();
                    Plugin.Logger.LogWarning("Could not list other players; teammate dots disabled. " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        private static bool _othersBroken;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ScanOthers(Unity.Netcode.NetworkSpawnManager spawns, ulong localId)
        {
            // The game replicates the network ids of all player objects to every client.
            var visibility = VisibilityManager.Singleton;
            var ids = visibility != null ? visibility.PlayerNetworkIds : null;
            if (ids == null) return;

            var spawned = spawns.SpawnedObjects;
            int count = ids.Count;
            for (int i = 0; i < count; i++)
            {
                ulong id = ids[i];
                if (id == localId || !spawned.ContainsKey(id)) continue; // not spawned here = not visible to us
                var obj = spawned[id];
                if (obj != null) _others.Add(obj.transform);
            }
        }
    }
}