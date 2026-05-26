using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gley.Common.Editor
{
    public class AsmdefUtils
    {
        [System.Serializable]
        private class AssemblyDefinition
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public string[] includePlatforms;
            public string[] excludePlatforms;
            public bool allowUnsafeCode;
            public bool overrideReferences;
            public string[] precompiledReferences;
            public bool autoReferenced;
            public string[] defineConstraints;
            public string[] versionDefines;
            public bool noEngineReferences;
        }

        public static void GenerateAsmdefFile(string path, string name)
        {
            string userAsmdefPath = $"{path}/{name}.asmdef";

            // Check if the user asmdef already exists
            if (File.Exists(userAsmdefPath))
            {
                //Debug.Log($"User ASMDEF file already exists at path: {userAsmdefPath}");
                return;
            }

            // Create the User folder if it doesn't exist
            string userFolderPath = Path.GetDirectoryName(userAsmdefPath);
            if (!Directory.Exists(userFolderPath))
            {
                Directory.CreateDirectory(userFolderPath);
            }

            // Create the user asmdef content
            AssemblyDefinition userAsmdef = new AssemblyDefinition
            {
                name = name,
                autoReferenced = true,
            };

            // Write the user asmdef file
            File.WriteAllText(userAsmdefPath, JsonUtility.ToJson(userAsmdef, true));
            AssetDatabase.Refresh();

            Debug.Log($"User ASMDEF file generated at path: {userAsmdefPath}");
        }



        public static void AddAsmdefReference(string asmdefToReference, string asmdefToSetTheReferenceInto)
        {
            // Check if the localization asmdef file exists
            if (!File.Exists(asmdefToSetTheReferenceInto))
            {
                Debug.LogError($"ASMDEF file not found at path: {asmdefToSetTheReferenceInto}");
                return;
            }

            // Read the existing localization asmdef file
            string asmdefContent = File.ReadAllText(asmdefToSetTheReferenceInto);

            // Deserialize the content into an AssemblyDefinition object
            AssemblyDefinition asmdef = JsonUtility.FromJson<AssemblyDefinition>(asmdefContent);

            // Check if the user asmdef reference already exists
            if (asmdef.references != null && System.Array.Exists(asmdef.references, reference => reference == asmdefToReference))
            {
                //Debug.Log($"Reference to '{asmdefToReference}' already exists in '{asmdefToSetTheReferenceInto}'");
                return;
            }

            // Add the user asmdef reference
            var referencesList = new System.Collections.Generic.List<string>(asmdef.references ?? new string[0]);
            referencesList.Add(asmdefToReference);
            asmdef.references = referencesList.ToArray();

            // Serialize the updated AssemblyDefinition object back to JSON
            string updatedContent = JsonUtility.ToJson(asmdef, true);

            // Write the updated content back to the localization asmdef file
            File.WriteAllText(asmdefToSetTheReferenceInto, updatedContent);
            AssetDatabase.Refresh();

            //Debug.Log($"Added reference to '{asmdefToReference}' in '{asmdefToSetTheReferenceInto}'");
        }



        public static string FindRuntimeAsmdefPath(string asmdefName)
        {
            // Find all assets with the .asmdef extension
            string[] asmdefGUIDs = AssetDatabase.FindAssets("t:asmdef");

            foreach (string guid in asmdefGUIDs)
            {
                // Get the asset path from the GUID
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                // Check if the asset name matches the desired asmdef name
                if (Path.GetFileNameWithoutExtension(assetPath) == asmdefName)
                {
                    return assetPath; // Return the relative path
                }
            }

            // If not found, return null or handle the error
            return null;
        }
    }
}