using System;
using UnityEditor;
using UnityEngine;
namespace Gley.Common.Editor
{
    public class WindowLoader : EditorWindow
    {
        public static T LoadWindow<T>(ISettingsWindowProperties windowProperties, IVersion version, out string rootFolder) where T : EditorWindow
        {
            rootFolder = GetRootFolder(windowProperties);  
            T window = (T)GetWindow(typeof(T));
            window.titleContent = new GUIContent(windowProperties.WindowName + version.LongVersion);
            window.minSize = new Vector2(windowProperties.MinWidth, windowProperties.MinHeight);
            window.Show();
            return (T)Convert.ChangeType(window, typeof(T));
        }

        public static T LoadWindow<T>(ISettingsWindowProperties windowProperties, IVersion version, out string rootFolder,out string rootWithoutAssets) where T : EditorWindow
        {
            T result = LoadWindow<T>(windowProperties, version, out rootFolder);
            rootWithoutAssets = rootFolder.Substring(7, rootFolder.Length - 7);
            return result;
        }

        public static string GetRootFolder(ISettingsWindowProperties windowProperties)
        {
            string rootFolder = EditorUtilities.FindFolder(windowProperties.FolderName, windowProperties.ParentFolder);
            if (rootFolder == null)
            {
                Debug.Log($"Folder Not Found: '{windowProperties.ParentFolder}/{windowProperties.FolderName}'");
            }
            return rootFolder;
        }

        public static string GetRootFolder(ISettingsWindowProperties windowProperties, out string rootWithoutAssets)
        {
            string rootFolder = EditorUtilities.FindFolder(windowProperties.FolderName, windowProperties.ParentFolder);
            if (rootFolder == null)
            {
                throw new Exception($"Folder Not Found: '{windowProperties.ParentFolder}/{windowProperties.FolderName}'");
            }
            rootWithoutAssets = rootFolder.Substring(7, rootFolder.Length - 7);
            return rootFolder;
        }

        public static string GetOrCreateRootFolder(ISettingsWindowProperties windowProperties)
        {
            string parentFolder = windowProperties.ParentFolder;
            string folderName = windowProperties.FolderName;

            // Validate parent folder
            if (!AssetDatabase.IsValidFolder(parentFolder))
            {
                //throw new Exception($"Parent folder not found: '{parentFolder}'");
            }

            string fullPath = $"{parentFolder}/{folderName}";

            // Create folder if it does not exist
            if (!AssetDatabase.IsValidFolder(fullPath))
            {
                AssetDatabase.CreateFolder(parentFolder, folderName);
                AssetDatabase.Refresh();
            }

            return fullPath;
        }

    }
}
