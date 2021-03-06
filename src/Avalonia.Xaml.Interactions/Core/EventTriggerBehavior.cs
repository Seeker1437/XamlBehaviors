// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Avalonia.Xaml.Interactivity;
using Avalonia.Controls;

namespace Avalonia.Xaml.Interactions.Core
{
    /// <summary>
    /// A behavior that listens for a specified event on its source and executes its actions when that event is fired.
    /// </summary>
    public sealed class EventTriggerBehavior : Trigger
    {
        private const string EventNameDefaultValue = "Loaded";

        static EventTriggerBehavior()
        {
            EventNameProperty.Changed.Subscribe(e =>
            {
                EventTriggerBehavior behavior = (EventTriggerBehavior)e.Sender;
                if (behavior.AssociatedObject == null || behavior.resolvedSource == null)
                {
                    return;
                }

                string oldEventName = (string)e.OldValue;
                string newEventName = (string)e.NewValue;

                behavior.UnregisterEvent(oldEventName);
                behavior.RegisterEvent(newEventName);
            });

            SourceObjectProperty.Changed.Subscribe(e =>
            {
                EventTriggerBehavior behavior = (EventTriggerBehavior)e.Sender;
                behavior.SetResolvedSource(behavior.ComputeResolvedSource());
            });
        }

        /// <summary>
        /// Identifies the <seealso cref="EventName"/> avalonia property.
        /// </summary>
        public static readonly AvaloniaProperty<string> EventNameProperty =
            AvaloniaProperty.Register<EventTriggerBehavior, string>(nameof(EventName), EventNameDefaultValue);

        /// <summary>
        /// Identifies the <seealso cref="SourceObject"/> avalonia property.
        /// </summary>
        public static readonly AvaloniaProperty<object> SourceObjectProperty =
            AvaloniaProperty.Register<EventTriggerBehavior, object>(nameof(SourceObject));

        private object resolvedSource;
        private Delegate eventHandler;
        private bool isLoadedEventRegistered;
        private bool isWindowsRuntimeEvent;
        private Func<Delegate, EventRegistrationToken> addEventHandlerMethod;
        private Action<EventRegistrationToken> removeEventHandlerMethod;

        /// <summary>
        /// Gets or sets the name of the event to listen for. This is a avalonia property.
        /// </summary>
        public string EventName
        {
            get { return this.GetValue(EventNameProperty); }
            set { this.SetValue(EventNameProperty, value); }
        }

        /// <summary>
        /// Gets or sets the source object from which this behavior listens for events.
        /// If <seealso cref="SourceObject"/> is not set, the source will default to <seealso cref="Behavior.AssociatedObject"/>. This is a avalonia property.
        /// </summary>
        public object SourceObject
        {
            get { return this.GetValue(SourceObjectProperty); }
            set { this.SetValue(SourceObjectProperty, value); }
        }

        /// <summary>
        /// Called after the behavior is attached to the <see cref="Behavior.AssociatedObject"/>.
        /// </summary>
        protected override void OnAttached()
        {
            base.OnAttached();
            this.SetResolvedSource(this.ComputeResolvedSource());
        }

        /// <summary>
        /// Called when the behavior is being detached from its <see cref="Behavior.AssociatedObject"/>.
        /// </summary>
        protected override void OnDetaching()
        {
            base.OnDetaching();
            this.SetResolvedSource(null);
        }

        private void SetResolvedSource(object newSource)
        {
            if (this.AssociatedObject == null || this.resolvedSource == newSource)
            {
                return;
            }

            if (this.resolvedSource != null)
            {
                this.UnregisterEvent(this.EventName);
            }

            this.resolvedSource = newSource;

            if (this.resolvedSource != null)
            {
                this.RegisterEvent(this.EventName);
            }
        }

        private object ComputeResolvedSource()
        {
            // If the SourceObject property is set at all, we want to use it. It is possible that it is data
            // bound and bindings haven't been evaluated yet. Plus, this makes the API more predictable.
            if (this.GetValue(SourceObjectProperty) != AvaloniaProperty.UnsetValue)
            {
                return this.SourceObject;
            }

            return this.AssociatedObject;
        }

        private void RegisterEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }

            if (eventName != EventNameDefaultValue)
            {
                Type sourceObjectType = this.resolvedSource.GetType();
                EventInfo info = sourceObjectType.GetRuntimeEvent(this.EventName);
                if (info == null)
                {
                    throw new ArgumentException(string.Format(
                        CultureInfo.CurrentCulture,
                        "Cannot find an event named {0} on type {1}.",
                        this.EventName,
                        sourceObjectType.Name));
                }

                MethodInfo methodInfo = typeof(EventTriggerBehavior).GetTypeInfo().GetDeclaredMethod("OnEvent");
                this.eventHandler = methodInfo.CreateDelegate(info.EventHandlerType, this);

                this.isWindowsRuntimeEvent = IsWindowsRuntimeType(info.EventHandlerType);
                if (this.isWindowsRuntimeEvent)
                {
                    this.addEventHandlerMethod = add => (EventRegistrationToken)info.AddMethod.Invoke(this.resolvedSource, new object[] { add });
                    this.removeEventHandlerMethod = token => info.RemoveMethod.Invoke(this.resolvedSource, new object[] { token });

                    WindowsRuntimeMarshal.AddEventHandler(this.addEventHandlerMethod, this.removeEventHandlerMethod, this.eventHandler);
                }
                else
                {
                    info.AddEventHandler(this.resolvedSource, this.eventHandler);
                }
            }
            else if (!this.isLoadedEventRegistered)
            {
                Control element = this.resolvedSource as Control;
                if (element != null && !IsElementLoaded(element))
                {
                    this.isLoadedEventRegistered = true;
                    element.AttachedToVisualTree += this.OnEvent;
                }
            }
        }

        private void UnregisterEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }

            if (eventName != EventNameDefaultValue)
            {
                if (this.eventHandler == null)
                {
                    return;
                }

                EventInfo info = this.resolvedSource.GetType().GetRuntimeEvent(eventName);
                if (this.isWindowsRuntimeEvent)
                {
                    WindowsRuntimeMarshal.RemoveEventHandler(this.removeEventHandlerMethod, this.eventHandler);
                }
                else
                {
                    info.RemoveEventHandler(this.resolvedSource, this.eventHandler);
                }

                this.eventHandler = null;
            }
            else if (this.isLoadedEventRegistered)
            {
                this.isLoadedEventRegistered = false;
                Control element = (Control)this.resolvedSource;
                element.AttachedToVisualTree -= this.OnEvent;
            }
        }

        private void OnEvent(object sender, object eventArgs)
        {
            Interaction.ExecuteActions(this.resolvedSource, this.Actions, eventArgs);
        }

        internal static bool IsElementLoaded(Control element)
        {
            if (element == null)
            {
                return false;
            }

            return (element.Parent != null);
        }

        private static bool IsWindowsRuntimeType(Type type)
        {
            if (type != null)
            {
                return type.AssemblyQualifiedName.EndsWith("ContentType=WindowsRuntime", StringComparison.Ordinal);
            }

            return false;
        }
    }
}
