using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NiCloud;
using NiCloud.Services;
using System.Runtime.InteropServices;
using System.Text;

namespace SampleApp;

static class Program
{
    static string ReadPassword()
    {
        var pass = new StringBuilder();
        while (true)
        {
            var c = Console.ReadKey(true).KeyChar;
            if (c == 13)
            {
                Console.WriteLine();
                break;
            }
            if (c == 8)
            {
                if (pass.Length > 0)
                {
                    Console.Write("\b \b");
                    pass.Length--;
                }
            }
            else
            {
                Console.Write('*');

                pass.Append(c);
            }
        }

        return pass.ToString();
    }

    [Flags]
    public enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001,
        // Legacy flag, should not be used.
        ES_USER_PRESENT = 0x00000004
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern uint SetThreadExecutionState(EXECUTION_STATE esFlags);
    private static void KeepAlive()
    {
        SetThreadExecutionState(EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
    }

    static async Task Main()
    {
        var serviceCollection = new ServiceCollection()
            .AddLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Trace));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var logger = serviceProvider.GetService<ILogger<NiCloudService>>();

        NiCloudSession session;
        if (File.Exists("session.json"))
        {
            var file = File.ReadAllText("session.json");
            session = NiCloudSession.Deserialize(file);
        } else
        {
            session = new NiCloudSession();
        }

        var api = new NiCloudService(session, logger);
        Console.WriteLine("Checking existing session");
        var sessionValid = await api.CheckSession();
        Console.WriteLine($"Result: {sessionValid}");

        if (!sessionValid)
        {
            Console.Write("Email: ");
            var mail = Console.ReadLine();
            Console.Write("Password: ");
            var pass = ReadPassword();
            await api.Init(mail, pass);

            if (api.Requires2fa)
            {
                var verification = await api.SendVerificationCode();
                Console.WriteLine("Two-factor authentication required.");
                Console.WriteLine($"Enter the code you received on your device with number: {verification?.TrustedPhoneNumber}");
                var code = Console.ReadLine();
                var result = await api.Validate2faCode(code);
                Console.WriteLine($"Code validation result: {result}");

                if (!result)
                {
                    Console.WriteLine("Failed to verify security code");
                    return;
                }
            }

            if (api.Requires2sa)
            {
                Console.WriteLine("Two-step authentication required. Your trusted devices are:");
                var devices = await api.GetTrustedDevices();

                var i = 0;
                foreach (var device in devices)
                {
                    Console.WriteLine(i + ") " + (device.DeviceName ?? $"SMS to {device.PhoneNumber}"));
                    i++;
                }

                Console.WriteLine("Which device to use?");
                if (!int.TryParse(Console.ReadLine(), out var choice))
                {
                    choice = 1;
                }

                var chosen = devices.ElementAtOrDefault(choice - 1) ?? devices.First();
                if (!await api.SendVerificationCode(chosen))
                {
                    Console.WriteLine("Failed to send verification code");
                }

                Console.WriteLine("Please enter validation code: ");
                var code = Console.ReadLine();
                var result = await api.ValidateVerificationCode(chosen, code);
                Console.WriteLine("Verification result " + result);
            }

            File.WriteAllText("session.json", api.Session.Serialize());
        }

        var driveApi = api.Drive();
        var root = await driveApi.GetRoot();
        var children = await root.GetChildren();

        var photosApi = api.Photos();

        await photosApi.Init();
        var albums = await photosApi.Albums();

        Directory.CreateDirectory(@"contents\live");
        Directory.CreateDirectory(@"contents\jpg");
        var downloader = new HttpClient();

        var album = albums.First();

        System.Collections.Concurrent.ConcurrentQueue<PhotoAsset> files = new System.Collections.Concurrent.ConcurrentQueue<PhotoAsset>();

        var namesWithSize = new HashSet<string>();
        var chunkNo = 0;

        await foreach (var chunk in album.GetPhotos(0))
        {
            chunkNo++;
            foreach (var photo in chunk)
            {
                if (!namesWithSize.Add(photo.FileName + "-" + photo.Size))
                {
                    var existing = files.FirstOrDefault(f => f.FileName == photo.FileName && f.Size == photo.Size);
                    var link1 = photo.Original.DownloadURL;
                    var link2 = existing.Original.DownloadURL;
                }
                files.Enqueue(photo);
            }
            Console.WriteLine("Got: " + files.Count);
        }

        await Parallel.ForEachAsync(Enumerable.Range(0, 34), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (chunk, _) =>
        {
            await foreach (var photo in albums.First()
                .GetPhotos(chunk * 1000)
                .SelectMany(photos => photos.ToAsyncEnumerable())
                .Take(1000))
            {
                files.Enqueue(photo);
                Console.WriteLine(files.Count + " " + (files.Count / 33654.0 * 100));
            }
        });

        var photos = files.ToList();
        foreach (var photo in photos)
        {
            KeepAlive();
            try
            {
                Console.Write(photo.FileName + " / " + photo.Size);
                var path = @"contents\" + photo.FileName;
                var fileInfo = new FileInfo(path);
                var i = 0;
                var name = Path.GetFileNameWithoutExtension(path);
                var extension = Path.GetExtension(path);
                if (photo.OriginalAlt != null)
                {

                }

                while (fileInfo.Exists && fileInfo.Length != photo.Size)
                {
                    i++;
                    path = @$"contents\{name} {i}{extension}";
                    fileInfo = new FileInfo(path);
                }

                if (fileInfo.Exists && fileInfo.Length == photo.Size && fileInfo.LastWriteTime != photo.CreateDate)
                {

                }

                if (!fileInfo.Exists)
                {
                    using var stream = await downloader.GetStreamAsync(photo.Original.DownloadURL);
                    using var sw = new FileStream(path, FileMode.Create);
                    stream.CopyTo(sw);
                    sw.Close();
                }
                File.SetLastWriteTime(path, photo.CreateDate);

                var livePhotoFileName = Path.ChangeExtension(path.Replace(@"contents\", @"contents\live\"), ".mov");
                if (photo.LivePhoto != null && !File.Exists(livePhotoFileName))
                {
                    using var stream = await downloader.GetStreamAsync(photo.LivePhoto.DownloadURL);
                    using var sw = new FileStream(livePhotoFileName, FileMode.Create);
                    stream.CopyTo(sw);
                    sw.Close();
                    File.SetLastWriteTime(path, photo.CreateDate);
                }

                var jpgFileName = Path.ChangeExtension(path.Replace(@"contents\", @"contents\jpg\"), ".jpg");
                if (photo.JpegFull != null && !File.Exists(jpgFileName))
                {
                    using var stream = await downloader.GetStreamAsync(photo.JpegFull.DownloadURL);
                    using var sw = new FileStream(jpgFileName, FileMode.Create);
                    stream.CopyTo(sw);
                    sw.Close();
                    File.SetLastWriteTime(path, photo.CreateDate);
                }

                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
            }

            var downloadInfos = await driveApi.GetDownloadInfo(children.Where(child => child.Type == NodeType.File));

            var tasks = downloadInfos.Select(async downloadInfo =>
            {
                var fileName = Path.GetFileName(new Uri(downloadInfo.Data_token.Url).LocalPath);
                var stream = await driveApi.DownloadFile(downloadInfo);

                Console.Write("Downloading " + fileName + "...");
                using var sw = new FileStream(@"contents\" + fileName, FileMode.Create);
                stream.CopyTo(sw);
                sw.Close();
                Console.WriteLine(" Success");
            });

            await Task.WhenAll(tasks);
        }
    }
}
