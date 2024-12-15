 #if UNITY_EDITOR

// MIT LICENSE, Copyright DomNomNom 2023

using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.Animations;
using UnityEditor.Animations;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

public class FixShadersRightEye : EditorWindow
{
    private List<ShaderInfo> allShaders = new List<ShaderInfo>();
    private Vector2 scrollPos = Vector2.zero;
    private int progressID;

    [MenuItem("Tools/DomNomNom/FixShadersRightEye")]
    public static void ShowMyEditor() {
      // This method is called when the user selects the menu item in the Editor
      EditorWindow wnd = GetWindow<FixShadersRightEye>();
      wnd.titleContent = new GUIContent("FixShadersRightEye");
    }

    public class ShaderInfo {
        public ShaderInfo() {}
        public string displayName;
        public string path;
        public bool has_been_scanned = false;
        public bool too_complex = false;
        public bool fixable = false;
        public bool completion_marker0 = false;
        public bool completion_marker1 = false;
        public bool completion_marker2 = false;
        public bool completion_marker3 = false;
        public bool completion_marker4 = false;
        public static readonly int completion_marker_count = 5;
        public bool upgradeIntroducesErrors = false;
        public bool upgradeChangesSomething = false;
        public bool shouldProcess = false;

        public new string ToString() {
            string prefix = "";
            int completion_markers =
                (completion_marker0? 1 : 0) +
                (completion_marker1? 1 : 0) +
                (completion_marker2? 1 : 0) +
                (completion_marker3? 1 : 0) +
                (completion_marker4? 1 : 0);
            if (!has_been_scanned) {
                prefix = "scanning...";
            } else if (too_complex) {
                prefix = "too difficult";
            } else if (completion_markers == completion_marker_count) {
                prefix = "Done";
            } else if (!upgradeChangesSomething) {
                prefix = "no change";
            } else {
                prefix = $"{completion_markers}/{completion_marker_count}";
                // prefix = "needs manual work";
            }
            prefix = $"[{prefix}] ";
            return prefix + displayName;
        }
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // VisualElements objects can contain other VisualElement following a tree hierarchy.
        VisualElement label = new Label("Select Shaders to upgrade:");
        root.Add(label);

        var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
        scroll.name = "shaderToggles";
        root.Add(scroll);

        var button_row = new VisualElement();
        button_row.style.flexDirection = FlexDirection.Row;
        {
            Button refresh = new Button();
            refresh.name = refresh.text = "Refresh";
            refresh.RegisterCallback<ClickEvent>(RefreshClicked);
            button_row.Add(refresh);

            Button SelectNone = new Button();
            SelectNone.name = SelectNone.text = "Select None";
            SelectNone.RegisterCallback<ClickEvent>(SelectNoneClicked);
            button_row.Add(SelectNone);

            Button SelectFixable = new Button();
            SelectFixable.name = SelectFixable.text = "Select Fixable";
            SelectFixable.RegisterCallback<ClickEvent>(SelectFixableClicked);
            button_row.Add(SelectFixable);
        }
        root.Add(button_row);

        Button go = new Button();
        go.name = go.text = "Fix Shaders!";
        go.RegisterCallback<ClickEvent>(GoClicked);
        root.Add(go);

        Refresh();
    }

    private void SelectNoneClicked(ClickEvent _) {
        foreach (ShaderInfo s in allShaders) {
            s.shouldProcess = false;
        }
        AllShadersToGUI();
    }
    private void SelectFixableClicked(ClickEvent _) {
        foreach (ShaderInfo s in allShaders) {
            s.shouldProcess = s.fixable;
        }
        AllShadersToGUI();
    }
    private void RefreshClicked(ClickEvent _) {
        Refresh();
    }
    private void GoClicked(ClickEvent _) {
        Go();
        Refresh();
    }
    private void Refresh() {
        allShaders = findAllShaders(new DirectoryInfo("Assets"));
        AllShadersToGUI();
        var _ = Task.Run(ScanShaders);
    }
    private void AllShadersToGUI() {
        var scroll = rootVisualElement.Q<ScrollView>("shaderToggles");
        while (allShaders.Count > scroll.childCount) {
            Toggle toggle = new Toggle();
            toggle.RegisterValueChangedCallback(OnToggleClicked);
            scroll.Add(toggle);
        }
        while (allShaders.Count < scroll.childCount) {
            scroll.Remove(scroll[scroll.childCount-1]);
        }
        // foreach (ShaderInfo s in allShaders) {
        for (int i=0; i<allShaders.Count; i++) {
            ShaderInfo s = allShaders[i];
            Toggle toggle = scroll[i] as Toggle;
            toggle.name = s.displayName;
            toggle.text = s.ToString();
            toggle.value = s.shouldProcess;
        }
    }
    private void OnToggleClicked(ChangeEvent<bool> e) {
        Toggle toggle = e.currentTarget as Toggle;
        int i = toggle.parent.IndexOf(toggle);
        allShaders[i].shouldProcess = toggle.value;
    }


    public void OnInspectorUpdate() {
        // This will only get called 10 times per second.
        // This is so we pick up updates from ScanShaders which is in a different thread.
        AllShadersToGUI();
        // Repaint();
    }

    public const string upgrade_comment = " // inserted by FixShadersRightEye.cs";
    private Regex pragma_vertex_finder = new Regex(@"^\s*#pragma\s+vertex\s+(.*)\b", RegexOptions.Compiled);
    private Regex pragma_fragment_finder = new Regex(@"^\s*#pragma\s+fragment\s+(.*)\b", RegexOptions.Compiled);
    private Regex return_finder = new Regex(@"^\s*return\s+(.*);", RegexOptions.Compiled);
    private Regex indent_finder = new Regex(@"^(\s*)", RegexOptions.Compiled);
    private Regex macro_finder = new Regex(@"^\s*([_A-Z]+)\s*\(", RegexOptions.Compiled);
    private Regex grab_pass_finder = new Regex(@"^\s*GrabPass\b", RegexOptions.Compiled);
    private Regex grab_pass_name_finder = new Regex("\"(.*?)\"", RegexOptions.Compiled);
    private string getIndent(string s) {
        return indent_finder.Matches(s)[0].Groups[1].Value;
    }
    public string upgradeShader(string path) {
        List<string> lines = File.ReadLines(path).ToList();

        bool has_some_vertex_shader = false;

        // Scan for things that look like "#pragma vert"
        for (int i=0; i<lines.Count; ++i) {

            var vertex_matches = pragma_vertex_finder.Matches(lines[i]);
            if (vertex_matches.Count == 0) continue;
            string vertex_func_name = vertex_matches[0].Groups[1].Value;
            // Once we found it, scan below for that function.
            // note: we assume that the "#pragma" is above the function and struct declaration

            string v2f_struct_type = "";  // usually "v2f"
            string v2f_in_var_name = "";  // usually "v"
            string appdata_type = "";
            Regex v2f_func_finder = new Regex($@"^\s*(.*)\s+{vertex_func_name}\s*\((.+?)\s+(.+?)\b", RegexOptions.Compiled);
            int vert_func_i;
            for (vert_func_i=i; vert_func_i<lines.Count; vert_func_i++) {
                var v2f_func_matches = v2f_func_finder.Matches(lines[vert_func_i]);
                if (v2f_func_matches.Count == 0) continue;
                v2f_struct_type = v2f_func_matches[0].Groups[1].Value;
                appdata_type    = v2f_func_matches[0].Groups[2].Value;
                v2f_in_var_name = v2f_func_matches[0].Groups[3].Value;
                break;
            }
            if (v2f_struct_type == "") continue;  // couldn't find the vertex shader function. might be in a #include. too complex
            if (v2f_struct_type == "void") continue; // this seems to be a surface shader that we can't do much about.

            has_some_vertex_shader = true;

            // find the return statement
            int return_i;
            string v2f_out_var_name = ""; // usually "o"
            bool found_UNITY_SETUP_INSTANCE_ID = false;
            bool found_UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO = false;
            for (return_i=vert_func_i+1; return_i < lines.Count; ++return_i) {
                string line = lines[return_i];
                var return_matches = return_finder.Matches(line);
                if (line.Contains("UNITY_SETUP_INSTANCE_ID")) {
                    found_UNITY_SETUP_INSTANCE_ID = true;
                }
                if (line.Contains("UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO")) {
                    found_UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO = true;
                }
                if (return_matches.Count == 0) continue;
                v2f_out_var_name = return_matches[0].Groups[1].Value;
                break;
            }
            if (v2f_out_var_name == "") {
                Debug.LogError($"couldn't find return for vert function in shader: {path}");
                return null;
            }

            // search for the declaration of the return variable.
            // (usually on the first line of the vertex shader function)
            int v2f_declaration_i;
            Regex declaration_finder = new Regex($@"^\s*{v2f_struct_type}\s+{v2f_out_var_name}", RegexOptions.Compiled);
            for (v2f_declaration_i=vert_func_i; v2f_declaration_i<return_i; ++v2f_declaration_i){
                var declaration_matches = declaration_finder.Matches(lines[v2f_declaration_i]);
                if (declaration_matches.Count == 0) continue;
                break;
            }
            if (v2f_declaration_i == return_i) {
                Debug.LogError($"couldn't find declaration '{v2f_struct_type} {v2f_out_var_name}' in shader: {path}");
                return null;
            }
            if (lines[v2f_declaration_i].Contains("{")) {
                // struct initialization is too complex to auto port.
                return null;
            }
            // insert new code there.
            int insert_pos = v2f_declaration_i + 1;
            string indent = getIndent(lines[v2f_declaration_i]);
            if (!found_UNITY_SETUP_INSTANCE_ID) {
                lines.Insert(insert_pos, $"{indent}UNITY_SETUP_INSTANCE_ID({v2f_in_var_name}); {upgrade_comment}");
                insert_pos++;
            }
            if (!found_UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO) {
                lines.Insert(insert_pos, $"{indent}UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO({v2f_out_var_name}); {upgrade_comment}");
                insert_pos++;
            }


            // add UNITY_VERTEX_OUTPUT_STEREO to struct v2f
            int struct_v2f_i;
            Regex struct_v2f_finder = new Regex($@"^\s*struct\s+{v2f_struct_type}");
            for (struct_v2f_i = i; struct_v2f_i<lines.Count; ++struct_v2f_i) {
                if (struct_v2f_finder.Matches(lines[struct_v2f_i]).Count > 0) {
                    break;
                }
            }
            if (struct_v2f_i == lines.Count) {
                Debug.LogWarning($"couldn't find 'struct {v2f_struct_type}' in shader: {path}");
            } else {
                // note: we assume the '{' is on a different line to '}'
                int closing_brace_i;
                bool found_UNITY_VERTEX_OUTPUT_STEREO = false;
                for (closing_brace_i = struct_v2f_i; closing_brace_i<lines.Count; ++closing_brace_i) {
                    if (lines[closing_brace_i].Contains("UNITY_VERTEX_OUTPUT_STEREO")) found_UNITY_VERTEX_OUTPUT_STEREO = true;
                    if (lines[closing_brace_i].Contains("}")) break;
                }
                if (closing_brace_i==lines.Count) {
                    Debug.LogError($"couldn't find end of 'struct {v2f_struct_type}' in shader: {path}");
                } else if (!found_UNITY_VERTEX_OUTPUT_STEREO) {
                    insert_pos = closing_brace_i;
                    indent = getIndent(lines[insert_pos - 1]);
                    lines.Insert(insert_pos, $"{indent}UNITY_VERTEX_OUTPUT_STEREO {upgrade_comment}");
                }
            }

            // add UNITY_VERTEX_INPUT_INSTANCE_ID to appdata
            int struct_appdata_i;
            Regex struct_appdata_finder = new Regex($@"^\s*struct\s+{appdata_type}");
            for (struct_appdata_i = i; struct_appdata_i<lines.Count; ++struct_appdata_i) {
                if (struct_appdata_finder.Matches(lines[struct_appdata_i]).Count > 0) {
                    break;
                }
            }
            if (struct_appdata_i == lines.Count) {
                // might not be neccessary in appdata_full
                // Debug.LogWarning($"couldn't find 'struct {appdata_type}' in shader: {path}");
            } else {
                // note: we assume the '{' is on a different line to '}'
                int closing_brace_i;
                bool found_UNITY_VERTEX_INPUT_INSTANCE_ID = false;
                for (closing_brace_i = struct_appdata_i; closing_brace_i<lines.Count; ++closing_brace_i) {
                    if (lines[closing_brace_i].Contains("UNITY_VERTEX_INPUT_INSTANCE_ID")) found_UNITY_VERTEX_INPUT_INSTANCE_ID = true;
                    if (lines[closing_brace_i].Contains("}")) break;
                }
                if (closing_brace_i==lines.Count) {
                    Debug.LogError($"couldn't find end of 'struct {appdata_type}' in shader: {path}");
                } else if (!found_UNITY_VERTEX_INPUT_INSTANCE_ID) {
                    insert_pos = closing_brace_i;
                    indent = getIndent(lines[insert_pos - 1]);
                    lines.Insert(insert_pos, $"{indent}UNITY_VERTEX_INPUT_INSTANCE_ID {upgrade_comment}");
                }
            }
        }

        // Scan for things that look like "#pragma fragment"
        for (int i=0; i<lines.Count && has_some_vertex_shader; ++i) {
            var fragment_matches = pragma_fragment_finder.Matches(lines[i]);
            if (fragment_matches.Count == 0) continue;
            string fragment_func_name = fragment_matches[0].Groups[1].Value;
            // Once we found it, scan below for that function.
            // note: we assume that the "#pragma" is above the function and struct declaration

            string frag_return_type = "";  // usually "fixed4"
            string in_var_name = "";  // usually "v"
            string v2f_type = "";
            Regex func_finder = new Regex($@"^\s*(.*)\s+{fragment_func_name}\s*\((.+?)\s+(.+?)\b", RegexOptions.Compiled);
            int frag_func_i;
            for (frag_func_i=i; frag_func_i<lines.Count; frag_func_i++) {
                var func_matches = func_finder.Matches(lines[frag_func_i]);
                if (func_matches.Count == 0) continue;
                frag_return_type = func_matches[0].Groups[1].Value;
                v2f_type    = func_matches[0].Groups[2].Value;
                in_var_name = func_matches[0].Groups[3].Value;
                break;
            }
            if (in_var_name == "") continue;  // couldn't find the fragment shader function. might be in a #include. too complex

            // insert on the first line after the open brace.
            int open_brace_i;
            for (open_brace_i = frag_func_i; open_brace_i<lines.Count; ++open_brace_i) {
                if (lines[open_brace_i].Contains('{')) {
                    break;
                }
            }
            if (open_brace_i == lines.Count) {
                Debug.LogError($"couldn't find open brace for fragment function in shader: {path}");
                return null;
            }
            int insert_pos = open_brace_i + 1;

            // try search for existing tags among a macro block at the top.
            bool exists_already = false;
            for (int j=insert_pos; j<lines.Count; ++j) {
                if (lines[j].Contains("UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX")) {
                    exists_already = true;
                    break;
                }
                if (macro_finder.Matches(lines[j]).Count == 0) break;
            }
            if (exists_already) continue;

            string indent = getIndent(lines[insert_pos]);
            lines.Insert(insert_pos, $"{indent}UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX({in_var_name}); {upgrade_comment}");
        }

        // Scan for GrabPass{}
        List<string> grab_pass_names = new List<string>();
        int first_grab_line_i = 0;
        for (int i=0; i<lines.Count; ++i) {

            if (grab_pass_finder.Matches(lines[i]).Count == 0) continue;

            if (first_grab_line_i == 0) first_grab_line_i = i;
            // scan for a grab pass name
            string grab_pass_name = "_GrabTexture";
            for (int j=i; j<lines.Count; ++j) {
                var name_matches = grab_pass_name_finder.Matches(lines[j]);
                if (name_matches.Count > 0) {
                    string name = name_matches[0].Groups[1].Value;
                    if (name == "LightMode") continue; // HAAAAX: this filters out Tags { "LightMode"="Vertex" }
                    grab_pass_name = name;
                    break;
                }
                if (lines[j].Contains('}')) break; // end of grab pass definition block.
            }
            grab_pass_names.Add(grab_pass_name);
        }
        // Upgrade grab pass texture definitions
        for (int i=first_grab_line_i; grab_pass_names.Count > 0 && i<lines.Count; ++i) {
            Regex grab_pass_definition_finder = new Regex($@"\bsampler2D\s+(({System.String.Join(")|(", grab_pass_names)}))", RegexOptions.Compiled);
            var matches = grab_pass_definition_finder.Matches(lines[i]);
            if (matches.Count == 0) continue;
            var grab_pass_name = matches[0].Groups[1];
            lines[i] = $"{getIndent(lines[i])}UNITY_DECLARE_SCREENSPACE_TEXTURE({grab_pass_name}); {upgrade_comment}";
        }
        // upgrade grab pass texture uses
        Regex tex2D_name_finder = new Regex($@"\btex2D\s*\(\s*(({System.String.Join(")|(", grab_pass_names)}))\b", RegexOptions.Compiled);
        Regex tex2D_name_finder2 = new Regex($@"^\s*(({System.String.Join(")|(", grab_pass_names)}))\b", RegexOptions.Compiled);
        Regex tex2D_finder = new Regex($@"\btex2D\s*\(", RegexOptions.Compiled);
        for (int i=first_grab_line_i; grab_pass_names.Count > 0 && i<lines.Count; ++i) {
            var matches = tex2D_finder.Matches(lines[i]);
            if (matches.Count == 0) continue;
            if (
                (tex2D_name_finder.Matches(lines[i]).Count == 0) &&  // case for texture name being on the same line
                (tex2D_name_finder2.Matches(lines[i+1]).Count == 0)  // case for texture name being on the next line
            ) continue;  // not sampling the right texture
            lines[i] = $"{lines[i].Replace("tex2D", "UNITY_SAMPLE_SCREENSPACE_TEXTURE")} {upgrade_comment}";
        }



        string endl = "\r\n";
        return System.String.Join(endl, lines) + endl;
    }

    public List<ShaderInfo> findAllShaders(DirectoryInfo dir) {
        List<ShaderInfo> allShaders = new List<ShaderInfo>();
        foreach (DirectoryInfo d2 in dir.GetDirectories()) {
            allShaders.AddRange(findAllShaders(d2));
        }
        FileInfo[] files = dir.GetFiles("*.shader");
        foreach (FileInfo file in files) {
            ShaderInfo info = new ShaderInfo();
            info.path = ("" + file);
            info.displayName = info.path;
            if (info.displayName.Contains("Assets")) {
                info.displayName = info.displayName.Split("Assets")[1];
                info.displayName = info.displayName.Replace("\\", "/");
                info.displayName = info.displayName.Trim('/');
            }
            allShaders.Add(info);
        }
        return allShaders;

    }

    public async void ScanShaders() {
        int progressID = Progress.Start("Scanning shaders");
        Regex completion_finder = new Regex(@"\b(UNITY_VERTEX_INPUT_INSTANCE_ID)|(UNITY_VERTEX_OUTPUT_STEREO)|(UNITY_SETUP_INSTANCE_ID)|(UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO)|(UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX)\b", RegexOptions.Compiled);
        for (int i=0; i< allShaders.Count; ++i) {
            ShaderInfo s = allShaders[i];
            if (s.has_been_scanned) continue;
            Progress.Report(progressID, i+1, allShaders.Count, "Scanning " + s.displayName);

            try {
                var lines = File.ReadLines(s.path);

                // special case for a shader that does too much complex things and breaks.
                if (lines.Count() > 1000 && s.path.Contains("DopeShader")) {
                    s.has_been_scanned = true;
                    s.too_complex = true;
                    continue;
                }

                foreach (string line in lines) {
                    if (completion_finder.Matches(line).Count > 0) {
                        if (line.Contains("UNITY_VERTEX_INPUT_INSTANCE_ID")) s.completion_marker0 = true;
                        if (line.Contains("UNITY_VERTEX_OUTPUT_STEREO")) s.completion_marker1 = true;
                        if (line.Contains("UNITY_SETUP_INSTANCE_ID")) s.completion_marker2 = true;
                        if (line.Contains("UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO")) s.completion_marker3 = true;
                        if (line.Contains("UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX")) s.completion_marker4 = true;
                    }
                }

                // Do a dry run of upgrading.
                string unmodifiedCode = File.ReadAllText(s.path);
                string upgradedCode = upgradeShader(s.path);
                s.upgradeChangesSomething = (upgradedCode != null) && (
                    Regex.Replace(unmodifiedCode, @"\r|\n", "") !=
                    Regex.Replace(upgradedCode, @"\r|\n", "")
                );
                if (upgradedCode == null) {
                    s.has_been_scanned = true;
                    s.too_complex = true;
                    continue;
                }
            } catch (System.Exception e) {
                Debug.LogError(e + " on shader: " + s.path);
            }

            if (s.upgradeChangesSomething) {
                s.fixable = true;
                s.shouldProcess = true;
            }
            s.has_been_scanned = true;
            await Task.Delay(5);
        }
        Progress.Remove(progressID);
    }


    public void Go() {
        int progressID = Progress.Start("Fixing shaders");

        for (int i=0; i<allShaders.Count; ++i) {
            ShaderInfo shader = allShaders[i];
            if (!shader.shouldProcess) continue;
            Progress.Report(progressID, i+1, allShaders.Count, "Fixing " + shader.displayName);

            Shader unmodified = ShaderUtil.CreateShaderAsset(File.ReadAllText(shader.path), /*compileInitialShaderVariants*/ false);
            string upgradedCode = upgradeShader(shader.path);
            if (upgradedCode == null) continue; // we expect an error message to be printed already
            Shader upgraded = ShaderUtil.CreateShaderAsset(upgradedCode, /*compileInitialShaderVariants*/ false);

            if (ShaderUtil.GetShaderMessageCount(upgraded) > ShaderUtil.GetShaderMessageCount(unmodified)) {
                Debug.LogError("Upgrading this shader would introduce more errors: " + shader.path);
                continue;
            }
            File.WriteAllText(shader.path, upgradedCode);
        }
        Progress.Remove(progressID);
    }

}
#else
 public class FixShadersRightEye{}
#endif
