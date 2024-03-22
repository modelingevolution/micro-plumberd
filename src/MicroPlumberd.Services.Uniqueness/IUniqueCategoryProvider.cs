namespace MicroPlumberd.Services.Uniqueness;

interface IUniqueCategoryProvider
{
    static abstract string Category { get;  }
}