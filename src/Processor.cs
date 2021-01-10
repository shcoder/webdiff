using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenQA.Selenium.Remote;
using webdiff.driver;
using webdiff.http;
using webdiff.img;
using webdiff.utils;

namespace webdiff
{
    public struct Driver
    {
        public Uri BaseUri;
        public RemoteWebDriver RemoteDriver;

        public Driver(Uri baseUri, RemoteWebDriver remoteDriver = null)
        {
            BaseUri = baseUri;
            RemoteDriver = remoteDriver;
        }

        public void Deconstruct(out Uri baseUri, out RemoteWebDriver remoteDriver)
        {
            baseUri = BaseUri;
            remoteDriver = RemoteDriver;
        }
    }

    internal class Processor: IDisposable
    {
        private const string ImgRelativePath = "img";

        private readonly Session session;
        private readonly ILogger<Processor> log;
        private DateTime Started;
        private string ResultsPath;
        private string ResultsImgPath;
        private Driver[] drivers;


        public Processor(Session session, ILogger<Processor> log)
        {
            this.session = session;
            this.log = log;
        }

        public bool Init()
        {
            Started = DateTime.Now;

            if (!InitFolders())
                return false;

            if (!InitDrivers())
                return false;

            return true;
        }

        private bool InitDrivers()
        {
            drivers = new[] {session.SvcLeft, session.SvcRight}
                .AsParallel().AsOrdered()
                .Select(uri =>
                {
                    RemoteWebDriver driver = null;
                    try
                    {
                        driver = webdiff.driver.Startup.StartNewDriver(session.Settings.Driver, session.Settings.Mobile)
                            .SetWindowSettings(session.Settings.Window);
                        log.LogInformation($"Started driver");// , {driver.Capabilities}
                        driver.Url = uri.ToString();
                    }
                    catch (Exception e)
                    {
                        log.LogError($"Failed to start driver: {e.Message}");
                    }

                    return new Driver(uri, driver);
                })
                .ToArray();

            if (drivers.Any(driver => driver.RemoteDriver == null))
            {
                session.Error = Error.CestLaVie;
                return false;
            }

            return true;
        }

        private bool InitFolders()
        {
            try
            {
                ResultsPath = Path.Combine(session.Output, session.Started.ToString("yyyyMMdd-HHmmss"))
                    .RelativeToBaseDirectory();
                ResultsImgPath = Path.Combine(ResultsPath, ImgRelativePath);

                Directory.CreateDirectory(ResultsPath);
                Directory.CreateDirectory(ResultsImgPath);
            }
            catch (Exception e)
            {
                log.LogError($"Failed to create output directory '{ResultsPath}': {e.Message}");
                session.Error = Error.CestLaVie;
                return false;
            }



            return true;
        }

        public bool LoadCookies()
        {
            session.LoadCookies();

            if (session.Error != Error.None)
                return false;

            session.Cookies?.ForEach(cookie =>
                drivers
                    .Select(item => item.RemoteDriver)
                    .ForEach(driver => driver.Manage().Cookies.AddCookie(cookie)));

            return true;
        }

        public void RunChecks()
        {
            var results = new Results
            {
                Started = session.Started,
                LeftBase = session.SvcLeft,
                RightBase = session.SvcRight,
                Profile = session.Profile,
                Diffs = new List<Diff>()
            };

            int errors =
                (session.Input == null ? Console.In.ReadLines() : File.ReadLines(session.Input.RelativeToBaseDirectory()))
                .Select(line => line.Trim())
                .Where(line => line != string.Empty)
                .Where(line => !line.StartsWith("#", StringComparison.Ordinal))
                .Select((line, idx) => Cmp(session, drivers, line, idx, results))
                .Count(res => !res);

            results.Ended = DateTime.Now;
            results.Elapsed = results.Ended - results.Started;

            File.Copy(session.Template.RelativeToBaseDirectory(), Path.Combine(ResultsPath, HtmlReportFilename));
            File.WriteAllText(Path.Combine(ResultsPath, JsResultsFilename), $"{JsRenderFunctionName}({JsonConvert.SerializeObject(results, Formatting.Indented, new SizeConverter(), new RectangleConverter())});{Environment.NewLine}");

            //Console.WriteLine();

            var result = errors == 0;
            WriteResult(result, result ? "ALL SAME" : $"DIFFERS ({errors})");
        }

        //TODO: Errors handling and refactoring
        private bool Cmp(Session session, Driver[] items, string relative, int idx, Results results)
        {
            var settings = session.Settings;
            var isScript = StringUtils.RemovePrefix(ref relative, "EXEC ");

            var pages = items.AsParallel().AsOrdered().Select(item =>
            {
                var (baseUri, driver) = item;

                if (isScript)
                    driver.ExecuteScript(relative);
                else
                    driver.Navigate().GoToUrl(new Uri(baseUri, relative));

                if (settings.Script.OnLoad != null)
                    driver.ExecuteScript(settings.Script.OnLoad);

                driver.Wait(settings.WaitUntil);

                return (Http: driver.GetHttpResponse(), Bmp: driver.GetVertScrollScreenshot(settings));
            }).ToArray();

            (HttpResponse Http, Bitmap Bmp) pageLeft = pages[0], pageRight = pages[1];
            Bitmap imgLeft = pageLeft.Bmp, imgRight = pageRight.Bmp, diff;

            int pixels;
            var result = ImageDiff.CompareImages(settings, imgLeft, imgRight, out diff);
            var areSame = (pixels = result.Unmatched) <= settings.Compare.PixelsThreshold || result.Map == null || result.Map.Count == 0;

            var match = 1.0 - (double)pixels / diff.Width / diff.Height;

            WriteResult(areSame, areSame ? "Same: " : "Diff: ", relative);
            log.LogInformation($"      Match {match:P1} ({pixels} / {diff.Width * diff.Height} pixels)");

            string leftName = null, rightName = null, diffName = null;

            var name = $"{idx:0000}-" + relative.Trim('/').ToSafeFilename();
            if (!areSame)
            {
                leftName = name + "-left.png";
                imgLeft.Save(Path.Combine(ResultsImgPath, leftName), ImageFormat.Png);

                rightName = name + "-right.png";
                imgRight.Save(Path.Combine(ResultsImgPath, rightName), ImageFormat.Png);

                diffName = name + "-diff.png";
                diff.Save(Path.Combine(ResultsImgPath, diffName), ImageFormat.Png);
            }

            results.TotalCount++;
            if (areSame) results.SameCount++;
            else results.DiffCount++;

            results.Diffs.Add(new Diff
            {
                Relative = relative,
                AreSame = areSame,
                UnmatchedPixels = result.Unmatched,
                TotalPixels = diff.Width * diff.Height,
                Match = match,
                Left = new Page
                {
                    Url = isScript ? null : new Uri(session.SvcLeft, relative),
                    Response = pageLeft.Http,
                    Img = new Img
                    {
                        Filename = leftName,
                        Src = leftName == null ? null : Path.Combine(ImgRelativePath, WebUtility.UrlEncode(leftName)),
                        Size = pageLeft.Bmp.Size
                    }
                },
                Right = new Page
                {
                    Url = isScript ? null : new Uri(session.SvcRight, relative),
                    Response = pageRight.Http,
                    Img = new Img
                    {
                        Filename = rightName,
                        Src = rightName == null ? null : Path.Combine(ImgRelativePath, WebUtility.UrlEncode(rightName)),
                        Size = pageRight.Bmp.Size
                    }
                },
                DiffImg = new Img
                {
                    Filename = diffName,
                    Src = diffName == null ? null : Path.Combine(ImgRelativePath, WebUtility.UrlEncode(diffName)),
                    Size = diff.Size
                },
                DiffMap = result.Map
            });

            return areSame;
        }

        private void WriteResult(bool success, string prefix, string text = null)
        {
            var message = string.Concat(prefix, text);
            if (success)
                log.LogInformation(message);
            else 
                log.LogWarning(message);
        }

        private const string HtmlReportFilename = "index.html";
        private const string JsRenderFunctionName = "render";
        private const string JsResultsFilename = "results.js";


        public void Dispose()
        {
            drivers.AsParallel().ForAll(driver => driver.RemoteDriver?.Dispose());
        }
    }
}