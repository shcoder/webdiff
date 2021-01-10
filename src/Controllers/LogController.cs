using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using webdiff.DAO;

namespace webdiff.Controllers
{
    [Route("api/log")]
    [ApiController] 
    public class LogController : ControllerBase
    {
        [HttpGet]
        public Logs Get(DateTime tms = default, LogLevel minLelev = LogLevel.Debug)
        {
            var list = InMemoryLoggerProviderExtension
                .InMemoryLoggerProvider
                ?.GetRecordsFromTms(tms)
                .ToList()
                .Where(r => r.Level >= minLelev)
                .ToList();
            return new Logs()
            {
                Items = list ?? new List<LogRecord>(0),
                LastRecordTms = list?.Count > 0 ? list.Select(r => r.Tms).Max() : default
            };
        }
    }
}

