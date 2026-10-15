using System.IO;
using System.Reflection;
using FPS.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS.Tests
{
    internal static class TestInput
    {
        private const string ActionsPath = "Assets/_Project/Settings/Input/InputSystem_Actions.inputactions";

        /// <summary>
        /// Batch-mode play sessions have no window focus; keep keyboard and mouse enabled so multi-frame tests receive input.
        /// Call after <c>InputTestFixture.Setup</c>, which resets settings.
        /// </summary>
        public static void IgnoreFocus() => InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

        /// <summary>An InputReader bound to a private copy of the project's input actions. Destroy both after use.</summary>
        public static InputReader CreateReader(out InputActionAsset actions)
        {
            actions = InputActionAsset.FromJson(File.ReadAllText(ActionsPath));
            var reader = ScriptableObject.CreateInstance<InputReader>();
            typeof(InputReader)
                .GetField(InputReader.ActionAssetFieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(reader, actions);
            return reader;
        }
    }
}
