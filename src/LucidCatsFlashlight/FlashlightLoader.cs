using System.Collections.Generic;
using System.Linq;
using Game.Player;
using HaniUtils.Core;
using HaniUtils.Gameplay;
using HaniUtils.NGO;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LucidCatsFlashlight;

/// <summary>
/// Lives for the whole game session. Responsibilities:
///  - F key toggles the local player's flashlight
///  - attaches/removes FlashlightRig to every spawned PlayerManager
///  - syncs flashlight state through NGO named messages (clients -> host -> everyone)
/// </summary>
public class FlashlightLoader : MonoBehaviour
{
    private const string MsgState = "lucidcats.flashlight.state";
    private const string MsgSync = "lucidcats.flashlight.sync";

    // Last known flashlight state per player (survives scene changes / respawns)
    private readonly Dictionary<ulong, bool> _states = new();
    private readonly Dictionary<ulong, FlashlightRig> _rigs = new();

    private NetworkManager _nm;
    private bool _netHooksInstalled;
    private bool _wasListening;
    private bool _syncRequested;
    private float _nextMaintenance;

    private void Update()
    {
        HandleInput();
        EnsureNetHooks();
        MaintainSession();

        if (Time.unscaledTime >= _nextMaintenance)
        {
            _nextMaintenance = Time.unscaledTime + 0.5f;
            MaintainRigs();
        }
    }

    private void HandleInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.fKey.wasPressedThisFrame)
        {
            return;
        }

        // Only toggle during real gameplay (cursor locked). Avoids toggling while
        // typing in the laptop UI or browsing menus.
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        try
        {
            ToggleLocal();
        }
        catch (System.Exception e)
        {
            FlashlightPlugin.Log.LogError($"Toggle failed: {e}");
        }
    }

    private void ToggleLocal()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            return;
        }

        var local = GetLocalPlayer();
        if (local == null)
        {
            FlashlightPlugin.Log.LogDebug("F pressed but no local player spawned yet.");
            return;
        }

        ulong id = nm.LocalClientId;
        bool newState = !(_states.TryGetValue(id, out var cur) && cur);

        ApplyState(id, newState);

        if (nm.IsHost)
        {
            BroadcastState(id, newState);
        }
        else
        {
            using var writer = new FastBufferWriter(16, Allocator.Temp);
            WriteState(writer, id, newState);
            nm.CustomMessagingManager.SendNamedMessage(MsgState, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }

        FlashlightPlugin.Log.LogInfo($"Flashlight {(newState ? "ON" : "OFF")}");
    }

    private static PlayerManager GetLocalPlayer()
    {
        if (!MonoBehaviourSingleton<LocalPlayerRegistryBase<PlayerManager>>.HasInstance)
        {
            return null;
        }

        var registry = MonoBehaviourSingleton<LocalPlayerRegistryBase<PlayerManager>>.Instance;
        return registry == null ? null : registry.Current;
    }

    // ---------------------------------------------------------------- network

    private void EnsureNetHooks()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            return;
        }

        _nm = nm;

        if (_netHooksInstalled)
        {
            if (!nm.IsListening)
            {
                // Session ended (left lobby); re-arm for next session.
                _netHooksInstalled = false;
                nm.OnClientConnectedCallback -= OnClientConnected;
            }
            return;
        }

        if (!nm.IsListening)
        {
            return;
        }

        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgState, HandleStateMessage);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgSync, HandleSyncMessage);
        nm.OnClientConnectedCallback += OnClientConnected;
        _netHooksInstalled = true;
        FlashlightPlugin.Log.LogInfo("Network hooks installed.");
    }

    private void MaintainSession()
    {
        if (_nm == null)
        {
            return;
        }

        bool listening = _nm.IsListening;
        if (!listening && _wasListening)
        {
            // Left the lobby: forget everything, rigs despawn with their players.
            _states.Clear();
            _rigs.Clear();
            _syncRequested = false;
            FlashlightPlugin.Log.LogInfo("Session ended; flashlight state reset.");
        }

        // Clients ask the host for the current states once per session (late join).
        if (listening && !_nm.IsHost && !_syncRequested)
        {
            _syncRequested = true;
            using var writer = new FastBufferWriter(8, Allocator.Temp);
            _nm.CustomMessagingManager.SendNamedMessage(MsgSync, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }

        _wasListening = listening;
    }

    private void OnClientConnected(ulong clientId)
    {
        // Belt and braces: push current states to anyone who joins a session in progress.
        if (_nm != null && _nm.IsHost && _states.Count > 0)
        {
            SendStateDump(_nm.CustomMessagingManager, clientId);
        }
    }

    private void HandleStateMessage(ulong sender, FastBufferReader reader)
    {
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                return;
            }

            ReadState(reader, out var targetId, out var on);

            if (nm.IsHost)
            {
                // Self-echo of our own broadcast (NGO invokes the handler on the host
                // when it sends to all clients). State is already applied - do NOT
                // rebroadcast, that would recurse forever and kill the process.
                if (sender == nm.LocalClientId)
                {
                    return;
                }

                // A client toggled their light: apply it, then relay to everyone.
                targetId = sender;
                ApplyState(targetId, on);
                BroadcastState(targetId, on);
            }
            else
            {
                ApplyState(targetId, on); // relayed by the host
            }
        }
        catch (System.Exception e)
        {
            FlashlightPlugin.Log.LogError($"HandleStateMessage failed: {e}");
        }
    }

    private void HandleSyncMessage(ulong sender, FastBufferReader reader)
    {
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                return;
            }

            if (nm.IsHost)
            {
                // Never respond to ourself: sending the dump to our own clientId
                // would self-invoke this handler and recurse forever.
                if (sender == nm.LocalClientId)
                {
                    return;
                }

                // Client asked for the current states.
                SendStateDump(nm.CustomMessagingManager, sender);
            }
            else
            {
                // Host sent us the dump.
                _states.Clear();
                reader.ReadValueSafe(out int count, default(FastBufferWriter.ForPrimitives));
                for (int i = 0; i < count; i++)
                {
                    ReadState(reader, out var id, out var on);
                    ApplyState(id, on);
                }

                FlashlightPlugin.Log.LogInfo($"Received flashlight state dump ({count} player(s)).");
            }
        }
        catch (System.Exception e)
        {
            FlashlightPlugin.Log.LogError($"HandleSyncMessage failed: {e}");
        }
    }

    private void BroadcastState(ulong id, bool on)
    {
        if (_nm == null || !_nm.IsListening)
        {
            return;
        }

        using var writer = new FastBufferWriter(16, Allocator.Temp);
        WriteState(writer, id, on);
        _nm.CustomMessagingManager.SendNamedMessageToAll(MsgState, writer, NetworkDelivery.Reliable);
    }

    private void SendStateDump(CustomMessagingManager messaging, ulong clientId)
    {
        // Never message ourselves - NGO invokes the handler locally for host self-sends,
        // which would re-enter this handler forever.
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            return;
        }

        using var writer = new FastBufferWriter(64, Allocator.Temp, 512);
        int count = _states.Count;
        writer.WriteValueSafe(in count, default(FastBufferWriter.ForPrimitives));
        foreach (var pair in _states)
        {
            ulong id = pair.Key;
            byte on = pair.Value ? (byte)1 : (byte)0;
            writer.WriteValueSafe(in id, default(FastBufferWriter.ForPrimitives));
            writer.WriteValueSafe(in on, default(FastBufferWriter.ForPrimitives));
        }

        messaging.SendNamedMessage(MsgSync, clientId, writer, NetworkDelivery.Reliable);
    }

    private static void WriteState(FastBufferWriter writer, ulong id, bool on)
    {
        ulong targetId = id;
        byte flag = on ? (byte)1 : (byte)0;
        writer.WriteValueSafe(in targetId, default(FastBufferWriter.ForPrimitives));
        writer.WriteValueSafe(in flag, default(FastBufferWriter.ForPrimitives));
    }

    private static void ReadState(FastBufferReader reader, out ulong id, out bool on)
    {
        reader.ReadValueSafe(out id, default(FastBufferWriter.ForPrimitives));
        reader.ReadValueSafe(out byte flag, default(FastBufferWriter.ForPrimitives));
        on = flag != 0;
    }

    // ---------------------------------------------------------------- rigs

    private void ApplyState(ulong id, bool on)
    {
        _states[id] = on;
        if (_rigs.TryGetValue(id, out var rig) && rig != null)
        {
            rig.SetOn(on);
        }
    }

    private void MaintainRigs()
    {
        try
        {
            var players = LiveInstanceRegistry<PlayerManager>.All.ToList();

            // Drop rigs whose player despawned.
            foreach (var pair in _rigs.ToList())
            {
                if (pair.Value == null || pair.Value.Player == null ||
                    !players.Contains(pair.Value.Player))
                {
                    if (pair.Value != null)
                    {
                        Destroy(pair.Value.gameObject);
                    }

                    _rigs.Remove(pair.Key);
                }
            }

            // Ensure every live player has a rig.
            foreach (var player in players)
            {
                if (player == null)
                {
                    continue;
                }

                ulong id = player.OwnerClientId;
                _rigs.TryGetValue(id, out var rig);

                if (rig != null && rig.Player != player)
                {
                    // New player object for the same owner; rebuild the rig.
                    Destroy(rig.gameObject);
                    rig = null;
                    _rigs.Remove(id);
                }

                if (rig == null)
                {
                    rig = CreateRig(player);
                    _rigs[id] = rig;
                }

                // Keep rig visuals in sync with known state + spectating.
                rig.SetOn(_states.TryGetValue(id, out var s) && s);
                rig.gameObject.SetActive(player.SleepManager == null || !player.SleepManager.IsSpectating);
            }
        }
        catch (System.Exception e)
        {
            FlashlightPlugin.Log.LogError($"Rig maintenance failed: {e}");
        }
    }

    private FlashlightRig CreateRig(PlayerManager player)
    {
        bool isLocal = player.IsOwner;
        var rigGo = new GameObject("LCF_FlashlightRig");

        if (isLocal && player.mainCam != null)
        {
            rigGo.transform.SetParent(player.mainCam.transform, false);
            rigGo.transform.localPosition = new Vector3(
                FlashlightPlugin.LocalRight.Value,
                -FlashlightPlugin.LocalDown.Value,
                FlashlightPlugin.LocalForward.Value);
            rigGo.transform.localRotation = Quaternion.identity;
        }
        else
        {
            // Remote players: anchor near the head of their cat model.
            Transform anchor = FindHeadAnchor(player, out Vector3 localPos);
            rigGo.transform.SetParent(anchor, false);
            rigGo.transform.localPosition = localPos;
            rigGo.transform.localRotation = Quaternion.Euler(
                FlashlightPlugin.RemotePitch.Value,
                FlashlightPlugin.RemoteYaw.Value,
                0f);
        }

        var rig = rigGo.AddComponent<FlashlightRig>();
        rig.Init(player);
        FlashlightPlugin.Log.LogDebug($"Rig created for {(isLocal ? "local" : "remote")} player {player.OwnerClientId}.");
        return rig;
    }

    private static Transform FindHeadAnchor(PlayerManager player, out Vector3 localPos)
    {
        var model = player.PlayerModel;
        if (model == null)
        {
            localPos = new Vector3(0f, 0.6f, 0.4f);
            return player.transform;
        }

        // 1) Prefer a real head bone if the rig has one.
        var transforms = model.GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            if (t.name.ToLowerInvariant().Contains("head"))
            {
                localPos = new Vector3(0f, 0.05f, 0.1f);
                return t;
            }
        }

        // 2) Fall back to a point derived from the model's renderer bounds.
        localPos = new Vector3(0f, FlashlightPlugin.RemoteHeight.Value * 0.5f, FlashlightPlugin.RemoteForward.Value);
        var renderer = model.GetComponentInChildren<Renderer>(true);
        if (renderer != null)
        {
            var b = renderer.bounds;
            Vector3 headWorld = b.center + Vector3.up * (b.extents.y * (FlashlightPlugin.RemoteHeight.Value * 2f - 1f) * 0.5f);
            localPos = model.InverseTransformPoint(headWorld) + new Vector3(0f, 0f, FlashlightPlugin.RemoteForward.Value);
        }

        return model;
    }
}
