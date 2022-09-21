using EnvDTE;
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
            var getAllKeyRegex = new Regex(@"^(?<EndOfKey>(\\""|[^""])*"")\s*:");

            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();

            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView?.TextSnapshot?.ContentType?.DisplayName.Equals("Json", StringComparison.OrdinalIgnoreCase) != true)
            {
                await Notify("No \"current\" document found or it's not a JSON document.");
                return;
            }

            string currentText = docView.TextView.TextSnapshot.GetText();

            int position = docView.TextView.Caret.Position.BufferPosition.Position;

            Match match =  getAllKeyRegex.Match(currentText.Substring(position));

            if(!match.Success)
            {
                await Notify("Impossible to execute : The cursor is not on a JSON key");
                return;
            }

            string textToEndOfPath = currentText.Substring(0, position) + match.Groups["EndOfKey"].Value;
            string reversedText = textToEndOfPath.ReverseText();

            var reverseKeyList = new List<string>();

            string fullKeyPath = string.Join(".", await ParseReverseAsync(reverseKeyList, reversedText)).ReverseText();

            Clipboard.SetText(fullKeyPath);

            await Notify($"\"{fullKeyPath}\" copied");
        }

        private async Task<List<string>> ParseReverseAsync(List<string> reverseKeyList, string reversedText)
        {
            var reverseStringDetectionRegex = new Regex(@"^""(?<Key>(""\\|[^""])*)""\s*");
            var objectStartWithKeyRegex = new Regex(@"^(?<start>{|\[)\s*:\s*");
            var objectStartWithoutKeyRegex = new Regex(@"^(?<start>{|\[)\s*");
            var otherJsonObject = new Regex(@"^,\s*}(\s+|llun|\d+(\.\d+)?|""(""\\|[^""])*""|:|,|(?<curlyBracket>})|(?<-curlyBracket>{)|(?<squareBracket>\])|(?<-squareBracket>\[))*{(\s*:\s*""(""\\|[^""])*"")*\s*");
            var otherJsonCollection = new Regex(@"^,\s*\](\s+|llun|\d+(\.\d+)?|""(""\\|[^""])*""|:|,|(?<curlyBracket>})|(?<-curlyBracket>{)|(?<squareBracket>\])|(?<-squareBracket>\[))*\[(\s*:\s*""(""\\|[^""])*"")*\s*");
            var otherJsonSimpleValue = new Regex(@"^,\s*(llun|\d+(\.\d+)?|""(""\\|[^""])*"")\s*:\s*""(""\\|[^""])*""\s*");

            int position = 0;

            List<Regex> regexList = new()
            {
                objectStartWithKeyRegex,
                objectStartWithoutKeyRegex,
                otherJsonObject,
                otherJsonCollection,
                otherJsonSimpleValue,
            };

            while(reversedText.Length > 0)
            {
                Match keyPartMatch = reverseStringDetectionRegex.Match(reversedText);

                if(keyPartMatch.Success)
                {
                    position=0;

                    if(reverseKeyList.Count > 0 && reverseKeyList.Last().EndsWith("["))
                    {
                        reverseKeyList[reverseKeyList.Count - 1] += keyPartMatch.Groups["Key"].Value;
                    }
                    else
                    {
                        reverseKeyList.Add(keyPartMatch.Groups["Key"].Value);
                    }
                    reversedText = reverseStringDetectionRegex.Replace(reversedText, "");

                    while(reversedText.Length > 0)
                    {
                        if(!regexList.Any(regex => ParseAndEatJsonElements(regex,ref position, ref reversedText, reverseKeyList)))
                        {
                            break;
                        }
                    }
                }
                else
                {
                    await Notify("Format Error. Current decoded path copied");

                    return reverseKeyList;
                }
            }

            return reverseKeyList;
        }

        private bool ParseAndEatJsonElements(Regex regex, ref int position, ref string reversedText, List<string> reverseKeyList)
        {
            Match match = regex.Match(reversedText);

            if(match.Success)
            {
                reversedText = regex.Replace(reversedText, "");
                if(match.Groups["start"].Success)
                {
                    if(match.Groups["start"].Value.Equals("["))
                    {
                        reverseKeyList.Add($"]{position.ToString().ReverseText()}[");
                    }

                    position = 0;
                }
                else
                {
                    position++;
                }
                return true;
            }

            return false;
        }

        private async Task<int> Notify(string text)
        {
            IVsStatusbar statusBar = await VS.Services.GetStatusBarAsync();
            
            return statusBar.SetText(text);
        }
    }
}
