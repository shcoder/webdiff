using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenQA.Selenium;

namespace webdiff.utils
{
	internal static class CookieParser
    {
        private static Type type;
        private static ConstructorInfo ctor;
        private static MethodInfo method;

        static CookieParser()
        {
            type = typeof(System.Net.Cookie).Assembly.GetType(CookieParserTypeName);
            ctor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault();
            method = type.GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Instance);
		}

        private static void Check()
        {
            if (type == null)
                throw new Exception($"Invalid runtime: type '{CookieParserTypeName}' not found");
            if (ctor == null)
                throw new Exception($"Invalid runtime: type '{CookieParserTypeName}' has no .ctor(string)");
            if (method == null)
                throw new Exception($"Invalid runtime: type '{CookieParserTypeName}' has no method Get()");
		}

		public static Cookie[] Parse(string filepath)
		{
			return File.ReadLines(filepath)
				.Select(line => line.Trim())
				.Where(line => line != string.Empty)
				.Select(ParseCookie)
				.Select(ToSeleniumCookie)
				.ToArray();
		}

		private static Cookie ToSeleniumCookie(this System.Net.Cookie cookie) =>
			new Cookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Expires != DateTime.MinValue ? (DateTime?)cookie.Expires : null);

		private static System.Net.Cookie ParseCookie(string cookie)
		{
			Check();
			var obj = ctor.Invoke(new object[] {cookie});
			return (System.Net.Cookie)method.Invoke(obj, null);
		}

		private const string CookieParserTypeName = "System.Net.CookieParser";
	}
}