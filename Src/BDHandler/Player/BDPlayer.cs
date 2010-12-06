using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BDInfo;
using DirectShowLib;
using DShowNET.Helper;
using MediaPortal.Configuration;
using MediaPortal.GUI.Library;
using MediaPortal.Player;
using MediaPortal.Player.Subtitles;
using MediaPortal.Profile;
using System.Text.RegularExpressions;
using System.Globalization;
using MediaPortal.Plugins.BDHandler.Filters;

namespace MediaPortal.Plugins.BDHandler
{

    public class BDPlayer : VideoPlayerVMR9
    {
        public static double MinimalFullFeatureLength = 3000;

        public BDPlayer() : base(g_Player.MediaType.Video) { }

        public BDPlayer(g_Player.MediaType type)
            : base(type)
        { }

        /// <summary>
        /// Gets or sets the source filter that is to be forced when playing blurays
        /// </summary>
        /// <value>The source filter.</value>
        public IFilter SourceFilter
        {
            get { return this.sourceFilter; }
            set { this.sourceFilter = value; }
        } protected IFilter sourceFilter;

        /// <summary>
        /// Plays the specified file.
        /// </summary>
        /// <param name="strFile">filepath</param>
        /// <returns></returns>
        public override bool Play(string strFile)
        {
            string path = strFile.ToLower();

            if (strFile.Length < 4)
            {
                path = Path.Combine(strFile, @"BDMV\index.bdmv");
                strFile = path;
            }

            if (path.EndsWith(".bdmv") || path.EndsWith(".m2ts"))
            {
                // only continue with playback if a feature was selected or the extension was m2ts.
                bool play = doFeatureSelection(ref strFile);
                if (play)
                {
                    return base.Play(strFile);
                }
            }

            // if we get here we always return true because the user called the dialog and we don't 
            // want an error saying we couldn't play the file
            return true;
        }

        /// <summary>
        /// Gets the audio language from the stream
        /// </summary>
        /// <param name="iStream">stream index</param>
        /// <returns>the localized audio language</returns>
        public override string AudioLanguage(int iStream)
        {
            string englishName = GetAudioLanguageFromStream(iStream);
            return Util.Utils.TranslateLanguageString(englishName);
        }

        /// <summary>
        /// Gets the audio type from the stream
        /// </summary>
        /// <param name="iStream">stream index</param>
        /// <returns></returns>
        public override string AudioType(int iStream)
        {
            FilterStreamInfos info = FStreams.GetStreamInfos(StreamType.Audio, iStream);

            Guid guid = GetFilterGuidByStreamInfo(info);
            if (guid == this.sourceFilter.GUID)
            {
                // todo: filter dictionary lookup
                ISelectFilter filter = this.sourceFilter as ISelectFilter;
                if (filter != null)
                {
                    string type = filter.ParseAudioType(info.Name);
                    BDHandlerCore.LogDebug("AudioType() Filter: {0}, IN: {1} OUT: {2}", filter.Name, info.Name, type);
                    return type;
                }
            }
            else
            {
                BDHandlerCore.LogDebug("AudioType() Filter: Unknown, GUID={0}", guid.ToString());
            }

            // if we made it this far just do the native dance
            return base.AudioType(iStream); ;
        }

        /// <summary>
        /// Gets the subtitle language from the stream
        /// </summary>
        /// <param name="iStream"></param>
        /// <returns>the localized subtitle language</returns>
        public override string SubtitleLanguage(int iStream)
        {
            string englishName = GetSubtitleLanguageFromStream(iStream);
            return Util.Utils.TranslateLanguageString(englishName);
        }

        /// <summary>
        /// Gets the subtitle name from the stream.
        /// </summary>
        /// <param name="iStream">the subtitle name (description)</param>
        /// <returns></returns>
        public override string SubtitleName(int iStream)
        {
            string name = GetSubtitleNameFromStream(iStream);
            return name;
        }

        /// <summary>
        /// Makes sure we build a working graph for our bluray playlist
        /// </summary>
        /// <returns></returns>
        protected override bool GetInterfaces()
        {
            if (CurrentFile.ToLower().EndsWith(".mpls"))
                return renderGraph();

            return base.GetInterfaces();
        }

        /// <summary>
        /// Overriden OnInitialized that to improve audio/subtitle selection
        /// </summary>
        protected override void OnInitialized()
        {
            #region Improved Subtitle & Audio Selection

            // AUDIO
            CultureInfo audioCulture = GetCultureInfoFromSettings("movieplayer", "audiolanguage");
            string selectedAudioLanguage = string.Empty;
            int audioStreams = AudioStreams;
            for (int i = 0; i < audioStreams; i++)
            {
                string language = GetAudioLanguageFromStream(i);
                if (audioCulture.Matches(language))
                {
                    CurrentAudioStream = i;
                    selectedAudioLanguage = language;
                    BDHandlerCore.LogInfo("Selected active audio track language: {0} ({1})", audioCulture.EnglishName, i);
                    break;
                }
            }

            // SUBTITLES
            CultureInfo subtitleCulture = GetCultureInfoFromSettings("subtitles", "language");
            int subtitleStreams = SubtitleStreams;
            
            for (int i = 0; i < subtitleStreams; i++)
            {
                string language = GetSubtitleLanguageFromStream(i);
                if (subtitleCulture.Matches(language))
                {
                    // set the current subtitle stream
                    CurrentSubtitleStream = i;

                    if (selectedAudioLanguage != string.Empty && subtitleCulture.Matches(selectedAudioLanguage))
                    {
                        // the languages of both audio and subtitles are the same so there's no need to show the subtitle
                        // todo: evaluate through user experience
                        EnableSubtitle = false;
                        BDHandlerCore.LogDebug("Disabling subtitles because the audio language is the same.");
                    }
                    else
                    {
                        // enable the subtitles
                        BDHandlerCore.LogInfo("Selected active subtitle track language: {0} ({1})", subtitleCulture.EnglishName, i);
                        EnableSubtitle = true;
                    }
                    break;
                }
            }


            #endregion

            // call the base OnInitialized method
            base.OnInitialized();
        }

        /// <summary>
        /// Scans a bluray folder and returns a BDInfo object
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private BDInfo scanWorker(string path)
        {
            BDHandlerCore.LogInfo("Scanning bluray structure: {0}", path);
            BDInfo bluray = new BDInfo(path.ToUpper());
            bluray.Scan();
            return bluray;
        }

        /// <summary>
        /// Returns wether a choice was made and changes the file path
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns>True if playback should continue, False if user cancelled.</returns>
        private bool doFeatureSelection(ref string filePath)
        {
            try
            {
                Func<string, BDInfo> scanner = scanWorker;
                IAsyncResult result = scanner.BeginInvoke(filePath, null, scanner);

                // Show the wait cursor during scan
                GUIWaitCursor.Init();
                GUIWaitCursor.Show();
                while (result.IsCompleted == false)
                {
                    GUIWindowManager.Process();
                    Thread.Sleep(100);
                }

                BDInfo bluray = scanner.EndInvoke(result);
                List<TSPlaylistFile> allPlayLists = bluray.PlaylistFiles.Values.Where(p => p.IsValid).OrderByDescending(p => p.TotalLength).Distinct().ToList();

                // this will be the title of the dialog, we strip the dialog of weird characters that might wreck the font engine.
                string heading = (bluray.Title != string.Empty) ? Regex.Replace(bluray.Title, @"[^\w\s\*\%\$\+\,\.\-\:\!\?\(\)]", "").Trim() : "Bluray: Select Feature";                

                GUIWaitCursor.Hide();

                // Feature selection logic 
                TSPlaylistFile listToPlay = null;
                if (allPlayLists.Count == 0)
                {
                    BDHandlerCore.LogInfo("No playlists found, bypassing dialog.", allPlayLists.Count);
                    return true;
                } 
                else if (allPlayLists.Count == 1)
                {
                    // if we have only one playlist to show just move on
                    BDHandlerCore.LogInfo("Found one valid playlist, bypassing dialog.", filePath);
                    listToPlay = allPlayLists[0];
                }
                else
                {
                    // Show selection dialog
                    BDHandlerCore.LogInfo("Found {0} playlists, showing selection dialog.", allPlayLists.Count);
                    
                    // first make an educated guess about what the real features are (more than one chapter, no loops and longer than one hour)
                    // todo: make a better filter on the playlists containing the real features
                    List<TSPlaylistFile> playLists = allPlayLists.Where(p => (p.Chapters.Count > 1 || p.TotalLength >= MinimalFullFeatureLength) && !p.HasLoops).ToList();

                    // if the filter yields zero results just list all playlists 
                    if (playLists.Count == 0)
                    {
                        playLists = allPlayLists;
                    }

                    IDialogbox dialog = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
                    while (true)
                    {
                        dialog.Reset();
                        dialog.SetHeading(heading);

                        int count = 1;

                        for (int i = 0; i < playLists.Count; i++)
                        {
                            TSPlaylistFile playList = playLists[i];
                            TimeSpan lengthSpan = new TimeSpan((long)(playList.TotalLength * 10000000));
                            string length = string.Format("{0:D2}:{1:D2}:{2:D2}", lengthSpan.Hours, lengthSpan.Minutes, lengthSpan.Seconds);
                            // todo: translation
                            string feature = string.Format("Feature #{0}, {2} Chapter{3} ({1})", count, length, playList.Chapters.Count, (playList.Chapters.Count > 1) ? "s" : string.Empty);
                            dialog.Add(feature);
                            count++;
                        }

                        if (allPlayLists.Count > playLists.Count)
                        {
                            // todo: translation
                            dialog.Add("List all features...");
                        }

                        dialog.DoModal(GUIWindowManager.ActiveWindow);

                        if (dialog.SelectedId == count)
                        {
                            // don't filter the playlists and continue to display the dialog again
                            playLists = allPlayLists;
                            continue;

                        }
                        else if (dialog.SelectedId < 1)
                        {
                            // user cancelled so we return
                            BDHandlerCore.LogDebug("User cancelled dialog.");
                            return false;
                        }

                        // end dialog
                        break;
                    }

                    listToPlay = playLists[dialog.SelectedId - 1];
                }

                // load the chapters
                chapters = listToPlay.Chapters.ToArray();
                BDHandlerCore.LogDebug("Selected: Playlist={0}, Chapters={1}", listToPlay.Name, chapters.Length);
                
                // create the chosen file path (playlist)
                filePath = Path.Combine(bluray.DirectoryPLAYLIST.FullName, listToPlay.Name);

                #region Refresh Rate Changer

                // Because g_player reads the framerate from the iniating media path we need to
                // do a re-check of the framerate after the user has chosen the playlist. We do
                // this by grabbing the framerate from the first video stream in the playlist as
                // this data was already scanned.
                using (Settings xmlreader = new MPSettings())
                {
                    bool enabled = xmlreader.GetValueAsBool("general", "autochangerefreshrate", false);
                    if (enabled)
                    {
                        TSFrameRate framerate = listToPlay.VideoStreams[0].FrameRate;
                        if (framerate != TSFrameRate.Unknown)
                        {
                            double fps = 0;
                            switch (framerate)
                            {
                                case TSFrameRate.FRAMERATE_59_94:
                                    fps = 59.94;
                                    break;
                                case TSFrameRate.FRAMERATE_50:
                                    fps = 50;
                                    break;
                                case TSFrameRate.FRAMERATE_29_97:
                                    fps = 29.97;
                                    break;
                                case TSFrameRate.FRAMERATE_25:
                                    fps = 25;
                                    break;
                                case TSFrameRate.FRAMERATE_24:
                                    fps = 24;
                                    break;
                                case TSFrameRate.FRAMERATE_23_976:
                                    fps = 23.976;
                                    break;
                            }

                            BDHandlerCore.LogDebug("Initiating refresh rate change: {0}", fps);
                            RefreshRateChanger.SetRefreshRateBasedOnFPS(fps, filePath, RefreshRateChanger.MediaType.Video);
                        }
                    }
                }

                #endregion

                return true;
            }
            catch (Exception e)
            {
                BDHandlerCore.LogError("Exception while reading bluray structure {0} {1}", e.Message, e.StackTrace);
                return true;
            }
        }

        /// <summary>
        /// Renders a graph that can playback mpls files
        /// </summary>
        /// <returns></returns>
        private bool renderGraph()
        {
            try
            {
                graphBuilder = (IGraphBuilder)new FilterGraph();
                _rotEntry = new DsROTEntry((IFilterGraph)graphBuilder);
                List<string> filters = new List<string>();

                BDHandlerCore.LogDebug("Player is active.");

                GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_SWITCH_FULL_WINDOWED, 0, 0, 0, 1, 0, null);
                GUIWindowManager.SendMessage(msg);

                Vmr9 = new VMR9Util();
                Vmr9.AddVMR9(graphBuilder);
                Vmr9.Enable(false);

                // load the source filter                
                IBaseFilter source = DirectShowUtil.AddFilterToGraph(graphBuilder, this.sourceFilter.Name);

                // check if it's available
                if (source == null)
                {
                    Error.SetError("Unable to load source filter", "Please register filter: " + this.sourceFilter.Name);
                    BDHandlerCore.LogError("Unable to load DirectShowFilter: {0}", this.sourceFilter.Name);
                    return false;
                }

                // load the file
                int result = ((IFileSourceFilter)source).Load(CurrentFile, null);
                if (result != 0) return false;

                // add filters and audio renderer
                using (Settings settings = new Settings(Config.GetFile(Config.Dir.Config, "MediaPortal.xml")))
                {
                    // Get the minimal settings required
                    bool useAutoDecoderSettings = settings.GetValueAsBool("movieplayer", "autodecodersettings", false);
                    string filterAudioRenderer = settings.GetValueAsString("movieplayer", "audiorenderer", "Default DirectSound Device");

                    // if "Auto Decoder Settings" is unchecked we add the filters specified in the codec configuration
                    // otherwise the DirectShow merit system is used (except for renderer and source filter)
                    if (!useAutoDecoderSettings)
                    {
                        // Get the Video Codec configuration settings
                        
                        string filterVideoMpeg2 = settings.GetValueAsString("movieplayer", "mpeg2videocodec", "");
                        string filterVideoH264 = settings.GetValueAsString("movieplayer", "h264videocodec", "");
                        string filterAudioMpeg2 = settings.GetValueAsString("movieplayer", "mpeg2audiocodec", "");
                        string filterAudioAAC = settings.GetValueAsString("movieplayer", "aacaudiocodec", "");

                        // Get the custom filters that apply
                        int i = 0;
                        while (true)
                        {
                            string filter = settings.GetValueAsString("movieplayer", string.Format("filter{0}", i), null);
                            if (filter == null)
                            {
                                break;
                            }

                            if (settings.GetValueAsBool("movieplayer", string.Format("usefilter{0}", i), false))
                            {
                                // we found a filter so we add it to the filter collection
                                filters.Add(filter);
                            }

                            i++;
                        }
                        
                        if (!string.IsNullOrEmpty(filterVideoH264))
                            DirectShowUtil.AddFilterToGraph(graphBuilder, filterVideoH264);
                        
                        //if (!string.IsNullOrEmpty(strVideoCodec) && strVideoCodec != strH264VideoCodec)
                        //    DirectShowUtil.AddFilterToGraph(graphBuilder, strVideoCodec);
                        
                        if (!string.IsNullOrEmpty(filterAudioMpeg2))
                            DirectShowUtil.AddFilterToGraph(graphBuilder, filterAudioMpeg2);
                        if (!string.IsNullOrEmpty(filterAudioAAC) && filterAudioMpeg2 != filterAudioAAC)
                            DirectShowUtil.AddFilterToGraph(graphBuilder, filterAudioAAC);
                    }

                    DirectShowUtil.AddAudioRendererToGraph(graphBuilder, filterAudioRenderer, false);

                    // Add custom filter collection to graph 
                    foreach (string filter in filters)
                    {
                        DirectShowUtil.AddFilterToGraph(graphBuilder, filter);
                    }                   

                }

                DirectShowUtil.RenderUnconnectedOutputPins(graphBuilder, source);
                DirectShowUtil.ReleaseComObject(source); source = null;
                DirectShowUtil.RemoveUnusedFiltersFromGraph(graphBuilder);

                SubEngine.GetInstance().LoadSubtitles(graphBuilder, m_strCurrentFile);

                if (Vmr9 == null || !Vmr9.IsVMR9Connected)
                {
                    BDHandlerCore.LogError("Failed to render file.");
                    mediaCtrl = null;
                    Cleanup();
                    return false;
                }

                mediaCtrl = (IMediaControl)graphBuilder;
                mediaEvt = (IMediaEventEx)graphBuilder;
                mediaSeek = (IMediaSeeking)graphBuilder;
                mediaPos = (IMediaPosition)graphBuilder;
                basicAudio = (IBasicAudio)graphBuilder;
                videoWin = (IVideoWindow)graphBuilder;
                m_iVideoWidth = Vmr9.VideoWidth;
                m_iVideoHeight = Vmr9.VideoHeight;
                Vmr9.SetDeinterlaceMode();

                return true;
            }
            catch (Exception e)
            {
                Error.SetError("Unable to play movie", "Unable build graph for VMR9");
                BDHandlerCore.LogError("Exception while creating DShow graph {0} {1}", e.Message, e.StackTrace);
                Cleanup();
                return false;
            }
        }

        #region Utility

        /// <summary>
        /// Gets the filter GUID by stream info.
        /// </summary>
        /// <param name="info">FilterStreamInfos struct</param>
        /// <returns></returns>
        protected virtual Guid GetFilterGuidByStreamInfo(FilterStreamInfos info) 
        {
            Guid guid = Guid.Empty;

            IBaseFilter foundfilter = DirectShowUtil.GetFilterByName(graphBuilder, info.Filter);
            if (foundfilter != null)
            {
                foundfilter.GetClassID(out guid);

                // release object
                DirectShowUtil.ReleaseComObject(foundfilter);
            }

            return guid;
        }

        /// <summary>
        /// Gets the culture info from configurationsettings.
        /// </summary>
        /// <param name="section">The section name.</param>
        /// <param name="entry">The entry name.</param>
        /// <returns></returns>
        public virtual CultureInfo GetCultureInfoFromSettings(string section, string entry)
        {
            CultureInfo culture = null;
            using (Settings xmlreader = new MPSettings())
            {
                try
                {
                    culture = CultureInfo.GetCultureInfo(xmlreader.GetValueAsString(section, entry, defaultLanguageCulture));
                }
                catch (Exception ex)
                {
                    culture = CultureInfo.GetCultureInfo(defaultLanguageCulture);
                    BDHandlerCore.LogError("unable to build CultureInfo - {0}", ex);
                }
            }

            return culture;
        }

        /// <summary>
        /// Gets the audio language from the stream
        /// </summary>
        /// <param name="iStream">stream index</param>
        /// <returns>language string formatted as english name</returns>
        public virtual string GetAudioLanguageFromStream(int iStream)
        {
            FilterStreamInfos info = FStreams.GetStreamInfos(StreamType.Audio, iStream);

            if (info.LCID > 0)
            {
                string lang = GetEnglishNameByLCID(info.LCID);
                BDHandlerCore.LogDebug("AudioLanguage() LCID: {0}, OUT: {1}", info.LCID, lang);
            }

            Guid guid = GetFilterGuidByStreamInfo(info);
            if (guid == this.sourceFilter.GUID)
            {
                // todo: filter dictionary lookup
                ISelectFilter filter = this.sourceFilter as ISelectFilter;
                if (filter != null)
                {
                    string language = filter.ParseAudioLanguage(info.Name);
                    BDHandlerCore.LogDebug("AudioLanguage() Filter: {0}, IN: {1} OUT: {2}", filter.Name, info.Name, language);
                    return language;
                }
            }
            else
            {
                BDHandlerCore.LogDebug("AudioLanguage() Filter: Unknown, GUID={0}", guid.ToString());
            }

            // if we made it this far just do the native dance
            return base.AudioLanguage(iStream);
        }

        /// <summary>
        /// Gets the subtitle language from the stream
        /// </summary>
        /// <param name="iStream"></param>
        /// <returns>language string formatted as english name</returns>
        public virtual string GetSubtitleLanguageFromStream(int iStream)
        {
            FilterStreamInfos info = FStreams.GetStreamInfos(StreamType.Subtitle, iStream);

            if (info.LCID > 0)
            {
                return GetEnglishNameByLCID(info.LCID);
            }

            Guid guid = GetFilterGuidByStreamInfo(info);
            if (guid == this.sourceFilter.GUID)
            {
                // todo: filter dictionary lookup
                ISelectFilter filter = this.sourceFilter as ISelectFilter;
                if (filter != null)
                {
                    string language = filter.ParseSubtitleLanguage(info.Name);
                    BDHandlerCore.LogDebug("SubtitleLanguage() Filter: {0}, IN: {1} OUT: {2}", filter.Name, info.Name, language);
                    return language;
                }
            }
            else
            {
                BDHandlerCore.LogDebug("SubtitleLanguage() Filter: Unknown, GUID={0}", guid.ToString());
            }

            return base.SubtitleLanguage(iStream);
        }

        /// <summary>
        /// Gets the subtitle name from the stream
        /// </summary>
        /// <param name="iStream"></param>
        /// <returns></returns>
        public virtual string GetSubtitleNameFromStream(int iStream)
        {
            FilterStreamInfos info = FStreams.GetStreamInfos(StreamType.Subtitle, iStream);

            Guid guid = GetFilterGuidByStreamInfo(info);
            if (guid == this.sourceFilter.GUID)
            {
                ISelectFilter filter = this.sourceFilter as ISelectFilter;
                if (filter != null)
                {
                    string name = filter.ParseSubtitleName(info.Name);
                    BDHandlerCore.LogDebug("SubtitleName() Filter: {0}, IN: {1} OUT: {2}", filter.Name, info.Name, name);
                    return name;
                }
            }
            else
            {
                BDHandlerCore.LogDebug("SubtitleName() Filter: Unknown, GUID={0}", guid.ToString());
            }

            return base.SubtitleLanguage(iStream);
        }

        /// <summary>
        /// Gets the english name by LCID.
        /// </summary>
        /// <param name="lcid">The lcid.</param>
        /// <returns></returns>
        public static string GetEnglishNameByLCID(int lcid)
        {
            return CultureInfo.GetCultureInfo(lcid).EnglishName.Split('(')[0].Trim();
        }

        #endregion
    }
}
