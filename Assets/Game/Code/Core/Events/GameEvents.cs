using System;
using System.Collections.Generic;

namespace F1Game.Core.Events
{
    /// <summary>
    /// Typed, allocation-free publish/subscribe hub for game-wide events.
    ///
    /// Systems (HUD widgets, audio, presentation, save coordinator) subscribe to
    /// the event structs they care about instead of polling the race/UI monoliths.
    /// Publishing iterates a plain list (no delegate-combine allocation per publish)
    /// and isolates subscriber exceptions so one broken listener cannot stall the
    /// race loop or starve later subscribers.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>Raised when a subscriber throws; wired to logging by the bootstrap.</summary>
        public static event Action<Type, Exception> HandlerError;

        static readonly List<Action> resetActions = new List<Action>();

        static class Channel<T> where T : struct
        {
            public static readonly List<Action<T>> Handlers = new List<Action<T>>();
            public static bool Registered;
        }

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                return;
            }

            if (!Channel<T>.Registered)
            {
                Channel<T>.Registered = true;
                lock (resetActions)
                {
                    resetActions.Add(() => Channel<T>.Handlers.Clear());
                }
            }

            Channel<T>.Handlers.Add(handler);
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                return;
            }

            Channel<T>.Handlers.Remove(handler);
        }

        public static void Publish<T>(in T evt) where T : struct
        {
            List<Action<T>> handlers = Channel<T>.Handlers;

            // Iterate by index so subscribers added during publish are deferred to
            // the next publish, and removal during publish cannot skip entries.
            int count = handlers.Count;
            for (int i = 0; i < count && i < handlers.Count; i++)
            {
                try
                {
                    handlers[i]?.Invoke(evt);
                }
                catch (Exception exception)
                {
                    HandlerError?.Invoke(typeof(T), exception);
                }
            }
        }

        public static int SubscriberCount<T>() where T : struct
        {
            return Channel<T>.Handlers.Count;
        }

        /// <summary>
        /// Clears every channel. Called on race-world teardown and between tests so
        /// stale subscribers from a destroyed session never receive events.
        /// </summary>
        public static void Reset()
        {
            lock (resetActions)
            {
                for (int i = 0; i < resetActions.Count; i++)
                {
                    resetActions[i]();
                }
            }
        }
    }
}
