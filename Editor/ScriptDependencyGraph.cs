// ScriptDependencyGraph.cs
//
// A GraphView-based node editor that visualizes which C# scripts in your
// Unity project reference each other.
//
// INSTALL: Place this file inside a folder named "Editor" anywhere under
// Assets, e.g. Assets/Editor/ScriptDependencyGraph.cs
//
// USE: Unity menu -> Window > Script Dependency Graph
//
// HOW IT WORKS (best-effort, not a full C# compiler):
//   1. Every .cs file under Assets is read and comments are stripped.
//   2. Declared type names (class/struct/interface/enum) are extracted
//      per file to build a lookup of "known project types".
//   3. Each file's remaining identifiers are intersected against that
//      lookup to find which other project types it references.
//   4. One node is created per file; one edge per file-to-file reference.
//
// LAYOUTS:
//   - Layered Layout (default): a hierarchical DAG layout. Scripts with no
//     dependencies (utilities/base classes) sit at the bottom; scripts that
//     depend on many others rise to the top. Crossing-reduction passes keep
//     it readable instead of spider-webby. Circular references are detected
//     and broken for layout purposes only (the edges are still drawn).
//   - Cluster by Folder: groups nodes into a grid per containing folder.
//   - Force Layout: classic spring simulation, kept as a fallback for
//     spotting isolated/disconnected clusters.
//
// Click a node to dim everything that isn't directly connected to it.
//
// LIMITATIONS: partial classes spread across multiple files collapse onto
// whichever file is scanned last for that type name; string/reflection-based
// references (e.g. GetComponent<T> via string, Type.GetType("Foo")) are not
// specially handled beyond plain textual matching. It's meant as a fast
// visual map, not a guaranteed-precise dependency graph.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DependencyGraphTool
{
    #region Data model

    /// <summary>Info about a single scanned .cs file.</summary>
    internal class ScriptInfo
    {
        public string Path;                       // project-relative path
        public string FileName;                   // e.g. "PlayerController.cs"
        public string Folder;                      // containing folder, used for coloring/clustering
        public List<string> DeclaredTypes = new List<string>();
        public HashSet<string> Dependencies = new HashSet<string>(); // other file names this file references
    }

    /// <summary>Scans the project and builds the dependency graph data.</summary>
    internal static class DependencyScanner
    {
        static readonly Regex TypeDeclRegex = new Regex(
            @"\b(?:class|struct|interface|enum)\s+([A-Za-z_]\w*)", RegexOptions.Compiled);

        static readonly Regex IdentifierRegex = new Regex(
            @"\b[A-Za-z_]\w*\b", RegexOptions.Compiled);

        static readonly Regex LineCommentRegex = new Regex(@"//.*?$", RegexOptions.Multiline);
        static readonly Regex BlockCommentRegex = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);
        static readonly Regex StringLiteralRegex = new Regex("\".*?(?<!\\\\)\"", RegexOptions.Compiled);

        public static List<ScriptInfo> Scan(string excludeContains)
        {
            string assetsPath = Application.dataPath;
            var files = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);

            var excludeTerms = string.IsNullOrWhiteSpace(excludeContains)
                ? new string[0]
                : excludeContains.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

            var infos = new List<ScriptInfo>();
            var rawTextByInfo = new Dictionary<ScriptInfo, string>();
            var typeToFile = new Dictionary<string, ScriptInfo>();

            foreach (var absPath in files)
            {
                string relPath = "Assets" + absPath.Substring(assetsPath.Length).Replace('\\', '/');

                if (excludeTerms.Any(term => relPath.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                string text;
                try { text = File.ReadAllText(absPath); }
                catch { continue; }

                string stripped = StripCommentsAndStrings(text);

                var info = new ScriptInfo
                {
                    Path = relPath,
                    FileName = Path.GetFileName(relPath),
                    Folder = Path.GetDirectoryName(relPath)?.Replace('\\', '/') ?? ""
                };

                foreach (Match m in TypeDeclRegex.Matches(stripped))
                {
                    string typeName = m.Groups[1].Value;
                    if (!info.DeclaredTypes.Contains(typeName))
                        info.DeclaredTypes.Add(typeName);
                    typeToFile[typeName] = info; // last-write-wins for partials
                }

                infos.Add(info);
                rawTextByInfo[info] = stripped;
            }

            var allTypeNames = new HashSet<string>(typeToFile.Keys);

            foreach (var info in infos)
            {
                var identifiers = new HashSet<string>();
                foreach (Match m in IdentifierRegex.Matches(rawTextByInfo[info]))
                    identifiers.Add(m.Value);

                foreach (var id in identifiers)
                {
                    if (info.DeclaredTypes.Contains(id)) continue;
                    if (!allTypeNames.Contains(id)) continue;

                    var targetInfo = typeToFile[id];
                    if (targetInfo == info) continue;

                    info.Dependencies.Add(targetInfo.FileName + "|" + targetInfo.Path);
                }
            }

            return infos;
        }

        static string StripCommentsAndStrings(string text)
        {
            text = BlockCommentRegex.Replace(text, " ");
            text = LineCommentRegex.Replace(text, " ");
            text = StringLiteralRegex.Replace(text, "\"\"");
            return text;
        }
    }

    #endregion

    #region Node view

    internal class ScriptNodeView : Node
    {
        public ScriptInfo Info;
        public Port InputPort;
        public Port OutputPort;

        public ScriptNodeView(ScriptInfo info)
        {
            Info = info;
            title = info.FileName.Replace(".cs", "");
            viewDataKey = info.Path;

            var subtitle = new Label(info.Folder);
            subtitle.style.fontSize = 9;
            subtitle.style.opacity = 0.6f;
            subtitle.style.marginLeft = 6;
            subtitle.style.marginBottom = 4;
            mainContainer.Add(subtitle);

            if (info.DeclaredTypes.Count > 1)
            {
                var typesLabel = new Label(string.Join(", ", info.DeclaredTypes));
                typesLabel.style.fontSize = 9;
                typesLabel.style.whiteSpace = WhiteSpace.Normal;
                typesLabel.style.marginLeft = 6;
                typesLabel.style.marginBottom = 4;
                typesLabel.style.maxWidth = 180;
                mainContainer.Add(typesLabel);
            }

            InputPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "used by";
            inputContainer.Add(InputPort);

            OutputPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            OutputPort.portName = "uses";
            outputContainer.Add(OutputPort);

            var color = FolderColor(info.Folder);
            titleContainer.style.backgroundColor = color;

            RefreshExpandedState();
            RefreshPorts();

            RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(info.Path);
                    if (asset != null) AssetDatabase.OpenAsset(asset);
                    evt.StopPropagation();
                }
            });
        }

        static Color FolderColor(string folder)
        {
            int hash = 0;
            unchecked { foreach (char c in folder) hash = hash * 31 + c; }
            float h = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(h, 0.45f, 0.35f);
        }
    }

    #endregion

    #region GraphView

    internal class ScriptDependencyGraphView : GraphView
    {
        public Dictionary<string, ScriptNodeView> NodesByPath = new Dictionary<string, ScriptNodeView>();
        public List<(ScriptNodeView from, ScriptNodeView to)> DependencyPairs = new List<(ScriptNodeView, ScriptNodeView)>();

        // from -> the things it depends on ("uses"); to -> the things that depend on it ("used by")
        public Dictionary<ScriptNodeView, List<ScriptNodeView>> Outgoing = new Dictionary<ScriptNodeView, List<ScriptNodeView>>();
        public Dictionary<ScriptNodeView, List<ScriptNodeView>> Incoming = new Dictionary<ScriptNodeView, List<ScriptNodeView>>();

        public ScriptDependencyGraphView()
        {
            SetupZoom(0.05f, 3f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            grid.StretchToParentSize();
            Insert(0, grid);

            style.flexGrow = 1;

            RegisterCallback<MouseUpEvent>(_ => HighlightSelection());
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            // Read-only viewer: no new connections allowed by the user.
            return new List<Port>();
        }

        public void Rebuild(List<ScriptInfo> infos)
        {
            DeleteElements(graphElements.ToList());
            NodesByPath.Clear();
            DependencyPairs.Clear();
            Outgoing.Clear();
            Incoming.Clear();

            foreach (var info in infos)
            {
                var node = new ScriptNodeView(info);
                AddElement(node);
                NodesByPath[info.Path] = node;
                Outgoing[node] = new List<ScriptNodeView>();
                Incoming[node] = new List<ScriptNodeView>();
            }

            foreach (var info in infos)
            {
                if (!NodesByPath.TryGetValue(info.Path, out var fromNode)) continue;

                foreach (var depKey in info.Dependencies)
                {
                    string depPath = depKey.Substring(depKey.IndexOf('|') + 1);
                    if (!NodesByPath.TryGetValue(depPath, out var toNode)) continue;

                    var edge = fromNode.OutputPort.ConnectTo(toNode.InputPort);
                    edge.pickingMode = PickingMode.Ignore; // read-only: don't intercept clicks meant for dragging
                    AddElement(edge);
                    DependencyPairs.Add((fromNode, toNode));
                    Outgoing[fromNode].Add(toNode);
                    Incoming[toNode].Add(fromNode);
                }
            }

            ApplyLayeredLayout();
            ResetHighlight();
        }

        // ---------------------------------------------------------------
        // Layered (hierarchical DAG) layout — the recommended default.
        // Nodes with no outgoing dependencies (leaves/utilities) sit at the
        // bottom; nodes that depend on many others rise toward the top.
        // A few barycenter sweeps reorder each row to reduce crossings.
        // ---------------------------------------------------------------
        public void ApplyLayeredLayout()
        {
            var nodes = NodesByPath.Values.ToList();
            if (nodes.Count == 0) return;

            var layer = new Dictionary<ScriptNodeView, int>();
            var visiting = new HashSet<ScriptNodeView>();

            int ComputeLayer(ScriptNodeView node)
            {
                if (layer.TryGetValue(node, out int cached)) return cached;
                if (visiting.Contains(node)) return 0; // cycle: treat as a leaf here to break it

                visiting.Add(node);
                int maxChild = -1;
                foreach (var dep in Outgoing[node])
                {
                    int childLayer = ComputeLayer(dep);
                    if (childLayer > maxChild) maxChild = childLayer;
                }
                visiting.Remove(node);

                int result = maxChild + 1;
                layer[node] = result;
                return result;
            }

            foreach (var node in nodes) ComputeLayer(node);

            int maxLayer = layer.Values.Max();
            var layers = new List<List<ScriptNodeView>>();
            for (int i = 0; i <= maxLayer; i++) layers.Add(new List<ScriptNodeView>());
            foreach (var node in nodes) layers[layer[node]].Add(node);
            foreach (var l in layers) l.Sort((a, b) => string.Compare(a.title, b.title, StringComparison.Ordinal));

            // Barycenter crossing-reduction: alternate sweeps referencing
            // whichever neighbor rows are currently positioned.
            var xPos = new Dictionary<ScriptNodeView, float>();
            foreach (var l in layers)
                for (int i = 0; i < l.Count; i++) xPos[l[i]] = i;

            for (int pass = 0; pass < 6; pass++)
            {
                bool topDown = pass % 2 == 0;
                var rowOrder = topDown ? Enumerable.Range(0, layers.Count) : Enumerable.Range(0, layers.Count).Reverse();

                foreach (int li in rowOrder)
                {
                    var row = layers[li];
                    if (row.Count <= 1) continue;

                    var barycenter = new Dictionary<ScriptNodeView, float>();
                    foreach (var node in row)
                    {
                        var neighborPositions = new List<float>();
                        foreach (var dep in Outgoing[node]) neighborPositions.Add(xPos[dep]);
                        foreach (var dep in Incoming[node]) neighborPositions.Add(xPos[dep]);
                        barycenter[node] = neighborPositions.Count > 0 ? neighborPositions.Average() : xPos[node];
                    }

                    var sorted = row.OrderBy(n => barycenter[n]).ToList();
                    for (int i = 0; i < sorted.Count; i++) xPos[sorted[i]] = i;
                    layers[li] = sorted;
                }
            }

            const float horizontalSpacing = 220f;
            const float verticalSpacing = 170f;

            for (int li = 0; li <= maxLayer; li++)
            {
                var row = layers[li];
                float totalWidth = (row.Count - 1) * horizontalSpacing;
                float y = (maxLayer - li) * verticalSpacing + 60f; // leaves (layer 0) at the bottom

                for (int i = 0; i < row.Count; i++)
                {
                    float x = i * horizontalSpacing - totalWidth / 2f + 800f;
                    var rect = row[i].GetPosition();
                    rect.position = new Vector2(x, y);
                    row[i].SetPosition(rect);
                }
            }
        }

        // ---------------------------------------------------------------
        // Groups nodes into a grid per containing folder. Good for seeing
        // module boundaries rather than dependency depth.
        // ---------------------------------------------------------------
        public void ApplyFolderClusterLayout()
        {
            var groups = NodesByPath.Values
                .GroupBy(n => n.Info.Folder)
                .OrderBy(g => g.Key)
                .ToList();

            const float cellSize = 200f;
            const float clusterPadding = 90f;

            int clusterCols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(groups.Count)));
            float currentX = 0f, currentY = 0f;
            float rowMaxHeight = 0f;
            int colIndex = 0;

            foreach (var group in groups)
            {
                var groupNodes = group.OrderBy(n => n.title).ToList();
                int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(groupNodes.Count)));
                int rows = Mathf.CeilToInt(groupNodes.Count / (float)cols);
                float clusterWidth = cols * cellSize;
                float clusterHeight = rows * cellSize;

                for (int i = 0; i < groupNodes.Count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    var rect = groupNodes[i].GetPosition();
                    rect.position = new Vector2(currentX + col * cellSize, currentY + row * cellSize);
                    groupNodes[i].SetPosition(rect);
                }

                rowMaxHeight = Mathf.Max(rowMaxHeight, clusterHeight);
                colIndex++;
                currentX += clusterWidth + clusterPadding;

                if (colIndex >= clusterCols)
                {
                    colIndex = 0;
                    currentX = 0f;
                    currentY += rowMaxHeight + clusterPadding;
                    rowMaxHeight = 0f;
                }
            }
        }

        // ---------------------------------------------------------------
        // Classic spring layout. Kept mainly for spotting disconnected
        // clusters — tends toward spider-webby on anything with hubs.
        // ---------------------------------------------------------------
        public void ApplyForceDirectedLayout(int iterations = 300)
        {
            var nodes = NodesByPath.Values.ToList();
            if (nodes.Count == 0) return;

            var pos = new Dictionary<ScriptNodeView, Vector2>();
            foreach (var node in nodes) pos[node] = node.GetPosition().position;

            float area = Mathf.Max(400f, nodes.Count) * 30000f;
            float k = Mathf.Sqrt(area / nodes.Count);
            float temperature = 400f;

            for (int iter = 0; iter < iterations; iter++)
            {
                var disp = new Dictionary<ScriptNodeView, Vector2>();
                foreach (var node in nodes) disp[node] = Vector2.zero;

                for (int i = 0; i < nodes.Count; i++)
                {
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        Vector2 delta = pos[nodes[i]] - pos[nodes[j]];
                        float dist = Mathf.Max(delta.magnitude, 0.01f);
                        Vector2 force = (delta / dist) * (k * k / dist);
                        disp[nodes[i]] += force;
                        disp[nodes[j]] -= force;
                    }
                }

                foreach (var (from, to) in DependencyPairs)
                {
                    Vector2 delta = pos[from] - pos[to];
                    float dist = Mathf.Max(delta.magnitude, 0.01f);
                    Vector2 force = (delta / dist) * (dist * dist / k);
                    disp[from] -= force;
                    disp[to] += force;
                }

                foreach (var node in nodes)
                {
                    Vector2 d = disp[node];
                    float dist = Mathf.Max(d.magnitude, 0.01f);
                    Vector2 limited = (d / dist) * Mathf.Min(dist, temperature);
                    pos[node] += limited;
                }

                temperature *= 0.97f;
            }

            float minX = pos.Values.Min(p => p.x);
            float minY = pos.Values.Min(p => p.y);

            foreach (var node in nodes)
            {
                var rect = node.GetPosition();
                rect.position = pos[node] - new Vector2(minX, minY) + new Vector2(50, 50);
                node.SetPosition(rect);
            }
        }

        // ---------------------------------------------------------------
        // Dims everything not directly connected to the current selection.
        // ---------------------------------------------------------------
        public void HighlightSelection()
        {
            var selectedNodes = selection.OfType<ScriptNodeView>().ToList();

            if (selectedNodes.Count == 0)
            {
                ResetHighlight();
                return;
            }

            var relevant = new HashSet<ScriptNodeView>(selectedNodes);
            foreach (var sel in selectedNodes)
            {
                foreach (var dep in Outgoing[sel]) relevant.Add(dep);
                foreach (var dep in Incoming[sel]) relevant.Add(dep);
            }

            foreach (var node in NodesByPath.Values)
                node.style.opacity = relevant.Contains(node) ? 1f : 0.12f;

            foreach (var element in graphElements.ToList())
            {
                if (element is Edge edge)
                {
                    var fromNode = edge.output?.node as ScriptNodeView;
                    var toNode = edge.input?.node as ScriptNodeView;
                    bool relevantEdge = (fromNode != null && selectedNodes.Contains(fromNode)) ||
                                        (toNode != null && selectedNodes.Contains(toNode));
                    edge.style.opacity = relevantEdge ? 1f : 0.05f;
                }
            }
        }

        public void ResetHighlight()
        {
            foreach (var node in NodesByPath.Values) node.style.opacity = 1f;
            foreach (var element in graphElements.ToList())
                if (element is Edge edge) edge.style.opacity = 1f;
        }
    }

    #endregion

    #region Editor window

    public class ScriptDependencyGraphWindow : EditorWindow
    {
        ScriptDependencyGraphView _graphView;
        MiniMap _miniMap;
        ToolbarSearchField _searchField;
        Label _statsLabel;
        string _excludeFilter = "Editor,ThirdParty,Plugins";

        [MenuItem("Window/Script Dependency Graph")]
        public static void Open()
        {
            var window = GetWindow<ScriptDependencyGraphWindow>();
            window.titleContent = new GUIContent("Script Dependencies");
            window.minSize = new Vector2(600, 400);
        }

        void OnEnable()
        {
            ConstructGraphView();
            ConstructToolbar();
            Rescan();
        }

        void ConstructGraphView()
        {
            _graphView = new ScriptDependencyGraphView { name = "Script Dependency Graph" };
            _graphView.StretchToParentSize();
            rootVisualElement.Add(_graphView);

            _miniMap = new MiniMap { anchored = true };
            _miniMap.SetPosition(new Rect(10, 30, 200, 140));
            _graphView.Add(_miniMap);
        }

        void ConstructToolbar()
        {
            var toolbar = new Toolbar();

            toolbar.Add(new ToolbarButton(Rescan) { text = "Rescan" });
            toolbar.Add(new ToolbarSpacer());

            toolbar.Add(new ToolbarButton(() => { _graphView.ApplyLayeredLayout(); _graphView.FrameAll(); }) { text = "Layered Layout" });
            toolbar.Add(new ToolbarButton(() => { _graphView.ApplyFolderClusterLayout(); _graphView.FrameAll(); }) { text = "Cluster by Folder" });
            toolbar.Add(new ToolbarButton(() => { _graphView.ApplyForceDirectedLayout(); _graphView.FrameAll(); }) { text = "Force Layout" });
            toolbar.Add(new ToolbarButton(() => _graphView.FrameAll()) { text = "Frame All" });

            toolbar.Add(new ToolbarSpacer());

            var excludeLabel = new Label(" exclude: ");
            excludeLabel.style.marginLeft = 8;
            excludeLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(excludeLabel);

            var excludeTextField = new TextField { value = _excludeFilter };
            excludeTextField.style.width = 200;
            excludeTextField.RegisterValueChangedCallback(evt => _excludeFilter = evt.newValue);
            toolbar.Add(excludeTextField);

            toolbar.Add(new ToolbarSpacer());

            var searchLabel = new Label(" find: ");
            searchLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(searchLabel);

            _searchField = new ToolbarSearchField();
            _searchField.style.width = 160;
            _searchField.RegisterValueChangedCallback(evt => FocusMatchingNode(evt.newValue));
            toolbar.Add(_searchField);

            _statsLabel = new Label();
            _statsLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _statsLabel.style.flexGrow = 1;
            _statsLabel.style.marginRight = 8;
            toolbar.Add(_statsLabel);

            rootVisualElement.Add(toolbar);
        }

        void Rescan()
        {
            EditorUtility.DisplayProgressBar("Script Dependency Graph", "Scanning scripts...", 0.5f);
            try
            {
                var infos = DependencyScanner.Scan(_excludeFilter);
                _graphView.Rebuild(infos);

                int edgeCount = infos.Sum(i => i.Dependencies.Count);
                _statsLabel.text = $"{infos.Count} scripts, {edgeCount} references";

                EditorApplication.delayCall += () => _graphView.FrameAll();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        void FocusMatchingNode(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;

            _graphView.ClearSelection();
            var matches = _graphView.NodesByPath.Values
                .Where(n => n.title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            foreach (var m in matches) _graphView.AddToSelection(m);
            if (matches.Count > 0) _graphView.FrameSelection();
            _graphView.HighlightSelection();
        }
    }

    #endregion
}
