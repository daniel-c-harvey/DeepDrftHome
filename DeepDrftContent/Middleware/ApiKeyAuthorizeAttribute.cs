namespace DeepDrftContent.Middleware
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ApiKeyAuthorizeAttribute : Attribute
    {
    }
}