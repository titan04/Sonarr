﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Notifications.Xbmc
{
    public class Xbmc : NotificationBase<XbmcSettings>
    {
        private readonly IXbmcService _xbmcService;
        private readonly Logger _logger;

        public Xbmc(IXbmcService xbmcService, Logger logger)
        {
            _xbmcService = xbmcService;
            _logger = logger;
        }

        public override string Link
        {
            get { return "http://xbmc.org/"; }
        }

        public override void OnGrab(string message)
        {
            const string header = "Sonarr - Grabbed";

            Notify(Settings, header, message);
        }

        public override void OnDownload(DownloadMessage message)
        {
            const string header = "Sonarr - Downloaded";

            Notify(Settings, header, message.Message);
            UpdateAndClean(message.Series, message.OldFiles.Any());
        }

        public override void AfterRename(Series series)
        {
            UpdateAndClean(series);
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            failures.AddIfNotNull(_xbmcService.Test(Settings));

            return new ValidationResult(failures);
        }

        private void Notify(XbmcSettings settings, string header, string message)
        {
            try
            {
                if (Settings.Notify)
                {
                    _xbmcService.Notify(Settings, header, message);
                }
            }
            catch (SocketException ex)
            {
                var logMessage = String.Format("Unable to connect to XBMC Host: {0}:{1}", Settings.Host, Settings.Port);
                _logger.DebugException(logMessage, ex);
            }
        }

        private void UpdateAndClean(Series series, bool clean = true)
        {
            try
            {
                if (Settings.UpdateLibrary)
                {
                    _xbmcService.Update(Settings, series);
                }

                if (clean && Settings.CleanLibrary)
                {
                    _xbmcService.Clean(Settings);
                }
            }
            catch (SocketException ex)
            {
                var logMessage = String.Format("Unable to connect to XBMC Host: {0}:{1}", Settings.Host, Settings.Port);
                _logger.DebugException(logMessage, ex);
            }
        }
    }
}
