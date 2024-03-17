using Xunit.Abstractions;
using Xunit.Sdk;

namespace MicroPlumberd.Tests.Utils;

[TraitDiscoverer("MicroPlumberd.Tests.Utils.TestCategoryDiscoverer", "MicroPlumberd.Tests")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class TestCategoryAttribute : Attribute, ITraitAttribute
{
    public string Category { get; }

    public TestCategoryAttribute(string category)
    {
        Category = category;
    }
}
public class TestCategoryDiscoverer : ITraitDiscoverer
{
    private const string Key = "Category";
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        string testCase;
        var attributeInfo = traitAttribute as ReflectionAttributeInfo;
        var testCaseAttribute = attributeInfo?.Attribute as TestCategoryAttribute;
        if (testCaseAttribute != null)
        {
            testCase = testCaseAttribute.Category;
        }
        else
        {
            var constructorArguments = traitAttribute.GetConstructorArguments().ToArray();
            testCase = constructorArguments[0].ToString();
        }
        yield return new KeyValuePair<string, string>(Key, testCase);
    }
}