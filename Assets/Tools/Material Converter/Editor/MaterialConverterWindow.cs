using UnityEngine;
using UnityEditor;
using UnityEditor.Presets;
using System.Collections.Generic;
using System.Linq;

public class MaterialConverterWindow : EditorWindow
{
    private Shader currentShader;
    private Shader targetShader;
    private Material currentMaterial;
    private Material targetMaterial;
    private List<ShaderPropertyMapping> propertyMappings = new List<ShaderPropertyMapping>();
    private Vector2 scrollPosition;
    private bool propertiesLoaded = false;
    private Dictionary<string, Vector2> dropdownScrollPositions = new Dictionary<string, Vector2>();

    [MenuItem("Tools/Material Converter")]
    public static void ShowWindow()
    {
        GetWindow<MaterialConverterWindow>("Material Converter");
    }

    [MenuItem("Assets/Convert Materials", true)]
    private static bool ValidateConvertSelectedMaterials()
    {
        return Selection.objects.Any(obj => obj is Material);
    }

    [MenuItem("Assets/Convert Materials", false, 20)]
    private static void OpenConverterForSelectedMaterials()
    {
        MaterialConverterWindow window = GetWindow<MaterialConverterWindow>("Material Converter");
        window.LoadSelectedMaterialsFromProjectWindow();
        window.Focus();
    }

    private class ShaderPropertyMapping
    {
        public string SourcePropertyName;
        public string TargetPropertyName;
        public ShaderPropertyType PropertyType;
        public bool IsValidMapping;
        public string[] TargetPropertyOptions;
        public int SelectedTargetPropertyIndex = -1; // -1 means None (no mapping)
        public bool SearchExpanded = false;
        public string SearchString = "";
        public static Dictionary<ShaderPropertyType, bool> TypeFoldouts = new Dictionary<ShaderPropertyType, bool>();
    }

    private enum ShaderPropertyType
    {
        Float,
        Color,
        Vector,
        Texture,
        Unknown
    }

    private void LoadSelectedMaterialsFromProjectWindow()
    {
        List<Material> mats = new List<Material>();
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj is Material mat)
            {
                mats.Add(mat);
            }
        }

        if (mats.Count > 0)
        {
            // Set the first material as the current material and set its shader
            currentMaterial = mats[0];
            currentShader = currentMaterial.shader;
        }
    }

    private void OnGUI()
    {
        // Rex Tools small text
        GUI.color = new Color(1, 1, 1, 0.6f);
        EditorGUILayout.LabelField("Rex Tools", EditorStyles.miniLabel);
        GUI.color = Color.white;

        // Title and preset buttons on the same line
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Material Converter", EditorStyles.largeLabel, GUILayout.ExpandWidth(true));
        
        // Preset buttons as icons
        GUI.enabled = currentShader != null && targetShader != null;
        var saveContent = EditorGUIUtility.IconContent("SaveAs");
        saveContent.tooltip = "Save current shader mapping as preset";
        if (GUILayout.Button(saveContent, GUILayout.Width(32), GUILayout.Height(24)))
        {
            SavePreset();
        }
        GUI.enabled = true;
        
        var loadContent = EditorGUIUtility.IconContent("Folder Icon");
        loadContent.tooltip = "Load shader mapping from preset";
        if (GUILayout.Button(loadContent, GUILayout.Width(32), GUILayout.Height(24)))
        {
            LoadPreset();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);

        DrawShaderSelectionArea();
        
        EditorGUILayout.Space();
        
        GUI.enabled = (currentShader != null && targetShader != null) || 
                     (currentMaterial != null && targetMaterial != null);
        
        if (GUILayout.Button("Load Properties"))
        {
            LoadProperties();
            propertiesLoaded = true;
        }
        
        if (propertiesLoaded && GUILayout.Button("Smart Property Matching"))
        {
            ApplySmartPropertyMatching();
        }
        
        GUI.enabled = true;
        
        EditorGUILayout.Space();
        
        if (propertiesLoaded)
        {
            DrawPropertyMappings();
            EditorGUILayout.Space();
            DrawConverterButtons();
        }
    }

    private void DrawShaderSelectionArea()
    {
        EditorGUILayout.BeginVertical("box");
        
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        currentShader = (Shader)EditorGUILayout.ObjectField("Current Shader", currentShader, typeof(Shader), false);
        if (EditorGUI.EndChangeCheck() && currentMaterial != null)
        {
            if (currentShader != currentMaterial.shader)
            {
                currentMaterial = null;
            }
        }
        
        EditorGUI.BeginChangeCheck();
        currentMaterial = (Material)EditorGUILayout.ObjectField("Current Material", currentMaterial, typeof(Material), false);
        if (EditorGUI.EndChangeCheck() && currentMaterial != null)
        {
            currentShader = currentMaterial.shader;
        }
        
        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        targetShader = (Shader)EditorGUILayout.ObjectField("Target Shader", targetShader, typeof(Shader), false);
        if (EditorGUI.EndChangeCheck() && targetMaterial != null)
        {
            if (targetShader != targetMaterial.shader)
            {
                targetMaterial = null;
            }
        }
        
        EditorGUI.BeginChangeCheck();
        targetMaterial = (Material)EditorGUILayout.ObjectField("Target Material", targetMaterial, typeof(Material), false);
        if (EditorGUI.EndChangeCheck() && targetMaterial != null)
        {
            targetShader = targetMaterial.shader;
        }
        
        EditorGUILayout.EndVertical();
    }

    private void LoadProperties()
    {
        if ((currentShader == null && currentMaterial == null) || (targetShader == null && targetMaterial == null))
        {
            EditorUtility.DisplayDialog("Error", "Both current and target shaders/materials must be assigned.", "OK");
            return;
        }

        Shader sourceShader = currentShader != null ? currentShader : currentMaterial.shader;
        Shader destShader = targetShader != null ? targetShader : targetMaterial.shader;

        propertyMappings.Clear();
        
        // Get all properties from current shader
        int propertyCount = ShaderUtil.GetPropertyCount(sourceShader);
        
        List<ShaderPropertyMapping> tempMappings = new List<ShaderPropertyMapping>();
        
        for (int i = 0; i < propertyCount; i++)
        {
            string propertyName = ShaderUtil.GetPropertyName(sourceShader, i);
            ShaderPropertyType propertyType = GetPropertyType(ShaderUtil.GetPropertyType(sourceShader, i));
            
            // Get target properties of the same type
            List<string> targetPropertiesOfSameType = new List<string>();
            targetPropertiesOfSameType.Add("None"); // Add "None" option as default
            
            int targetPropertyCount = ShaderUtil.GetPropertyCount(destShader);
            for (int j = 0; j < targetPropertyCount; j++)
            {
                string targetPropName = ShaderUtil.GetPropertyName(destShader, j);
                ShaderPropertyType targetPropType = GetPropertyType(ShaderUtil.GetPropertyType(destShader, j));
                
                if (targetPropType == propertyType)
                {
                    targetPropertiesOfSameType.Add($"{targetPropType} | {targetPropName}");
                }
            }
            
            ShaderPropertyMapping mapping = new ShaderPropertyMapping
            {
                SourcePropertyName = propertyName,
                PropertyType = propertyType,
                TargetPropertyName = "None",
                TargetPropertyOptions = targetPropertiesOfSameType.ToArray(),
                SelectedTargetPropertyIndex = 0, // Default to "None"
                IsValidMapping = false
            };
            
            tempMappings.Add(mapping);
        }
        
        // Sort the property mappings: first by type, then alphabetically
        propertyMappings = tempMappings
            .OrderBy(p => p.PropertyType.ToString())
            .ThenBy(p => p.SourcePropertyName)
            .ToList();
        
        // For each mapping, sort the target options alphabetically (keeping "None" at index 0)
        foreach (var mapping in propertyMappings)
        {
            if (mapping.TargetPropertyOptions.Length <= 1) continue;
            
            string none = mapping.TargetPropertyOptions[0]; // Save "None" option
            var sortedOptions = mapping.TargetPropertyOptions.Skip(1).OrderBy(o => o).ToList();
            sortedOptions.Insert(0, none); // Re-insert "None" at the beginning
            mapping.TargetPropertyOptions = sortedOptions.ToArray();
        }
    }

    private void ApplySmartPropertyMatching()
    {
        foreach (ShaderPropertyMapping mapping in propertyMappings)
        {
            // Find the best match by property name among properties of the same type
            string bestMatch = FindBestMatchingProperty(mapping.SourcePropertyName, mapping.TargetPropertyOptions);
            
            if (!string.IsNullOrEmpty(bestMatch) && bestMatch != "None")
            {
                int matchIndex = System.Array.IndexOf(mapping.TargetPropertyOptions, bestMatch);
                if (matchIndex >= 0)
                {
                    mapping.SelectedTargetPropertyIndex = matchIndex;
                    mapping.TargetPropertyName = bestMatch;
                    mapping.IsValidMapping = true;
                }
            }
        }
    }

    private string FindBestMatchingProperty(string sourceProperty, string[] targetOptions)
    {
        // Skip the first option which is "None"
        if (targetOptions.Length <= 1) 
            return "None";
        
        string sourcePropertyName = sourceProperty.ToLower();
        List<(int priority, string option)> matches = new List<(int priority, string option)>();
        
        // Split source property into words (handle both camelCase and underscores)
        var sourceWords = SplitIntoWords(sourcePropertyName);
        
        for (int i = 1; i < targetOptions.Length; i++)
        {
            string targetOption = targetOptions[i];
            string targetPropertyName = targetOption.Substring(targetOption.IndexOf(" | ") + 3).ToLower();
            var targetWords = SplitIntoWords(targetPropertyName);
            
            // Check if there's at least one word match
            bool hasWordMatch = sourceWords.Any(sw => targetWords.Any(tw => tw == sw));
            if (!hasWordMatch) continue;
            
            // Priority levels:
            // 4 - Exact match
            // 3 - Target contains full source name
            // 2 - Source contains full target name
            // 1 - At least one word matches
            
            if (targetPropertyName == sourcePropertyName)
            {
                matches.Add((4, targetOptions[i]));
            }
            else if (targetPropertyName.Contains(sourcePropertyName))
            {
                matches.Add((3, targetOptions[i]));
            }
            else if (sourcePropertyName.Contains(targetPropertyName))
            {
                matches.Add((2, targetOptions[i]));
            }
            else
            {
                matches.Add((1, targetOptions[i]));
            }
        }
        
        // Return highest priority match if any found
        var bestMatch = matches.OrderByDescending(m => m.priority).FirstOrDefault();
        return bestMatch.option ?? "None";
    }

    private List<string> SplitIntoWords(string input)
    {
        // First split by underscore
        var parts = input.Split('_');
        List<string> words = new List<string>();
        
        foreach (var part in parts)
        {
            // Then split camelCase
            var camelCaseParts = System.Text.RegularExpressions.Regex.Replace(
                part,
                "([A-Z])",
                " $1",
                System.Text.RegularExpressions.RegexOptions.Compiled
            ).Trim().Split(' ');
            
            words.AddRange(camelCaseParts.Select(w => w.ToLower()));
        }
        
        return words.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
    }

    private ShaderPropertyType GetPropertyType(ShaderUtil.ShaderPropertyType type)
    {
        switch (type)
        {
            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                return ShaderPropertyType.Float;
            case ShaderUtil.ShaderPropertyType.Color:
                return ShaderPropertyType.Color;
            case ShaderUtil.ShaderPropertyType.Vector:
                return ShaderPropertyType.Vector;
            case ShaderUtil.ShaderPropertyType.TexEnv:
                return ShaderPropertyType.Texture;
            default:
                return ShaderPropertyType.Unknown;
        }
    }

    private void DrawPropertyMappings()
    {
        EditorGUILayout.LabelField("Property Mappings", EditorStyles.boldLabel);
        
        // Calculate how much space we have for property mappings
        Rect windowRect = position;
        float remainingHeight = windowRect.height - EditorGUIUtility.singleLineHeight * 20;
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(Mathf.Max(200, remainingHeight)));
        EditorGUILayout.BeginVertical("box");

        // Group properties by type
        var groupedProperties = propertyMappings.GroupBy(m => m.PropertyType);
        
        foreach (var group in groupedProperties)
        {
            // Initialize foldout state if not exists
            if (!ShaderPropertyMapping.TypeFoldouts.ContainsKey(group.Key))
            {
                ShaderPropertyMapping.TypeFoldouts[group.Key] = true;
            }
            
            // Draw foldout header
            ShaderPropertyMapping.TypeFoldouts[group.Key] = EditorGUILayout.Foldout(
                ShaderPropertyMapping.TypeFoldouts[group.Key], 
                $"{group.Key} Properties ({group.Count()})", 
                true
            );
            
            if (ShaderPropertyMapping.TypeFoldouts[group.Key])
            {
                foreach (ShaderPropertyMapping mapping in group)
                {
                    EditorGUILayout.BeginVertical("box");
                    
                    // Source property with type
                    EditorGUILayout.LabelField($"{mapping.SourcePropertyName}", EditorStyles.boldLabel);
                    
                    // Target property section with border
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    
                    // Store original GUI color
                    Color originalColor = GUI.color;
                    
                    // Set green color if mapping is valid
                    if (mapping.IsValidMapping && mapping.SelectedTargetPropertyIndex > 0)
                    {
                        GUI.color = new Color(0.7f, 1f, 0.7f);
                    }
                    
                    // Dropdown header (always visible)
                    EditorGUILayout.BeginHorizontal("box");
                    string displayName = mapping.SelectedTargetPropertyIndex >= 0 && 
                                      mapping.SelectedTargetPropertyIndex < mapping.TargetPropertyOptions.Length 
                        ? mapping.TargetPropertyOptions[mapping.SelectedTargetPropertyIndex] 
                        : "None";
                    
                    EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);
                    
                    if (GUILayout.Button(mapping.SearchExpanded ? "▲" : "▼", GUILayout.Width(20)))
                    {
                        mapping.SearchExpanded = !mapping.SearchExpanded;
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    // Reset GUI color
                    GUI.color = originalColor;
                    
                    // Dropdown content (visible when expanded)
                    if (mapping.SearchExpanded)
                    {
                        // Search field
                        mapping.SearchString = EditorGUILayout.TextField("Search", mapping.SearchString);
                        
                        // Filtered list
                        List<string> filteredOptions = mapping.TargetPropertyOptions
                            .Where(opt => string.IsNullOrEmpty(mapping.SearchString) || opt.ToLower().Contains(mapping.SearchString.ToLower()))
                            .ToList();
                        
                        float dropdownHeight = Mathf.Min(filteredOptions.Count * 20f, 200f);
                        EditorGUILayout.BeginVertical("box", GUILayout.Height(dropdownHeight));
                        
                        // Track scroll position per mapping
                        string scrollKey = $"{mapping.SourcePropertyName}_{mapping.PropertyType}";
                        if (!dropdownScrollPositions.ContainsKey(scrollKey))
                        {
                            dropdownScrollPositions[scrollKey] = Vector2.zero;
                        }
                        
                        // Use the tracked scroll position
                        dropdownScrollPositions[scrollKey] = EditorGUILayout.BeginScrollView(
                            dropdownScrollPositions[scrollKey], 
                            false, 
                            true, 
                            GUILayout.Height(dropdownHeight)
                        );
                        
                        for (int i = 0; i < filteredOptions.Count; i++)
                        {
                            string option = filteredOptions[i];
                            if (GUILayout.Button(option, EditorStyles.label))
                            {
                                mapping.SelectedTargetPropertyIndex = System.Array.IndexOf(mapping.TargetPropertyOptions, option);
                                
                                // Extract property name without type prefix for target property name
                                if (option != "None")
                                {
                                    mapping.TargetPropertyName = option.Substring(option.IndexOf(" | ") + 3);
                                }
                                else
                                {
                                    mapping.TargetPropertyName = "None";
                                }
                                
                                mapping.IsValidMapping = (option != "None");
                                mapping.SearchExpanded = false;
                            }
                        }
                        
                        EditorGUILayout.EndScrollView();
                        EditorGUILayout.EndVertical();
                    }
                    
                    EditorGUILayout.EndVertical(); // End target property border
                    EditorGUILayout.EndVertical(); // End property box
                    EditorGUILayout.Space(5);
                }
            }
        }
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    private void DrawConverterButtons()
    {
        EditorGUILayout.BeginHorizontal();
        
        GUI.enabled = propertiesLoaded && targetShader != null;
        if (GUILayout.Button("Convert Selected Materials", GUILayout.Height(30)))
        {
            ConvertSelectedMaterials();
        }
        
        if (currentShader != null && GUILayout.Button("Find & Convert All", GUILayout.Height(30)))
        {
            FindAndConvertAllMaterials();
        }
        GUI.enabled = true;
        
        EditorGUILayout.EndHorizontal();
    }

    private void SavePreset()
    {
        if (currentShader == null || targetShader == null)
        {
            EditorUtility.DisplayDialog("Error", "Both source and target shaders must be assigned.", "OK");
            return;
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Shader Mapping Preset",
            $"{currentShader.name}To{targetShader.name}Preset",
            "asset",
            "Save shader mapping preset"
        );

        if (string.IsNullOrEmpty(path)) return;

        var preset = ScriptableObject.CreateInstance<MaterialConverterPreset>();
        preset.sourceShaderName = currentShader.name;
        preset.targetShaderName = targetShader.name;

        foreach (var mapping in propertyMappings)
        {
            if (mapping.IsValidMapping && mapping.SelectedTargetPropertyIndex > 0)
            {
                string targetPropName = mapping.TargetPropertyName;
                if (targetPropName.Contains(" | "))
                {
                    targetPropName = targetPropName.Substring(targetPropName.IndexOf(" | ") + 3);
                }

                preset.propertyPairs.Add(new PropertyPair
                {
                    sourceProperty = mapping.SourcePropertyName,
                    targetProperty = targetPropName,
                    propertyType = (int)mapping.PropertyType
                });
            }
        }

        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Success", "Preset saved successfully!", "OK");
    }

    private void LoadPreset()
    {
        string path = EditorUtility.OpenFilePanelWithFilters(
            "Load Shader Mapping Preset",
            "Assets",
            new[] { "Material Converter Preset", "asset", "All files", "*" }
        );

        if (string.IsNullOrEmpty(path)) return;

        // Convert the full path to a project-relative path
        path = "Assets" + path.Substring(Application.dataPath.Length);

        var preset = AssetDatabase.LoadAssetAtPath<MaterialConverterPreset>(path);
        if (preset == null)
        {
            EditorUtility.DisplayDialog("Error", "Failed to load preset file.", "OK");
            return;
        }

        // Find and assign shaders
        currentShader = Shader.Find(preset.sourceShaderName);
        targetShader = Shader.Find(preset.targetShaderName);

        if (currentShader == null || targetShader == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not find one or both shaders specified in the preset.", "OK");
            return;
        }

        // Load properties automatically
        LoadProperties();
        propertiesLoaded = true;

        // Apply mappings from preset
        foreach (var pair in preset.propertyPairs)
        {
            var mapping = propertyMappings.FirstOrDefault(m => 
                m.SourcePropertyName == pair.sourceProperty && 
                (int)m.PropertyType == pair.propertyType);

            if (mapping != null)
            {
                // Find the target property in the options
                int targetIndex = System.Array.FindIndex(mapping.TargetPropertyOptions, 
                    opt => opt.EndsWith(pair.targetProperty));

                if (targetIndex >= 0)
                {
                    mapping.SelectedTargetPropertyIndex = targetIndex;
                    mapping.TargetPropertyName = mapping.TargetPropertyOptions[targetIndex];
                    mapping.IsValidMapping = true;
                }
            }
        }

        Repaint();
        EditorUtility.DisplayDialog("Success", "Preset loaded successfully!", "OK");
    }

    private void FindAndConvertAllMaterials()
    {
        if (currentShader == null)
        {
            EditorUtility.DisplayDialog("Error", "Source shader must be assigned.", "OK");
            return;
        }
        
        // Find all materials in the project
        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        List<Material> materialsToConvert = new List<Material>();
        
        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            
            if (mat != null && mat.shader == currentShader)
            {
                materialsToConvert.Add(mat);
            }
        }
        
        if (materialsToConvert.Count == 0)
        {
            EditorUtility.DisplayDialog("No Materials Found", "No materials using the selected shader were found in the project.", "OK");
            return;
        }
        
        bool proceed = EditorUtility.DisplayDialog(
            "Convert All Materials",
            $"Found {materialsToConvert.Count} materials using the shader '{currentShader.name}'. Do you want to convert all of them to '{targetShader.name}'?",
            "Convert All",
            "Cancel"
        );
        
        if (proceed)
        {
            ConvertMaterialsList(materialsToConvert);
        }
    }

    private void ConvertSelectedMaterials()
    {
        if (targetShader == null)
        {
            EditorUtility.DisplayDialog("Error", "Target shader must be assigned.", "OK");
            return;
        }

        List<Material> materialsToConvert = new List<Material>();
        
        // Get materials from selection
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj is Material mat)
            {
                materialsToConvert.Add(mat);
            }
        }
        
        if (materialsToConvert.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "No materials selected. Please select materials in the Project window.", "OK");
            return;
        }
        
        ConvertMaterialsList(materialsToConvert);
    }
    
    private void ConvertMaterialsList(List<Material> materialsToConvert)
    {
        List<string> logMessages = new List<string>();
        logMessages.Add($"Converting {materialsToConvert.Count} materials from {currentShader.name} to {targetShader.name}");
        logMessages.Add("-----------------------------------------------------");
        
        // Convert each material
        foreach (Material sourceMaterial in materialsToConvert)
        {
            logMessages.Add($"Converting material: {sourceMaterial.name}");
            Undo.RecordObject(sourceMaterial, "Convert Material");
            
            // Store texture values before changing shader
            Dictionary<string, (Texture, Vector2, Vector2)> textureData = new Dictionary<string, (Texture, Vector2, Vector2)>();
            foreach (ShaderPropertyMapping mapping in propertyMappings)
            {
                if (mapping.PropertyType == ShaderPropertyType.Texture && mapping.IsValidMapping)
                {
                    if (sourceMaterial.HasProperty(mapping.SourcePropertyName))
                    {
                        Texture tex = sourceMaterial.GetTexture(mapping.SourcePropertyName);
                        Vector2 offset = sourceMaterial.GetTextureOffset(mapping.SourcePropertyName);
                        Vector2 scale = sourceMaterial.GetTextureScale(mapping.SourcePropertyName);
                        textureData[mapping.SourcePropertyName] = (tex, offset, scale);
                    }
                }
            }
            
            // Change shader
            sourceMaterial.shader = targetShader;
            
            // Copy properties based on mapping
            foreach (ShaderPropertyMapping mapping in propertyMappings)
            {
                // Skip if no mapping or set to "None"
                if (mapping.SelectedTargetPropertyIndex <= 0 || mapping.TargetPropertyName == "None" || !mapping.IsValidMapping)
                    continue;
                
                // Extract the real property name from the mapping
                string targetPropertyName = mapping.TargetPropertyName;
                if (targetPropertyName.Contains(" | "))
                {
                    targetPropertyName = targetPropertyName.Substring(targetPropertyName.IndexOf(" | ") + 3);
                }
                
                switch (mapping.PropertyType)
                {
                    case ShaderPropertyType.Float:
                        if (sourceMaterial.HasProperty(mapping.SourcePropertyName) && sourceMaterial.HasProperty(targetPropertyName))
                        {
                            float value = sourceMaterial.GetFloat(mapping.SourcePropertyName);
                            sourceMaterial.SetFloat(targetPropertyName, value);
                            logMessages.Add($"  Mapped {mapping.PropertyType}: {mapping.SourcePropertyName} -> {targetPropertyName} = {value}");
                        }
                        break;
                    case ShaderPropertyType.Color:
                        if (sourceMaterial.HasProperty(mapping.SourcePropertyName) && sourceMaterial.HasProperty(targetPropertyName))
                        {
                            Color value = sourceMaterial.GetColor(mapping.SourcePropertyName);
                            sourceMaterial.SetColor(targetPropertyName, value);
                            logMessages.Add($"  Mapped {mapping.PropertyType}: {mapping.SourcePropertyName} -> {targetPropertyName} = {value}");
                        }
                        break;
                    case ShaderPropertyType.Vector:
                        if (sourceMaterial.HasProperty(mapping.SourcePropertyName) && sourceMaterial.HasProperty(targetPropertyName))
                        {
                            Vector4 value = sourceMaterial.GetVector(mapping.SourcePropertyName);
                            sourceMaterial.SetVector(targetPropertyName, value);
                            logMessages.Add($"  Mapped {mapping.PropertyType}: {mapping.SourcePropertyName} -> {targetPropertyName} = {value}");
                        }
                        break;
                    case ShaderPropertyType.Texture:
                        if (sourceMaterial.HasProperty(targetPropertyName) && textureData.ContainsKey(mapping.SourcePropertyName))
                        {
                            var (tex, offset, scale) = textureData[mapping.SourcePropertyName];
                            sourceMaterial.SetTexture(targetPropertyName, tex);
                            sourceMaterial.SetTextureOffset(targetPropertyName, offset);
                            sourceMaterial.SetTextureScale(targetPropertyName, scale);
                            
                            logMessages.Add($"  Mapped {mapping.PropertyType}: {mapping.SourcePropertyName} -> {targetPropertyName} = {(tex ? tex.name : "null")}");
                            logMessages.Add($"    Offset: {offset}, Scale: {scale}");
                        }
                        break;
                }
            }
            
            logMessages.Add("-----------------------------------------------------");
        }
        
        // Print all logs at once
        Debug.Log(string.Join("\n", logMessages));
        
        EditorUtility.DisplayDialog("Success", $"Converted {materialsToConvert.Count} materials. See the console for details.", "OK");
    }
}