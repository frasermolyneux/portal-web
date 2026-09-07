namespace XtremeIdiots.Portal.Web.IntegrationTests.Execution;

[Trait("Category", "HttpIntegration")]
public sealed class TestCategoryContractTests
{
    [Fact]
    public void EveryIntegrationTest_BelongsToExactlyOneExecutionSuite()
    {
        var assembly = typeof(TestCategoryContractTests).Assembly;
        var testMethods = assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Where(method => method.IsDefined(typeof(FactAttribute), inherit: true))
            .ToArray();

        Assert.NotEmpty(testMethods);
        Assert.All(testMethods, method =>
        {
            var categories = assembly.CustomAttributes
                .Concat(method.DeclaringType!.CustomAttributes)
                .Concat(method.CustomAttributes)
                .Where(attribute => attribute.AttributeType == typeof(TraitAttribute) ||
                                    attribute.AttributeType == typeof(AssemblyTraitAttribute))
                .Where(attribute => Equals(attribute.ConstructorArguments[0].Value, "Category"))
                .Select(attribute => attribute.ConstructorArguments[1].Value as string)
                .Where(value => value is "Unit" or "HttpIntegration" or "Browser")
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.True(categories.Length == 1 && categories[0] is "HttpIntegration" or "Browser",
                $"{method.DeclaringType.FullName}.{method.Name} must declare exactly one HttpIntegration or Browser category.");
        });
    }
}
