namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// Every API test class joins this collection so the run shares one database and one host.
/// Put <c>[Collection(ApiTestCollection.Name)]</c> on the class and take
/// <see cref="ApiTestFixture"/> in its constructor.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiTestCollection : ICollectionFixture<ApiTestFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "Api";
}
