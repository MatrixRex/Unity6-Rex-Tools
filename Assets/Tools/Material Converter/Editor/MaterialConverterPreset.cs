using UnityEngine;
using System.Collections.Generic;

public class MaterialConverterPreset : ScriptableObject
{
    public string sourceShaderName;
    public string targetShaderName;
    public List<PropertyPair> propertyPairs = new List<PropertyPair>();
}

[System.Serializable]
public class PropertyPair
{
    public string sourceProperty;
    public string targetProperty;
    public int propertyType;
}
