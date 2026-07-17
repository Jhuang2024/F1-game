using System;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using UnityEngine;

namespace F1Game.UI.Navigation
{
    /// <summary>
    /// Owns the production frontend screens. Screen prefabs are instantiated
    /// once under a persistent canvas root and toggled with CanvasGroups on
    /// navigation — the whole-canvas destroy/rebuild the legacy RuntimeUi does
    /// per interaction never happens here.
    ///
    /// Every navigation publishes <see cref="UiNavigationEvent"/> so audio,
    /// analytics and the legacy shell can observe transitions without coupling.
    /// </summary>
    public sealed class ScreenRouter
    {
        readonly Transform screenRoot;
        readonly Dictionary<string, Func<Transform, ScreenView>> factories =
            new Dictionary<string, Func<Transform, ScreenView>>();
        readonly Dictionary<string, ScreenView> instances = new Dictionary<string, ScreenView>();
        readonly NavigationStack stack = new NavigationStack();

        ScreenView current;

        /// <summary>
        /// Optional enter animation, run after a screen becomes visible (wired
        /// by UiShell to TransitionService.FadeIn so navigation honours the
        /// theme's motion tokens instead of hard-cutting).
        /// </summary>
        public Action<ScreenView> EnterTransition;

        /// <summary>
        /// Runs on the outgoing screen BEFORE it is hidden (wired by UiShell to
        /// cancel any in-flight fade on that screen). Without this, a fade
        /// started by EnterTransition kept running after SetVisible(false) and
        /// raised the hidden screen's alpha back to 1 - during the boot
        /// instantiation pass that resurrected every screen in the stack and
        /// left the last one (Results) drawn on top of the main menu as a dead
        /// overlay (per report - "why do i open up the game to this now? a
        /// screen that doesn't work?").
        /// </summary>
        public Action<ScreenView> ExitTransition;

        public NavigationStack Stack => stack;
        public ScreenView Current => current;
        public string CurrentScreenId => current != null ? current.ScreenId : null;

        public ScreenRouter(Transform screenRoot)
        {
            this.screenRoot = screenRoot;
        }

        /// <summary>
        /// Registers a screen. The factory usually instantiates an authored
        /// prefab; it runs at most once per screen id.
        /// </summary>
        public void Register(string screenId, Func<Transform, ScreenView> factory)
        {
            factories[screenId] = factory;
        }

        public void RegisterPrefab(string screenId, ScreenView prefab)
        {
            Register(screenId, root => UnityEngine.Object.Instantiate(prefab, root));
        }

        public bool IsRegistered(string screenId) => factories.ContainsKey(screenId);

        public void Show(string screenId, object args = null)
        {
            NavigateTo(screenId, args, pushToStack: true);
        }

        /// <summary>Navigate without growing the back stack (redirects).</summary>
        public void Replace(string screenId, object args = null)
        {
            NavigateTo(screenId, args, pushToStack: false);
            stack.ReplaceCurrent(screenId, args);
        }

        public bool Back()
        {
            if (!stack.TryPop(out NavigationStack.Entry previous))
            {
                return false;
            }

            NavigateTo(previous.ScreenId, previous.Args, pushToStack: false);
            return true;
        }

        public void ResetStack()
        {
            stack.Clear();
        }

        void NavigateTo(string screenId, object args, bool pushToStack)
        {
            if (!factories.ContainsKey(screenId))
            {
                Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.UiScreenMissing, "Unknown screen id: " + screenId));
                return;
            }

            string fromId = CurrentScreenId;
            if (current != null)
            {
                current.OnExited();
                ExitTransition?.Invoke(current);
                current.SetVisible(false);
            }

            if (!instances.TryGetValue(screenId, out ScreenView view) || view == null)
            {
                view = factories[screenId](screenRoot);
                instances[screenId] = view;
            }

            current = view;
            view.SetVisible(true);
            EnterTransition?.Invoke(view);
            view.OnEntered(args);

            if (pushToStack)
            {
                stack.Push(screenId, args);
            }

            GameEvents.Publish(new UiNavigationEvent(fromId, screenId, false));
        }
    }
}
