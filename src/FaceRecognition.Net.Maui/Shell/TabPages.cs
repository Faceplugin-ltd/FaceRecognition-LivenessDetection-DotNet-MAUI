namespace FaceRecognition.Net.Maui;

/// <summary>The Identity tab. One class per tab, because Shell's DataTemplate takes a type.</summary>
public class IdentityTabPage : CameraTabPage
{
    public IdentityTabPage() : base(attributeMode: false)
    {
    }
}

/// <summary>The Attribute tab — the same screen, reading attributes instead of searching.</summary>
public class AttributeTabPage : CameraTabPage
{
    public AttributeTabPage() : base(attributeMode: true)
    {
    }
}
