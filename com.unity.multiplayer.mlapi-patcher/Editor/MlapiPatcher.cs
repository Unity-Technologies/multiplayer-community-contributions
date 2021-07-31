using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEngine;
using UnityEngine.Assertions;
using Object = UnityEngine.Object;

namespace MLAPI.Patcher.Editor
{
    public class MlapiPatcher : EditorWindow
    {
        const string k_DllMappingCachePath = "mlapi-patcher-dll-guids.temp.json";
        const string k_PatchStatePath = "mlapi-patcher-state.temp.json";

        // This is class Id for MonoScript * 100'000 https://docs.unity3d.com/Manual/ClassIDReference.html
        const string k_MissingScriptId = "11500000";

        static readonly Dictionary<string, string> k_APIChanges = new Dictionary<string, string>()
        {
            { "NetworkingManager", "NetworkManager" },
            { "NetworkedObject", "NetworkObject" },
            { "NetworkedBehaviour", "NetworkBehaviour" },
            { "NetworkedClient", "NetworkClient" },
            { "NetworkedPrefab", "NetworkPrefab" },
            { "NetworkedVar", "NetworkVariable" },
            { "NetworkedTransform", "NetworkTransform" },
            { "NetworkedAnimator", "NetworkAnimator" },
            { "NetworkedAnimatorEditor", "NetworkAnimatorEditor" },
            { "NetworkedNavMeshAgent", "NetworkNavMeshAgent" },
            { "SpawnManager", "NetworkSpawnManager" },
            { "BitStream", "NetworkBuffer" },
            { "PooledBitStream", "PooledNetworkBuffer" },
            { "BitSerializer", "NetworkSerializer" },
            { "BitReader", "NetworkReader" },
            { "BitWriter", "NetworkWriter" },
            { "PooledBitWriter", "PooledNetworkWriter" },
            { "PooledBitReader", "PooledNetworkReader" },
            { "NetEventType", "NetworkEventType" },
            { "ChannelType", "NetworkDelivery" },
            { "Channel", "NetworkChannel" },
            { "SendChannel", "SendNetworkChannel" },
            { "Transport", "NetworkTransport" },
            { "NetworkedDictionary", "NetworkDictionary" },
            { "NetworkedList", "NetworkList" },
            { "NetworkedSet", "NetworkSet" },
            { "MLAPIConstants", "NetworkConstants" },
            { "UnetTransport", "UNetTransport" },
            { "ServerRPC", "ServerRpc" },
            { "ClientRPC", "ClientRpc" },
        };

        static readonly List<string> OldMlapiUnityObjects = new List<string>()
        {
            "NetworkingManager",
            "NetworkedObject",
            "NetworkedTransform",
            "NetworkedAnimator",
            "NetworkedNavMeshAgent",
            "UnetTransport",
        };

        [MenuItem("Window/MLAPI Patcher")]
        public static void ShowWindow() => GetWindow(typeof(MlapiPatcher));

        bool? m_DllVersion = null;

        Object m_SourceVersionDirectory;

        void OnEnable()
        {
            titleContent.text = "MLAPI Patcher";
        }

        void OnGUI()
        {
            if (m_DllVersion == null)
            {
                GUILayout.Label("Are you using the installer or the source version of MLAPI?");

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Installer"))
                {
                    m_DllVersion = true;
                }

                if (GUILayout.Button("Source"))
                {
                    m_DllVersion = false;
                }

                GUILayout.EndHorizontal();
            }
            else
            {
                if (m_DllVersion.Value == false)
                {
                    GUILayout.Label("MLAPI Source Directory");
                    m_SourceVersionDirectory = EditorGUILayout.ObjectField(m_SourceVersionDirectory, typeof(Object), false);
                }

                if (GUILayout.Button("Update Script References"))
                {
                    ReplaceAllScriptReferences(m_DllVersion.Value);
                }

                if (GUILayout.Button("Replace Type Names (Optional)"))
                {
                    UpdateApiUsages();
                }
            }
        }

        private string FindMlapiDllPath()
        {
            var result = new List<string>();
            FindFilesOfTypes(Application.dataPath, new[] { "MLAPI.dll" }, result);
            if (result.Any())
            {
                Assert.IsTrue(result.Count == 1);
                return result.First();
            }

            return null;
        }

        /// <summary>
        /// References to a monobehaviour in a dll are stored as guid of the dll and fileID based on type.
        /// This creates a table from guid => type name.
        /// </summary>
        private Dictionary<string, string> BuildDllMapping()
        {
            var dllGuidToFileId = new Dictionary<string, string>();

            Assembly assembly = Assembly.LoadFrom(FindMlapiDllPath());

            foreach (Type t in assembly.GetTypes())
            {
                var fileId = ComputeGuid(t).ToString();
                if (dllGuidToFileId.ContainsKey(fileId))
                {
                    Debug.LogWarning($"duplicate guid: {fileId}, script name:{t.Name}");
                }
                else
                {
                    dllGuidToFileId[fileId] = t.Name;

                    // Debug.Log($"found {fileId}: {t.Name}");
                }
            }

            return dllGuidToFileId;
        }

        /// <summary>
        /// Builds a table which maps from 
        /// </summary>
        /// <returns></returns>
        private Dictionary<string, string> BuildSourceMapping()
        {
            Assert.IsNotNull(m_SourceVersionDirectory);

            var sourceMapping = new Dictionary<string, string>();

            var filePaths = new List<string>();
            var folderPath = AssetDatabase.GetAssetPath(m_SourceVersionDirectory);
            var fileNames = OldMlapiUnityObjects.Select(t => t + ".cs.meta").ToArray();

            FindFilesOfTypes(folderPath, fileNames, filePaths);

            Assert.IsTrue(fileNames.Length == OldMlapiUnityObjects.Count);

            foreach (var path in filePaths)
            {
                sourceMapping[ExtractGuidFromMetaFile(path)] = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
            }

            return sourceMapping;
        }

        Dictionary<string, string> BuildPackageGuidMapping()
        {
            var packageTypeToGuid = new Dictionary<string, string>();

            var metaTypes = new[] { ".cs.meta" };
            var filePaths = new List<string>();

            FindFilesOfTypes(GetMlapiPackageFolderPath(), metaTypes, filePaths);

            foreach (string path in filePaths)
            {
                packageTypeToGuid[Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path))] = ExtractGuidFromMetaFile(path);
            }

            return packageTypeToGuid;
        }

        string GetMlapiPackageFolderPath()
        {
            var assetPath = Application.dataPath;
            var parentDirectory = new DirectoryInfo(assetPath).Parent;
            Assert.IsNotNull(parentDirectory);

            var packageCacheFolderPath = Path.Combine(parentDirectory.FullName, Path.Combine(Path.Combine("Library", "PackageCache")));
            var directory = new DirectoryInfo(packageCacheFolderPath);
            var mlapiPackageFolder = directory.GetDirectories().First(t => t.Name.StartsWith("com.unity.multiplayer.mlapi@"));
            
            Assert.IsNotNull(mlapiPackageFolder);
            
            return mlapiPackageFolder.FullName;
        }

        /// <summary>
        /// Recursively finds all the files ending with the given types.
        /// </summary>
        /// <param name="path">The path to collect the files from. Can be a folder or a single file.</param>
        /// <param name="types">The file endings to collect.</param>
        /// <param name="results">The list to add the found results.</param>
        private void FindFilesOfTypes(string path, string[] types, List<string> results)
        {
            if (File.Exists(path))
            {
                results.Add(path);
            }
            else
            {
                if (!string.IsNullOrEmpty(path))
                {
                    foreach (string file in Directory.GetFiles(path))
                    {
                        foreach (string type in types)
                        {
                            if (file.EndsWith(type))
                            {
                                results.Add(file);
                                break;
                            }
                        }
                    }

                    foreach (string directory in Directory.GetDirectories(path))
                    {
                        FindFilesOfTypes(directory, types, results);
                    }
                }
            }
        }

        private string ExtractGuidFromMetaFile(string filePath)
        {
            using (StreamReader streamReader = new StreamReader(filePath))
            {
                while (!streamReader.EndOfStream)
                {
                    var line = streamReader.ReadLine();
                    if (line.StartsWith("guid:"))
                    {
                        return line.Substring(line.IndexOf(":") + 2);
                    }
                }
            }

            throw new InvalidOperationException($"guid not found in file: {filePath}");
        }

        private void UpdateApiUsages()
        {
            var results = new List<string>();
            FindFilesOfTypes(Application.dataPath, new[] { ".cs" }, results);
            
            Dictionary<Regex, string> replacements = new Dictionary<Regex, string>();
            foreach (var apiChange in k_APIChanges)
            {
                var regex = new Regex($"(?<prefix> |\\.|<|\\[|\\(|!){apiChange.Key}(?!(s.UNET))");
                var replacement = $"${{prefix}}{apiChange.Value}";
                replacements.Add(regex, replacement);
            }

            for (int i = 0; i < results.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Update type names", results[i], (float)i / results.Count);
                UpdateApiUsagesForFile(results[i], replacements);
            }

            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
        }

        private void UpdateApiUsagesForFile(string filePath, Dictionary<Regex, string> replacements)
        {
            string[] lines = File.ReadAllLines(filePath);

            bool replacedAny = false;
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var replacement in replacements)
                {
                    //var newLine = lines[i].Replace($" {apiChange.Key}", $" {apiChange.Value}");

                    var newLine = replacement.Key.Replace(lines[i], replacement.Value);
                    
                    if (newLine != lines[i])
                    {
                        replacedAny = true;
                        lines[i] = newLine;
                    }
                }
            }

            if (replacedAny)
            {
                Debug.Log($"Updated APIs in file {filePath}");
                File.WriteAllLines(filePath, lines);
            }
        }

        private void ReplaceAllScriptReferences(bool fromDllVersion)
        {
            Dictionary<string, string> initialMapping;
            if (fromDllVersion)
            {
                initialMapping = BuildDllMapping();
            }
            else
            {
                initialMapping = BuildSourceMapping();
            }

            var packageMapping = BuildPackageGuidMapping();

            var relevantObjectTypes = new string[3] { ".asset", ".prefab", ".unity" };
            var results = new List<string>();
            FindFilesOfTypes(Application.dataPath, relevantObjectTypes, results);

            for (int i = 0; i < results.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Update Script References", results[i], (float)i / results.Count);
                ReplaceScriptReferencesForFile(results[i], initialMapping, packageMapping, fromDllVersion);
            }

            File.Delete(Path.Combine(Application.dataPath, k_DllMappingCachePath));

            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
        }

        private void ReplaceScriptReferencesForFile(string filePath, Dictionary<string, string> initialMapping, Dictionary<string, string> packageMapping, bool fromDllVersion)
        {
            string[] lines = File.ReadAllLines(filePath);

            bool replacedAny = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("MonoBehaviour:"))
                {
                    while (!lines[i].TrimStart().StartsWith("m_Script:"))
                    {
                        i++;
                    }

                    if (ReplaceGuidsInLine(ref lines[i], initialMapping, packageMapping, fromDllVersion))
                    {
                        replacedAny = true;
                    }
                }
            }

            if (replacedAny)
            {
                File.WriteAllLines(filePath, lines);
            }
        }

        /// <summary>
        /// Replaces the guids in the given line string.
        /// </summary>
        /// <param name="line">The line.</param>
        /// <returns>True if a replacement was done else false/</returns>
        private bool ReplaceGuidsInLine(ref string line, Dictionary<string, string> initialMapping, Dictionary<string, string> packageMapping, bool fromDllVersion)
        {
            string fileId = ExtractFromLine(line, "fileID");
            string guid = ExtractFromLine(line, "guid");

            Assert.IsNotNull(guid);

            if (fromDllVersion)
            {
                if (fileId == null || fileId == k_MissingScriptId)
                {
                    return false;
                }
            }

            var key = fromDllVersion ? fileId : guid;

            if (initialMapping.TryGetValue(key, out string originalName))
            {
                var updatedName = UpdateTypeName(originalName);
                if (packageMapping.TryGetValue(updatedName, out string newValue))
                {
                    Debug.Log("Replace reference:" + originalName);

                    line = line.Replace(guid, newValue);
                    line = line.Replace(fileId, "11500000");
                    return true;
                }

                Debug.LogWarning($"Can't find guid of file: {originalName}");
            }

            return false;
        }

        private string UpdateTypeName(string oldTypeName)
        {
            if (k_APIChanges.TryGetValue(oldTypeName, out string newTypeName))
            {
                return newTypeName;
            }

            return oldTypeName;
        }

        private static string ExtractFromLine(string line, string identifier)
        {
            int start = line.IndexOf($"{identifier}:") + $"{identifier}: ".Length;
            int lenght = line.IndexOf(",", start) - start;
            return lenght > 0 ? line.Substring(start, lenght) : null;
        }

        private static int ComputeGuid(Type t) // TODO why does scriptable build pipeline not provide this
        {
            string hashGenerator = "s\0\0\0" + t.Namespace + t.Name;
            using (var md4 = MD4.Create())
            {
                byte[] hash = md4.ComputeHash(Encoding.UTF8.GetBytes(hashGenerator));
                return BitConverter.ToInt32(hash, 0);
            }
        }
    }
}
