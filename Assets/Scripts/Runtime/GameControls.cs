// GameControls.cs
// The flight controls as one InputActionMap made in code, rebindable (remake: Options > Controls, OptionKind.Binding).
// Defaults: the PC version's keys (Galaxy on Fire 2 Full HD, see CLAUDE.md "UI and platforms") and the remake's controller
// buttons. Every row has three slots: two keyboard / mouse bindings and one controller binding (Steer's controller slot is a
// whole stick or the D-pad, so the stick keeps its radial dead zone). The player's changes are binding overrides kept in
// PlayerPrefs ("controls_bindings"); an empty override unbinds a slot. Menus keep their fixed keys (arrows, Enter, Esc,
// controller A / B / Menu) so no binding can lock the player out; the pause key (Esc / Menu) isn't rebindable either.
// Rebinding a key another row uses swaps the two.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GoF2Remake.Flight
{
    public enum BindSlot { Key1, Key2, Pad }

    /// <summary>One line of the key bindings list: an action (or a composite's parts) and its three slots.</summary>
    public sealed class ControlRow
    {
        public string id;
        public Func<string> label;
        public InputAction action;
        /// <summary>Composite rows: one prompt label per part, in the order the slots list them; null = one binding.</summary>
        public Func<string>[] partLabels;
        /// <summary>Binding indices per slot (a composite slot: its parts); null = no such slot.</summary>
        public readonly int[][] slots = new int[3][];
        /// <summary>The control type the controller slot takes ("Button" / "Vector2").</summary>
        public string padType = "Button";

        public bool HasSlot(BindSlot s) => slots[(int)s] != null;
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameControls
    {
        // v2: action|bindingIndex=overridePath per line. v1 stored the Input System's own JSON, keyed by binding ids that
        // AddBinding regenerates on every launch, so nothing ever loaded back.
        const string PrefsKey = "controls_bindings2";
        const string KeyGroup = "Keyboard", PadGroup = "Gamepad";

        public static readonly InputActionMap Map = new InputActionMap("Flight");
        static readonly List<ControlRow> rows = new List<ControlRow>();
        public static IReadOnlyList<ControlRow> Rows => rows;

        /// <summary>A binding changed (a rebind, a reset): hints and option rows show the new keys.</summary>
        public static event Action Changed;

        public static readonly InputAction Steer, Throttle, Brake, Boost, LevelOut, Roll, StrafeLeft, StrafeRight, DodgeLeft, DodgeRight,
            FirePrimary, FireSecondary, SwitchSecondary, Action, AutopilotMenu, ActionsMenu, Wingmen, KhadorDrive, FastForward,
            Camera, AutoTurret, Cloak, TimeExtender, MouseSteering, Chat, Screenshot;

        static string X(string key, string english) => Localization.Extra(key, english);

        static GameControls()
        {
            // ---- flying
            Steer = Composite("steer", () => X("ctlSteer", "Steer"), InputActionType.Value, "Vector2", "2DVector",
                new[] { "Up", "Down", "Left", "Right" },
                new Func<string>[] { () => X("ctlUp", "up"), () => X("ctlDown", "down"), () => X("ctlLeft", "left"), () => X("ctlRight", "right") },
                new[] { "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow" },
                padSingle: "<Gamepad>/leftStick", padType: "Vector2");
            Throttle = Composite("throttle", () => X("ctlThrottle", "Throttle"), InputActionType.Value, "Axis", "1DAxis",
                new[] { "Positive", "Negative" },
                new Func<string>[] { () => X("ctlFaster", "faster"), () => X("ctlSlower", "slower") },
                new[] { "<Keyboard>/rightBracket", "<Keyboard>/slash" },
                padParts: new[] { "<Gamepad>/rightShoulder", "<Gamepad>/leftShoulder" });
            Brake = Button("brake", () => X("ctlBrake", "Brake"), "<Keyboard>/s", null, null);
            Boost = Button("boost", () => X("ctlBoost", "Boost"), "<Keyboard>/w", null, "<Gamepad>/buttonSouth");
            Roll = Composite("roll", () => X("ctlRoll", "Roll"), InputActionType.Value, "Axis", "1DAxis",
                new[] { "Negative", "Positive" },
                new Func<string>[] { () => X("ctlLeft", "left"), () => X("ctlRight", "right") },
                new[] { "<Keyboard>/1", "<Keyboard>/3" }, padParts: new string[] { null, null });
            LevelOut = Button("levelOut", () => X("ctlLevelOut", "Level out"), "<Keyboard>/2", null, "<Gamepad>/buttonNorth");
            // The PC version's binding screen: 3350 / 3351 "Strafe left / right" (held, PlayerEgo::strafe). The dodge (the
            // phone's swipe; the right stick's flick) has no keyboard default.
            StrafeLeft = Button("strafeLeft", () => Localization.Get(3350), "<Keyboard>/a", null, null);
            StrafeRight = Button("strafeRight", () => Localization.Get(3351), "<Keyboard>/d", null, null);
            DodgeLeft = Button("dodgeLeft", () => X("ctlDodgeLeft", "Dodge left"), null, null, null);
            DodgeRight = Button("dodgeRight", () => X("ctlDodgeRight", "Dodge right"), null, null, null);
            // ---- weapons
            FirePrimary = Button("firePrimary", () => X("ctlFirePrimary", "Fire"), "<Keyboard>/space", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            FireSecondary = Button("fireSecondary", () => X("ctlFireSecondary", "Fire secondary"), "<Keyboard>/r", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            SwitchSecondary = Button("switchSecondary", () => X("ctlSwitchSecondary", "Switch secondary"), "<Keyboard>/g", null, "<Gamepad>/dpad/right");
            Camera = Button("camera", () => X("ctlCamera", "Camera / turret view"), "<Keyboard>/t", null, "<Gamepad>/dpad/up");
            AutoTurret = Button("autoTurret", () => X("ctlAutoTurret", "Auto turret on / off"), "<Keyboard>/y", null, "<Gamepad>/dpad/down");
            // ---- navigation and equipment
            Action = Button("action", () => X("ctlAction", "Action (dock, autopilot, jump, mine)"), "<Keyboard>/f", "<Keyboard>/enter", "<Gamepad>/buttonWest");
            AutopilotMenu = Button("autopilotMenu", () => X("ctlAutopilotMenu", "Autopilot menu"), "<Keyboard>/q", null, "<Gamepad>/select");
            // The PC version's E "Actions": the quick menu (Hud::initHudMenu(0): secondary weapons, wingmen, cloak, Khador Drive).
            ActionsMenu = Button("actionsMenu", () => X("ctlActionsMenu", "Actions menu"), "<Keyboard>/e", null, "<Gamepad>/dpad/left");
            FastForward = Button("fastForward", () => X("ctlFastForward", "Fast-forward (hold)"), "<Keyboard>/tab", null, "<Gamepad>/buttonNorth");
            Wingmen = Button("wingmen", () => X("ctlWingmen", "Wingmen"), "<Keyboard>/v", null, null);
            KhadorDrive = Button("khadorDrive", () => X("ctlKhador", "Khador Drive"), "<Keyboard>/k", null, null);
            Cloak = Button("cloak", () => X("ctlCloak", "Cloak"), "<Keyboard>/c", null, "<Gamepad>/rightStickPress");
            TimeExtender = Button("timeExtender", () => X("ctlTimeExtender", "Time extender"), "<Keyboard>/x", null, "<Gamepad>/leftStickPress");
            // ---- other
            MouseSteering = Button("mouseSteering", () => X("ctlMouseSteering", "Mouse steering on / off"), "<Keyboard>/m", "<Mouse>/middleButton", null, padSlot: false);
            Chat = Button("chat", () => X("ctlChat", "Chat (multiplayer)"), "<Keyboard>/b", null, null);
            Screenshot = Button("screenshot", () => X("ctlScreenshot", "Screenshot"), "<Keyboard>/f12", null, null);
        }

        static InputAction Button(string id, Func<string> label, string key1, string key2, string pad, bool padSlot = true)
        {
            var a = Map.AddAction(id, InputActionType.Button);
            var row = new ControlRow { id = id, label = label, action = a };
            row.slots[0] = new[] { Add(a, key1, KeyGroup) };
            row.slots[1] = new[] { Add(a, key2, KeyGroup) };
            if (padSlot) row.slots[2] = new[] { Add(a, pad, PadGroup) };
            rows.Add(row);
            return a;
        }

        /// <summary>A composite row: two keyboard composites (the second unbound) and, on the controller, the same composite
        /// ('padParts') or one control ('padSingle', a stick).</summary>
        static InputAction Composite(string id, Func<string> label, InputActionType type, string controlType, string composite,
                                     string[] parts, Func<string>[] partLabels, string[] keys, string[] padParts = null,
                                     string padSingle = null, string padType = "Button")
        {
            var a = Map.AddAction(id, type);
            a.expectedControlType = controlType;
            var row = new ControlRow { id = id, label = label, action = a, partLabels = partLabels, padType = padType };
            row.slots[0] = AddComposite(a, composite, parts, keys, KeyGroup);
            row.slots[1] = AddComposite(a, composite, parts, new string[parts.Length], KeyGroup);
            if (padSingle != null) row.slots[2] = new[] { Add(a, padSingle, PadGroup) };
            else if (padParts != null) row.slots[2] = AddComposite(a, composite, parts, padParts, PadGroup);
            rows.Add(row);
            return a;
        }

        static int Add(InputAction a, string path, string group)
        {
            a.AddBinding(path ?? "", groups: group);
            return a.bindings.Count - 1;
        }

        static int[] AddComposite(InputAction a, string composite, string[] parts, string[] paths, string group)
        {
            var syntax = a.AddCompositeBinding(composite);
            for (int i = 0; i < parts.Length; i++) syntax.With(parts[i], paths[i] ?? "", group);
            int first = a.bindings.Count - parts.Length;
            var indices = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) indices[i] = first + i;
            return indices;
        }

        // ---- lifetime ------------------------------------------------------------------------------------------

        static int suspended;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Start()
        {
            CancelRebind();
            suspended = 0;
            Load();
            Map.Enable();
        }

        /// <summary>Chat typing (NetChat) and rebinding switch the game's controls off; nested calls count.</summary>
        public static void Suspend(bool on)
        {
            suspended = Math.Max(0, suspended + (on ? 1 : -1));
            if (suspended > 0) Map.Disable();
            else if (Application.isPlaying) Map.Enable();
        }

        public static void ResumeAll()
        {
            suspended = 0;
            if (Application.isPlaying) Map.Enable();
        }

        static void Load()
        {
            Map.RemoveAllBindingOverrides();
            foreach (var line in PlayerPrefs.GetString(PrefsKey, "").Split('\n'))
            {
                int bar = line.IndexOf('|'), eq = line.IndexOf('=');
                if (bar < 0 || eq < bar) continue;
                var a = Map.FindAction(line.Substring(0, bar));
                if (a != null && int.TryParse(line.Substring(bar + 1, eq - bar - 1), out int i) && i >= 0 && i < a.bindings.Count)
                    a.ApplyBindingOverride(i, line.Substring(eq + 1));   // "" = unbound
            }
        }

        static void Save()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var a in Map.actions)
                for (int i = 0; i < a.bindings.Count; i++)
                    if (a.bindings[i].overridePath != null) sb.Append(a.name).Append('|').Append(i).Append('=').Append(a.bindings[i].overridePath).Append('\n');
            PlayerPrefs.SetString(PrefsKey, sb.ToString());
            PlayerPrefs.Save();
        }

        /// <summary>Every binding back to its default (the options' "Reset key bindings" and Default settings, 497).</summary>
        public static void ResetToDefaults()
        {
            CancelRebind();
            Map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // ---- lookups --------------------------------------------------------------------------------------------

        public static ControlRow RowOf(InputAction action) => rows.Find(r => r.action == action);

        static bool IsPad(BindSlot s) => s == BindSlot.Pad;

        /// <summary>The slot's effective control paths (a composite: its parts; unbound parts are "").</summary>
        public static string[] Paths(ControlRow row, BindSlot slot)
        {
            var idx = row.slots[(int)slot];
            if (idx == null) return Array.Empty<string>();
            var paths = new string[idx.Length];
            for (int i = 0; i < idx.Length; i++) paths[i] = row.action.bindings[idx[i]].effectivePath ?? "";
            return paths;
        }

        public static bool IsBound(ControlRow row, BindSlot slot)
        {
            foreach (var p in Paths(row, slot)) if (!string.IsNullOrEmpty(p)) return true;
            return false;
        }

        /// <summary>A controller binding uses this control (the right stick flick dodges only while nothing steers with it).</summary>
        public static bool PadUses(string controlPath)
        {
            foreach (var row in rows)
                foreach (var p in Paths(row, BindSlot.Pad))
                    if (!string.IsNullOrEmpty(p) && p.StartsWith(controlPath, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The short name the HUD and the options show for one control path ("F", "SPACE", "LMB", "A", "RT", "LS").</summary>
        public static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path.ToLowerInvariant();
            if (p.StartsWith("<gamepad>/"))
            {
                string c = p.Substring("<gamepad>/".Length);
                return c switch
                {
                    "buttonsouth" => "A", "buttoneast" => "B", "buttonwest" => "X", "buttonnorth" => "Y",
                    "leftshoulder" => "LB", "rightshoulder" => "RB", "lefttrigger" => "LT", "righttrigger" => "RT",
                    "leftstick" => "LS", "rightstick" => "RS", "leftstickpress" => "LS", "rightstickpress" => "RS",
                    "dpad" => "D-PAD", "dpad/up" => "D-PAD ↑", "dpad/down" => "D-PAD ↓", "dpad/left" => "D-PAD ←", "dpad/right" => "D-PAD →",
                    "start" => "MENU", "select" => "VIEW",
                    _ => InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant(),
                };
            }
            if (p.StartsWith("<mouse>/"))
            {
                return p.Substring("<mouse>/".Length) switch
                {
                    "leftbutton" => "LMB", "rightbutton" => "RMB", "middlebutton" => "MMB", "backbutton" => "MB4", "forwardbutton" => "MB5",
                    _ => InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant(),
                };
            }
            switch (p)
            {
                case "<keyboard>/uparrow": return "↑";
                case "<keyboard>/downarrow": return "↓";
                case "<keyboard>/leftarrow": return "←";
                case "<keyboard>/rightarrow": return "→";
                case "<keyboard>/space": return X("keySpace", "Space").ToUpperInvariant();
                case "<keyboard>/enter": return "ENTER";
                case "<keyboard>/escape": return "ESC";
                case "<keyboard>/tab": return "TAB";
                case "<keyboard>/leftshift": return "L-SHIFT";
                case "<keyboard>/rightshift": return "R-SHIFT";
                case "<keyboard>/leftctrl": return "L-CTRL";
                case "<keyboard>/rightctrl": return "R-CTRL";
                case "<keyboard>/leftalt": return "L-ALT";
                case "<keyboard>/rightalt": return "R-ALT";
            }
            // The key's name on this keyboard's layout ("]" for rightBracket on a US keyboard).
            var kb = Keyboard.current;
            var control = kb != null ? InputControlPath.TryFindControl(kb, path) : null;
            string name = control != null ? control.displayName : InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(name) ? "?" : name.ToUpperInvariant();
        }

        /// <summary>The slot as text: its key, or a composite's keys ("↑ ↓ ← →"); "" = unbound.</summary>
        public static string SlotText(ControlRow row, BindSlot slot)
        {
            if (!IsBound(row, slot)) return "";
            var names = new List<string>();
            foreach (var p in Paths(row, slot)) names.Add(string.IsNullOrEmpty(p) ? "–" : ShortName(p));
            return string.Join(" ", names);
        }

        /// <summary>The first bound slot's text for the keyboard or the controller ("" = none): the flight hints' #KEY_ tokens.</summary>
        public static string KeyText(InputAction action, bool pad)
        {
            var row = RowOf(action);
            if (row == null) return "";
            if (pad) return row.HasSlot(BindSlot.Pad) ? SlotText(row, BindSlot.Pad) : "";
            string k = SlotText(row, BindSlot.Key1);
            return k.Length > 0 ? k : SlotText(row, BindSlot.Key2);
        }

        // ---- rebinding -----------------------------------------------------------------------------------------

        static InputActionRebindingExtensions.RebindingOperation operation;
        static int rebindEndFrame = -10;

        /// <summary>A key is being captured: the menus ignore their keys meanwhile (and on the frame it ends, so the
        /// captured key doesn't also act in the menu).</summary>
        public static bool Rebinding => operation != null;
        public static bool BlocksMenus => operation != null || Time.frameCount <= rebindEndFrame + 1;

        /// <summary>Captures the slot's binding (a composite slot part by part). 'prompt' gets the part being asked for
        /// ("up", or null for a single binding); 'done' runs once it ended (captured, cleared or cancelled). Esc (or the
        /// controller's Menu) cancels, Backspace / Delete unbinds the slot.</summary>
        public static void Rebind(ControlRow row, BindSlot slot, Action<string> prompt, Action done)
        {
            CancelRebind();
            var idx = row.slots[(int)slot];
            if (idx == null) { done?.Invoke(); return; }
            Suspend(true);
            RebindPart(row, slot, 0, prompt, done);
        }

        static void RebindPart(ControlRow row, BindSlot slot, int part, Action<string> prompt, Action done)
        {
            var idx = row.slots[(int)slot];
            int index = idx[part];
            bool single = idx.Length == 1;
            prompt?.Invoke(single ? null : row.partLabels?[part]?.Invoke());
            string old = row.action.bindings[index].effectivePath ?? "";
            bool clear = false;

            var op = row.action.PerformInteractiveRebinding(index)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .OnMatchWaitForAnother(0.1f);
            if (IsPad(slot))
            {
                op.WithControlsHavingToMatchPath("<Gamepad>")
                  .WithExpectedControlType(single && row.padType == "Vector2" ? "Vector2" : "Button");
                // A stick direction is a valid button (roll, throttle, the dodge on the right stick); a drifting stick stays
                // below the half-way mark that a deliberate push passes.
                op.WithMagnitudeHavingToBeGreaterThan(0.5f);
            }
            else
            {
                op.WithControlsHavingToMatchPath("<Keyboard>")
                  .WithControlsHavingToMatchPath("<Mouse>")
                  .WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta")
                  .WithControlsExcluding("<Mouse>/scroll").WithControlsExcluding("<Mouse>/press")
                  .WithExpectedControlType("Button");
            }
            op.OnPotentialMatch(o =>
            {
                var c = o.selectedControl;
                if (c is KeyControl k && (k.keyCode == Key.Backspace || k.keyCode == Key.Delete)) { clear = true; o.Cancel(); }
                else if (Gamepad.current != null && c == Gamepad.current.startButton) o.Cancel();   // the pause button stays fixed
            });
            op.OnComplete(o =>
            {
                string now = row.action.bindings[index].effectivePath ?? "";
                SwapOthers(row.action, index, slot, now, old);
                Dispose();
                if (part + 1 < idx.Length) RebindPart(row, slot, part + 1, prompt, done);
                else Finish(done);
            });
            op.OnCancel(o =>
            {
                if (clear)
                    foreach (int i in idx) row.action.ApplyBindingOverride(i, "");
                Dispose();
                Finish(done);
            });
            operation = op;
            op.Start();
        }

        static void Dispose()
        {
            var op = operation;
            operation = null;
            op?.Dispose();
        }

        static void Finish(Action done)
        {
            rebindEndFrame = Time.frameCount;
            Suspend(false);
            Save();
            Changed?.Invoke();
            done?.Invoke();
        }

        /// <summary>Stops a capture in progress (a menu closed meanwhile), keeping what was bound so far.</summary>
        public static void CancelRebind()
        {
            if (operation == null) return;
            var op = operation;
            operation = null;
            op.Dispose();
            rebindEndFrame = Time.frameCount;
            Suspend(false);
            Save();
            Changed?.Invoke();
        }

        /// <summary>Unbinds one slot (a right click on it in the options).</summary>
        public static void Clear(ControlRow row, BindSlot slot)
        {
            var idx = row.slots[(int)slot];
            if (idx == null) return;
            foreach (int i in idx) row.action.ApplyBindingOverride(i, "");
            Save();
            Changed?.Invoke();
        }

        /// <summary>Another binding of the same kind (keyboard / controller) that had the new control gets the old one.</summary>
        static void SwapOthers(InputAction action, int index, BindSlot slot, string now, string old)
        {
            if (string.IsNullOrEmpty(now)) return;
            bool pad = IsPad(slot);
            foreach (var row in rows)
                for (int s = 0; s < 3; s++)
                {
                    if (row.slots[s] == null || (s == (int)BindSlot.Pad) != pad) continue;
                    foreach (int i in row.slots[s])
                    {
                        if (row.action == action && i == index) continue;
                        if (string.Equals(row.action.bindings[i].effectivePath, now, StringComparison.OrdinalIgnoreCase))
                            row.action.ApplyBindingOverride(i, old);
                    }
                }
        }
    }
}
