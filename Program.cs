using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace TheWanderingInn {
    class Program {
        static HttpClient Client = new HttpClient();
        static Options Options;
        static void Main(string[] args) {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional : true, reloadOnChange : true);

            var t = builder.Build();

            Options = t.GetSection("WanderingInn").Get<Options>();

            ExportWanderingInn().GetAwaiter().GetResult();
        }

        private static async Task ExportWanderingInn() {
            var outputDir = Directory.CreateDirectory(Options.Output);

            var lines = await GetHtmlContent(Options.TableOfContents);

            var tocStartLine = lines.FindIndex(x => x.Contains("<div class=\"entry-content\">"));
            var tocEndLine = lines.FindIndex(x => x.Contains("</div><!-- .entry-content -->"));

            var tocLines = lines.Skip(tocStartLine).Take(tocEndLine - tocStartLine).ToList();

            var volumeMatcher = new Regex(@"<p><strong>(Volume *\d)<");
            var volumes = tocLines.Where(x => volumeMatcher.IsMatch(x));

            foreach (var volumeHtmlHeader in volumes) {
                var volumeName = volumeMatcher.Matches(volumeHtmlHeader) [0].Groups[1].ToString();
                var volumeNumber = volumeName.Last().ToString();
                var volumeStart = tocLines.FindIndex(x => x == volumeHtmlHeader);
                var volumeContent = tocLines.Skip(volumeStart + 1).TakeWhile(x => !volumeMatcher.IsMatch(x)).ToList();

                if (Options.VolumesToExport.Any(x => x == volumeNumber)) {
                    await ExportVolume(volumeName, volumeHtmlHeader, volumeContent);
                }
            }
        }

        private static async Task ExportVolume(string volumeName, string volumeHeader, List<string> chapters) {
            Console.WriteLine($"Exporting {volumeName} with {chapters.Count} chapters");
            var volumeDirectory = Directory.CreateDirectory(Path.Combine(Options.Output, volumeName));

            var chapterExtractor = new Regex("<a href=\"(.*)\">(.*)<\\/a>");
            var actualChapters = chapters.Where(x => chapterExtractor.IsMatch(x)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("<html>");
            sb.AppendLine("<body>");
            sb.AppendLine("<h1>Table of Contents</h1>");
            sb.AppendLine("<p style=\"text-indent:0pt\">");

            for (int i = 0; i < actualChapters.Count; i++) {
                var chapter = actualChapters[i];
                var chapterInformation = chapterExtractor.Matches(chapter);
                var chapterUrl = chapterInformation[0].Groups[1].ToString();
                var chapterName = chapterInformation[0].Groups[2].ToString();
                
                Console.WriteLine($"Exporting {chapterName} in volume {volumeName} ({i+1}/{actualChapters.Count})");

                var fileName = await ExportChapter(i, chapterUrl, chapterName, volumeDirectory.FullName);
                sb.AppendLine($"<a href=\"{fileName}\">{chapterName}</a><br/>");
            }

            sb.AppendLine("</p>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            File.WriteAllText(Path.Combine(volumeDirectory.FullName, "index.html"), sb.ToString());
        }

        private static async Task<string> ExportChapter(int chapterIndex, string url, string chapterName, string basePath) {
            if (url.Contains("wanderinginn.wordpress.com")) {
                url = url.Replace("wanderinginn.wordpress.com", "wanderinginn.com");
            }
            var contents = await GetHtmlContent(url);
            var chapterStart = contents.FindIndex(x => x.Contains("<div class=\"entry-content\">"));
            var chapterEnd = contents.FindIndex(chapterStart, x => x.Contains("<hr />"));
            var chapterContents = contents.Skip(chapterStart).Take(chapterEnd - chapterStart);
            chapterContents = chapterContents.Skip(1).Prepend($"<h1>{chapterName}</h1>");
            var fileName = $"{chapterIndex}_{chapterName.Replace(":", " - ")}.html";
            var filePath = Path.Combine(basePath, fileName);
            File.WriteAllLines(filePath, chapterContents);
            return fileName;
        }

        private static async Task<List<string>> GetHtmlContent(string url) {
            var htmlString = await Client.GetStringAsync(url);
            var lines = htmlString.Split(new [] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
            return lines;
        }
    }
}