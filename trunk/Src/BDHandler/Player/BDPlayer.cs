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

namespace MediaPortal.Plugins.BDHandler {

    public class BDPlayer : VideoPlayerVMR9
    {

        // "MPC - Mpeg Source (Gabest)
        public static Guid MpcMpegSourceFilter {
            get {
                return new Guid("{1365BE7A-C86A-473C-9A41-C0A6E82C9FA3}");
            }
        }

        public static string MpcMegSourceFilterName = "MPC - Mpeg Source (Gabest)";

        public BDPlayer() : base(g_Player.MediaType.Video) { }

        public BDPlayer(g_Player.MediaType type) : base(type)
        { }

        public override bool Play(string strFile) {
            string path = strFile.ToLower();

            if (strFile.Length < 4) {
                path = Path.Combine(strFile, @"BDMV\index.bdmv");
                strFile = path;
            }

            if (path.EndsWith(".bdmv") || path.EndsWith(".m2ts"))
                strFile = doFeatureSelection(strFile);

            return base.Play(strFile);
        }

        protected override bool GetInterfaces() {
            if (CurrentFile.ToLower().EndsWith(".mpls"))
                return renderGraph();

            return base.GetInterfaces();
        }
        
        delegate BDROM ScanProcess(string path);
 
        private BDROM scanWorker(string path) {
            Log.Info(BDHandlerCore.LogPrefix + "Scanning bluray structure: {0}", path);
            BDROM bluray = new BDROM(path.ToUpper());
            bluray.Scan();
            return bluray;            
        }

        private string doFeatureSelection(string path) {
            try {

                ScanProcess scanner = new ScanProcess(scanWorker);
                IAsyncResult result = scanner.BeginInvoke(path, null, scanner);

                // Show the wait cursor during scan
                GUIWaitCursor.Init();
                GUIWaitCursor.Show(); 
                while(result.IsCompleted == false) {
                    GUIWindowManager.Process();
                    Thread.Sleep(100);
                }

                BDROM bluray = scanner.EndInvoke(result);
                List<TSPlaylistFile> playLists = bluray.PlaylistFiles.Values.Where(p => p.IsValid).OrderByDescending(p => p.TotalLength).ToList();

                GUIWaitCursor.Hide();

                if (playLists.Count == 0)
                    return path;

                if (playLists.Count == 1)
                    return Path.Combine(bluray.DirectoryPLAYLIST.FullName, playLists[0].Name);

                IDialogbox dialog = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
                dialog.Reset();

                for (int i = 0; i < playLists.Count; i++) {
                    TSPlaylistFile playList = playLists[i];
                    TimeSpan lengthSpan = new TimeSpan((long)(playList.TotalLength * 10000000));
                    string length = string.Format("{0:D2}:{1:D2}:{2:D2}", lengthSpan.Hours, lengthSpan.Minutes, lengthSpan.Seconds);
                    string feature = string.Format("Feature #{0} ({1})", (i + 1), length);
                    dialog.Add(feature);
                }

                dialog.SetHeading("Bluray: Select Feature");
                dialog.DoModal(GUIWindowManager.ActiveWindow);
                if (dialog.SelectedId < 1)
                    return path;

                return Path.Combine(bluray.DirectoryPLAYLIST.FullName, playLists[dialog.SelectedId - 1].Name);
            }
            catch (Exception e) {
                Log.Error(BDHandlerCore.LogPrefix + "Exception while reading bluray structure {0} {1}", e.Message, e.StackTrace);
                return path;
            }
        }

        private bool renderGraph() {
            try {
                graphBuilder = (IGraphBuilder)new FilterGraph();
                _rotEntry = new DsROTEntry((IFilterGraph)graphBuilder);

                Log.Info("BDPlayer: Active");
                GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_SWITCH_FULL_WINDOWED, 0, 0, 0, 1, 0, null);
                GUIWindowManager.SendMessage(msg);

                Vmr9 = new VMR9Util();
                Vmr9.AddVMR9(graphBuilder);
                Vmr9.Enable(false);

                // load the source filter                
                IBaseFilter source = DirectShowUtil.AddFilterToGraph(graphBuilder, MpcMegSourceFilterName);

                // check if it's avaiable
                if (source == null) {
                    Error.SetError("Unable to load source filter", "Please register filter: " + MpcMegSourceFilterName);
                    Log.Error(BDHandlerCore.LogPrefix + "Unable to load DirectShowFilter: " + MpcMegSourceFilterName, null);
                    return false;
                }

                // load the file
                int result = ((IFileSourceFilter)source).Load(CurrentFile, null);
                if (result != 0) return false;
               
                // add filters and audio renderer
                using (Settings settings = new Settings(Config.GetFile(Config.Dir.Config, "MediaPortal.xml"))) {

                    // Get the Video Codec configuration settings
                    bool bAutoDecoderSettings = settings.GetValueAsBool("movieplayer", "autodecodersettings", false);
                    string strVideoCodec = settings.GetValueAsString("movieplayer", "mpeg2videocodec", "");
                    string strH264VideoCodec = settings.GetValueAsString("movieplayer", "h264videocodec", "");
                    string strAudioCodec = settings.GetValueAsString("movieplayer", "mpeg2audiocodec", "");
                    string strAACAudioCodec = settings.GetValueAsString("movieplayer", "aacaudiocodec", "");
                    string strAudiorenderer = settings.GetValueAsString("movieplayer", "audiorenderer", "Default DirectSound Device");

                    // todo: custom filters from the post-processing tab?

                    // if "Auto Decoder Settings" is unchecked we add the filters specified in the codec configuration
                    // otherwise the DirectShow merit system is used (except for renderer and source filter)
                    if (!bAutoDecoderSettings) {
                        if (!string.IsNullOrEmpty(strH264VideoCodec))
                            DirectShowUtil.AddFilterToGraph(graphBuilder, strH264VideoCodec);
                        if (!string.IsNullOrEmpty(strVideoCodec) && strVideoCodec != strH264VideoCodec)
                            DirectShowUtil.AddFilterToGraph(graphBuilder, strVideoCodec);
                        if (!string.IsNullOrEmpty(strAudioCodec))
                            DirectShowUtil.AddFilterToGraph(graphBuilder, strAudioCodec);
                        if (!string.IsNullOrEmpty(strAACAudioCodec) && strAudioCodec != strAACAudioCodec)
                            DirectShowUtil.AddFilterToGraph(graphBuilder, strAACAudioCodec);
                    }

                    DirectShowUtil.AddAudioRendererToGraph(graphBuilder, strAudiorenderer, false);
                }

                DirectShowUtil.RenderUnconnectedOutputPins(graphBuilder, source);
                DirectShowUtil.ReleaseComObject(source); source = null;
                DirectShowUtil.RemoveUnusedFiltersFromGraph(graphBuilder);
                
                SubEngine.GetInstance().LoadSubtitles(graphBuilder, m_strCurrentFile);

                if (Vmr9 == null || !Vmr9.IsVMR9Connected) {
                    Log.Error(BDHandlerCore.LogPrefix + "Failed to render file.");
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
            catch (Exception e) {
                Error.SetError("Unable to play movie", "Unable build graph for VMR9");
                Log.Error(BDHandlerCore.LogPrefix + "Exception while creating DShow graph {0} {1}", e.Message, e.StackTrace);
                Cleanup();
                return false;
            }
        }

    }
}
