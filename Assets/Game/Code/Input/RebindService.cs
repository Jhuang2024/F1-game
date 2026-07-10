using System;
using System.Collections.Generic;
using F1Game.Core.Persistence;
using UnityEngine.InputSystem;

namespace F1Game.Input
{
    /// <summary>
    /// Runtime rebinding on top of the Input System: interactive listening,
    /// conflict detection within the same action map, multiple binding slots
    /// per action, and persistence of override JSON through the hardened save
    /// service (with backups, like every other save).
    /// </summary>
    public sealed class RebindService
    {
        const string OverridesFile = "formula_racing_bindings.json";

        [Serializable]
        class OverridesEnvelope
        {
            public int schemaVersion = 1;
            public string overridesJson = "";
        }

        readonly InputActionAsset asset;
        InputActionRebindingExtensions.RebindingOperation activeOperation;

        public bool IsListening => activeOperation != null;

        public RebindService(InputActionAsset asset)
        {
            this.asset = asset;
        }

        /// <summary>
        /// Starts interactive rebinding for one binding slot of an action.
        /// Cancel with Escape. onComplete reports success plus any conflict.
        /// </summary>
        public void StartInteractiveRebind(InputAction action, int bindingIndex, Action<bool, string> onComplete)
        {
            if (action == null || IsListening)
            {
                onComplete?.Invoke(false, "busy");
                return;
            }

            bool wasEnabled = action.enabled;
            action.Disable();

            activeOperation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(_ =>
                {
                    Cleanup(action, wasEnabled);
                    onComplete?.Invoke(false, "cancelled");
                })
                .OnComplete(operation =>
                {
                    string conflict = FindConflict(action, bindingIndex);
                    if (conflict != null)
                    {
                        action.RemoveBindingOverride(bindingIndex);
                        Cleanup(action, wasEnabled);
                        onComplete?.Invoke(false, "conflicts with " + conflict);
                        return;
                    }

                    Cleanup(action, wasEnabled);
                    SaveOverrides();
                    onComplete?.Invoke(true, null);
                })
                .Start();
        }

        public void CancelListening()
        {
            activeOperation?.Cancel();
        }

        void Cleanup(InputAction action, bool reEnable)
        {
            activeOperation?.Dispose();
            activeOperation = null;
            if (reEnable)
            {
                action.Enable();
            }
        }

        /// <summary>Same-map conflict detection: returns the conflicting action name, or null.</summary>
        public string FindConflict(InputAction action, int bindingIndex)
        {
            InputBinding binding = action.bindings[bindingIndex];
            string path = binding.effectivePath;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            foreach (InputAction other in action.actionMap.actions)
            {
                for (int i = 0; i < other.bindings.Count; i++)
                {
                    if (other == action && i == bindingIndex)
                    {
                        continue;
                    }

                    if (other.bindings[i].isComposite)
                    {
                        continue;
                    }

                    if (other.bindings[i].effectivePath == path)
                    {
                        return other.name;
                    }
                }
            }

            return null;
        }

        /// <summary>Indices of all non-composite binding slots for an action (multi-slot UI).</summary>
        public List<int> BindingSlots(InputAction action, string controlSchemeGroup = null)
        {
            var slots = new List<int>();
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite)
                {
                    continue;
                }

                if (controlSchemeGroup != null && !string.IsNullOrEmpty(binding.groups) &&
                    !binding.groups.Contains(controlSchemeGroup))
                {
                    continue;
                }

                slots.Add(i);
            }

            return slots;
        }

        public void ResetAllToDefaults()
        {
            asset.RemoveAllBindingOverrides();
            SaveOverrides();
        }

        public void SaveOverrides()
        {
            JsonSaveService.Save(OverridesFile, new OverridesEnvelope
            {
                overridesJson = asset.SaveBindingOverridesAsJson(),
            });
        }

        public void LoadOverrides()
        {
            OverridesEnvelope envelope = JsonSaveService.Load(OverridesFile, new OverridesEnvelope());
            if (!string.IsNullOrEmpty(envelope.overridesJson))
            {
                asset.LoadBindingOverridesFromJson(envelope.overridesJson);
            }
        }
    }
}
