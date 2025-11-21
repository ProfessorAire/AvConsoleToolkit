// UploadAsset.cake - Upload packaged zip to GitHub release

using System.Net.Http.Headers;
using System.Net.Http;
#load "./Environment.cake"
var artifactsDir = GetArtifactsPath();

Task("Default")
    .Does(async () =>
{
    var githubToken = Argument("githubToken", EnvironmentVariable("GITHUB_TOKEN"));
    if (string.IsNullOrWhiteSpace(githubToken)) throw new InvalidOperationException("GitHub token not provided. Use the --githubToken argument or set GITHUB_TOKEN env var.");

    var uploadTemplatePath = System.IO.Path.Combine(artifactsDir, "upload_url.txt");
    var uploadTemplate = GetEnvironmentVariable("UPLOAD_URL");
    if (string.IsNullOrWhiteSpace(uploadTemplate))
    {
         throw new InvalidOperationException("upload_url not found. Run CreateRelease first.");
    }

    var zips = GetFiles(System.IO.Path.Combine(artifactsDir, "AvConsoleToolkit-v*.zip"));
    if (!zips.Any()) throw new InvalidOperationException("No packaged artifact found in artifacts directory. Run Package task first.");
    var packagePath = zips.First().FullPath;

    var fileName = System.IO.Path.GetFileName(packagePath);
    var uploadUrl = uploadTemplate;
    var idx = uploadUrl.IndexOf('{');
    if (idx >= 0) uploadUrl = uploadUrl.Substring(0, idx);
    var finalUrl = $"{uploadUrl}?name={System.Net.WebUtility.UrlEncode(fileName)}";

    using var http = new System.Net.Http.HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("AvConsoleToolkit-Cake-Upload");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", githubToken);

    using var fs = System.IO.File.OpenRead(packagePath);
    var content = new StreamContent(fs);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    var resp = await http.PostAsync(finalUrl, content);
    var respBody = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Upload failed: {resp.StatusCode}\n{respBody}");
    Information($"Uploaded asset {fileName} to release.");
});

RunTarget(Argument("target", "Default"));
