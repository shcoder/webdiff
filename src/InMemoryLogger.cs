using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using Microsoft.Extensions.Logging;

namespace webdiff
{
    internal class InMemoryLoggerProvider : ILoggerProvider
    {
        private ConcurrentDictionary<string, InMemoryLogger> loggers;

        public InMemoryLoggerProvider()
        {
            loggers = new ConcurrentDictionary<string, InMemoryLogger>();
        }

        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return loggers.GetOrAdd(categoryName, key => new InMemoryLogger(key, this));
        }

        public IEnumerable<LogRecord> GetRecordsFromTms(DateTime tms)
        {
            return loggers.Values
                .AsParallel()
                .SelectMany(logger => logger.GetRecordsFromTms(tms));
        }
    }

    internal class InMemoryLogger : ILogger
    {
        private readonly string category;
        private readonly InMemoryLoggerProvider inMemoryLoggerProvider;
        private readonly BlockingCollection<LogRecord> records;

        public InMemoryLogger(string category, InMemoryLoggerProvider inMemoryLoggerProvider)
        {
            this.category = category;
            this.inMemoryLoggerProvider = inMemoryLoggerProvider;
            records = new();
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            records.Add(new LogRecord()
            {
                Tms = DateTime.UtcNow,
                Level = logLevel,
                EventId = eventId,
                Message = formatter(state, exception),
                Category = category,
                Exception = exception
            });
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public IEnumerable<LogRecord> GetRecordsFromTms(DateTime tms)
        {
            return records.Where(r => r.Tms > tms);
        }
    }

    internal record LogRecord
    {
        public DateTime Tms;
        public LogLevel Level;
        public EventId EventId;
        public string Message;
        public string Category;
        public Exception Exception;
    }

    internal static class InMemoryLoggerProviderExtension
    {
        internal static InMemoryLoggerProvider InMemoryLoggerProvider;
        public static ILoggingBuilder AddInMemory(this ILoggingBuilder builder)
        {
            InMemoryLoggerProvider = new InMemoryLoggerProvider();
            builder.AddProvider(InMemoryLoggerProvider);
            return builder;
        }
    }
}