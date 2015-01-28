﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;
using NzbDrone.Core.MediaFiles.MediaInfo;


namespace NzbDrone.Core.MediaFiles.EpisodeImport
{
    public interface IMakeImportDecision
    {
        List<ImportDecision> GetImportDecisions(List<String> videoFiles, Series series);
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Series series, ParsedEpisodeInfo folderInfo, bool sceneSource);
    }

    public class ImportDecisionMaker : IMakeImportDecision
    {
        private readonly IEnumerable<IRejectWithReason> _specifications;
        private readonly IParsingService _parsingService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly IDetectSample _detectSample;
        private readonly Logger _logger;

        public ImportDecisionMaker(IEnumerable<IRejectWithReason> specifications,
                                   IParsingService parsingService,
                                   IMediaFileService mediaFileService,
                                   IDiskProvider diskProvider,
                                   IVideoFileInfoReader videoFileInfoReader,
                                   IDetectSample detectSample,
                                   Logger logger)
        {
            _specifications = specifications;
            _parsingService = parsingService;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
            _videoFileInfoReader = videoFileInfoReader;
            _detectSample = detectSample;
            _logger = logger;
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Series series)
        {
            return GetImportDecisions(videoFiles, series, null, false);
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Series series, ParsedEpisodeInfo folderInfo, bool sceneSource)
        {
            var newFiles = _mediaFileService.FilterExistingFiles(videoFiles.ToList(), series);

            _logger.Debug("Analyzing {0}/{1} files.", newFiles.Count, videoFiles.Count());

            return GetDecisions(newFiles, series, folderInfo, sceneSource).ToList();
        }

        private IEnumerable<ImportDecision> GetDecisions(List<string> videoFiles, Series series, ParsedEpisodeInfo folderInfo, bool sceneSource)
        {
            var shouldUseFolderName = ShouldUseFolderName(videoFiles, series, folderInfo);

            foreach (var file in videoFiles)
            {
                ImportDecision decision = null;

                try
                {
                    var localEpisode = _parsingService.GetLocalEpisode(file, series, shouldUseFolderName ? folderInfo : null, sceneSource);

                    if (localEpisode != null)
                    {
                        localEpisode.Quality = GetQuality(folderInfo, localEpisode.Quality, series);
                        localEpisode.Size = _diskProvider.GetFileSize(file);

                        _logger.Debug("Size: {0}", localEpisode.Size);

                        //TODO: make it so media info doesn't ruin the import process of a new series
                        if (sceneSource)
                        {
                            localEpisode.MediaInfo = _videoFileInfoReader.GetMediaInfo(file);
                        }
                        
                        decision = GetDecision(localEpisode);
                    }

                    else
                    {
                        localEpisode = new LocalEpisode();
                        localEpisode.Path = file;

                        decision = new ImportDecision(localEpisode, "Unable to parse file");
                    }
                }
                catch (EpisodeNotFoundException e)
                {
                    var localEpisode = new LocalEpisode();
                    localEpisode.Path = file;

                    decision = new ImportDecision(localEpisode, e.Message);
                }
                catch (Exception e)
                {
                    _logger.ErrorException("Couldn't import file. " + file, e);
                }

                if (decision != null)
                {
                    yield return decision;
                }
            }
        }

        private ImportDecision GetDecision(LocalEpisode localEpisode)
        {
            var reasons = _specifications.Select(c => EvaluateSpec(c, localEpisode))
                                         .Where(c => c.IsNotNullOrWhiteSpace());

            return new ImportDecision(localEpisode, reasons.ToArray());
        }

        private string EvaluateSpec(IRejectWithReason spec, LocalEpisode localEpisode)
        {
            try
            {
                if (spec.RejectionReason.IsNullOrWhiteSpace())
                {
                    throw new InvalidOperationException("[Need Rejection Text]");
                }

                var generalSpecification = spec as IImportDecisionEngineSpecification;

                if (generalSpecification != null && !generalSpecification.IsSatisfiedBy(localEpisode))
                {
                    return spec.RejectionReason;
                }
            }
            catch (Exception e)
            {
                //e.Data.Add("report", remoteEpisode.Report.ToJson());
                //e.Data.Add("parsed", remoteEpisode.ParsedEpisodeInfo.ToJson());
                _logger.ErrorException("Couldn't evaluate decision on " + localEpisode.Path, e);
                return String.Format("{0}: {1}", spec.GetType().Name, e.Message);
            }

            return null;
        }

        private bool ShouldUseFolderName(List<string> videoFiles, Series series, ParsedEpisodeInfo folderInfo)
        {
            if (folderInfo == null)
            {
                return false;
            }

            if (folderInfo.FullSeason)
            {
                return false;
            }

            return videoFiles.Count(file =>
            {
                var size = _diskProvider.GetFileSize(file);
                var fileQuality = QualityParser.ParseQuality(file);
                var sample = _detectSample.IsSample(series, GetQuality(folderInfo, fileQuality, series), file, size, folderInfo.SeasonNumber);

                if (sample)
                {
                    return false;
                }

                return true;
            }) == 1;
        }

        private QualityModel GetQuality(ParsedEpisodeInfo folderInfo, QualityModel fileQuality, Series series)
        {
            if (folderInfo != null &&
                            new QualityModelComparer(series.Profile).Compare(folderInfo.Quality,
                                fileQuality) > 0)
            {
                _logger.Debug("Using quality from folder: {0}", folderInfo.Quality);
                return folderInfo.Quality;
            }

            return fileQuality;
        }
    }
}
