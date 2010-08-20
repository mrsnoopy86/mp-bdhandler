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

namespace MediaPortal.Plugins.BDHandler
{

    public class BDPlayer : VideoPlayerVMR9
    {

        // "MPC - Mpeg Source (Gabest)
        public static Guid MpcMpegSourceFilter
        {
            get
            {
                return new Guid("{1365BE7A-C86A-473C-9A41-C0A6E82C9FA3}");
            }
        }

        public static string MpcMegSourceFilterName = "MPC - Mpeg Source (Gabest)";
        public static double MinimalFullFeatureLength = 3000;

        public BDPlayer() : base(g_Player.MediaType.Video) { }

        public BDPlayer(g_Player.MediaType type)
            : base(type)
        { }

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
                if (doFeatureSelection(ref strFile) || path.EndsWith(".m2ts"))
                {
                    return base.Play(strFile);
                }
            }

            // if we get here we always return true because the user called the dialog and we don't 
            // want an error saying we couldn't play the file
            return true;
        }

        /// <summary>
        /// Gets the audio language from the stream (mpeg source splitter safe!)
        /// </summary>
        /// <param name="iStream"></param>
        /// <returns></returns>
        public override string AudioLanguage(int iStream)
        {
            // first let the native method do it's work
            string language = base.AudioLanguage(iStream);

            // in case it failed do a double check for the mpeg source splitter format
            language = Regex.Replace(language, @"^audio - ([^,]+),.+$", "$1", RegexOptions.IgnoreCase);

            return language;
        }

        public override string AudioType(int iStream)
        {
            string type = base.AudioType(iStream);
            type = Regex.Replace(type, @"^audio - [^,]+,(.+)$", "$1", RegexOptions.IgnoreCase);

            return type;
        }

        /// <summary>
        /// Gets the subtitle language from the stream (mpeg source splitter safe!)
        /// </summary>
        /// <param name="iStream"></param>
        /// <returns></returns>
        public override string SubtitleLanguage(int iStream)
        {
            // first let the native method do it's work
            string language = base.SubtitleLanguage(iStream);

            // in case it failed do a double check for the mpeg source splitter format
            language = Regex.Replace(language, @"^subtitle - ([^,]+),.+$", "$1", RegexOptions.IgnoreCase);

            return language;
        }

        /// <summary>
        /// This method acts like the AnalyseStreams() method within VideoPlayerVMR7.cs in MediaPortal 1.1.0 
        /// but the contents is exactly the same except for the fact that it only takes 
        /// filters into account that have multiple streams (and are able to actually switch them)
        /// this exception is marked with a region and was supplied in a patch on the forums.
        /// Unfortunately this was the only way of overriding it.
        /// </summary>
        /// <returns></returns>
        public bool ReanalyseStreams()
        {
            BDHandlerCore.LogDebug("Re-analysing streams to filter duplicates...");                                   
            try
            {
                if (FStreams == null)
                {
                    FStreams = new FilterStreams();
                }
                FStreams.DeleteAllStreams();
                string filter;
                IBaseFilter[] foundfilter = new IBaseFilter[2];
                int fetched = 0;
                IEnumFilters enumFilters;
                graphBuilder.EnumFilters(out enumFilters);
                if (enumFilters != null)
                {
                    enumFilters.Reset();
                    while (enumFilters.Next(1, foundfilter, out fetched) == 0)
                    {
                        if (foundfilter[0] != null && fetched == 1)
                        {

                            IAMExtendedSeeking pEs = foundfilter[0] as IAMExtendedSeeking;
                            if (pEs != null)
                            {
                                int markerCount = 0;
                                if (pEs.get_MarkerCount(out markerCount) == 0 && markerCount > 0)
                                {
                                    chapters = new double[markerCount];
                                    for (int i = 1; i <= markerCount; i++)
                                    {
                                        double markerTime = 0;
                                        pEs.GetMarkerTime(i, out markerTime);
                                        chapters[i - 1] = markerTime;
                                    }
                                }
                            }

                            IAMStreamSelect pStrm = foundfilter[0] as IAMStreamSelect;
                            if (pStrm != null)
                            {
                                FilterInfo foundfilterinfos = new FilterInfo();
                                foundfilter[0].QueryFilterInfo(out foundfilterinfos);
                                filter = foundfilterinfos.achName;
                                int cStreams = 0;
                                pStrm.Count(out cStreams);

                                #region new logic for VMR7

                                if (cStreams < 2)
                                {
                                    BDHandlerCore.LogDebug("Skipping one stream in {0}", filter);    
                                    continue;
                                }

                                #endregion

                                //GET STREAMS
                                for (int istream = 0; istream < cStreams; istream++)
                                {
                                    AMMediaType sType;
                                    AMStreamSelectInfoFlags sFlag;
                                    int sPDWGroup, sPLCid;
                                    string sName;
                                    object pppunk, ppobject;
                                    //STREAM INFO
                                    pStrm.Info(istream, out sType, out sFlag, out sPLCid,
                                               out sPDWGroup, out sName, out pppunk, out ppobject);
                                    FilterStreamInfos FSInfos = new FilterStreamInfos();
                                    FSInfos.Current = false;
                                    FSInfos.Filter = filter;
                                    FSInfos.Name = sName;
                                    FSInfos.Id = istream;
                                    FSInfos.Type = StreamType.Unknown;
                                    //Avoid listing ffdshow video filter's plugins amongst subtitle and audio streams.
                                    if ((FSInfos.Filter == "ffdshow Video Decoder" || FSInfos.Filter == "ffdshow raw video filter") &&
                                        ((sPDWGroup == 1) || (sPDWGroup == 2)))
                                    {
                                        FSInfos.Type = StreamType.Unknown;
                                    }
                                    //VIDEO
                                    else if (sPDWGroup == 0)
                                    {
                                        FSInfos.Type = StreamType.Video;
                                    }
                                    //AUDIO
                                    else if (sPDWGroup == 1)
                                    {
                                        FSInfos.Type = StreamType.Audio;
                                    }
                                    //SUBTITLE
                                    else if (sPDWGroup == 2 && sName.LastIndexOf("off") == -1 && sName.LastIndexOf("Hide ") == -1 &&
                                             sName.LastIndexOf("No ") == -1 && sName.LastIndexOf("Miscellaneous ") == -1)
                                    {
                                        FSInfos.Type = StreamType.Subtitle;
                                    }
                                    //NO SUBTITILE TAG
                                    else if ((sPDWGroup == 2 && (sName.LastIndexOf("off") != -1 || sName.LastIndexOf("No ") != -1)) ||
                                             (sPDWGroup == 6590033 && sName.LastIndexOf("Hide ") != -1))
                                    {
                                        FSInfos.Type = StreamType.Subtitle_hidden;
                                    }
                                    //DirectVobSub SHOW SUBTITLE TAG
                                    else if (sPDWGroup == 6590033 && sName.LastIndexOf("Show ") != -1)
                                    {
                                        FSInfos.Type = StreamType.Subtitle_shown;
                                    }
                                    Log.Debug("VideoPlayer: FoundStreams: Type={0}; Name={1}, Filter={2}, Id={3}, PDWGroup={4}",
                                              FSInfos.Type.ToString(), FSInfos.Name, FSInfos.Filter, FSInfos.Id.ToString(),
                                              sPDWGroup.ToString());

                                    switch (FSInfos.Type)
                                    {
                                        case StreamType.Unknown:
                                            break;
                                        case StreamType.Video:
                                        case StreamType.Audio:
                                        case StreamType.Subtitle:
                                            if (FStreams.GetStreamCount(FSInfos.Type) == 0)
                                            {
                                                FSInfos.Current = true;
                                                pStrm.Enable(FSInfos.Id, 0);
                                                pStrm.Enable(FSInfos.Id, AMStreamSelectEnableFlags.Enable);
                                            }
                                            goto default;
                                        default:
                                            FStreams.AddStreamInfos(FSInfos);
                                            break;
                                    }
                                }
                            }
                            DirectShowUtil.ReleaseComObject(foundfilter[0]);
                        }
                    }
                    DirectShowUtil.ReleaseComObject(enumFilters);
                }
            }
            catch { }
            return true;
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

            // Reanalyse streams to filter false ones
            ReanalyseStreams();

            #region Improved Subtitle & Audio Selection

            // Recheck the audio WITHOUT translating the CultureInfo
            // this is an additional check that should be put into 
            // the VideoPlayerVMR7.cs (line 371) but as long as we are overriding
            // here we can fix it ourselves

            // if there is only one audiostream there's no need to do this extra logic
            if (AudioStreams > 1)
            {
                CultureInfo ci = null;
                using (Settings xmlreader = new MPSettings())
                {
                    try
                    {
                        ci = new CultureInfo(xmlreader.GetValueAsString("movieplayer", "audiolanguage", defaultLanguageCulture));
                    }
                    catch (Exception ex)
                    {
                        ci = new CultureInfo(defaultLanguageCulture);
                        BDHandlerCore.LogError("unable to build CultureInfo - {0}", ex);
                    }
                }

                for (int i = 0; i < AudioStreams; i++)
                {
                    string language = AudioLanguage(i);
                    if (ci.EnglishName.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                        ci.TwoLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                        ci.ThreeLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                        ci.ThreeLetterWindowsLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentAudioStream = i;
                        BDHandlerCore.LogInfo("Selected active audio track language: {0} ({1})", ci.EnglishName, i);
                        break;
                    }
                }
            }

            // if there is only one subtitle stream there's no need to do this extra logic
            if (SubtitleStreams > 1)
            {
                CultureInfo ci = null;
                using (Settings xmlreader = new MPSettings())
                {
                    try
                    {
                        ci = new CultureInfo(xmlreader.GetValueAsString("subtitles", "language", defaultLanguageCulture));
                    }
                    catch (Exception ex)
                    {
                        ci = new CultureInfo(defaultLanguageCulture);
                        BDHandlerCore.LogError("unable to build CultureInfo - {0}", ex);
                    }
                }

                for (int i = 0; i < SubtitleStreams; i++)
                {
                    string subtitleLanguage = SubtitleLanguage(i);
                    if (ci.EnglishName.Equals(subtitleLanguage, StringComparison.OrdinalIgnoreCase) ||
                        ci.TwoLetterISOLanguageName.Equals(subtitleLanguage, StringComparison.OrdinalIgnoreCase) ||
                        ci.ThreeLetterISOLanguageName.Equals(subtitleLanguage, StringComparison.OrdinalIgnoreCase) ||
                        ci.ThreeLetterWindowsLanguageName.Equals(subtitleLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        BDHandlerCore.LogInfo("Selected active subtitle track language: {0} ({1})", ci.EnglishName, i);
                        CurrentSubtitleStream = i;
                        break;
                    }
                }
            }

            // now check whether we should show the subtitles
            if (EnableSubtitle && SubtitleStreams > 0 && AudioStreams > 0)
            {
                if (SubtitleLanguage(CurrentSubtitleStream).Equals(AudioLanguage(CurrentAudioStream), StringComparison.OrdinalIgnoreCase))
                {
                    // the languages of both audio and subtitles are the same so there's no need to show the subtitle
                    EnableSubtitle = false;

                    BDHandlerCore.LogDebug("Disabling subtitles because the audio language is the same.");
                }
            }

            #endregion

            // call the base OnInitialized method
            base.OnInitialized();
        }

        delegate BDInfo ScanProcess(string path);

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
                ScanProcess scanner = new ScanProcess(scanWorker);
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

                // todo: make a better filter on the playlists containing the real features

                if (allPlayLists.Count == 0)
                {
                    BDHandlerCore.LogInfo("No playlists found, bypassing dialog.", allPlayLists.Count);
                    return true;
                }

                if (allPlayLists.Count == 1)
                {
                    filePath = Path.Combine(bluray.DirectoryPLAYLIST.FullName, allPlayLists[0].Name);
                    BDHandlerCore.LogInfo("Found one valid playlist, bypassing dialog.", filePath);
                    return true;
                }

                BDHandlerCore.LogInfo("Found {0} valid playlists, showing selection dialog.", allPlayLists.Count);

                // first make an educated guess about what the real features are (more than one chapter, no loops and longer than one hour)
                List<TSPlaylistFile> playLists = allPlayLists.Where(p => (p.Chapters.Count > 1 || p.TotalLength >= MinimalFullFeatureLength) && !p.HasLoops).ToList();

                // immediately show all features if the above filter yields zero results
                if (playLists.Count == 0)
                {
                    playLists = allPlayLists;
                }

                bool listMore = (allPlayLists.Count > playLists.Count);
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

                    if (listMore)
                    {
                        // todo: translation
                        dialog.Add("List all features...");
                    }

                    dialog.DoModal(GUIWindowManager.ActiveWindow);

                    if (dialog.SelectedId == count)
                    {
                        // don't filter the playlists and continue to display the dialog again
                        playLists = allPlayLists;
                        listMore = false;
                        continue;

                    } else if (dialog.SelectedId < 1)
                    {
                        // user cancelled so we terug
                        BDHandlerCore.LogDebug("User cancelled dialog.");
                        return false;
                    }

                    // end dialog
                    break;
                }

                TSPlaylistFile listToPlay = playLists[dialog.SelectedId - 1];

                // load the chapters
                chapters = listToPlay.Chapters.ToArray();
                BDHandlerCore.LogDebug("User Selection: Playlist={0}, Chapters={1}", listToPlay.Name, chapters.Length);
                
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
                IBaseFilter source = DirectShowUtil.AddFilterToGraph(graphBuilder, MpcMegSourceFilterName);

                // check if it's available
                if (source == null)
                {
                    Error.SetError("Unable to load source filter", "Please register filter: " + MpcMegSourceFilterName);
                    BDHandlerCore.LogError("Unable to load DirectShowFilter: {0}", MpcMegSourceFilterName);
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

    }
}
