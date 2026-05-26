using Gley.Common.Editor;
using System;
using UnityEditor;
using UnityEngine;
namespace Gley.UrbanSystem.Editor
{
    public class WindowLoader : EditorWindow
    {
        public static T LoadWindow<T>(string WINDOW_NAME, int MIN_WIDTH, int MIN_HEIGHT, IVersion version) where T : SettingsWindowBase
        {
            T window = (T)GetWindow(typeof(T));
            window.titleContent = new GUIContent(WINDOW_NAME + version.LongVersion);
            window.minSize = new Vector2(MIN_WIDTH, MIN_HEIGHT);
            window.Show();
            return (T)Convert.ChangeType(window, typeof(T));
        }
    }
}
