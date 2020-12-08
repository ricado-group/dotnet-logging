using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Bugsnag;

namespace RICADO.Logging
{
    public static class LogManager
    {
        #region Private Properties

        private static bool _initialized = false;
        private static object _initializedLock = new object();
        
        private static Client _bugsnagClient;
        private static object _bugsnagClientLock = new object();

        private static Configuration _bugsnagConfiguration;
        private static object _bugsnagConfigurationLock = new object();

        #endregion


        #region Public Properties

        public static bool IsInitialized
        {
            get
            {
                lock(_initializedLock)
                {
                    return _initialized;
                }
            }
            private set
            {
                lock(_initializedLock)
                {
                    _initialized = value;
                }
            }
        }
        
        public static string Environment { get; internal set; }

        public static Client BugsnagClient
        {
            get
            {
                lock(_bugsnagClientLock)
                {
                    return _bugsnagClient;
                }
            }
            internal set
            {
                lock(_bugsnagClientLock)
                {
                    _bugsnagClient = value;
                }
            }
        }

        public static Configuration BugsnagConfiguration
        {
            get
            {
                lock(_bugsnagConfigurationLock)
                {
                    return _bugsnagConfiguration;
                }
            }
            private set
            {
                lock(_bugsnagConfigurationLock)
                {
                    _bugsnagConfiguration = value;
                }
            }
        }

        public static bool HasConfiguredBugsnagClient
        {
            get
            {
                if(BugsnagClient != null && BugsnagClient.Configuration.ApiKey != null && BugsnagClient.Configuration.ApiKey.Length > 0)
                {
                    return true;
                }

                return false;
            }
        }

        #endregion


        #region Public Methods

        public static void Initialize(string logLevel, string environment, string projectRoot)
        {
            lock(_initializedLock)
            {
                if(_initialized == false)
                {
                    System.Threading.Tasks.TaskScheduler.UnobservedTaskException += taskSchedulerUnobservedTaskException;

                    Environment = environment;

                    LogLevel enumLogLevel = LogLevel.Information;

                    if (Enum.TryParse<LogLevel>(logLevel, true, out enumLogLevel) == false)
                    {
                        Logger.LogWarning("Initialized using Default *Information* Log Level. Unable to interpret provided Log Level '" + logLevel + "'");
                    }

                    Logger.LogLevel = enumLogLevel;

                    BugsnagConfiguration = new Configuration();

                    Version appVersion = Assembly.GetEntryAssembly().GetName().Version;

                    if (appVersion != null)
                    {
                        BugsnagConfiguration.AppVersion = appVersion.ToString(3);
                    }

                    BugsnagConfiguration.NotifyReleaseStages = new[] { "staging", "production" };

                    if (projectRoot != null)
                    {
                        BugsnagConfiguration.ProjectRoots = new[] { projectRoot };
                    }

                    if (environment != null && environment.Length > 0)
                    {
                        BugsnagConfiguration.ReleaseStage = environment.ToLower();
                    }

                    _initialized = true;
                }
            }
        }

        public static void ConfigureBugsnag(string apiKey, params string[] namespaces)
        {
            if(apiKey == null || apiKey.Length == 0)
            {
                return;
            }
            
            if(BugsnagConfiguration == null)
            {
                return;
            }

            BugsnagConfiguration.ApiKey = apiKey;

            if(namespaces != null && namespaces.Length > 0)
            {
                BugsnagConfiguration.ProjectNamespaces = namespaces;
            }

            updateBugsnagClient();
        }

        public static void AddBugsnagMetadata(string key, object value)
        {
            if(key == null || key.Length == 0 || value == null)
            {
                return;
            }

            if(BugsnagConfiguration.GlobalMetadata == null)
            {
                BugsnagConfiguration.GlobalMetadata = new KeyValuePair<string, object>[] { };
            }

            Dictionary<string, object> metadata = new Dictionary<string, object>();

            foreach(KeyValuePair<string, object> item in BugsnagConfiguration.GlobalMetadata)
            {
                if(metadata.ContainsKey(item.Key) == false)
                {
                    metadata.Add(item.Key, item.Value);
                }
            }

            if(metadata.ContainsKey(key))
            {
                metadata[key] = value;
            }
            else
            {
                metadata.Add(key, value);
            }

            BugsnagConfiguration.GlobalMetadata = metadata.ToArray();

            updateBugsnagClient();
        }

        public static void AddBugsnagIgnoredClass(Type classType)
        {
            if (classType == null || classType == typeof(Logger) || classType == typeof(LogManager))
            {
                return;
            }

            if (BugsnagConfiguration.IgnoreClasses == null)
            {
                BugsnagConfiguration.IgnoreClasses = new Type[] { };
            }

            List<Type> ignoreClassTypes = BugsnagConfiguration.IgnoreClasses.ToList<Type>();

            if (ignoreClassTypes.Contains(classType) == false)
            {
                ignoreClassTypes.Add(classType);
            }

            BugsnagConfiguration.IgnoreClasses = ignoreClassTypes.ToArray();

            updateBugsnagClient();
        }

        public static void Destroy()
        {
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= taskSchedulerUnobservedTaskException;

            lock (_bugsnagClientLock)
            {
                if(_bugsnagClient != null)
                {
                    _bugsnagClient = null;
                }
            }
        }

        #endregion


        #region Private Methods

        private static void updateBugsnagClient()
        {
            lock(_bugsnagClientLock)
            {
                if(_bugsnagClient != null)
                {
                    _bugsnagClient = null;
                }

                _bugsnagClient = new Client(BugsnagConfiguration);
            }
        }

        private static void taskSchedulerUnobservedTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            if (e != null)
            {
                try
                {
                    if(HasConfiguredBugsnagClient == true)
                    {
                        lock(_bugsnagClientLock)
                        {
                            _bugsnagClient.Notify(e.Exception, Bugsnag.Payload.HandledState.ForUnhandledException());
                        }
                    }
                    else
                    {
                        Logger.LogCritical(e.Exception, "Task Scheduler Unhandled Exception");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex);
                }
                finally
                {
                    e.SetObserved();
                }
            }
        }

        #endregion
    }
}
