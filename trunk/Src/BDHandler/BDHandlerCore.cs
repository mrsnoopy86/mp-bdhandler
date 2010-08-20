using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using MediaPortal.GUI.Library;
using MediaPortal.Player;
using MediaPortal.Profile;
using Microsoft.Win32;

namespace MediaPortal.Plugins.BDHandler 
{

    public class BDHandlerCore {

        static BDHandlerCore() {}

        private static FactoryWrapper _factory;
        private static string logPrefix = "BDHandler:";

        public static bool Init() {
            try {
                RegistryKey key = Registry.ClassesRoot.OpenSubKey(@"CLSID\{" + BDPlayer.MpcMpegSourceFilter.ToString() + @"}\InprocServer32", RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey);
                if (key != null) {
                    string codecFile = key.GetValue("", null).ToString();
                    if (!Path.IsPathRooted(codecFile)) {
                        string systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
                        codecFile = Path.Combine(systemPath, codecFile);
                    }
                    if (!File.Exists(codecFile))
                        return false;

                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(codecFile);
                    LogInfo("Detected '{0}' ({1}.{2}.{3}.{4})", BDPlayer.MpcMegSourceFilterName, info.ProductMajorPart, info.ProductMinorPart, info.ProductBuildPart, info.ProductPrivatePart);
                    return (info.ProductBuildPart >= 1287);
                }
                else {
                    LogInfo("'{0}' was not detected on the system.", BDPlayer.MpcMegSourceFilterName);
                    return false;
                }
            }
            catch (Exception) {
                LogWarn("Source filter detection failed.");
                return true;
            }
        }

        public static bool Enabled {
            get {
                return _enabled;
            }
            set {
                if (value && !_enabled) {
                    if (_factory == null)
                        _factory = new FactoryWrapper(g_Player.Factory);
                    g_Player.Factory = _factory;
                }
                else if (!value && _enabled) {
                    g_Player.Factory = _factory.GetDefaultFactory();
                }
                _enabled = value;
            }
        } static bool _enabled = false;

        public static void PlayDisc(string device) {
            bool play = true;
            using (Settings xmlreader = new MPSettings()) {
                string autoPlay = xmlreader.GetValueAsString("dvdplayer", "autoplay", "Ask");
                if (autoPlay == "No")
                    return;

                if (autoPlay == "Ask") {
                    GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_ASKYESNO, 0, 0, 0, 0, 0, null);
                    msg.Param1 = 713;
                    msg.Param2 = 531;

                    GUIWindowManager.SendMessage(msg);
                    play = msg.Param1 != 0;
                }
            }

            if (play && g_Player.Play(device) && g_Player.Playing)
                g_Player.ShowFullScreenWindow();
        }

        #region Logging

        public static void LogInfo(string format, params object[] args)
        {
            string logText = string.Format("{0} {1}", logPrefix, format);
            Log.Info(logText, args);
        }

        public static void LogWarn(string format, params object[] args)
        {
            string logText = string.Format("{0} {1}", logPrefix, format);
            Log.Warn(logText, args);
        }

        public static void LogDebug(string format, params object[] args)
        {
            string logText = string.Format("{0} {1}", logPrefix, format);
            Log.Debug(logText, args);
        }

        public static void LogError(string format, params object[] args)
        {
            string logText = string.Format("{0} {1}", logPrefix, format);
            Log.Error(logText, args);
        }

        #endregion

    }
}
