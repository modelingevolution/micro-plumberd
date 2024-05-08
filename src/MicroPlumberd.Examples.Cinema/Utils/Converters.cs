using MudBlazor;

namespace MicroPlumberd.Examples.Cinema.Utils
{
    public class OptionConverters
    {
        public static MudBlazor.Converter<Option<string>, string> String =
            new MudBlazor.Converter<Option<string>, string>() { GetFunc = x => x, SetFunc = x => x.Value };
    }
}
