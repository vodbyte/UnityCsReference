// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditorInternal;
using UnityEngine.UIElements;

using UnityEditor.Connect;
using UnityEditor.StyleSheets;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("com.unity.quicksearch.tests")]

namespace UnityEditor.Search
{

    /// <summary>
    /// This utility class mainly contains proxy to internal API that are shared between the version in trunk and the package version.
    /// </summary>
    static class Utils
    {

        internal static readonly bool isDeveloperBuild = false;

        private static UnityEngine.Object[] s_LastDraggedObjects;


        static Utils()
        {
            isDeveloperBuild = Unsupported.IsSourceBuild();
        }

        internal static void OpenInBrowser(string baseUrl, List<Tuple<string, string>> query = null)
        {
            var url = baseUrl;

            if (query != null)
            {
                url += "?";
                for (var i = 0; i < query.Count; ++i)
                {
                    var item = query[i];
                    url += item.Item1 + "=" + item.Item2;
                    if (i < query.Count - 1)
                    {
                        url += "&";
                    }
                }
            }

            var uri = new Uri(url);
            Process.Start(uri.AbsoluteUri);
        }

        internal static SettingsProvider[] FetchSettingsProviders()
        {
            return SettingsService.FetchSettingsProviders();
        }

        internal static string GetNameFromPath(string path)
        {
            var lastSep = path.LastIndexOf('/');
            if (lastSep == -1)
                return path;

            return path.Substring(lastSep + 1);
        }

        internal static Hash128 GetSourceAssetFileHash(string guid)
        {
            return AssetDatabase.GetSourceAssetFileHash(guid);
        }

        internal static Texture2D GetAssetThumbnailFromPath(string path)
        {
            var thumbnail = GetAssetPreviewFromGUID(AssetDatabase.AssetPathToGUID(path));
            if (thumbnail)
                return thumbnail;
            thumbnail = AssetDatabase.GetCachedIcon(path) as Texture2D;
            return thumbnail ?? InternalEditorUtility.FindIconForFile(path);
        }

        private static Texture2D GetAssetPreviewFromGUID(string guid)
        {
            return AssetPreview.GetAssetPreviewFromGUID(guid);
        }

        internal static Texture2D GetAssetPreviewFromPath(string path, FetchPreviewOptions previewOptions)
        {
            return GetAssetPreviewFromPath(path, new Vector2(128, 128), previewOptions);
        }

        internal static Texture2D GetAssetPreviewFromPath(string path, Vector2 previewSize, FetchPreviewOptions previewOptions)
        {
            var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (assetType == typeof(SceneAsset))
                return AssetDatabase.GetCachedIcon(path) as Texture2D;

            //UnityEngine.Debug.Log($"Generate preview for {path}, {previewSize}, {previewOptions}");

            if (previewOptions.HasFlag(FetchPreviewOptions.Normal))
            {
                if (assetType == typeof(AudioClip))
                    return GetAssetThumbnailFromPath(path);

                var fi = new FileInfo(path);
                if (!fi.Exists)
                    return null;
                if (fi.Length > 16 * 1024 * 1024)
                    return GetAssetThumbnailFromPath(path);
            }

            if (!typeof(Texture).IsAssignableFrom(assetType))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex)
                    return tex;
            }

            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null)
                return null;

            if (previewOptions.HasFlag(FetchPreviewOptions.Large))
            {
                var tex = AssetPreviewUpdater.CreatePreview(obj, null, path, (int)previewSize.x, (int)previewSize.y);
                if (tex)
                    return tex;
            }

            return GetAssetPreview(obj, previewOptions) ?? AssetDatabase.GetCachedIcon(path) as Texture2D;
        }

        internal static int GetMainAssetInstanceID(string assetPath)
        {
            return AssetDatabase.GetMainAssetInstanceID(assetPath);
        }

        internal static Texture2D GetAssetPreview(UnityEngine.Object obj, FetchPreviewOptions previewOptions)
        {
            var preview = AssetPreview.GetAssetPreview(obj);
            if (preview == null || previewOptions.HasFlag(FetchPreviewOptions.Large))
            {
                var largePreview = AssetPreview.GetMiniThumbnail(obj);
                if (preview == null || (largePreview != null && largePreview.width > preview.width))
                    preview = largePreview;
            }
            return preview;
        }

        internal static bool IsEditorValid(Editor e)
        {
            return e && e.serializedObject != null && e.serializedObject.isValid;
        }

        internal static int Wrap(int index, int n)
        {
            return ((index % n) + n) % n;
        }

        internal static void SelectObject(UnityEngine.Object obj, bool ping = false)
        {
            if (!obj)
                return;
            Selection.activeObject = obj;
            if (ping)
            {
                EditorApplication.delayCall += () =>
                {
                    EditorWindow.FocusWindowIfItsOpen(GetProjectBrowserWindowType());
                    EditorApplication.delayCall += () => EditorGUIUtility.PingObject(obj);
                };
            }
        }

        internal static UnityEngine.Object SelectAssetFromPath(string path, bool ping = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            SelectObject(asset, ping);
            return asset;
        }

        internal static void FrameAssetFromPath(string path)
        {
            var asset = SelectAssetFromPath(path);
            if (asset != null)
            {
                EditorApplication.delayCall += () =>
                {
                    EditorWindow.FocusWindowIfItsOpen(GetProjectBrowserWindowType());
                    EditorApplication.delayCall += () => EditorGUIUtility.PingObject(asset);
                };
            }
            else
            {
                EditorUtility.RevealInFinder(path);
            }
        }

        internal static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        internal static void GetMenuItemDefaultShortcuts(List<string> outItemNames, List<string> outItemDefaultShortcuts)
        {
            Menu.GetMenuItemDefaultShortcuts(outItemNames, outItemDefaultShortcuts);
        }

        internal static string FormatProviderList(IEnumerable<SearchProvider> providers, bool fullTimingInfo = false, bool showFetchTime = true)
        {
            return string.Join(fullTimingInfo ? "\r\n" : ", ", providers.Select(p =>
            {
                var fetchTime = p.fetchTime;
                if (fullTimingInfo)
                    return $"{p.name} ({fetchTime:0.#} ms, Enable: {p.enableTime:0.#} ms, Init: {p.loadTime:0.#} ms)";

                var avgTimeLabel = String.Empty;
                if (showFetchTime && fetchTime > 9.99)
                    avgTimeLabel = $" ({fetchTime:#} ms)";
                return $"<b>{p.name}</b>{avgTimeLabel}";
            }));
        }

        internal static string FormatBytes(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return $"{Math.Sign(byteCount) * num} {suf[place]}";
        }

        internal static string ToGuid(string assetPath)
        {
            string metaFile = $"{assetPath}.meta";
            if (!File.Exists(metaFile))
                return null;

            string line;
            using (var file = new StreamReader(metaFile))
            {
                while ((line = file.ReadLine()) != null)
                {
                    if (!line.StartsWith("guid:", StringComparison.Ordinal))
                        continue;
                    return line.Substring(6);
                }
            }

            return null;
        }

        internal static Rect GetEditorMainWindowPos()
        {
            var windows = Resources.FindObjectsOfTypeAll<ContainerWindow>();
            foreach (var win in windows)
            {
                if (win.showMode == ShowMode.MainWindow)
                    return win.position;
            }

            return new Rect(0, 0, 800, 600);
        }

        internal static Rect GetCenteredWindowPosition(Rect parentWindowPosition, Vector2 size)
        {
            var pos = new Rect
            {
                x = 0, y = 0,
                width = Mathf.Min(size.x, parentWindowPosition.width * 0.90f),
                height = Mathf.Min(size.y, parentWindowPosition.height * 0.90f)
            };
            var w = (parentWindowPosition.width - pos.width) * 0.5f;
            var h = (parentWindowPosition.height - pos.height) * 0.5f;
            pos.x = parentWindowPosition.x + w;
            pos.y = parentWindowPosition.y + h;
            return pos;
        }

        internal static Type GetProjectBrowserWindowType()
        {
            return typeof(ProjectBrowser);
        }

        internal static Rect GetMainWindowCenteredPosition(Vector2 size)
        {
            var mainWindowRect = GetEditorMainWindowPos();
            return GetCenteredWindowPosition(mainWindowRect, size);
        }

        internal static void ShowDropDown(this EditorWindow window, Vector2 size)
        {
            window.maxSize = window.minSize = size;
            window.position = GetMainWindowCenteredPosition(size);
            window.ShowPopup();

            var parentView = window.m_Parent;
            parentView.AddToAuxWindowList();
            parentView.window.m_DontSaveToLayout = true;
        }

        internal static string JsonSerialize(object obj)
        {
            return Json.Serialize(obj);
        }

        internal static object JsonDeserialize(string json)
        {
            return Json.Deserialize(json);
        }

        internal static int GetNumCharactersThatFitWithinWidth(GUIStyle style, string text, float width)
        {
            return style.GetNumCharactersThatFitWithinWidth(text, width);
        }

        internal static string GetNextWord(string src, ref int index)
        {
            // Skip potential white space BEFORE the actual word we are extracting
            for (; index < src.Length; ++index)
            {
                if (!char.IsWhiteSpace(src[index]))
                {
                    break;
                }
            }

            var startIndex = index;
            for (; index < src.Length; ++index)
            {
                if (char.IsWhiteSpace(src[index]))
                {
                    break;
                }
            }

            return src.Substring(startIndex, index - startIndex);
        }

        internal static int LevenshteinDistance<T>(IEnumerable<T> lhs, IEnumerable<T> rhs) where T : System.IEquatable<T>
        {
            if (lhs == null) throw new System.ArgumentNullException("lhs");
            if (rhs == null) throw new System.ArgumentNullException("rhs");

            IList<T> first = lhs as IList<T> ?? new List<T>(lhs);
            IList<T> second = rhs as IList<T> ?? new List<T>(rhs);

            int n = first.Count, m = second.Count;
            if (n == 0) return m;
            if (m == 0) return n;

            int curRow = 0, nextRow = 1;
            int[][] rows = { new int[m + 1], new int[m + 1] };
            for (int j = 0; j <= m; ++j)
                rows[curRow][j] = j;

            for (int i = 1; i <= n; ++i)
            {
                rows[nextRow][0] = i;

                for (int j = 1; j <= m; ++j)
                {
                    int dist1 = rows[curRow][j] + 1;
                    int dist2 = rows[nextRow][j - 1] + 1;
                    int dist3 = rows[curRow][j - 1] +
                        (first[i - 1].Equals(second[j - 1]) ? 0 : 1);

                    rows[nextRow][j] = System.Math.Min(dist1, System.Math.Min(dist2, dist3));
                }
                if (curRow == 0)
                {
                    curRow = 1;
                    nextRow = 0;
                }
                else
                {
                    curRow = 0;
                    nextRow = 1;
                }
            }
            return rows[curRow][m];
        }

        internal static int LevenshteinDistance(string lhs, string rhs, bool caseSensitive = true)
        {
            if (!caseSensitive)
            {
                lhs = lhs.ToLower();
                rhs = rhs.ToLower();
            }
            char[] first = lhs.ToCharArray();
            char[] second = rhs.ToCharArray();
            return LevenshteinDistance(first, second);
        }

        internal static Texture2D GetThumbnailForGameObject(GameObject go)
        {
            var thumbnail = PrefabUtility.GetIconForGameObject(go);
            if (thumbnail)
                return thumbnail;
            return EditorGUIUtility.ObjectContent(go, go.GetType()).image as Texture2D;
        }

        internal static Texture2D FindTextureForType(Type type)
        {
            return EditorGUIUtility.FindTexture(type);
        }

        internal static Texture2D GetIconForObject(UnityEngine.Object obj)
        {
            return EditorGUIUtility.GetIconForObject(obj);
        }

        internal static void PingAsset(string assetPath)
        {
            EditorGUIUtility.PingObject(AssetDatabase.GetMainAssetInstanceID(assetPath));
        }

        internal static T ConvertValue<T>(string value)
        {
            var type = typeof(T);
            var converter = TypeDescriptor.GetConverter(type);
            if (converter.IsValid(value))
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                return (T)converter.ConvertFromString(null, CultureInfo.InvariantCulture, value);
            }
            return (T)Activator.CreateInstance(type);
        }

        internal static bool TryConvertValue<T>(string value, out T convertedValue)
        {
            var type = typeof(T);
            var converter = TypeDescriptor.GetConverter(type);
            try
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                convertedValue = (T)converter.ConvertFromString(null, CultureInfo.InvariantCulture, value);
                return true;
            }
            catch
            {
                convertedValue = default;
                return false;
            }
        }

        internal static void StartDrag(UnityEngine.Object[] objects, string label = null)
        {
            s_LastDraggedObjects = objects;
            if (s_LastDraggedObjects == null)
                return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = s_LastDraggedObjects;
            DragAndDrop.StartDrag(label);
        }

        internal static void StartDrag(UnityEngine.Object[] objects, string[] paths, string label = null)
        {
            s_LastDraggedObjects = objects;
            if (paths == null || paths.Length == 0)
                return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = s_LastDraggedObjects;
            DragAndDrop.paths = paths;
            DragAndDrop.StartDrag(label);
        }

        internal static Type GetTypeFromName(string typeName)
        {
            return TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().FirstOrDefault(t => t.Name == typeName) ?? typeof(UnityEngine.Object);
        }

        internal static string StripHTML(string input)
        {
            return Regex.Replace(input, "<.*?>", String.Empty);
        }

        internal static UnityEngine.Object ToObject(SearchItem item, Type filterType)
        {
            if (item == null || item.provider == null)
                return null;
            return item.provider.toObject?.Invoke(item, filterType);
        }

        internal static bool IsFocusedWindowTypeName(string focusWindowName)
        {
            return EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().ToString().EndsWith("." + focusWindowName);
        }

        internal static string CleanString(string s)
        {
            var sb = s.ToCharArray();
            for (int c = 0; c < s.Length; ++c)
            {
                var ch = s[c];
                if (ch == '_' || ch == '.' || ch == '-' || ch == '/')
                    sb[c] = ' ';
            }
            return new string(sb).ToLowerInvariant();
        }

        internal static string CleanPath(string path)
        {
            return path.Replace("\\", "/");
        }

        internal static bool IsPathUnderProject(string path)
        {
            if (!Path.IsPathRooted(path))
            {
                path = new FileInfo(path).FullName;
            }

            path = CleanPath(path);
            return Application.dataPath == path || path.StartsWith(Application.dataPath + "/");
        }

        internal static string GetPathUnderProject(string path)
        {
            var cleanPath = CleanPath(path);
            if (!Path.IsPathRooted(cleanPath) || !path.StartsWith(Application.dataPath))
            {
                return cleanPath;
            }

            return cleanPath.Substring(Application.dataPath.Length - 6);
        }

        internal static Texture2D GetSceneObjectPreview(GameObject obj, Vector2 previewSize, FetchPreviewOptions options, Texture2D defaultThumbnail)
        {
            var sr = obj.GetComponent<SpriteRenderer>();
            if (sr && sr.sprite && sr.sprite.texture)
                return sr.sprite.texture;


            if (!options.HasFlag(FetchPreviewOptions.Large))
            {
                var preview = AssetPreview.GetAssetPreview(obj);
                if (preview)
                    return preview;
            }

            var assetPath = SearchUtils.GetHierarchyAssetPath(obj, true);
            if (string.IsNullOrEmpty(assetPath))
                return AssetPreview.GetAssetPreview(obj) ?? defaultThumbnail;
            return GetAssetPreviewFromPath(assetPath, previewSize, options);
        }

        internal static bool TryGetNumber(object value, out double number)
        {
            if (value is sbyte
                || value is byte
                || value is short
                || value is ushort
                || value is int
                || value is uint
                || value is long
                || value is ulong
                || value is float
                || value is double
                || value is decimal)
            {
                number = Convert.ToDouble(value);
                return true;
            }

            return double.TryParse(Convert.ToString(value), out number);
        }

        internal static bool IsRunningTests()
        {
            return !InternalEditorUtility.isHumanControllingUs || InternalEditorUtility.inBatchMode;
        }

        internal static bool IsMainProcess()
        {
            if (AssetDatabaseAPI.IsAssetImportWorkerProcess())
                return false;

            if (EditorUtility.isInSafeMode)
                return false;

            if (MPE.ProcessService.level != MPE.ProcessLevel.Main)
                return false;

            return true;
        }

        internal static event EditorApplication.CallbackFunction tick
        {
            add
            {
                EditorApplication.tick -= value;
                EditorApplication.tick += value;
            }
            remove
            {
                EditorApplication.tick -= value;
            }
        }

        internal static void CallDelayed(EditorApplication.CallbackFunction callback, double seconds = 0)
        {
            EditorApplication.CallDelayed(callback, seconds);
        }

        internal static void SetFirstInspectedEditor(Editor editor)
        {
            editor.firstInspectedEditor = true;
        }

        internal static GUIStyle FromUSS(string name)
        {
            return GUIStyleExtensions.FromUSS(GUIStyle.none, name);
        }

        internal static GUIStyle FromUSS(GUIStyle @base, string name)
        {
            return GUIStyleExtensions.FromUSS(@base, name);
        }

        internal static bool HasCurrentWindowKeyFocus()
        {
            return EditorGUIUtility.HasCurrentWindowKeyFocus();
        }

        internal static void AddStyleSheet(VisualElement rootVisualElement, string ussFileName)
        {
            rootVisualElement.AddStyleSheetPath($"StyleSheets/QuickSearch/{ussFileName}");
        }

        internal static InspectorWindowUtils.LayoutGroupChecker LayoutGroupChecker()
        {
            return new InspectorWindowUtils.LayoutGroupChecker();
        }



        internal static string GetConnectAccessToken()
        {
            return UnityConnect.instance.GetAccessToken();
        }

        internal static string GetPackagesKey()
        {
            return UnityConnect.instance.GetConfigurationURL(CloudConfigUrl.CloudPackagesKey);
        }

        internal static void OpenPackageManager(string packageName)
        {
            PackageManager.UI.PackageManagerWindow.SelectPackageAndFilterStatic(packageName, PackageManager.UI.Internal.PackageFilterTab.AssetStore);
        }

        internal static char FastToLower(char c)
        {
            // ASCII non-letter characters and
            // lower case letters.
            if (c < 'A' || (c > 'Z' && c <= 'z'))
            {
                return c;
            }

            if (c >= 'A' && c <= 'Z')
            {
                return (char)(c + 32);
            }

            return Char.ToLower(c, CultureInfo.InvariantCulture);
        }

        internal static string FastToLower(string str)
        {
            int length = str.Length;

            var chars = new char[length];

            for (int i = 0; i < length; ++i)
            {
                chars[i] = FastToLower(str[i]);
            }

            return new string(chars);
        }

        internal static string FormatCount(ulong count)
        {
            if (count < 1000U)
                return count.ToString(CultureInfo.InvariantCulture.NumberFormat);
            if (count < 1000000U)
                return (count / 1000U).ToString(CultureInfo.InvariantCulture.NumberFormat) + "k";
            if (count < 1000000000U)
                return (count / 1000000U).ToString(CultureInfo.InvariantCulture.NumberFormat) + "M";
            return (count / 1000000000U).ToString(CultureInfo.InvariantCulture.NumberFormat) + "G";
        }

        internal static bool TryAdd<K, V>(this Dictionary<K, V> dict, K key, V value)
        {
            if (!dict.ContainsKey(key))
            {
                dict.Add(key, value);
                return true;
            }

            return false;
        }

        internal static string[] GetAssetRootFolders()
        {
            return AssetDatabase.GetAssetRootFolders();
        }
    }
}