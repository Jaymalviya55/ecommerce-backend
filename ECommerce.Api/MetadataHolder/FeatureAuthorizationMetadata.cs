namespace ECommerce.Api.MetadataHolder;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class FeatureAuthorizationAttribute : Attribute
{
    public string Feature { get; }
    public string Action { get; }

    public FeatureAuthorizationAttribute(string feature, string action = "read")
    {
        Feature = feature;
        Action = action;
    }
}
