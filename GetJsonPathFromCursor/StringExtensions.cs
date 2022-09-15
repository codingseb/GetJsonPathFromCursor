using System.Linq;

namespace GetJsonPathFromCursor
{
    public static class StringExtensions
    {
        public static string ReverseText(this string text) => new(text.Reverse().ToArray());
    }
}
