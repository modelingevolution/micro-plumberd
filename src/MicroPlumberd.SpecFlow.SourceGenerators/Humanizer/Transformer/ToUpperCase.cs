namespace Humanizer;

class ToUpperCase : ICulturedStringTransformer
{
    public string Transform(string input) =>
        Transform(input, null);

    public string Transform(string input, CultureInfo? culture)
    {
        culture ??= CultureInfo.DefaultThreadCurrentCulture;

        return culture.TextInfo.ToUpper(input);
    }
}