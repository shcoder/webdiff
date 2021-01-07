using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using NDesk.Options;
using Newtonsoft.Json;
using OpenQA.Selenium.Remote;
using webdiff.driver;
using webdiff.http;
using webdiff.img;
using webdiff.utils;
using Cookie = OpenQA.Selenium.Cookie;

namespace webdiff
{
    internal static partial class Program
	{

        private static int Main(string[] args)
        {
            var session = new Session();
            session.LoadConfiguration(args);
			if (session.Error != Error.None)
                return (int)session.Error;

            using var processor = new Processor(session);
            if (!processor.Init())
                return (int)session.Error;
			if (!processor.LoadCookies())
                return (int)session.Error;

            try
			{
				processor.RunChecks();
            }
			catch(Exception e)
			{
				Console.Error.WriteLine(e);
				return (int)Error.CestLaVie;
			}

            return 0;
        }
	}
}