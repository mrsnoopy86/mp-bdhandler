using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using MediaPortal.Configuration;
using MediaPortal.GUI.Library;
using MediaPortal.Player;
using MediaPortal.Ripper;
using MediaPortal.Profile;

namespace MediaPortal.Plugins.BDHandler {

    [PluginIcons("MediaPortal.Plugins.BDHandler.Resources.BDHandler.png", "MediaPortal.Plugins.BDHandler.Resources.BDHandlerDisabled.png")]
    public class BDHandlerPlugin : IPlugin, ISetupForm {

        public static string LogPrefix = "[BDHandler] ";

        private FactoryWrapper _factory;

        public BDHandlerPlugin() {
            Assembly assy = Assembly.GetExecutingAssembly();
            foreach (Attribute attr in Attribute.GetCustomAttributes(assy)) {
                if (attr.GetType() == typeof(AssemblyTitleAttribute))
                    _pluginName = ((AssemblyTitleAttribute)attr).Title;
                else if (attr.GetType() == typeof(AssemblyDescriptionAttribute))
                    _pluginDesc = ((AssemblyDescriptionAttribute)attr).Description;
                else if (attr.GetType() == typeof(AssemblyCompanyAttribute))
                    _pluginAuthor = ((AssemblyCompanyAttribute)attr).Company;
            }
        }

        private void OnMessage(GUIMessage message) {
            if (message.Message != GUIMessage.MessageType.GUI_MSG_BLURAY_DISK_INSERTED || g_Player.Playing)
                return;

            playDisc(message.Label);
        }

        private void playDisc(string device) {
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

        #region IPlugin Members

        public void Start() {
            _factory = new FactoryWrapper(g_Player.Factory);
            g_Player.Factory = _factory;

            GUIWindowManager.Receivers += new SendMessageHandler(this.OnMessage);

            Log.Info(LogPrefix + " Enabled.");
        }

        public void Stop() {
            g_Player.Factory = _factory.GetDefaultFactory();
            Log.Info(LogPrefix + " Disabled.");
        }

        #endregion

        #region ISetupForm Members

        public string PluginName() {
            return _pluginName;
        } private string _pluginName;

        public string Description() {
            return _pluginDesc; 
        } private string _pluginDesc;

        public string Author() {
            return _pluginAuthor;
        }  private string _pluginAuthor;

        public void ShowPlugin() {
            return;
        }

        public bool CanEnable() {
            return true;
        }

        public int GetWindowId() {
            return 0;
        }

        public bool DefaultEnabled() {
            return true;
        }

        public bool HasSetup() {
            return false;
        }

        public bool GetHome(out string strButtonText, out string strButtonImage, out string strButtonImageFocus, out string strPictureImage) {
            strButtonText = string.Empty;
            strButtonImage = string.Empty;
            strButtonImageFocus = string.Empty;
            strPictureImage = string.Empty;
            return false;
        }

        #endregion
    }

}
