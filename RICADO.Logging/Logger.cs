using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace RICADO.Logging
{
    public static class Logger
    {
        #region Private Locals

        private static ConcurrentDictionary<string, LogMessageTracking> _logMessageTracking = new ConcurrentDictionary<string, LogMessageTracking>();

        #endregion


        #region Public Properties

        public static LogLevel LogLevel { get; set; }

        #endregion


        #region Public Methods

        public static void Log(LogLevel logLevel, Exception exception, string message, params object[] args)
        {
            log(logLevel, exception, message, args);
        }

        public static void Log(LogLevel logLevel, string message, params object[] args)
        {
            log(logLevel, null, message, args);
        }

        public static void LogTrace(Exception exception, string message, params object[] args)
        {
            log(LogLevel.Trace, exception, message, args);
        }

        public static void LogTrace(string message, params object[] args)
        {
            log(LogLevel.Trace, null, message, args);
        }

        public static void LogTrace(Exception exception, params object[] args)
        {
            log(LogLevel.Trace, exception, null, args);
        }

        public static void LogDebug(Exception exception, string message, params object[] args)
        {
            log(LogLevel.Debug, exception, message, args);
        }

        public static void LogDebug(string message, params object[] args)
        {
            log(LogLevel.Debug, null, message, args);
        }

        public static void LogDebug(Exception exception, params object[] args)
        {
            log(LogLevel.Debug, exception, null, args);
        }

        public static void LogInformation(Exception exception, string message, params object[] args)
        {
            log(LogLevel.Information, exception, message, args);
        }

        public static void LogInformation(string message, params object[] args)
        {
            log(LogLevel.Information, null, message, args);
        }

        public static void LogInformation(Exception exception, params object[] args)
        {
            log(LogLevel.Information, exception, null, args);
        }

        public static void LogWarning(Exception exception, string message, params object[] args)
        {
            log(LogLevel.Warning, exception, message, args);
        }

        public static void LogWarning(string message, params object[] args)
        {
            log(LogLevel.Warning, null, message, args);
        }

        public static void LogWarning(Exception exception, params object[] args)
        {
            log(LogLevel.Warning, exception, null, args);
        }

        public static void LogError(Exception exception, string message, params object[] args)
        {
            log(LogLevel.Error, exception, message, args);
        }

        public static void LogError(string message, params object[] args)
        {
            log(LogLevel.Error, null, message, args);
        }

        public static void LogError(Exception exception, params object[] args)
        {
            log(LogLevel.Error, exception, null, args);
        }

        public static void LogCritical(Exception exception, string message, params object[] args)
        {
            log(LogLevel.Critical, exception, message, args);
        }

        public static void LogCritical(string message, params object[] args)
        {
            log(LogLevel.Critical, null, message, args);
        }

        public static void LogCritical(Exception exception, params object[] args)
        {
            log(LogLevel.Critical, exception, null, args);
        }

        #endregion


        #region Private Methods

        private static void log(LogLevel logLevel, Exception exception, string message, params object[] args)
        {
            if(LogLevel > logLevel || logLevel == LogLevel.None)
            {
                return;
            }

            StringBuilder messageBuilder = new StringBuilder();

            if(message != null && message.Length > 0)
            {
                if(args.Length > 0 && message.Contains('{') && message.Contains('}'))
                {
                    message = buildMessageWithArguments(message, args);
                }

                messageBuilder.Append(message);
            }

            if(exception != null)
            {
                if(messageBuilder.Length > 0)
                {
                    messageBuilder.Append(Environment.NewLine + Environment.NewLine);
                }

                messageBuilder.Append(getExceptionMessageContent(exception));

                messageBuilder.Append(Environment.NewLine);
            }

            if(messageBuilder.Length == 0)
            {
                return;
            }

            StackTrace stackTrace = new StackTrace();

            string className = getStackTraceClassName(stackTrace);

            string methodName = getStackTraceMethodName(stackTrace);

            if(isLogMessageAllowed(messageBuilder.ToString(), logLevel, className, methodName) == false)
            {
                return;
            }

            TextWriter textWriter = logLevel == LogLevel.Error || logLevel == LogLevel.Critical ? Console.Error : Console.Out;

            textWriter.WriteLine("[{0}] {1} -> {2} :: {3} :: {4}", DateTime.Now.ToString("s"), className, methodName, getLogLevelString(logLevel), messageBuilder.ToString());

            trackLogMessage(messageBuilder.ToString(), logLevel, className, methodName);

            if(LogManager.HasConfiguredBugsnagClient == false)
            {
                return;
            }

            try
            {
                if (message != null && message.Length > 0 && logLevel >= LogLevel.Information && logLevel != LogLevel.None)
                {
                    Dictionary<string, string> breadcrumbMetadata = new Dictionary<string, string>();

                    breadcrumbMetadata.Add("message", message);
                    breadcrumbMetadata.Add("className", className);
                    breadcrumbMetadata.Add("methodName", methodName);

                    LogManager.BugsnagClient.Breadcrumbs.Leave("LogLevel." + logLevel.ToString(), Bugsnag.BreadcrumbType.Log, breadcrumbMetadata);
                }

                if (exception != null)
                {
                    if (logLevel == LogLevel.Information)
                    {
                        LogManager.BugsnagClient.Notify(exception, Bugsnag.Severity.Info);
                    }
                    else if (logLevel == LogLevel.Warning)
                    {
                        LogManager.BugsnagClient.Notify(exception, Bugsnag.Severity.Warning);
                    }
                    else if (logLevel == LogLevel.Error || logLevel == LogLevel.Critical)
                    {
                        LogManager.BugsnagClient.Notify(exception, Bugsnag.Severity.Error);
                    }
                }
            }
            catch
            {
            }
        }
        
        private static string getLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Critical => "critical",
                LogLevel.Debug => "debug",
                LogLevel.Error => "error",
                LogLevel.Information => "information",
                LogLevel.Trace => "trace",
                LogLevel.Warning => "warning",
                _ => "unknown",
            };
        }

        private static bool isLogMessageAllowed(string message, LogLevel logLevel, string className, string methodName)
        {
            string signature = getLogMessageSignature(message, logLevel, className, methodName);

            if (_logMessageTracking.ContainsKey(signature) == false)
            {
                return true;
            }

            LogMessageTracking tracking;

            if (_logMessageTracking.TryGetValue(signature, out tracking))
            {
                if (DateTime.Now.Subtract(tracking.Timestamp).TotalSeconds >= 10) // TODO: Make this configurable
                {
                    _logMessageTracking.TryRemove(signature, out tracking);

                    return true;
                }
            }

            if (_logMessageTracking.TryGetValue(signature, out tracking))
            {
                if (tracking.Count > 3) // TODO: Make this configurable
                {
                    return false;
                }
            }

            return true;
        }

        private static void trackLogMessage(string message, LogLevel logLevel, string className, string methodName)
        {
            string signature = getLogMessageSignature(message, logLevel, className, methodName);

            LogMessageTracking tracking;

            if (_logMessageTracking.TryGetValue(signature, out tracking) == false)
            {
                tracking = new LogMessageTracking()
                {
                    Signature = signature,
                    Timestamp = DateTime.Now,
                    Count = 0,
                };
            }

            tracking.Count++;

            _logMessageTracking.AddOrUpdate(signature, tracking, (key, oldtracking) =>
            {
                return tracking;
            });
        }

        private static string getLogMessageSignature(string message, LogLevel logLevel, string className, string methodName)
        {
            string signature = "";

            if (className != null)
            {
                signature += className + "-";
            }
            else
            {
                signature += "NOCLASS-";
            }

            if (methodName != null)
            {
                signature += methodName + "-";
            }
            else
            {
                signature += "NOMETHOD-";
            }

            if (message != null)
            {
                signature += message + "-";
            }
            else
            {
                signature += "NOMESSAGE-";
            }

            signature += getLogLevelString(logLevel);

            return signature;
        }

        private static string getStackTraceClassName(StackTrace stackTrace)
        {
            string unknownClassName = "UnknownClass";

            if (stackTrace.FrameCount < 2)
            {
                return unknownClassName;
            }

            StackFrame stackFrame = stackTrace.GetFrame(2);

            if (stackFrame == null || stackFrame.HasMethod() == false)
            {
                return unknownClassName;
            }

            MethodBase methodBase = stackFrame.GetMethod();

            if (methodBase == null || methodBase.DeclaringType == null || methodBase.DeclaringType.Name == null || methodBase.DeclaringType.Name.Length == 0)
            {
                return unknownClassName;
            }

            return methodBase.DeclaringType.Name;
        }

        private static string getStackTraceMethodName(StackTrace stackTrace)
        {
            string unknownMethodName = "UnknownMethod";

            if (stackTrace.FrameCount < 2)
            {
                return unknownMethodName;
            }

            StackFrame stackFrame = stackTrace.GetFrame(2);

            if (stackFrame == null || stackFrame.HasMethod() == false)
            {
                return unknownMethodName;
            }

            MethodBase methodBase = stackFrame.GetMethod();

            if (methodBase == null || methodBase.Name == null || methodBase.Name.Length == 0)
            {
                return unknownMethodName;
            }

            return methodBase.Name;
        }

        private static string getExceptionMessageContent(Exception exception)
        {
            if(exception == null)
            {
                return null;
            }
            
            try
            {
                StackTrace stackTrace = new StackTrace(exception);
                
                string message = "Exception: " + exception.Message + Environment.NewLine + Environment.NewLine;

                message += "Stack Trace: ";

                int stackCount = 0;

                foreach (StackFrame stackFrame in stackTrace.GetFrames().Reverse())
                {
                    MethodBase method = stackFrame.GetMethod();

                    if (method != null)
                    {
                        message += method.DeclaringType.FullName.Replace('+', '.');
                        message += ".";
                        message += method.Name;
                        message += "(";

                        bool addComma = true;
                        ParameterInfo[] parameters = method.GetParameters();

                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (addComma == false)
                            {
                                message += ", ";
                            }
                            else
                            {
                                addComma = false;
                            }

                            string name = "<UnknownType>";

                            if (parameters[i].ParameterType != null)
                            {
                                name = parameters[i].ParameterType.Name;
                            }

                            message += name + " " + parameters[i].Name;
                        }

                        message += ")";

                        if (stackCount < (stackTrace.GetFrames().Count() - 1))
                        {
                            message += " -> ";
                        }

                        stackCount++;
                    }
                }
            }
            catch
            {
            }

            return exception.ToString();
        }

        private static string buildMessageWithArguments(string message, params object[] args)
        {
            StringBuilder builder = new StringBuilder();
            
            Queue<object> argumentItems = new Queue<object>(args);

            foreach(string messageSegment in Regex.Split(message, "{[A-Za-z0-9]+}"))
            {
                if(messageSegment.Length > 0)
                {
                    builder.Append(messageSegment);
                }

                object argumentItem = null;

                if (argumentItems.Count > 0)
                {
                    argumentItem = argumentItems.Dequeue();
                }

                if (argumentItem != null)
                {
                    builder.Append(argumentItem);
                }
            }

            return builder.ToString();
        }

        #endregion


        #region Structs

        public struct LogMessageTracking
        {
            public DateTime Timestamp;
            public int Count;
            public string Signature;
        }

        #endregion
    }
}
