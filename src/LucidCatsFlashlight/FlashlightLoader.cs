using System;
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

    // Last known flashlight state per player (survives scene changes / respawns).
    // The battery byte rides along so friends see each other's torch dying.
    private readonly Dictionary<ulong, (bool On, byte Battery)> _states = new();
    private readonly Dictionary<ulong, FlashlightRig> _rigs = new();

    private NetworkManager _nm;
    private bool _netHooksInstalled;
    private bool _wasListening;
    private bool _syncRequested;
    private float _nextMaintenance;
    private float _nextBatterySend;

    // Rebindable flashlight key: a keyboard key or a mouse button.
    private const float RebindHoldSeconds = 2f;
    private Key _toggleKey = Key.F;
    private int _mouseButton = -1; // 0=left, 1=right, 2=middle, 3=side-forward, 4=side-back
    private bool _rebinding;
    private float _holdStartedAt;
    private bool _rebindPrompted;

    private void Awake()
    {
        ParseToggleKey();
    }

    private void ParseToggleKey()
    {
        string raw = FlashlightPlugin.ToggleKey != null ? FlashlightPlugin.ToggleKey.Value : null;
        if (string.IsNullOrWhiteSpace(raw) || !TryParseInput(raw, out Key key, out int mouse) ||
            (key == Key.None && mouse < 0))
        {
            FlashlightPlugin.Log.LogWarning(
                $"Could not parse ToggleKey '{raw}'; falling back to F.");
            _toggleKey = Key.F;
            _mouseButton = -1;
            return;
        }

        _toggleKey = key;
        _mouseButton = mouse;
    }

    private void Update()
    {
        HandleInput();
        EnsureNetHooks();
        MaintainSession();
        UpdateBatteryNetwork();
        FlashlightSettingsTab.Tick(this);

        if (Time.unscaledTime >= _nextMaintenance)
        {
            _nextMaintenance = Time.unscaledTime + 0.5f;
            MaintainRigs();
        }
    }

    /// <summary>
    /// Battery-driven network traffic for the local player: shuts the light off
    /// through the normal sync path when the battery dies, and keeps friends
    /// seeing the drain with a level update about once a second.
    /// </summary>
    private void UpdateBatteryNetwork()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !FlashlightBattery.Enabled)
        {
            return;
        }

        ulong id = nm.LocalClientId;
        bool on = _states.TryGetValue(id, out var s) && s.On;

        if (on && FlashlightBattery.Level <= 0.0005f)
        {
            // Dead mid-use: normal off state, so the close click plays and
            // friends see the torch dissolve.
            ApplyState(id, false, FlashlightBattery.ToByte());
            SendLocalState(id, false);
            FlashlightPlugin.Log.LogInfo("Battery dead; flashlight switched off.");
            return;
        }

        if (on && Time.unscaledTime >= _nextBatterySend)
        {
            SendLocalState(id, true);
        }
    }

    private void HandleInput()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        // Rebind capture keeps running outside gameplay, so a half-finished
        // rebind always resolves (Esc cancels).
        if (_rebinding)
        {
            CaptureRebindKey(keyboard, mouse);
            return;
        }

        // Only toggle during real gameplay (cursor locked). Avoids toggling while
        // typing in the laptop UI or browsing menus.
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        bool pressedThisFrame = false;
        bool isPressed = false;
        if (_mouseButton >= 0 && mouse != null)
        {
            var button = MouseButtonControl(mouse, _mouseButton);
            if (button != null)
            {
                pressedThisFrame = button.wasPressedThisFrame;
                isPressed = button.isPressed;
            }
        }
        else if (keyboard != null)
        {
            var keyControl = keyboard[_toggleKey];
            if (keyControl != null)
            {
                pressedThisFrame = keyControl.wasPressedThisFrame;
                isPressed = keyControl.isPressed;
            }
        }

        if (pressedThisFrame)
        {
            _holdStartedAt = Time.unscaledTime;
            _rebindPrompted = false;

            try
            {
                ToggleLocal();
            }
            catch (Exception e)
            {
                FlashlightPlugin.Log.LogError($"Toggle failed: {e}");
            }
        }

        // Holding the flashlight key for two seconds enters rebind mode.
        if (isPressed && !_rebindPrompted &&
            Time.unscaledTime - _holdStartedAt >= RebindHoldSeconds)
        {
            _rebindPrompted = true;
            StartRebind();
        }
    }

    // ---------------------------------------------------------------- rebinding

    /// <summary>Name of the current flashlight key (shown in the settings card).</summary>
    public string CurrentKeyName => _mouseButton >= 0 ? MouseButtonName(_mouseButton) : _toggleKey.ToString();

    /// <summary>Starts the rebind flow from the settings menu's Change button.</summary>
    public void BeginRebindFromUi()
    {
        if (!_rebinding)
        {
            StartRebind();
        }
    }

    private void StartRebind()
    {
        _rebinding = true;
        FlashlightPrompt.Show(
            $"Press any keyboard key or mouse button to bind the flashlight...\n(Currently: {CurrentKeyName}  |  Esc to cancel)");
        FlashlightPlugin.Log.LogInfo("Rebinding the flashlight key: press a key or mouse button (Esc to cancel).");
    }

    private void CaptureRebindKey(Keyboard keyboard, Mouse mouse)
    {
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            CancelRebind();
            return;
        }

        if (keyboard != null)
        {
            foreach (var keyControl in keyboard.allKeys)
            {
                if (keyControl == null || keyControl.keyCode == Key.None || !keyControl.wasPressedThisFrame)
                {
                    continue;
                }

                ApplyNewKey(keyControl.keyCode.ToString());
                return;
            }
        }

        if (mouse != null)
        {
            for (int i = 0; i < MouseButtons; i++)
            {
                var button = MouseButtonControl(mouse, i);
                if (button != null && button.wasPressedThisFrame)
                {
                    ApplyNewKey(MouseButtonName(i));
                    return;
                }
            }
        }
    }

    private void ApplyNewKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName) || !TryParseInput(keyName, out Key key, out int mouse) ||
            (key == Key.None && mouse < 0))
        {
            CancelRebind();
            return;
        }

        _toggleKey = key;
        _mouseButton = mouse;
        if (FlashlightPlugin.ToggleKey != null)
        {
            FlashlightPlugin.ToggleKey.Value = keyName.Trim();
            FlashlightPlugin.SaveConfig();
        }

        _rebinding = false;
        FlashlightPrompt.Hide();
        FlashlightPlugin.Log.LogInfo($"Flashlight toggle rebound to {keyName}.");
    }

    private void CancelRebind()
    {
        _rebinding = false;
        FlashlightPrompt.Hide();
        FlashlightPlugin.Log.LogInfo("Rebind cancelled; keeping the current key.");
    }

    // ---------------------------------------------------------- keyboard/mouse input

    private const int MouseButtons = 5; // left, right, middle, side-forward, side-back

    private static string MouseButtonName(int index) => index switch
    {
        0 => "LeftMouse",
        1 => "RightMouse",
        2 => "MiddleMouse",
        3 => "Mouse4",
        4 => "Mouse5",
        _ => "F",
    };

    private static UnityEngine.InputSystem.Controls.ButtonControl MouseButtonControl(Mouse mouse, int index) => index switch
    {
        0 => mouse.leftButton,
        1 => mouse.rightButton,
        2 => mouse.middleButton,
        3 => mouse.forwardButton,
        4 => mouse.backButton,
        _ => null,
    };

    private static bool TryParseInput(string raw, out Key key, out int mouse)
    {
        key = Key.None;
        mouse = -1;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "leftmouse": mouse = 0; return true;
            case "rightmouse": mouse = 1; return true;
            case "middlemouse": mouse = 2; return true;
            case "mouse4":
            case "sideforward": mouse = 3; return true;
            case "mouse5":
            case "sideback": mouse = 4; return true;
            default:
                return Enum.TryParse(raw.Trim(), ignoreCase: true, out key) && key != Key.None;
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
        bool currentState = _states.TryGetValue(id, out var cur) && cur.On;
        bool newState = !currentState;

        // Dead battery: a quiet click, but no light. While any charge remains,
        // short bursts are allowed - it just dies mid-use again.
        if (newState && !FlashlightBattery.CanTurnOn)
        {
            _rigs.TryGetValue(id, out var rig);
            rig?.PlayDeadClick();
            FlashlightPlugin.Log.LogDebug("F pressed but the battery is dead.");
            return;
        }

        ApplyState(id, newState, FlashlightBattery.ToByte());
        SendLocalState(id, newState);

        FlashlightPlugin.Log.LogInfo($"Flashlight {(newState ? "ON" : "OFF")} (battery {FlashlightBattery.Level:P0})");
    }

    /// <summary>
    /// Sends the local player's current state to the host (clients) or straight
    /// to everyone (host). Used by toggles AND the periodic battery updates.
    /// </summary>
    private void SendLocalState(ulong id, bool on)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            return;
        }

        _nextBatterySend = Time.unscaledTime + 1f;
        if (nm.IsHost)
        {
            BroadcastState(id, on, FlashlightBattery.ToByte());
        }
        else
        {
            using var writer = new FastBufferWriter(16, Allocator.Temp);
            WriteState(writer, id, on, FlashlightBattery.ToByte());
            nm.CustomMessagingManager.SendNamedMessage(MsgState, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }
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
            FlashlightBattery.ForgetSession();
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

            ReadState(reader, out var targetId, out var on, out var battery);

            if (nm.IsHost)
            {
                // Self-echo of our own broadcast (NGO invokes the handler on the host
                // when it sends to all clients). State is already applied - do NOT
                // rebroadcast, that would recurse forever and kill the process.
                if (sender == nm.LocalClientId)
                {
                    return;
                }

                // A client toggled / updated their light: apply it, then relay to everyone.
                targetId = sender;
                ApplyState(targetId, on, battery);
                BroadcastState(targetId, on, battery);
            }
            else
            {
                ApplyState(targetId, on, battery); // relayed by the host
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
                    ReadState(reader, out var id, out var on, out var battery);
                    ApplyState(id, on, battery);
                }

                FlashlightPlugin.Log.LogInfo($"Received flashlight state dump ({count} player(s)).");
            }
        }
        catch (System.Exception e)
        {
            FlashlightPlugin.Log.LogError($"HandleSyncMessage failed: {e}");
        }
    }

    private void BroadcastState(ulong id, bool on, byte battery)
    {
        if (_nm == null || !_nm.IsListening)
        {
            return;
        }

        using var writer = new FastBufferWriter(16, Allocator.Temp);
        WriteState(writer, id, on, battery);
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
            byte on = pair.Value.On ? (byte)1 : (byte)0;
            byte bat = pair.Value.Battery;
            writer.WriteValueSafe(in id, default(FastBufferWriter.ForPrimitives));
            writer.WriteValueSafe(in on, default(FastBufferWriter.ForPrimitives));
            writer.WriteValueSafe(in bat, default(FastBufferWriter.ForPrimitives));
        }

        messaging.SendNamedMessage(MsgSync, clientId, writer, NetworkDelivery.Reliable);
    }

    private static void WriteState(FastBufferWriter writer, ulong id, bool on, byte battery)
    {
        ulong targetId = id;
        byte flag = on ? (byte)1 : (byte)0;
        writer.WriteValueSafe(in targetId, default(FastBufferWriter.ForPrimitives));
        writer.WriteValueSafe(in flag, default(FastBufferWriter.ForPrimitives));
        writer.WriteValueSafe(in battery, default(FastBufferWriter.ForPrimitives));
    }

    private static bool _warnedOldFormat;

    private static void ReadState(FastBufferReader reader, out ulong id, out bool on, out byte battery)
    {
        reader.ReadValueSafe(out id, default(FastBufferWriter.ForPrimitives));
        reader.ReadValueSafe(out byte flag, default(FastBufferWriter.ForPrimitives));
        // v1.1.0 peers send the old format without the battery byte. Accept
        // their messages and treat their light as full (they have no battery),
        // so toggle sync still works in mixed-version lobbies.
        if (reader.TryBeginRead(1))
        {
            reader.ReadValueSafe(out battery, default(FastBufferWriter.ForPrimitives));
        }
        else
        {
            battery = byte.MaxValue;
            if (!_warnedOldFormat)
            {
                _warnedOldFormat = true;
                FlashlightPlugin.Log.LogWarning(
                    "Received an old-format flashlight message (a v1.1.0 peer). " +
                    "Toggle sync will work, but that player has no battery - " +
                    "everyone should update to the same mod version.");
            }
        }

        on = flag != 0;
    }

    // ---------------------------------------------------------------- rigs

    private void ApplyState(ulong id, bool on, byte battery)
    {
        _states[id] = (on, battery);
        if (_rigs.TryGetValue(id, out var rig) && rig != null)
        {
            rig.SetOn(on);
            if (!rig.IsLocal)
            {
                // Remote rigs render the owner's battery level; local rigs use
                // their own simulation.
                rig.SetBattery(battery);
            }
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

                // Remote rigs that couldn't reach the hand yet keep retrying: the body
                // can load after the rig is created.
                if (rig != null && !rig.IsLocal && !rig.HandAttached && TryAttachToHand(rig.gameObject, player))
                {
                    rig.HandAttached = true;
                }

                // Keep rig visuals in sync with known state + spectating.
                var state = _states.TryGetValue(id, out var s) ? s : default;
                rig.SetOn(state.On);
                if (!rig.IsLocal)
                {
                    rig.SetBattery(state.Battery);
                }

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
        bool attached = false;

        if (isLocal && player.mainCam != null)
        {
            rigGo.transform.SetParent(player.mainCam.transform, false);
            rigGo.transform.localPosition = new Vector3(
                FlashlightPlugin.LocalRight.Value,
                -FlashlightPlugin.LocalDown.Value,
                FlashlightPlugin.LocalForward.Value);
            rigGo.transform.localRotation = Quaternion.Euler(0f, FlashlightPlugin.LocalYaw.Value, 0f);
        }
        else
        {
            // Remote players: hold the flashlight in the cat's right hand so it follows
            // the arm. If the body isn't loaded yet, a point beside the cat stands in and
            // MaintainRigs keeps retrying the hand.
            attached = TryAttachToHand(rigGo, player);
            if (!attached)
            {
                Transform anchor = FindHandAnchor(player, out Vector3 localPos);
                rigGo.transform.SetParent(anchor, false);
                rigGo.transform.localPosition = localPos;
                rigGo.transform.localRotation = Quaternion.Euler(
                    -FlashlightPlugin.RemotePitch.Value,
                    FlashlightPlugin.RemoteYaw.Value,
                    0f);
            }
        }

        var rig = rigGo.AddComponent<FlashlightRig>();
        rig.Init(player);
        rig.HandAttached = attached;
        if (isLocal)
        {
            FlashlightHud.EnsureCreated();
        }

        FlashlightPlugin.Log.LogDebug($"Rig created for {(isLocal ? "local" : "remote")} player {player.OwnerClientId}.");
        return rig;
    }

    /// <summary>
    /// Parents a remote player's flashlight to their right hand, aimed along the
    /// cat's facing with the grip at the palm. Returns false while no hand is found.
    /// </summary>
    private static bool TryAttachToHand(GameObject rigGo, PlayerManager player)
    {
        Transform hand = FindHandBone(player);
        if (hand == null)
        {
            return false;
        }

        rigGo.transform.SetParent(hand, false);
        rigGo.transform.localPosition = Vector3.zero;
        rigGo.transform.localRotation = Quaternion.identity;

        // Compensate for bone scale so the flashlight keeps its real size.
        var ls = hand.lossyScale;
        if (ls.x > 0.0001f && ls.y > 0.0001f && ls.z > 0.0001f)
        {
            rigGo.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        }

        // Aim along the cat's facing; RemoteForward and RemoteRight nudge from the wrist.
        Transform body = player.transform;
        rigGo.transform.rotation = body.rotation *
            Quaternion.Euler(-FlashlightPlugin.RemotePitch.Value, FlashlightPlugin.RemoteYaw.Value, 0f);
        rigGo.transform.position += body.forward * FlashlightPlugin.RemoteForward.Value +
            body.right * FlashlightPlugin.RemoteRight.Value;

        FlashlightPlugin.Log.LogInfo($"Flashlight held in '{hand.name}' for player {player.OwnerClientId}.");
        return true;
    }

    /// <summary>
    /// Finds the cat's right hand: the humanoid RightHand bone when the rig has an
    /// avatar, otherwise the best hand-like bone name in its hierarchy.
    /// </summary>
    private static Transform FindHandBone(PlayerManager player)
    {
        var animator = player.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            return null;
        }

        if (animator.isHuman)
        {
            Transform humanHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (humanHand != null)
            {
                return humanHand;
            }
        }

        var transforms = animator.GetComponentsInChildren<Transform>(true);

        Transform best = null;
        int bestScore = int.MinValue;
        foreach (var t in transforms)
        {
            string n = t.name.ToLowerInvariant();
            int score;
            if (n.Contains("hand") || n.Contains("paw"))
            {
                score = 100;
            }
            else if (n.Contains("wrist"))
            {
                score = 90;
            }
            else if (n.Contains("forearm"))
            {
                score = 55;
            }
            else if (n.Contains("arm"))
            {
                score = 60;
            }
            else
            {
                continue;
            }

            bool right = n.Contains("_r") || n.Contains(".r") || n.Contains("right") ||
                         (n.Length > 1 && n[n.Length - 1] == 'r' && !char.IsLetter(n[n.Length - 2]));
            if (right)
            {
                score += 30;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        if (best != null)
        {
            FlashlightPlugin.Log.LogInfo($"[Flashlight] Hand bone candidate: '{best.name}'");
        }

        return best;
    }

    /// <summary>
    /// Fallback "held in hand" anchor for biped cats: hip height at the side of
    /// the body, computed in model space so it follows the cat's facing.
    /// </summary>
    private static Transform FindHandAnchor(PlayerManager player, out Vector3 localPos)
    {
        var model = player.PlayerModel;
        if (model == null)
        {
            localPos = new Vector3(0.3f, 0.6f, 0.05f);
            return player.transform;
        }

        var renderer = model.GetComponentInChildren<Renderer>(true);
        if (renderer == null)
        {
            localPos = new Vector3(0f, 0.55f, 0.08f);
            return model;
        }

        var b = renderer.bounds;
        Vector3 handWorld = b.center
            - Vector3.up * (b.extents.y * 0.25f)          // hip height, just below center
            + model.right * (b.extents.x * 0.55f)         // at the side of the body
            + model.forward * 0.02f;                      // barely in front
        localPos = model.InverseTransformPoint(handWorld);
        return model;
    }
}
