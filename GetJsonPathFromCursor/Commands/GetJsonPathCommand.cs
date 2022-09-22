using EnvDTE;
using Microsoft.VisualStudio.GraphModel.CodeSchema;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace GetJsonPathFromCursor
{
    [Command(PackageIds.GetJsonPathCommand)]
    internal sealed class GetJsonPathCommand : BaseCommand<GetJsonPathCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            var getAllKeyRegex = new Regex(@"^(?<startOfKey>\s*"")?(?<EndOfKey>(\\""|[^""])*"")\s*:");
            var isOutOfAStringRegex = new Regex(@"^(""(\\""|[^""])*""|[^""])*");

            // parse and eat Json Element regex
            var objectStartWithKeyRegex = new Regex(@"^(?<start>{|\[)\s*:\s*");
            var objectStartWithoutKeyRegex = new Regex(@"^(?<start>{|\[)\s*");
            var otherJsonObject = new Regex(@"^,\s*}(\s+|\w+|\d+(\.\d+)?|""(""\\|[^""])*""|:|,|(?<curlyBracket>})|(?<-curlyBracket>{)|(?<squareBracket>\])|(?<-squareBracket>\[))*{(\s*:\s*""(""\\|[^""])*"")?\s*");
            var otherJsonCollection = new Regex(@"^,\s*\](\s+|\w+|\d+(\.\d+)?|""(""\\|[^""])*""|:|,|(?<curlyBracket>})|(?<-curlyBracket>{)|(?<squareBracket>\])|(?<-squareBracket>\[))*\[(\s*:\s*""(""\\|[^""])*"")?\s*");
            var otherJsonSimpleValue = new Regex(@"^,\s*(\w+|\d+(\.\d+)?|""(""\\|[^""])*"")(\s*:\s*""(""\\|[^""])*"")?\s*");

            List<Regex> parseAndEatRegexList = new()
            {
                objectStartWithKeyRegex,
                objectStartWithoutKeyRegex,
                otherJsonObject,
                otherJsonCollection,
                otherJsonSimpleValue,
            };

            var reverseKeyList = new List<string>();

            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();

            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView?.TextSnapshot?.ContentType?.DisplayName.Equals("Json", StringComparison.OrdinalIgnoreCase) != true)
            {
                await NotifyAsync("No \"current\" document found or it's not a JSON document.");
                return;
            }

            string currentText = docView.TextView.TextSnapshot.GetText();

            int textPosition = docView.TextView.Caret.Position.BufferPosition.Position;

            Match match = getAllKeyRegex.Match(currentText.Substring(textPosition));

            if (match.Success)
            {
                // Correction of the position if cursor is just before a key
                if (match.Groups["startOfKey"].Success)
                    textPosition += match.Groups["startOfKey"].Length;
            }
            else
            {
                // Try to correct th eposition of the cursor if we are outside of a key

                int collectionPosition = 0;

                while (textPosition > 0)
                {
                    // Correction of the position if the cursor is on the value part of a key value pair or just before the ':'
                    if (Regex.IsMatch(currentText.Substring(textPosition), @"^\s*:")
                        && isOutOfAStringRegex.IsMatch(currentText.Substring(0, textPosition))) // Ensure that the : is not in a string
                    {
                        while (textPosition > 0 && currentText[textPosition] != '"')
                            textPosition--;

                        break;
                    }

                    textPosition--;

                    // if we are in a collection
                    if ((currentText[textPosition] == ',' || currentText[textPosition] == '[')
                        && isOutOfAStringRegex.IsMatch(currentText.Substring(0, textPosition))) // Ensure that the : is not in a string
                    {
                        textPosition++;

                        string leafReversedText = currentText.Substring(0, textPosition).ReverseText();

                        while (leafReversedText.Length > 0)
                        {
                            if (!parseAndEatRegexList.Any(regex => ParseAndEatJsonElements(regex, ref collectionPosition, ref leafReversedText, reverseKeyList)))
                            {
                                textPosition = leafReversedText.Length - 1;
                                break;
                            }
                        }

                        break;
                    }
                }

                match = getAllKeyRegex.Match(currentText.Substring(textPosition));

                if (textPosition == 0 || !match.Success)
                {
                    await NotifyAsync("Impossible to execute : The position of the cursor is not on an identifiable part of JSON. Check that the format of the file is correct.");
                    return;
                }
            }

            string textToEndOfPath = currentText.Substring(0, textPosition) + match.Groups["EndOfKey"].Value;
            string reversedText = textToEndOfPath.ReverseText();

            var messages = new List<string>();

            string fullKeyPath = string.Join(".", await ParseReverseAsync(reverseKeyList, reversedText, messages, parseAndEatRegexList)).ReverseText();

            Clipboard.SetText(fullKeyPath);

            messages.Add($"\"{fullKeyPath}\" copied");

            await NotifyAsync(string.Join(": ", messages));
        }

        private async Task<List<string>> ParseReverseAsync(List<string> reverseKeyList, string reversedText, List<string> messages, List<Regex> parseAndEatRegexList)
        {
            var reverseStringDetectionRegex = new Regex(@"^""(?<Key>(""\\|[^""])*)""\s*");

            int collectionPosition = 0;

            while (reversedText.Length > 0)
            {
                Match keyPartMatch = reverseStringDetectionRegex.Match(reversedText);

                if (keyPartMatch.Success)
                {
                    collectionPosition = 0;

                    if (reverseKeyList.Count > 0 && reverseKeyList.Last().EndsWith("["))
                    {
                        reverseKeyList[reverseKeyList.Count - 1] += keyPartMatch.Groups["Key"].Value;
                    }
                    else
                    {
                        reverseKeyList.Add(keyPartMatch.Groups["Key"].Value);
                    }
                    reversedText = reverseStringDetectionRegex.Replace(reversedText, "");

                    while (reversedText.Length > 0)
                    {
                        if (!parseAndEatRegexList.Any(regex => ParseAndEatJsonElements(regex, ref collectionPosition, ref reversedText, reverseKeyList)))
                        {
                            break;
                        }
                    }
                }
                else
                {
                    messages.Add("Format Error. Current decoded path");
                    await NotifyAsync(string.Join(": ", messages));

                    return reverseKeyList;
                }
            }

            return reverseKeyList;
        }

        private bool ParseAndEatJsonElements(Regex regex, ref int collectionPosition, ref string reversedText, List<string> reverseKeyList)
        {
            Match match = regex.Match(reversedText);

            if (match.Success)
            {
                reversedText = regex.Replace(reversedText, "");
                if (match.Groups["start"].Success)
                {
                    if (match.Groups["start"].Value.Equals("["))
                    {
                        if (reverseKeyList.Count > 0 && reverseKeyList.Last().EndsWith("["))
                        {
                            reverseKeyList[reverseKeyList.Count - 1] += $"]{collectionPosition.ToString().ReverseText()}[";
                        }
                        else
                        {
                            reverseKeyList.Add($"]{collectionPosition.ToString().ReverseText()}[");
                        }
                    }

                    collectionPosition = 0;
                }
                else
                {
                    collectionPosition++;
                }
                return true;
            }

            return false;
        }

        private async Task<int> NotifyAsync(string text)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            IVsStatusbar statusBar = await VS.Services.GetStatusBarAsync();

            return statusBar.SetText(text);
        }
    }
}
