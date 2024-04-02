using Xunit;

namespace MicroPlumberd.Testing;

[CollectionDefinition("Specs")]
public class TestCollection : ICollectionFixture<SpecsContext>
{
    // This class has no code and is never created. Its purpose is solely
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}