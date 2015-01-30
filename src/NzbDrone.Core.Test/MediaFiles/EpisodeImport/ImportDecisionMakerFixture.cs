﻿using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using FizzWare.NBuilder;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport
{
    [TestFixture]
    public class ImportDecisionMakerFixture : CoreTest<ImportDecisionMaker>
    {
        private List<string> _videoFiles;
        private LocalEpisode _localEpisode;
        private Series _series;
        private QualityModel _quality;

        private Mock<IImportDecisionEngineSpecification> _pass1;
        private Mock<IImportDecisionEngineSpecification> _pass2;
        private Mock<IImportDecisionEngineSpecification> _pass3;

        private Mock<IImportDecisionEngineSpecification> _fail1;
        private Mock<IImportDecisionEngineSpecification> _fail2;
        private Mock<IImportDecisionEngineSpecification> _fail3;

        [SetUp]
        public void Setup()
        {
            _pass1 = new Mock<IImportDecisionEngineSpecification>();
            _pass2 = new Mock<IImportDecisionEngineSpecification>();
            _pass3 = new Mock<IImportDecisionEngineSpecification>();

            _fail1 = new Mock<IImportDecisionEngineSpecification>();
            _fail2 = new Mock<IImportDecisionEngineSpecification>();
            _fail3 = new Mock<IImportDecisionEngineSpecification>();

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>())).Returns(true);
            _pass1.Setup(c => c.RejectionReason).Returns("_pass1");

            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>())).Returns(true);
            _pass2.Setup(c => c.RejectionReason).Returns("_pass2");

            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>())).Returns(true);
            _pass3.Setup(c => c.RejectionReason).Returns("_pass3");


            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>())).Returns(false);
            _fail1.Setup(c => c.RejectionReason).Returns("_fail1");

            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>())).Returns(false);
            _fail2.Setup(c => c.RejectionReason).Returns("_fail2");

            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>())).Returns(false);
            _fail3.Setup(c => c.RejectionReason).Returns("_fail3");

            _series = Builder<Series>.CreateNew()
                                     .With(e => e.Profile = new Profile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                     .Build();

            _quality = new QualityModel(Quality.DVD);

            _localEpisode = new LocalEpisode
            { 
                Series = _series,
                Quality = _quality,
                Path = @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV.avi"
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(c => c.GetLocalEpisode(It.IsAny<String>(), It.IsAny<Series>(), It.IsAny<Boolean>(), It.IsAny<ParsedEpisodeInfo>()))
                  .Returns(_localEpisode);

            GivenVideoFiles(new List<string> { @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV.avi".AsOsAgnostic() });
        }

        private void GivenSpecifications(params Mock<IImportDecisionEngineSpecification>[] mocks)
        {
            Mocker.SetConstant<IEnumerable<IRejectWithReason>>(mocks.Select(c => c.Object));
        }

        private void GivenVideoFiles(List<string> videoFiles)
        {
            _videoFiles = videoFiles;

            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.FilterExistingFiles(_videoFiles, It.IsAny<Series>()))
                .Returns(_videoFiles);
        }

        [Test]
        public void should_call_all_specifications()
        {
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetImportDecisions(_videoFiles, new Series(), false, null);

            _fail1.Verify(c => c.IsSatisfiedBy(_localEpisode), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(_localEpisode), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(_localEpisode), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(_localEpisode), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(_localEpisode), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(_localEpisode), Times.Once());
        }

        [Test]
        public void should_return_rejected_if_single_specs_fail()
        {
            GivenSpecifications(_fail1);

            var result = Subject.GetImportDecisions(_videoFiles, new Series());

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_of_specs_fail()
        {
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_videoFiles, new Series());

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_pass_if_all_specs_pass()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_videoFiles, new Series());

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetImportDecisions(_videoFiles, new Series());
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_blowup_the_process_due_to_failed_parse()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IParsingService>()
                  .Setup(c => c.GetLocalEpisode(It.IsAny<String>(), It.IsAny<Series>(), It.IsAny<Boolean>(), It.IsAny<ParsedEpisodeInfo>()))
                  .Throws<TestException>();

            _videoFiles = new List<String>
                {
                    "The.Office.S03E115.DVDRip.XviD-OSiTV",
                    "The.Office.S03E115.DVDRip.XviD-OSiTV",
                    "The.Office.S03E115.DVDRip.XviD-OSiTV"
                };

            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.FilterExistingFiles(_videoFiles, It.IsAny<Series>()))
                .Returns(_videoFiles);

            Subject.GetImportDecisions(_videoFiles, _series);

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<String>(), It.IsAny<Series>(), It.IsAny<Boolean>(), It.IsAny<ParsedEpisodeInfo>()), Times.Exactly(_videoFiles.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        [Test]
        public void should_use_file_quality_if_folder_quality_is_null()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            var expectedQuality = QualityParser.ParseQuality(_videoFiles.Single());

            var result = Subject.GetImportDecisions(_videoFiles, _series);

            result.Single().LocalEpisode.Quality.Should().Be(expectedQuality);
        }

        [Test]
        public void should_use_file_quality_if_folder_quality_is_lower_than_file_quality()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            var expectedQuality = QualityParser.ParseQuality(_videoFiles.Single());

            var result = Subject.GetImportDecisions(_videoFiles, _series, true, new ParsedEpisodeInfo{Quality = new QualityModel(Quality.SDTV)});

            result.Single().LocalEpisode.Quality.Should().Be(expectedQuality);
        }

        [Test]
        public void should_use_folder_quality_when_it_is_greater_than_file_quality()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            var expectedQuality = new QualityModel(Quality.Bluray1080p);

            var result = Subject.GetImportDecisions(_videoFiles, _series, true, new ParsedEpisodeInfo { Quality = expectedQuality });

            result.Single().LocalEpisode.Quality.Should().Be(expectedQuality);
        }

        [Test]
        public void should_not_throw_if_episodes_are_not_found()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IParsingService>()
                  .Setup(c => c.GetLocalEpisode(It.IsAny<String>(), It.IsAny<Series>(), It.IsAny<Boolean>(), It.IsAny<ParsedEpisodeInfo>()))
                  .Throws(new EpisodeNotFoundException("Episode not found"));

            _videoFiles = new List<String>
                {
                    "The.Office.S03E115.DVDRip.XviD-OSiTV",
                    "The.Office.S03E115.DVDRip.XviD-OSiTV",
                    "The.Office.S03E115.DVDRip.XviD-OSiTV"
                };

            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.FilterExistingFiles(_videoFiles, It.IsAny<Series>()))
                .Returns(_videoFiles);

            Subject.GetImportDecisions(_videoFiles, _series);

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<String>(), It.IsAny<Series>(), It.IsAny<Boolean>(), It.IsAny<ParsedEpisodeInfo>()), Times.Exactly(_videoFiles.Count));
        }

        [Test]
        public void should_not_use_folder_for_full_season()
        {
            var videoFiles = new[]
                             {
                                 @"C:\Test\Unsorted\Series.Title.S01\S01E01.mkv".AsOsAgnostic(),
                                 @"C:\Test\Unsorted\Series.Title.S01\S01E02.mkv".AsOsAgnostic(),
                                 @"C:\Test\Unsorted\Series.Title.S01\S01E03.mkv".AsOsAgnostic()
                             };

            GivenSpecifications(_pass1);
            GivenVideoFiles(videoFiles.ToList());

            var folderInfo = Parser.Parser.ParseTitle("Series.Title.S01");

            Subject.GetImportDecisions(_videoFiles, _series, true, folderInfo);

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<string>(), It.IsAny<Series>(), true, null), Times.Exactly(3));

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<string>(), It.IsAny<Series>(), true, It.Is<ParsedEpisodeInfo>(p => p != null)), Times.Never());
        }

        [Test]
        public void should_not_use_folder_when_it_contains_more_than_one_valid_video_file()
        {
            var videoFiles = new[]
                             {
                                 @"C:\Test\Unsorted\Series.Title.S01E01\S01E01.mkv".AsOsAgnostic(),
                                 @"C:\Test\Unsorted\Series.Title.S01E01\1x01.mkv".AsOsAgnostic()
                             };

            GivenSpecifications(_pass1);
            GivenVideoFiles(videoFiles.ToList());

            var folderInfo = Parser.Parser.ParseTitle("Series.Title.S01E01");

            Subject.GetImportDecisions(_videoFiles, _series, true, folderInfo);

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<string>(), It.IsAny<Series>(), true, null), Times.Exactly(2));

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<string>(), It.IsAny<Series>(), true, It.Is<ParsedEpisodeInfo>(p => p != null)), Times.Never());
        }

        [Test]
        public void should_use_folder_when_only_one_video_file()
        {
            GivenSpecifications(_pass1);

            var folderInfo = Parser.Parser.ParseTitle("Series.Title.S01E01");

            Subject.GetImportDecisions(_videoFiles, _series, true, folderInfo);

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<string>(), It.IsAny<Series>(), true, It.IsAny<ParsedEpisodeInfo>()), Times.Exactly(1));

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<string>(), It.IsAny<Series>(), true, null), Times.Never());
        }

        [Test]
        public void should_use_folder_when_only_one_video_file_and_a_sample()
        {
            var videoFiles = new[]
                             {
                                 @"C:\Test\Unsorted\Series.Title.S01E01\S01E01.mkv".AsOsAgnostic(),
                                 @"C:\Test\Unsorted\Series.Title.S01E01\S01E01.sample.mkv".AsOsAgnostic()
                             };

            GivenSpecifications(_pass1);
            GivenVideoFiles(videoFiles.ToList());

            Mocker.GetMock<ISampleService>()
                  .Setup(s => s.IsSample(_series, It.IsAny<QualityModel>(), It.Is<string>(c => c.Contains("sample")), It.IsAny<long>(), It.IsAny<int>()))
                  .Returns(true);

            var folderInfo = Parser.Parser.ParseTitle("Series.Title.S01E01");

            Subject.GetImportDecisions(_videoFiles, _series, true, folderInfo);

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<string>(), It.IsAny<Series>(), true, It.IsAny<ParsedEpisodeInfo>()), Times.Exactly(2));

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.GetLocalEpisode(It.IsAny<string>(), It.IsAny<Series>(), true, null), Times.Never());
        }
    }
}