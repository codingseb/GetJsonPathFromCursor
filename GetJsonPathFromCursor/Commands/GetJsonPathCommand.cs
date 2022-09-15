using EnvDTE;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
                await VS.MessageBox.ShowWarningAsync("Get full JSON path to cursor", "No \"current\" document found or it's not a JSON document.");
                return;
            }

            string currentText = docView.TextView.TextSnapshot.GetText();

            int position = docView.TextView.Caret.Position.BufferPosition.Position;

            Match match =  getAllKeyRegex.Match(currentText.Substring(position));

            if(!match.Success)
            {
                await VS.MessageBox.ShowWarningAsync("Get full JSON path to cursor", "Impossible to execute : The cursor is not on a JSON key");
                return;
            }

            string textToEndOfPath = currentText.Substring(0, position) + match.Groups["EndOfKey"].Value;
            string reversedText = textToEndOfPath.ReverseText();

            var reverseKeyList = new List<string>();

            string fullKeyPath = string.Join(".", ParseReverse(reverseKeyList, reversedText)).ReverseText();

            Clipboard.SetText(fullKeyPath);
        }

        private List<string> ParseReverse(List<string> reverseKeyList, string reversedText)
        {
            var reverseStringDetectionRegex = new Regex(@"^""(?<Key>(""\\|[^""])*)""\s*");
            var objectStartWithKeyRegex = new Regex(@"^{\s*:\s*");
            var objectStartWithoutKeyRegex = new Regex(@"^{\s*");
            var otherJsonObject = new Regex(@"^,\s*}(\s+|""(""\\|[^""])*""|:|,|(?<curlyBracket>})|(?<-curlyBracket>{))*{(\s*:\s*""(""\\|[^""])*"")*\s*");
            var otherJsonString = new Regex(@"^,\s*""(""\\|[^""])*""\s*:\s*""(""\\|[^""])*""\s*");

            while(reversedText.Length > 0)
            {
                Match keyPartMatch = reverseStringDetectionRegex.Match(reversedText);

                if(keyPartMatch.Success)
                {
                    reverseKeyList.Add(keyPartMatch.Groups["Key"].Value);
                    reversedText = reverseStringDetectionRegex.Replace(reversedText, "");

                    while(reversedText.Length > 0)
                    {
                        if(objectStartWithKeyRegex.IsMatch(reversedText))
                        {
                            reversedText =  objectStartWithKeyRegex.Replace(reversedText, "");
                        }
                        else if(objectStartWithoutKeyRegex.IsMatch(reversedText))
                        {
                            reversedText =  objectStartWithoutKeyRegex.Replace(reversedText, "");
                        }
                        else if(otherJsonObject.IsMatch(reversedText))
                        {
                            reversedText =  otherJsonObject.Replace(reversedText, "");
                        }
                        else if(otherJsonString.IsMatch(reversedText))
                        {
                            reversedText =  otherJsonString.Replace(reversedText, "");
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    VS.MessageBox.ShowWarning("Get full JSON path to cursor", "Format Error. Current decoded path copied");

                    return reverseKeyList;
                }
            }

            return reverseKeyList;
        }
    }
}
